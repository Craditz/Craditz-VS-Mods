namespace FeralKinshipCompanions;
internal static class CompanionTroughLayout
{
    internal const double ToggleY = 380;
    internal const string Intro = "Optional high-wellbeing work while At Ease. Uses matching food from your enabled storage inside the existing work boundary. Manually feed a trough once to begin tending.";
    internal const string Note = "Refills troughs with the food you last added. To stop refilling a trough, break and replace it; leave it empty until you want tending to resume.";
    internal static bool ToggleAt(double x, double y, double width) => x >= 32 && x <= width - 32 && y >= ToggleY - 12 && y < ToggleY + 25;
}
