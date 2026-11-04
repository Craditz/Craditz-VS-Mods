#nullable enable
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    internal bool TryGetWorkCartTerritoryCenter(Entity fox, out Vec3d? center)
    {
        center = null;
        if (!TryGetAssignedWorkCart(fox, false, out _, out var pos, requireFoxInsideRadius: false) || pos == null) return false;
        center = pos.ToVec3d().Add(.5, 0, .5);
        return true;
    }
}
