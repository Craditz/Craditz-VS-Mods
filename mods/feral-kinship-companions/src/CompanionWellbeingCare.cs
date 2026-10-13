#nullable enable
using System;
using System.Collections.Generic;
namespace FeralKinshipCompanions;
internal static class CompanionWellbeingCare
{
    internal static string ChooseFavorite(string current, bool currentExists, IReadOnlyList<string> candidates, int choice)
    {
        if (!string.IsNullOrEmpty(current) && currentExists) return current;
        return candidates.Count == 0 ? string.Empty : candidates[(int)((uint)choice % (uint)candidates.Count)];
    }
    internal static bool Meal(CompanionWellbeingState s, string code, bool consumed)
    {
        if (s.Version != CompanionWellbeingRules.Version || !s.HasEstablishedHome || !consumed
            || string.IsNullOrEmpty(code) || !string.Equals(code, s.FavoriteFoodCode, StringComparison.Ordinal)) return false;
        s.FavoriteFoodSeconds = 600;
        TickFood(s, 0);
        return true;
    }
    internal static void TickFood(CompanionWellbeingState s, double seconds)
    {
        if (s.Version != CompanionWellbeingRules.Version) return;
        seconds = double.IsFinite(seconds) ? Math.Clamp(seconds, 0, 5) : 0;
        s.FavoriteFoodSeconds = double.IsFinite(s.FavoriteFoodSeconds) ? Math.Max(0, Math.Min(600, s.FavoriteFoodSeconds) - seconds) : 0;
        s.FavoriteFoodContribution = (float)(s.FavoriteFoodSeconds / 120);
        CompanionWellbeingRules.Recalculate(s);
    }
    internal static void Bed(CompanionWellbeingState s, string key, bool verified, bool reached,
        bool fancy, double hours, double hoursPerDay, bool homePresent)
    {
        if (s.Version != CompanionWellbeingRules.Version) return;
        if (!verified) return; // Unloaded/unknown block is not failed access.
        if (!string.Equals(s.ComfortBedKey, key, StringComparison.Ordinal))
        {
            s.ComfortBedKey = key;
            s.ComfortBedReached = false;
            s.ComfortBedAbsentHours = 0;
        }
        if (string.IsNullOrEmpty(key))
        {
            s.BedComfortContribution = 0;
            s.BedComfort = "No assigned bed";
        }
        else
        {
            double limit = (double.IsFinite(hoursPerDay) && hoursPerDay > 0 ? hoursPerDay : 24) * 2;
            if (!double.IsFinite(s.ComfortBedAbsentHours)) s.ComfortBedAbsentHours = 0;
            if (reached) { s.ComfortBedReached = true; s.ComfortBedAbsentHours = 0; }
            else if (double.IsFinite(hours) && hours > 0) s.ComfortBedAbsentHours = Math.Min(limit, s.ComfortBedAbsentHours + hours);
            bool expired = s.ComfortBedAbsentHours >= limit;
            s.BedComfortContribution = fancy && s.ComfortBedReached && !expired && homePresent ? 5 : 0;
            s.BedComfort = expired ? "Bed not reached for two days" : !s.ComfortBedReached ? "Reach assigned bed to verify comfort"
                : fancy ? "Fancy bed" : "Basic bed";
        }
        CompanionWellbeingRules.Recalculate(s);
    }
}
