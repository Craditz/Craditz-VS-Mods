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
    // Exact centers/ranges only: nearby cells can have different heat/water/tree results.
    private readonly Dictionary<(int X, int Y, int Z, int Dimension, float Range), EnvironmentSnapshot> sharedEnvironmentSnapshots = new();

    private EnvironmentSnapshot GetEnvironmentSnapshot(Entity entity)
    {
        int centerX = (int)Math.Floor(entity.Pos.X);
        int centerY = (int)Math.Floor(entity.Pos.Y);
        int centerZ = (int)Math.Floor(entity.Pos.Z);
        int dimension = entity.Pos.Dimension;
        long nowMs = entity.World.ElapsedMilliseconds;
        float searchRange = GetFoxObjectProximityDistance(entity);

        if (environmentSnapshots.TryGetValue(entity.EntityId, out EnvironmentSnapshot? cached)
            && cached.Dimension == dimension
            && Math.Abs(cached.SearchRange - searchRange) <= 0.01f
            && nowMs - cached.CapturedAtMs <= EnvironmentSnapshotLifetimeMs - (entity.EntityId & 1023)
            && Math.Abs(centerX - cached.CenterX) <= 2
            && Math.Abs(centerY - cached.CenterY) <= 2
            && Math.Abs(centerZ - cached.CenterZ) <= 2)
        {
            return cached;
        }

        var cell = (centerX, centerY, centerZ, dimension, searchRange);
        if (sharedEnvironmentSnapshots.TryGetValue(cell, out EnvironmentSnapshot? shared)
            && nowMs - shared.CapturedAtMs <= EnvironmentSnapshotLifetimeMs - (entity.EntityId & 1023))
        {
            environmentSnapshots[entity.EntityId] = shared;
            return shared;
        }
        if (sharedEnvironmentSnapshots.Count >= 256)
        {
            foreach (var key in sharedEnvironmentSnapshots.Where(pair => nowMs - pair.Value.CapturedAtMs > EnvironmentSnapshotLifetimeMs)
                         .Select(pair => pair.Key).ToArray()) sharedEnvironmentSnapshots.Remove(key);
            if (sharedEnvironmentSnapshots.Count >= 256) sharedEnvironmentSnapshots.Clear();
        }

        EnvironmentSnapshot snapshot = new()
        {
            CapturedAtMs = nowMs,
            CenterX = centerX,
            CenterY = centerY,
            CenterZ = centerZ,
            Dimension = dimension,
            SearchRange = searchRange
        };

        if (serverApi == null)
        {
            return snapshot;
        }

        IBlockAccessor accessor = serverApi.World.BlockAccessor;
        int range = (int)Math.Ceiling(searchRange);
        float rangeSquared = searchRange * searchRange;
        BlockPos position = new(dimension);
        for (int x = centerX - range; x <= centerX + range; x++)
        {
            int dx = x - centerX;
            for (int z = centerZ - range; z <= centerZ + range; z++)
            {
                int dz = z - centerZ;
                if (dx * dx + dz * dz > rangeSquared)
                {
                    continue;
                }

                for (int y = Math.Max(0, centerY - 3); y <= centerY + 3; y++)
                {
                    position.Set(x, y, z);
                    Block block = accessor.GetBlock(position);

                    if (!snapshot.NearWater
                        && Math.Abs(y - centerY) <= 1
                        && accessor.GetBlock(position, BlockLayersAccess.Fluid).MatterState == EnumMatterState.Liquid)
                    {
                        snapshot.NearWater = true;
                    }
                    if (!snapshot.NearLight
                        && accessor.GetLightLevel(position, EnumLightLevelType.OnlyBlockLight) >= LightSourceLevel)
                    {
                        snapshot.NearLight = true;
                    }
                    if (!snapshot.NearHeat && IsActiveHeatSource(block, accessor, position))
                    {
                        snapshot.NearHeat = true;
                    }
                    if (!snapshot.NearLargeTree && IsLargeTreeAt(block, accessor, position))
                    {
                        snapshot.NearLargeTree = true;
                    }
                    if (!snapshot.NearCropField && IsCropFieldBlock(block))
                    {
                        snapshot.NearCropField = true;
                    }
                    if (!snapshot.NearMechanicalDevice && IsMechanicalBlock(block))
                    {
                        snapshot.NearMechanicalDevice = true;
                    }
                    if (snapshot.NearWater && snapshot.NearLight && snapshot.NearHeat && snapshot.NearLargeTree
                        && snapshot.NearCropField && snapshot.NearMechanicalDevice) goto ScanComplete;
                }
            }
        }

        ScanComplete:
        sharedEnvironmentSnapshots[cell] = snapshot;
        environmentSnapshots[entity.EntityId] = snapshot;
        return snapshot;
    }

}
