#nullable enable

using System;

namespace FeralKinshipCompanions;

/// <summary>
/// Soft compatibility for the optional Butchering food contracts that are not
/// represented by the generic tame food tags. The domain/path gate keeps this
/// inert when Butchering is absent and avoids treating similarly named items
/// from another mod as compatible food.
/// </summary>
internal static class CompanionButcheringFoodPolicy
{
    internal static bool Matches(string speciesId, string domain, string path, bool acceptsMeat)
    {
        if (!string.Equals(domain, "butchering", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(speciesId)
            || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        bool pig = string.Equals(speciesId, "pig", StringComparison.OrdinalIgnoreCase);
        bool offalCarnivore = string.Equals(speciesId, "bear", StringComparison.OrdinalIgnoreCase)
            || string.Equals(speciesId, "hyena", StringComparison.OrdinalIgnoreCase)
            || string.Equals(speciesId, "wolf", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(path, "offal-clean", StringComparison.OrdinalIgnoreCase)) return pig;
        if (string.Equals(path, "offal-bloody", StringComparison.OrdinalIgnoreCase)) return pig || offalCarnivore;

        // Butchering's raw prime meat is meal-only in its released item JSON,
        // but it is a reported Companion food. Keep this explicit and scoped
        // to the existing Companion meat/Protein diet rather than borrowing
        // Butchering's narrower offal-attraction list.
        if (path.Equals("primemeat-raw", StringComparison.OrdinalIgnoreCase)
            || path.Equals("primemeat-cooked", StringComparison.OrdinalIgnoreCase)
            || path.Equals("primemeat-curedhealing", StringComparison.OrdinalIgnoreCase)
            || path.Equals("primemeat-healing", StringComparison.OrdinalIgnoreCase)
            || path.Equals("smoked-none-primemeat", StringComparison.OrdinalIgnoreCase)
            || path.Equals("smoked-healing-primemeat", StringComparison.OrdinalIgnoreCase))
        {
            return acceptsMeat;
        }

        return false;
    }
}
