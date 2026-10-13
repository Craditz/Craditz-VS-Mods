#nullable enable
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
namespace FeralKinshipCompanions;

internal sealed record TroughRefillJob(BlockPos Source, int Slot, BlockPos Trough, string Token,
    ItemStack Food, int Portion, Vec3d SourceApproach, Vec3d TroughApproach)
{
    internal int PickupStarted, ApplyStarted;
}
public sealed partial class FeralKinshipCompanionSystem
{
    private const string TroughCarryKey = "feralKinshipTroughCarry";
    private readonly HashSet<BlockPos> fillingTroughs = new();
    private static bool IsTroughRefillEnabled(Entity entity)
        => GetDomesticationStatus(entity)?.GetBool(CompanionDuty.OptionKey(CompanionDuty.TroughRefill), false) == true;
    private string GetTroughRefillRequirement(Entity entity)
    {
        if (IsCompanionOwnerOffline(entity)) return "Paused: owner offline";
        if (GetCompanionActivityMode(entity) != CompanionActivityMode.AtEase) return "Needs At Ease";
        if (IsFoxIncapacitated(entity) || IsFoxAwayFromWorld(entity) || !entity.Alive) return "Needs an available companion";
        if (IsCompanionFoodRestricted(entity)) return "Needs food before extra duties";
        string id = GetDomesticationStatus(entity)?.GetString(FoxIdKey, "") ?? "";
        if (packRepository?.TryGetRecord(id, out var record) != true || record?.OwnerUid != GetCompanionOwnerUid(entity)
            || record.Wellbeing is not { HasObservation: true } wellbeing || wellbeing.Version != CompanionWellbeingRules.Version
            || !float.IsFinite(wellbeing.Value)) return "Needs a current wellbeing check";
        if (!wellbeing.HasEstablishedHome) return "Needs an established home";
        bool ready = CompanionReseeding.Update(wellbeing.Copy(), companionConfig.ReseedingUnlockWellbeing, companionConfig.ReseedingPauseWellbeing);
        float required = ready ? companionConfig.ReseedingPauseWellbeing : companionConfig.ReseedingUnlockWellbeing;
        return $"Wellbeing {wellbeing.Value:R}/{required:G} - " + (ready ? "requirement met" : "needs higher wellbeing");
    }
    internal bool CanRefillTrough(Entity entity, BlockPos pos)
    {
        if (serverApi == null || packRepository?.Loaded != true || entity is not EntityAgent || !IsTamedFox(entity) || !entity.Alive
            || IsCompanionJuvenile(entity) || IsFoxIncapacitated(entity) || IsFoxAwayFromWorld(entity)
            || IsCompanionOwnerOffline(entity) || IsCompanionFoodRestricted(entity) || !IsTroughRefillEnabled(entity)
            || GetCompanionActivityMode(entity) != CompanionActivityMode.AtEase || pos.dimension != entity.Pos.Dimension
            || serverApi.World.BlockAccessor.GetChunkAtBlockPos(pos) == null) return false;
        string ownerUid = GetCompanionOwnerUid(entity), id = GetDomesticationStatus(entity)?.GetString(FoxIdKey, "") ?? "";
        if (!packRepository.TryGetRecord(id, out var record) || record?.OwnerUid != ownerUid) return false;
        bool previous = record.Wellbeing?.ReseedingUnlocked == true, ready = UpdateReseedingReadiness(record);
        if (previous != ready) packRepository.Save();
        if (!ready || !AllowsWellbeingDuty(entity, demanding: true)
            || !TryGetStorageRoutingScope(ownerUid, pos.dimension, entity, out var center, out double radius, out _, false)
            || center == null || !CompanionCampBounds.Contains(pos.X, pos.Z, pos.dimension, center.X, center.Z, center.dimension, radius)
            || GetCompanionBlockAccessPlayer(entity) is not { } owner
            || serverApi.World.Claims.TestAccess(owner, pos, EnumBlockAccessFlags.Use) != EnumWorldAccessResponse.Granted) return false;
        if (IsFoxAssignedToWorkCart(entity)
            && (!TryGetAssignedWorkCart(entity, false, out var cart, out _) || cart?.OwnerUid != ownerUid)) return false;
        if (serverApi.World.BlockAccessor.GetBlock(pos) is BlockTroughDoubleBlock large)
        {
            var other = large.OtherPartPos(pos);
            if (serverApi.World.BlockAccessor.GetChunkAtBlockPos(other) == null
                || large.Variant["part"] != "large-head"
                || serverApi.World.BlockAccessor.GetBlock(other) is not BlockTroughDoubleBlock second
                || second.GetType() != typeof(BlockTroughDoubleBlock) || second.Variant["part"] != "large-feet"
                || !second.OtherPartPos(other).Equals(pos) || second.Variant["side"] != large.Variant["side"]
                || serverApi.World.Claims.TestAccess(owner, other, EnumBlockAccessFlags.Use) != EnumWorldAccessResponse.Granted) return false;
        }
        return true;
    }
    private bool TryRefillTarget(Entity entity, BlockPos pos, string? token, out BlockEntityTrough? trough, out TroughCare? state, out int portion)
    {
        trough = null; state = null; portion = 0;
        if (!CanRefillTrough(entity, pos) || serverApi!.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityTrough found
            || !CompanionTroughCare.Supported(found) || !found.Pos.Equals(pos)) return false;
        var remembered = CompanionTroughCare.Get(found);
        if (remembered == null || remembered.OwnerUid != GetCompanionOwnerUid(entity)
            || (token != null && remembered.Token != token) || !CompanionTroughCare.TryPortion(found, remembered.Food, out portion)) return false;
        trough = found; state = remembered; return true;
    }
    internal bool TryPlanTroughRefill(Entity entity, out TroughRefillJob? job)
    {
        job = null;
        if (serverApi == null || HasFoxStorageCargo(entity) || HasFoxCommandedStorageJob(entity) || !IsTroughRefillEnabled(entity)
            || !TryGetGroundCleanupCenter(entity, out var center) || center == null) return false;
        int range = Math.Min(12, GetGroundCleanupSearchRange(entity)); var origin = entity.Pos.AsBlockPos;
        double best = double.MaxValue;
        for (int dx = -range; dx <= range; dx++)
        for (int dz = -range; dz <= range; dz++)
        for (int dy = -3; dy <= 3; dy++)
        {
            if (dx * dx + dz * dz > range * range) continue;
            var pos = origin.AddCopy(dx, dy, dz);
            if (serverApi.World.BlockAccessor.GetChunkAtBlockPos(pos) == null
                || serverApi.World.BlockAccessor.GetBlock(pos) is not BlockTroughBase
                || !TryRefillTarget(entity, pos, null, out _, out var state, out int portion)
                || !TryGetFoxStorageApproachTarget(entity, pos, out var targetApproach) || targetApproach == null) continue;
            foreach (var supplyRecord in packRepository!.GetAmenitiesForOwner(GetCompanionOwnerUid(entity)))
            {
                var source = new BlockPos(supplyRecord.X, supplyRecord.Y, supplyRecord.Z, supplyRecord.Dimension);
                if (!TryFertilizerSupply(entity, source, out var container)
                    || !TryGetFoxStorageApproachTarget(entity, source, out var sourceApproach) || sourceApproach == null) continue;
                for (int i = 0; i < container!.Inventory.Count; i++)
                {
                    var slot = container.Inventory[i]; var food = slot?.Itemstack;
                    if (!CompanionFertilizer.SupportedSourceSlot(slot) || food?.StackSize < portion
                        || !CompanionTroughCare.SameFood(serverApi.World, food, state!.Food) || !StorageRoutingAccepts(supplyRecord, food!)) continue;
                    double distance = entity.Pos.SquareDistanceTo(sourceApproach) + sourceApproach.SquareDistanceTo(targetApproach);
                    if (distance >= best) continue;
                    best = distance; job = new(source, i, pos, state.Token, state.Food.Clone(), portion, sourceApproach, targetApproach);
                }
            }
        }
        return job != null;
    }
    internal bool TakeTroughPortion(Entity entity, TroughRefillJob job)
    {
        if (HasFoxStorageCargo(entity) || entity.Pos.SquareDistanceTo(job.SourceApproach) > 4
            || !TryRefillTarget(entity, job.Trough, job.Token, out _, out var state, out int portion) || portion != job.Portion
            || !CompanionTroughCare.SameFood(serverApi!.World, job.Food, state!.Food)
            || !TryFertilizerSupply(entity, job.Source, out var source) || job.Slot < 0 || job.Slot >= source!.Inventory.Count) return false;
        var slot = source.Inventory[job.Slot]; var food = slot?.Itemstack;
        if (!CompanionFertilizer.SupportedSourceSlot(slot) || food?.StackSize < portion
            || !CompanionTroughCare.SameFood(serverApi.World, food, state.Food)
            || !StorageRoutingAccepts(packRepository!.GetAmenity(job.Source)!, food!)) return false;
        if (System.Threading.Interlocked.CompareExchange(ref job.PickupStarted, 1, 0) != 0) return false;
        // Native whole withdrawal notifies before returning. Detach the actual
        // final portion first and record finite cargo before any inventory hook.
        ItemStack? carried;
        if (food!.StackSize == portion) { carried = food; slot!.Itemstack = null; }
        else carried = slot!.TakeOut(portion);
        if (carried?.StackSize != portion) return false;
        entity.WatchedAttributes.SetItemstack(FoxStorageCarryKey, carried);
        entity.WatchedAttributes.SetBool(TroughCarryKey, true);
        entity.WatchedAttributes.SetBool(FoxStorageCommandedCarryKey, false);
        SetFoxStorageReturnSource(entity, job.Source);
        entity.WatchedAttributes.MarkPathDirty(TroughCarryKey); entity.WatchedAttributes.MarkPathDirty(FoxStorageCarryKey);
        try { slot.MarkDirty(); (source as BlockEntity)?.MarkDirty(); }
        catch (Exception error) { serverApi!.Logger.Warning("[FeralKinshipCompanions] Trough pickup notification failed: {0}", error); return false; }
        return true;
    }
    internal bool ApplyTroughPortion(Entity entity, TroughRefillJob job)
    {
        if (serverApi == null || entity.Pos.SquareDistanceTo(job.TroughApproach) > 4 || !fillingTroughs.Add(job.Trough)) return false;
        try
        {
            var carried = entity.WatchedAttributes.GetItemstack(FoxStorageCarryKey);
            if (!entity.WatchedAttributes.GetBool(TroughCarryKey) || carried?.StackSize != job.Portion
                || !TryRefillTarget(entity, job.Trough, job.Token, out var trough, out var state, out int portion) || portion != job.Portion
                || !CompanionTroughCare.SameFood(serverApi.World, carried, state!.Food)
                || GetCompanionBlockAccessPlayer(entity) is not { } owner) return false;
            if (System.Threading.Interlocked.CompareExchange(ref job.ApplyStarted, 1, 0) != 0) return false;
            var escrow = new DummySlot(carried); bool applied = false;
            entity.WatchedAttributes.SetItemstack(FoxStorageCarryKey, null);
            try
            {
                applied = trough!.Block.OnBlockInteractStart(serverApi.World, new FertilizerPlayer(owner, escrow),
                    new BlockSelection { Position = job.Trough.Copy(), Face = BlockFacing.UP });
            }
            catch (Exception error) { serverApi.Logger.Warning("[FeralKinshipCompanions] Trough interaction failed: {0}", error); }
            finally
            {
                // Restore only the actual unspent escrow item. If native code
                // consumed it before a notification exception, never refund it.
                if (!escrow.Empty) entity.WatchedAttributes.SetItemstack(FoxStorageCarryKey, escrow.Itemstack);
                else { ClearFoxStorageReturnSource(entity); entity.WatchedAttributes.RemoveAttribute(TroughCarryKey); }
                entity.WatchedAttributes.MarkPathDirty(FoxStorageCarryKey); entity.WatchedAttributes.MarkPathDirty(TroughCarryKey);
            }
            if (applied && escrow.Empty) RecordWellbeingDuty(entity, .25, label: "Refilled a remembered trough");
            return applied && escrow.Empty;
        }
        finally { fillingTroughs.Remove(job.Trough); }
    }
}
