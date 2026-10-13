#nullable enable
namespace FeralKinshipCompanions;
internal static class CompanionHarvestLayout
{
    internal const double GatheringY = 330, CropsY = 374, ReseedingY = 477, FertilizerY = 538, BerriesY = 609, MushroomsY = 609;
    internal static string OptionAt(double x, double y, double width)
    {
        if (x < 18 || x > width - 18) return string.Empty;
        if (y >= 320 && y < 366) return CompanionDuty.GatherFinishedProducts;
        if (y >= 366 && y < 408) return CompanionDuty.FinishedCrops;
        if (x >= 60 && x <= width - 32 && y >= 470 && y < 508) return CompanionDuty.Reseeding;
        if (x >= 60 && x <= width - 32 && y >= 531 && y < 568) return CompanionDuty.Fertilizer;
        if (y >= 600 && y < 634) return x < width / 2 ? CompanionDuty.FinishedBerries : CompanionDuty.FinishedMushrooms;
        return string.Empty;
    }
}
