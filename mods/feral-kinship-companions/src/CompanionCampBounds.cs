namespace FeralKinshipCompanions;

internal static class CompanionCampBounds
{
    internal static bool Contains(int x, int z, int dimension,
        int centerX, int centerZ, int centerDimension, double radius)
    {
        if (dimension != centerDimension || radius < 0) return false;
        double dx = (double)x - centerX;
        double dz = (double)z - centerZ;
        return dx * dx + dz * dz <= radius * radius;
    }
}
