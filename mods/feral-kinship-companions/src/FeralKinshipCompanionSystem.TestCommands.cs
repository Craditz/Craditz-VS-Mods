using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private void RegisterDeveloperSpawnCommands(ICoreServerAPI api)
    {
        var parsers = api.ChatCommands.Parsers;
        api.ChatCommands.Create("companionspawnfoxtaming")
            .WithDescription("Spawn an owned, tamed Fox Taming fox for Companion testing.")
            .RequiresPlayer()
            .RequiresPrivilege(Privilege.chat)
            .WithArgs(parsers.OptionalWord("type"), parsers.OptionalWord("gender"))
            .HandleWith(OnCompanionSpawnFoxTamingCommand);
        api.ChatCommands.Create("companionspawnguide")
            .WithDescription("Spawn the protected Follow/Stay guide fox for testing.")
            .RequiresPlayer()
            .RequiresPrivilege(Privilege.chat)
            .HandleWith(OnCompanionSpawnGuideCommand);
        api.ChatCommands.Create("bramble")
            .WithDescription("Inspect or safely reset/test your per-player Bramble helper.")
            .RequiresPlayer()
            .RequiresPrivilege(Privilege.chat)
            .WithArgs(parsers.OptionalWord("action"), parsers.OptionalWord("value"))
            .HandleWith(OnBrambleDeveloperCommand);
        api.ChatCommands.Create("companionexpedition")
            .WithDescription("Inspect, immediately complete, or statistically simulate companion expeditions.")
            .RequiresPlayer()
            .RequiresPrivilege(Privilege.chat)
            .WithArgs(
                parsers.OptionalWord("action"),
                parsers.OptionalWord("route-or-id"),
                parsers.OptionalWord("raw-completion"),
                parsers.OptionalWord("party-size"),
                parsers.OptionalWord("runs"),
                parsers.OptionalWord("risk-scale"),
                parsers.OptionalWord("forced-event-or-consequence"))
            .HandleWith(OnCompanionExpeditionDeveloperCommand);
    }

    private TextCommandResult OnCompanionSpawnFoxTamingCommand(TextCommandCallingArgs args)
    {
        if (serverApi == null || args.Caller.Player is not IServerPlayer player || player.Entity == null)
        {
            return TextCommandResult.Error("This command can only be used by a player in a loaded world.");
        }

        if (!CanUseDeveloperTools(player))
        {
            return TextCommandResult.Error(
                "The Fox Taming test spawn is only available in Creative mode or to server administrators."
            );
        }

        string type = args.Parsers.Count > 0 && !args.Parsers[0].IsMissing
            ? ((string?)args[0] ?? string.Empty).Trim().ToLowerInvariant()
            : "red";
        string gender = args.Parsers.Count > 1 && !args.Parsers[1].IsMissing
            ? ((string?)args[1] ?? string.Empty).Trim().ToLowerInvariant()
            : "male";

        if (type is not ("red" or "arctic") || gender is not ("male" or "female"))
        {
            return TextCommandResult.Error(
                "Usage: /companionspawnfoxtaming [red|arctic] [male|female]"
            );
        }

        string code = $"foxtaming:tamefox-{type}-adult-{gender}";
        EntityProperties? properties = serverApi.World.GetEntityType(new AssetLocation(code));
        if (properties == null)
        {
            return TextCommandResult.Error(
                $"Fox Taming entity '{code}' is not available. Install Fox Taming before using this test command."
            );
        }

        Vec3d? spawnPosition = FindSafeEntityPosition(
            player.Entity.Pos.AsBlockPos,
            properties,
            null,
            6,
            4
        );
        if (spawnPosition == null)
        {
            return TextCommandResult.Error("No safe space was found near you for the test fox.");
        }

        Entity? fox = serverApi.World.ClassRegistry.CreateEntity(properties);
        if (fox == null)
        {
            return TextCommandResult.Error($"Could not create Fox Taming entity '{code}'.");
        }

        fox.Pos.X = spawnPosition.X;
        fox.Pos.Y = spawnPosition.Y;
        fox.Pos.Z = spawnPosition.Z;
        fox.Pos.Yaw = player.Entity.Pos.Yaw;
        fox.PositionBeforeFalling.Set(spawnPosition.X, spawnPosition.Y, spawnPosition.Z);

        ITreeAttribute status = GetDomesticationStatus(fox, true)!;
        status.SetString("owner", player.PlayerUID);
        status.SetString("domesticationLevel", "DOMESTICATED");
        status.SetFloat("obedience", 1f);
        status.SetInt("generation", 0);
        status.SetString(ActivityModeKey, CompanionActivityMode.AtEase);
        status.SetString(CombatStyleKey, CompanionCombatStyle.Passive);
        status.SetString(RiskToleranceKey, CompanionRiskTolerance.Steady);

        serverApi.World.SpawnEntity(fox);
        RegisterLoadedFox(fox);
        RegisterFoxInPack(fox);
        packRepository?.Save();

        return TextCommandResult.Success(
            $"Spawned owned tamed Fox Taming fox: {code}."
        );
    }

    private TextCommandResult OnCompanionSpawnGuideCommand(TextCommandCallingArgs args)
    {
        if (serverApi == null || args.Caller.Player is not IServerPlayer player || player.Entity == null)
        {
            return TextCommandResult.Error("This command can only be used by a player in a loaded world.");
        }

        if (!CanUseDeveloperTools(player))
        {
            return TextCommandResult.Error(
                "The guide fox test spawn is only available in Creative mode or to server administrators."
            );
        }

        const string code = "feralkinship:tamefox-red-male";
        EntityProperties? properties = serverApi.World.GetEntityType(new AssetLocation(code));
        if (properties == null)
        {
            return TextCommandResult.Error(
                $"Guide fox entity '{code}' is not registered. The Feral Kinship core mod is required."
            );
        }

        Vec3d? spawnPosition = FindSafeEntityPosition(
            player.Entity.Pos.AsBlockPos,
            properties,
            null,
            6,
            4
        );
        if (spawnPosition == null)
        {
            return TextCommandResult.Error("No safe space was found near you for the guide fox.");
        }

        Entity? fox = serverApi.World.ClassRegistry.CreateEntity(properties);
        if (fox == null)
        {
            return TextCommandResult.Error($"Could not create guide fox entity '{code}'.");
        }

        fox.Pos.X = spawnPosition.X;
        fox.Pos.Y = spawnPosition.Y;
        fox.Pos.Z = spawnPosition.Z;
        fox.Pos.Yaw = player.Entity.Pos.Yaw;
        fox.PositionBeforeFalling.Set(spawnPosition.X, spawnPosition.Y, spawnPosition.Z);

        ITreeAttribute status = GetDomesticationStatus(fox, true)!;
        status.SetBool(GuideFoxKey, true);
        status.SetBool(AutoNameHandledKey, true);
        status.SetString("owner", player.PlayerUID);
        status.SetString("domesticationLevel", "DOMESTICATED");
        status.SetFloat("obedience", 1f);
        status.SetString(ActivityModeKey, CompanionActivityMode.Follow);
        status.SetString(CombatStyleKey, CompanionCombatStyle.Passive);
        status.SetString(RiskToleranceKey, CompanionRiskTolerance.Fearless);

        TreeAttribute nameTag = new();
        nameTag.SetString("name", GuideFoxName);
        fox.WatchedAttributes.SetAttribute("nametag", nameTag);

        serverApi.World.SpawnEntity(fox);

        return TextCommandResult.Success(
            $"Spawned protected guide fox '{GuideFoxName}'. Empty-hand interact toggles Follow and Stay."
        );
    }

    private TextCommandResult OnBrambleDeveloperCommand(TextCommandCallingArgs args)
    {
        if (serverApi == null || brambleRepository?.Loaded != true
            || args.Caller.Player is not IServerPlayer player || player.Entity == null)
            return TextCommandResult.Error("This command requires a loaded player and world.");
        if (!CanUseDeveloperTools(player))
            return TextCommandResult.Error("Bramble test controls are available only in Creative mode or to server administrators.");

        string action = args.Parsers.Count > 0 && !args.Parsers[0].IsMissing
            ? ((string?)args[0] ?? string.Empty).Trim().ToLowerInvariant()
            : "status";
        string value = args.Parsers.Count > 1 && !args.Parsers[1].IsMissing
            ? ((string?)args[1] ?? string.Empty).Trim().ToLowerInvariant()
            : string.Empty;
        BramblePlayerRecord state = brambleRepository.GetOrCreate(player.PlayerUID);

        switch (action)
        {
            case "status":
                return TextCommandResult.Success(
                    $"Bramble: onboarding={state.OnboardingState}, species={state.SpeciesId}, chapter={(state.FirstCompanionOwned ? "universal" : "first-taming")}, hints={state.HintMode}, mode={(state.Follow ? "follow" : "stay")}, entity={state.CurrentEntityId}.");
            case "reset":
                Entity[] oldBrambles = serverApi.World.LoadedEntities.Values
                    .Concat(loadedBrambles.Values)
                    .Where(entity =>
                    {
                        if (!IsGuideFox(entity)) return false;
                        string ownerUid = GetCompanionOwnerUid(entity);
                        return string.IsNullOrWhiteSpace(ownerUid)
                            || string.Equals(ownerUid, player.PlayerUID, StringComparison.Ordinal);
                    })
                    .GroupBy(entity => entity.EntityId)
                    .Select(group => group.First())
                    .ToArray();
                brambleRepository.Reset(player.PlayerUID);
                state = brambleRepository.GetOrCreate(player.PlayerUID);
                foreach (Entity old in oldBrambles)
                {
                    if (old.Alive) old.Die(EnumDespawnReason.Removed);
                }
                EnsureBrambleForPlayer(player, true);
                brambleRepository.Save();
                SendBrambleState(player, showOnboarding: true);
                return TextCommandResult.Success("Bramble onboarding, form, milestones, preferences, and materialized instance were reset for this player.");
            case "offer":
                state.OnboardingState = BrambleOnboardingState.Unresolved;
                state.SpeciesChosen = false;
                brambleRepository.Save();
                SendBrambleState(player, showOnboarding: true);
                return TextCommandResult.Success("Bramble onboarding offer reopened.");
            case "species":
                if (!BrambleTutorialSpeciesIds.Contains(value, StringComparer.Ordinal))
                    return TextCommandResult.Error("Species must be bear, chicken, deer, fox, gazelle, goat, hare, hyena, pig, raccoon, sheep, or wolf.");
                HandleBrambleSpeciesChoice(player, state, value);
                return TextCommandResult.Success($"Bramble form selection requested: {value}.");
            case "chapter":
                if (value is not ("taming" or "universal")) return TextCommandResult.Error("Use /bramble chapter taming|universal.");
                state.FirstCompanionOwned = value == "universal";
                brambleRepository.Save();
                SendBrambleState(player);
                return TextCommandResult.Success($"Bramble test chapter set to {value}.");
            case "hints":
                if (!BrambleHintMode.IsValid(value)) return TextCommandResult.Error("Use /bramble hints quiet|occasional|frequent.");
                state.HintMode = BrambleHintMode.Normalize(value);
                state.LastHintUtcMs = 0;
                brambleRepository.Save();
                SendBrambleState(player);
                return TextCommandResult.Success($"Bramble hint mode set to {value}; hint cooldown reset.");
            case "recommend":
                string[] recommendations = { "clear", "mortal", "wounded", "request", "hungry", "juveniles_only", "never_requested", "talents", "pack_unseen", "no_companion" };
                if (!recommendations.Contains(value, StringComparer.Ordinal))
                    return TextCommandResult.Error("Use clear, mortal, wounded, request, hungry, juveniles_only, never_requested, talents, pack_unseen, or no_companion.");
                if (value == "clear") brambleRecommendationOverrides.Remove(player.PlayerUID);
                else brambleRecommendationOverrides[player.PlayerUID] = value;
                SendBrambleState(player);
                return TextCommandResult.Success(value == "clear" ? "Recommendation override cleared." : $"Recommendation override set to {value}.");
            case "greet":
                Entity? bramble = EnsureBrambleForPlayer(player, true);
                if (bramble == null) return TextCommandResult.Error("Bramble could not be materialized safely.");
                TryEmitBrambleGreeting(bramble, player, state, true);
                return TextCommandResult.Success("Forced the safe nearby Bramble-to-Bramble greeting check.");
            case "taxes":
                Entity? taxBramble = EnsureBrambleForPlayer(player, true);
                if (taxBramble == null) return TextCommandResult.Error("Bramble could not be materialized safely.");
                EmitBrambleLine(taxBramble, player, "bramble.easteregg.taxes.01", CompanionDialoguePriority.Low);
                return TextCommandResult.Success("Played the optional tax Easter egg once.");
            case "summon":
                state.Follow = true;
                state.EntityToken = Guid.NewGuid().ToString("N");
                state.CurrentEntityId = 0;
                Entity? summoned = SpawnBramble(player, state);
                brambleRepository.Save();
                return summoned == null ? TextCommandResult.Error("No safe Bramble spawn position was found.") : TextCommandResult.Success("Bramble was safely materialized nearby in Follow mode; stale instances will self-remove if loaded.");
            default:
                return TextCommandResult.Error("Use /bramble status|reset|offer|species|chapter|hints|recommend|greet|taxes|summon [value].");
        }
    }

    private TextCommandResult OnCompanionExpeditionDeveloperCommand(TextCommandCallingArgs args)
    {
        if (serverApi == null || packRepository?.Loaded != true
            || args.Caller.Player is not IServerPlayer player)
        {
            return TextCommandResult.Error("This command can only be used by a player in a loaded world.");
        }
        if (!CanUseDeveloperTools(player))
        {
            return TextCommandResult.Error(
                "Expedition developer controls are available only in Creative mode or to server administrators.");
        }

        string action = CommandWord(args, 0, "inspect").ToLowerInvariant();
        string value = CommandWord(args, 1, string.Empty).ToLowerInvariant();
        if (action == "inspect")
        {
            IReadOnlyList<FoxExpeditionRecord> active = packRepository.GetExpeditions(player.PlayerUID);
            if (!long.TryParse(value, out long requestedId))
            {
                return active.Count == 0
                    ? TextCommandResult.Success("No active expedition exists for this pack.")
                    : TextCommandResult.Success(string.Join(" | ", active.Select(DescribeActiveExpeditionDebug)));
            }
            FoxExpeditionRecord? expedition = packRepository.GetExpedition(requestedId);
            return expedition == null || !string.Equals(expedition.OwnerUid, player.PlayerUID, StringComparison.Ordinal)
                ? TextCommandResult.Error($"Active expedition #{requestedId} was not found for this pack.")
                : TextCommandResult.Success(DescribeActiveExpeditionDebug(expedition));
        }
        if (action == "complete")
        {
            IReadOnlyList<FoxExpeditionRecord> active = packRepository.GetExpeditions(player.PlayerUID);
            FoxExpeditionRecord? expedition = long.TryParse(value, out long requestedId)
                ? packRepository.GetExpedition(requestedId)
                : active.FirstOrDefault();
            if (expedition == null || !string.Equals(expedition.OwnerUid, player.PlayerUID, StringComparison.Ordinal))
                return TextCommandResult.Error("Choose an active expedition ID from /companionexpedition inspect.");
            if (!packRepository.MakeExpeditionDue(
                    expedition.ExpeditionId,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    serverApi.World.Calendar.TotalHours))
                return TextCommandResult.Error("That expedition could not be marked ready to complete.");
            packRepository.Save();
            return TextCommandResult.Success(
                $"Expedition #{expedition.ExpeditionId} is due now and will resolve on the next server expedition tick.");
        }
        if (action != "simulate")
        {
            return TextCommandResult.Error(
                "Use /companionexpedition inspect [id], complete [id], or simulate <route> <raw|all> <party> <runs> <risk> [event|injury|mortal|mia|cargo].");
        }

        FoxExpeditionDefinition? definition = FoxExpeditionCatalog.Get(value);
        if (definition == null || !definition.ProducesRandomLoot)
            return TextCommandResult.Error("Simulation route must be a loot-bearing expedition route ID.");

        string rawText = CommandWord(args, 2, "1");
        int partySize = ParseInt(CommandWord(args, 3, definition.MinimumFoxes.ToString()), definition.MinimumFoxes, 1, 64);
        int runs = ParseInt(CommandWord(args, 4, "10000"), 10000, 1, 100000);
        float riskScale = ParseFloat(CommandWord(args, 5, "1"), 1f, 0f, 10f);
        string forced = CommandWord(args, 6, string.Empty).ToLowerInvariant();
        float[] rawValues = rawText.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? new[] { 0.50f, 0.75f, 1f, 1.25f, 1.50f, 2f, 3f, 5f }
            : new[] { ParseFloat(rawText, 1f, 0f, 20f) };
        List<string> summaries = new();
        foreach (float rawCompletion in rawValues)
        {
            summaries.Add(SimulateExpeditionStories(
                definition,
                rawCompletion,
                partySize,
                runs,
                riskScale,
                forced));
        }
        return TextCommandResult.Success(string.Join(" | ", summaries));
    }

    private static string CommandWord(TextCommandCallingArgs args, int index, string fallback) =>
        args.Parsers.Count > index && !args.Parsers[index].IsMissing
            ? ((string?)args[index] ?? fallback).Trim()
            : fallback;

    private static int ParseInt(string text, int fallback, int minimum, int maximum) =>
        int.TryParse(text, out int value) ? Math.Clamp(value, minimum, maximum) : fallback;

    private static float ParseFloat(string text, float fallback, float minimum, float maximum) =>
        float.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;

    private static string DescribeActiveExpeditionDebug(FoxExpeditionRecord expedition)
    {
        float raw = ExpeditionCalculations.GetRawCompletion(
            expedition.ExpeditionStrength,
            expedition.TargetStrength);
        ExpeditionStoryState? story = expedition.Story;
        return $"#{expedition.ExpeditionId} {expedition.Type}: raw={raw:P1}, completion={Math.Min(raw, 1f):P1}, "
            + $"overcap={(story?.OvercapRewardFactor ?? ExpeditionCalculations.GetOvercapRewardFactor(raw)):0.###}, "
            + $"risk={story?.EffectiveRiskMultiplier ?? 0f:0.###}, profiles={story?.PrimaryProfile ?? "legacy"}/{story?.SecondaryProfile ?? "-"}, "
            + $"events={(story == null ? "legacy-unresolved" : string.Join(",", story.Events.Select(item => $"{item.Id}:{item.Outcome}")))}.";
    }

    private static string SimulateExpeditionStories(
        FoxExpeditionDefinition definition,
        float rawCompletion,
        int partySize,
        int runs,
        float riskScale,
        string forced)
    {
        int opportunityRuns = 0;
        int hazardRuns = 0;
        int injuries = 0;
        int mortal = 0;
        int mia = 0;
        int cargo = 0;
        int minor = 0;
        int moderate = 0;
        int major = 0;
        double ordinaryRolls = 0d;
        double goalMultiplier = 0d;
        double specialRolls = 0d;
        double debrisRolls = 0d;
        double vesselRolls = 0d;
        string forceEvent = ExpeditionEventRegistry.Get(forced) != null ? forced : string.Empty;
        string forceConsequence = string.IsNullOrWhiteSpace(forceEvent) ? forced : string.Empty;
        Random random = new(HashCode.Combine(definition.Id, rawCompletion, partySize, runs, riskScale, forced));
        List<string> party = Enumerable.Range(1, partySize).Select(index => $"sim-{index}").ToList();
        for (int run = 0; run < runs; run++)
        {
            FoxExpeditionRecord expedition = new()
            {
                Type = definition.Id,
                SelectedFoxIds = party,
                TargetStrength = definition.TargetStrength,
                ExpeditionStrength = definition.TargetStrength * rawCompletion,
                BaseDurationHours = definition.BaseDurationHours
            };
            ExpeditionStoryState story = ExpeditionStoryResolver.Resolve(
                expedition,
                definition,
                random,
                debug: new ExpeditionStoryDebugOptions
                {
                    RiskScale = riskScale,
                    ForceEventId = forceEvent,
                    ForceConsequence = forceConsequence
                });
            if (story.Events.Any(item => item.Category is "opportunity" or "mixed")) opportunityRuns++;
            if (story.Events.Any(item => item.Category == "hazard")) hazardRuns++;
            injuries += story.MemberOutcomes.Count(item => item.Status == "Returned injured");
            mortal += story.MemberOutcomes.Count(item => item.Status == "Mortally wounded");
            mia += story.MemberOutcomes.Count(item => item.Status == "MIA");
            if (story.CargoState == "damaged") cargo++;
            minor += story.Events.Count(item => item.Severity == "minor");
            moderate += story.Events.Count(item => item.Severity == "moderate");
            major += story.Events.Count(item => item.Severity == "major");
            ordinaryRolls += story.RewardModifiers.AdditionalOrdinaryRolls;
            goalMultiplier += story.RewardModifiers.GoalQuantityMultiplier;
            specialRolls += story.RewardModifiers.AdditionalSpecialRolls;
            debrisRolls += story.RewardModifiers.AdditionalDebrisRolls;
            vesselRolls += story.RewardModifiers.AdditionalVesselRolls;
        }
        float overcap = ExpeditionCalculations.GetOvercapRewardFactor(rawCompletion);
        double ancientExpected = definition.AncientFindRolls
            * definition.AncientFindChance
            * Math.Clamp(rawCompletion, 0f, 1f);
        return $"{definition.Id} raw={rawCompletion:P0} completion={Math.Min(rawCompletion, 1f):P0} overcap={overcap:0.###} n={runs}: "
            + $"positive={opportunityRuns * 100d / runs:0.0}%, hazard={hazardRuns * 100d / runs:0.0}%, "
            + $"injury={injuries * 100d / runs:0.00}%, mortal={mortal * 100d / runs:0.00}%, MIA={mia * 100d / runs:0.00}%, cargo={cargo * 100d / runs:0.00}%, "
            + $"tiers/run={minor / (double)runs:0.00}/{moderate / (double)runs:0.00}/{major / (double)runs:0.00}, "
            + $"extra ordinary={ordinaryRolls / runs:0.00}, goal x={goalMultiplier / runs:0.000}, special={specialRolls / runs:0.00}, debris={debrisRolls / runs:0.00}, vessel={vesselRolls / runs:0.00}, ancient expected={ancientExpected:0.00}.";
    }
}
