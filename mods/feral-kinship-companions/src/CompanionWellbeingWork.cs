#nullable enable
using System;
namespace FeralKinshipCompanions;
internal static class CompanionWellbeingWork
{
    internal static bool NeedsRest(CompanionWellbeingState? s) => s?.Version == CompanionWellbeingRules.Version
        && s.HasEstablishedHome && double.IsFinite(s.WorkBurden) && s.WorkBurden > .000001;
    internal static float ScanFactor(bool ownerOnline) => ownerOnline ? 1f : 5f;
    private static void Normalize(CompanionWellbeingState s)
    {
        s.WorkBurden = double.IsFinite(s.WorkBurden) ? Math.Clamp(s.WorkBurden, 0, 100) : 0;
        s.WorkQuietSeconds = double.IsFinite(s.WorkQuietSeconds) ? Math.Clamp(s.WorkQuietSeconds, 0, 30) : 0;
        s.CompletedWorkUnits = Math.Max(0, s.CompletedWorkUnits);
        s.TripsSinceRest = Math.Max(0, s.TripsSinceRest);
    }
    internal static bool CompleteDuty(CompanionWellbeingState s, double units, bool ownerOnline, bool selfProvisioning, string label)
    {
        if (s.Version != CompanionWellbeingRules.Version || !ownerOnline || !s.HasEstablishedHome || selfProvisioning || !double.IsFinite(units) || units <= 0) return false;
        Normalize(s);
        s.WorkBurden = Math.Min(100, s.WorkBurden + Math.Min(10, units));
        s.WorkQuietSeconds = 0;
        s.CompletedWorkUnits = s.CompletedWorkUnits == int.MaxValue ? int.MaxValue : s.CompletedWorkUnits + 1;
        s.LastWork = label;
        Update(s, false, false);
        return true;
    }
    internal static bool Depart(CompanionWellbeingState s, long expeditionId, string kind, bool ownerOnline)
    {
        if (s.Version != CompanionWellbeingRules.Version || expeditionId <= 0 || expeditionId <= s.LastChargedExpeditionId) return false;
        Normalize(s);
        s.LastChargedExpeditionId = expeditionId;
        s.ExpeditionHoursCharged = 0;
        if (s.HasEstablishedHome) s.TripsSinceRest = Math.Min(int.MaxValue - 1, s.TripsSinceRest) + 1;
        s.LastWork = "Expedition: " + kind;
        return true;
    }
    internal static double ChargeHours(CompanionWellbeingState s, double from, double now, double end, bool observedOnline, string kind)
    {
        if (s.Version != CompanionWellbeingRules.Version || !double.IsFinite(from) || !double.IsFinite(now) || !double.IsFinite(end)) return 0;
        double hours = observedOnline && s.HasEstablishedHome && kind is not ("hunt" or "forage")
            ? Math.Max(0, Math.Min(now, end) - from) : 0;
        s.ExpeditionLastWorldHour = Math.Max(from, now);
        if (hours <= 0) return 0;
        Normalize(s);
        s.ExpeditionHoursCharged += hours;
        s.WorkBurden = Math.Min(100, s.WorkBurden + hours);
        s.WorkQuietSeconds = 0;
        Update(s, true, false);
        return hours;
    }
    internal static bool Outcome(CompanionWellbeingState s, long id, bool carry, bool helper, bool safeSuccess, bool ownerOnline)
    {
        if (s.Version != CompanionWellbeingRules.Version || id <= 0 || id <= s.LastExpeditionOutcomeId) return false;
        s.LastExpeditionOutcomeId = id;
        if (!s.HasEstablishedHome) return true;
        Normalize(s);
        if (carry)
        {
            s.ExpeditionStressSeconds = 600;
            if (helper && ownerOnline) {s.WorkBurden = Math.Min(100, s.WorkBurden + 5); s.WorkQuietSeconds = 0;}
        }
        else if (safeSuccess) s.ExpeditionGoodSeconds = 600;
        Feelings(s, 0);
        Update(s, false, false);
        return true;
    }
    internal static void Feelings(CompanionWellbeingState s, double seconds)
    {
        seconds = double.IsFinite(seconds) ? Math.Clamp(seconds, 0, 5) : 0;
        s.ExpeditionGoodSeconds = double.IsFinite(s.ExpeditionGoodSeconds) ? Math.Max(0, Math.Min(600, s.ExpeditionGoodSeconds) - seconds) : 0;
        s.ExpeditionStressSeconds = double.IsFinite(s.ExpeditionStressSeconds) ? Math.Max(0, Math.Min(600, s.ExpeditionStressSeconds) - seconds) : 0;
        s.ExpeditionFeelingContribution = (float)((s.ExpeditionGoodSeconds - s.ExpeditionStressSeconds) / 120);
        s.ExpeditionFeeling = s.ExpeditionStressSeconds > 0 ? s.ExpeditionGoodSeconds > 0 ? "Mixed expedition memories" : "Difficult return"
            : s.ExpeditionGoodSeconds > 0 ? "Satisfying expedition" : "No recent expedition";
    }
    internal static void Observe(CompanionWellbeingState s, double seconds, bool working, bool resting)
    {
        Normalize(s);
        Feelings(s, seconds);
        seconds = double.IsFinite(seconds) ? Math.Clamp(seconds, 0, 5) : 0;
        double previousQuiet = s.WorkQuietSeconds;
        s.WorkQuietSeconds = working ? 0 : Math.Min(30, s.WorkQuietSeconds + seconds);
        double recoverSeconds = working ? 0 : Math.Max(0, seconds - Math.Max(0, 30 - previousQuiet));
        s.WorkBurden = Math.Max(0, s.WorkBurden - recoverSeconds / (resting ? 10 : 30));
        if (s.WorkBurden < .000001) { s.WorkBurden = 0; s.TripsSinceRest = 0; }
        Update(s, working, resting);
    }
    private static void Update(CompanionWellbeingState s, bool working, bool resting)
    {
        s.WorkloadContribution = !s.HasEstablishedHome ? 0 : s.WorkBurden >= 60 ? -20 : s.WorkBurden >= 30 ? -10 : s.WorkBurden >= 12 ? -5 : 0;
        s.Workload = working ? "Working" : s.WorkBurden == 0 ? "Rested" : resting ? "Resting and recovering" : "Recovering between jobs";
        CompanionWellbeingRules.Recalculate(s);
    }
}
