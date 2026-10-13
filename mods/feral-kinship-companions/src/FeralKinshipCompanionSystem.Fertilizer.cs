#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
namespace FeralKinshipCompanions;

internal sealed record FertilizerJob(BlockPos Source, int Slot, BlockPos Crop, string CropCode,
    string ItemCode, Vec3d SourceApproach, Vec3d CropApproach);

public sealed partial class FeralKinshipCompanionSystem
{
    private const string FertilizerCarryKey = "feralKinshipFertilizerCarry";
    private readonly HashSet<BlockPos> applyingFertilizer = new();
    private static bool IsFertilizerEnabled(Entity entity)
        => GetDomesticationStatus(entity)?.GetBool(CompanionDuty.OptionKey(CompanionDuty.Fertilizer), false) == true;

    internal bool CanTendCrop(Entity entity, BlockPos target)
    {
        if (serverApi == null || packRepository?.Loaded != true || !entity.Alive || entity is not EntityAgent
            || IsCompanionJuvenile(entity) || IsFoxIncapacitated(entity) || IsFoxAwayFromWorld(entity)
            || IsCompanionOwnerOffline(entity) || IsCompanionFoodRestricted(entity)
            || GetCompanionActivityMode(entity) != CompanionActivityMode.AtEase
            || !IsFertilizerEnabled(entity) || !IsFinishedProductsEnabled(entity) || !IsFinishedCropsEnabled(entity)
            || target.dimension != entity.Pos.Dimension
            || serverApi.World.BlockAccessor.GetChunkAtBlockPos(target) == null
            || serverApi.World.BlockAccessor.GetChunkAtBlockPos(target.DownCopy()) == null
            || !CanCompanionModifyBlock(entity, target) || !CanCompanionModifyBlock(entity, target.DownCopy())) return false;
        string owner = GetCompanionOwnerUid(entity);
        string id = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (!packRepository.TryGetRecord(id, out var record) || record?.OwnerUid != owner) return false;
        bool previous = record.Wellbeing?.ReseedingUnlocked == true;
        bool ready = UpdateReseedingReadiness(record);
        if (previous != ready) packRepository.Save();
        if (!ready || !AllowsWellbeingDuty(entity, demanding: true)
            || !TryGetStorageRoutingScope(owner, target.dimension, entity, out var center, out double radius, out _, false)
            || center == null || !CompanionCampBounds.Contains(target.X, target.Z, target.dimension, center.X, center.Z, center.dimension, radius)) return false;
        return !IsFoxAssignedToWorkCart(entity)
            || TryGetAssignedWorkCart(entity, false, out var cart, out _) && cart?.OwnerUid == owner;
    }

    private bool TryFertilizerNeed(Entity entity, BlockPos cropPos, string? expectedCode,
        out BlockEntityFarmland? farm, out CompanionFertilizer.Need need)
    {
        farm = null; need = default;
        if (!CanTendCrop(entity, cropPos) || serverApi == null) return false;
        var blocks = serverApi.World.BlockAccessor;
        if (blocks.GetBlock(cropPos) is not BlockCrop crop
            || (expectedCode != null && crop.Code.ToString() != expectedCode)
            || blocks.GetBlockEntity(cropPos.DownCopy()) is not BlockEntityFarmland candidate
            || candidate.GetType() != typeof(BlockEntityFarmland)) return false;
        float[]? queued = CompanionFertilizer.Queue(candidate);
        if (queued == null || !CompanionFertilizer.TryNeed(crop, candidate.Nutrients, queued,
                companionConfig.FertilizerTargetNutrient, out need)) return false;
        var tree = new Vintagestory.API.Datastructures.TreeAttribute();
        candidate.ToTreeAttributes(tree);
        need = need with { OriginalFertility = candidate.OriginalFertility[need.Nutrient],
            AppliedBoosts = tree.GetStringArray("permaBoosts", Array.Empty<string>()) };
        farm = candidate; return true;
    }

    private bool TryFertilizerSupply(Entity entity, BlockPos pos, out IBlockEntityContainer? container)
    {
        container = null;
        if (serverApi == null || packRepository?.Loaded != true || pos.dimension != entity.Pos.Dimension
            || serverApi.World.BlockAccessor.GetChunkAtBlockPos(pos) == null) return false;
        string ownerUid = GetCompanionOwnerUid(entity);
        var record = packRepository.GetAmenity(pos);
        IPlayer? owner = GetCompanionBlockAccessPlayer(entity);
        if (record == null || record.OwnerUid != ownerUid || record.Kind is not ("storage" or "storage-target")
            || !IsStorageRoutingEnabled(record) || owner == null
            || serverApi.World.Claims.TestAccess(owner, pos, EnumBlockAccessFlags.Use) != EnumWorldAccessResponse.Granted
            || !TryGetStorageRoutingScope(ownerUid, pos.dimension, entity, out var center, out double radius, out _, false)
            || center == null || !IsWithinStorageRoutingScope(record, center, radius)) return false;
        var be = serverApi.World.BlockAccessor.GetBlockEntity(pos);
        if (be is not IBlockEntityContainer inventory
            || !FoxStorageRouting.IsSupportedStorageTarget(serverApi.World.BlockAccessor.GetBlock(pos), be)) return false;
        try { if (inventory.Inventory == null || inventory.Inventory.TakeLocked) return false; }
        catch { return false; }
        container = inventory; return true;
    }

    private bool TryGetFertilizerApproachTarget(Entity entity, BlockPos crop, out Vec3d? approach)
    {
        approach = null;
        if (serverApi == null || crop.dimension != entity.Pos.Dimension) return false;
        // Approaches describe feet positions, so a flat field is anchored at
        // crop height (one block above soil). Retain the storage helper's bounded
        // horizontal search and native validation without changing storage jobs.
        (int X, int Z)[] offsets = {
            (-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1),
            (-2, 0), (2, 0), (0, -2), (0, 2), (-2, -1), (-2, 1), (2, -1), (2, 1),
            (-1, -2), (1, -2), (-1, 2), (1, 2)
        };
        int[] heights = { 0, -1, -2, 1 }; // One-block uphill fallback for modest uneven ground.
        foreach (var offset in offsets.OrderBy(o => Math.Abs(o.X) + Math.Abs(o.Z))
                     .ThenBy(o => entity.Pos.SquareDistanceTo(new Vec3d(crop.X + o.X + .5,
                         crop.Y + crop.dimension * BlockPos.DimensionBoundary, crop.Z + o.Z + .5))))
        foreach (int dy in heights)
        {
            BlockPos feet = crop.AddCopy(offset.X, dy, offset.Z);
            var target = new Vec3d(feet.X + .5, feet.Y + feet.dimension * BlockPos.DimensionBoundary, feet.Z + .5);
            if (!IsValidFoxStorageApproach(entity, feet, feet.DownCopy(), target)
                && !IsValidNativeFarmlandApproach(entity, feet, target)) continue;
            approach = target; return true;
        }
        return false;
    }

    private bool IsValidNativeFarmlandApproach(Entity entity, BlockPos feet, Vec3d target)
    {
        // Native farmland is 15/16 high and SideSolid.UP=false. Accept its real
        // surface only, within the existing navigation support tolerance; custom
        // support classes and speculative partial blocks are not inferred.
        var blocks = serverApi!.World.BlockAccessor;
        BlockPos below = feet.DownCopy();
        var support = blocks.GetBlock(below);
        if (support.GetType() != typeof(BlockFarmland)
            || blocks.GetChunkAtBlockPos(feet) == null || blocks.GetChunkAtBlockPos(below) == null
            || blocks.IsNotTraversable(feet) || blocks.IsNotTraversable(feet.UpCopy())
            || blocks.GetBlock(feet, BlockLayersAccess.Fluid).IsLiquid()
            || blocks.GetBlock(feet.UpCopy(), BlockLayersAccess.Fluid).IsLiquid()
            || serverApi.World.CollisionTester.IsColliding(blocks, entity.CollisionBox, target, false)) return false;
        var boxes = support.GetCollisionBoxes(blocks, below);
        return boxes != null && boxes.Any(box => box.MinX <= .5f && box.MaxX >= .5f
            && box.MinZ <= .5f && box.MaxZ >= .5f
            && feet.Y - (below.Y + box.MaxY) >= 0 && feet.Y - (below.Y + box.MaxY) <= .12);
    }

    internal bool TryPlanFertilizer(Entity entity, out FertilizerJob? job)
    {
        job = null;
        if (serverApi == null || HasFoxStorageCargo(entity) || HasFoxCommandedStorageJob(entity)
            || !IsFertilizerEnabled(entity) || IsCompanionOwnerOffline(entity) || IsCompanionFoodRestricted(entity)
            || GetCompanionActivityMode(entity) != CompanionActivityMode.AtEase
            || !IsFinishedProductsEnabled(entity) || !IsFinishedCropsEnabled(entity)
            || !TryGetGroundCleanupCenter(entity, out var center) || center == null) return false;
        // Bounded local discovery on the normal duty cadence. Never expand the work boundary.
        int range = Math.Min(12, GetGroundCleanupSearchRange(entity));
        var supplies = new List<(BlockPos Position, int Slot, ItemStack Stack, Vec3d Approach)>();
        foreach (var record in packRepository!.GetAmenitiesForOwner(GetCompanionOwnerUid(entity)))
        {
            var pos = new BlockPos(record.X, record.Y, record.Z, record.Dimension);
            if (!TryFertilizerSupply(entity, pos, out var container)
                || !TryGetFoxStorageApproachTarget(entity, pos, out var approach) || approach == null) continue;
            for (int i = 0; i < container!.Inventory.Count; i++)
            {
                ItemStack? stack = container.Inventory[i]?.Itemstack;
                if (stack?.StackSize is > 0 && CompanionFertilizer.SupportedSourceSlot(container.Inventory[i]) && StorageRoutingAccepts(record, stack))
                    supplies.Add((pos, i, stack.Clone(), approach));
            }
        }
        if (supplies.Count == 0) return false;
        double best = double.MaxValue;
        // Look near the companion as it walks its existing camp, including modest slopes.
        BlockPos origin = entity.Pos.AsBlockPos;
        for (int dx = -range; dx <= range; dx++)
        for (int dz = -range; dz <= range; dz++)
        for (int dy = -3; dy <= 3; dy++)
        {
            if (dx * dx + dz * dz > range * range) continue;
            BlockPos crop = origin.AddCopy(dx, dy, dz);
            if (serverApi.World.BlockAccessor.GetChunkAtBlockPos(crop) == null
                || serverApi.World.BlockAccessor.GetBlock(crop) is not BlockCrop
                || !TryFertilizerNeed(entity, crop, null, out _, out var need)
                || !TryGetFertilizerApproachTarget(entity, crop, out var approach) || approach == null) continue;
            foreach (var supply in supplies)
            {
                if (!CompanionFertilizer.TryDose(supply.Stack, need, companionConfig.FertilizerMaxOvershoot, out double waste)) continue;
                double score = waste * 100000 + entity.Pos.SquareDistanceTo(supply.Approach) + supply.Approach.SquareDistanceTo(approach);
                if (score >= best) continue;
                best = score;
                job = new FertilizerJob(supply.Position, supply.Slot, crop, need.CropCode,
                    supply.Stack.Collectible.Code.ToString(), supply.Approach, approach);
            }
        }
        return job != null;
    }

    internal bool TakeFertilizerDose(Entity entity, FertilizerJob job)
    {
        if (HasFoxStorageCargo(entity) || entity.Pos.SquareDistanceTo(job.SourceApproach) > 4
            || !TryFertilizerNeed(entity, job.Crop, job.CropCode, out _, out var need)
            || !TryFertilizerSupply(entity, job.Source, out var source)
            || job.Slot < 0 || job.Slot >= source!.Inventory.Count) return false;
        var slot = source.Inventory[job.Slot];
        if (!CompanionFertilizer.SupportedSourceSlot(slot) || slot?.Itemstack?.Collectible.Code.ToString() != job.ItemCode
            || !StorageRoutingAccepts(packRepository!.GetAmenity(job.Source)!, slot.Itemstack)
            || !CompanionFertilizer.TryDose(slot.Itemstack, need, companionConfig.FertilizerMaxOvershoot, out _)) return false;
        // Native TakeOut(1) calls TakeOutWhole for the final item. Its inventory
        // notification runs after removal and can throw before returning cargo.
        // Transfer that real final stack through the nonvirtual Itemstack property
        // instead; the existing notification below runs only after cargo persists.
        // Both withdrawal overrides are excluded above. Never restore a snapshot
        // after arbitrary callbacks: the detached item itself remains accounted for.
        ItemStack? one;
        if (slot.Itemstack.StackSize == 1)
        {
            one = slot.Itemstack;
            slot.Itemstack = null;
        }
        else one = slot.TakeOut(1);
        if (one?.StackSize is not > 0) return false;
        // Cargo is recorded before notification hooks, so interruption cannot erase the dose.
        entity.WatchedAttributes.SetItemstack(FoxStorageCarryKey, one);
        entity.WatchedAttributes.SetBool(FertilizerCarryKey, true);
        entity.WatchedAttributes.MarkPathDirty(FertilizerCarryKey);
        SetFoxStorageReturnSource(entity, job.Source);
        entity.WatchedAttributes.SetBool(FoxStorageCommandedCarryKey, false);
        entity.WatchedAttributes.MarkPathDirty(FoxStorageCarryKey);
        try { slot.MarkDirty(); (source as BlockEntity)?.MarkDirty(); }
        catch (Exception error)
        {
            serverApi!.Logger.Warning("[FeralKinshipCompanions] Fertilizer pickup notification failed: {0}", error);
            return false; // The real withdrawn item remains persisted cargo.
        }
        return one.StackSize == 1 && one.Collectible.Code.ToString() == job.ItemCode;
    }

    internal bool ApplyFertilizerDose(Entity entity, FertilizerJob job)
    {
        if (serverApi == null || entity.Pos.SquareDistanceTo(job.CropApproach) > 4
            || !applyingFertilizer.Add(job.Crop)) return false;
        try
        {
            ItemStack? carried = entity.WatchedAttributes.GetItemstack(FoxStorageCarryKey);
            if (carried?.StackSize != 1 || carried.Collectible.Code.ToString() != job.ItemCode
                || !TryFertilizerNeed(entity, job.Crop, job.CropCode, out var farm, out var need)
                || !CompanionFertilizer.TryDose(carried, need, companionConfig.FertilizerMaxOvershoot, out _)
                || GetCompanionBlockAccessPlayer(entity) is not { } owner) return false;
            float[] before = CompanionFertilizer.Queue(farm!)!;
            var escrow = new DummySlot(carried);
            // Clear carried dose before native callbacks: it is now held by this one action.
            entity.WatchedAttributes.SetItemstack(FoxStorageCarryKey, null);
            bool applied = false;
            try { applied = farm!.OnBlockInteract(new FertilizerPlayer(owner, escrow)); }
            catch (Exception error)
            {
                serverApi.Logger.Warning("[FeralKinshipCompanions] Fertilizer callback failed at {0}: {1}", job.Crop, error);
            }
            finally
            {
                float[]? after = CompanionFertilizer.Queue(farm!);
                bool changed = after == null || !before.SequenceEqual(after);
                // Native code changes soil before consuming. A notification failure
                // after that change must not refund a spent dose and duplicate nutrients.
                if (!changed && !escrow.Empty) entity.WatchedAttributes.SetItemstack(FoxStorageCarryKey, escrow.Itemstack);
                else { farm!.MarkDirty(true); ClearFoxStorageReturnSource(entity); entity.WatchedAttributes.RemoveAttribute(FertilizerCarryKey); }
                entity.WatchedAttributes.MarkPathDirty(FoxStorageCarryKey);
            }
            if (applied) RecordWellbeingDuty(entity, .25, label: "Tended a growing crop");
            return applied;
        }
        finally { applyingFertilizer.Remove(job.Crop); }
    }
}
