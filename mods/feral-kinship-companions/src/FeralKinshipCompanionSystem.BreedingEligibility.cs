#nullable enable
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    // Shared, read-only gates. Display queries never run reconciliation, RNG,
    // attempt accounting, scan scheduling, or persistence.
    private string? BreedingSelfBlockReason(Entity entity, CompanionBreedingDefinition definition, ITreeAttribute status)
    {
        if (!status.GetBool(BreedingEnabledKey, false)) return "disabled";
        if (definition.IsFotsa && entity.WatchedAttributes.GetTreeAttribute("multiply")?.GetDouble("totalDaysCooldownUntil", 0) > entity.World.Calendar.TotalDays) return "cooldown";
        if (!CompanionBreedingCatalog.TryGetGender(entity, out string gender)) return "gender";
        if (!string.Equals(gender, "female", StringComparison.Ordinal)) return "male";
        if (!string.Equals(GetCompanionActivityMode(entity), CompanionActivityMode.AtEase, StringComparison.Ordinal)) return "activity";
        if (IsFoxAwayFromWorld(entity)) return "away";
        if (IsFoxIncapacitated(entity)) return "incapacitated";
        if (IsCompanionFoodRestricted(entity)) return "food";
        return null;
    }

    private static bool IsBreedingNight(Entity entity, out long night)
    {
        double hour = entity.World.Calendar.HourOfDay;
        double hoursPerDay = entity.World.Calendar.HoursPerDay;
        night = (long)Math.Floor(entity.World.Calendar.TotalHours / hoursPerDay
            - (hour < hoursPerDay * 0.25d ? 1d : 0d));
        return hour >= hoursPerDay * 0.75d || hour < hoursPerDay * 0.25d;
    }

    private string? BreedingPartnerBlockReason(Entity mother, Entity candidate, CompanionBreedingDefinition definition)
    {
        ITreeAttribute? motherStatus = GetDomesticationStatus(mother);
        ITreeAttribute? candidateStatus = GetDomesticationStatus(candidate);
        string owner = motherStatus?.GetString("owner", string.Empty) ?? string.Empty;
        // Exclude foreign data before deriving any player-facing reason.
        if (candidate.EntityId == mother.EntityId || !candidate.Alive || string.IsNullOrWhiteSpace(owner)
            || candidateStatus == null || !string.Equals(candidateStatus.GetString("owner", string.Empty), owner, StringComparison.Ordinal)
            || !CompanionBreedingCatalog.TryGetForAdult(candidate, out CompanionBreedingDefinition candidateDefinition)
            || !definition.CanMate(mother, candidate, candidateDefinition)
            || !CompanionBreedingCatalog.TryGetGender(candidate, out string gender)
            || !string.Equals(gender, "male", StringComparison.Ordinal)) return "ignore";
        if (IsCompanionJuvenile(candidate) || IsCompanionPregnant(candidate)
            || IsFoxAwayFromWorld(candidate) || IsFoxIncapacitated(candidate)) return "partner-unavailable";
        if (IsCompanionFoodRestricted(candidate)) return "partner-food";
        if (!string.Equals(GetCompanionActivityMode(candidate), CompanionActivityMode.AtEase, StringComparison.Ordinal)) return "partner-activity";
        if (!candidateStatus.GetBool(BreedingEnabledKey, false)) return "partner-disabled";
        string motherId = motherStatus?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        string candidateId = candidateStatus.GetString(FoxIdKey, string.Empty);
        string bondedId = motherStatus?.GetString(BondedPartnerIdKey, string.Empty) ?? string.Empty;
        string candidateBondedId = candidateStatus.GetString(BondedPartnerIdKey, string.Empty);
        if (string.IsNullOrWhiteSpace(motherId) || string.IsNullOrWhiteSpace(candidateId)) return "partner-unavailable";
        if ((!string.IsNullOrWhiteSpace(bondedId) && !string.Equals(bondedId, candidateId, StringComparison.Ordinal))
            || (!string.IsNullOrWhiteSpace(candidateBondedId) && !string.Equals(candidateBondedId, motherId, StringComparison.Ordinal))) return "bonded-elsewhere";
        return AssignedBedsBlockReason(mother, candidate);
    }

    internal string GetCurrentBreedingEligibility(Entity entity)
    {
        ITreeAttribute? status = GetDomesticationStatus(entity);
        if (status == null) return BreedingReasonText("unavailable");
        if (RetainedNativeSuspension.Protected(entity)) return BreedingReasonText("away");
        if (IsCompanionJuvenile(entity)) return BreedingReasonText("juvenile");
        if (!CompanionBreedingCatalog.TryGetForAdult(entity, out CompanionBreedingDefinition definition)) return BreedingReasonText("unsupported");
        if (status.GetBool(PregnancyActiveKey, false)) return BreedingReasonText("pregnant");
        if (definition.IsFotsa && entity.WatchedAttributes.GetTreeAttribute("multiply")?.GetBool("isPregnant") == true) return BreedingReasonText("native-pregnant");
        string? reason = BreedingSelfBlockReason(entity, definition, status);
        if (reason == "cooldown")
        {
            double remaining = entity.WatchedAttributes.GetTreeAttribute("multiply")!.GetDouble("totalDaysCooldownUntil") - entity.World.Calendar.TotalDays;
            return Lang.Get("feralkinshipcompanions:breeding-current-cooldown", Math.Max(0, remaining));
        }
        if (reason != null) return BreedingReasonText(reason);
        if (!IsBreedingNight(entity, out long night)) return BreedingReasonText("night");
        if (status.GetLong(LastBreedingAttemptNightKey, long.MinValue) == night) return BreedingReasonText("attempted");
        if (!HasOnlineBreedingOwner(entity)) return BreedingReasonText("owner-offline");
        if (FindEligibleBreedingPartner(entity, definition, out reason) == null) return BreedingReasonText(reason ?? "partner");
        return BreedingReasonText("ready");
    }

    private static string BreedingReasonText(string reason) => Lang.Get("feralkinshipcompanions:breeding-current-" + reason);
}
