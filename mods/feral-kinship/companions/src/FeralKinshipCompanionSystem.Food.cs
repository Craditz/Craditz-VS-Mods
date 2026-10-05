#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VintageStoryConfigMigration;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private const double DiningBoardInteractionRadius = 3.5d;
    private const string CompanionFoodConfigFileName = "FeralKinshipCompanions.json";
    private const string BlueberryModeConfigKey = "EnableBlueberryMode";
    internal const string FoodStateInitializedKey = "feralKinshipFoodStateInitialized";
    internal const string FoodLevelKey = "feralKinshipFoodLevel";
    internal const string FoodLastUpdateTotalHoursKey = "feralKinshipFoodLastUpdateTotalHours";
    private const float FoodSeekThreshold = 0.65f;
    private const float FoodLowThreshold = 0.25f;
    private const float FoodStarvingThreshold = 0.001f;
    private const double FullFoodDurationHours = 96d;
    private const float MinimumFoodPerItem = 0.25f;
    private const float MaximumFoodPerItem = 0.65f;
    private const float SatietyForFullFoodBonus = 1600f;
    private const float FoodFeedCutoff = 1f - MinimumFoodPerItem;
    private const string DiningAmenityKind = "dining";
    private const string DiningBoardBlockPath = "packamenity-dining";
    private static readonly CreatureDiet CatsCompanionDiet = new()
    {
        FoodCategories = new[] { EnumFoodCategory.Protein }
    };
    private static readonly string[] CatsCompanionFoodPaths =
    {
        "bushmeat-raw",
        "redmeat-raw",
        "poultry-raw",
        "fish-raw",
        "kibble-meat-raw",
        "kibble-meat-dry",
        "petcookie-meat-perfect"
    };

    private CompanionConfig companionConfig = new();
    private bool blueberryModeEnabled;
    private readonly Dictionary<long, float> lastFoodUiLevelByEntity = new();
    private readonly Dictionary<long, double> lastObservedPetAiFeedCooldownByEntity = new();

    private sealed class CompanionConfig
    {
        public bool EnableCompanionFoodSystem { get; set; } = true;
        public bool AllowCompanionDoorUse { get; set; } = false;
        public bool EnableBramble { get; set; } = true;
        public bool StfuMode { get; set; } = false;
        public bool EnableDiagnosticLogging { get; set; } = false;
        public bool IdleSounds { get; set; } = true;
    }

    internal bool FoodSystemEnabled => companionConfig.EnableCompanionFoodSystem;
    internal bool DoorUseEnabled => companionConfig.AllowCompanionDoorUse;
    internal bool BrambleEnabled => companionConfig.EnableBramble;
    internal bool StfuModeEnabled => companionConfig.StfuMode;
    internal bool DiagnosticLoggingEnabled => companionConfig.EnableDiagnosticLogging;
    internal bool SourceAnimalIdleVocalizationsEnabled => companionConfig.IdleSounds;
    internal bool BlueberryModeEnabled => blueberryModeEnabled;
    private void LoadCompanionFoodConfig()
    {
        if (serverApi == null)
        {
            return;
        }

        try
        {
            JsonObject? rawConfig = serverApi.LoadModConfig(CompanionFoodConfigFileName);
            blueberryModeEnabled = rawConfig?.KeyExists(BlueberryModeConfigKey) == true
                && rawConfig[BlueberryModeConfigKey].AsBool(false);
            CompanionConfig? loaded = serverApi.LoadModConfig<CompanionConfig>(
                CompanionFoodConfigFileName);
            companionConfig = loaded ?? new CompanionConfig();
            IdleSoundsConfigMigration migration = IdleSoundsConfigPolicy.Migrate(
                rawConfig?.Token?.ToString());
            companionConfig.IdleSounds = migration.Enabled;
            JsonObject? normalizedRawConfig = migration.NormalizedJson == null
                ? rawConfig
                : JsonObject.FromJson(migration.NormalizedJson);
            // Store the normalized shape so an existing food-only config gains
            // new settings without changing its values or unknown keys. The
            // retired sound key is removed after its value is migrated.
            try
            {
                ConfigDefaults.StorePreservingUnknown(
                    serverApi,
                    CompanionFoodConfigFileName,
                    companionConfig,
                    normalizedRawConfig);
            }
            catch (Exception exception)
            {
                serverApi.Logger.Warning(
                    "[FeralKinshipCompanions] Loaded {0}, but could not write its normalized shape. {1}",
                    CompanionFoodConfigFileName,
                    exception.Message);
            }
        }
        catch (Exception exception)
        {
            companionConfig = new CompanionConfig();
            blueberryModeEnabled = false;
            serverApi.Logger.Error(
                "[FeralKinshipCompanions] Could not load {0}; using safe companion defaults. {1}",
                CompanionFoodConfigFileName,
                exception.Message);
        }
        serverApi.Logger.Notification(
            "[FeralKinshipCompanions] Navigation diagnostics {0} ({1}).",
            DiagnosticLoggingEnabled ? "ENABLED" : "DISABLED", CompanionFoodConfigFileName);
    }

    private void EnsureCompanionFoodState(Entity entity)
    {
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        if (status.GetBool(FoodStateInitializedKey, false))
        {
            status.SetFloat(FoodLevelKey, Math.Clamp(status.GetFloat(FoodLevelKey, 1f), 0f, 1f));
            return;
        }

        status.SetBool(FoodStateInitializedKey, true);
        status.SetFloat(FoodLevelKey, 1f);
        status.SetDouble(FoodLastUpdateTotalHoursKey, entity.World.Calendar.TotalHours);
        MarkSocialStateDirty(entity);
    }

    private void UpdateCompanionFood(Entity entity)
    {
        EnsureCompanionFoodState(entity);
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        lastObservedPetAiFeedCooldownByEntity.TryAdd(
            entity.EntityId,
            status.GetDouble("cooldown", 0d));
        double nowHours = entity.World.Calendar.TotalHours;
        double previousHours = status.GetDouble(FoodLastUpdateTotalHoursKey, nowHours);
        bool ownerOffline = IsCompanionOwnerOffline(entity);

        // Disabling hunger is intentionally a clean compatibility mode. Keep
        // the timestamp current so re-enabling cannot apply a surprise backlog.
        // An offline owner receives the same pause treatment while the loaded
        // companion continues using its ordinary AI and duties.
        if (!FoodSystemEnabled || ownerOffline || IsFoxAwayFromWorld(entity) || IsFoxIncapacitated(entity))
        {
            status.SetDouble(FoodLastUpdateTotalHoursKey, nowHours);
            if (ownerOffline)
            {
                PersistCompanionFoodState(entity, status);
            }
            return;
        }

        double elapsedHours = Math.Max(0d, nowHours - previousHours);
        if (elapsedHours <= 0d)
        {
            return;
        }

        float previousLevel = Math.Clamp(status.GetFloat(FoodLevelKey, 1f), 0f, 1f);
        double foodDurationHours = FullFoodDurationHours
            * (1d + FeralKinshipCompanionSystem.GetFoxPackTalentRank(entity, "efficient-metabolism") * 0.20d);
        float currentLevel = Math.Clamp(
            previousLevel - (float)(elapsedHours / foodDurationHours),
            0f,
            1f);
        status.SetDouble(FoodLastUpdateTotalHoursKey, nowHours);
        status.SetFloat(FoodLevelKey, currentLevel);
        PersistCompanionFoodState(entity, status);

        string previousState = GetFoodStateLabel(previousLevel, true);
        string currentState = GetFoodStateLabel(currentLevel, true);
        bool stateChanged = !string.Equals(previousState, currentState, StringComparison.Ordinal);
        if (!lastFoodUiLevelByEntity.TryGetValue(entity.EntityId, out float lastSent)
            || stateChanged
            || Math.Abs(lastSent - currentLevel) >= 0.01f)
        {
            lastFoodUiLevelByEntity[entity.EntityId] = currentLevel;
            MarkSocialStateDirty(entity);
            SendStateToOwner(entity, string.Empty);
        }
        if (stateChanged && TryGetOwnerPlayer(entity, out IServerPlayer? owner))
        {
            string? eventId = currentLevel <= FoodStarvingThreshold
                ? (TryResolveCompanionFoodTarget(entity, out _, out _) ? null : "food.starving_no_food")
                : currentLevel < FoodLowThreshold
                    ? (TryResolveCompanionFoodTarget(entity, out _, out _) ? "food.hungry_getting_food" : "food.hungry_low_food")
                    : currentLevel < FoodSeekThreshold
                        ? (TryResolveCompanionFoodTarget(entity, out _, out _) ? "food.hungry_getting_food" : "food.hungry_no_food")
                        : null;
            if (eventId != null) EmitDialogueEvent(entity, owner!, eventId, string.Empty, CompanionDialoguePriority.Medium);
        }
    }

    private void PersistCompanionFoodState(Entity entity, ITreeAttribute status)
    {
        string foxId = status.GetString(FoxIdKey, string.Empty);
        if (packRepository?.Loaded == true
            && !string.IsNullOrWhiteSpace(foxId)
            && packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
            && record != null)
        {
            record.FoodStateInitialized = true;
            record.FoodLevel = Math.Clamp(status.GetFloat(FoodLevelKey, 1f), 0f, 1f);
            record.FoodLastUpdateTotalHours = status.GetDouble(
                FoodLastUpdateTotalHoursKey,
                entity.World.Calendar.TotalHours);
        }
    }

    internal bool IsCompanionFoodRestricted(Entity entity)
    {
        return FoodSystemEnabled
            && !IsCompanionOwnerOffline(entity)
            && GetCompanionFoodLevel(entity) <= FoodStarvingThreshold;
    }

    internal bool IsCompanionOwnerOffline(Entity entity)
    {
        if (serverApi == null)
        {
            return false;
        }

        string ownerUid = GetCompanionOwnerUid(entity);
        return !string.IsNullOrWhiteSpace(ownerUid)
            && !IsOnlineServerPlayer(serverApi.World.PlayerByUid(ownerUid));
    }

    internal static bool IsOnlineServerPlayer(IPlayer? player) =>
        player is IServerPlayer serverPlayer
        && IsPlayingConnectionState(serverPlayer.ConnectionState);

    internal static bool IsPlayingConnectionState(EnumClientState state) =>
        state == EnumClientState.Playing;

    private void RebaseOwnerCompanionFoodClocks(string ownerUid)
    {
        if (serverApi == null
            || packRepository?.Loaded != true
            || string.IsNullOrWhiteSpace(ownerUid))
        {
            return;
        }

        double nowHours = serverApi.World.Calendar.TotalHours;
        bool changed = false;

        // Loaded entities carry their own timestamp mirror. Rebase both sides
        // so a later ledger refresh cannot restore an offline-era timestamp.
        foreach (Entity entity in loadedFoxes.Values.Where(entity =>
                     entity.Alive
                     && string.Equals(GetCompanionOwnerUid(entity), ownerUid, StringComparison.Ordinal)))
        {
            ITreeAttribute? status = GetDomesticationStatus(entity);
            if (status?.GetBool(FoodStateInitializedKey, false) != true)
            {
                continue;
            }

            status.SetDouble(FoodLastUpdateTotalHoursKey, nowHours);
            PersistCompanionFoodState(entity, status);
            changed = true;
        }

        // Unloaded companions have no tick with which to observe the owner's
        // connection state. Rebase their durable clock at both logout and
        // return so elapsed offline time is never charged on their next load.
        changed |= RebaseCompanionFoodRecords(
            packRepository.GetRecordsForOwner(ownerUid),
            nowHours) > 0;

        if (changed)
        {
            packRepository.Save();
        }
    }

    internal static int RebaseCompanionFoodRecords(
        IEnumerable<FoxPackRecordV2> records,
        double nowHours)
    {
        int changed = 0;
        foreach (FoxPackRecordV2 record in records)
        {
            if (!record.FoodStateInitialized)
            {
                continue;
            }

            record.FoodLastUpdateTotalHours = nowHours;
            changed++;
        }
        return changed;
    }

    internal static float GetCompanionFoodLevel(Entity entity)
    {
        ITreeAttribute? status = GetDomesticationStatus(entity);
        return status?.GetBool(FoodStateInitializedKey, false) == true
            ? Math.Clamp(status.GetFloat(FoodLevelKey, 1f), 0f, 1f)
            : 1f;
    }

    internal static bool ShouldCompanionSeekFood(Entity entity)
    {
        FeralKinshipCompanionSystem? system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        return system?.FoodSystemEnabled == true
            && !system.IsCompanionOwnerOffline(entity)
            && GetCompanionFoodLevel(entity) < FoodSeekThreshold;
    }

    internal static string GetFoodStateLabel(float level, bool enabled)
    {
        if (!enabled) return "Food system disabled";
        level = Math.Clamp(level, 0f, 1f);
        if (level <= FoodStarvingThreshold) return "Starving — resting until fed";
        if (level < FoodLowThreshold) return "Very hungry";
        if (level < FoodSeekThreshold) return "Hungry";
        return "Well fed";
    }

    internal bool TryResolveCompanionFoodTarget(Entity entity, out BlockPos? diningPos, out Vec3d? target)
    {
        diningPos = null;
        target = null;
        if (!FoodSystemEnabled || !ShouldCompanionSeekFood(entity)) return false;

        foreach ((FoxPackAmenityRecord amenity, BlockEntityFeralKinshipDiningBoard dining) in
                 GetLoadedDiningBoards(entity, requireSuitableFood: true))
        {
            diningPos = new BlockPos(amenity.X, amenity.Y, amenity.Z, amenity.Dimension);
            target = ResolveStandingPositionBeside(entity, diningPos);
            return target != null;
        }
        return false;
    }

    internal bool TryResolveCompanionStarvingRestTarget(Entity entity, out Vec3d? target)
    {
        target = null;
        if (!IsCompanionFoodRestricted(entity) || serverApi == null || packRepository?.Loaded != true)
        {
            return false;
        }

        string foxId = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        FoxBedRecord? bed = packRepository.GetBedForFox(foxId);
        GetCompanionCareScope(entity, out BlockPos careCenter, out double careRadius, out _);
        if (bed != null && CompanionCampBounds.Contains(bed.X, bed.Z, bed.Dimension,
                careCenter.X, careCenter.Z, careCenter.dimension, careRadius))
        {
            BlockPos bedPos = new(bed.X, bed.Y, bed.Z, bed.Dimension);
            if (serverApi.World.BlockAccessor.GetChunkAtBlockPos(bedPos) != null
                && serverApi.World.BlockAccessor.GetBlock(bedPos).Code?.Path.StartsWith(
                    FoxBedBlockPathPrefix,
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                target = ResolveStandingPositionBeside(entity, bedPos);
                if (target != null) return true;
            }
        }

        foreach ((FoxPackAmenityRecord amenity, BlockEntityFeralKinshipDiningBoard _) in
                 GetLoadedDiningBoards(entity, requireSuitableFood: false))
        {
            target = ResolveStandingPositionBeside(
                entity,
                new BlockPos(amenity.X, amenity.Y, amenity.Z, amenity.Dimension));
            if (target != null) return true;
            break;
        }

        BlockPos? cart = careCenter;
        if (cart != null && cart.dimension == entity.Pos.Dimension
            && serverApi.World.BlockAccessor.GetChunkAtBlockPos(cart) != null)
        {
            target = ResolveStandingPositionBeside(entity, cart);
            return target != null;
        }

        // A pack can temporarily have no placed structures. Starvation must
        // still suppress work, so rest safely in place until food is supplied.
        target = entity.Pos.XYZ;
        return true;
    }

    internal bool TryConsumeCompanionFood(Entity entity, BlockPos diningPos)
    {
        if (!FoodSystemEnabled || IsCompanionOwnerOffline(entity)
            || entity.Pos.Dimension != diningPos.dimension
            || !IsWithinDiningBoardInteractionRadius(entity, diningPos))
        {
            return false;
        }

        BlockEntityFeralKinshipDiningBoard? dining = EnsureDiningBoardEntity(diningPos);
        CreatureDiet? diet = GetCreatureDiet(entity);
        if (dining == null || diet == null)
        {
            return false;
        }

        if (GetCompanionFoodLevel(entity) >= FoodFeedCutoff)
        {
            return false;
        }

        for (int index = 0; index < dining.Inventory.Count; index++)
        {
            ItemSlot slot = dining.Inventory[index];
            if (slot.Empty || slot.Itemstack == null || !IsSuitableDiningBoardFood(entity, slot.Itemstack, diet))
            {
                continue;
            }

            ItemStack consumed = slot.Itemstack.Clone();
            consumed.StackSize = 1;
            if (slot.TakeOut(1) == null)
            {
                return false;
            }

            slot.MarkDirty();
            dining.MarkDirty(true);
            RestoreCompanionFoodFromStack(entity, consumed);
            if (TryGetOwnerPlayer(entity, out IServerPlayer? owner))
                EmitDialogueEvent(entity, owner!, "food.eat.dining_board", string.Empty, CompanionDialoguePriority.Low);
            return true;
        }
        return false;
    }

    private void TryConsumeNearbyDiningBoardFood(Entity entity)
    {
        if (!ShouldCompanionSeekFood(entity)) return;

        foreach ((FoxPackAmenityRecord amenity, BlockEntityFeralKinshipDiningBoard _) in
                 GetLoadedDiningBoards(entity, requireSuitableFood: true))
        {
            BlockPos diningPos = new(amenity.X, amenity.Y, amenity.Z, amenity.Dimension);
            if (IsWithinDiningBoardInteractionRadius(entity, diningPos)
                && TryConsumeCompanionFood(entity, diningPos))
            {
                return;
            }
        }
    }

    private bool TryHandleDirectHandFeed(Entity entity, IServerPlayer owner, ItemSlot slot)
    {
        float levelBeforeFeeding = GetCompanionFoodLevel(entity);
        if (serverApi == null || !FoodSystemEnabled
            || !IsTamedFox(entity) || IsFoxIncapacitated(entity)
            || !IsOwner(entity, owner.PlayerUID)
            || owner.Entity == null
            || owner.Entity.Controls.Sneak
            )
        {
            return false;
        }

        // Validate the server-side actor and target before changing either
        // inventory. The normal entity interaction range is eight blocks.
        if (owner.Entity.Pos.Dimension != entity.Pos.Dimension
            || owner.Entity.Pos.SquareDistanceTo(entity.Pos) > 8d * 8d)
        {
            return false;
        }

        // Leave room for the smallest possible food portion. This check is
        // intentionally before both the PetAI race fallback and our own item
        // transaction, so an almost-full companion keeps the player's food.
        if (GetCompanionFoodLevel(entity) >= FoodFeedCutoff)
        {
            return false;
        }

        // PetAI can win the event race for the final item in a held stack and
        // leave the server slot empty before this handler runs. A new PetAI
        // cooldown is the only safe proof that this particular interaction
        // accepted that missing item; reconcile that one case without opening
        // the Companion window a second time.
        if (slot?.Itemstack == null)
        {
            ITreeAttribute status = GetDomesticationStatus(entity, true)!;
            double cooldown = status.GetDouble("cooldown", 0d);
            double previouslyObserved = lastObservedPetAiFeedCooldownByEntity.TryGetValue(
                entity.EntityId,
                out double observed
            ) ? observed : cooldown;
            if (cooldown <= previouslyObserved + 0.000001d) return false;

            lastObservedPetAiFeedCooldownByEntity[entity.EntityId] = cooldown;
            RestoreCompanionFoodAmount(entity, MinimumFoodPerItem);
            EmitDialogueEvent(entity, owner,
                levelBeforeFeeding < FoodSeekThreshold ? "food.owner_fed_hungry" : "food.owner_fed_not_hungry",
                string.Empty, CompanionDialoguePriority.Medium);
            return true;
        }

        ItemStack offered = slot.Itemstack.Clone();
        offered.StackSize = 1;
        CreatureDiet? diet = GetCreatureDiet(entity);
        if (diet == null
            || (!diet.Matches(offered) && !MatchesOptionalButcheringFood(entity, offered, diet)))
        {
            return false;
        }

        // PetAI may put food in its mouth slot instead of removing it. Make
        // Companion feeding authoritative: remove exactly one validated item,
        // then apply its nutrition. PreventSubsequent stops a second consumer.
        if (slot.TakeOut(1) == null) return false;
        slot.MarkDirty();
        RestoreCompanionFoodFromStack(entity, offered);
        EmitDialogueEvent(entity, owner,
            levelBeforeFeeding < FoodSeekThreshold ? "food.owner_fed_hungry" : "food.owner_fed_not_hungry",
            string.Empty, CompanionDialoguePriority.Medium);
        return true;
    }

    internal void CreditCompanionFoodFromHeldItem(Entity entity, ItemStack food)
    {
        if (!FoodSystemEnabled || IsCompanionOwnerOffline(entity) || IsFoxIncapacitated(entity)) return;
        RestoreCompanionFoodFromStack(entity, food);
    }

    private void RestoreCompanionFoodFromStack(Entity entity, ItemStack food)
    {
        FoodNutritionProperties? nutrition = food.Collectible.GetNutritionProperties(entity.World, food, entity);
        float restored = Math.Clamp(
            MinimumFoodPerItem + Math.Max(0f, nutrition?.Satiety ?? 0f) / SatietyForFullFoodBonus,
            MinimumFoodPerItem,
            MaximumFoodPerItem);
        RestoreCompanionFoodAmount(entity, restored * (1f + GetFoxPackTalentRank(entity, "better-portions") * 0.20f));
    }

    private void RestoreCompanionFoodAmount(Entity entity, float amount)
    {
        EnsureCompanionFoodState(entity);
        float restored = Math.Max(0f, amount);
        restored = Math.Clamp(
            restored,
            0f,
            1f);
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        float level = Math.Clamp(status.GetFloat(FoodLevelKey, 1f) + restored, 0f, 1f);
        status.SetFloat(FoodLevelKey, level);
        status.SetDouble(FoodLastUpdateTotalHoursKey, entity.World.Calendar.TotalHours);
        PersistCompanionFoodState(entity, status);
        lastFoodUiLevelByEntity[entity.EntityId] = level;
        MarkSocialStateDirty(entity);
        SendStateToOwner(entity, string.Empty);
    }

    private static bool IsWithinDiningBoardInteractionRadius(Entity entity, BlockPos diningPos)
    {
        // The route target is a safe standing position beside the board, not
        // the board's center. Keep this check horizontal so the board's
        // raised collision box does not make a correctly arrived companion
        // appear too far away vertically.
        double dx = entity.Pos.X - (diningPos.X + 0.5d);
        double dz = entity.Pos.Z - (diningPos.Z + 0.5d);
        return dx * dx + dz * dz <= DiningBoardInteractionRadius * DiningBoardInteractionRadius;
    }

    private IEnumerable<(FoxPackAmenityRecord amenity, BlockEntityFeralKinshipDiningBoard dining)>
        GetLoadedDiningBoards(Entity entity, bool requireSuitableFood)
    {
        if (serverApi == null || packRepository?.Loaded != true)
        {
            yield break;
        }

        string ownerUid = GetCompanionOwnerUid(entity);
        CreatureDiet? diet = requireSuitableFood ? GetCreatureDiet(entity) : null;
        if (string.IsNullOrWhiteSpace(ownerUid) || (requireSuitableFood && diet == null))
        {
            yield break;
        }

        GetCompanionCareScope(entity, out BlockPos careCenter, out double careRadius, out string careScope);
        int outsideCamp = 0;
        foreach (FoxPackAmenityRecord amenity in packRepository.GetAmenitiesForOwner(ownerUid)
                     .Where(record => record.Dimension == entity.Pos.Dimension
                         && string.Equals(record.Kind, DiningAmenityKind, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(record => entity.Pos.SquareDistanceTo(DiningPosition(record))))
        {
            // Apply the same camp boundary as storage BEFORE touching another
            // camp's block entities/inventories or submitting any path searches.
            if (!IsWithinStorageRoutingScope(amenity, careCenter, careRadius))
            {
                outsideCamp++;
                continue;
            }
            BlockPos pos = new(amenity.X, amenity.Y, amenity.Z, amenity.Dimension);
            BlockEntityFeralKinshipDiningBoard? dining = EnsureDiningBoardEntity(pos);
            if (dining == null) continue;
            if (requireSuitableFood && !ContainsSuitableFood(entity, dining, diet!)) continue;
            yield return (amenity, dining);
        }
        if (outsideCamp > 0 && DiagnosticLoggingEnabled)
            LogDutyDiagnostic(entity, "food-scope", $"scope={careScope} radius={careRadius:0.0} outside-camp={outsideCamp}");
    }

    private void GetCompanionCareScope(Entity entity, out BlockPos center, out double radius, out string scope)
    {
        // A delivery-to-main-cart flag must not move a Work Cart worker's food
        // or rest assignment to another camp.
        if (TryGetStorageRoutingScope(GetCompanionOwnerUid(entity), entity.Pos.Dimension, entity,
                out BlockPos? camp, out radius, out scope, useDeliveryOverride: false) && camp != null)
        {
            center = camp;
            return;
        }

        // Keep a nearby standalone Dining Board usable before a camp is placed.
        center = entity.Pos.AsBlockPos;
        radius = GetCompanionCampRadius(entity);
        scope = $"local@{center}";
    }

    private BlockEntityFeralKinshipDiningBoard? EnsureDiningBoardEntity(BlockPos pos)
    {
        if (serverApi == null || serverApi.World.BlockAccessor.GetChunkAtBlockPos(pos) == null)
        {
            return null;
        }

        Block block = serverApi.World.BlockAccessor.GetBlock(pos);
        if (!string.Equals(block.Code?.Path, DiningBoardBlockPath, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        BlockEntityFeralKinshipDiningBoard? dining =
            serverApi.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityFeralKinshipDiningBoard;
        if (dining == null && !string.IsNullOrWhiteSpace(block.EntityClass))
        {
            serverApi.World.BlockAccessor.SpawnBlockEntity(block.EntityClass, pos);
            dining = serverApi.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityFeralKinshipDiningBoard;
            dining?.MarkDirty(true);
        }
        return dining;
    }

    private static bool ContainsSuitableFood(Entity entity, BlockEntityFeralKinshipDiningBoard dining, CreatureDiet diet)
    {
        for (int index = 0; index < dining.Inventory.Count; index++)
        {
            ItemSlot slot = dining.Inventory[index];
            if (!slot.Empty && slot.Itemstack != null && IsSuitableDiningBoardFood(entity, slot.Itemstack, diet)) return true;
        }
        return false;
    }

    private static bool IsSuitableDiningBoardFood(Entity entity, ItemStack stack, CreatureDiet diet)
    {
        if (string.Equals(entity.Code.Domain, "cats", StringComparison.OrdinalIgnoreCase)
            && entity.Code.Path.StartsWith("cat-", StringComparison.OrdinalIgnoreCase))
        {
            AssetLocation code = stack.Collectible.Code;
            if (CatsCompanionFoodPaths.Contains(code.Path, StringComparer.OrdinalIgnoreCase)
                && (!string.Equals(code.Path, "petcookie-meat-perfect", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(code.Domain, "petai", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // Cats retain their existing explicit whitelist for ordinary
            // domains. Only the scoped optional Butchering exception may pass
            // after that whitelist; never fall through to generic diet tags.
            return MatchesOptionalButcheringFood(entity, stack, diet);
        }

        if (diet.Matches(stack)) return true;
        return MatchesOptionalButcheringFood(entity, stack, diet);
    }

    private static bool MatchesOptionalButcheringFood(Entity entity, ItemStack stack, CreatureDiet diet)
    {
        bool acceptsMeat = (diet.WeightedFoodTags?.Any(tag =>
                string.Equals(tag.Code, "meat", StringComparison.OrdinalIgnoreCase)) == true)
            || (diet.FoodCategories?.Contains(EnumFoodCategory.Protein) == true);
        return TryGetCompanionSpecies(entity, out CompanionSpeciesProfile species)
            && CompanionButcheringFoodPolicy.Matches(
                species.Id,
                stack.Collectible.Code.Domain,
                stack.Collectible.Code.Path,
                acceptsMeat);
    }

    private static CreatureDiet? GetCreatureDiet(Entity entity)
    {
        // Cats replaces its entity AI and does not consistently expose a
        // creatureDiet attribute on every age definition (notably kittens).
        // Companions needs the diet here for Dining Board discovery and
        // consumption, so keep the Cats compatibility contract explicit.
        if (string.Equals(entity.Code.Domain, "cats", StringComparison.OrdinalIgnoreCase)
            && entity.Code.Path.StartsWith("cat-", StringComparison.OrdinalIgnoreCase))
        {
            return CatsCompanionDiet;
        }

        return entity.Properties.Attributes?["creatureDiet"].AsObject<CreatureDiet>();
    }

    private Vec3d? ResolveStandingPositionBeside(Entity entity, BlockPos pos)
    {
        if (serverApi == null) return null;
        return FindSafeEntityPosition(pos, entity.Properties, entity, 2, 2)
            ?? DiningPosition(pos);
    }

    private static Vec3d DiningPosition(FoxPackAmenityRecord record) => new(
        record.X + 0.5,
        record.Y + record.Dimension * BlockPos.DimensionBoundary,
        record.Z + 0.5);

    private static Vec3d DiningPosition(BlockPos pos) => new(
        pos.X + 0.5,
        pos.Y + pos.dimension * BlockPos.DimensionBoundary,
        pos.Z + 0.5);
}
