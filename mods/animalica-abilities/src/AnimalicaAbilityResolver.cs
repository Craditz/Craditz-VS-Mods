using System;
using Vintagestory.API.Common;

namespace ScentTrails;

[Flags]
public enum AnimalicaAbilityFlags
{
    None = 0,
    Scent = 1,
    NightSight = 2
}

public enum AnimalicaActiveAbility
{
    None = 0,
    Pounce = 1,
    Adrenaline = 2
}

public sealed class AnimalicaAbilityProfile
{
    public AnimalicaAbilityProfile(
        string race,
        AnimalicaAbilityFlags abilities,
        AnimalicaActiveAbility activeAbility = AnimalicaActiveAbility.None
    )
    {
        Race = race;
        Abilities = abilities;
        ActiveAbility = activeAbility;
    }

    public string Race { get; }

    public AnimalicaAbilityFlags Abilities { get; }

    public AnimalicaActiveAbility ActiveAbility { get; }

    public bool HasScent => (Abilities & AnimalicaAbilityFlags.Scent) != 0;

    public bool HasNightSight => (Abilities & AnimalicaAbilityFlags.NightSight) != 0;

    public bool HasPounce => ActiveAbility == AnimalicaActiveAbility.Pounce;

    public bool HasAdrenaline => ActiveAbility == AnimalicaActiveAbility.Adrenaline;

    public string SpeciesLocalizationKey => $"animalicaabilities:species-{Race}";

    public static AnimalicaAbilityProfile None { get; } = new("unknown", AnimalicaAbilityFlags.None);
}

public static class AnimalicaAbilityResolver
{
    private const string ExplicitRaceAttribute = "feralKinshipRace";
    private const string PlayerModelLibModelAttribute = "skinModel";
    private const string ExtraTraitsAttribute = "extraTraits";

    public const string ScentTraitCode = "animalicasenses-scent";
    public const string NightSightTraitCode = "animalicasenses-nightsight";
    public const string BothTraitCode = "animalicasenses-both";
    public const string PounceTraitCode = "animalicaabilities-pounce";
    public const string AdrenalineTraitCode = "animalicaabilities-adrenaline";

    // These aliases mirror Feral Kinship's normalized player-race catalog and
    // the model codes added by Animalica's compatibility packs. Ability
    // ownership is assigned to the normalized race, so compatible models such
    // as Foxxo inherit the same profile as Animalica foxes.
    private static readonly ModelRule[] ModelRules =
    {
        new("bear", "bear"),
        new("blackbear", "bear"),
        new("brownbear", "bear"),
        new("polarbear", "bear"),
        new("giantpanda", "bear"),
        new("sunbear", "bear"),
        new("cats", "feline"),
        new("bird", "chicken"),
        new("chicken", "chicken"),
        new("critter-hedgehog", "hedgehog"),
        new("critter-owl", "owl"),
        new("feraldeer", "deer"),
        new("deer", "deer"),
        new("deer_antler", "deer"),
        new("equus", "equus"),
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
        new("draconis-cuprocaudus", "dragon"),
        new("feverstone-cockatrice", "dragon"),
        new("feverstone-direwolf", "wolf"),
        new("feverstone-hellboar", "boar"),
        new("feverstone-toad", "frog"),
        new("hieronymus-african-monitors", "monitor"),
        new("hieronymus-asian-monitors", "monitor"),
        new("hieronymus-roughneck-dumerils-monitors", "monitor"),
        new("hieronymus-komodo-dragon", "monitor"),
        new("hieronymus-banded-geckos", "gecko"),
        new("hieronymus-knob-tailed-geckos", "gecko"),
        new("hieronymus-leopard-geckos", "gecko"),
        new("hieronymus-ensatinas", "frog"),
        new("hieronymus-new-zealand-frogs", "frog"),
        new("hieronymus-pond-frogs", "frog"),
        new("hieronymus-rain-frogs", "frog"),
        new("jimothy", "small-scavenger"),
        new("caninae", "canine"),
        new("machairodontinae", "feline"),
        new("pantherinae", "feline"),
        new("dinosaur-abelisauridae", "dinosaur-predator"),
        new("dinosaur-carcharodontosauridae", "dinosaur-predator"),
        new("dinosaur-dromaeosauridae", "dinosaur-predator"),
        new("dinosaur-spinosauridae", "dinosaur-predator"),
        new("dinosaur-tyrannosauridae", "dinosaur-predator"),
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

    // First-pass gameplay assignments. These are intentionally coarse and
    // can later be split into Weak, Normal, and Strong profiles per sense.
    private static readonly AnimalicaAbilityProfile[] Profiles =
    {
        new("bear", AnimalicaAbilityFlags.Scent, AnimalicaActiveAbility.Adrenaline),
        new("boar", AnimalicaAbilityFlags.Scent, AnimalicaActiveAbility.Adrenaline),
        new("canine", AnimalicaAbilityFlags.Scent | AnimalicaAbilityFlags.NightSight, AnimalicaActiveAbility.Adrenaline),
        new("chicken", AnimalicaAbilityFlags.None),
        new("dinosaur-predator", AnimalicaAbilityFlags.Scent | AnimalicaAbilityFlags.NightSight, AnimalicaActiveAbility.Adrenaline),
        new("deer", AnimalicaAbilityFlags.None, AnimalicaActiveAbility.Adrenaline),
        new("dragon", AnimalicaAbilityFlags.Scent | AnimalicaAbilityFlags.NightSight, AnimalicaActiveAbility.Pounce),
        new("feline", AnimalicaAbilityFlags.Scent | AnimalicaAbilityFlags.NightSight, AnimalicaActiveAbility.Pounce),
        new("fish", AnimalicaAbilityFlags.None),
        new("fox", AnimalicaAbilityFlags.Scent | AnimalicaAbilityFlags.NightSight, AnimalicaActiveAbility.Pounce),
        new("frog", AnimalicaAbilityFlags.NightSight, AnimalicaActiveAbility.Pounce),
        new("gazelle", AnimalicaAbilityFlags.None, AnimalicaActiveAbility.Adrenaline),
        new("gecko", AnimalicaAbilityFlags.Scent | AnimalicaAbilityFlags.NightSight, AnimalicaActiveAbility.Pounce),
        new("goat", AnimalicaAbilityFlags.None),
        new("hare", AnimalicaAbilityFlags.NightSight, AnimalicaActiveAbility.Adrenaline),
        new("hedgehog", AnimalicaAbilityFlags.Scent | AnimalicaAbilityFlags.NightSight),
        new("hyena", AnimalicaAbilityFlags.Scent | AnimalicaAbilityFlags.NightSight, AnimalicaActiveAbility.Adrenaline),
        new("monitor", AnimalicaAbilityFlags.Scent),
        new("owl", AnimalicaAbilityFlags.NightSight),
        new("pig", AnimalicaAbilityFlags.Scent, AnimalicaActiveAbility.Adrenaline),
        new("raccoon", AnimalicaAbilityFlags.Scent | AnimalicaAbilityFlags.NightSight),
        new("sheep", AnimalicaAbilityFlags.None),
        new("small-scavenger", AnimalicaAbilityFlags.Scent),
        new("wolf", AnimalicaAbilityFlags.Scent | AnimalicaAbilityFlags.NightSight, AnimalicaActiveAbility.Adrenaline),
        new("equus", AnimalicaAbilityFlags.None, AnimalicaActiveAbility.Adrenaline),
    };

    public static AnimalicaAbilityProfile Resolve(EntityPlayer? player)
    {
        if (player == null)
        {
            return AnimalicaAbilityProfile.None;
        }

        AnimalicaAbilityProfile speciesProfile = ResolveSpeciesProfile(player);
        AnimalicaAbilityFlags abilities = speciesProfile.Abilities | ResolveTraitAbilities(player);
        AnimalicaActiveAbility activeAbility = speciesProfile.ActiveAbility;
        if (activeAbility == AnimalicaActiveAbility.None)
        {
            activeAbility = ResolveTraitActiveAbility(player);
        }

        return new AnimalicaAbilityProfile(speciesProfile.Race, abilities, activeAbility);
    }

    public static AnimalicaAbilityFlags ResolveTraitAbilities(EntityPlayer? player)
    {
        if (player == null)
        {
            return AnimalicaAbilityFlags.None;
        }

        AnimalicaAbilityFlags abilities = AnimalicaAbilityFlags.None;
        string[] traits = player.WatchedAttributes.GetStringArray(
            ExtraTraitsAttribute,
            Array.Empty<string>()
        );

        foreach (string trait in traits)
        {
            if (trait.Equals(ScentTraitCode, StringComparison.Ordinal)
                || trait.Equals(BothTraitCode, StringComparison.Ordinal))
            {
                abilities |= AnimalicaAbilityFlags.Scent;
            }

            if (trait.Equals(NightSightTraitCode, StringComparison.Ordinal)
                || trait.Equals(BothTraitCode, StringComparison.Ordinal))
            {
                abilities |= AnimalicaAbilityFlags.NightSight;
            }
        }

        return abilities;
    }

    public static AnimalicaActiveAbility ResolveTraitActiveAbility(EntityPlayer? player)
    {
        if (player == null)
        {
            return AnimalicaActiveAbility.None;
        }

        string[] traits = player.WatchedAttributes.GetStringArray(
            ExtraTraitsAttribute,
            Array.Empty<string>()
        );

        foreach (string trait in traits)
        {
            if (trait.Equals(PounceTraitCode, StringComparison.Ordinal))
            {
                return AnimalicaActiveAbility.Pounce;
            }

            if (trait.Equals(AdrenalineTraitCode, StringComparison.Ordinal))
            {
                return AnimalicaActiveAbility.Adrenaline;
            }
        }

        return AnimalicaActiveAbility.None;
    }

    private static AnimalicaAbilityProfile ResolveSpeciesProfile(EntityPlayer player)
    {
        string explicitRace = player.WatchedAttributes.GetString(ExplicitRaceAttribute, string.Empty);
        if (!string.IsNullOrWhiteSpace(explicitRace))
        {
            AnimalicaAbilityProfile explicitProfile = ResolveRace(explicitRace.Trim().ToLowerInvariant());
            if (!explicitProfile.Race.Equals(AnimalicaAbilityProfile.None.Race, StringComparison.OrdinalIgnoreCase))
            {
                return explicitProfile;
            }
        }

        string modelCode = player.WatchedAttributes.GetString(PlayerModelLibModelAttribute, string.Empty);
        return ResolveModel(modelCode);
    }

    public static AnimalicaAbilityProfile ResolveModel(string? modelCode)
    {
        modelCode ??= string.Empty;
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
                return ResolveRace(rule.Race);
            }
        }

        return AnimalicaAbilityProfile.None;
    }

    private static AnimalicaAbilityProfile ResolveRace(string race)
    {
        foreach (AnimalicaAbilityProfile profile in Profiles)
        {
            if (profile.Race.Equals(race, StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }
        }

        return AnimalicaAbilityProfile.None;
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
}
