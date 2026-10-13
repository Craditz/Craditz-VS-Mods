#nullable enable
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

internal static class CompanionRecruitmentPlacement
{
    internal static bool Aquatic(EntityProperties type) => type.Habitat is EnumHabitat.Sea or EnumHabitat.Underwater;

    internal static Vec3d? Find(BlockPos origin, EntityProperties type, System.Func<BlockPos, Vec3d, bool> safe)
    {
        int depth = Aquatic(type) ? 4 : 2;
        for (int radius = 1; radius <= 4; radius++)
        for (int dy = 2; dy >= -depth; dy--)
        for (int dz = -radius; dz <= radius; dz++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius) continue;
            var feet = new BlockPos(origin.X + dx, origin.Y + dy, origin.Z + dz, origin.dimension);
            var candidate = new Vec3d(feet.X + 0.5, feet.InternalY + (Aquatic(type) ? 0.15 : 0), feet.Z + 0.5);
            if (safe(feet, candidate)) return candidate;
        }
        return null;
    }

    // Inspect the whole final-type collision volume, including neighboring
    // cells for large animals; never query unloaded space or accept lava.
    internal static bool HabitatSafe(EntityProperties type, BlockPos feet, Vec3d position,
        System.Func<BlockPos, bool> loaded, System.Func<BlockPos, bool> fullWater, System.Func<BlockPos, bool> liquid,
        System.Func<BlockPos, bool> solidFloor)
    {
        Cuboidf? box = type.SpawnCollisionBox;
        if (box == null || !float.IsFinite(box.X1) || !float.IsFinite(box.X2)
            || !float.IsFinite(box.Y1) || !float.IsFinite(box.Y2) || !float.IsFinite(box.Z1) || !float.IsFinite(box.Z2)
            || box.XSize <= 0 || box.YSize <= 0 || box.ZSize <= 0
            || box.XSize > 32 || box.YSize > 32 || box.ZSize > 32) return false;
        int x1 = (int)Math.Floor(position.X + box.X1), x2 = (int)Math.Ceiling(position.X + box.X2) - 1;
        int z1 = (int)Math.Floor(position.Z + box.Z1), z2 = (int)Math.Ceiling(position.Z + box.Z2) - 1;
        int y1 = (int)Math.Floor(position.Y - feet.dimension * BlockPos.DimensionBoundary + box.Y1);
        int y2 = (int)Math.Ceiling(position.Y - feet.dimension * BlockPos.DimensionBoundary + box.Y2) - 1;
        for (int x = x1; x <= x2; x++)
        for (int z = z1; z <= z2; z++)
        {
            if (!Aquatic(type))
            {
                var floor = new BlockPos(x, feet.Y - 1, z, feet.dimension);
                if (!loaded(floor) || !solidFloor(floor)) return false;
            }
            for (int y = y1; y <= y2; y++)
            {
                var cell = new BlockPos(x, y, z, feet.dimension);
                if (!loaded(cell) || (Aquatic(type) ? !fullWater(cell) : liquid(cell))) return false;
            }
        }
        return true;
    }
}
