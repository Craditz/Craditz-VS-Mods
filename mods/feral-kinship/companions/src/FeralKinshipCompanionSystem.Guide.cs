#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FeralKinship;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    internal enum GuideAdviceAction
    {
        None,
        TreatmentHelp,
        OpenCompanion,
        OpenSocial,
        OpenTalents,
        OpenPack,
        OpenTamingGuide
    }

    internal sealed class GuideAdviceClient
    {
        public string Id { get; init; } = string.Empty;
        public string Heading { get; init; } = string.Empty;
        public string Body { get; init; } = string.Empty;
        public string[] Steps { get; init; } = Array.Empty<string>();
        public string ActionLabel { get; init; } = string.Empty;
        public GuideAdviceAction Action { get; init; }
        public long TargetEntityId { get; init; }
        public bool HasAction => Action != GuideAdviceAction.None;
    }

    private sealed class BrambleRecommendation
    {
        public string Id = string.Empty;
        public string Heading = string.Empty;
        public string Body = string.Empty;
        public string[] Steps = Array.Empty<string>();
        public GuideAdviceAction Action;
        public long TargetEntityId;
        public string TargetFoxId = string.Empty;
        public string TargetName = string.Empty;
        public int Priority = CompanionDialoguePriority.Low;
    }

    private const string GuideFoxKey = "feralKinshipGuideFox";
    private const string BrambleTokenKey = "feralKinshipBrambleToken";
    private const string BrambleSpeciesKey = "feralKinshipBrambleSpecies";
    private const string GuideFoxName = "Bramble";
    private const long BrambleOccasionalHintCooldownMs = 20 * 60 * 1000;
    private const long BrambleFrequentHintCooldownMs = 8 * 60 * 1000;
    private const long BrambleAmbientCooldownMs = 90 * 60 * 1000;
    private const long BrambleGreetingCooldownMs = 30 * 60 * 1000;
    private const long BrambleTaxesCooldownMs = 30L * 24 * 60 * 60 * 1000;
    internal const string BrambleBedHomeType = "bed";
    internal const string BramblePackCartHomeType = "packcart";

    internal static readonly string[] BrambleTutorialSpeciesIds =
    {
        "bear", "chicken", "deer", "fox", "gazelle", "goat",
        "hare", "hyena", "pig", "raccoon", "sheep", "wolf"
    };

    private static readonly Dictionary<string, string> BrambleFormCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bear"] = "feralkinship:tamebear-black-male",
        ["chicken"] = "feralkinship:tamechicken-hen",
        ["deer"] = "feralkinship:tamedeer-whitetail-male",
        ["fox"] = "feralkinship:tamefox-red-male",
        ["gazelle"] = "feralkinship:tamegazelle-thomson-male",
        ["goat"] = "feralkinship:tamegoat-angora-male",
        ["hare"] = "feralkinship:tamehare-arctic-male",
        ["hyena"] = "feralkinship:tamehyena-spotted-male",
        ["pig"] = "feralkinship:tamepig-eurasian-adult-male",
        ["raccoon"] = "feralkinship:tameraccoon-common-male",
        ["sheep"] = "feralkinship:tamesheep-bighorn-male",
        ["wolf"] = "feralkinship:tamewolf-eurasian-male"
    };

    private readonly Dictionary<long, Entity> loadedBrambles = new();
    private readonly Dictionary<string, string> brambleRecommendationOverrides = new(StringComparer.Ordinal);
    private readonly BrambleDialogueCatalog brambleDialogue = new();
    private BrambleRepository? brambleRepository;
    private BrambleStatePacket? lastBrambleState;

    internal static bool IsGuideFox(Entity entity)
        => GetDomesticationStatus(entity)?.GetBool(GuideFoxKey, false) == true;

    internal string BrambleLine(string id, string fallback = "") => brambleDialogue.Line(id, fallback);
    internal IReadOnlyList<BrambleDialogueEntry> BrambleLines(string prefix) => brambleDialogue.WithPrefix(prefix);

    private void LoadBrambleDialogue(ICoreAPI api)
    {
        if (brambleDialogue.Count == 0) brambleDialogue.Load(api);
    }

    internal void RegisterLoadedBramble(Entity entity)
    {
        if (serverApi == null || brambleRepository?.Loaded != true || !IsGuideFox(entity)) return;
        if (!BrambleEnabled)
        {
            loadedBrambles.Remove(entity.EntityId);
            if (entity.Alive) entity.Die(EnumDespawnReason.Removed);
            return;
        }
        string ownerUid = GetCompanionOwnerUid(entity);
        if (string.IsNullOrWhiteSpace(ownerUid))
        {
            entity.Die(EnumDespawnReason.Removed);
            return;
        }

        BramblePlayerRecord record = brambleRepository.GetOrCreate(ownerUid);
        if (record.Dismissed)
        {
            entity.Die(EnumDespawnReason.Removed);
            return;
        }
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        string token = status.GetString(BrambleTokenKey, string.Empty);
        if (string.IsNullOrWhiteSpace(token) && (record.CurrentEntityId == 0 || record.CurrentEntityId == entity.EntityId))
        {
            token = record.EntityToken;
            status.SetString(BrambleTokenKey, token);
        }
        if (!string.Equals(token, record.EntityToken, StringComparison.Ordinal))
        {
            serverApi.Logger.Notification("[FeralKinshipCompanions] Removed stale Bramble instance {0} for {1}.", entity.EntityId, ownerUid);
            entity.Die(EnumDespawnReason.Removed);
            return;
        }

        Entity? previous = record.CurrentEntityId > 0 ? serverApi.World.GetEntityById(record.CurrentEntityId) : null;
        if (previous != null && previous.EntityId != entity.EntityId && IsGuideFox(previous))
        {
            entity.Die(EnumDespawnReason.Removed);
            return;
        }

        loadedBrambles[entity.EntityId] = entity;
        record.CurrentEntityId = entity.EntityId;
        ObserveBramblePosition(record, entity);
        NormalizeBrambleEntity(entity, record);
        brambleRepository.Save();
    }

    internal void UnregisterLoadedBramble(Entity entity)
    {
        loadedBrambles.Remove(entity.EntityId);
        if (brambleRepository?.Loaded != true) return;
        string ownerUid = GetCompanionOwnerUid(entity);
        if (!brambleRepository.TryGet(ownerUid, out BramblePlayerRecord record)) return;
        ObserveBramblePosition(record, entity);
        if (record.CurrentEntityId == entity.EntityId) record.CurrentEntityId = 0;
    }

    private static void ObserveBramblePosition(BramblePlayerRecord record, Entity entity)
    {
        record.LastKnownX = (int)Math.Floor(entity.Pos.X);
        record.LastKnownY = (int)Math.Floor(entity.Pos.InternalY);
        record.LastKnownZ = (int)Math.Floor(entity.Pos.Z);
        record.LastKnownDimension = entity.Pos.Dimension;
        record.HasLastKnownPosition = true;
    }

    private static void SetBrambleName(Entity entity)
    {
        ITreeAttribute? existing = entity.WatchedAttributes.GetTreeAttribute("nametag");
        if (string.Equals(existing?.GetString("name", string.Empty), GuideFoxName, StringComparison.Ordinal)) return;
        TreeAttribute nameTag = new();
        nameTag.SetString("name", GuideFoxName);
        entity.WatchedAttributes.SetAttribute("nametag", nameTag);
        entity.WatchedAttributes.MarkPathDirty("nametag");
    }

    private static void NormalizeBrambleEntity(Entity entity, BramblePlayerRecord record)
    {
        ApplyBrambleIdentity(entity, record);
        EntityBehaviorHealth? health = entity.GetBehavior<EntityBehaviorHealth>();
        if (health != null && health.Health < health.MaxHealth) health.Health = health.MaxHealth;
    }

    private static void ApplyBrambleIdentity(Entity entity, BramblePlayerRecord record)
    {
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        bool dirty = false;
        void SetBool(string key, bool value) { if (status.GetBool(key, !value) == value) return; status.SetBool(key, value); dirty = true; }
        void SetFloat(string key, float value) { if (Math.Abs(status.GetFloat(key, value + 1f) - value) < 0.0001f) return; status.SetFloat(key, value); dirty = true; }
        void SetString(string key, string value) { if (string.Equals(status.GetString(key, string.Empty), value, StringComparison.Ordinal)) return; status.SetString(key, value); dirty = true; }
        SetBool(GuideFoxKey, true);
        SetBool(AutoNameHandledKey, true);
        SetString("owner", record.OwnerUid);
        SetString("domesticationLevel", "DOMESTICATED");
        SetFloat("obedience", 1f);
        SetString(BrambleTokenKey, record.EntityToken);
        SetString(BrambleSpeciesKey, record.SpeciesId);
        bool follow = record.Follow && string.IsNullOrWhiteSpace(record.HomeType);
        SetString(ActivityModeKey, follow ? CompanionActivityMode.Follow : CompanionActivityMode.AtEase);
        SetString(CombatStyleKey, CompanionCombatStyle.Passive);
        SetString(RiskToleranceKey, CompanionRiskTolerance.Cautious);
        SetString(FollowDistanceKey, CompanionFollowDistance.Close);
        if (dirty) entity.WatchedAttributes.MarkPathDirty(DomesticationStatusPath);
        SetBrambleName(entity);
    }

    private Entity? EnsureBrambleForPlayer(IServerPlayer player, bool forceNearPlayer = false)
    {
        if (serverApi == null || !BrambleEnabled || brambleRepository?.Loaded != true || player.Entity == null) return null;
        BramblePlayerRecord record = brambleRepository.GetOrCreate(player.PlayerUID);
        if (record.Dismissed) return null;
        Entity? existing = record.CurrentEntityId > 0 ? serverApi.World.GetEntityById(record.CurrentEntityId) : null;
        if (existing != null && existing.Alive && IsGuideFox(existing))
        {
            NormalizeBrambleEntity(existing, record);
            return existing;
        }
        Entity? loaded = loadedBrambles.Values.FirstOrDefault(entity =>
            entity.Alive
            && IsGuideFox(entity)
            && string.Equals(GetCompanionOwnerUid(entity), player.PlayerUID, StringComparison.Ordinal));
        if (loaded != null)
        {
            record.CurrentEntityId = loaded.EntityId;
            NormalizeBrambleEntity(loaded, record);
            return loaded;
        }
        // A home assignment is a respawn anchor, not merely an AI preference.
        // The old last-known-position guard left Bramble absent after a relog
        // or player death whenever the home-mode entity had been unloaded.
        if (!record.Follow
            && string.IsNullOrWhiteSpace(record.HomeType)
            && !forceNearPlayer
            && record.HasLastKnownPosition) return null;
        record.EntityToken = Guid.NewGuid().ToString("N");
        record.CurrentEntityId = 0;
        Entity? spawned = SpawnBramble(player, record);
        brambleRepository.Save();
        return spawned;
    }

    private Entity? SpawnBramble(IServerPlayer owner, BramblePlayerRecord record)
    {
        if (serverApi == null || owner.Entity == null) return null;
        if (!BrambleFormCodes.TryGetValue(record.SpeciesId, out string? code)) code = BrambleFormCodes["fox"];
        EntityProperties? properties = serverApi.World.GetEntityType(new AssetLocation(code));
        if (properties == null)
        {
            serverApi.Logger.Error("[FeralKinshipCompanions] Bramble form entity '{0}' is not registered.", code);
            return null;
        }
        BlockPos spawnAnchor = owner.Entity.Pos.AsBlockPos;
        bool spawningAtHome = TryGetBrambleHomeSpawnAnchor(ownerUid: owner.PlayerUID, record, out BlockPos homeAnchor);
        if (spawningAtHome) spawnAnchor = homeAnchor;

        Vec3d? spawnPosition = FindSafeEntityPosition(
            spawnAnchor,
            properties,
            null,
            spawningAtHome ? 3 : 7,
            spawningAtHome ? 3 : 5);
        if (spawnPosition == null && spawningAtHome)
        {
            double homeHeight = string.Equals(record.HomeType, BramblePackCartHomeType, StringComparison.Ordinal)
                ? 1.22
                : 0.35;
            spawnPosition = new Vec3d(
                homeAnchor.X + 0.5,
                homeAnchor.Y + homeAnchor.dimension * BlockPos.DimensionBoundary + homeHeight,
                homeAnchor.Z + 0.5);
        }
        if (spawnPosition == null)
        {
            serverApi.Logger.Warning("[FeralKinshipCompanions] No safe Bramble spawn position was found near {0}.", owner.PlayerUID);
            return null;
        }
        Entity? entity = serverApi.World.ClassRegistry.CreateEntity(properties);
        if (entity == null) return null;
        entity.Pos.X = spawnPosition.X;
        entity.Pos.Y = spawnPosition.Y;
        entity.Pos.Z = spawnPosition.Z;
        entity.Pos.Yaw = owner.Entity.Pos.Yaw;
        entity.PositionBeforeFalling.Set(spawnPosition.X, spawnPosition.Y, spawnPosition.Z);
        // World.SpawnEntity invokes entity lifecycle callbacks immediately.
        // Mark this as Bramble first so the ordinary tamed-companion
        // registration cannot claim it as an ownerless fox.
        ApplyBrambleIdentity(entity, record);
        serverApi.World.SpawnEntity(entity);
        RegisterLoadedBramble(entity);
        return entity;
    }

    private bool TryGetBrambleHomeSpawnAnchor(string ownerUid, BramblePlayerRecord record, out BlockPos anchor)
    {
        anchor = new BlockPos(record.HomeX, record.HomeY, record.HomeZ, record.HomeDimension);
        if (string.Equals(record.HomeType, BrambleBedHomeType, StringComparison.Ordinal)) return true;
        if (!string.Equals(record.HomeType, BramblePackCartHomeType, StringComparison.Ordinal)) return false;

        // Older Bramble records only stored the mode. Prefer the live cart when
        // available so those records can still recover to the correct home.
            BlockPos? activeCart = GetActiveCairnPosition(ownerUid, discoverNearby: true);
        if (activeCart != null)
        {
            anchor = activeCart;
        }
        return true;
    }

    private Entity? ReplaceBrambleForm(IServerPlayer owner, BramblePlayerRecord record)
    {
        if (serverApi == null) return null;
        Entity? old = record.CurrentEntityId > 0 ? serverApi.World.GetEntityById(record.CurrentEntityId) : null;
        string oldToken = record.EntityToken;
        record.EntityToken = Guid.NewGuid().ToString("N");
        record.CurrentEntityId = 0;
        Entity? replacement = SpawnBramble(owner, record);
        if (replacement == null)
        {
            record.EntityToken = oldToken;
            if (old != null && old.Alive) record.CurrentEntityId = old.EntityId;
            return null;
        }
        if (old != null && old.Alive && old.EntityId != replacement.EntityId) old.Die(EnumDespawnReason.Removed);
        return replacement;
    }

    private void ToggleGuideActivity(Entity entity, IServerPlayer player)
    {
        if (brambleRepository?.Loaded != true || !IsGuideFox(entity)
            || !string.Equals(GetCompanionOwnerUid(entity), player.PlayerUID, StringComparison.Ordinal)) return;
        BramblePlayerRecord record = brambleRepository.GetOrCreate(player.PlayerUID);
        record.Follow = !record.Follow;
        if (record.Follow)
        {
            ClearBrambleHome(record);
        }
        else if (TryGetPackCartIdleTarget(entity, out _)
            && GetActiveCairnPosition(player.PlayerUID) is BlockPos cart)
        {
            record.HomeType = BramblePackCartHomeType;
            record.HomeX = cart.X;
            record.HomeY = cart.Y;
            record.HomeZ = cart.Z;
            record.HomeDimension = cart.dimension;
        }
        else
        {
            record.HomeType = string.Empty;
        }
        NormalizeBrambleEntity(entity, record);
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        status.SetLong(ActivityStartedUtcMsKey, UtcNowMs());
        status.SetLong(ActivityArrivedUtcMsKey, 0);
        entity.WatchedAttributes.MarkPathDirty(DomesticationStatusPath);
        if (entity is EntityAgent agent)
        {
            agent.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.StopTasks();
            agent.Controls.StopAllMovement();
            entity.Pos.Motion.Set(0, 0, 0);
        }
        brambleRepository.Save();
        EmitBrambleLine(entity, player, record.Follow ? "bramble.mode.follow.01" : "bramble.mode.stay.01", CompanionDialoguePriority.Low);
    }

    private void SyncBrambleActivityCommand(Entity entity, IServerPlayer player, string mode)
    {
        if (serverApi == null || brambleRepository?.Loaded != true || !IsGuideFox(entity)
            || !string.Equals(GetCompanionOwnerUid(entity), player.PlayerUID, StringComparison.Ordinal)) return;

        BramblePlayerRecord record = brambleRepository.GetOrCreate(player.PlayerUID);
        if (record.Dismissed) return;

        // This is only a compatibility sync for generic activity packets.
        // Bramble's actual player-facing control is his crouch-interact
        // Follow/Stay toggle, while ordinary Companions use At Ease.
        record.Follow = string.Equals(mode, CompanionActivityMode.Follow, StringComparison.Ordinal);
        if (record.Follow)
        {
            ClearBrambleHome(record);
        }
        else if (string.Equals(mode, CompanionActivityMode.AtEase, StringComparison.Ordinal)
            && !string.Equals(record.HomeType, BrambleBedHomeType, StringComparison.Ordinal)
            && GetActiveCairnPosition(player.PlayerUID, discoverNearby: true) is BlockPos cart)
        {
            record.HomeType = BramblePackCartHomeType;
            record.HomeX = cart.X;
            record.HomeY = cart.Y;
            record.HomeZ = cart.Z;
            record.HomeDimension = cart.dimension;
        }

        brambleRepository.Save();
    }

    internal void UpdateGuideFox(Entity entity, float dt)
    {
        if (serverApi == null || brambleRepository?.Loaded != true || !IsGuideFox(entity) || !entity.Alive) return;
        if (!BrambleEnabled)
        {
            loadedBrambles.Remove(entity.EntityId);
            entity.Die(EnumDespawnReason.Removed);
            return;
        }
        string ownerUid = GetCompanionOwnerUid(entity);
        if (serverApi.World.PlayerByUid(ownerUid) is not IServerPlayer owner) return;
        BramblePlayerRecord record = brambleRepository.GetOrCreate(ownerUid);
        if (record.Dismissed)
        {
            loadedBrambles.Remove(entity.EntityId);
            entity.Die(EnumDespawnReason.Removed);
            return;
        }
        if (!string.Equals(GetDomesticationStatus(entity)?.GetString(BrambleTokenKey, string.Empty), record.EntityToken, StringComparison.Ordinal))
        {
            entity.Die(EnumDespawnReason.Removed);
            return;
        }
        NormalizeBrambleEntity(entity, record);
        ObserveBramblePosition(record, entity);
        UpdateBrambleMilestones(record);
        if (!TryEmitBrambleFirstTamingHint(entity, owner, record)
            && !TryEmitBrambleNearbyTargetHint(entity, owner, record))
        {
            TryEmitBrambleHint(entity, owner, record);
        }
        TryEmitBrambleGreeting(entity, owner, record, false);
        TryEmitBrambleAmbient(entity, owner, record);
    }

    private void UpdateBrambleMilestones(BramblePlayerRecord record)
    {
        if (packRepository?.Loaded != true) return;
        List<FoxPackRecordV2> companions = CurrentBrambleCompanionRecords(record.OwnerUid);
        if (companions.Count > 0) record.FirstCompanionOwned = true;
        if (companions.Any(companion => companion.RequestsCompleted > 0)) record.FirstRequestCompleted = true;
    }

    private List<FoxPackRecordV2> CurrentBrambleCompanionRecords(string ownerUid)
        => packRepository?.GetRecordsForOwner(ownerUid)
            .Where(record => !record.Archived && !string.Equals(record.Status, "Dead", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(record => serverApi?.World.GetEntityById(record.EntityId) != null)
            .ThenBy(record => record.Number).ToList() ?? new List<FoxPackRecordV2>();

    private BrambleRecommendation EvaluateBrambleRecommendation(BramblePlayerRecord state)
    {
        List<FoxPackRecordV2> companions = CurrentBrambleCompanionRecords(state.OwnerUid);
        if (brambleRecommendationOverrides.TryGetValue(state.OwnerUid, out string? forced) && !string.IsNullOrWhiteSpace(forced))
            return BuildForcedBrambleRecommendation(forced, companions.FirstOrDefault(), state);

        FoxPackRecordV2? mortal = companions.FirstOrDefault(record => record.HealthState == MortallyWoundedHealthState);
        if (mortal != null) return RecommendationForRecord("mortal", mortal, GuideAdviceAction.TreatmentHelp, CompanionDialoguePriority.Critical);
        FoxPackRecordV2? wounded = companions.FirstOrDefault(record => record.HealthState == RecoveringHealthState
            || record.CurrentHealth > 0f && record.MaxHealth > 0f && record.CurrentHealth / record.MaxHealth <= 0.35f);
        if (wounded != null) return RecommendationForRecord("wounded", wounded, GuideAdviceAction.TreatmentHelp, CompanionDialoguePriority.Critical);
        FoxPackRecordV2? request = companions.Where(record => !string.IsNullOrWhiteSpace(record.ActiveRequestId)
                || !string.IsNullOrWhiteSpace(record.ActiveRequest) && !string.Equals(record.ActiveRequest, "None", StringComparison.OrdinalIgnoreCase))
            .OrderBy(record => record.RequestExpiresUtcMs <= 0 ? long.MaxValue : record.RequestExpiresUtcMs).FirstOrDefault();
        if (request != null) return RecommendationForRecord("request", request, GuideAdviceAction.OpenSocial, CompanionDialoguePriority.High);
        FoxPackRecordV2? hungry = companions.Where(record => record.FoodStateInitialized && record.FoodLevel < 0.25f)
            .OrderBy(record => record.FoodLevel).FirstOrDefault();
        if (hungry != null) return RecommendationForRecord("hungry", hungry, GuideAdviceAction.OpenCompanion, CompanionDialoguePriority.High);
        FoxPackRecordV2? adult = companions.FirstOrDefault(record => !record.IsJuvenile);
        if (adult != null && !state.FirstRequestCompleted)
            return RecommendationForRecord("never_requested", adult, GuideAdviceAction.OpenSocial, CompanionDialoguePriority.Medium);
        if (adult != null && !state.TalentsExplored)
            return RecommendationForRecord("talents", adult, GuideAdviceAction.OpenTalents, CompanionDialoguePriority.Medium);
        if (companions.Count > 0 && !state.PackExplored)
            return RecommendationForRecord("pack_unseen", companions[0], GuideAdviceAction.OpenPack, CompanionDialoguePriority.Medium);
        if (companions.Count > 0 && !state.PackCartExplored)
            return SimpleRecommendation("pack_cart", "Set up your Pack Cart", GuideAdviceAction.None, CompanionDialoguePriority.Medium);
        if (companions.Count > 0 && companions.All(record => record.IsJuvenile))
            return SimpleRecommendation("juveniles_only", "Young Companions need time", GuideAdviceAction.None, CompanionDialoguePriority.Medium);
        if (companions.Count == 0)
        {
            if (serverApi?.World.PlayerByUid(state.OwnerUid) is IServerPlayer owner
                && FindNearbyWildTutorialTarget(owner, state.SpeciesId) != null)
            {
                return SimpleRecommendation("nearby_target", "A possible Companion is nearby", GuideAdviceAction.OpenTamingGuide, CompanionDialoguePriority.Medium, state.SpeciesId);
            }
            return SimpleRecommendation("no_companion", "Find your first Companion", GuideAdviceAction.OpenTamingGuide, CompanionDialoguePriority.Medium, state.SpeciesId);
        }
        return new BrambleRecommendation
        {
            Id = "clear",
            Heading = "Nothing urgent",
            Body = BrambleLine("bramble.ambient.observation.02", "Nothing needs explaining right now."),
            Priority = CompanionDialoguePriority.Low
        };
    }

    private BrambleRecommendation BuildForcedBrambleRecommendation(string forced, FoxPackRecordV2? target, BramblePlayerRecord state)
    {
        forced = forced.Trim().ToLowerInvariant();
        if (forced is "none" or "clear") return new BrambleRecommendation { Id = "clear", Heading = "Nothing urgent", Body = BrambleLine("bramble.ambient.observation.02") };
        if (forced == "no_companion") return SimpleRecommendation(forced, "Find your first Companion", GuideAdviceAction.OpenTamingGuide, CompanionDialoguePriority.Medium, state.SpeciesId);
        if (forced == "juveniles_only") return SimpleRecommendation(forced, "Young Companions need time", GuideAdviceAction.None, CompanionDialoguePriority.Medium);
        target ??= new FoxPackRecordV2 { Name = "Test Companion", FoxId = "bramble-test" };
        return forced switch
        {
            "mortal" => RecommendationForRecord(forced, target, GuideAdviceAction.TreatmentHelp, CompanionDialoguePriority.Critical),
            "wounded" => RecommendationForRecord(forced, target, GuideAdviceAction.TreatmentHelp, CompanionDialoguePriority.Critical),
            "request" => RecommendationForRecord(forced, target, GuideAdviceAction.OpenSocial, CompanionDialoguePriority.High),
            "hungry" => RecommendationForRecord(forced, target, GuideAdviceAction.OpenCompanion, CompanionDialoguePriority.High),
            "never_requested" => RecommendationForRecord(forced, target, GuideAdviceAction.OpenSocial, CompanionDialoguePriority.Medium),
            "talents" => RecommendationForRecord(forced, target, GuideAdviceAction.OpenTalents, CompanionDialoguePriority.Medium),
            "pack_unseen" => RecommendationForRecord(forced, target, GuideAdviceAction.OpenPack, CompanionDialoguePriority.Medium),
            "pack_cart" => SimpleRecommendation(forced, "Set up your Pack Cart", GuideAdviceAction.None, CompanionDialoguePriority.Medium),
            _ => new BrambleRecommendation { Id = "clear", Heading = "Nothing urgent", Body = "Unknown test recommendation override." }
        };
    }

    private BrambleRecommendation RecommendationForRecord(string kind, FoxPackRecordV2 record, GuideAdviceAction action, int priority)
    {
        string name = string.IsNullOrWhiteSpace(record.Name) ? $"Companion #{record.Number}" : record.Name;
        string body = BrambleLine("bramble.next." + kind + ".01", name + " needs your attention.")
            .Replace("{name}", name, StringComparison.Ordinal);
        return new BrambleRecommendation
        {
            Id = kind,
            Heading = kind switch
            {
                "mortal" => "Treat this immediately", "wounded" => "Care comes first", "request" => "A request is waiting",
                "hungry" => "Feed them first", "never_requested" => "Start with the Social tab", "talents" => "A talent point is waiting",
                "pack_unseen" => "Look at Pack status", _ => "What to do next"
            },
            Body = body,
            Steps = RecommendationSteps(kind, name),
            Action = action,
            TargetEntityId = record.EntityId,
            TargetFoxId = record.FoxId,
            TargetName = name,
            Priority = priority
        };
    }

    private BrambleRecommendation SimpleRecommendation(string kind, string heading, GuideAdviceAction action, int priority, string speciesId = "")
    {
        string species = GetBrambleSpeciesDisplayName(speciesId);
        string body = BrambleLine("bramble.next." + kind + ".01", heading).Replace("{species}", species, StringComparison.OrdinalIgnoreCase);
        return new BrambleRecommendation
        {
            Id = kind,
            Heading = heading,
            Body = body,
            Steps = kind == "no_companion"
                ? new[] { $"Look for a wild {species}.", "Hold an accepted taming food in your active hand.", "Use the cautious approach in the taming chapter, then feed it with a normal right-click." }
                : kind == "nearby_target"
                    ? new[] { $"Approach the wild {species} carefully.", "Hold an accepted taming food in your active hand.", "Feed it with a normal right-click until taming begins." }
                    : kind == "pack_cart"
                        ? new[] { "Build a Pack Cart when you are ready.", "Right-click the Pack Cart to open it.", "Use it as the hub for the larger pack systems." }
                    : new[] { "Keep juveniles fed.", "Keep them safe.", "Give them time to grow." },
            Action = action,
            Priority = priority
        };
    }

    private static string[] RecommendationSteps(string kind, string name) => kind switch
    {
        "mortal" => new[] { "Hold a valid healing item such as the appropriate poultice.", $"Ctrl + right-click {name} to begin treatment.", "Keep them safe until the recovering state finishes." },
        "wounded" => new[] { "Stop work and combat.", "Use a healing item and Ctrl + right-click the Companion.", "Resume commands only after recovery." },
        "request" => new[] { $"Right-click {name} with an empty hand.", "Open the Social tab and review the active request.", "Complete it before it expires." },
        "hungry" => new[] { "Hold an accepted Companion food.", $"Right-click {name} with the food to feed them.", "Check hunger before assigning more work." },
        "never_requested" => new[] { $"Right-click {name} with an empty hand.", "Open Social and ask for a request.", "Complete one request to finish this milestone." },
        "talents" => new[] { $"Right-click {name} with an empty hand.", "Open Talents and review the available branches.", "Spend or deliberately bank the point." },
        "pack_unseen" => new[] { $"Right-click {name} with an empty hand.", "Open Pack status and review the roster.", "Then explore camps, duties, carts, and expeditions." },
        _ => Array.Empty<string>()
    };

    private bool TryEmitBrambleFirstTamingHint(Entity bramble, IServerPlayer owner, BramblePlayerRecord state)
    {
        if (state.HintMode == BrambleHintMode.Quiet || state.FirstCompanionOwned || state.FirstTamingStarted) return false;
        Entity? target = FindNearbyTamingTarget(owner, state.SpeciesId);
        if (target == null) return false;

        const string id = "bramble.unprompted.taming_started.01";
        string fallback = BrambleLine(id, "That one has accepted your first offering. Keep the approach calm and keep going.");
        if (!EmitDialogueEvent(bramble, owner, id, fallback, CompanionDialoguePriority.Medium, "bramble.taming")) return false;
        state.FirstTamingStarted = true;
        brambleRepository?.Save();
        return true;
    }

    private bool TryEmitBrambleNearbyTargetHint(Entity bramble, IServerPlayer owner, BramblePlayerRecord state)
    {
        if (state.HintMode == BrambleHintMode.Quiet || state.FirstCompanionOwned) return false;
        Entity? target = FindNearbyWildTutorialTarget(owner, state.SpeciesId);
        if (target == null || target.EntityId == state.LastWildTargetEntityId) return false;

        const string id = "bramble.next.nearby_target.01";
        string species = GetBrambleSpeciesDisplayName(state.SpeciesId);
        string fallback = BrambleLine(id, "There is a {species} nearby. If you were waiting for a candidate, that is one.")
            .Replace("{species}", species, StringComparison.OrdinalIgnoreCase);
        if (!EmitDialogueEvent(bramble, owner, id, fallback, CompanionDialoguePriority.Medium, "bramble.hint")) return false;
        state.LastWildTargetEntityId = target.EntityId;
        brambleRepository?.Save();
        return true;
    }

    private void TryEmitBrambleHint(Entity bramble, IServerPlayer owner, BramblePlayerRecord state)
    {
        if (state.HintMode == BrambleHintMode.Quiet) return;
        long now = UtcNowMs();
        long cooldown = state.HintMode == BrambleHintMode.Frequent ? BrambleFrequentHintCooldownMs : BrambleOccasionalHintCooldownMs;
        if (now - state.LastHintUtcMs < cooldown) return;
        BrambleRecommendation recommendation = EvaluateBrambleRecommendation(state);
        string? hintId = GetTransitionHintId(state, recommendation);
        if (hintId == null) return;
        string fallback = BrambleLine(hintId, recommendation.Body)
            .Replace("{name}", recommendation.TargetName, StringComparison.Ordinal)
            .Replace("{species}", GetBrambleSpeciesDisplayName(state.SpeciesId), StringComparison.OrdinalIgnoreCase);
        if (!EmitDialogueEvent(bramble, owner, hintId, fallback, recommendation.Priority, "bramble.hint")) return;
        if (hintId.EndsWith("first_request.01", StringComparison.Ordinal)) state.SeenMilestones.Add("system:first-request");
        if (hintId.EndsWith("first_talent.01", StringComparison.Ordinal)) state.SeenMilestones.Add("system:first-talent");
        if (hintId.EndsWith("first_pack_ready.01", StringComparison.Ordinal)) state.SeenMilestones.Add("system:first-pack-ready");
        if (hintId.EndsWith("first_expedition_available.01", StringComparison.Ordinal)) state.SeenMilestones.Add("system:first-expedition-available");
        state.LastHintUtcMs = now;
        brambleRepository?.Save();
    }

    private string? GetTransitionHintId(BramblePlayerRecord state, BrambleRecommendation recommendation)
    {
        List<FoxPackRecordV2> companions = CurrentBrambleCompanionRecords(state.OwnerUid);
        FoxPackRecordV2? target = companions.FirstOrDefault(record => string.Equals(record.FoxId, recommendation.TargetFoxId, StringComparison.Ordinal));
        BrambleObservedCompanionState? observed = target == null ? null : GetOrCreateObservedState(state, target);
        string? result = recommendation.Id switch
        {
            "mortal" when observed?.Mortal == false => "bramble.unprompted.mortal_first.01",
            "wounded" when observed?.Wounded == false => "bramble.unprompted.wounded_first.01",
            "request" when !state.SeenMilestones.Contains("system:first-request", StringComparer.Ordinal) => "bramble.unprompted.first_request.01",
            "hungry" when observed?.VeryHungry == false => "bramble.unprompted.very_hungry.01",
            "talents" when !state.SeenMilestones.Contains("system:first-talent", StringComparer.Ordinal) => "bramble.unprompted.first_talent.01",
            "pack_unseen" when !state.SeenMilestones.Contains("system:first-pack-ready", StringComparer.Ordinal) => "bramble.unprompted.first_pack_ready.01",
            "pack_cart" => "bramble.next.pack_cart.01",
            "no_companion" => "bramble.next.no_companion.01",
            _ => DetectAdulthoodOrExpeditionHint(state, recommendation)
        };
        foreach (FoxPackRecordV2 companion in companions)
        {
            BrambleObservedCompanionState snapshot = GetOrCreateObservedState(state, companion);
            snapshot.Juvenile = companion.IsJuvenile;
            snapshot.Mortal = companion.HealthState == MortallyWoundedHealthState;
            snapshot.Wounded = companion.HealthState == RecoveringHealthState
                || companion.CurrentHealth > 0f && companion.MaxHealth > 0f && companion.CurrentHealth / companion.MaxHealth <= 0.35f;
            snapshot.VeryHungry = companion.FoodStateInitialized && companion.FoodLevel < 0.25f;
            snapshot.HadRequest = !string.IsNullOrWhiteSpace(companion.ActiveRequestId)
                || !string.IsNullOrWhiteSpace(companion.ActiveRequest) && !string.Equals(companion.ActiveRequest, "None", StringComparison.OrdinalIgnoreCase);
            snapshot.HadTalentPoint = companion.Points > 0;
        }
        return result;
    }

    private static BrambleObservedCompanionState GetOrCreateObservedState(BramblePlayerRecord state, FoxPackRecordV2 companion)
    {
        BrambleObservedCompanionState? observed = state.ObservedCompanionStates
            .FirstOrDefault(candidate => string.Equals(candidate.FoxId, companion.FoxId, StringComparison.Ordinal));
        if (observed != null) return observed;
        observed = new BrambleObservedCompanionState { FoxId = companion.FoxId, Juvenile = companion.IsJuvenile };
        state.ObservedCompanionStates.Add(observed);
        return observed;
    }

    private string? DetectAdulthoodOrExpeditionHint(BramblePlayerRecord state, BrambleRecommendation recommendation)
    {
        List<FoxPackRecordV2> companions = CurrentBrambleCompanionRecords(state.OwnerUid);
        foreach (FoxPackRecordV2 companion in companions)
        {
            BrambleObservedCompanionState? previous = state.ObservedCompanionStates.FirstOrDefault(observed => string.Equals(observed.FoxId, companion.FoxId, StringComparison.Ordinal));
            if (previous?.Juvenile == true && !companion.IsJuvenile)
            {
                previous.Juvenile = false;
                recommendation.TargetFoxId = companion.FoxId;
                recommendation.TargetName = string.IsNullOrWhiteSpace(companion.Name)
                    ? $"Companion #{companion.Number}"
                    : companion.Name;
                return "bramble.unprompted.juvenile_adult.01";
            }
            if (previous == null) state.ObservedCompanionStates.Add(new BrambleObservedCompanionState { FoxId = companion.FoxId, Juvenile = companion.IsJuvenile });
        }
        if (companions.Any(companion => !companion.IsJuvenile) && state.PackExplored
            && !state.SeenMilestones.Contains("system:first-expedition-available", StringComparer.Ordinal))
            return "bramble.unprompted.first_expedition_available.01";
        return null;
    }

    private void TryEmitBrambleAmbient(Entity bramble, IServerPlayer owner, BramblePlayerRecord state)
    {
        if (state.HintMode == BrambleHintMode.Quiet) return;
        long now = UtcNowMs();
        if (now - state.LastAmbientUtcMs < BrambleAmbientCooldownMs || EvaluateBrambleRecommendation(state).Id != "clear") return;
        int denominator = state.HintMode == BrambleHintMode.Frequent ? 900 : 2400;
        if (serverApi?.World.Rand.Next(denominator) != 0) return;
        string id = state.Follow ? (serverApi.World.Rand.Next(2) == 0 ? "bramble.ambient.follow.01" : "bramble.ambient.follow.02") : "bramble.ambient.observation.01";
        if (EmitBrambleLine(bramble, owner, id, CompanionDialoguePriority.Low))
        {
            state.LastAmbientUtcMs = now;
            brambleRepository?.Save();
        }
        if (now - state.LastTaxesUtcMs >= BrambleTaxesCooldownMs && serverApi.World.Rand.Next(10000) == 0
            && EmitBrambleLine(bramble, owner, "bramble.easteregg.taxes.01", CompanionDialoguePriority.Low)) state.LastTaxesUtcMs = now;
    }

    private void TryEmitBrambleGreeting(Entity bramble, IServerPlayer owner, BramblePlayerRecord state, bool force)
    {
        if (serverApi == null) return;
        long now = UtcNowMs();
        if (!force && now - state.LastGreetingUtcMs < BrambleGreetingCooldownMs) return;
        Entity? other = loadedBrambles.Values.FirstOrDefault(candidate => candidate.EntityId != bramble.EntityId && candidate.Alive
            && candidate.Pos.Dimension == bramble.Pos.Dimension && candidate.Pos.SquareDistanceTo(bramble.Pos) <= 12d * 12d
            && !string.Equals(GetCompanionOwnerUid(candidate), state.OwnerUid, StringComparison.Ordinal));
        if (other == null || !force && serverApi.World.Rand.Next(1800) != 0) return;
        string otherOwnerUid = GetCompanionOwnerUid(other);
        EmitDialogueEvent(bramble, owner, "bramble.ambient.greeting.01", "Morning, Bramble.", CompanionDialoguePriority.Low, "bramble.greeting");
        if (serverApi.World.PlayerByUid(otherOwnerUid) is IServerPlayer otherOwner)
            EmitDialogueEvent(other, otherOwner, "bramble.ambient.greeting.01", "Morning, Bramble.", CompanionDialoguePriority.Low, "bramble.greeting");
        state.LastGreetingUtcMs = now;
        if (brambleRepository?.TryGet(otherOwnerUid, out BramblePlayerRecord otherState) == true) otherState.LastGreetingUtcMs = now;
        brambleRepository?.Save();
    }

    private bool EmitBrambleLine(Entity bramble, IServerPlayer owner, string id, int priority)
        => EmitDialogueEvent(bramble, owner, id, BrambleLine(id), priority, "bramble");

    private void SendBrambleState(
        IServerPlayer player,
        bool showOnboarding = false,
        string responseLine = "",
        bool dismissalPending = false)
    {
        if (serverChannel == null || !BrambleEnabled || brambleRepository?.Loaded != true) return;
        BramblePlayerRecord state = brambleRepository.GetOrCreate(player.PlayerUID);
        if (state.Dismissed)
        {
            serverChannel.SendPacket(new BrambleStatePacket { Open = false }, player);
            return;
        }
        UpdateBrambleMilestones(state);
        Entity? bramble = EnsureBrambleForPlayer(player);
        BrambleRecommendation recommendation = EvaluateBrambleRecommendation(state);
        serverChannel.SendPacket(new BrambleStatePacket
        {
            Open = true,
            ShowOnboarding = showOnboarding && !BrambleOnboardingState.IsResolved(state.OnboardingState),
            OnboardingState = state.OnboardingState,
            SpeciesId = state.SpeciesId,
            SpeciesChosen = state.SpeciesChosen,
            FirstTamingChapter = !state.FirstCompanionOwned,
            HintMode = state.HintMode,
            Follow = state.Follow,
            BrambleEntityId = bramble?.EntityId ?? state.CurrentEntityId,
            PlayerSpeciesRelationship = GetPlayerSpeciesRelationship(player, state.SpeciesId),
            AcceptedFoods = GetAcceptedTamingFoods(state.SpeciesId),
            NearbyWildTarget = FindNearbyWildTutorialTarget(player, state.SpeciesId) != null,
            RecommendationId = recommendation.Id,
            RecommendationHeading = recommendation.Heading,
            RecommendationBody = recommendation.Body,
            RecommendationSteps = recommendation.Steps.ToList(),
            RecommendationAction = (int)recommendation.Action,
            RecommendationTargetEntityId = recommendation.TargetEntityId,
            ResponseLine = responseLine,
            DismissalPending = dismissalPending
        }, player);
        brambleRepository.Save();
    }

    private void OnBrambleRequest(IServerPlayer player, BrambleRequestPacket packet)
    {
        if (serverApi == null || !BrambleEnabled || brambleRepository?.Loaded != true || player.Entity == null) return;
        BramblePlayerRecord state = brambleRepository.GetOrCreate(player.PlayerUID);
        Entity? bramble = state.CurrentEntityId > 0 ? serverApi.World.GetEntityById(state.CurrentEntityId) : null;
        bool inRange = bramble == null || bramble.Pos.Dimension == player.Entity.Pos.Dimension && bramble.Pos.SquareDistanceTo(player.Entity.Pos) <= 12d * 12d;
        switch (packet.Action)
        {
            case BrambleRequestPacket.Open:
                if (inRange && bramble != null && packet.TargetEntityId == bramble.EntityId) SendBrambleState(player);
                break;
            case BrambleRequestPacket.OnboardingChoice:
                HandleBrambleOnboardingChoice(player, state, packet.Value);
                break;
            case BrambleRequestPacket.SelectSpecies:
                HandleBrambleSpeciesChoice(player, state, packet.Value);
                break;
            case BrambleRequestPacket.SetHintMode:
                if (!BrambleHintMode.IsValid(packet.Value)) return;
                state.HintMode = BrambleHintMode.Normalize(packet.Value);
                SendBrambleState(player, responseLine: BrambleLine("bramble.hints." + state.HintMode + ".01"));
                break;
            case BrambleRequestPacket.Refresh:
                SendBrambleState(player);
                break;
            case BrambleRequestPacket.BeginDismissal:
                SendBrambleState(
                    player,
                    responseLine: "I think I can manage from here. Let Bramble move on?",
                    dismissalPending: true);
                break;
            case BrambleRequestPacket.CancelDismissal:
                SendBrambleState(player);
                break;
            case BrambleRequestPacket.ConfirmDismissal:
                if (string.Equals(packet.Value, "confirm", StringComparison.OrdinalIgnoreCase))
                {
                    DismissBrambleForPlayer(player, state);
                }
                break;
        }
    }

    private void DismissBrambleForPlayer(IServerPlayer player, BramblePlayerRecord state)
    {
        if (serverApi == null || brambleRepository?.Loaded != true) return;
        if (state.Dismissed) return;

        foreach (Entity bramble in loadedBrambles.Values
            .Where(entity => entity.Alive
                && IsGuideFox(entity)
                && string.Equals(GetCompanionOwnerUid(entity), player.PlayerUID, StringComparison.Ordinal))
            .ToArray())
        {
            loadedBrambles.Remove(bramble.EntityId);
            bramble.Die(EnumDespawnReason.Removed);
        }

        if (state.CurrentEntityId > 0
            && serverApi.World.GetEntityById(state.CurrentEntityId) is Entity current
            && current.Alive
            && IsGuideFox(current))
        {
            current.Die(EnumDespawnReason.Removed);
        }

        packRepository?.ClearBrambleBedAssignment(player.PlayerUID);
        state.Dismissed = true;
        state.Follow = false;
        ClearBrambleHome(state);
        state.CurrentEntityId = 0;
        state.EntityToken = Guid.NewGuid().ToString("N");
        state.LastOnboardingChoice = "dismissed";
        brambleRepository.Save();
        serverChannel?.SendPacket(new BrambleStatePacket { Open = false }, player);
    }

    private void ClearBrambleHome(BramblePlayerRecord record)
    {
        if (string.Equals(record.HomeType, BrambleBedHomeType, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(record.OwnerUid))
        {
            packRepository?.ClearBrambleBedAssignment(record.OwnerUid);
        }
        record.HomeType = string.Empty;
        record.HomeX = 0;
        record.HomeY = 0;
        record.HomeZ = 0;
        record.HomeDimension = 0;
    }

    private bool TryAssignBrambleToBed(IServerPlayer player, BlockPos pos, out string refusal)
    {
        refusal = string.Empty;
        if (brambleRepository?.Loaded != true
            || !brambleRepository.TryGet(player.PlayerUID, out BramblePlayerRecord record)
            || record.Dismissed)
        {
            refusal = "Bramble is not available to assign right now.";
            return false;
        }

        ClearBrambleHome(record);
        if (packRepository?.TryAssignBrambleBed(player.PlayerUID, pos, out refusal) != true) return false;

        record.Follow = false;
        record.HomeType = BrambleBedHomeType;
        record.HomeX = pos.X;
        record.HomeY = pos.Y;
        record.HomeZ = pos.Z;
        record.HomeDimension = pos.dimension;
        Entity? bramble = record.CurrentEntityId > 0 ? serverApi?.World.GetEntityById(record.CurrentEntityId) : null;
        if (bramble != null && bramble.Alive)
        {
            NormalizeBrambleEntity(bramble, record);
            if (bramble is EntityAgent agent)
            {
                agent.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.StopTasks();
                agent.Controls.StopAllMovement();
                bramble.Pos.Motion.Set(0, 0, 0);
            }
        }
        brambleRepository.Save();
        return true;
    }

    private void ClearBrambleBedAssignment(string ownerUid)
    {
        if (brambleRepository?.Loaded != true) return;
        packRepository?.ClearBrambleBedAssignment(ownerUid);
        if (!brambleRepository.TryGet(ownerUid, out BramblePlayerRecord record) || record.Dismissed) return;
        if (string.Equals(record.HomeType, BrambleBedHomeType, StringComparison.Ordinal))
        {
            ClearBrambleHome(record);
            brambleRepository.Save();
        }
    }

    internal bool TryResolveBrambleStayTarget(Entity entity, out Vec3d? target, out bool assignedBed)
    {
        target = null;
        assignedBed = false;
        if (serverApi == null || brambleRepository?.Loaded != true || !IsGuideFox(entity)) return false;
        string ownerUid = GetCompanionOwnerUid(entity);
        if (!brambleRepository.TryGet(ownerUid, out BramblePlayerRecord record) || record.Dismissed) return false;

        if (string.Equals(record.HomeType, BramblePackCartHomeType, StringComparison.Ordinal))
        {
            BlockPos? cart = GetActiveCairnPosition(ownerUid, discoverNearby: true);
            if (cart == null)
            {
                cart = new BlockPos(record.HomeX, record.HomeY, record.HomeZ, record.HomeDimension);
            }
            if (cart == null
                || cart.dimension != entity.Pos.Dimension
                || serverApi.World.BlockAccessor.GetChunkAtBlockPos(cart) == null)
            {
                return false;
            }

            Block cartBlock = serverApi.World.BlockAccessor.GetBlock(cart);
            if (cartBlock.Code == null || !IsPackMarkerCode(cartBlock.Code)) return false;
            target = new Vec3d(
                cart.X + 0.5,
                cart.Y + 1.22 + cart.dimension * BlockPos.DimensionBoundary,
                cart.Z + 0.5);
            return true;
        }

        if (!string.Equals(record.HomeType, BrambleBedHomeType, StringComparison.Ordinal)) return false;
        assignedBed = true;
        BlockPos bedPos = new(record.HomeX, record.HomeY, record.HomeZ, record.HomeDimension);
        if (entity.Pos.Dimension != bedPos.dimension) return false;
        IWorldChunk? chunk = serverApi.World.BlockAccessor.GetChunkAtBlockPos(bedPos);
        if (chunk != null)
        {
            Block? bedBlock = serverApi.World.BlockAccessor.GetBlock(bedPos);
            if (bedBlock.Code?.Path.StartsWith(FoxBedBlockPathPrefix, StringComparison.OrdinalIgnoreCase) != true)
                return false;
        }

        Vec3d fallback = new(
            bedPos.X + 0.5,
            bedPos.Y + bedPos.dimension * BlockPos.DimensionBoundary + 0.35,
            bedPos.Z + 0.5);
        target = chunk != null
            ? FindSafeEntityPosition(bedPos, entity.Properties, entity, 2, 2) ?? fallback
            : fallback;
        return target != null;
    }

    internal bool IsBrambleHomeModeActive(Entity entity)
    {
        if (brambleRepository?.Loaded != true) return false;
        string ownerUid = GetCompanionOwnerUid(entity);
        return brambleRepository.TryGet(ownerUid, out BramblePlayerRecord record)
            && !record.Dismissed
            && !string.IsNullOrWhiteSpace(record.HomeType);
    }

    private void HandleBrambleOnboardingChoice(IServerPlayer player, BramblePlayerRecord state, string choice)
    {
        string response;
        bool reopen = true;
        switch ((choice ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "accept": state.OnboardingState = BrambleOnboardingState.Accepted; state.LastOnboardingChoice = "accept"; response = BrambleLine("bramble.onboarding.accept.01"); break;
            case "not-now": state.OnboardingState = BrambleOnboardingState.NotNow; state.LastOnboardingChoice = "not-now"; response = BrambleLine("bramble.onboarding.not_now.01"); break;
            case "ask-later": state.OnboardingState = BrambleOnboardingState.AskLater; state.LastOnboardingChoice = "ask-later"; response = BrambleLine("bramble.onboarding.ask_later.01"); break;
            case "dismiss": state.OnboardingState = BrambleOnboardingState.AskLater; state.LastOnboardingChoice = "ask-later"; response = string.Empty; reopen = false; break;
            default: return;
        }
        brambleRepository?.Save();
        if (reopen) SendBrambleState(player, responseLine: response);
    }

    private void HandleBrambleSpeciesChoice(IServerPlayer player, BramblePlayerRecord state, string speciesId)
    {
        speciesId = (speciesId ?? string.Empty).Trim().ToLowerInvariant();
        if (!BrambleTutorialSpeciesIds.Contains(speciesId, StringComparer.Ordinal)) return;
        string previous = state.SpeciesId;
        bool previousChosen = state.SpeciesChosen;
        state.SpeciesId = speciesId;
        state.SpeciesChosen = true;
        if (ReplaceBrambleForm(player, state) == null)
        {
            state.SpeciesId = previous;
            state.SpeciesChosen = previousChosen;
            SendBrambleState(player, responseLine: "I could not safely take that form here. Move to open ground and try again.");
            return;
        }
        brambleRepository?.Save();
        SendBrambleState(player, responseLine: BrambleLine("bramble.species_selected." + speciesId + ".01"));
    }

    private static string GetPlayerSpeciesRelationship(IServerPlayer player, string selectedSpecies)
    {
        string? playerSpecies = player.Entity is EntityPlayer entityPlayer ? FeralRaceResolver.GetRace(entityPlayer) : null;
        if (string.IsNullOrWhiteSpace(playerSpecies)) return "unknown";
        return string.Equals(playerSpecies, selectedSpecies, StringComparison.OrdinalIgnoreCase) ? "same" : "different";
    }

    private List<string> GetAcceptedTamingFoods(string speciesId)
    {
        var result = new List<string>();
        if (serverApi == null || !BrambleFormCodes.TryGetValue(speciesId, out string? code)) return result;
        EntityProperties? properties = serverApi.World.GetEntityType(new AssetLocation(code));
        JsonObject? tameable = properties?.Server?.BehaviorsAsJsonObj?.FirstOrDefault(behavior => string.Equals(behavior["code"].AsString(), "tameable", StringComparison.OrdinalIgnoreCase));
        foreach (JsonObject treat in tameable?["treat"].AsArray() ?? Array.Empty<JsonObject>())
        {
            string path = treat["code"].AsString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path)) continue;
            string domain = treat["domain"].AsString("game");
            AssetLocation location = new(domain, path);
            Item? item = serverApi.World.GetItem(location);
            if (item != null)
            {
                result.Add(new ItemStack(item).GetName());
                continue;
            }
            Block? block = serverApi.World.GetBlock(location);
            result.Add(block != null ? new ItemStack(block).GetName() : path.Replace('-', ' '));
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private Entity? FindNearbyWildTutorialTarget(IServerPlayer player, string speciesId)
    {
        if (serverApi == null || player.Entity == null) return null;
        return serverApi.World.GetEntitiesAround(player.Entity.Pos.XYZ, 32f, 16f, entity =>
        {
            if (entity.Code == null || GetDomesticationStatus(entity) != null) return false;
            return CompanionSpeciesCatalog.TryGetByWildRecruitmentEntityCode(entity.Code.ToString(), out CompanionSpeciesProfile profile)
                && string.Equals(profile.Id, speciesId, StringComparison.OrdinalIgnoreCase);
        }).OrderBy(entity => entity.Pos.SquareDistanceTo(player.Entity.Pos)).FirstOrDefault();
    }

    private Entity? FindNearbyTamingTarget(IServerPlayer player, string speciesId)
    {
        if (serverApi == null || player.Entity == null) return null;
        return serverApi.World.GetEntitiesAround(player.Entity.Pos.XYZ, 32f, 16f, entity =>
        {
            if (entity.Code == null || !PetAiCompatibilityBridge.IsTaming(entity)) return false;
            if (!CompanionSpeciesCatalog.TryGetByWildRecruitmentEntityCode(entity.Code.ToString(), out CompanionSpeciesProfile profile)
                || !string.Equals(profile.Id, speciesId, StringComparison.OrdinalIgnoreCase)) return false;
            string ownerUid = GetCompanionOwnerUid(entity);
            return string.IsNullOrWhiteSpace(ownerUid) || string.Equals(ownerUid, player.PlayerUID, StringComparison.Ordinal);
        }).OrderBy(entity => entity.Pos.SquareDistanceTo(player.Entity.Pos)).FirstOrDefault();
    }

    internal static string GetBrambleSpeciesDisplayName(string? speciesId)
        => CompanionSpeciesCatalog.TryGetById(speciesId, out CompanionSpeciesProfile profile)
            && BrambleTutorialSpeciesIds.Contains(profile.Id, StringComparer.Ordinal) ? profile.DisplayName : "Companion";

    private void OnBrambleState(BrambleStatePacket packet)
    {
        lastBrambleState = packet;
        if (clientApi == null) return;
        if (!packet.Open)
        {
            guideDialog?.TryClose();
            return;
        }
        socialDialog?.TryClose(); developerDialog?.TryClose(); packDialog?.TryClose(); perkDialog?.TryClose(); guideDialog?.TryClose();
        guideDialog = new GuiDialogFeralKinshipGuide(clientApi, this, packet);
        guideDialog.TryOpen();
    }

    internal BrambleStatePacket? GetBrambleStateClient() => lastBrambleState;

    internal bool TryOpenGuideGui(Entity entity)
    {
        if (clientApi?.World.Player.Entity == null || !entity.Alive || !IsGuideFox(entity)
            || !string.Equals(GetCompanionOwnerUid(entity), clientApi.World.Player.PlayerUID, StringComparison.Ordinal)) return false;
        clientChannel?.SendPacket(new BrambleRequestPacket { Action = BrambleRequestPacket.Open, TargetEntityId = entity.EntityId });
        return true;
    }

    internal void SendBrambleChoice(int action, string value = "")
        => clientChannel?.SendPacket(new BrambleRequestPacket { Action = action, Value = value });

    internal GuideAdviceClient GetGuideAdviceClient()
    {
        BrambleStatePacket? packet = lastBrambleState;
        if (packet == null) return new GuideAdviceClient { Heading = "The trail is still coming into focus", Body = "Ask again when the world has finished loading." };
        GuideAdviceAction action = (GuideAdviceAction)packet.RecommendationAction;
        return new GuideAdviceClient
        {
            Id = packet.RecommendationId,
            Heading = packet.RecommendationHeading,
            Body = packet.RecommendationBody,
            Steps = packet.RecommendationSteps?.ToArray() ?? Array.Empty<string>(),
            Action = action,
            ActionLabel = action switch
            {
                GuideAdviceAction.TreatmentHelp => "Treatment help", GuideAdviceAction.OpenCompanion => "Open Companion",
                GuideAdviceAction.OpenSocial => "Open Social", GuideAdviceAction.OpenTalents => "Open Talents",
                GuideAdviceAction.OpenPack => "Open Pack", GuideAdviceAction.OpenTamingGuide => "Open taming guide", _ => string.Empty
            },
            TargetEntityId = packet.RecommendationTargetEntityId
        };
    }

    internal bool TryFollowGuideAdvice(GuideAdviceClient advice)
    {
        if (!advice.HasAction) return false;
        if (advice.Action is GuideAdviceAction.TreatmentHelp or GuideAdviceAction.OpenTamingGuide) return true;
        Entity? target = clientApi?.World.GetEntityById(advice.TargetEntityId);
        if (target == null || !target.Alive) return false;
        guideDialog?.TryClose();
        return advice.Action switch
        {
            GuideAdviceAction.OpenCompanion => TryOpenFoxSocialGui(target),
            GuideAdviceAction.OpenSocial => TryOpenFoxSocialGui(target, "social"),
            GuideAdviceAction.OpenTalents => TryOpenFoxPerksGui(target),
            GuideAdviceAction.OpenPack => TryOpenFoxPackGui(target.EntityId),
            _ => false
        };
    }

    private void MarkBramblePackExplored(string ownerUid)
    {
        if (brambleRepository?.TryGet(ownerUid, out BramblePlayerRecord state) != true || state.PackExplored) return;
        state.PackExplored = true;
        brambleRepository.Save();
    }

    internal void MarkBramblePackCartExplored(string ownerUid)
    {
        if (brambleRepository?.TryGet(ownerUid, out BramblePlayerRecord state) != true || state.PackCartExplored) return;
        state.PackCartExplored = true;
        brambleRepository.Save();
    }

    internal void MarkBrambleTalentsExplored(string ownerUid)
    {
        if (brambleRepository?.TryGet(ownerUid, out BramblePlayerRecord state) != true || state.TalentsExplored) return;
        state.TalentsExplored = true;
        state.SeenMilestones.Add("system:first-talent");
        brambleRepository.Save();
    }
}
