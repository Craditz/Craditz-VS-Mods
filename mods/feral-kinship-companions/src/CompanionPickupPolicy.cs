using System;

namespace FeralKinshipCompanions;

internal static class CompanionPickupPolicy
{
    // Butchering registers class "butcherable" with this type (v1.14.3).
    // Match its hierarchy without requiring the optional mod's assembly.
    // Do not infer corpses from meat, hide, storage flags, or broad code names.
    internal static bool IsCorpse(Type? collectibleType)
    {
        for (Type? type = collectibleType; type != null; type = type.BaseType)
        {
            if (type.FullName == "Butchering.src.common.item.ItemButcherable") return true;
        }
        return false;
    }
}
