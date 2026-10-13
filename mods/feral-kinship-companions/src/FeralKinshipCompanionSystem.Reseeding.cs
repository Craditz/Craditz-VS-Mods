#nullable enable
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private static bool IsReseedingEnabled(Entity entity)
        => GetDomesticationStatus(entity)?.GetBool(CompanionDuty.OptionKey(CompanionDuty.Reseeding), false) == true;

    private bool UpdateReseedingReadiness(FoxPackRecordV2 record)
        => CompanionReseeding.Update(record.Wellbeing, companionConfig.ReseedingUnlockWellbeing, companionConfig.ReseedingPauseWellbeing);

    private string GetReseedingStatus(Entity entity)
    {
        return (IsReseedingEnabled(entity) ? "Selected. " : "Not enabled. ")
            + GetReseedingRequirement(entity) + " " + GetReseedingThresholds();
    }

    private string GetReseedingThresholds()
        => $"Unlock at {companionConfig.ReseedingUnlockWellbeing:G}; pauses below {companionConfig.ReseedingPauseWellbeing:G}.";

    private string GetReseedingRequirement(Entity entity)
    {
        if (!IsFinishedProductsEnabled(entity) || !IsFinishedCropsEnabled(entity)) return "Needs crop gathering enabled";
        if (IsCompanionOwnerOffline(entity)) return "Paused: owner offline";
        if (GetCompanionActivityMode(entity) != CompanionActivityMode.AtEase) return "Needs At Ease";
        if (IsFoxIncapacitated(entity) || IsFoxAwayFromWorld(entity) || !entity.Alive) return "Needs an available companion";
        if (IsCompanionFoodRestricted(entity)) return "Needs food before extra duties";
        string id = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (packRepository?.TryGetRecord(id, out var record) != true || record?.OwnerUid != GetCompanionOwnerUid(entity)
            || record.Wellbeing is not { HasObservation: true } wellbeing
            || wellbeing.Version != CompanionWellbeingRules.Version || !float.IsFinite(wellbeing.Value))
            return "Needs a current wellbeing check";
        if (!wellbeing.HasEstablishedHome) return "Needs an established home";
        // Display the actual hysteresis requirement without changing saved readiness.
        bool ready = CompanionReseeding.Update(wellbeing.Copy(), companionConfig.ReseedingUnlockWellbeing, companionConfig.ReseedingPauseWellbeing);
        float required = ready ? companionConfig.ReseedingPauseWellbeing : companionConfig.ReseedingUnlockWellbeing;
        // Round-trip precision keeps even the adjacent float below a threshold
        // distinct from that threshold. Display does not alter eligibility.
        return $"Wellbeing {wellbeing.Value:R}/{required:G} - " + (ready ? "requirement met" : "needs higher wellbeing");
    }

    private bool CanReseedPlot(Entity entity, BlockPos target)
    {
        if (serverApi == null || packRepository?.Loaded != true || entity is not EntityAgent
            || !entity.Alive || IsFoxIncapacitated(entity) || IsFoxAwayFromWorld(entity)
            || IsCompanionOwnerOffline(entity) || IsCompanionFoodRestricted(entity)
            || GetCompanionActivityMode(entity) != CompanionActivityMode.AtEase
            || !IsReseedingEnabled(entity) || !IsFinishedProductsEnabled(entity) || !IsFinishedCropsEnabled(entity)
            || target.dimension != entity.Pos.Dimension
            || serverApi.World.BlockAccessor.GetChunkAtBlockPos(target) == null
            || !CanCompanionModifyBlock(entity, target) || !CanCompanionModifyBlock(entity, target.DownCopy())) return false;
        string owner = GetCompanionOwnerUid(entity);
        string id = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (!packRepository.TryGetRecord(id, out var record) || record == null || record.OwnerUid != owner) return false;
        bool wasReady = record.Wellbeing?.ReseedingUnlocked == true;
        bool ready = UpdateReseedingReadiness(record);
        if (wasReady != ready) packRepository.Save();
        if (!ready || !AllowsWellbeingDuty(entity, demanding: true)) return false;
        if (!TryGetStorageRoutingScope(owner, target.dimension, entity, out BlockPos? center,
                out double radius, out _, useDeliveryOverride: false) || center == null) return false;
        double dx = target.X - center.X, dz = target.Z - center.Z;
        if (dx * dx + dz * dz > radius * radius) return false;
        if (IsFoxAssignedToWorkCart(entity)
            && (!TryGetAssignedWorkCart(entity, false, out var cart, out _) || cart?.OwnerUid != owner)) return false;
        return true;
    }

    private void TryReseedHarvestedCrop(Entity entity, BlockPos target, BlockCrop harvested, List<ItemStack> outputs)
    {
        if (serverApi == null || entity is not EntityAgent agent || !CanReseedPlot(entity, target)) return;
        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        if (blocks.GetBlockEntity(target.DownCopy()) is not BlockEntityFarmland farmland || !farmland.CanPlant()) return;
        int separator = harvested.Code.Path.LastIndexOf('-');
        if (separator < 0) return;
        // AssetLocation.WithPath mutates its receiver. Never rewrite the shared
        // ripe block code while constructing the stage-one lookup identity.
        AssetLocation expected = harvested.Code.Clone().WithPath(harvested.Code.Path[..(separator + 1)] + "1");
        foreach (ItemStack output in outputs)
        {
            if (output.StackSize <= 0 || output.Collectible is not ItemPlantableSeed seed) continue;
            string? type = seed.Variant?["type"];
            if (string.IsNullOrEmpty(type) || type == "bellpepper") continue;
            Block? crop = serverApi.World.GetBlock(seed.CodeWithPath("crop-" + type + "-1"));
            if (crop is not BlockCrop || crop.CropProps == null || !crop.Code.Equals(expected)) continue;
            ItemSlot source = new DummySlot(output);
            try
            {
                CompanionReseeding.TryPlant(source,
                () => CanReseedPlot(entity, target) && farmland.CanPlant()
                    && ReferenceEquals(blocks.GetBlockEntity(target.DownCopy()), farmland),
                slot => farmland.TryPlant(crop, slot, agent, new BlockSelection
                { Position = target.DownCopy(), Face = BlockFacing.UP, HitPosition = new Vec3d(.5, 1, .5) }),
                () => blocks.GetBlock(target).Id != 0,
                    error => serverApi.Logger.Warning("[FeralKinshipCompanions] Reseeding callback failed at {0}: {1}", target, error.Message));
            }
            finally { output.StackSize = source.StackSize; }
            if (blocks.GetBlock(target).Id == crop.Id) blocks.GetBlockEntity(target.DownCopy())?.MarkDirty(true);
            return; // At most one seed and one attempt for this harvest.
        }
    }
}
