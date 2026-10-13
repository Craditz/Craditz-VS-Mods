#nullable enable
using System;
namespace FeralKinshipCompanions;

// Timers consume only observed, loaded, owner-online seconds. No calendar catch-up.
internal static class CompanionWellbeingSafety
{
    internal static double SafeDelta(long previous, long now) => previous >= 0 && now >= previous && now - previous <= 5000 ? (now - previous) / 1000d : 0;
    internal static void Observe(CompanionWellbeingState s, float fraction, bool downed, bool recovering,
        bool threat, bool exposedRain, double seconds)
    {
        s.ThreatSeconds = double.IsFinite(s.ThreatSeconds) ? Math.Clamp(s.ThreatSeconds, 0, 30) : 0;
        s.QuietSeconds = double.IsFinite(s.QuietSeconds) ? Math.Clamp(s.QuietSeconds, 0, 120) : 0;
        s.ExposureSeconds = double.IsFinite(s.ExposureSeconds) ? Math.Clamp(s.ExposureSeconds, 0, 60) : 0;
        seconds = double.IsFinite(seconds) ? Math.Clamp(seconds, 0, 5) : 0;
        if (float.IsFinite(fraction))
        {
            fraction = Math.Clamp(fraction, 0, 1);
            s.Health = downed ? "Downed: needs treatment" : recovering ? "Recovering" : fraction < .3f ? "Badly wounded" : fraction < .65f ? "Injured" : fraction < .95f ? "Minor wounds" : "Healthy";
            s.HealthContribution = downed ? -30 : fraction < .3f ? -25 : fraction < .65f ? -15 : fraction < .95f ? -5 : 0;
            if (recovering) s.HealthContribution = Math.Min(-5, s.HealthContribution);
        }
        else s.Health = "Health unknown; previous value held";
        if (threat)
        {
            s.QuietSeconds = 0;
            s.ThreatSeconds = Math.Min(30, Math.Max(0, s.ThreatSeconds) + seconds);
            if (s.ThreatSeconds >= 30) s.SettlingAfterThreat = true;
            s.Safety = s.ThreatSeconds >= 30 ? "Threat lingering nearby" : "Nearby threat; staying alert";
            s.SafetyContribution = s.ThreatSeconds >= 30 ? -15 : 0;
        }
        else
        {
            s.ThreatSeconds = 0;
            s.QuietSeconds = Math.Min(120, Math.Max(0, s.QuietSeconds) + seconds);
            if (s.QuietSeconds >= 60) s.SettlingAfterThreat = false;
            s.Safety = s.SettlingAfterThreat ? "Settling after danger" : s.QuietSeconds >= 120 ? "Feeling safe" : "No nearby threat seen";
            s.SafetyContribution = s.SettlingAfterThreat ? -5 : s.QuietSeconds >= 120 ? 5 : 0;
        }
        s.ExposureSeconds = exposedRain ? Math.Min(60, Math.Max(0, s.ExposureSeconds) + seconds) : 0;
        s.Shelter = exposedRain ? s.ExposureSeconds >= 60 ? "Exposed to wet weather" : "Wet weather; seeking cover" : "No weather exposure";
        s.ShelterContribution = s.ExposureSeconds >= 60 ? -5 : 0;
        if (!s.HasEstablishedHome) s.HealthContribution = s.SafetyContribution = s.ShelterContribution = 0;
        CompanionWellbeingRules.Recalculate(s);
    }
}
