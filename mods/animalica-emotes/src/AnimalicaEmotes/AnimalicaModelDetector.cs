using System;

namespace AnimalicaEmotes;

internal static class AnimalicaModelDetector
{
    private static readonly string[] ModelTokens =
    {
        "feralfox",
        "feralwolf",
        "spottedhyena",
        "feralhyena",
        "blackbear",
        "brownbear",
        "polarbear",
        "giantpanda",
        "sunbear",
        "feraldeer-caribou",
        "deer-",
        "gazelle-",
        "goat-",
        "pig-",
        "sheep-",
        "feralfish",
        "chicken-",
        "hare-",
        "feralraccoon",
        "pantherinae-"
    };

    public static bool IsAnimalicaModel(string? modelCode)
    {
        if (string.IsNullOrWhiteSpace(modelCode))
        {
            return false;
        }

        foreach (string token in ModelTokens)
        {
            if (modelCode.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    public static AnimalicaModelFamily GetFamily(string? modelCode)
    {
        string code = modelCode ?? string.Empty;
        if (Contains(code, "feralwolf")) return AnimalicaModelFamily.Wolf;
        if (Contains(code, "feralfox")) return AnimalicaModelFamily.Fox;
        if (Contains(code, "spottedhyena") || Contains(code, "feralhyena")) return AnimalicaModelFamily.Hyena;
        if (Contains(code, "bear") || Contains(code, "giantpanda")) return AnimalicaModelFamily.Bear;
        if (Contains(code, "pantherinae-")) return AnimalicaModelFamily.Panther;
        if (Contains(code, "hare-")) return AnimalicaModelFamily.Hare;
        if (Contains(code, "feralraccoon")) return AnimalicaModelFamily.Raccoon;
        if (Contains(code, "chicken-")) return AnimalicaModelFamily.Chicken;
        if (Contains(code, "feralfish")) return AnimalicaModelFamily.Fish;
        if (Contains(code, "deer-") || Contains(code, "feraldeer-caribou")) return AnimalicaModelFamily.Deer;
        if (Contains(code, "goat-")) return AnimalicaModelFamily.Goat;
        if (Contains(code, "pig-")) return AnimalicaModelFamily.Pig;
        if (Contains(code, "sheep-")) return AnimalicaModelFamily.Sheep;
        return AnimalicaModelFamily.Other;
    }

    private static bool Contains(string value, string token)
    {
        return value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

internal enum AnimalicaModelFamily
{
    Other,
    Wolf,
    Fox,
    Hyena,
    Bear,
    Panther,
    Deer,
    Goat,
    Pig,
    Sheep,
    Hare,
    Raccoon,
    Chicken,
    Fish
}
