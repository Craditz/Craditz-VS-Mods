#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

internal static class FeralKinshipNaturalCleanup
{
    internal const int IneligiblePriority = int.MaxValue;

    internal static bool IsEligible(Block? block)
    {
        return GetPriority(block) != IneligiblePriority;
    }

    internal static int GetPriority(Block? block)
    {
        AssetLocation? code = block?.Code;
        string? path = code?.Path;
        if (!string.Equals(code?.Domain, "game", StringComparison.OrdinalIgnoreCase)
            || path == null
            || string.IsNullOrWhiteSpace(path)) return IneligiblePriority;

        // Lower values are handled first. Grass is a separate, lower-priority
        // task so it naturally waits until this cleanup list is exhausted.
        // The block code does not retain whether a player placed the block or
        // world generation placed it. Treat both the same, and match the
        // uncovered water/ice forms as well as the ordinary free form.
        if (IsNormalUncoveredCattail(path)) return 0;
        if (IsUncoveredVariant(path, "looseflints-")) return 1;
        if (IsUncoveredVariant(path, "loosestick-")) return 2;
        if (IsUncoveredVariant(path, "looseboulders-")) return 3;
        if (IsUncoveredVariant(path, "loosestones-")) return 4;
        return IneligiblePriority;
    }

    internal static bool IsCattail(Block block)
    {
        return GetPriority(block) == 0;
    }

    internal static Vec3d GetTarget(BlockPos pos)
    {
        return new Vec3d(
            pos.X + 0.5,
            pos.Y + pos.dimension * BlockPos.DimensionBoundary,
            pos.Z + 0.5
        );
    }

    private static bool IsNormalUncoveredCattail(string path)
    {
        return path.StartsWith("tallplant-coopersreed-", StringComparison.OrdinalIgnoreCase)
            && (path.EndsWith("-normal-free", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("-normal-water", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("-normal-ice", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUncoveredVariant(string path, string prefix)
    {
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && (path.EndsWith("-free", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("-water", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("-ice", StringComparison.OrdinalIgnoreCase));
    }
}
