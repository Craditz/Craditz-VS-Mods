#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private static Cuboidd CartPlacementVolume(BlockPos target) => new(
        target.X, target.InternalY, target.Z, target.X + 1, target.InternalY + 1, target.Z + 1);

    private static Cuboidf CartDropBox(EntityItem item) => item.CollisionBox
        ?? new Cuboidf(-.125f, 0, -.125f, .125f, .25f, .125f);

    private bool PrepareCartPlacement(Entity worker, BlockEntityFeralKinshipWorkCart cart, BlockPos target)
    {
        if (!MiningPlacementClear(worker, target))
        {
            SetCartMaterialStatus(cart, "Waiting for animals or players to clear the construction cell.");
            return false;
        }
        var volume = CartPlacementVolume(target);
        var drops = serverApi!.World.GetEntitiesAround(target.ToVec3d().Add(.5, .5, .5), 2, 3,
            e => e is EntityItem item && item.Alive && !item.Slot.Empty
                && CartDropBox(item).ToDouble().Translate(item.Pos.XYZ).Intersects(volume)).OfType<EntityItem>();
        var moves = new List<(EntityItem Item, Vec3d Landing)>();
        // Resolve every destination before moving any stack. Keep the same
        // entities and stack contents: this is clearing construction, not pickup.
        foreach (var item in drops)
        {
            if (!TryCartDropLanding(worker, item, target, out var landing))
            {
                SetCartMaterialStatus(cart, "Waiting for nearby safe ground to move a construction drop aside.");
                return false;
            }
            moves.Add((item, landing!));
        }
        foreach (var move in moves)
        {
            move.Item.Pos.Motion.Set(0, 0, 0);
            // The landing's terrain is already loaded. Apply the position and
            // native teleport flag synchronously: TeleportTo queues a chunk
            // callback, which could otherwise run after the block was placed.
            move.Item.IsTeleport = true;
            move.Item.Pos.SetPos(move.Landing.X,
                move.Landing.Y - target.dimension * BlockPos.DimensionBoundary, move.Landing.Z);
            move.Item.PositionBeforeFalling.Set(move.Item.Pos.X, move.Item.Pos.Y, move.Item.Pos.Z);
            move.Item.OnGround = false;
            // A previous pickup route targeted the old hole; let it re-plan.
            droppedItemReservations.Remove(move.Item.EntityId);
            cart.QuarryFailedDebris.Remove(move.Item.EntityId);
        }
        if (moves.Count > 0) cart.NextQuarryDebrisScan = 0;
        return true;
    }

    private bool TryCartDropLanding(Entity worker, EntityItem item, BlockPos target, out Vec3d? landing)
    {
        landing = null;
        var blocks = serverApi!.World.BlockAccessor;
        var volume = CartPlacementVolume(target);
        var itemBox = CartDropBox(item);
        var workerFeet = worker.Pos.AsBlockPos;
        int supportY = (int)Math.Floor(worker.Pos.Y - .08);
        // Prefer the worker's current safe stand, then its immediately adjacent
        // floor/stair surfaces. Never send drops down a shaft or through a wall.
        var columns = new List<(double X, double Z)> { (worker.Pos.X, worker.Pos.Z) };
        for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
            columns.Add((workerFeet.X + dx + .5, workerFeet.Z + dz + .5));
        foreach (var column in columns)
        foreach (int dy in new[] { 0, 1, -1 })
        {
            var floorPos = new BlockPos((int)Math.Floor(column.X), supportY + dy,
                (int)Math.Floor(column.Z), target.dimension);
            if (blocks.GetChunkAtBlockPos(floorPos) == null
                || blocks.GetChunkAtBlockPos(floorPos.UpCopy(2)) == null) continue;
            Block floor = blocks.GetBlock(floorPos);
            if (!floor.SideSolid[BlockFacing.UP.Index] && !MiningStair(floor)) continue;
            var boxes = floor.GetCollisionBoxes(blocks, floorPos);
            if (boxes == null) continue;
            foreach (var box in boxes.OrderByDescending(b => b.MaxY))
            {
                double localX = column.X - floorPos.X, localZ = column.Z - floorPos.Z;
                if (localX < box.MinX || localX > box.MaxX || localZ < box.MinZ || localZ > box.MaxZ) continue;
                var stand = new Vec3d(column.X, floorPos.InternalY + box.MaxY + .02, column.Z);
                var point = stand.AddCopy(0, -itemBox.MinY, 0);
                if (worker.Pos.SquareDistanceTo(stand) > 2.25
                    || point.SquareDistanceTo(target.ToVec3d().Add(.5, .5, .5)) > 49
                    || itemBox.ToDouble().Translate(point).Intersects(volume)
                    || blocks.GetBlock(stand.AsBlockPos, BlockLayersAccess.Fluid).IsLiquid()
                    || blocks.GetBlock(stand.AsBlockPos.UpCopy(), BlockLayersAccess.Fluid).IsLiquid()
                    || serverApi.World.CollisionTester.IsColliding(blocks, itemBox, point, false)
                    || serverApi.World.CollisionTester.IsColliding(blocks, worker.CollisionBox, stand, false)
                    || !MiningLineClear(worker.Pos.XYZ.AddCopy(0, .5, 0), stand.AsBlockPos)) continue;
                landing = point; return true;
            }
        }
        return false;
    }
}
