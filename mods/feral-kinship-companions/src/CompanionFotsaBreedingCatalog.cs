#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace FeralKinshipCompanions;

internal sealed class CompanionFotsaOffspring
{
    internal CompanionFotsaOffspring(string childCode, string adultCode)
    { ChildCode = childCode; AdultCode = adultCode; }
    internal string ChildCode { get; }
    internal string AdultCode { get; }
}

/// <summary>
/// Built from resolved runtime definitions after Tamables finalization. Never
/// guesses taxonomy, a baby code, or gestation from the generic UI profile.
/// Tamables' released birth override uses registered same-family tame babies,
/// including for nest-laying birds. We preserve that explicit tame-side design.
/// </summary>
internal static class CompanionFotsaBreedingCatalog
{
    private static Dictionary<string, CompanionBreedingDefinition> adults = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, CompanionBreedingDefinition> juveniles = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, CompanionFotsaOffspring> growth = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, string> unavailable = new(StringComparer.OrdinalIgnoreCase);

    internal static bool IsFotsa(AssetLocation? code) => code != null
        && code.Domain.Equals("tamablesfotsa", StringComparison.OrdinalIgnoreCase)
        && code.Path.StartsWith("tame-", StringComparison.OrdinalIgnoreCase);
    internal static bool IsAge(EntityProperties type, string age) =>
        type.Variant != null && type.Variant.TryGetValue("age", out string? value) && value == age;
    internal static string Gender(EntityProperties type) =>
        type.Variant != null && type.Variant.TryGetValue("gender", out string? value) ? value : string.Empty;

    // Keep every taxonomic/type token; remove only known life-stage, sex and
    // ownership-form tokens. Pigment is optional for mating but maternal for births.
    internal static string FamilyKey(EntityProperties type, bool includePigment = false)
    {
        string pigment = type.Variant != null && type.Variant.TryGetValue("pigment", out string? value) ? value : string.Empty;
        return string.Join("-", type.Code.Path.ToLowerInvariant().Split('-').Where(token =>
            token is not ("adult" or "baby" or "female" or "male" or "tamed" or "semitamed")
            && (includePigment || string.IsNullOrEmpty(pigment) || token != pigment)));
    }

    internal static JsonObject? FindBehavior(EntityProperties type, string code) =>
        type.Server?.BehaviorsAsJsonObj?.FirstOrDefault(behavior => behavior["code"].AsString() == code);

    internal static bool TryGetForAdult(AssetLocation? code, out CompanionBreedingDefinition definition)
    {
        definition = null!;
        return code != null && adults.TryGetValue(code.ToString(), out definition!);
    }
    internal static bool TryGetForJuvenile(AssetLocation? code, out CompanionBreedingDefinition definition)
    {
        definition = null!;
        return code != null && juveniles.TryGetValue(code.ToString(), out definition!);
    }
    internal static bool TryGetGrowth(AssetLocation? code, out CompanionFotsaOffspring child)
    {
        child = null!;
        return code != null && growth.TryGetValue(code.ToString(), out child!);
    }
    internal static string UnavailableReason(AssetLocation? code) =>
        code != null && unavailable.TryGetValue(code.ToString(), out string? reason) ? reason : "unregistered FOTSA reproduction definition";

    internal static IReadOnlyDictionary<string, string> Initialize(IEnumerable<EntityProperties> entityTypes)
    {
        EntityProperties[] types = entityTypes.Where(type => IsFotsa(type.Code)).ToArray();
        Dictionary<string, EntityProperties> byCode = types.ToDictionary(type => type.Code.ToString(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, CompanionFotsaOffspring> newGrowth = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> missing = new(StringComparer.OrdinalIgnoreCase);
        foreach (EntityProperties baby in types.Where(type => IsAge(type, "baby")))
        {
            JsonObject? grow = FindBehavior(baby, "grow");
            string[] destinations = (grow?["adultEntityCodes"].AsArray<string>(Array.Empty<string>()) ?? Array.Empty<string>())
                .Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code!).ToArray();
            // Released Tamables has one exact age/sex-preserving destination.
            // An unknown future multi-destination definition fails closed.
            if (destinations.Length != 1 || !byCode.TryGetValue(destinations[0], out EntityProperties? adult)
                || !IsAge(adult, "adult") || FamilyKey(baby) != FamilyKey(adult)
                || Gender(baby) != Gender(adult) || Gender(baby) is not ("female" or "male"))
            { missing[baby.Code.ToString()] = "missing or mismatched tame juvenile growth destination"; continue; }
            newGrowth[baby.Code.ToString()] = new(baby.Code.ToString(), adult.Code.ToString());
        }

        Dictionary<string, CompanionBreedingDefinition> newAdults = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, CompanionBreedingDefinition> newJuveniles = new(StringComparer.OrdinalIgnoreCase);
        foreach (EntityProperties mother in types.Where(type => IsAge(type, "adult") && Gender(type) == "female"))
        {
            string motherCode = mother.Code.ToString();
            JsonObject? multiply = FindBehavior(mother, "tamablesfotsamultiply");
            if (multiply == null || !multiply["enabled"].AsBool(true))
            { missing[motherCode] = "no enabled source multiply definition"; continue; }

            double pregnancy = multiply["pregnancyDays"].AsDouble(3);
            double qMin = multiply["spawnQuantityMin"].AsDouble(1), qMax = multiply["spawnQuantityMax"].AsDouble(2);
            double cMin = multiply["multiplyCooldownDaysMin"].AsDouble(6), cMax = multiply["multiplyCooldownDaysMax"].AsDouble(12);
            if (!double.IsFinite(pregnancy) || pregnancy <= 0 || !double.IsFinite(qMin) || qMin < 0
                || !double.IsFinite(qMax) || qMax < qMin || qMax > 128
                || !double.IsFinite(cMin) || cMin < 0 || !double.IsFinite(cMax) || cMax < cMin)
            { missing[motherCode] = "invalid source pregnancy/litter/cooldown values"; continue; }

            string family = FamilyKey(mother), appearanceFamily = FamilyKey(mother, true);
            EntityProperties[] babies = types.Where(type => IsAge(type, "baby") && FamilyKey(type, true) == appearanceFamily).ToArray();
            if (babies.Length == 0 || babies.Any(baby => !newGrowth.ContainsKey(baby.Code.ToString()))
                || !babies.Any(baby => Gender(baby) == "female") || !babies.Any(baby => Gender(baby) == "male"))
            { missing[motherCode] = "no complete same-species tame offspring/growth pair"; continue; }

            string[] matePatterns = multiply["requiresNearbyEntityCodes"].AsArray<string>(Array.Empty<string>())
                .Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code!).ToArray();
            if (matePatterns.Length == 0 && multiply["requiresNearbyEntityCode"].AsString() is string single)
                matePatterns = new[] { single };
            // Keep source mating restrictions while narrowing broad upstream
            // genus wildcards to this exact species. No unrelated crossbreeds.
            EntityProperties[] males = types.Where(type => IsAge(type, "adult") && Gender(type) == "male"
                && FamilyKey(type) == family
                && matePatterns.Any(pattern => Vintagestory.API.Util.WildcardUtil.Match(new AssetLocation(pattern), type.Code))).ToArray();
            if (males.Length == 0)
            { missing[motherCode] = "no registered same-species male allowed by source mating definition"; continue; }

            CompanionBreedingDefinition definition = new("tamables-fotsa", pregnancy, (int)Math.Floor(qMin), (int)Math.Ceiling(qMax))
            {
                FamilyId = family, FotsaOffspring = babies.Select(baby => newGrowth[baby.Code.ToString()]).ToArray(),
                AllowedMateCodes = new(males.Select(male => male.Code.ToString()), StringComparer.OrdinalIgnoreCase),
                SourceQuantityMin = qMin, SourceQuantityMax = qMax, CooldownDaysMin = cMin, CooldownDaysMax = cMax
            };
            newAdults[motherCode] = definition;
            foreach (EntityProperties male in males) newAdults.TryAdd(male.Code.ToString(), definition);
            foreach (EntityProperties baby in babies) newJuveniles.TryAdd(baby.Code.ToString(), definition);
        }
        foreach (EntityProperties adult in types.Where(type => IsAge(type, "adult")))
            if (!newAdults.ContainsKey(adult.Code.ToString())) missing.TryAdd(adult.Code.ToString(), "no compatible validated female reproduction definition");
        adults = newAdults; juveniles = newJuveniles; growth = newGrowth; unavailable = missing;
        return missing;
    }
}
