using System;
using System.Collections.Generic;

namespace FeralKinshipCompanions;

internal static class UndergroundCompanionRecoveryPolicy
{
    internal static bool ShouldRecover(
        int dimension,
        bool swimming,
        bool teleporting,
        bool mounted,
        bool intersectsTerrain)
    {
        return dimension == 0
            && !swimming
            && !teleporting
            && !mounted
            && intersectsTerrain;
    }

    internal static IEnumerable<int> EnumerateVerticalOffsets(int verticalRange)
    {
        yield return 0;
        for (int distance = 1; distance <= Math.Max(0, verticalRange); distance++)
        {
            yield return distance;
            yield return -distance;
        }
    }
}
