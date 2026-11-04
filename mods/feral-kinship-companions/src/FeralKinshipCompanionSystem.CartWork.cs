#nullable enable
using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;
public sealed partial class FeralKinshipCompanionSystem
{
    // One calendar hour to haul debris before this animal's perk-adjusted bedtime.
    private bool IsCartWindDown(Entity fox)
    {
        var c = fox.World.Calendar;
        double bedtime = (19.5 + Math.Max(0, GetFoxPackTalentRank(fox, "later-bedtime"))) * c.HoursPerDay / 24d;
        return c.HourOfDay >= bedtime - c.HoursPerDay / 24d || IsCompanionBedtime(fox);
    }

    internal bool TryGetCartJobApproach(Entity fox, WorkCartQuarryJob job, out Vec3d? approach)
    {
        if (job.Kind is not (QuarryJobKind.Mine or QuarryJobKind.Build)) { approach = job.Approach; return true; }
        return job.Cart.MiningMode ? TryMiningApproach(fox, job.Cart, job.Target, job.Kind == QuarryJobKind.Build, out approach)
            : TryQuarryApproach(fox, job.Cart, job.Target, job.Kind == QuarryJobKind.Build, out approach);
    }

    private bool TryJoinCartJob(Entity fox, BlockEntityFeralKinshipWorkCart cart, out WorkCartQuarryJob? job)
    {
        job = null;
        foreach (var current in cart.QuarryJobs.Where(j => !j.Done).OrderBy(j => j.Workers.Count))
        {
            Vec3d? approach;
            if (cart.MiningMode ? !TryMiningApproach(fox, cart, current.Target, current.Kind == QuarryJobKind.Build, out approach)
                : !TryQuarryApproach(fox, cart, current.Target, current.Kind == QuarryJobKind.Build, out approach)) continue;
            current.Workers[fox.EntityId] = serverApi!.World.ElapsedMilliseconds;
            job = current; return true;
        }
        return false;
    }

    private void BreakCartWorkBlock(Entity fox, ItemSlot pick, BlockSelection selection)
    {
        Block b = selection.Block;
        if (b.BlockMaterial is EnumBlockMaterial.Stone or EnumBlockMaterial.Ore)
            pick.Itemstack!.Collectible.OnBlockBrokenWith(serverApi!.World, fox, pick, selection);
        else
        {
            // Native block drops/behaviors, with no pickaxe wear on soil, clay or vegetation.
            b.OnBlockBroken(serverApi!.World, selection.Position, null);
        }
    }

    private void SyncCartWorkBlock(BlockPos pos)
    {
        var blocks = serverApi!.World.BlockAccessor;
        blocks.MarkBlockDirty(pos);
        foreach (var face in BlockFacing.ALLFACES) blocks.MarkBlockDirty(pos.AddCopy(face));
        blocks.TriggerNeighbourBlockUpdate(pos);
    }

    private void MaintainQuarryRemnants(BlockEntityFeralKinshipWorkCart cart)
    {
        var q = cart.Quarry;
        if (!q.Started || !q.Enabled || q.CompletedCutY == int.MaxValue || serverApi == null || packRepository == null) return;
        var record = packRepository.GetWorkCart(cart.Pos);
        var owner = record == null ? null : serverApi.World.PlayerByUid(record.OwnerUid);
        if (owner == null) return;
        int index = cart.QuarryRemnantColumn++ % (q.Width * q.Width);
        int x = q.MinX + index % q.Width, z = q.MinZ + index / q.Width;
        var blocks = serverApi.World.BlockAccessor;
        // One previously cleared column per cart maintenance pass. Never sweep a
        // new layer or a cave. Grass regrowth/stale plants cannot survive in air.
        for (int y = q.SurveyTop; y >= Math.Max(q.CompletedCutY, q.SurveyTop - 96); y--)
        {
            var point = new QuarryPoint(x, y, z); var pos = point.Position(cart.Pos.dimension);
            if (blocks.GetChunkAtBlockPos(pos) == null) break;
            Block actual = blocks.GetBlock(pos);
            if (actual.Id == 0 || q.IsProtected(point) || q.IsStructure(point) || q.IsEntrance(point)) continue;
            if (actual.BlockMaterial == EnumBlockMaterial.Plant && QuarryNatural(actual)
                && blocks.GetBlock(pos.DownCopy()).Id == 0 && CanPlayerModifyBlock(owner, pos))
            {
                actual.OnBlockBroken(serverApi.World, pos, null); SyncCartWorkBlock(pos);
            }
            else if (QuarryNatural(actual) && !cart.QuarryJobs.Any(j => j.Target.Equals(pos))) q.QueueRemnant(point);
        }
    }

    private bool TryCreateCartToolJob(Entity fox, out WorkCartQuarryJob? job)
    {
        job = null;
        if (serverApi == null || packRepository == null
            || !TryGetAssignedWorkCart(fox, false, out var record, out var pos) || pos == null
            || record?.Kind is not ("logging" or "mining" or "quarrying")) return false;
        var cart = EnsureWorkCartTools(pos);
        if (cart == null || !CanContinueQuarry(fox, cart)) return false;
        var cargo = fox.WatchedAttributes.GetItemstack(QuarryCarryKey);
        if (cargo?.StackSize > 0)
        {
            if (!cart.Tools.Accepts(0, cargo)) return false;
            if (fox.WatchedAttributes.GetBool("cartToolReturn")) return TryCreateQuarryDelivery(fox, cart, cargo, out job);
            if (!TryGetFoxStorageApproachTarget(fox, pos, out var approach) || approach == null) return false;
            job = new() { Cart = cart, Target = pos, Approach = approach, Kind = QuarryJobKind.DeliverTool };
            return true;
        }
        if (IsCartWindDown(fox) || !Enumerable.Range(0, 4).Any(i => cart.Tools[i].Empty && !cart.Tools.IsReserved(i))
            || cart.QuarrySupplyWorker != 0 && cart.QuarrySupplyWorker != fox.EntityId
                && cart.QuarrySupplyLease > serverApi.World.ElapsedMilliseconds) return false;
        // Keep the dedicated rock buffer's refill priority ahead of extra tools.
        if (cart.Tools.QuarryMode && TryCreateQuarrySupply(fox, cart, out job)) return true;
        string owner = record.OwnerUid;
        foreach (var storage in packRepository.GetAmenitiesForOwner(owner).Where(a => a.Dimension == pos.dimension
            && a.Kind is "storage" or "storage-target").OrderBy(a => fox.Pos.SquareDistanceTo(new Vec3d(a.X, a.Y, a.Z))))
        {
            var at = new BlockPos(storage.X, storage.Y, storage.Z, storage.Dimension);
            if (at.HorDistanceSqTo(pos.X, pos.Z) > GetWorkCartRadius(fox) * GetWorkCartRadius(fox)
                || !QuarryContainerAllowed(fox, at, false, null, out var inventory, out _)
                || !TryGetFoxStorageApproachTarget(fox, at, out var approach) || approach == null) continue;
            for (int i = 0; i < inventory!.Count; i++)
            {
                var slot = inventory[i];
                if (slot?.Itemstack == null || !cart.Tools.Accepts(0, slot.Itemstack)
                    || slot.Itemstack.Collectible.GetRemainingDurability(slot.Itemstack) <= 0) continue;
                job = new() { Cart = cart, Target = at, Approach = approach, Kind = QuarryJobKind.WithdrawTool, SourceSlot = i };
                cart.QuarrySupplyWorker = fox.EntityId; cart.QuarrySupplyLease = serverApi.World.ElapsedMilliseconds + 15000;
                return true;
            }
        }
        return false;
    }

    private bool ExecuteCartToolJob(Entity fox, WorkCartQuarryJob job)
    {
        if (serverApi == null) return false;
        var cargo = fox.WatchedAttributes.GetItemstack(QuarryCarryKey);
        if (job.Kind == QuarryJobKind.WithdrawTool)
        {
            if (cargo?.StackSize > 0 || !QuarryContainerAllowed(fox, job.Target, false, null, out var inv, out var be)
                || job.SourceSlot < 0 || job.SourceSlot >= inv!.Count) return false;
            var source = inv[job.SourceSlot];
            if (source?.Itemstack == null || !job.Cart.Tools.Accepts(0, source.Itemstack)
                || source.Itemstack.Collectible.GetRemainingDurability(source.Itemstack) <= 0
                || !Enumerable.Range(0, 4).Any(i => job.Cart.Tools[i].Empty && !job.Cart.Tools.IsReserved(i))) return false;
            var taken = source.TakeOut(1);
            source.MarkDirty(); be?.MarkDirty(true);
            if (taken != null) SetQuarryCargo(fox, job.Cart, taken, false);
        }
        else
        {
            if (cargo == null || !cargo.ResolveBlockOrItem(serverApi.World) || !job.Cart.Tools.Accepts(0, cargo)) return false;
            var source = new DummySlot(cargo);
            for (int i = 0; i < 4 && !source.Empty; i++)
                if (job.Cart.Tools[i].Empty && !job.Cart.Tools.IsReserved(i)) source.TryPutInto(serverApi.World, job.Cart.Tools[i], 1);
            if (!source.Empty)
            {
                // An owner may fill the slot during the trip. Keep the tool as cargo,
                // then let normal delivery return it to linked storage on the next task.
                fox.WatchedAttributes.SetBool("cartToolReturn", true);
                return false;
            }
            SetQuarryCargo(fox, job.Cart, null, false); job.Cart.MarkDirty(true);
        }
        job.Done = true; return false;
    }
}
