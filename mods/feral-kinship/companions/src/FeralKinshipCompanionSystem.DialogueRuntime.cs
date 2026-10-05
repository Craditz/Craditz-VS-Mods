#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

internal sealed class CompanionConversationDocument
{
    public int SchemaVersion { get; set; }
    public List<CompanionConversationExchange> Exchanges { get; set; } = new();
}

internal sealed class CompanionConversationExchange
{
    public string Id { get; set; } = string.Empty;
    public int Tier { get; set; }
    public string Context { get; set; } = "idle";
    public string PersonalityA { get; set; } = "*";
    public string PersonalityB { get; set; } = "*";
    public string RoleA { get; set; } = "Generic";
    public string RoleB { get; set; } = "Generic";
    public string Relationship { get; set; } = string.Empty;
    public List<CompanionConversationLine> Lines { get; set; } = new();
}

internal sealed class CompanionConversationLine
{
    public string Speaker { get; set; } = "A";
    public string Role { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public sealed partial class FeralKinshipCompanionSystem
{
    private const string DialogueMilestonesKey = "feralKinshipDialogueMilestones";
    private const string DialoguePendingMilestonesKey = "feralKinshipDialoguePendingMilestones";
    private const string DialogueRewardedMilestonesKey = "feralKinshipDialogueRewardedMilestones";
    private const long ConversationPairCooldownMs = 15 * 60 * 1000;
    private const long ConversationCompanionCooldownMs = 10 * 60 * 1000;
    private const long ConversationOwnerMinimumMs = 2 * 60 * 1000;
    private const long ConversationOwnerMaximumMs = 4 * 60 * 1000;
    private const long ConversationLineDelayMs = 2300;
    private static readonly PropertyInfo? GrowSpawnedTotalHoursProperty = typeof(EntityBehaviorGrow)
        .GetProperty("SpawnedTotalHours", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private readonly List<CompanionConversationExchange> conversationExchanges = new();
    private readonly Dictionary<string, ActiveCompanionConversation> activeConversationByOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> conversationPairCooldowns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> conversationCompanionCooldowns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> conversationExchangeCooldowns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> lastConversationContextByOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> nextConversationByOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<long, Dictionary<string, long>> triggeredConversationContexts = new();
    private readonly Dictionary<long, long> conversationPriorityBlocks = new();
    private readonly Dictionary<long, long> nextDialogueStateScan = new();
    private readonly Dictionary<long, string> lastDialogueWeather = new();
    private readonly Dictionary<long, long> dialogueWeatherChangedAt = new();
    private readonly Dictionary<long, string> spontaneousMoodEpisode = new();
    private readonly Dictionary<long, long> spontaneousMoodSince = new();
    private readonly Dictionary<long, bool> dialogueCombatState = new();
    private readonly Dictionary<long, bool> dialogueSleepingState = new();
    private readonly Dictionary<long, long> nextNightDialogue = new();
    private readonly Dictionary<long, long> nextRecoveryDialogue = new();
    private long nextDialogueMaintenanceUtcMs;

    private sealed class ActiveCompanionConversation
    {
        public long Token;
        public string OwnerUid = string.Empty;
        public Entity A = null!;
        public Entity B = null!;
        public CompanionConversationExchange Exchange = null!;
        public int NextLine;
    }

    private void LoadDialogueRuntime(ICoreAPI api)
    {
        nextDialogueMaintenanceUtcMs = 0;
        conversationExchanges.Clear();
        try
        {
            IAsset? asset = api.Assets.TryGet(new AssetLocation(
                "feralkinshipcompanions", "config/dialogue/dialogue-conversations.json"));
            CompanionConversationDocument? document = asset?.ToObject<CompanionConversationDocument>();
            foreach (CompanionConversationExchange exchange in document?.Exchanges ?? new())
            {
                if (!string.IsNullOrWhiteSpace(exchange.Id)
                    && exchange.Tier is >= 1 and <= 4
                    && exchange.Lines.Count >= 2
                    && exchange.Lines.All(line => !string.IsNullOrWhiteSpace(line.Text)))
                {
                    conversationExchanges.Add(exchange);
                }
            }
            api.Logger.Notification(
                "[FeralKinshipCompanions] Conversation catalog loaded: {0} complete exchanges (T1 {1}, T2 {2}, T3 {3}, T4 {4}).",
                conversationExchanges.Count,
                conversationExchanges.Count(exchange => exchange.Tier == 1),
                conversationExchanges.Count(exchange => exchange.Tier == 2),
                conversationExchanges.Count(exchange => exchange.Tier == 3),
                conversationExchanges.Count(exchange => exchange.Tier == 4));
        }
        catch (Exception exception)
        {
            api.Logger.Error(
                "[FeralKinshipCompanions] Conversation catalog could not be loaded; conversations are disabled. {0}",
                exception.Message);
        }
    }

    private void MaintainDialogueRuntime(long nowUtcMs)
    {
        if (nowUtcMs < nextDialogueMaintenanceUtcMs) return;
        nextDialogueMaintenanceUtcMs = nowUtcMs + 1000;
        MaintainFollowWaitingWindows(nowUtcMs);
        if (StfuModeEnabled || serverApi == null || dialogueService == null) return;

        foreach (Entity companion in loadedFoxes.Values.ToArray())
        {
            if (!IsDialogueCompanionPresent(companion)) continue;
            if (nextDialogueStateScan.GetValueOrDefault(companion.EntityId) > nowUtcMs) continue;
            nextDialogueStateScan[companion.EntityId] = nowUtcMs + 5000;
            MaintainPendingPackDialogue(companion);
            MaintainFirstSeenMilestones(companion);
            MaintainHighVisibilityDialogue(companion, nowUtcMs);
        }

        foreach (IGrouping<string, Entity> ownerPack in loadedFoxes.Values
                     .Where(companion => !activeConversationByOwner.ContainsKey(GetCompanionOwnerUid(companion))
                         && nextConversationByOwner.GetValueOrDefault(GetCompanionOwnerUid(companion)) <= nowUtcMs)
                     .Where(IsConversationEligible)
                     .GroupBy(GetCompanionOwnerUid)
                     .Where(group => !string.IsNullOrWhiteSpace(group.Key)))
        {
            string ownerUid = ownerPack.Key;
            if (activeConversationByOwner.ContainsKey(ownerUid)
                || nextConversationByOwner.GetValueOrDefault(ownerUid) > nowUtcMs)
            {
                continue;
            }

            Entity[] companions = ownerPack.ToArray();
            if (companions.Length < 2)
            {
                TryStartMissingParentMonologue(ownerUid, companions, nowUtcMs);
                ScheduleNextConversation(ownerUid, nowUtcMs);
                continue;
            }

            var pairs = new List<(Entity A, Entity B)>();
            for (int first = 0; first < companions.Length; first++)
            for (int second = first + 1; second < companions.Length; second++)
            {
                Entity a = companions[first];
                Entity b = companions[second];
                if (a.Pos.Dimension == b.Pos.Dimension && a.Pos.SquareDistanceTo(b.Pos.XYZ) <= 12d * 12d)
                    pairs.Add((a, b));
            }
            Shuffle(pairs);
            bool started = false;
            foreach ((Entity a, Entity b) in pairs)
            {
                if (TryStartConversation(ownerUid, a, b, nowUtcMs))
                {
                    started = true;
                    break;
                }
            }
            if (!started) TryStartMissingParentMonologue(ownerUid, companions, nowUtcMs);
            ScheduleNextConversation(ownerUid, nowUtcMs);
        }
    }

    private void ScheduleNextConversation(string ownerUid, long nowUtcMs)
    {
        nextConversationByOwner[ownerUid] = nowUtcMs + Random.Shared.NextInt64(
            ConversationOwnerMinimumMs, ConversationOwnerMaximumMs + 1);
    }

    private bool TryStartConversation(string ownerUid, Entity first, Entity second, long nowUtcMs,
        string? requiredContext = null)
    {
        if (serverApi?.World.PlayerByUid(ownerUid) is not IServerPlayer owner
            || owner.Entity == null || owner.Entity.Pos.Dimension != first.Pos.Dimension
            || owner.Entity.Pos.SquareDistanceTo(first.Pos.XYZ) > 32d * 32d)
            return false;
        if (activeConversationByOwner.Values.Any(active =>
                (active.A.Pos.Dimension == first.Pos.Dimension && active.A.Pos.SquareDistanceTo(first.Pos.XYZ) <= 24d * 24d)
                || (active.B.Pos.Dimension == first.Pos.Dimension && active.B.Pos.SquareDistanceTo(first.Pos.XYZ) <= 24d * 24d)))
            return false;
        if (IsConversationPriorityBlockedNear(first, nowUtcMs)) return false;
        string firstId = GetDialogueCompanionId(first);
        string secondId = GetDialogueCompanionId(second);
        string pairKey = string.CompareOrdinal(firstId, secondId) < 0
            ? firstId + "|" + secondId
            : secondId + "|" + firstId;
        if (conversationPairCooldowns.GetValueOrDefault(pairKey) > nowUtcMs
            || conversationCompanionCooldowns.GetValueOrDefault(firstId) > nowUtcMs
            || conversationCompanionCooldowns.GetValueOrDefault(secondId) > nowUtcMs)
        {
            return false;
        }

        HashSet<string> contexts = requiredContext == null
            ? GetConversationContexts(first, second, nowUtcMs)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { requiredContext };
        var matches = new List<(CompanionConversationExchange Exchange, Entity A, Entity B)>();
        foreach (CompanionConversationExchange exchange in conversationExchanges)
        {
            if (!contexts.Contains(exchange.Context)
                || conversationExchangeCooldowns.GetValueOrDefault(
                    ConversationExchangeCooldownKey(ownerUid, exchange.Id)) > nowUtcMs)
                continue;
            if (MatchesExchange(exchange, first, second)) matches.Add((exchange, first, second));
            else if (MatchesExchange(exchange, second, first)) matches.Add((exchange, second, first));
        }
        if (matches.Count == 0) return false;
        if (requiredContext == null)
        {
            HashSet<string> triggered = GetTriggeredConversationContexts(first, nowUtcMs);
            triggered.UnionWith(GetTriggeredConversationContexts(second, nowUtcMs));
            var triggeredMatches = matches.Where(match => triggered.Contains(match.Exchange.Context)).ToList();
            if (triggeredMatches.Count > 0) matches = triggeredMatches;
        }
        int tier = matches.Max(match => match.Exchange.Tier);
        matches = matches.Where(match => match.Exchange.Tier == tier).ToList();
        string lastContext = lastConversationContextByOwner.GetValueOrDefault(ownerUid, string.Empty);
        var differentContextMatches = matches.Where(match =>
            !match.Exchange.Context.Equals(lastContext, StringComparison.OrdinalIgnoreCase)).ToList();
        if (differentContextMatches.Count > 0) matches = differentContextMatches;
        var best = matches.ToArray();
        var chosen = best[Random.Shared.Next(best.Length)];
        long token = Random.Shared.NextInt64(1, long.MaxValue);
        activeConversationByOwner[ownerUid] = new ActiveCompanionConversation
        {
            Token = token,
            OwnerUid = ownerUid,
            A = chosen.A,
            B = chosen.B,
            Exchange = chosen.Exchange
        };
        conversationPairCooldowns[pairKey] = nowUtcMs + ConversationPairCooldownMs;
        conversationCompanionCooldowns[firstId] = nowUtcMs + ConversationCompanionCooldownMs;
        conversationCompanionCooldowns[secondId] = nowUtcMs + ConversationCompanionCooldownMs;
        conversationExchangeCooldowns[ConversationExchangeCooldownKey(ownerUid, chosen.Exchange.Id)] =
            nowUtcMs + 30 * 60 * 1000;
        lastConversationContextByOwner[ownerUid] = chosen.Exchange.Context;
        ConsumeTriggeredConversationContext(chosen.A, chosen.Exchange.Context);
        ConsumeTriggeredConversationContext(chosen.B, chosen.Exchange.Context);
        ContinueConversation(ownerUid, token);
        return true;
    }

    private bool TryStartTriggeredRelationshipConversation(string ownerUid, Entity participant,
        string context, long nowUtcMs)
    {
        if (!IsConversationEligible(participant) || activeConversationByOwner.ContainsKey(ownerUid)) return false;
        Entity[] partners = loadedFoxes.Values.Where(candidate =>
                candidate.EntityId != participant.EntityId
                && GetCompanionOwnerUid(candidate) == ownerUid
                && IsConversationEligible(candidate)
                && candidate.Pos.Dimension == participant.Pos.Dimension
                && candidate.Pos.SquareDistanceTo(participant.Pos.XYZ) <= 12d * 12d)
            .ToArray();
        Shuffle(partners);
        foreach (Entity partner in partners)
        {
            if (TryStartConversation(ownerUid, participant, partner, nowUtcMs, context)) return true;
        }
        return false;
    }

    private void ContinueConversation(string ownerUid, long token)
    {
        if (serverApi == null
            || !activeConversationByOwner.TryGetValue(ownerUid, out ActiveCompanionConversation? active)
            || active.Token != token)
            return;
        if (!IsConversationEligible(active.A) || !IsConversationEligible(active.B)
            || active.A.Pos.Dimension != active.B.Pos.Dimension
            || active.A.Pos.SquareDistanceTo(active.B.Pos.XYZ) > 14d * 14d
            || IsTemporalStormActive())
        {
            activeConversationByOwner.Remove(ownerUid);
            return;
        }
        if (active.NextLine >= active.Exchange.Lines.Count)
        {
            activeConversationByOwner.Remove(ownerUid);
            return;
        }
        if (serverApi.World.PlayerByUid(ownerUid) is not IServerPlayer owner
            || owner.Entity == null || owner.Entity.Pos.Dimension != active.A.Pos.Dimension
            || owner.Entity.Pos.SquareDistanceTo(active.A.Pos.XYZ) > 32d * 32d)
        {
            activeConversationByOwner.Remove(ownerUid);
            return;
        }
        CompanionConversationLine line = active.Exchange.Lines[active.NextLine++];
        Entity speaker = string.Equals(line.Speaker, "B", StringComparison.OrdinalIgnoreCase) ? active.B : active.A;
        SendDialoguePacket(owner, speaker, new CompanionDialogueSelection
        {
            EventId = active.Exchange.Id,
            LineId = active.Exchange.Id + ":" + active.NextLine,
            Text = line.Text,
            Channel = "bubble",
            SemanticGroup = "conversation",
            Priority = CompanionDialoguePriority.Low,
            ExpiresAtUtcMs = UtcNowMs() + 10000
        });
        serverApi.Event.RegisterCallback(_ => ContinueConversation(ownerUid, token), (int)ConversationLineDelayMs);
    }

    private void InterruptConversationsNear(Entity speaker, CompanionDialogueSelection selection)
    {
        long nowUtcMs = UtcNowMs();
        if (selection.Priority > CompanionDialoguePriority.Low)
            lastFollowPriorityByOwner[GetCompanionOwnerUid(speaker)] = nowUtcMs;
        if (selection.Priority > CompanionDialoguePriority.Low)
            conversationPriorityBlocks[speaker.EntityId] = Math.Max(
                conversationPriorityBlocks.GetValueOrDefault(speaker.EntityId),
                Math.Max(nowUtcMs + 5000, selection.ExpiresAtUtcMs));
        if (selection.Priority <= CompanionDialoguePriority.Low) return;
        foreach ((string ownerUid, ActiveCompanionConversation active) in activeConversationByOwner.ToArray())
        {
            if ((active.A.Pos.Dimension == speaker.Pos.Dimension && active.A.Pos.SquareDistanceTo(speaker.Pos.XYZ) <= 16d * 16d)
                || (active.B.Pos.Dimension == speaker.Pos.Dimension && active.B.Pos.SquareDistanceTo(speaker.Pos.XYZ) <= 16d * 16d))
            {
                activeConversationByOwner.Remove(ownerUid);
            }
        }
    }

    private bool IsConversationPriorityBlockedNear(Entity candidate, long nowUtcMs)
    {
        foreach ((long entityId, long blockedUntil) in conversationPriorityBlocks.ToArray())
        {
            if (blockedUntil <= nowUtcMs)
            {
                conversationPriorityBlocks.Remove(entityId);
                continue;
            }
            if (loadedFoxes.TryGetValue(entityId, out Entity? blocker)
                && blocker.Pos.Dimension == candidate.Pos.Dimension
                && blocker.Pos.SquareDistanceTo(candidate.Pos.XYZ) <= 16d * 16d)
                return true;
        }
        return false;
    }

    private bool MatchesExchange(CompanionConversationExchange exchange, Entity a, Entity b)
    {
        string personalityA = GetDomesticationStatus(a)?.GetString(PersonalityKey, string.Empty) ?? string.Empty;
        string personalityB = GetDomesticationStatus(b)?.GetString(PersonalityKey, string.Empty) ?? string.Empty;
        if (exchange.PersonalityA != "*" && !exchange.PersonalityA.Equals(personalityA, StringComparison.OrdinalIgnoreCase)) return false;
        if (exchange.PersonalityB != "*" && !exchange.PersonalityB.Equals(personalityB, StringComparison.OrdinalIgnoreCase)) return false;
        if (exchange.Tier == 4 && exchange.Relationship == "parent_juvenile")
        {
            bool aIsParent = exchange.RoleA.Equals("Parent", StringComparison.OrdinalIgnoreCase);
            Entity parent = aIsParent ? a : b;
            Entity child = aIsParent ? b : a;
            if (exchange.Context == "injury" && GetHealthFraction(child) >= .75f) return false;
            if (exchange.Context == "parent_injured" && GetHealthFraction(parent) >= .75f) return false;
            if (exchange.Context == "frightened" && GetMood(child) is not ("alarmed" or "anxious")) return false;
        }
        return exchange.Tier switch
        {
            4 => MatchesRelationship(exchange, a, b),
            3 => exchange.PersonalityA != "*" && exchange.PersonalityB != "*",
            2 => (exchange.PersonalityA == "*") != (exchange.PersonalityB == "*"),
            _ => true
        };
    }

    private static bool MatchesRelationship(CompanionConversationExchange exchange, Entity a, Entity b)
    {
        ITreeAttribute? aStatus = GetDomesticationStatus(a);
        ITreeAttribute? bStatus = GetDomesticationStatus(b);
        if (aStatus == null || bStatus == null) return false;
        string aId = aStatus.GetString(FoxIdKey, string.Empty);
        string bId = bStatus.GetString(FoxIdKey, string.Empty);
        if (exchange.Relationship == "bonded_partners")
            return aStatus.GetString(BondedPartnerIdKey, string.Empty) == bId
                && bStatus.GetString(BondedPartnerIdKey, string.Empty) == aId;

        bool aParent = bStatus.GetString(ParentMotherIdKey, string.Empty) == aId
            || bStatus.GetString(ParentFatherIdKey, string.Empty) == aId;
        bool aIsParentRole = exchange.RoleA.Equals("Parent", StringComparison.OrdinalIgnoreCase);
        bool childStageMatches = exchange.Relationship == "parent_juvenile"
            ? IsCompanionJuvenile(aIsParentRole ? b : a)
            : !IsCompanionJuvenile(aIsParentRole ? b : a);
        return childStageMatches && (aIsParentRole ? aParent :
            aStatus.GetString(ParentMotherIdKey, string.Empty) == bId
            || aStatus.GetString(ParentFatherIdKey, string.Empty) == bId);
    }

    private HashSet<string> GetConversationContexts(Entity a, Entity b, long nowUtcMs)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "idle", "idle_checkin", "light_banter", "observations", "pack_group" };
        double hour = a.World.Calendar.HourOfDay;
        if (hour < 7 || hour >= 19) { result.Add("night"); result.Add("night_rest"); }
        string activityA = GetCompanionActivityMode(a);
        string activityB = GetCompanionActivityMode(b);
        if (activityA == CompanionActivityMode.AtEase && activityB == CompanionActivityMode.AtEase)
        { result.Add("home"); result.Add("home_den"); }
        if (activityA == CompanionActivityMode.Follow || activityB == CompanionActivityMode.Follow)
        { result.Add("travel"); }
        if (HasEnabledDialogueDuty(a) || HasEnabledDialogueDuty(b))
        { result.Add("work"); }
        if (IsInConversationCombatAftermath(a, nowUtcMs) || IsInConversationCombatAftermath(b, nowUtcMs))
        { result.Add("danger_aftermath"); result.Add("combat_aftermath"); }
        if (GetHealthFraction(a) < .75f || GetHealthFraction(b) < .75f)
        { result.Add("injury"); result.Add("injury_recovery"); result.Add("parent_injured"); }
        bool juvenilePair = IsCompanionJuvenile(a) || IsCompanionJuvenile(b);
        if (juvenilePair)
        {
            result.Add("follow_parent");
            result.Add("learning");
            if (GetMood(a) is "alarmed" or "anxious" || GetMood(b) is "alarmed" or "anxious")
                result.Add("frightened");
            Entity juvenile = IsCompanionJuvenile(a) ? a : b;
            if (IsApproachingAdulthood(juvenile))
                result.Add("adulthood_approaching");
        }
        else if (HasParentIdentity(a) || HasParentIdentity(b))
        {
            result.Add("adulthood");
        }
        string expeditionA = GetDomesticationStatus(a)?.GetString(ExpeditionStatusKey, string.Empty) ?? string.Empty;
        string expeditionB = GetDomesticationStatus(b)?.GetString(ExpeditionStatusKey, string.Empty) ?? string.Empty;
        if (expeditionA.StartsWith("Returned", StringComparison.OrdinalIgnoreCase)
            || expeditionB.StartsWith("Returned", StringComparison.OrdinalIgnoreCase))
        {
            result.Add("expedition_return");
            result.Add("reunion");
            result.Add("returning");
        }
        string weather = GetDialogueWeather(a);
        if (weather == "clear") result.Add("weather_clear");
        else if (weather == "rain") result.Add("weather_rain");
        else if (weather == "snow") result.Add("weather_snow");
        else if (weather is "cold" or "heat") result.Add("weather_temperature");
        result.UnionWith(GetTriggeredConversationContexts(a, nowUtcMs));
        result.UnionWith(GetTriggeredConversationContexts(b, nowUtcMs));
        result.Add("social");
        result.Add("quiet_companionship");
        return result;
    }

    private static bool HasParentIdentity(Entity entity)
    {
        ITreeAttribute? status = GetDomesticationStatus(entity);
        return !string.IsNullOrWhiteSpace(status?.GetString(ParentMotherIdKey, string.Empty))
            || !string.IsNullOrWhiteSpace(status?.GetString(ParentFatherIdKey, string.Empty));
    }

    private static bool IsApproachingAdulthood(Entity juvenile)
    {
        EntityBehaviorGrow? grow = juvenile.GetBehavior<EntityBehaviorGrow>();
        if (grow == null || grow.HoursToGrow <= 0 || GrowSpawnedTotalHoursProperty == null) return false;
        try
        {
            double spawnedTotalHours = Convert.ToDouble(GrowSpawnedTotalHoursProperty.GetValue(grow));
            return juvenile.World.Calendar.TotalHours - spawnedTotalHours >= grow.HoursToGrow * .8d;
        }
        catch
        {
            return false;
        }
    }

    private bool TryStartMissingParentMonologue(string ownerUid, IEnumerable<Entity> companions, long nowUtcMs)
    {
        if (serverApi?.World.PlayerByUid(ownerUid) is not IServerPlayer owner) return false;
        foreach (Entity child in companions.Where(IsCompanionJuvenile))
        {
            if (owner.Entity == null || owner.Entity.Pos.Dimension != child.Pos.Dimension
                || owner.Entity.Pos.SquareDistanceTo(child.Pos.XYZ) > 32d * 32d
                || IsTemporalStormActive()
                || IsConversationPriorityBlockedNear(child, nowUtcMs)
                || conversationCompanionCooldowns.GetValueOrDefault(GetDialogueCompanionId(child)) > nowUtcMs
                || activeConversationByOwner.Values.Any(active =>
                    (active.A.Pos.Dimension == child.Pos.Dimension && active.A.Pos.SquareDistanceTo(child.Pos.XYZ) <= 24d * 24d)
                    || (active.B.Pos.Dimension == child.Pos.Dimension && active.B.Pos.SquareDistanceTo(child.Pos.XYZ) <= 24d * 24d)))
                continue;
            ITreeAttribute? status = GetDomesticationStatus(child);
            string mother = status?.GetString(ParentMotherIdKey, string.Empty) ?? string.Empty;
            string father = status?.GetString(ParentFatherIdKey, string.Empty) ?? string.Empty;
            bool parentPresent = loadedFoxes.Values.Any(entity =>
                IsDialogueCompanionPresent(entity)
                && (GetDialogueCompanionId(entity) == mother || GetDialogueCompanionId(entity) == father)
                && entity.Pos.Dimension == child.Pos.Dimension
                && entity.Pos.SquareDistanceTo(child.Pos.XYZ) <= 24d * 24d);
            if (parentPresent) continue;
            CompanionConversationExchange[] exchanges = conversationExchanges.Where(exchange =>
                exchange.Tier == 4 && exchange.Relationship == "parent_juvenile"
                && exchange.Context == "cannot_find_parent"
                && conversationExchangeCooldowns.GetValueOrDefault(
                    ConversationExchangeCooldownKey(ownerUid, exchange.Id)) <= nowUtcMs).ToArray();
            if (exchanges.Length == 0) return false;
            CompanionConversationExchange exchange = exchanges[Random.Shared.Next(exchanges.Length)];
            CompanionConversationLine? line = exchange.Lines.FirstOrDefault(candidate =>
                candidate.Role.Equals("Juvenile", StringComparison.OrdinalIgnoreCase)
                || candidate.Speaker.Equals(exchange.RoleA.Equals("Juvenile", StringComparison.OrdinalIgnoreCase) ? "A" : "B", StringComparison.OrdinalIgnoreCase));
            if (line == null) return false;
            SendDialoguePacket(owner, child, new CompanionDialogueSelection
            {
                EventId = exchange.Id,
                LineId = exchange.Id + ":one-sided",
                Text = line.Text,
                SemanticGroup = "conversation.parent-missing",
                Priority = CompanionDialoguePriority.Medium,
                ExpiresAtUtcMs = nowUtcMs + 10000
            });
            conversationCompanionCooldowns[GetDialogueCompanionId(child)] = nowUtcMs + ConversationCompanionCooldownMs;
            conversationExchangeCooldowns[ConversationExchangeCooldownKey(ownerUid, exchange.Id)] =
                nowUtcMs + 30 * 60 * 1000;
            lastConversationContextByOwner[ownerUid] = exchange.Context;
            return true;
        }
        return false;
    }

    private void MaintainFirstSeenMilestones(Entity companion)
    {
        ITreeAttribute? status = GetDomesticationStatus(companion);
        string ownerUid = GetCompanionOwnerUid(companion);
        if (status == null || serverApi == null) return;
        IServerPlayer? owner = serverApi.World.PlayerByUid(ownerUid) as IServerPlayer;

        var candidates = new List<string>();
        if (IsTemporalStormActive()) candidates.Add("milestone.first_temporal_storm");
        if (owner?.Entity?.WatchedAttributes.GetDouble("temporalStability", 1d) <= .001d)
            candidates.Add("milestone.zero_temporal_stability");
        EnvironmentSnapshot environment = GetEnvironmentSnapshot(companion);
        if (environment.NearMechanicalDevice) candidates.Add("milestone.mechanical_piece");
        ScanMilestoneBlocks(companion, candidates);
        foreach (Entity nearby in serverApi.World.GetEntitiesAround(companion.Pos.XYZ, 32f, 20f))
        {
            if (!nearby.Alive || nearby.Pos.Dimension != companion.Pos.Dimension || nearby == companion) continue;
            string path = nearby.Code?.Path ?? string.Empty;
            if (nearby is EntityItem dropped
                && CompanionMilestoneRules.IsGemItemPath(dropped.Itemstack?.Collectible?.Code?.Path))
                candidates.Add("milestone.gem");
            if (IsTrader(nearby)) candidates.Add("milestone.trader");
            if (path == "bell-normal") candidates.Add("milestone.bell");
            if (path == "shiver-bellhead") candidates.Add("milestone.bellhead_shiver");
            if (path == "bowtorn-gearfoot") candidates.Add("milestone.gearfoot_bowtorn");
            if (path == "drifter-double-headed") candidates.Add("milestone.double_headed_drifter");
            if (path == "locust-corrupt-sawblade") candidates.Add("milestone.corrupt_sawblade_locust");
            if (path == "eidolon-immobilized") candidates.Add("milestone.hidden_boss_01");
            if (path == "erel-pristine" || path == "erel-corrupted") candidates.Add("milestone.hidden_boss_02");
        }
        string completedBefore = status.GetString(DialogueMilestonesKey, string.Empty);
        string pendingBefore = status.GetString(DialoguePendingMilestonesKey, string.Empty);
        string rewardedBefore = status.GetString(DialogueRewardedMilestonesKey, string.Empty);
        HashSet<string> completed = ParsePersistedDialogueSet(completedBefore);
        HashSet<string> pending = ParsePersistedDialogueSet(pendingBefore);
        HashSet<string> rewarded = ParsePersistedDialogueSet(rewardedBefore);
        foreach (string eventId in pending.Where(IsMilestoneDialogueEvent))
            completed.Add(eventId);

        string[] unseenCandidates = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(eventId => !completed.Contains(eventId))
            .ToArray();

        // A single scan can see several milestones at once. Reward and record
        // all of them immediately, but only offer the first one for dialogue;
        // otherwise the pending set becomes a conversational backlog that is
        // replayed as a burst over subsequent scans.
        foreach (string eventId in unseenCandidates)
        {
            completed.Add(eventId);
            if (!rewarded.Contains(eventId))
            {
                AwardIndividualAndPackPoint(companion, status);
                rewarded.Add(eventId);
            }
        }

        string? dialogueEventId = unseenCandidates.FirstOrDefault();
        if (dialogueEventId != null && owner != null)
            EmitDialogueEvent(
                companion,
                owner,
                dialogueEventId,
                GetMilestonePlaceholder(dialogueEventId),
                CompanionDialoguePriority.High,
                dialogueEventId);

        // Dialogue milestones were persisted before they granted talent points.
        // Reconcile that history through a separate ledger so existing foxes
        // receive their earned points exactly once.
        foreach (string eventId in completed
                     .Where(IsMilestoneDialogueEvent)
                     .Where(value => !rewarded.Contains(value))
                     .ToArray())
        {
            AwardIndividualAndPackPoint(companion, status);
            rewarded.Add(eventId);
        }

        // Pending entries from older builds represent already-seen milestones,
        // not dialogue that must be replayed. They have been accounted for by
        // the reconciliation above, so do not carry them forward.
        pending.Clear();

        string completedAfter = string.Join('|', completed.OrderBy(value => value));
        string pendingAfter = string.Join('|', pending.OrderBy(value => value));
        string rewardedAfter = string.Join('|', rewarded.OrderBy(value => value));
        if (completedAfter != completedBefore
            || pendingAfter != pendingBefore
            || rewardedAfter != rewardedBefore)
        {
            status.SetString(DialogueMilestonesKey, completedAfter);
            status.SetString(DialoguePendingMilestonesKey, pendingAfter);
            status.SetString(DialogueRewardedMilestonesKey, rewardedAfter);
            MarkSocialStateDirty(companion);
        }
    }

    private void MaintainPendingPackDialogue(Entity companion)
    {
        if (packRepository?.Loaded != true || !TryGetOwnerPlayer(companion, out IServerPlayer? owner)) return;
        string foxId = GetDialogueCompanionId(companion);
        if (!packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
            || record == null || string.IsNullOrWhiteSpace(record.PendingDialogueEvent)) return;
        string eventId = record.PendingDialogueEvent;
        if (!EmitDialogueEvent(companion, owner!, eventId, string.Empty, CompanionDialoguePriority.High)) return;
        record.PendingDialogueEvent = string.Empty;
        record.WasMiaBeforeReturn = false;
        if (eventId == "expedition.mia_return")
        {
            Entity? reactor = loadedFoxes.Values
                .Where(entity => entity.EntityId != companion.EntityId && IsConversationEligible(entity)
                    && GetCompanionOwnerUid(entity) == GetCompanionOwnerUid(companion)
                    && entity.Pos.Dimension == companion.Pos.Dimension
                    && entity.Pos.SquareDistanceTo(companion.Pos.XYZ) <= 12d * 12d)
                .OrderBy(entity => entity.Pos.SquareDistanceTo(companion.Pos.XYZ))
                .FirstOrDefault();
            if (reactor != null)
                EmitDialogueEvent(reactor, owner!, "expedition.mia_return_reaction", string.Empty, CompanionDialoguePriority.High);
        }
        if (eventId == "expedition.mia_return")
            RememberTriggeredConversationContext(companion, "mia_return", 10 * 60 * 1000);
        else if (eventId.StartsWith("expedition.return_", StringComparison.OrdinalIgnoreCase))
            RememberTriggeredConversationContext(companion, "expedition_return", 10 * 60 * 1000);
        packRepository.Save();
    }

    private void ScanMilestoneBlocks(Entity companion, List<string> candidates)
    {
        if (serverApi == null) return;
        BlockPos center = companion.Pos.AsBlockPos;
        for (int dx = -5; dx <= 5; dx++)
        for (int dy = -2; dy <= 3; dy++)
        for (int dz = -5; dz <= 5; dz++)
        {
            BlockPos pos = new(center.X + dx, center.Y + dy, center.Z + dz, center.dimension);
            Block block = serverApi.World.BlockAccessor.GetBlock(pos);
            string path = block.Code?.Path ?? string.Empty;
            if (CompanionMilestoneRules.IsAnvilBlockPath(path)) candidates.Add("milestone.anvil");
            if (CompanionMilestoneRules.IsCopperOreBlockPath(path)) candidates.Add("milestone.copper_ore");
            if (CompanionMilestoneRules.IsTinOreBlockPath(path)) candidates.Add("milestone.tin_ore");
            if (CompanionMilestoneRules.IsIronOreBlockPath(path)) candidates.Add("milestone.iron_ore");
            if (CompanionMilestoneRules.IsGoldOreBlockPath(path)) candidates.Add("milestone.gold_ore");
            if (CompanionMilestoneRules.IsBountifulOreBlockPath(path)) candidates.Add("milestone.bountiful_ore");
            if (CompanionMilestoneRules.IsNaturalBeehiveBlockPath(path)) candidates.Add("milestone.natural_beehive");
            if (CompanionMilestoneRules.IsVisibleItemDisplayBlockPath(path)
                && serverApi.World.BlockAccessor.GetBlockEntity(pos) is IBlockEntityContainer display
                && display.Inventory.Any(slot =>
                    CompanionMilestoneRules.IsGemItemPath(slot.Itemstack?.Collectible?.Code?.Path)))
            {
                candidates.Add("milestone.gem");
            }
            if (path.Contains("snow", StringComparison.OrdinalIgnoreCase)) candidates.Add("milestone.snow");
            if (block is BlockCrop crop && crop.CropProps != null && crop.CurrentCropStage >= crop.CropProps.GrowthStages)
                candidates.Add("milestone.mature_crop");
        }
    }

    private static string GetMilestonePlaceholder(string eventId) => eventId switch
    {
        "milestone.anvil" => "[PLACEHOLDER: first anvil discovery]",
        "milestone.copper_ore" => "[PLACEHOLDER: first copper ore block discovery]",
        "milestone.tin_ore" => "[PLACEHOLDER: first tin ore block discovery]",
        "milestone.iron_ore" => "[PLACEHOLDER: first iron ore block discovery]",
        "milestone.gold_ore" => "We're Rich!",
        "milestone.bountiful_ore" => "[PLACEHOLDER: first bountiful ore block discovery]",
        "milestone.gem" => "[PLACEHOLDER: first loose or displayed gem discovery]",
        "milestone.natural_beehive" => "[PLACEHOLDER: first natural beehive discovery]",
        _ => string.Empty
    };

    private void MaintainHighVisibilityDialogue(Entity companion, long nowUtcMs)
    {
        bool inCombat = IsRecentlyInCombat(companion, nowUtcMs);
        if (dialogueCombatState.TryGetValue(companion.EntityId, out bool wasInCombat) && wasInCombat != inCombat
            && TryGetOwnerPlayer(companion, out IServerPlayer? combatOwner))
        {
            EmitDialogueEvent(companion, combatOwner!, inCombat ? "combat.enter" : "combat.end", string.Empty,
                inCombat ? CompanionDialoguePriority.High : CompanionDialoguePriority.Medium);
        }
        dialogueCombatState[companion.EntityId] = inCombat;

        bool nighttime = companion.World.Calendar.HourOfDay < 6.5 || companion.World.Calendar.HourOfDay >= 19.5;
        bool sleeping = GetMood(companion) is "sleepy" or "resting";
        if (nighttime && dialogueSleepingState.GetValueOrDefault(companion.EntityId) && !sleeping
            && TryGetOwnerPlayer(companion, out IServerPlayer? wokenOwner))
            EmitDialogueEvent(companion, wokenOwner!, "night.woken", string.Empty, CompanionDialoguePriority.Medium);
        dialogueSleepingState[companion.EntityId] = sleeping;
        if (companion.WatchedAttributes.GetInt(EntityHealthStateKey, 0) == RecoveringHealthState
            && nextRecoveryDialogue.GetValueOrDefault(companion.EntityId) <= nowUtcMs
            && TryGetOwnerPlayer(companion, out IServerPlayer? recoveryOwner))
        {
            nextRecoveryDialogue[companion.EntityId] = nowUtcMs + 15 * 60 * 1000;
            EmitDialogueEvent(companion, recoveryOwner!, "recovery.restless", string.Empty, CompanionDialoguePriority.Medium);
        }
        if (nighttime && !sleeping && nextNightDialogue.GetValueOrDefault(companion.EntityId) <= nowUtcMs
            && TryGetOwnerPlayer(companion, out IServerPlayer? nightOwner))
        {
            nextNightDialogue[companion.EntityId] = nowUtcMs + 30 * 60 * 1000;
            string foxId = GetDialogueCompanionId(companion);
            FoxBedRecord? bed = packRepository?.GetBedForFox(foxId);
            if (bed == null)
                EmitDialogueEvent(companion, nightOwner!, "night.no_bed", string.Empty, CompanionDialoguePriority.Medium);
            else if (roomRegistry?.GetRoomForPosition(new BlockPos(bed.X, bed.Y, bed.Z, bed.Dimension)) is not Room room
                     || room.ExitCount > 0)
                EmitDialogueEvent(companion, nightOwner!, "night.outdoor_bed", string.Empty, CompanionDialoguePriority.Medium);
        }

        string mood = GetMood(companion);
        if (!string.Equals(spontaneousMoodEpisode.GetValueOrDefault(companion.EntityId), mood, StringComparison.OrdinalIgnoreCase))
        {
            spontaneousMoodEpisode[companion.EntityId] = mood;
            spontaneousMoodSince[companion.EntityId] = nowUtcMs;
        }
        else if (spontaneousMoodSince.GetValueOrDefault(companion.EntityId) > 0
                 && nowUtcMs - spontaneousMoodSince[companion.EntityId] >= 30000
                 && IsConversationEligible(companion)
                 && Random.Shared.NextDouble() < .015
                 && TryGetOwnerPlayer(companion, out IServerPlayer? owner))
        {
            string? eventId = GetMoodDialogueEvent(mood, false);
            if (eventId != null && EmitDialogueEvent(companion, owner!, eventId, string.Empty, CompanionDialoguePriority.Low))
                spontaneousMoodSince[companion.EntityId] = long.MaxValue;
        }

        string weather = GetDialogueWeather(companion);
        if (!string.Equals(lastDialogueWeather.GetValueOrDefault(companion.EntityId), weather, StringComparison.Ordinal))
        {
            lastDialogueWeather[companion.EntityId] = weather;
            dialogueWeatherChangedAt[companion.EntityId] = nowUtcMs;
        }
        else if (dialogueWeatherChangedAt.GetValueOrDefault(companion.EntityId) > 0
                 && nowUtcMs - dialogueWeatherChangedAt[companion.EntityId] >= 20000
                 && TryGetOwnerPlayer(companion, out IServerPlayer? owner)
                 && EmitDialogueEvent(companion, owner!, "weather." + weather, string.Empty, CompanionDialoguePriority.Low))
        {
            dialogueWeatherChangedAt[companion.EntityId] = 0;
        }
        if (IsConversationEligible(companion) && Random.Shared.NextDouble() < .00035
            && TryGetOwnerPlayer(companion, out IServerPlayer? easterOwner))
            EmitDialogueEvent(companion, easterOwner!, "thought.easter_egg", string.Empty, CompanionDialoguePriority.Low);
    }

    private string GetDialogueWeather(Entity companion)
    {
        if (IsTemporalStormActive()) return "storm_wind";
        float temperature = serverApi?.World.BlockAccessor.GetClimateAt(
            companion.Pos.AsBlockPos, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly,
            companion.World.Calendar.TotalDays).Temperature ?? 10f;
        if (IsPrecipitating(companion)) return temperature <= 0 ? "snow" : "rain";
        if (temperature < 0) return "cold";
        if (temperature > 30) return "heat";
        return "clear";
    }

    private bool EmitDialogueEvent(Entity companion, IServerPlayer owner, string eventId, string fallback,
        int fallbackPriority = CompanionDialoguePriority.Low, string semanticGroup = "",
        IReadOnlyDictionary<string, string>? contextFacts = null)
    {
        if (StfuModeEnabled) return false;

        if (eventId.StartsWith("food.", StringComparison.OrdinalIgnoreCase))
            RememberTriggeredConversationContext(companion, "food", 5 * 60 * 1000);
        if (fallbackPriority <= CompanionDialoguePriority.Low
            && activeConversationByOwner.Values.Any(active =>
                active.A.EntityId == companion.EntityId || active.B.EntityId == companion.EntityId))
            return false;
        return dialogueService?.TryEmit(companion, owner, eventId, CreateDialogueContext(companion, eventId, contextFacts),
            fallback, fallbackPriority, semanticGroup) == true;
    }

    internal bool EmitCompanionRuntimeDialogue(Entity companion, string eventId,
        int priority = CompanionDialoguePriority.Low,
        IReadOnlyDictionary<string, string>? additionalFacts = null)
    {
        return TryGetOwnerPlayer(companion, out IServerPlayer? owner)
            && EmitDialogueEvent(
                companion,
                owner!,
                eventId,
                string.Empty,
                priority,
                contextFacts: additionalFacts);
    }

    private void EmitCommandAcknowledgement(Entity companion, IServerPlayer owner, string eventId)
    {
        if (!string.IsNullOrWhiteSpace(eventId))
            EmitDialogueEvent(companion, owner, eventId, string.Empty, CompanionDialoguePriority.Medium);
    }

    private static string GetDutyCommandEvent(string duty) => duty switch
    {
        CompanionDuty.GroundDroppedItems => "command.duty.dropped_items.enabled",
        CompanionDuty.GroundCattails => "command.duty.cattails.enabled",
        CompanionDuty.GroundFlint => "command.duty.flint.enabled",
        CompanionDuty.GroundSticks => "command.duty.sticks.enabled",
        CompanionDuty.GroundBoulders => "command.duty.boulders.enabled",
        CompanionDuty.GroundRocks => "command.duty.rocks.enabled",
        CompanionDuty.FinishedCrops => "command.duty.crops.enabled",
        CompanionDuty.FinishedBerries => "command.duty.berries.enabled",
        CompanionDuty.FinishedMushrooms => "command.duty.mushrooms.enabled",
        CompanionDuty.FlowerRemoval => "command.duty.flower_removal.enabled",
        CompanionDuty.SnowShoveling => "command.duty.snow_shoveling.enabled",
        CompanionDuty.CharcoalShoveling => "command.duty.snow_shoveling.enabled",
        CompanionDuty.SnowballCollection => "command.duty.snowball_collection.enabled",
        CompanionDuty.StorageSorting => "command.duty.general_storage_sorting.enabled",
        _ => string.Empty
    };

    private bool TryGetOwnerPlayer(Entity companion, out IServerPlayer? owner)
    {
        owner = serverApi?.World.PlayerByUid(GetCompanionOwnerUid(companion)) as IServerPlayer;
        return owner != null;
    }

    private static string? GetMoodDialogueEvent(string mood, bool rightClick) => (mood.ToLowerInvariant(), rightClick) switch
    {
        ("sleepy", true) => "thought.sleepy.rightclick",
        ("sleepy", false) => "thought.sleepy.spontaneous",
        ("alarmed" or "anxious", true) => "thought.alarmed_anxious.rightclick." + mood.ToLowerInvariant(),
        ("alarmed" or "anxious", false) => "thought.alarmed_anxious.spontaneous",
        ("playful", true) => "thought.playful.rightclick",
        ("playful", false) => "thought.playful.spontaneous",
        ("resting" or "content", true) => "thought.resting_content.rightclick." + mood.ToLowerInvariant(),
        ("resting" or "content", false) => "thought.resting_content.spontaneous",
        ("alert" or "rallied", true) => "thought.alert_rallied.rightclick." + mood.ToLowerInvariant(),
        ("alert" or "rallied", false) => "thought.alert_rallied.spontaneous",
        ("curious" or "restless", true) => "thought.curious_restless.rightclick." + mood.ToLowerInvariant(),
        ("curious" or "restless", false) => "thought.curious_restless.spontaneous",
        (_, true) => "thought.neutral.rightclick",
        _ => null
    };

    private static bool IsDialogueCompanionPresent(Entity entity) =>
        entity.Alive && IsTamedFox(entity) && !IsFoxAwayFromWorld(entity);

    private bool IsConversationEligible(Entity entity)
    {
        if (!IsDialogueCompanionPresent(entity) || IsFoxIncapacitated(entity) || IsCompanionFoodRestricted(entity)) return false;
        string mood = GetMood(entity);
        if (mood is "sleepy" or "resting") return false;
        long now = UtcNowMs();
        return !IsRecentlyInCombat(entity, now);
    }

    private static bool IsRecentlyInCombat(Entity entity, long nowUtcMs)
    {
        ITreeAttribute? status = GetDomesticationStatus(entity);
        long latest = Math.Max(status?.GetLong(LastCombatUtcMsKey, 0) ?? 0,
            Math.Max(status?.GetLong(LastDamageUtcMsKey, 0) ?? 0, status?.GetLong(LastAttackUtcMsKey, 0) ?? 0));
        return latest > 0 && nowUtcMs - latest < 10000;
    }

    private static bool IsInConversationCombatAftermath(Entity entity, long nowUtcMs)
    {
        ITreeAttribute? status = GetDomesticationStatus(entity);
        long latest = Math.Max(status?.GetLong(LastCombatUtcMsKey, 0) ?? 0,
            Math.Max(status?.GetLong(LastDamageUtcMsKey, 0) ?? 0, status?.GetLong(LastAttackUtcMsKey, 0) ?? 0));
        long elapsed = latest > 0 ? nowUtcMs - latest : long.MaxValue;
        return elapsed >= 10000 && elapsed < 2 * 60 * 1000;
    }

    private void RememberTriggeredConversationContext(Entity entity, string context, long durationMs)
    {
        if (!triggeredConversationContexts.TryGetValue(entity.EntityId, out Dictionary<string, long>? contexts))
        {
            contexts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            triggeredConversationContexts[entity.EntityId] = contexts;
        }
        contexts[context] = UtcNowMs() + durationMs;
    }

    private HashSet<string> GetTriggeredConversationContexts(Entity entity, long nowUtcMs)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!triggeredConversationContexts.TryGetValue(entity.EntityId, out Dictionary<string, long>? contexts))
            return result;
        foreach ((string context, long expiresAt) in contexts.ToArray())
        {
            if (expiresAt <= nowUtcMs) contexts.Remove(context);
            else result.Add(context);
        }
        if (contexts.Count == 0) triggeredConversationContexts.Remove(entity.EntityId);
        return result;
    }

    private void ConsumeTriggeredConversationContext(Entity entity, string context)
    {
        if (!triggeredConversationContexts.TryGetValue(entity.EntityId, out Dictionary<string, long>? contexts)) return;
        contexts.Remove(context);
        if (contexts.Count == 0) triggeredConversationContexts.Remove(entity.EntityId);
    }

    private static float GetHealthFraction(Entity entity)
    {
        ITreeAttribute? health = entity.WatchedAttributes.GetTreeAttribute("health");
        float current = health?.GetFloat("currenthealth", 1f) ?? 1f;
        float maximum = health?.GetFloat("maxhealth", Math.Max(1f, current)) ?? Math.Max(1f, current);
        return maximum <= 0 ? 1f : current / maximum;
    }

    private static bool HasEnabledDialogueDuty(Entity entity) =>
        IsGroundCleanupEnabled(entity) || IsMowLawnEnabled(entity) || IsFinishedProductsEnabled(entity)
        || IsFlowerRemovalEnabled(entity) || IsSnowShovelingEnabled(entity)
        || IsCharcoalShovelingEnabled(entity) || IsSnowballCollectionEnabled(entity)
        || IsGeneralStorageSortingEnabled(entity);

    private static string GetDialogueCompanionId(Entity entity)
    {
        string id = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        return string.IsNullOrWhiteSpace(id) ? "entity:" + entity.EntityId : id;
    }

    private static string ConversationExchangeCooldownKey(string ownerUid, string exchangeId) =>
        ownerUid + "|" + exchangeId;

    private static bool IsMilestoneDialogueEvent(string eventId) =>
        eventId.StartsWith("milestone.", StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> ParsePersistedDialogueSet(string value) => new(
        value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        StringComparer.OrdinalIgnoreCase);

    private static void Shuffle<T>(IList<T> values)
    {
        for (int index = values.Count - 1; index > 0; index--)
        {
            int swap = Random.Shared.Next(index + 1);
            (values[index], values[swap]) = (values[swap], values[index]);
        }
    }
}
