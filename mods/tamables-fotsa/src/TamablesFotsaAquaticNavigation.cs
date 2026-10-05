#nullable enable

using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace TamablesFotsa;

internal static class TamablesFotsaAquaticNavigation
{
    private const int MaximumVisitedWaterCells = 4096;
    private const int MaximumHorizontalSearchRadius = 32;
    private const int MaximumVerticalSearchRadius = 10;
    private const int TeleportWaterSearchRadius = 8;

    private static readonly (int X, int Y, int Z)[] UnderwaterNeighbors =
    {
        (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1),
        (0, 1, 0), (0, -1, 0)
    };

    private static readonly (int X, int Y, int Z)[] SurfaceNeighbors =
    {
        (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1)
    };

    public static bool IsInSwimmableWater(Entity entity)
    {
        if (entity.Swimming) return true;

        Block fluid = entity.World.BlockAccessor.GetBlock(entity.Pos.AsBlockPos, BlockLayersAccess.Fluid);
        return fluid.IsLiquid() && fluid.LiquidLevel >= 7;
    }

    public static bool IsWaterLineClear(EntityAgent entity, Vec3d target)
    {
        Vec3d start = entity.Pos.XYZ;
        double dx = target.X - start.X;
        double dy = target.Y - start.Y;
        double dz = target.Z - start.Z;
        int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz))) * 4d));

        for (int step = 0; step <= steps; step++)
        {
            double amount = step / (double)steps;
            Vec3d sample = new(
                start.X + dx * amount,
                start.Y + dy * amount,
                start.Z + dz * amount
            );
            if (!CanOccupyWater(entity, sample)) return false;
        }

        return true;
    }

    public static bool TryBuildConnectedWaterRoute(
        EntityAgent entity,
        Entity owner,
        float stoppingDistance,
        out List<Vec3d> route)
    {
        route = new List<Vec3d>();
        BlockPos start = entity.Pos.AsBlockPos;
        if (!CanOccupyWater(entity, start)) return false;

        PriorityQueue<BlockPos, double> frontier = new();
        Dictionary<BlockPos, BlockPos?> parents = new();
        double startScore = DistanceScore(start, owner);
        double bestScore = startScore;
        BlockPos best = start;

        frontier.Enqueue(start, startScore);
        parents[start] = null;

        (int X, int Y, int Z)[] neighbors = entity.Properties.Habitat == EnumHabitat.Sea
            ? SurfaceNeighbors
            : UnderwaterNeighbors;

        while (frontier.Count > 0 && parents.Count < MaximumVisitedWaterCells)
        {
            BlockPos current = frontier.Dequeue();
            double score = DistanceScore(current, owner);
            if (score < bestScore)
            {
                best = current;
                bestScore = score;
                if (bestScore <= stoppingDistance * stoppingDistance) break;
            }

            foreach ((int offsetX, int offsetY, int offsetZ) in neighbors)
            {
                BlockPos next = new(
                    current.X + offsetX,
                    current.Y + offsetY,
                    current.Z + offsetZ,
                    current.dimension
                );
                if (Math.Abs(next.X - start.X) > MaximumHorizontalSearchRadius
                    || Math.Abs(next.Z - start.Z) > MaximumHorizontalSearchRadius
                    || Math.Abs(next.Y - start.Y) > MaximumVerticalSearchRadius
                    || parents.ContainsKey(next)
                    || !CanOccupyWater(entity, next))
                {
                    continue;
                }

                parents[next] = current;
                frontier.Enqueue(next, DistanceScore(next, owner));
            }
        }

        // Do not start a route that cannot make meaningful progress toward
        // the owner. This leaves the animal safely at its current shoreline.
        if (best.Equals(start) || bestScore >= startScore - 0.25d) return false;

        BlockPos? cursor = best;
        while (cursor != null && !cursor.Equals(start))
        {
            route.Add(ToWorldPosition(cursor));
            cursor = parents[cursor];
        }

        route.Reverse();
        return route.Count > 0;
    }

    public static bool TryTeleportToWaterNearOwner(EntityAgent entity, Entity owner)
    {
        BlockPos origin = owner.Pos.AsBlockPos;
        for (int radius = 0; radius <= TeleportWaterSearchRadius; radius++)
        {
            for (int dy = 2; dy >= -4; dy--)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (radius > 0 && Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius) continue;

                        BlockPos waterPos = new(origin.X + dx, origin.Y + dy, origin.Z + dz, origin.dimension);
                        if (!CanOccupyWater(entity, waterPos)) continue;

                        Vec3d candidate = ToWorldPosition(waterPos);
                        Entity[] nearby = entity.World.GetEntitiesAround(
                            candidate,
                            Math.Max(0.9f, entity.SelectionBox.XSize),
                            Math.Max(1.5f, entity.SelectionBox.YSize),
                            other => other != entity && other.Alive
                        );
                        if (nearby.Length > 0) continue;

                        entity.TeleportTo(candidate);
                        entity.PositionBeforeFalling.Set(candidate.X, candidate.Y, candidate.Z);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool CanOccupyWater(EntityAgent entity, BlockPos position)
    {
        Block fluid = entity.World.BlockAccessor.GetBlock(position, BlockLayersAccess.Fluid);
        return fluid.IsLiquid()
            && fluid.LiquidLevel >= 7
            && !entity.World.CollisionTester.IsColliding(
                entity.World.BlockAccessor,
                entity.Properties.SpawnCollisionBox,
                ToWorldPosition(position),
                false
            );
    }

    private static bool CanOccupyWater(EntityAgent entity, Vec3d position)
    {
        BlockPos blockPos = new((int)position.X, (int)position.Y, (int)position.Z);
        Block fluid = entity.World.BlockAccessor.GetBlock(blockPos, BlockLayersAccess.Fluid);
        return fluid.IsLiquid()
            && fluid.LiquidLevel >= 7
            && !entity.World.CollisionTester.IsColliding(
                entity.World.BlockAccessor,
                entity.Properties.SpawnCollisionBox,
                position,
                false
            );
    }

    private static double DistanceScore(BlockPos position, Entity owner)
    {
        double dx = position.X + 0.5d - owner.Pos.X;
        double dy = position.InternalY + 0.15d - owner.Pos.InternalY;
        double dz = position.Z + 0.5d - owner.Pos.Z;
        return dx * dx + dz * dz + dy * dy * 0.25d;
    }

    private static Vec3d ToWorldPosition(BlockPos position)
    {
        return new Vec3d(position.X + 0.5d, position.InternalY + 0.15d, position.Z + 0.5d);
    }
}
