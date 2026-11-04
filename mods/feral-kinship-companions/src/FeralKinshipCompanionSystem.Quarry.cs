#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    internal const string QuarryCarryKey = "feralKinshipQuarryCarry";
    private const string QuarryCarryBufferKey = "feralKinshipQuarryCarryBuffer";
    private const string QuarryCarryCartKey = "feralKinshipQuarryCarryCart";

    internal void PlayQuarryWorkSound(Entity fox) => serverApi?.World.PlaySoundAt(
        new AssetLocation("game:sounds/block/rock-hit-pickaxe"), fox, null, 1f, 16, .45f);

    internal bool CanContinueQuarry(Entity fox, BlockEntityFeralKinshipWorkCart cart)
    {
        if (serverApi == null || !fox.Alive || fox.State != EnumEntityState.Active
            || !IsTamedFox(fox) || IsCompanionJuvenile(fox) || IsFoxAwayFromWorld(fox)
            || IsFoxIncapacitated(fox) || IsCompanionFoodRestricted(fox) || !AllowsWellbeingDuty(fox, demanding: false)
            || IsCartRescuePaused(fox, cart.Pos) || HasFoxStorageJob(fox) || GetCompanionActivityMode(fox) != CompanionActivityMode.AtEase
            || ShouldFoxSeekDenShelter(fox) || GetMood(fox) is "sleepy" or "resting" or "anxious" or "alarmed") return false;
        var owner = serverApi.World.PlayerByUid(GetDomesticationStatus(fox)?.GetString("owner", "") ?? "");
        return owner != null && serverApi.World.Claims.TestAccess(owner, cart.Pos, EnumBlockAccessFlags.Use) == EnumWorldAccessResponse.Granted
            && TryGetAssignedWorkCart(fox, false, out var record, out var pos, requireFoxInsideRadius: false)
            && record?.Kind == (cart.MiningMode ? "mining" : cart.Tools.QuarryMode ? "quarrying" : "logging") && pos?.Equals(cart.Pos) == true
            && serverApi.World.BlockAccessor.GetBlockEntity(cart.Pos) == cart;
    }

    internal bool TryCreateQuarryJob(Entity fox, out WorkCartQuarryJob? job)
    {
        job = null;
        if (TryCreateCartToolJob(fox, out job)) return true;
        if (TryCreateMiningJob(fox, out job)) return true;
        if (serverApi == null || !TryGetAssignedWorkCart(fox, false, out var record, out var pos)
            || record?.Kind != "quarrying" || pos == null) return false;
        var cart = EnsureWorkCartTools(pos);
        if (cart?.Tools.QuarryMode != true || !CanContinueQuarry(fox, cart)) return false;
        var q = cart.Quarry;
        if (fox.WatchedAttributes.GetItemstack(QuarryCarryKey) is ItemStack cargo && cargo.StackSize > 0)
            return TryCreateQuarryDelivery(fox, cart, cargo, out job);
        if (!q.Enabled || !q.Started || q.UnreadablePayload != null)
            return TryCreateQuarryCompletionCleanup(fox, cart, out job);
        if (!IsCartWindDown(fox) && TryCreateQuarrySupply(fox, cart, out job)) return true;
        // Evening work is cleanup-only. Finish carried items before starting another cut.
        if (IsCartWindDown(fox)) return TryCreateQuarryCollection(fox, cart, bufferOnly: true, out job)
            || TryCreateQuarryCollection(fox, cart, bufferOnly: false, out job);
        if (TryCreateQuarryCollection(fox, cart, bufferOnly: false, out job)) return true;
        if (!AllowsWellbeingDuty(fox, demanding: true)) return false;
        if (cart.QuarryJobs.Count > 0 && (cart.QuarryJobs.Any(j => j.Kind == QuarryJobKind.Build)
            || cart.Tools.FindAvailable(0) < 0)) return TryJoinCartJob(fox, cart, out job);
        long now = serverApi.World.ElapsedMilliseconds;
        if (now < cart.NextQuarryPlanAt) return false;
        cart.NextQuarryPlanAt = now + 250;
        AdvanceQuarrySurvey(cart, 32);

        ActivateQuarryFoundations(cart);
        var repair = q.Structure.FirstOrDefault(part => part.Placed && !part.DeferredFoundation
            && serverApi.World.BlockAccessor.GetChunkAtBlockPos(part.Point.Position(pos.dimension)) != null
            && serverApi.World.BlockAccessor.GetBlock(part.Point.Position(pos.dimension)).Code?.ToString() != part.Code);
        var construction = repair ?? q.Structure.FirstOrDefault(part => !part.Placed && !part.DeferredFoundation);
        if (construction != null)
        {
            // Access work takes priority over optional old pillars. A cut is
            // atomic, so releasing unfinished cleanup loses no blocks or tool wear.
            foreach (var optional in cart.QuarryJobs.Where(j => j.IsRemnant).ToArray()) FinishQuarryPrimary(cart, optional);
            if (cart.QuarryJobs.Count > 0) return TryJoinCartJob(fox, cart, out job);
            q.PreferredRock = construction.Rock;
            if (!QuarryBufferHas(cart, construction.Rock, 8))
            {
                q.Status = $"Waiting for 8 {construction.Rock} rocks in the buffer; staircase must be completed first.";
                return false;
            }
            var target = construction.Point.Position(pos.dimension);
            string problem = QuarryCutProblem(cart, target, structure: true);
            if (problem.Length > 0) { q.Status = "Staircase blocked: " + problem; return false; }
            if (!MiningPlacementClear(fox, target)) { q.Status = "Clear animals or players from the stair placement first."; return false; }
            if (TryQuarryApproach(fox, cart, target, construction: true, out var approach))
                return CreateQuarryPrimary(fox, cart, target, approach!, QuarryJobKind.Build,
                    construction.Rock, construction.Code, out job);
            q.Status = "No reachable standing position for the staircase work.";
            return false;
        }
        if (q.Stage is not ("flattening" or "excavating")) return TryCreateQuarryRemnantJob(fox, cart, out job);
        if (!QuarryPastStairsIntact(cart)) { q.Status = "Waiting for staircase repair."; return false; }
        int inspected = 0;
        foreach (QuarryPoint p in q.Pending.OrderBy(p => fox.Pos.SquareDistanceTo(p.Position(pos.dimension).ToVec3d())).ToArray())
        {
            if (cart.QuarryFailedTargets.GetValueOrDefault(p) > now
                || cart.QuarryJobs.Any(j => j.Target.X == p.X && j.Target.Y == p.Y && j.Target.Z == p.Z)) continue;
            if (++inspected > 32) break;
            if (q.IsProtected(p) || q.IsStructure(p) || QuarryEntrance(q, p)) { q.Pending.Remove(p); continue; }
            var target = p.Position(pos.dimension);
            if (serverApi.World.BlockAccessor.GetChunkAtBlockPos(target) == null
                || BlockFacing.ALLFACES.Any(face => serverApi.World.BlockAccessor.GetChunkAtBlockPos(target.AddCopy(face)) == null))
            { q.Status = "Waiting for loaded quarry terrain."; continue; }
            Block block = serverApi.World.BlockAccessor.GetBlock(target);
            if (block.Id == 0) { q.Pending.Remove(p); q.Excavated.Add(p); continue; }
            string problem = QuarryCutProblem(cart, target, structure: false);
            if (problem.Length > 0)
            {
                q.Protect(p, problem);
                ReportCartFinding(cart, target, problem);
                cart.MarkDirty(true); continue;
            }
            if (!TryQuarryApproach(fox, cart, target, construction: false, out var approach))
            { cart.QuarryFailedTargets[p] = now + 4000; continue; }
            if (CreateQuarryPrimary(fox, cart, target, approach!, QuarryJobKind.Mine,
                    QuarryRockType(block), "", out job)) return true;
        }
        if (TryCreateQuarryRemnantJob(fox, cart, out job)) return true;
        q.Status = q.Pending.Count > 0 ? "Waiting for a reachable work face and usable pickaxe."
            : q.Stage == "flattening" ? "Flattening the south footprint." : $"Preparing layer {q.Layer}/{q.Depth}.";
        return TryJoinCartJob(fox, cart, out job);
    }

    private bool TryCreateQuarryCompletionCleanup(Entity fox, BlockEntityFeralKinshipWorkCart cart,
        out WorkCartQuarryJob? job)
    {
        job = null;
        return cart.Quarry.Started && cart.Quarry.Stage == "complete" && cart.Quarry.UnreadablePayload == null
            && TryCreateQuarryCollection(fox, cart, bufferOnly: false, out job);
    }

    private bool TryQuarryApproach(Entity fox, BlockEntityFeralKinshipWorkCart cart,
        BlockPos target, bool construction, out Vec3d? approach)
    {
        var reference = target.Copy();
        if (construction) reference.Y = Math.Max(target.Y, cart.Quarry.SurfaceY - cart.Quarry.Layer + 1);
        int workY = reference.Y;
        Cuboidf clearance = (fox.CollisionBox ?? fox.Properties.SpawnCollisionBox).Clone();
        clearance.Y2 = Math.Max(clearance.Y2, clearance.Y1 + 2);
        bool Allowed(Vec3d point)
        {
            double dx = point.X - (target.X + 0.5), dz = point.Z - (target.Z + 0.5);
            double dy = point.Y - (target.InternalY + 0.5);
            if (dx * dx + dy * dy + dz * dz > (construction ? 36 : 12.25)) return false;
            if (!construction && (int)Math.Floor(point.X) == target.X && (int)Math.Floor(point.Z) == target.Z
                && point.Y >= target.InternalY + .99) return false;
            var feet = new BlockPos((int)Math.Floor(point.X), (int)Math.Floor(point.Y)
                - target.dimension * BlockPos.DimensionBoundary, (int)Math.Floor(point.Z), target.dimension);
            var blocks = serverApi!.World.BlockAccessor;
            for (int h = -1; h < Math.Ceiling(clearance.Y2); h++)
                if (blocks.GetChunkAtBlockPos(feet.AddCopy(0, h, 0)) == null) return false;
            return !serverApi.World.CollisionTester.IsColliding(blocks, clearance, point, false)
                && (cart.Quarry.WasExcavated(new(feet.X, feet.Y, feet.Z)) || QuarryOpenToSky(feet, cart.Quarry));
        }
        // Search actual supported stands up to three blocks away. Recognize
        // stair surfaces too; a remnant near the rim can be reached from outside.
        int workerY = (int)Math.Floor(fox.Pos.Y) - target.dimension * BlockPos.DimensionBoundary;
        var blocks = serverApi!.World.BlockAccessor;
        var offsets = (from x in Enumerable.Range(-3, 7) from z in Enumerable.Range(-3, 7)
            where x * x + z * z <= 9 select (X: x, Z: z))
            .OrderBy(p => p.X == 0 && p.Z == 0 ? 1 : 0)
            .ThenBy(p => fox.Pos.SquareDistanceTo(new Vec3d(target.X + p.X + .5, target.InternalY, target.Z + p.Z + .5))).ToArray();
        foreach (int y in new[] { Math.Clamp(workerY, workY - 1, workY + (construction ? 3 : 2)), workY + 2, workY }.Distinct())
        foreach (var offset in offsets) foreach (int dy in new[] { 0, 1, -1 })
        {
            var feet = new BlockPos(target.X + offset.X, y + dy, target.Z + offset.Z, target.dimension);
            if (blocks.GetChunkAtBlockPos(feet.DownCopy()) == null) continue;
            Block floor = blocks.GetBlock(feet.DownCopy());
            if (!floor.SideSolid[BlockFacing.UP.Index] && !MiningStair(floor)) continue;
            var point = new Vec3d(feet.X + .5, feet.InternalY, feet.Z + .5);
            if (!AlignMiningStandingPoint(fox, point, feet) || !Allowed(point)) continue;
            approach = point; return true;
        }
        approach = null; return false;
    }

    private bool CreateQuarryPrimary(Entity fox, BlockEntityFeralKinshipWorkCart cart,
        BlockPos target, Vec3d approach, QuarryJobKind kind, string rock, string buildCode, out WorkCartQuarryJob? job)
    {
        job = null;
        if (cart.QuarryJobs.Count >= 4 || cart.QuarryJobs.Any(j => j.Target.Equals(target)
            || kind == QuarryJobKind.Mine && (int)Math.Floor(j.Approach.X) == target.X
                && (int)Math.Floor(j.Approach.Z) == target.Z && (int)Math.Floor(j.Approach.Y) - 1 == target.InternalY)) return false;
        job = new() { Kind = kind, Cart = cart, Target = target, Approach = approach,
            OriginalBlockId = serverApi!.World.BlockAccessor.GetBlock(target).Id, Rock = rock, BuildCode = buildCode };
        if (job.OriginalBlockId != 0)
        {
            int tier = serverApi.World.BlockAccessor.GetBlock(target).RequiredMiningTier;
            job.ToolIndex = cart.Tools.Reserve(job, tier);
            if (job.ToolIndex < 0) { job = null; cart.Quarry.Status = "Supply a usable pickaxe of the required tier."; return false; }
        }
        job.Workers[fox.EntityId] = serverApi.World.ElapsedMilliseconds;
        cart.QuarryJobs.Add(job);
        cart.Quarry.Status = kind == QuarryJobKind.Build ? $"Building {rock} staircase, layer {cart.Quarry.Layer}."
            : cart.Quarry.Stage == "flattening" ? "Flattening the south footprint." : $"Mining layer {cart.Quarry.Layer}/{cart.Quarry.Depth}.";
        cart.MarkDirty(true); return true;
    }

    internal bool AdvanceQuarryWork(Entity fox, WorkCartQuarryJob job, float dt)
    {
        if (serverApi == null || job.Done || !CanContinueQuarry(fox, job.Cart)) return false;
        var cart = job.Cart;
        var q = cart.Quarry;
        job.Workers[fox.EntityId] = serverApi.World.ElapsedMilliseconds;
        if (job.Kind is not (QuarryJobKind.Mine or QuarryJobKind.Build))
            return ExecuteQuarryResourceJob(fox, job);
        if (cart.MiningMode) return AdvanceMiningWork(fox, job, dt);
        if (!q.Enabled || !cart.QuarryJobs.Contains(job)
            || fox.Pos.SquareDistanceTo(job.Target.ToVec3d().Add(.5, .5, .5)) > (job.Kind == QuarryJobKind.Build ? 42.25 : 16)) return false;
        if (job.IsRemnant && !MiningLineClear(fox.Pos.XYZ.AddCopy(0, .7, 0), job.Target))
        {
            cart.QuarryFailedTargets[new(job.Target.X, job.Target.Y, job.Target.Z)] = serverApi.World.ElapsedMilliseconds + 12000;
            FinishQuarryPrimary(cart, job); return false;
        }
        Block block = serverApi.World.BlockAccessor.GetBlock(job.Target);
        if (block.Id != job.OriginalBlockId || !QuarryCutSafe(cart, job.Target, job.Kind == QuarryJobKind.Build)
            || (job.Kind == QuarryJobKind.Mine && !QuarryPastStairsIntact(cart)))
        { FinishQuarryPrimary(cart, job); return false; }
        var owner = serverApi.World.PlayerByUid(packRepository!.GetWorkCart(cart.Pos)!.OwnerUid);
        if (owner == null) return false;
        ItemSlot? pick = job.ToolIndex >= 0 && cart.Tools.Owns(job.ToolIndex, job) ? cart.Tools[job.ToolIndex] : null;
        if (block.Id != 0 && (pick == null || !cart.Tools.IsUsableTool(pick, block.RequiredMiningTier)))
        { FinishQuarryPrimary(cart, job); return false; }
        var selection = new BlockSelection { Position = job.Target, Block = block, Face = BlockFacing.UP };
        double speed = block.Id == 0 ? 1 : pick!.Itemstack!.Collectible.GetMiningSpeed(pick.Itemstack, selection, block, owner);
        job.Work += Math.Clamp(dt, 0, .25f) * Math.Max(.05, speed);
        if (job.Work < (block.Id == 0 ? .8 : Math.Max(.15, block.GetResistance(serverApi.World.BlockAccessor, job.Target)))) return true;
        if (CompleteQuarryBlock(fox, job)) RecordWellbeingDuty(fox, .1, label: "Quarrying");
        return false;
    }

    private bool CompleteQuarryBlock(Entity fox, WorkCartQuarryJob job)
    {
        if (serverApi == null || !job.Cart.QuarryJobs.Contains(job) || job.Done) return false;
        var cart = job.Cart;
        var q = cart.Quarry;
        Block block = serverApi.World.BlockAccessor.GetBlock(job.Target);
        if (!q.Enabled || block.Id != job.OriginalBlockId
            || !QuarryCutSafe(cart, job.Target, job.Kind == QuarryJobKind.Build)
            || (job.Kind == QuarryJobKind.Mine && !QuarryPastStairsIntact(cart)))
        { FinishQuarryPrimary(cart, job); return false; }
        ItemSlot? pick = job.ToolIndex >= 0 && cart.Tools.Owns(job.ToolIndex, job) ? cart.Tools[job.ToolIndex] : null;
        if (block.Id != 0 && (pick == null || !cart.Tools.IsUsableTool(pick, block.RequiredMiningTier)))
        { FinishQuarryPrimary(cart, job); return false; }
        var selection = new BlockSelection { Position = job.Target, Block = block, Face = BlockFacing.UP };
        if (job.Kind == QuarryJobKind.Build && !QuarryBufferHas(cart, job.Rock, 8))
        { q.Status = "Waiting for staircase rocks."; FinishQuarryPrimary(cart, job); return false; }
        bool naturalFull = (block.GetBehavior(typeof(BlockBehaviorBreakIfFloating), true) as BlockBehaviorBreakIfFloating)
            ?.IsSurroundedByNonSolid(serverApi.World, job.Target) == true;
        bool countedStone = job.Kind == QuarryJobKind.Mine && job.Rock.Length > 0 && !naturalFull;
        if (block.Id != 0)
        {
            IWorldAccessor? previousWorld = BlockBehaviorQuarryRecovery.ActiveWorld;
            BlockPos? previousPosition = BlockBehaviorQuarryRecovery.ActivePosition;
            try
            {
                if (countedStone && q.WillRecover(job.Rock))
                { BlockBehaviorQuarryRecovery.ActiveWorld = serverApi.World; BlockBehaviorQuarryRecovery.ActivePosition = job.Target; }
                BreakCartWorkBlock(fox, pick!, selection);
            }
            finally
            { BlockBehaviorQuarryRecovery.ActiveWorld = previousWorld; BlockBehaviorQuarryRecovery.ActivePosition = previousPosition; }
            if (serverApi.World.BlockAccessor.GetBlock(job.Target).Id == block.Id)
            { FinishQuarryPrimary(cart, job); return false; }
            SyncCartWorkBlock(job.Target);
            q.MinedBlocks++;
            if (countedStone) q.CountStone(job.Rock);
            q.Excavated.Add(new(job.Target.X, job.Target.Y, job.Target.Z));
        }
        if (job.Kind == QuarryJobKind.Build)
        {
            Block? expected = serverApi.World.GetBlock(new AssetLocation(job.BuildCode));
            if (expected == null || expected.Id == 0 || !QuarryBufferHas(cart, job.Rock, 8))
            { FinishQuarryPrimary(cart, job); return false; }
            if (!PrepareCartPlacement(fox, cart, job.Target))
            { FinishQuarryPrimary(cart, job); cart.MarkDirty(true); return block.Id != 0; }
            cart.Tools[4].TakeOut(8); cart.Tools[4].MarkDirty();
            serverApi.World.BlockAccessor.SetBlock(expected.Id, job.Target);
            serverApi.World.BlockAccessor.TriggerNeighbourBlockUpdate(job.Target);
            SyncCartWorkBlock(job.Target);
            q.RocksSpent += 8;
            var part = q.Structure.First(p => p.Point == new QuarryPoint(job.Target.X, job.Target.Y, job.Target.Z));
            part.Placed = true;
        }
        else
        {
            var point = new QuarryPoint(job.Target.X, job.Target.Y, job.Target.Z);
            q.Pending.Remove(point); q.CleanupPending.Remove(point);
        }
        FinishQuarryPrimary(cart, job); cart.MarkDirty(true);
        return true;
    }

    private static void FinishQuarryPrimary(BlockEntityFeralKinshipWorkCart cart, WorkCartQuarryJob job)
    {
        job.Done = true; cart.Tools.Release(job); cart.QuarryJobs.Remove(job);
    }

    internal void ReleaseQuarryJob(Entity fox, WorkCartQuarryJob job, bool failed)
    {
        job.Workers.Remove(fox.EntityId);
        if (job.Kind is QuarryJobKind.Mine or QuarryJobKind.Build)
        {
            if (job.Workers.Count == 0 && job.Cart.QuarryJobs.Contains(job))
            {
                if (failed && serverApi != null) job.Cart.QuarryFailedTargets[new(job.Target.X, job.Target.Y, job.Target.Z)]
                    = serverApi.World.ElapsedMilliseconds + 12000;
                FinishQuarryPrimary(job.Cart, job);
            }
        }
        else if (job.Cart.QuarrySupplyWorker == fox.EntityId) job.Cart.QuarrySupplyWorker = 0;
        if (job.ItemEntityId != 0 && droppedItemReservations.GetValueOrDefault(job.ItemEntityId) == fox.EntityId)
            droppedItemReservations.Remove(job.ItemEntityId);
        if (failed && job.Kind == QuarryJobKind.Collect && job.ItemEntityId != 0 && serverApi != null)
            job.Cart.QuarryFailedDebris[job.ItemEntityId] = serverApi.World.ElapsedMilliseconds + 30000;
    }
}
