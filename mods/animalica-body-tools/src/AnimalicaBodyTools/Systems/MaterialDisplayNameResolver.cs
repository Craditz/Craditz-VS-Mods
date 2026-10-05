using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace AnimalicaBodyTools.Systems;

/// <summary>
/// Resolves player-facing names for provider-specific Body Tools materials.
/// Language JSON is loaded before optional provider state is reliable, so the
/// conflict qualifier is applied at the same consumer used by held items,
/// equipped tooltips, and handbook entries.
/// </summary>
internal static class MaterialDisplayNameResolver
{
    private static readonly IReadOnlyDictionary<string, string> MaterialNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["titanium"] = "Titanium",
            ["obdoltoolstitanium"] = "Titanium",
            ["metalsofmythstitanium"] = "Titanium",
            ["aluminumbronze"] = "Aluminum Bronze",
            ["realloyaluminumbronze"] = "Aluminum Bronze"
        };

    private static readonly IReadOnlyDictionary<string, string> FamilyNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["clawtips"] = "Claw Tips",
            ["cultivatingclaws"] = "Cultivating Claws",
            ["fangcaps"] = "Fang Caps",
            ["harvestingclaws"] = "Harvesting Claws",
            ["prospectingclaws"] = "Prospecting Claws",
            ["rendingclaws"] = "Rending Claws",
            ["tunnelingclaws"] = "Tunneling Claws"
        };

    private static ICoreAPI? api;

    internal static void Configure(ICoreAPI coreApi) => api = coreApi;

    internal static void Reset() => api = null;

    internal static string? Resolve(string? code, string? fallback)
    {
        if (string.IsNullOrWhiteSpace(code)) return fallback;
        string shortCode = code!;
        int namespaceSeparator = shortCode.IndexOf(':');
        if (namespaceSeparator >= 0) shortCode = shortCode[(namespaceSeparator + 1)..];
        bool wearable = shortCode.EndsWith("-wearable", StringComparison.Ordinal);
        if (wearable)
        {
            shortCode = shortCode[..^"-wearable".Length];
        }

        int separator = shortCode.IndexOf('-');
        if (separator <= 0 || separator == shortCode.Length - 1) return fallback;
        string family = shortCode[..separator];
        string material = shortCode[(separator + 1)..];
        if (!FamilyNames.TryGetValue(family, out string? familyName)
            || !MaterialNames.TryGetValue(material, out string? materialName))
        {
            return fallback;
        }

        string display = $"{materialName} {familyName}";
        string? qualifier = HasProviderCounterpart(family, material, wearable)
            ? GetConflictQualifier(material)
            : null;
        return qualifier == null ? display : $"{display} ({qualifier})";
    }

    private static bool HasProviderCounterpart(string family, string material, bool wearable)
    {
        if (api?.World == null) return false;

        string suffix = wearable ? "-wearable" : string.Empty;
        foreach (string candidate in material switch
        {
            "titanium" => ["obdoltoolstitanium", "metalsofmythstitanium"],
            "obdoltoolstitanium" => ["titanium", "metalsofmythstitanium"],
            "metalsofmythstitanium" => ["titanium", "obdoltoolstitanium"],
            "aluminumbronze" => ["realloyaluminumbronze"],
            "realloyaluminumbronze" => ["aluminumbronze"],
            _ => Array.Empty<string>()
        })
        {
            AssetLocation candidateCode = new($"animalicabodytools:{family}-{candidate}{suffix}");
            if (api.World.GetItem(candidateCode) != null) return true;
        }

        return false;
    }

    private static string? GetConflictQualifier(string material)
    {
        if (api?.ModLoader == null) return null;

        return material switch
        {
            "titanium" when IsEnabled("obdoltools") || IsEnabled("metalsofmyths") => "TitaniumTools",
            "obdoltoolstitanium" when IsEnabled("titaniumtools") || IsEnabled("metalsofmyths") => "More Vanilla Tools",
            "metalsofmythstitanium" when IsEnabled("titaniumtools") || IsEnabled("obdoltools") => "Metals of Myths",
            "aluminumbronze" when IsEnabled("realloy") => "Silly Aluminum",
            "realloyaluminumbronze" when IsEnabled("sillyaluminum") => "Re-Alloy",
            _ => null
        };
    }

    private static bool IsEnabled(string modid) => api?.ModLoader.IsModEnabled(modid) == true;
}
