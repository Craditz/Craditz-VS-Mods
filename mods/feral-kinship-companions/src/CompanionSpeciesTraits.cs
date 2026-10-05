#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;

namespace FeralKinshipCompanions;

internal sealed record CompanionSpeciesTrait(string Species, string Name, string Description,
    string Synergy, string SynergyDescription);

// Stored at departure, never reconstructed from live animals on return. Null on old trips.
[ProtoContract]
public sealed class CompanionSpeciesExpeditionBonus
{
    [ProtoMember(1)] public float Strength;
    [ProtoMember(2)] public float TravelReduction;
    [ProtoMember(3)] public float PlantYield;
    [ProtoMember(4)] public float UsefulFind;
    [ProtoMember(5)] public float RootFind;
    [ProtoMember(6)] public float SeedFind;
    [ProtoMember(7)] public float OrdinaryFind;
    [ProtoMember(8)] public float SmallCatch;
    [ProtoMember(9)] public float HostileProtection;
    [ProtoMember(10)] public float TerrainProtection;
    [ProtoMember(11)] public float Escape;
    [ProtoMember(12)] public float SeverityReduction;
    [ProtoMember(13)] public string SynergySpecies = string.Empty;
    [ProtoMember(14)] public bool SynergyTriggered;
    [ProtoMember(15)] public List<string> Notes = new();
}

internal static class CompanionSpeciesTraits
{
    public static readonly IReadOnlyList<CompanionSpeciesTrait> All = new CompanionSpeciesTrait[]
    {
        new("wolf", "Born Hunter", "+15% personal hunting strength.", "Coordinated Hunt", "35% chance of an extra prime catch on hunts."),
        new("raccoon", "Nimble Paws", "+15% scavenging strength; up to 15% chance of an extra useful find.", "Leave No Crate Unturned", "One extra useful-find roll after a completed search."),
        new("deer", "Woodland Grazer", "+15% foraging strength and up to 15% more plant cargo.", "Hidden Glades", "50% chance of an extra edible patch on foraging trips."),
        new("fox", "Cunning Tracker", "+15% hunting strength; up to 15% extra small-catch chance or faster scouting.", "Outfoxed", "30% chance to prevent one injury, separation, delay or cargo loss, or escape an avoidable site encounter."),
        new("bear", "Heavyweight", "Up to 15% chance to prevent a hostile encounter's injury.", "Unchallenged", "25% chance to prevent one hostile encounter's injury."),
        new("pig", "Keen Snout", "+15% foraging strength; up to 15% chance of an extra root find.", "Rooting Party", "One extra root, edible mushroom, or buried-supply find while foraging."),
        new("goat", "Sure-Footed", "Up to 15% chance to avoid a terrain setback.", "High Paths", "30% chance to prevent one terrain delay or cargo loss."),
        new("hare", "Quick Feet", "Up to 15% faster scouting and chance to escape separation.", "Gone in a Flash", "30% chance to escape one injury or separation."),
        new("hyena", "Nothing Wasted", "+15% hunting/scavenging strength; up to 15% extra ordinary-find chance.", "Pick It Clean", "One extra small catch after a full-strength hunt, or ordinary find after a completed search."),
        new("gazelle", "Light Stride", "Up to 15% shorter expedition travel.", "Swift Passage", "Travel takes a further 10% less time, after innate reductions."),
        new("sheep", "Close Flock", "Up to 15% chance to reduce an injury's severity.", "Safety in Numbers", "35% chance to reduce one severe injury to a lesser injury, or prevent a lesser injury."),
        new("chicken", "Sharp Eyes", "+15% foraging strength; up to 15% chance of extra seeds or grain.", "Scratch and Peck", "One extra seed or grain find on foraging trips.")
    };

    public static CompanionSpeciesTrait? Get(string? species) => All.FirstOrDefault(
        t => string.Equals(t.Species, species, StringComparison.OrdinalIgnoreCase));
    public static string UpgradeDescription(string species, int from, int to)
    {
        string strength = $"strength bonus rises from {from}% to {to}%";
        string support = $"rises from {from}% to {to}%, averaged across the whole party";
        string description = species switch
        {
            "wolf" => $"This wolf's hunting {strength}.",
            "fox" => $"This fox's hunting {strength}. Its extra small-catch chance on hunts and travel reduction while scouting also rise to {to}%, averaged across the whole party.",
            "raccoon" => $"This raccoon's scavenging and ruin-expedition {strength}. Its extra useful-find chance also rises to {to}%, averaged across the whole party.",
            "deer" => $"This deer's foraging {strength}. Its plant-cargo bonus also rises to {to}%, averaged across the whole party.",
            "pig" => $"This pig's foraging {strength}. Its chance of an extra root, edible mushroom, or buried-supply find also rises to {to}%, averaged across the whole party.",
            "chicken" => $"This chicken's foraging {strength}. Its chance of finding extra seeds or grain also rises to {to}%, averaged across the whole party.",
            "hyena" => $"This hyena's hunting, scavenging and ruin-expedition {strength}. Its extra ordinary-find chance also rises to {to}%, averaged across the whole party.",
            "bear" => $"This bear's chance to prevent an injury from a hostile expedition encounter {support}.",
            "goat" => $"This goat's chance to prevent a terrain setback during an expedition {support}.",
            "hare" => $"This hare's scouting travel reduction and chance to prevent an expedition companion going missing rise from {from}% to {to}%, averaged across the whole party.",
            "gazelle" => $"This gazelle's expedition travel reduction {support}.",
            "sheep" => $"This sheep's chance to reduce an expedition injury's severity {support}. A severe injury becomes a lesser injury; a lesser injury is prevented.",
            _ => string.Empty
        };
        return description;
    }

    // Values describe this animal's contribution before the party average is applied.
    public static string[] InnateDetails(string species, int training)
    {
        int percent = (int)Math.Round(Affinity(training) * 100f);
        return species switch
        {
            "wolf" => new[] { $"Hunts: +{percent}% to this wolf's own strength contribution." },
            "fox" => new[] { $"Hunts: +{percent}% to this fox's own strength contribution.", $"Hunts: {percent}% extra small-catch chance, shared across the party.", $"Scouting: {percent}% shorter travel, shared across the party." },
            "raccoon" => new[] { $"Scavenging and ruin expeditions: +{percent}% to this raccoon's own strength contribution.", $"Those searches: {percent}% extra useful-find chance, shared across the party." },
            "deer" => new[] { $"Foraging: +{percent}% to this deer's own strength contribution.", $"Foraging: +{percent}% plant cargo, shared across the party." },
            "pig" => new[] { $"Foraging: +{percent}% to this pig's own strength contribution.", $"Foraging: {percent}% chance of extra roots, edible mushrooms or buried supplies, shared across the party." },
            "chicken" => new[] { $"Foraging: +{percent}% to this chicken's own strength contribution.", $"Foraging: {percent}% extra seed or grain chance, shared across the party." },
            "hyena" => new[] { $"Hunts, scavenging and ruin expeditions: +{percent}% to this hyena's own strength contribution.", $"Those trips: {percent}% extra small-catch or ordinary-find chance, shared across the party." },
            "bear" => new[] { $"Hostile expedition encounters: {percent}% chance to prevent an injury, shared across the party." },
            "goat" => new[] { $"Expedition terrain setbacks: {percent}% chance to prevent the consequence, shared across the party." },
            "hare" => new[] { $"Scouting: {percent}% shorter travel, shared across the party.", $"Expedition separation: {percent}% chance to prevent a companion going missing, shared across the party." },
            "gazelle" => new[] { $"All expedition routes: {percent}% shorter travel, shared across the party." },
            "sheep" => new[] { $"Expedition injuries: {percent}% chance to reduce severity, shared across the party.", "A severe injury becomes a lesser injury; a lesser injury is prevented." },
            _ => Array.Empty<string>()
        };
    }

    public static string TrainingId(string species) => $"species-instinct-{species}";
    public static string MasteryId(string species) => $"species-mastery-{species}";
    public static float Affinity(int training) => 0.15f + 0.05f * Math.Clamp(training, 0, 2);
    public static bool IsHunt(string route) => route is FoxExpeditionType.Hunt or FoxExpeditionType.GreatHunt or FoxExpeditionType.ApexHunt;
    public static bool IsForage(string route) => route is FoxExpeditionType.Forage or FoxExpeditionType.DistantForage or FoxExpeditionType.PrimevalReach;
    public static bool IsScavenge(string route) => route is FoxExpeditionType.Scavenge or FoxExpeditionType.RuinDelve or FoxExpeditionType.ResonantDepths;

    public static float StrengthBonus(string species, string route, float contribution, int training)
    {
        bool applies = Get(species)?.Species switch
        {
            "wolf" or "fox" => IsHunt(route),
            "hyena" => IsHunt(route) || IsScavenge(route),
            "raccoon" => IsScavenge(route),
            "deer" or "pig" or "chicken" => IsForage(route),
            _ => false
        };
        return applies ? Math.Max(0f, contribution) * Affinity(training) : 0f;
    }

    public static CompanionSpeciesExpeditionBonus Build(string route,
        IEnumerable<(string Species, float Strength, int Training)> members)
    {
        var party = members.ToArray();
        CompanionSpeciesExpeditionBonus bonus = new();
        if (party.Length == 0) return bonus;
        // Count every member in the denominator, including compatibility animals.
        // A strict majority is unique; a solo animal does not form a party synergy.
        if (party.Length >= 2)
            bonus.SynergySpecies = party.GroupBy(m => Get(m.Species)?.Species ?? string.Empty)
                .FirstOrDefault(group => group.Key.Length > 0 && group.Count() > party.Length / 2)?.Key
                ?? string.Empty;
        foreach (var member in party)
        {
            float share = Affinity(member.Training) / party.Length;
            bonus.Strength += StrengthBonus(member.Species, route, member.Strength, member.Training);
            switch (Get(member.Species)?.Species)
            {
                case "fox":
                    if (IsHunt(route)) bonus.SmallCatch += share;
                    if (route == FoxExpeditionType.Scout) bonus.TravelReduction += share;
                    break;
                case "raccoon": if (IsScavenge(route)) bonus.UsefulFind += share; break;
                case "deer": if (IsForage(route)) bonus.PlantYield += share; break;
                case "pig": if (IsForage(route)) bonus.RootFind += share; break;
                case "chicken": if (IsForage(route)) bonus.SeedFind += share; break;
                case "hyena": if (IsHunt(route) || IsScavenge(route)) bonus.OrdinaryFind += share; break;
                case "bear": bonus.HostileProtection += share; break;
                case "goat": bonus.TerrainProtection += share; break;
                case "hare":
                    bonus.Escape += share;
                    if (route == FoxExpeditionType.Scout) bonus.TravelReduction += share;
                    break;
                case "gazelle": bonus.TravelReduction += share; break;
                case "sheep": bonus.SeverityReduction += share; break;
            }
        }
        if (bonus.SynergySpecies == "gazelle")
            bonus.TravelReduction = 1f - (1f - bonus.TravelReduction) * 0.9f;
        return bonus;
    }

    public static bool IsHostile(string cause) => cause is "animal-attack" or "predator-attack"
        or "hard-won-quarry" or "quarry-fought-back" or "territorial-beast" or "locusts" or "occupied" or "ringing";
    public static bool IsTerrain(string cause) => cause is "difficult-terrain" or "dangerous-crossing"
        or "collapsed-passage" or "collapsed-storeroom" or "structural-collapse" or "dangerous-descent" or "fragile" or "narrow" or "heavy-rain";

    // Returns a substituted consequence, or empty when prevented. Synergy can intervene once.
    public static string Mitigate(CompanionSpeciesExpeditionBonus? bonus, string cause,
        string consequence, Random random, bool allowSynergy = true)
    {
        if (bonus == null || consequence.Length == 0) return consequence;
        bool injury = consequence is "injury" or "mortal";
        float innate = IsHostile(cause) && injury ? bonus.HostileProtection : 0f;
        if (IsTerrain(cause)) innate = Math.Max(innate, bonus.TerrainProtection);
        if (consequence == "mia") innate = Math.Max(innate, bonus.Escape);
        if (innate > 0f && random.NextDouble() < innate)
        {
            bonus.Notes.Add($"Species instincts prevented {DescribeConsequence(consequence)}.");
            return string.Empty;
        }
        if (injury && bonus.SeverityReduction > 0f && random.NextDouble() < bonus.SeverityReduction)
        {
            bonus.Notes.Add("Close Flock reduced an injury's severity.");
            return consequence == "mortal" ? "injury" : string.Empty;
        }
        if (!allowSynergy || bonus.SynergyTriggered) return consequence;
        float chance = bonus.SynergySpecies switch
        {
            "fox" => 0.30f,
            "bear" when IsHostile(cause) && injury => 0.25f,
            "goat" when IsTerrain(cause) && consequence is "late" or "cargo" => 0.30f,
            "hare" when injury || consequence == "mia" => 0.30f,
            "sheep" when injury => 0.35f,
            _ => 0f
        };
        if (chance <= 0f || random.NextDouble() >= chance) return consequence;
        bonus.SynergyTriggered = true;
        string result = bonus.SynergySpecies == "sheep" && consequence == "mortal" ? "injury" : string.Empty;
        bonus.Notes.Add($"{Get(bonus.SynergySpecies)!.Synergy}: "
            + (result.Length > 0 ? "a severe injury became a lesser injury." : $"avoided {DescribeConsequence(consequence)}."));
        return result;
    }

    private static string DescribeConsequence(string consequence) => consequence switch
    {
        "mortal" => "a severe injury", "injury" => "an injury", "cargo" => "cargo loss",
        "mia" => "a companion going missing", "late" => "a delay", _ => "a dangerous encounter"
    };

    // Shared with the automated checks; applies one fixed party reward, never per animal.
    public static string RollSynergyReward(CompanionSpeciesExpeditionBonus bonus, string route,
        float completion, Random random)
    {
        if (bonus.SynergyTriggered) return string.Empty;
        string reward = bonus.SynergySpecies switch
        {
            "wolf" when IsHunt(route) && random.NextDouble() < .35 => "prime",
            "raccoon" when IsScavenge(route) => "useful",
            "deer" when IsForage(route) && random.NextDouble() < .50 => "edible",
            "pig" when IsForage(route) => "roots",
            "chicken" when IsForage(route) => "seeds",
            "hyena" when (IsHunt(route) || IsScavenge(route)) && completion >= 1f =>
                IsHunt(route) ? "small" : "ordinary",
            _ => string.Empty
        };
        if (reward.Length > 0) bonus.SynergyTriggered = true;
        return reward;
    }

    public static string SynergySummary(CompanionSpeciesExpeditionBonus bonus, string route)
    {
        var trait = Get(bonus.SynergySpecies);
        if (trait == null) return "Synergy: one species must be over half the party; no solo bonus.";
        bool applies = trait.Species switch
        {
            "wolf" => IsHunt(route),
            "raccoon" => IsScavenge(route),
            "deer" or "pig" or "chicken" => IsForage(route),
            "hyena" => IsHunt(route) || IsScavenge(route),
            "gazelle" => true,
            _ => FoxExpeditionCatalog.Get(route)?.ProducesRandomLoot == true
        };
        return $"{trait.Synergy}" + (applies ? " active — " + trait.SynergyDescription : " — no matching effect on this route.");
    }
}
