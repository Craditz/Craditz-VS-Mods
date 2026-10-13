#nullable enable
using System;
using ProtoBuf;

namespace FeralKinshipCompanions;
internal enum CompanionHomeStatus { Unestablished, Present, Missing, Unknown }

// Stage two derives wellbeing from real food and assigned-bed state.
// Work readiness is derived server-side; no companion ownership is changed.
[ProtoContract]
public sealed class CompanionWellbeingState
{
    [ProtoMember(1)] public int Version;
    [ProtoMember(2)] public float Value = 50f;
    [ProtoMember(3)] public bool HasObservation;
    [ProtoMember(4)] public double ObservedAtHours;
    [ProtoMember(5)] public string Food = "Not observed";
    [ProtoMember(6)] public string Bed = "Not observed";
    [ProtoMember(7)] public string Rest = "Not observed";
    [ProtoMember(8)] public int ObservationCount;

    [ProtoMember(9)] public float FoodContribution;
    [ProtoMember(10)] public float BedContribution;
    [ProtoMember(11)] public bool BedContributionKnown;
    [ProtoMember(12)] public bool FoodEnabled;
    [ProtoMember(13)] public float FoodLevel;

    [ProtoMember(14)] public float RawValue = 50f;
    [ProtoMember(15)] public string Home = "Not checked";
    [ProtoMember(16)] public float HousingContribution;
    [ProtoMember(17)] public bool HasEstablishedHome;

    [ProtoMember(18)] public string Health = "Not checked";
    [ProtoMember(19)] public float HealthContribution;
    [ProtoMember(20)] public string Safety = "Not checked";
    [ProtoMember(21)] public float SafetyContribution;
    [ProtoMember(22)] public string Shelter = "Not checked";
    [ProtoMember(23)] public float ShelterContribution;
    [ProtoMember(24)] public double ThreatSeconds;
    [ProtoMember(25)] public double QuietSeconds;
    [ProtoMember(26)] public double ExposureSeconds;
    [ProtoMember(27)] public bool SettlingAfterThreat;

    [ProtoMember(28)] public string Company = "Not checked";
    [ProtoMember(29)] public float CompanyContribution;
    [ProtoMember(30)] public float KinContribution;
    [ProtoMember(31)] public string Enrichment = "Not checked";
    [ProtoMember(32)] public float EnrichmentContribution;
    [ProtoMember(33)] public double CompanySeconds;
    [ProtoMember(34)] public double IsolationSeconds;
    [ProtoMember(35)] public double NoActivitySeconds;
    [ProtoMember(36)] public double SocialActivityRemaining;
    [ProtoMember(37)] public double ExploreActivityRemaining;
    [ProtoMember(38)] public double AmenityActivityRemaining;
    [ProtoMember(39)] public string LastActivity = "None observed";

    [ProtoMember(40)] public string Workload = "No recent exertion";
    [ProtoMember(41)] public float WorkloadContribution;
    [ProtoMember(42)] public double WorkBurden;
    [ProtoMember(43)] public double WorkQuietSeconds;
    [ProtoMember(44)] public int CompletedWorkUnits;
    [ProtoMember(45)] public long LastChargedExpeditionId;
    [ProtoMember(46)] public int TripsSinceRest;
    [ProtoMember(47)] public string LastWork = "None";

    [ProtoMember(48)] public long LastExpeditionOutcomeId;
    [ProtoMember(49)] public double ExpeditionGoodSeconds;
    [ProtoMember(50)] public double ExpeditionStressSeconds;
    [ProtoMember(51)] public float ExpeditionFeelingContribution;
    [ProtoMember(52)] public string ExpeditionFeeling = "No recent expedition";
    [ProtoMember(53)] public double ExpeditionHoursCharged;
    [ProtoMember(54)] public double ExpeditionLastWorldHour;

    [ProtoMember(55)] public int LastWellbeingRequestCount;
    [ProtoMember(56)] public int LastWellbeingLevel;
    [ProtoMember(57)] public double RequestFeelingSeconds;
    [ProtoMember(58)] public double ProgressFeelingSeconds;
    [ProtoMember(59)] public float RequestFeelingContribution;
    [ProtoMember(60)] public float ProgressFeelingContribution;
    [ProtoMember(61)] public string LastWellbeingRequest = string.Empty;

    [ProtoMember(62)] public int WorkBand;

    [ProtoMember(63)] public string FavoriteFoodCode = string.Empty;
    [ProtoMember(64)] public string FavoriteFoodName = string.Empty;
    [ProtoMember(65)] public double FavoriteFoodSeconds;
    [ProtoMember(66)] public float FavoriteFoodContribution;
    [ProtoMember(67)] public string ComfortBedKey = string.Empty;
    [ProtoMember(68)] public bool ComfortBedReached;
    [ProtoMember(69)] public double ComfortBedAbsentHours;
    [ProtoMember(70)] public float BedComfortContribution;
    [ProtoMember(71)] public string BedComfort = "No comfort verified";

    [ProtoMember(72)] public bool ReseedingUnlocked;

    public CompanionWellbeingState Copy() => (CompanionWellbeingState)MemberwiseClone();
}

internal static class CompanionWellbeingRules
{
    internal const int Version = 10;
    // A reload/clock jump never accrues wellbeing changes; scoring uses current needs only.
    public static CompanionWellbeingState Normalize(CompanionWellbeingState? state)
    {
        state ??= new CompanionWellbeingState();
        if (state.Version > Version) return state; // Preserve unknown future state.
        if (state.Version <= 0) state.Value = 50f;
        state.Version = Version;
        state.Value = float.IsFinite(state.Value) ? Math.Clamp(state.Value, -100f, 100f) : 50f;
        if (!double.IsFinite(state.ObservedAtHours) || state.ObservedAtHours < 0d)
        {
            state.HasObservation = false;
            state.ObservedAtHours = 0d;
        }
        state.ObservationCount = Math.Max(0, state.ObservationCount);
        state.FoodContribution = float.IsFinite(state.FoodContribution) ? Math.Clamp(state.FoodContribution, -25f, 15f) : 0f;
        state.BedContribution = float.IsFinite(state.BedContribution) ? Math.Clamp(state.BedContribution, -10f, 10f) : 0f;
        return state;
    }

    internal static void Recalculate(CompanionWellbeingState state)
    {
        state.RawValue = state.HasEstablishedHome ? 50f + state.FoodContribution + state.HousingContribution
            + state.HealthContribution + state.SafetyContribution + state.ShelterContribution
            + state.CompanyContribution + state.KinContribution + state.EnrichmentContribution + state.WorkloadContribution + state.ExpeditionFeelingContribution + state.RequestFeelingContribution + state.ProgressFeelingContribution + state.FavoriteFoodContribution + state.BedComfortContribution : 50f;
        state.Value = Soften(state.RawValue);
    }

    internal static float Soften(float raw)
    {
        if (!float.IsFinite(raw)) return 50f;
        if (raw >= 0) return Math.Min(100f, raw);
        return (float)(raw / (1d + Math.Abs((double)raw) / 100d));
    }

    internal static float FoodPoints(float level, bool enabled)
    {
        if (!enabled) return 0f;
        if (level <= 0.001f) return -25f;
        if (level < 0.25f) return -15f;
        if (level < 0.65f) return -5f;
        return 15f;
    }

    internal static float? BedPoints(string bed) => bed switch
    {
        "No assigned bed" or "Assigned; block changed" => -10f,
        "Assigned; block present" => 10f,
        _ => null
    };

    public static bool Observe(CompanionWellbeingState state, bool ownerOnline, bool available,
        double hours, string food, string bed, string rest, float foodLevel = 1f, bool foodEnabled = true, CompanionHomeStatus home = CompanionHomeStatus.Present)
    {
        if (state.Version != Version || !ownerOnline || !available || !double.IsFinite(hours) || hours < 0d
            || foodEnabled && !float.IsFinite(foodLevel))
            return false;
        state.Food = food;
        state.Bed = bed;
        state.Rest = rest;
        state.ObservedAtHours = hours;
        state.HasObservation = true;
        state.ObservationCount = state.ObservationCount == int.MaxValue ? int.MaxValue : state.ObservationCount + 1;
        state.FoodLevel = float.IsFinite(foodLevel) ? Math.Clamp(foodLevel, 0f, 1f) : 1f;
        state.FoodEnabled = foodEnabled;
        state.FoodContribution = FoodPoints(state.FoodLevel, foodEnabled);
        float? bedPoints = BedPoints(bed);
        if (bedPoints.HasValue)
        {
            state.BedContribution = bedPoints.Value;
            state.BedContributionKnown = true;
        }
        if (home == CompanionHomeStatus.Unestablished && state.HasEstablishedHome)
            home = CompanionHomeStatus.Missing; // Never downgrade an established record to the starter exemption.
        // Once established, losing the cart never returns this owner to the starter exemption.
        state.Home = home switch
        {
            CompanionHomeStatus.Unestablished => "No Pack Cart yet",
            CompanionHomeStatus.Present => "Pack Cart registered",
            CompanionHomeStatus.Missing => "No home",
            _ => "Home not currently verifiable"
        };
        if (home == CompanionHomeStatus.Unestablished)
        {
            state.HasEstablishedHome = false;
            state.FoodContribution = 0;
            state.HousingContribution = 0;
            state.RawValue = state.Value = 50;
            return true;
        }
        state.HasEstablishedHome = true;
        if (home == CompanionHomeStatus.Missing) state.HousingContribution = -25;
        else if (home == CompanionHomeStatus.Present)
            state.HousingContribution = state.BedContributionKnown ? state.BedContribution : 0;
        // Unknown housing retains its last confirmed contribution. Missing-home replaces bed scoring.
        Recalculate(state);
        // Work readiness is evaluated at new-job boundaries; transient personality mood remains separate.
        return true;
    }
}
