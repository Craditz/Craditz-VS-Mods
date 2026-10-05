using System;
using Vintagestory.API.Common;

namespace FeralKinship
{
public static class FeralRaceResolver
{
    private const string ExplicitRaceAttribute = "feralKinshipRace";
    private const string PlayerModelLibModelAttribute = "skinModel";

    private readonly struct ModelRule
    {
        public ModelRule(string model, string race, bool allowJoinedSuffix = false)
        {
            Model = model;
            Race = race;
            AllowJoinedSuffix = allowJoinedSuffix;
        }

        public string Model { get; }
        public string Race { get; }
        public bool AllowJoinedSuffix { get; }
    }

    // Joined suffixes are limited to the Animalica models that deliberately
    // use codes such as feralfoxcarry. Other aliases require an exact match or
    // a normal model-code separator, avoiding accidental matches like
    // "foxtrot" or "goatherd".
    private static readonly ModelRule[] ModelRules =
    {
        // Anthro Players (furry) compatibility aliases.
        new("bear", "bear"),
        new("blackbear", "bear"),
        new("brownbear", "bear"),
        new("polarbear", "bear"),
        new("giantpanda", "bear"),
        new("sunbear", "bear"),
        new("bird", "chicken"),
        new("chicken", "chicken"),
        // Promoted Animalica Cats compatibility models use these exact keys.
        new("cats-adult-cat", "cat"),
        new("cats-kitten", "cat"),
        // Galeret 0.1.3 registers these four PlayerModelLib model keys;
        // all are feline variants and therefore share the cat kin race.
        new("cathay", "cat"),
        new("galeret", "cat"),
        new("suthay", "cat"),
        new("suthayrath", "cat"),
        new("feraldeer", "deer"),
        new("deer", "deer"),
        new("deer_antler", "deer"),
        new("feralfish", "fish"),
        new("feralfox", "fox", allowJoinedSuffix: true),
        new("fox", "fox"),
        new("foxxo", "fox"),
        new("gazelle", "gazelle"),
        new("goat", "goat"),
        new("hare", "hare"),
        new("rabbit", "hare"),
        new("hyena", "hyena"),
        new("spottedhyena", "hyena"),
        new("pig", "pig"),
        new("feralraccoon", "raccoon", allowJoinedSuffix: true),
        new("raccoon", "raccoon"),
        new("raccoonplayermodel", "raccoon"),
        new("sheep", "sheep"),
        new("wolf", "wolf"),
        new("feralwolf", "wolf", allowJoinedSuffix: true),
        new("lupine", "wolf"),
        new("wolp", "wolf")
    };

    // Optional FotSA feline clades use their own entity-code roots instead of `cat-*`.
    // Their absence is harmless: no entity with these roots exists without the source mod.
    private static readonly string[] OptionalCatAnimalPrefixes =
    {
        "felinae",
        "pantherinae-panthera",
        "pantherinae-neofelis",
        "machairodontinae"
    };

    private static readonly string[] AnimalRacePrefixes =
    {
        "bear",
        "cat",
        "chicken",
        "deer",
        "fish",
        "fox",
        "gazelle",
        "goat",
        "hare",
        "hyena",
        "pig",
        "raccoon",
        "sheep",
        "wolf"
    };

    public static string? GetRace(EntityPlayer player)
    {
        string explicitRace = player.WatchedAttributes.GetString(ExplicitRaceAttribute, string.Empty);
        if (!string.IsNullOrWhiteSpace(explicitRace))
        {
            return explicitRace.Trim().ToLowerInvariant();
        }

        string modelCode = player.WatchedAttributes.GetString(PlayerModelLibModelAttribute, string.Empty);
        int domainSeparator = modelCode.IndexOf(':');
        if (domainSeparator >= 0)
        {
            modelCode = modelCode.Substring(domainSeparator + 1);
        }

        modelCode = modelCode.Trim();
        foreach (ModelRule rule in ModelRules)
        {
            if (MatchesModelRule(modelCode, rule))
            {
                return rule.Race;
            }
        }

        return null;
    }

    private static bool MatchesModelRule(string modelCode, ModelRule rule)
    {
        if (modelCode.Equals(rule.Model, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!modelCode.StartsWith(rule.Model, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (rule.AllowJoinedSuffix)
        {
            return true;
        }

        char separator = modelCode[rule.Model.Length];
        return separator == '-' || separator == '_' || separator == '.' || separator == '/';
    }

    public static string? GetAnimalRace(EntityAgent animal)
    {
        string entityCode = animal.Code == null ? string.Empty : animal.Code.Path;
        foreach (string prefix in OptionalCatAnimalPrefixes)
        {
            if (entityCode.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                entityCode.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase))
            {
                return "cat";
            }
        }

        foreach (string race in AnimalRacePrefixes)
        {
            if (entityCode.Equals(race, StringComparison.OrdinalIgnoreCase) ||
                entityCode.StartsWith(race + "-", StringComparison.OrdinalIgnoreCase))
            {
                return race;
            }
        }

        return null;
    }
}
}
