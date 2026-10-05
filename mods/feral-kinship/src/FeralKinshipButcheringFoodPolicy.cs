using System;
using System.Collections.Generic;

namespace FeralKinship;

/// <summary>
/// Bounded optional-Butchering additions to the core PetAI taming diet.
/// The domain gate keeps this inert when Butchering is absent and prevents a
/// similarly named item from another mod from becoming a tame treat.
/// </summary>
internal static class FeralKinshipButcheringFoodPolicy
{
    internal static bool IsAcceptedDynamicTamingTreat(
        string speciesId,
        string domain,
        string path,
        IEnumerable<(string Domain, string Name)> liveTreats)
    {
        if (!TryGetTamingTreat(speciesId, domain, path, out _, out _))
        {
            return false;
        }

        foreach ((string liveDomain, string liveName) in liveTreats)
        {
            if (string.Equals(liveDomain, domain, StringComparison.OrdinalIgnoreCase)
                && string.Equals(liveName, path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool TryGetTamingTreat(
        string speciesId,
        string domain,
        string path,
        out float progress,
        out long cooldownSeconds)
    {
        progress = 0f;
        cooldownSeconds = 0L;

        if (!string.Equals(domain, "butchering", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(speciesId)
            || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        bool pig = string.Equals(speciesId, "pig", StringComparison.OrdinalIgnoreCase);
        bool bloodyOffalCarnivore = string.Equals(speciesId, "bear", StringComparison.OrdinalIgnoreCase)
            || string.Equals(speciesId, "hyena", StringComparison.OrdinalIgnoreCase)
            || string.Equals(speciesId, "wolf", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(path, "offal-clean", StringComparison.OrdinalIgnoreCase))
        {
            if (!pig) return false;
            progress = 0.12f;
            cooldownSeconds = 2L;
            return true;
        }

        if (string.Equals(path, "offal-bloody", StringComparison.OrdinalIgnoreCase))
        {
            if (!pig && !bloodyOffalCarnivore) return false;
            progress = 0.12f;
            cooldownSeconds = 2L;
            return true;
        }

        if (!IsPrimeMeatPath(path) || !AcceptsMeat(speciesId))
        {
            return false;
        }

        progress = string.Equals(speciesId, "cat", StringComparison.OrdinalIgnoreCase)
            ? 0.2f
            : 0.25f;
        cooldownSeconds = 3L;
        return true;
    }

    internal static bool AcceptsMeat(string speciesId)
    {
        return string.Equals(speciesId, "bear", StringComparison.OrdinalIgnoreCase)
            || string.Equals(speciesId, "fox", StringComparison.OrdinalIgnoreCase)
            || string.Equals(speciesId, "hyena", StringComparison.OrdinalIgnoreCase)
            || string.Equals(speciesId, "pig", StringComparison.OrdinalIgnoreCase)
            || string.Equals(speciesId, "raccoon", StringComparison.OrdinalIgnoreCase)
            || string.Equals(speciesId, "wolf", StringComparison.OrdinalIgnoreCase)
            || string.Equals(speciesId, "cat", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrimeMeatPath(string path)
    {
        return path.Equals("primemeat-raw", StringComparison.OrdinalIgnoreCase)
            || path.Equals("primemeat-cooked", StringComparison.OrdinalIgnoreCase)
            || path.Equals("primemeat-curedhealing", StringComparison.OrdinalIgnoreCase)
            || path.Equals("primemeat-healing", StringComparison.OrdinalIgnoreCase)
            || path.Equals("smoked-none-primemeat", StringComparison.OrdinalIgnoreCase)
            || path.Equals("smoked-healing-primemeat", StringComparison.OrdinalIgnoreCase);
    }
}
