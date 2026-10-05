using System;

namespace FeralKinshipCompanions;

internal static class CompanionFollowIdleBounds
{
    internal static bool Inside(double x, double z, double ownerX, double ownerZ, double radius)
    {
        double dx = x - ownerX, dz = z - ownerZ;
        return double.IsFinite(dx) && double.IsFinite(dz) && radius > 0
            && dx * dx + dz * dz <= radius * radius;
    }

    internal static double RestSpacing(double dx, double dz, double combinedHalfWidth, double combinedHalfDepth)
    {
        double x = Math.Abs(dx) < 0.00001 ? double.PositiveInfinity : combinedHalfWidth / Math.Abs(dx);
        double z = Math.Abs(dz) < 0.00001 ? double.PositiveInfinity : combinedHalfDepth / Math.Abs(dz);
        return Math.Min(x, z) + 0.12;
    }

    internal static bool AttentionCandidate(double x, double z, double startX, double startZ,
        double ownerX, double ownerZ, double radius)
    {
        double dx = x - startX, dz = z - startZ;
        return dx * dx + dz * dz <= 9.5 * 9.5
            && Inside(x, z, ownerX, ownerZ, radius - 0.65)
            && Inside(x, z, ownerX, ownerZ, 2.1)
            && !Inside(x, z, ownerX, ownerZ, 1.5);
    }

    internal static bool Candidate(double x, double z, double startX, double startZ,
        double ownerX, double ownerZ, double radius)
    {
        double dx = x - startX, dz = z - startZ;
        double distanceSquared = dx * dx + dz * dz;
        return distanceSquared >= 0.8 * 0.8 && distanceSquared <= 2.1 * 2.1
            && Inside(x, z, ownerX, ownerZ, radius - 0.65)
            && !Inside(x, z, ownerX, ownerZ, 1.0);
    }
}
