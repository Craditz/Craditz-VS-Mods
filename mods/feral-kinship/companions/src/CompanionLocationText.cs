using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

internal static class CompanionLocationText
{
    // Convert only for display. Saved and network positions remain world coordinates.
    internal static string Format(ICoreAPI api, int worldX, int worldY, int worldZ)
    {
        Vec3i local = new BlockPos(worldX, worldY, worldZ, 0).ToLocalPosition(api);
        return FormattableString.Invariant($"{local.X}, {local.Y}, {local.Z}");
    }
}
