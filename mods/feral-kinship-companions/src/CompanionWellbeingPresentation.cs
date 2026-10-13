#nullable enable
using System;
using System.Collections.Generic;
namespace FeralKinshipCompanions;

internal static class CompanionWellbeingPresentation
{
    // Report observed problems only. A present block does not prove reachability.
    public static List<string> Problems(CompanionWellbeingState? state)
    {
        var result = new List<string>();
        if (state?.HasObservation != true) return result;
        var band = CompanionWellbeingRecovery.Update(state);
        if (band != WellbeingWorkBand.Normal) result.Add(band == WellbeingWorkBand.Recovery ? "Recovery: food duties only" : band == WellbeingWorkBand.LightOnly ? "Light duties only" : "Taking longer work breaks");
        if (state.Food?.StartsWith("Starving", StringComparison.Ordinal) == true)
            result.Add("Starving: needs food");
        else if (state.Food == "Very hungry" || state.Food == "Hungry")
            result.Add(state.Food + ": needs food");
        if (state.Health is "Minor wounds" or "Injured" or "Badly wounded" or "Downed: needs treatment" or "Recovering") result.Add(state.Health + ": needs care");
        if (state.Safety == "Threat lingering nearby" || state.Safety == "Nearby threat; staying alert") result.Add("Nearby wild predator");
        if (state.ShelterContribution < 0) result.Add("Exposed to wet weather: needs shelter");
        if (state.CompanyContribution < 0) result.Add("Missing company: needs companionship");
        if (state.EnrichmentContribution < 0) result.Add("Needs something to do");
        if (state.WorkloadContribution < 0) result.Add("Overworked: needs a break");
        if (state.Home == "No home") { result.Add("No home: replace the Pack Cart"); return result; }
        if (state.Home == "No Pack Cart yet") return result;
        if (state.Home == "Home not currently verifiable") { result.Add("Home check pending; previous value held"); return result; }
        if (state.BedComfort == "Bed not reached for two days") result.Add("Bed not reached for 2 days");
        if (state.Bed == "No assigned bed") result.Add("No bed assigned");
        else if (state.Bed == "Assigned; block changed") result.Add("Assigned bed is missing or changed");
        return result;
    }
}
