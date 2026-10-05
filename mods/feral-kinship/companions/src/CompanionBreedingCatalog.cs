#nullable enable

using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;

internal sealed class CompanionBreedingDefinition
{
    private readonly string juvenileDomain;
    private readonly string juvenilePrefix;
    private readonly string juvenileContains;
    private readonly string juvenileSuffix;
    private readonly string adultDomain;
    private readonly string adultPrefix;

    public CompanionBreedingDefinition(
        string speciesId,
        double pregnancyDays,
        int litterSizeMin,
        int litterSizeMax,
        string? childCodeTemplate = null,
        string? adultCodeTemplate = null,
        string? juvenileDomain = null,
        string? juvenilePrefix = null,
        string? juvenileContains = null,
        string? juvenileSuffix = null,
        string? adultDomain = null,
        string? adultPrefix = null)
    {
        SpeciesId = speciesId;
        PregnancyDays = pregnancyDays;
        LitterSizeMin = Math.Max(1, litterSizeMin);
        LitterSizeMax = Math.Max(LitterSizeMin, litterSizeMax);
        ChildCodeTemplate = childCodeTemplate ?? "game:{species}-{type}-baby-{gender}";
        AdultCodeTemplate = adultCodeTemplate
            ?? (string.Equals(speciesId, "pig", StringComparison.Ordinal)
                ? "feralkinship:tamepig-{type}-adult-{gender}"
                : "feralkinship:tame{species}-{type}-{gender}");
        this.juvenileDomain = juvenileDomain ?? "game";
        this.juvenilePrefix = juvenilePrefix ?? speciesId + "-";
        this.juvenileContains = juvenileContains ?? "-baby-";
        this.juvenileSuffix = juvenileSuffix ?? string.Empty;
        this.adultDomain = adultDomain ?? "feralkinship";
        this.adultPrefix = adultPrefix ?? $"tame{speciesId}-";
    }

    public string SpeciesId { get; }
    public double PregnancyDays { get; }
    public int LitterSizeMin { get; }
    public int LitterSizeMax { get; }
    private string ChildCodeTemplate { get; }
    private string AdultCodeTemplate { get; }

    public string BuildChildCode(string typeId, string genderId) =>
        FormatCode(ChildCodeTemplate, typeId, genderId);

    public string BuildAdultCode(string typeId, string genderId) =>
        FormatCode(AdultCodeTemplate, typeId, genderId);

    public bool MatchesJuvenileCode(AssetLocation? code)
    {
        if (code == null
            || !string.Equals(code.Domain, juvenileDomain, StringComparison.OrdinalIgnoreCase)
            || !code.Path.StartsWith(juvenilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return (string.IsNullOrEmpty(juvenileContains)
                || code.Path.Contains(juvenileContains, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrEmpty(juvenileSuffix)
                || code.Path.EndsWith(juvenileSuffix, StringComparison.OrdinalIgnoreCase));
    }

    public bool MatchesAdultCode(AssetLocation? code)
    {
        return code != null
            && string.Equals(code.Domain, adultDomain, StringComparison.OrdinalIgnoreCase)
            && code.Path.StartsWith(adultPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private string FormatCode(string template, string typeId, string genderId) =>
        template
            .Replace("{species}", SpeciesId, StringComparison.Ordinal)
            .Replace("{type}", typeId, StringComparison.Ordinal)
            .Replace("{gender}", genderId, StringComparison.Ordinal);
}

/// <summary>
/// The deliberately narrow first breeding slice. Every entry is backed by a
/// vanilla 1.22 live-birth definition and a vanilla juvenile that can retain
/// its original growth and parent-follow AI while Companions adds metadata.
/// </summary>
internal static class CompanionBreedingCatalog
{
    public const double AttemptChance = 0.35d;
    public const float PregnancyMovementMultiplier = 0.80f;
    public const float PregnancyExpeditionMultiplier = 0.60f;

    private static readonly Dictionary<string, CompanionBreedingDefinition> BySpecies =
        new(StringComparer.Ordinal)
        {
            ["fox"] = new("fox", 5d, 4, 6),
            ["hare"] = new("hare", 5d, 3, 5),
            ["hyena"] = new("hyena", 3d, 1, 2),
            ["wolf"] = new("wolf", 3d, 4, 6),
            ["goat"] = new("goat", 20d, 1, 2),
            ["pig"] = new("pig", 25d, 8, 12),
            ["sheep"] = new("sheep", 20d, 1, 2)
        };

    // These definitions deliberately retain the source mod's entity codes.
    // Companions owns the relationship, eligibility, pregnancy, and birth
    // decision; the source mod still owns the baby model and normal growth
    // path. That makes removing Companions safe for existing source animals.
    private static readonly CompanionBreedingDefinition[] ExternalDefinitions =
    {
        new(
            "dog",
            20d,
            4,
            6,
            childCodeTemplate: "wolftaming:dog-{type}-pup",
            adultCodeTemplate: "wolftaming:dog-{type}-{gender}",
            juvenileDomain: "wolftaming",
            juvenilePrefix: "dog-",
            juvenileContains: string.Empty,
            juvenileSuffix: "-pup",
            adultDomain: "wolftaming",
            adultPrefix: "dog-"),
        new(
            "cat",
            20d,
            3,
            5,
            childCodeTemplate: "cats:cat-kitten-{type}",
            adultCodeTemplate: "cats:cat-{gender}-{type}",
            juvenileDomain: "cats",
            juvenilePrefix: "cat-kitten-",
            juvenileContains: string.Empty,
            adultDomain: "cats",
            adultPrefix: "cat-"),
        new(
            "fox",
            5d,
            4,
            6,
            childCodeTemplate: "foxtaming:tamefox-{type}-baby-{gender}",
            adultCodeTemplate: "foxtaming:tamefox-{type}-adult-{gender}",
            juvenileDomain: "foxtaming",
            juvenilePrefix: "tamefox-",
            juvenileContains: "-baby-",
            adultDomain: "foxtaming",
            adultPrefix: "tamefox-")
    };

    public static IReadOnlyCollection<string> SupportedSpeciesIds => BySpecies.Keys;

    public static bool TryGetForAdult(Entity entity, out CompanionBreedingDefinition definition)
    {
        if (entity.Code != null
            && CompanionSpeciesCatalog.TryGetByTameEntityCode(entity.Code, out CompanionSpeciesProfile profile)
            && TryGetExternalDefinition(entity.Code, profile.Id, out definition))
        {
            return true;
        }

        if (entity.Code != null
            && CompanionSpeciesCatalog.TryGetByTameEntityCode(entity.Code, out profile)
            && BySpecies.TryGetValue(profile.Id, out CompanionBreedingDefinition? found))
        {
            definition = found;
            return true;
        }

        definition = null!;
        return false;
    }

    public static bool TryGetForJuvenile(Entity entity, out CompanionBreedingDefinition definition)
    {
        definition = null!;
        if (entity.Code == null)
        {
            return false;
        }

        foreach (CompanionBreedingDefinition candidate in AllDefinitions())
        {
            if (candidate.MatchesJuvenileCode(entity.Code))
            {
                definition = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetExternalDefinition(
        AssetLocation code,
        string speciesId,
        out CompanionBreedingDefinition definition)
    {
        foreach (CompanionBreedingDefinition candidate in ExternalDefinitions)
        {
            if (string.Equals(candidate.SpeciesId, speciesId, StringComparison.Ordinal)
                && candidate.MatchesAdultCode(code))
            {
                definition = candidate;
                return true;
            }
        }

        definition = null!;
        return false;
    }

    private static IEnumerable<CompanionBreedingDefinition> AllDefinitions()
    {
        foreach (CompanionBreedingDefinition definition in BySpecies.Values)
        {
            yield return definition;
        }

        foreach (CompanionBreedingDefinition definition in ExternalDefinitions)
        {
            yield return definition;
        }
    }

    public static bool TryGetSpeciesProfile(string speciesId, out CompanionSpeciesProfile profile)
    {
        if (BySpecies.ContainsKey(speciesId)
            && CompanionSpeciesCatalog.TryGetById(speciesId, out profile))
        {
            return true;
        }

        profile = null!;
        return false;
    }

    public static bool TryGetGender(Entity entity, out string genderId)
    {
        genderId = GetVariant(entity, "gender");
        return genderId is "male" or "female";
    }

    public static bool TryGetType(Entity entity, out string typeId)
    {
        typeId = GetVariant(entity, "type");
        return !string.IsNullOrWhiteSpace(typeId);
    }

    public static bool IsKnownType(CompanionSpeciesProfile profile, string typeId)
    {
        foreach (CompanionRecruitmentVariant variant in profile.RecruitmentVariants)
        {
            if (string.Equals(variant.TypeId, typeId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetVariant(Entity entity, string key)
    {
        return entity.Properties?.Variant != null
            && entity.Properties.Variant.TryGetValue(key, out string? value)
                ? value?.Trim().ToLowerInvariant() ?? string.Empty
                : string.Empty;
    }
}
