#nullable enable
using System;
namespace FeralKinshipCompanions;
internal static class CompanionWellbeingCompany
{
    private static double Bounded(double value, double max) => double.IsFinite(value) ? Math.Clamp(value, 0, max) : 0;
    internal static string Category(string kind) => kind switch
    {
        "companion-rest" or "packmate-greeting" or "chase" or "play-runner" => "social",
        "look-around" or "scent-investigation" or "camp-lookout" or "interest-watch" or "short-wander" or "perimeter-walk" => "explore",
        "pack-space-visit" or "pack-cart-sit" => "amenity",
        _ => string.Empty
    };
    internal static bool Credit(CompanionWellbeingState s, string kind, double performedSeconds)
    {
        if (s.Version != CompanionWellbeingRules.Version || !double.IsFinite(performedSeconds) || performedSeconds < 4) return false;
        string category = Category(kind);
        if (category.Length == 0) return false;
        if (category == "social") s.SocialActivityRemaining = 600;
        if (category == "explore") s.ExploreActivityRemaining = 600;
        if (category == "amenity") s.AmenityActivityRemaining = 600;
        s.NoActivitySeconds = 0;
        s.LastActivity = kind.Replace('-', ' ');
        return true;
    }
    internal static void Observe(CompanionWellbeingState s, string personality, bool packmate, bool sameKind,
        bool ownerNearby, bool companyKnown, bool ableToParticipate, double seconds)
    {
        seconds = Bounded(seconds, 5);
        s.CompanySeconds = Bounded(s.CompanySeconds, 30);
        s.IsolationSeconds = Bounded(s.IsolationSeconds, 600);
        s.NoActivitySeconds = Bounded(s.NoActivitySeconds, 1200);
        s.SocialActivityRemaining = Math.Max(0, Bounded(s.SocialActivityRemaining, 600) - seconds);
        s.ExploreActivityRemaining = Math.Max(0, Bounded(s.ExploreActivityRemaining, 600) - seconds);
        s.AmenityActivityRemaining = Math.Max(0, Bounded(s.AmenityActivityRemaining, 600) - seconds);
        bool independent = personality is "solitary" or "independent";
        bool social = personality is "social" or "affectionate";
        if (companyKnown)
        {
            bool company = packmate || ownerNearby;
            s.CompanySeconds = company ? Math.Min(30, s.CompanySeconds + seconds) : 0;
            if (company) s.IsolationSeconds = 0;
            else if (ableToParticipate) s.IsolationSeconds = Math.Min(600, s.IsolationSeconds + seconds);
            s.Company = packmate ? "Packmate nearby" : ownerNearby ? "Owner nearby" : independent ? "Comfortable with solitude" : s.IsolationSeconds >= 600 ? "Missing company" : "Some time alone";
            s.CompanyContribution = company && s.CompanySeconds >= 30
                ? packmate ? independent ? 2 : social ? 8 : 5
                : personality == "affectionate" ? 8 : independent ? 1 : personality == "social" ? 4 : 3
                : !company && !independent && ableToParticipate && s.IsolationSeconds >= 600 ? social ? -10 : -5 : 0;
            s.KinContribution = packmate && sameKind && s.CompanySeconds >= 30 ? 2 : 0;
        }
        else s.Company = "Company not currently verifiable";
        int variety = (s.SocialActivityRemaining > 0 ? 1 : 0) + (s.ExploreActivityRemaining > 0 ? 1 : 0) + (s.AmenityActivityRemaining > 0 ? 1 : 0);
        if (ableToParticipate && variety == 0) s.NoActivitySeconds = Math.Min(1200, s.NoActivitySeconds + seconds);
        bool activePersonality = personality is "playful" or "curious" or "restless";
        s.Enrichment = variety > 0 ? $"Recent activity: {s.LastActivity}" : !ableToParticipate ? "Recuperating; activity not expected" : s.NoActivitySeconds >= 1200 ? "Needs something to do" : "No recent activity observed";
        s.EnrichmentContribution = variety > 0 ? Math.Min(activePersonality ? 10 : 8, (activePersonality ? 7 : 5) + (variety - 1) * 2)
            : ableToParticipate && s.NoActivitySeconds >= 1200 ? activePersonality ? -10 : -5 : 0;
        if (!s.HasEstablishedHome) s.CompanyContribution = s.KinContribution = s.EnrichmentContribution = 0;
        CompanionWellbeingRules.Recalculate(s);
    }
}
