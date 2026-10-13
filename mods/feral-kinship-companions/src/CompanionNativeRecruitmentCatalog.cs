#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;

// Server runtime definitions are the authority. No provider dependency, guessed
// wild replacement, or global fallback from one native family to another.
internal static class CompanionNativeRecruitmentCatalog
{
    private static Dictionary<string, CompanionRecruitmentVariant> bySource = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, CompanionRecruitmentVariant> byTame = new(StringComparer.OrdinalIgnoreCase);

    internal static bool IsNative(CompanionSpeciesProfile profile) =>
        profile.Id is "tamables-fotsa" or "tamables-critters";

    internal static string BehaviorCode(CompanionSpeciesProfile profile) =>
        profile.Id == "tamables-fotsa" ? "tamablesfotsatameable" : "tamablescritterstameable";

    internal static bool Concrete(string? code) => !string.IsNullOrWhiteSpace(code)
        && code.Contains(':') && code.IndexOfAny(new[] { '{', '}', '*', '?' }) < 0;

    internal static bool IsAdult(EntityProperties type, bool requireAge = false)
    {
        if (type.Server?.BehaviorsAsJsonObj == null) return false;
        if (IsJuvenile(type)) return false;
        if (type.Variant?.TryGetValue("age", out string? age) == true)
            return string.Equals(age, "adult", StringComparison.OrdinalIgnoreCase);
        if (requireAge) return false;
        // Age-less mature Critters have no growth definition. A disabled growth
        // entry is accepted only with explicit adult age metadata.
        if (type.Server.BehaviorsAsJsonObj.Any(b => b["code"].AsString() is "grow" or "tamablescrittersgrow")) return false;
        return true;
    }

    internal static bool IsJuvenile(EntityProperties type)
    {
        if (type.Server?.BehaviorsAsJsonObj?.Any(b =>
            (b["code"].AsString() is "grow" or "tamablescrittersgrow") && b["enabled"].AsBool(true)) == true) return true;
        if (type.Variant?.TryGetValue("age", out string? age) == true && !string.Equals(age, "adult", StringComparison.OrdinalIgnoreCase)) return true;
        string[] stages = { "baby", "juvenile", "chick", "calf", "poult", "young", "egg", "larva", "tadpole", "kitten", "puppy" };
        return type.Code.Path.ToLowerInvariant().Split('-', '_').Any(stages.Contains);
    }

    internal static string Family(EntityProperties source) => source.Code.Domain + ":" +
        string.Join("-", source.Code.Path.ToLowerInvariant().Split('-').Where(token =>
            token is not ("adult" or "male" or "female" or "tamed" or "semitamed")));

    internal static EntityProperties? Resolve(CompanionSpeciesProfile profile, CompanionRecruitmentVariant variant,
        System.Func<AssetLocation, EntityProperties?> getType)
    {
        if (!IsNative(profile) || !Concrete(variant.EntityCode)) return null;
        EntityProperties? source = getType(new AssetLocation(variant.EntityCode));
        if (source == null || !IsAdult(source, profile.Id == "tamables-fotsa")) return null;
        var mappings = source.Server!.BehaviorsAsJsonObj!.Where(b =>
            b["code"].AsString() == BehaviorCode(profile)).ToArray();
        if (mappings.Length != 1 || !mappings[0]["enabled"].AsBool(true)) return null;
        string? destination = mappings[0]["tameEntityCode"].AsString();
        string expected = profile.TameCodeDomain + ":tame-" + source.Code.Domain + "-" + source.Code.Path;
        if (!Concrete(destination) || !string.Equals(destination, expected, StringComparison.OrdinalIgnoreCase)
            || (variant.TameEntityCode != null && !string.Equals(destination, variant.TameEntityCode, StringComparison.OrdinalIgnoreCase))) return null;
        EntityProperties? tame = getType(new AssetLocation(destination!));
        if (tame == null || !profile.MatchesTameEntityCode(tame.Code)
            || !IsAdult(tame, profile.Id == "tamables-fotsa") || tame.Habitat != source.Habitat) return null;
        string prefix = profile.TameCodeDomain;
        var behaviors = tame.Server!.BehaviorsAsJsonObj!;
        if (!behaviors.Any(b => b["code"].AsString() == prefix + "tameguard" && b["enabled"].AsBool(true))
            || !behaviors.Any(b => b["code"].AsString() == prefix + "tameable" && b["enabled"].AsBool(true))
            || !behaviors.Any(b => b["code"].AsString() == prefix + "sourcename"
                && b["enabled"].AsBool(true)
                && string.Equals(b["sourceEntityCode"].AsString(), source.Code.ToString(), StringComparison.OrdinalIgnoreCase))) return null;
        return tame;
    }

    internal static void Initialize(IEnumerable<EntityProperties> types)
    {
        var registered = types.Where(t => t.Code != null).GroupBy(t => t.Code.ToString(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single(), StringComparer.OrdinalIgnoreCase);
        EntityProperties? Get(AssetLocation code) => registered.GetValueOrDefault(code.ToString());
        var sources = new Dictionary<string, CompanionRecruitmentVariant>(StringComparer.OrdinalIgnoreCase);
        var tames = new Dictionary<string, CompanionRecruitmentVariant>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in new[] { CompanionSpeciesCatalog.TamablesFotsa, CompanionSpeciesCatalog.TamablesCritters })
        foreach (EntityProperties source in registered.Values.OrderBy(t => t.Code.ToString(), StringComparer.Ordinal))
        {
            var candidate = new CompanionRecruitmentVariant(source.Code.ToString(), source.Code.ToString(),
                source.Code.Path.Replace('-', ' '), familyId: profile.Id + ":" + Family(source));
            EntityProperties? tame = Resolve(profile, candidate, Get);
            if (tame == null) continue;
            var variant = new CompanionRecruitmentVariant(candidate.Id, candidate.EntityCode, candidate.DisplayName,
                tameEntityCode: tame.Code.ToString(), familyId: candidate.FamilyId);
            sources[variant.EntityCode] = variant;
            tames[tame.Code.ToString()] = variant;
        }
        bySource = sources;
        byTame = tames;
    }

    internal static bool TryGet(string? sourceCode, out CompanionSpeciesProfile profile, out CompanionRecruitmentVariant variant)
    {
        profile = null!; variant = null!;
        if (sourceCode == null || !bySource.TryGetValue(sourceCode, out variant!)) return false;
        return CompanionSpeciesCatalog.TryGetByTameEntityCode(variant.TameEntityCode, out profile);
    }

    internal static IReadOnlyList<CompanionRecruitmentVariant> ForMember(CompanionSpeciesProfile profile,
        string? tameCode, System.Func<AssetLocation, EntityProperties?> getType)
    {
        if (!IsNative(profile)) return profile.RecruitmentVariants;
        if (tameCode == null || !byTame.TryGetValue(tameCode, out var member)
            || Resolve(profile, member, getType) == null) return Array.Empty<CompanionRecruitmentVariant>();
        return bySource.Values.Where(v => v.FamilyId == member.FamilyId && Resolve(profile, v, getType) != null).ToArray();
    }
}
