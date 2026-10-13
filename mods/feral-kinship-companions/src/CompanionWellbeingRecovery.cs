#nullable enable
using System;
namespace FeralKinshipCompanions;
internal enum WellbeingWorkBand { Normal, Slower, LightOnly, Recovery }
internal static class CompanionWellbeingRecovery
{
    // Five-point exit margins keep a companion from repeatedly changing its mind at a boundary.
    internal static WellbeingWorkBand Update(CompanionWellbeingState? w)
    {
        if (w == null || w.Version != CompanionWellbeingRules.Version || !w.HasObservation || !w.HasEstablishedHome)
            return WellbeingWorkBand.Normal;
        float value = float.IsFinite(w.Value) ? w.Value : 50;
        int band = value < 0 ? 3 : value < 25 ? 2 : value < 50 ? 1 : 0;
        int previous = Math.Clamp(w.WorkBand, 0, 3);
        if (previous == 3 && value < 5) band = 3;
        else if (previous >= 2 && value < 30) band = Math.Max(band, 2);
        else if (previous >= 1 && value < 55) band = Math.Max(band, 1);
        w.WorkBand = band;
        return (WellbeingWorkBand)band;
    }
    internal static float ScanFactor(WellbeingWorkBand band) => band == WellbeingWorkBand.Normal ? 1 : band == WellbeingWorkBand.Slower ? 2 : 3;
    internal static bool AllowsDuty(WellbeingWorkBand band, bool demanding = false, bool food = false, bool essential = false)
        => essential || food || band < WellbeingWorkBand.LightOnly || band == WellbeingWorkBand.LightOnly && !demanding;
    internal static bool FoodTrip(string type, bool eatsMeat, bool eatsPlants)
        => type == "hunt" && eatsMeat || type == "forage" && eatsPlants;
    internal static bool AllowsTrip(WellbeingWorkBand band, string type, bool eatsMeat, bool eatsPlants)
        => band < WellbeingWorkBand.LightOnly || FoodTrip(type, eatsMeat, eatsPlants);
    internal static string Label(WellbeingWorkBand band) => band switch
    {
        WellbeingWorkBand.Slower => "Taking it slower: longer work breaks",
        WellbeingWorkBand.LightOnly => "Light duties only; food trips remain available",
        WellbeingWorkBand.Recovery => "Recovery: food gathering and essential care only",
        _ => "Normal work readiness"
    };
}
