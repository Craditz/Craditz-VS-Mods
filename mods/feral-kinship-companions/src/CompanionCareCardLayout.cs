#nullable enable
using System;
namespace FeralKinshipCompanions;
internal static class CompanionCareCardLayout
{
    internal static double LineHeight(double size) => size >= 28 ? 34 : size >= 17 ? 24 : 19;
    internal static int GroupAt(double x, double y)
    {
        if (x < 30 || x > 730 || y < 150 || y >= 438) return -1;
        int column = x >= 388 ? 1 : 0, row = (int)((y - 150) / 96);
        double localX = x - (30 + column * 358), localY = y - (150 + row * 96);
        return localX >= 0 && localX <= 342 && localY >= 0 && localY <= 84 ? row * 2 + column : -1;
    }
}
