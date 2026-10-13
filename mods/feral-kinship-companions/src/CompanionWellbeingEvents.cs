#nullable enable
using System;
namespace FeralKinshipCompanions;
internal static class CompanionWellbeingEvents
{
    internal static bool Request(CompanionWellbeingState s, int completed, string id, bool developer)
    {
        if (s.Version != CompanionWellbeingRules.Version || developer || completed <= 0 || completed <= s.LastWellbeingRequestCount || string.IsNullOrWhiteSpace(id)) return false;
        s.LastWellbeingRequestCount = completed;
        s.LastWellbeingRequest = id;
        if (s.HasEstablishedHome) s.RequestFeelingSeconds = 600;
        Observe(s, 0);
        return true;
    }
    internal static bool Progress(CompanionWellbeingState s, int level, bool eligible)
    {
        if (s.Version != CompanionWellbeingRules.Version || !eligible || level <= 1 || level <= s.LastWellbeingLevel) return false;
        s.LastWellbeingLevel = level;
        if (s.HasEstablishedHome) s.ProgressFeelingSeconds = 600;
        Observe(s, 0);
        return true;
    }
    internal static void Observe(CompanionWellbeingState s, double seconds)
    {
        if (s.Version != CompanionWellbeingRules.Version) return;
        seconds = double.IsFinite(seconds) ? Math.Clamp(seconds, 0, 5) : 0;
        s.RequestFeelingSeconds = double.IsFinite(s.RequestFeelingSeconds) ? Math.Max(0, Math.Min(600, s.RequestFeelingSeconds) - seconds) : 0;
        s.ProgressFeelingSeconds = double.IsFinite(s.ProgressFeelingSeconds) ? Math.Max(0, Math.Min(600, s.ProgressFeelingSeconds) - seconds) : 0;
        s.RequestFeelingContribution = (float)(s.RequestFeelingSeconds / 120);
        s.ProgressFeelingContribution = (float)(s.ProgressFeelingSeconds / 200);
        CompanionWellbeingRules.Recalculate(s);
    }
}
