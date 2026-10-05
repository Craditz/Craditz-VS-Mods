#nullable enable

using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace FeralKinshipCompanions;

[Flags]
internal enum CompanionCapabilities
{
    None = 0,
    Social = 1 << 0,
    Mood = 1 << 1,
    Requests = 1 << 2,
    Talents = 1 << 3,
    Dens = 1 << 4,
    PackStructures = 1 << 5,
    Expeditions = 1 << 6,
    Recruitment = 1 << 7,
    Cargo = 1 << 8,
    PetCommands = 1 << 9,
    Combat = 1 << 10,
    AwayState = 1 << 11,
    Foxfire = 1 << 12,

    InitialFeatureSet = Social | Mood | Requests | Talents | Dens | PackStructures
        | Expeditions | Recruitment | Cargo | PetCommands | Combat | AwayState | Foxfire
}

internal sealed class CompanionRecruitmentVariant
{
    public CompanionRecruitmentVariant(
        string id,
        string entityCode,
        string displayName,
        string? typeId = null,
        string? genderId = null,
        float weight = 1f)
    {
        Id = id;
        EntityCode = entityCode;
        DisplayName = displayName;
        TypeId = typeId;
        GenderId = genderId;
        Weight = weight;
    }

    public string Id { get; }
    public string EntityCode { get; }
    public string DisplayName { get; }
    public string? TypeId { get; }
    public string? GenderId { get; }
    public float Weight { get; }
}

internal sealed class CompanionSpeciesProfile
{
    private readonly HashSet<string> knownAnimationCodes;
    private readonly HashSet<string> knownSoundKeys;
    private readonly HashSet<string> tameCodePathAliases;
    private readonly HashSet<string> additionalTameEntityCodes;

    public CompanionSpeciesProfile(
        string id,
        string displayName,
        string tameCodePathBase,
        string wildCodeTemplate,
        IReadOnlyList<CompanionRecruitmentVariant> recruitmentVariants,
        float bodyWidth,
        float bodyHeight,
        float baseMeleeDamage,
        string idleAnimationCode,
        string? sitAnimationCode,
        string? sleepAnimationCode,
        string? lieAnimationCode,
        string? attackAnimationCode,
        string? walkAnimationCode,
        string? runAnimationCode,
        IEnumerable<string> knownAnimations,
        IEnumerable<string>? knownSounds = null,
        string? fallbackSoundKey = null,
        float recruitmentWeight = 1f,
        CompanionCapabilities capabilities = CompanionCapabilities.InitialFeatureSet,
        string tameCodeDomain = "feralkinship",
        IEnumerable<string>? tameCodePathAliases = null,
        IEnumerable<string>? additionalTameEntityCodes = null,
        bool usesExternalPetAiTaskSet = false)
    {
        Id = id;
        DisplayName = displayName;
        TameCodePathBase = tameCodePathBase;
        WildCodeTemplate = wildCodeTemplate;
        RecruitmentVariants = recruitmentVariants;
        BodyWidth = bodyWidth;
        BodyHeight = bodyHeight;
        BaseMeleeDamage = baseMeleeDamage;
        IdleAnimationCode = idleAnimationCode;
        SitAnimationCode = sitAnimationCode;
        // Core Kinship supplies these stable, lowercase semantic aliases.
        SleepAnimationCode = sleepAnimationCode ?? "sleep";
        LieAnimationCode = lieAnimationCode ?? "lie";
        AttackAnimationCode = attackAnimationCode ?? "attack";
        WalkAnimationCode = walkAnimationCode ?? "walk";
        RunAnimationCode = runAnimationCode ?? "run";
        FallbackAnimationCode = idleAnimationCode;
        FallbackSoundKey = fallbackSoundKey ?? "idle";
        RecruitmentWeight = recruitmentWeight;
        Capabilities = capabilities;
        knownAnimationCodes = new HashSet<string>(knownAnimations, StringComparer.OrdinalIgnoreCase);
        knownAnimationCodes.Add(SleepAnimationCode);
        knownAnimationCodes.Add(LieAnimationCode);
        knownAnimationCodes.Add(AttackAnimationCode);
        knownAnimationCodes.Add(WalkAnimationCode);
        knownAnimationCodes.Add(RunAnimationCode);
        knownSoundKeys = new HashSet<string>(knownSounds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        knownSoundKeys.Add("hurt");
        knownSoundKeys.Add("death");
        knownSoundKeys.Add("idle");
        TameCodeDomain = tameCodeDomain;
        this.tameCodePathAliases = new HashSet<string>(tameCodePathAliases ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        this.additionalTameEntityCodes = new HashSet<string>(additionalTameEntityCodes ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        UsesExternalPetAiTaskSet = usesExternalPetAiTaskSet;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string TameCodeDomain { get; }
    public string TameCodePathBase { get; }
    public string TameCodePattern => $"{TameCodeDomain}:{TameCodePathBase}-*";
    public bool UsesExternalPetAiTaskSet { get; }
    public string WildCodeTemplate { get; }
    public IReadOnlyList<CompanionRecruitmentVariant> RecruitmentVariants { get; }
    public float RecruitmentWeight { get; }
    public CompanionCapabilities Capabilities { get; }

    // These dimensions mirror the core tame entity hitboxes. Pair spacing can
    // combine the two radii instead of assuming every companion is fox-sized.
    public float BodyWidth { get; }
    public float BodyHeight { get; }
    public float BaseMeleeDamage { get; }
    public float PersonalSpaceRadius => BodyWidth * 0.5f + 0.15f;
    public float SuggestedArrivalRadius => Math.Max(0.45f, BodyWidth * 0.45f);

    // Animation fields name either core declarations or stable aliases added
    // by the companion overlay. A null request resolves to the safe fallback.
    public string IdleAnimationCode { get; }
    public string? SitAnimationCode { get; }
    public string? SleepAnimationCode { get; }
    public string? LieAnimationCode { get; }
    public string? AttackAnimationCode { get; }
    public string? WalkAnimationCode { get; }
    public string? RunAnimationCode { get; }
    public string FallbackAnimationCode { get; }
    public string? FallbackSoundKey { get; }

    public bool HasCapability(CompanionCapabilities capability)
    {
        return (Capabilities & capability) == capability;
    }

    public bool MatchesTameEntityCode(AssetLocation? code)
    {
        return code != null
            && (additionalTameEntityCodes.Contains(code.ToString())
                || (string.Equals(code.Domain, TameCodeDomain, StringComparison.OrdinalIgnoreCase)
                && (string.Equals(code.Path, TameCodePathBase, StringComparison.OrdinalIgnoreCase)
                || code.Path.StartsWith(TameCodePathBase + "-", StringComparison.OrdinalIgnoreCase)
                || tameCodePathAliases.Contains(code.Path))));
    }

    public bool KnowsAnimation(string? animationCode)
    {
        return !string.IsNullOrWhiteSpace(animationCode) && knownAnimationCodes.Contains(animationCode);
    }

    public string ResolveAnimationOrFallback(string? animationCode)
    {
        return KnowsAnimation(animationCode)
            ? animationCode!.ToLowerInvariant()
            : FallbackAnimationCode.ToLowerInvariant();
    }

    public bool KnowsSound(string? soundKey)
    {
        return !string.IsNullOrWhiteSpace(soundKey) && knownSoundKeys.Contains(soundKey);
    }

    public string? ResolveSoundOrSilence(string? soundKey)
    {
        return KnowsSound(soundKey) ? soundKey : FallbackSoundKey;
    }
}

internal static class CompanionSpeciesCatalog
{
    private static readonly string[] GenderStates = { "male", "female" };

    public static readonly CompanionSpeciesProfile Bear = new(
        id: "bear",
        displayName: "Bear",
        tameCodePathBase: "tamebear",
        wildCodeTemplate: "game:bear-{type}-adult-{gender}",
        recruitmentVariants: GenderedAdultVariants("bear", new[] { "black", "brown", "sun", "panda", "polar" }),
        bodyWidth: 1.30f,
        bodyHeight: 1.20f,
        baseMeleeDamage: 10f,
        idleAnimationCode: "idle",
        sitAnimationCode: "sit",
        sleepAnimationCode: "sleep",
        lieAnimationCode: null,
        attackAnimationCode: null,
        walkAnimationCode: null,
        runAnimationCode: null,
        knownAnimations: new[] { "hurt", "die", "idle", "sleep", "sit" },
        knownSounds: new[] { "hurt", "death", "idle" },
        fallbackSoundKey: "idle");

    public static readonly CompanionSpeciesProfile Cat = new(
        id: "cat",
        displayName: "Cat",
        tameCodePathBase: "cat-male-house",
        wildCodeTemplate: "cats:cat-{gender}-{type}",
        recruitmentVariants: ExternalGenderedVariants("cat", "cats", "cat-{gender}-{type}", new[]
        {
            "house", "ocelot", "serval", "european"
        }),
        bodyWidth: 0.80f,
        bodyHeight: 0.55f,
        baseMeleeDamage: 3f,
        idleAnimationCode: "idle",
        sitAnimationCode: "sit",
        sleepAnimationCode: "sleep",
        lieAnimationCode: "lie",
        attackAnimationCode: "attack",
        walkAnimationCode: "walk",
        runAnimationCode: "run",
        knownAnimations: new[] { "hurt", "die", "dead", "idle", "sleep", "wounded-idle", "sit", "lie", "attack", "walk", "run", "eat" },
        knownSounds: new[] { "hurt", "death", "idle" },
        fallbackSoundKey: "idle",
        tameCodeDomain: "cats",
        tameCodePathAliases: AdultCatTamePaths());

    public static readonly CompanionSpeciesProfile Chicken = new(
        id: "chicken",
        displayName: "Chicken",
        tameCodePathBase: "tamechicken",
        wildCodeTemplate: "game:chicken-{age}",
        recruitmentVariants: new[]
        {
            new CompanionRecruitmentVariant("hen", "game:chicken-hen", "Hen", genderId: "female"),
            new CompanionRecruitmentVariant("rooster", "game:chicken-rooster", "Rooster", genderId: "male")
        },
        bodyWidth: 0.50f,
        bodyHeight: 0.60f,
        baseMeleeDamage: 0.5f,
        idleAnimationCode: "idle",
        sitAnimationCode: "sit",
        sleepAnimationCode: "sleep",
        lieAnimationCode: null,
        attackAnimationCode: null,
        walkAnimationCode: null,
        runAnimationCode: null,
        knownAnimations: new[] { "hurt", "die", "idle", "sleep", "sit" },
        knownSounds: new[] { "hurt", "death", "idle" },
        fallbackSoundKey: "idle");

    public static readonly CompanionSpeciesProfile Deer = new(
        id: "deer",
        displayName: "Deer",
        tameCodePathBase: "tamedeer",
        wildCodeTemplate: "game:deer-{type}-adult-{gender}",
        recruitmentVariants: GenderedAdultVariants("deer", new[]
        {
            "whitetail", "redbrocket", "marsh", "caribou", "water", "pudu",
            "elk", "moose", "taruca", "chital", "guemal", "pampas", "fallow"
        }),
        bodyWidth: 1.00f,
        bodyHeight: 1.20f,
        baseMeleeDamage: 3f,
        idleAnimationCode: "idle",
        sitAnimationCode: "sit",
        sleepAnimationCode: "sleep",
        lieAnimationCode: null,
        attackAnimationCode: null,
        walkAnimationCode: null,
        runAnimationCode: null,
        knownAnimations: new[] { "hurt", "die", "idle", "sleep", "sit" });

    public static readonly CompanionSpeciesProfile Dog = new(
        id: "dog",
        displayName: "Dog",
        tameCodePathBase: "dog-wolf-male",
        wildCodeTemplate: "wolftaming:dog-{type}-{gender}",
        recruitmentVariants: ExternalGenderedVariants("dog", "wolftaming", "dog-{type}-{gender}", new[]
        {
            "wolf", "hunting", "shepherd", "corgi", "peach"
        }),
        bodyWidth: 1.10f,
        bodyHeight: 0.90f,
        baseMeleeDamage: 6f,
        idleAnimationCode: "idle",
        sitAnimationCode: "sit",
        sleepAnimationCode: "sleep",
        lieAnimationCode: "lie",
        attackAnimationCode: "attack",
        walkAnimationCode: "walk",
        runAnimationCode: "run",
        knownAnimations: new[] { "hurt", "die", "dead", "idle", "sleep", "wounded-idle", "sit", "lie", "attack", "walk", "run", "eat" },
        knownSounds: new[] { "hurt", "death", "idle" },
        fallbackSoundKey: "idle",
        tameCodeDomain: "wolftaming",
        tameCodePathAliases: AdultDogTamePaths());

    public static readonly CompanionSpeciesProfile Fox = new(
        id: "fox",
        displayName: "Fox",
        tameCodePathBase: "tamefox",
        wildCodeTemplate: "game:fox-{type}-adult-{gender}",
        recruitmentVariants: GenderedAdultVariants("fox", new[] { "red", "arctic" }),
        bodyWidth: 0.75f,
        bodyHeight: 0.50f,
        baseMeleeDamage: 4f,
        idleAnimationCode: "idle",
        sitAnimationCode: "sit",
        sleepAnimationCode: "sleep",
        lieAnimationCode: "lie",
        attackAnimationCode: "attack",
        walkAnimationCode: null,
        runAnimationCode: null,
        knownAnimations: new[] { "hurt", "die", "idle", "sleep", "sit", "lie", "attack", "sniff" },
        knownSounds: new[] { "hurt", "death", "idle" },
        fallbackSoundKey: "idle",
        additionalTameEntityCodes: AdultFoxTamingEntityCodes());

    public static readonly CompanionSpeciesProfile Gazelle = new(
        id: "gazelle",
        displayName: "Gazelle",
        tameCodePathBase: "tamegazelle",
        wildCodeTemplate: "game:gazelle-{type}-adult-{gender}",
        recruitmentVariants: GenderedAdultVariants("gazelle", new[] { "thomson" }),
        bodyWidth: 1.30f,
        bodyHeight: 1.40f,
        baseMeleeDamage: 2f,
        idleAnimationCode: "idle",
        sitAnimationCode: "sit",
        sleepAnimationCode: "sleep",
        lieAnimationCode: null,
        attackAnimationCode: null,
        walkAnimationCode: null,
        runAnimationCode: null,
        knownAnimations: new[] { "hurt", "die", "idle", "sleep", "sit" });

    public static readonly CompanionSpeciesProfile Goat = new(
        id: "goat",
        displayName: "Goat",
        tameCodePathBase: "tamegoat",
        wildCodeTemplate: "game:goat-{type}-adult-{gender}",
        recruitmentVariants: GenderedAdultVariants("goat", new[]
        {
            "angora", "ibexalp", "ibexnub", "markhor", "mountain", "muskox",
            "nubian", "sirohi", "takingold", "turdag", "valais"
        }),
        bodyWidth: 0.90f,
        bodyHeight: 0.90f,
        baseMeleeDamage: 3f,
        idleAnimationCode: "idle",
        sitAnimationCode: "sit",
        sleepAnimationCode: "sleep",
        lieAnimationCode: null,
        attackAnimationCode: null,
        walkAnimationCode: null,
        runAnimationCode: null,
        knownAnimations: new[] { "hurt", "die", "idle", "sleep", "sit" });

    public static readonly CompanionSpeciesProfile Hare = new(
        id: "hare",
        displayName: "Hare",
        tameCodePathBase: "tamehare",
        wildCodeTemplate: "game:hare-{type}-adult-{gender}",
        recruitmentVariants: GenderedAdultVariants("hare", new[] { "arctic", "cape", "european", "indian", "jackblack", "scrub" }),
        bodyWidth: 0.75f,
        bodyHeight: 0.50f,
        baseMeleeDamage: 1f,
        idleAnimationCode: "idle",
        sitAnimationCode: "sit",
        sleepAnimationCode: "sleep",
        lieAnimationCode: null,
        attackAnimationCode: null,
        walkAnimationCode: null,
        runAnimationCode: null,
        knownAnimations: new[] { "hurt", "die", "idle", "sleep", "sit" });

    public static readonly CompanionSpeciesProfile Hyena = new(
        id: "hyena",
        displayName: "Hyena",
        tameCodePathBase: "tamehyena",
        wildCodeTemplate: "game:hyena-{type}-adult-{gender}",
        recruitmentVariants: GenderedAdultVariants("hyena", new[] { "spotted" }),
        bodyWidth: 1.20f,
        bodyHeight: 1.00f,
        baseMeleeDamage: 6f,
        idleAnimationCode: "idle",
        sitAnimationCode: "sit",
        sleepAnimationCode: "sleep",
        lieAnimationCode: null,
        attackAnimationCode: null,
        walkAnimationCode: null,
        runAnimationCode: null,
        knownAnimations: new[] { "hurt", "die", "idle", "sleep", "sit" });

    public static readonly CompanionSpeciesProfile Pig = new(
        id: "pig",
        displayName: "Pig",
        tameCodePathBase: "tamepig",
        wildCodeTemplate: "game:pig-{type}-adult-{gender}",
        recruitmentVariants: GenderedAdultVariants("pig", new[] { "eurasian", "redriver", "warthog" }),
        bodyWidth: 1.10f,
        bodyHeight: 0.90f,
        baseMeleeDamage: 5f,
        idleAnimationCode: "idle",
        sitAnimationCode: "sit",
        sleepAnimationCode: "sleep",
        lieAnimationCode: null,
        attackAnimationCode: null,
        walkAnimationCode: null,
        runAnimationCode: null,
        knownAnimations: new[] { "hurt", "die", "idle", "sleep", "sit" });

    public static readonly CompanionSpeciesProfile Raccoon = new(
        id: "raccoon",
        displayName: "Raccoon",
        tameCodePathBase: "tameraccoon",
        wildCodeTemplate: "game:raccoon-{type}-adult-{gender}",
        recruitmentVariants: GenderedAdultVariants("raccoon", new[] { "common" }),
        bodyWidth: 0.75f,
        bodyHeight: 0.50f,
        baseMeleeDamage: 3f,
        idleAnimationCode: "idle",
        sitAnimationCode: "sit",
        sleepAnimationCode: null,
        lieAnimationCode: null,
        attackAnimationCode: null,
        walkAnimationCode: null,
        runAnimationCode: null,
        knownAnimations: new[] { "hurt", "die", "idle", "sit" });

    public static readonly CompanionSpeciesProfile Sheep = new(
        id: "sheep",
        displayName: "Sheep",
        tameCodePathBase: "tamesheep",
        wildCodeTemplate: "game:sheep-{type}-adult-{gender}",
        recruitmentVariants: GenderedAdultVariants("sheep", new[] { "bighorn", "mouflon" }),
        bodyWidth: 1.15f,
        bodyHeight: 1.40f,
        baseMeleeDamage: 3f,
        idleAnimationCode: "idle",
        sitAnimationCode: "sit",
        sleepAnimationCode: "sleep",
        lieAnimationCode: null,
        attackAnimationCode: null,
        walkAnimationCode: null,
        runAnimationCode: null,
        knownAnimations: new[] { "hurt", "die", "idle", "sleep", "sit" });

    public static readonly CompanionSpeciesProfile Wolf = new(
        id: "wolf",
        displayName: "Wolf",
        tameCodePathBase: "tamewolf",
        wildCodeTemplate: "game:wolf-{type}-adult-{gender}",
        recruitmentVariants: GenderedAdultVariants("wolf", new[] { "eurasian" }),
        bodyWidth: 1.20f,
        bodyHeight: 1.00f,
        baseMeleeDamage: 8f,
        idleAnimationCode: "idle",
        sitAnimationCode: "sit",
        sleepAnimationCode: "sleep",
        lieAnimationCode: null,
        attackAnimationCode: null,
        walkAnimationCode: null,
        runAnimationCode: null,
        knownAnimations: new[] { "hurt", "die", "idle", "sleep", "sit" },
        knownSounds: new[] { "hurt", "death", "idle" },
        fallbackSoundKey: "idle");

    // Tamables:FOTSA registers one tamed entity domain for all of its
    // supported Fauna of the Stone Age animals.  The source animal remains
    // encoded in the path after "tame-", so this deliberately acts as a
    // compatibility profile instead of pretending every FOTSA species is a
    // vanilla recruitment species.
    public static readonly CompanionSpeciesProfile TamablesFotsa = new(
        id: "tamables-fotsa",
        displayName: "FOTSA Tamable",
        tameCodePathBase: "tame",
        wildCodeTemplate: string.Empty,
        recruitmentVariants: Array.Empty<CompanionRecruitmentVariant>(),
        bodyWidth: 1.00f,
        bodyHeight: 0.90f,
        baseMeleeDamage: 4f,
        idleAnimationCode: "idle",
        sitAnimationCode: "sit",
        sleepAnimationCode: "sleep",
        lieAnimationCode: "lie",
        attackAnimationCode: "attack",
        walkAnimationCode: "walk",
        runAnimationCode: "run",
        knownAnimations: new[]
        {
            "hurt", "die", "dead", "idle", "sleep", "wounded-idle", "sit", "lie",
            "attack", "walk", "run", "eat"
        },
        knownSounds: new[] { "hurt", "death", "idle" },
        fallbackSoundKey: "idle",
        tameCodeDomain: "tamablesfotsa",
        usesExternalPetAiTaskSet: true);

    // Tamables:Critters registers one tamed entity domain for all of its
    // supported optional animal packs. The source animal remains encoded in
    // the path after "tame-", so this uses the same compatibility profile as
    // FOTSA without making the Critters mod a required dependency.
    public static readonly CompanionSpeciesProfile TamablesCritters = new(
        id: "tamables-critters",
        displayName: "Critters Tamable",
        tameCodePathBase: "tame",
        wildCodeTemplate: string.Empty,
        recruitmentVariants: Array.Empty<CompanionRecruitmentVariant>(),
        bodyWidth: 1.00f,
        bodyHeight: 0.90f,
        baseMeleeDamage: 4f,
        idleAnimationCode: "idle",
        sitAnimationCode: "sit",
        sleepAnimationCode: "sleep",
        lieAnimationCode: "lie",
        attackAnimationCode: "attack",
        walkAnimationCode: "walk",
        runAnimationCode: "run",
        knownAnimations: new[]
        {
            "hurt", "die", "dead", "idle", "sleep", "wounded-idle", "sit", "lie",
            "attack", "walk", "run", "eat", "fly", "swim", "burrow"
        },
        knownSounds: new[] { "hurt", "death", "idle" },
        fallbackSoundKey: "idle",
        tameCodeDomain: "tamablescritters",
        usesExternalPetAiTaskSet: true);

    public static readonly IReadOnlyList<CompanionSpeciesProfile> All = new[]
    {
        Bear, Cat, Chicken, Deer, Dog, Fox, Gazelle, Goat, Hare, Hyena, Pig, Raccoon, Sheep, Wolf,
        TamablesFotsa, TamablesCritters
    };

    private static readonly Dictionary<string, CompanionSpeciesProfile> ById = BuildById();
    private static readonly Dictionary<string, CompanionSpeciesProfile> ByWildRecruitmentCode = BuildByWildRecruitmentCode();

    public static bool TryGetById(string? speciesId, out CompanionSpeciesProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(speciesId) && ById.TryGetValue(speciesId, out CompanionSpeciesProfile? found))
        {
            profile = found;
            return true;
        }

        profile = null!;
        return false;
    }

    public static bool TryGetByTameEntityCode(AssetLocation? code, out CompanionSpeciesProfile profile)
    {
        if (code != null)
        {
            foreach (CompanionSpeciesProfile candidate in All)
            {
                if (candidate.MatchesTameEntityCode(code))
                {
                    profile = candidate;
                    return true;
                }
            }
        }

        profile = null!;
        return false;
    }

    public static bool TryGetByTameEntityCode(string? code, out CompanionSpeciesProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            int separator = code.IndexOf(':');
            string domain = separator >= 0 ? code[..separator] : "feralkinship";
            string path = separator >= 0 ? code[(separator + 1)..] : code;
            return TryGetByTameEntityCode(new AssetLocation(domain, path), out profile);
        }

        profile = null!;
        return false;
    }

    public static bool TryGetByWildRecruitmentEntityCode(string? entityCode, out CompanionSpeciesProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(entityCode)
            && ByWildRecruitmentCode.TryGetValue(entityCode, out CompanionSpeciesProfile? found))
        {
            profile = found;
            return true;
        }

        profile = null!;
        return false;
    }

    public static bool TryGetRecruitmentVariant(
        string? entityCode,
        out CompanionSpeciesProfile profile,
        out CompanionRecruitmentVariant variant)
    {
        if (TryGetByWildRecruitmentEntityCode(entityCode, out profile))
        {
            foreach (CompanionRecruitmentVariant candidate in profile.RecruitmentVariants)
            {
                if (string.Equals(candidate.EntityCode, entityCode, StringComparison.OrdinalIgnoreCase))
                {
                    variant = candidate;
                    return true;
                }
            }
        }

        profile = null!;
        variant = null!;
        return false;
    }

    private static IReadOnlyList<CompanionRecruitmentVariant> GenderedAdultVariants(
        string speciesId,
        IReadOnlyList<string> types)
    {
        List<CompanionRecruitmentVariant> variants = new(types.Count * GenderStates.Length);
        foreach (string type in types)
        {
            foreach (string gender in GenderStates)
            {
                string id = $"{type}-{gender}";
                string displayName = $"{Humanize(type)} {Humanize(gender)}";
                variants.Add(new CompanionRecruitmentVariant(
                    id,
                    $"game:{speciesId}-{type}-adult-{gender}",
                    displayName,
                    type,
                    gender));
            }
        }

        return variants;
    }

    private static IReadOnlyList<CompanionRecruitmentVariant> ExternalGenderedVariants(
        string speciesId,
        string domain,
        string pathTemplate,
        IReadOnlyList<string> types)
    {
        List<CompanionRecruitmentVariant> variants = new(types.Count * GenderStates.Length);
        foreach (string type in types)
        {
            foreach (string gender in GenderStates)
            {
                string id = $"{type}-{gender}";
                string path = pathTemplate
                    .Replace("{type}", type, StringComparison.Ordinal)
                    .Replace("{gender}", gender, StringComparison.Ordinal);
                string displayName = $"{Humanize(type)} {Humanize(gender)}";
                variants.Add(new CompanionRecruitmentVariant(
                    id,
                    $"{domain}:{path}",
                    displayName,
                    type,
                    gender));
            }
        }

        return variants;
    }

    private static IReadOnlyList<string> AdultDogTamePaths()
    {
        List<string> paths = new(5 * GenderStates.Length);
        foreach (string type in new[] { "wolf", "hunting", "shepherd", "corgi", "peach" })
        {
            foreach (string gender in GenderStates)
            {
                paths.Add($"dog-{type}-{gender}");
            }
        }

        return paths;
    }

    private static IReadOnlyList<string> AdultCatTamePaths()
    {
        List<string> paths = new(4 * GenderStates.Length);
        foreach (string type in new[] { "house", "ocelot", "serval", "european" })
        {
            foreach (string gender in GenderStates)
            {
                paths.Add($"cat-{gender}-{type}");
            }
        }

        return paths;
    }

    private static IReadOnlyList<string> AdultFoxTamingEntityCodes()
    {
        List<string> codes = new(2 * GenderStates.Length);
        foreach (string type in new[] { "red", "arctic" })
        {
            foreach (string gender in GenderStates)
            {
                codes.Add($"foxtaming:tamefox-{type}-adult-{gender}");
            }
        }

        return codes;
    }

    private static Dictionary<string, CompanionSpeciesProfile> BuildById()
    {
        Dictionary<string, CompanionSpeciesProfile> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (CompanionSpeciesProfile profile in All) result.Add(profile.Id, profile);
        return result;
    }

    private static Dictionary<string, CompanionSpeciesProfile> BuildByWildRecruitmentCode()
    {
        Dictionary<string, CompanionSpeciesProfile> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (CompanionSpeciesProfile profile in All)
        {
            foreach (CompanionRecruitmentVariant variant in profile.RecruitmentVariants)
            {
                result.Add(variant.EntityCode, profile);
            }
        }

        return result;
    }

    private static string Humanize(string value)
    {
        return value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
    }
}
