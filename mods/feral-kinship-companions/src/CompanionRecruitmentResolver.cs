using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;

internal static class CompanionRecruitmentResolver
{
    internal static EntityProperties? Resolve(
        CompanionSpeciesProfile profile,
        CompanionRecruitmentVariant variant,
        System.Func<AssetLocation, EntityProperties?> getEntityType)
    {
        if (CompanionNativeRecruitmentCatalog.IsNative(profile))
            return CompanionNativeRecruitmentCatalog.Resolve(profile, variant, getEntityType);
        if (!CompanionNativeRecruitmentCatalog.Concrete(variant.EntityCode)) return null;
        EntityProperties? source = getEntityType(new AssetLocation(variant.EntityCode));
        if (source == null || CompanionNativeRecruitmentCatalog.IsJuvenile(source)) return null;

        // Registered behavior JSON already has the engine's variant placeholders
        // resolved. Use the same destination as PetAI's ordinary tame conversion.
        string? tameCode = source.Server?.BehaviorsAsJsonObj?
            .FirstOrDefault(behavior => string.Equals(
                behavior["code"].AsString(), "tameable", StringComparison.OrdinalIgnoreCase))?
            ["tameEntityCode"].AsString();
        EntityProperties? tame = string.IsNullOrWhiteSpace(tameCode)
            ? source // Cats and dogs can become domesticated in place.
            : getEntityType(AssetLocation.Create(tameCode));

        // Ownership attributes alone must never turn a wild definition into a
        // reward. Also reject a misconfigured destination belonging to another species.
        return tame != null && profile.MatchesTameEntityCode(tame.Code)
            && !CompanionNativeRecruitmentCatalog.IsJuvenile(tame)
            && CompanionNativeRecruitmentCatalog.Concrete(tame.Code.ToString()) ? tame : null;
    }
}
