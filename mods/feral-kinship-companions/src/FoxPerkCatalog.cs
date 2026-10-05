#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Datastructures;

namespace FeralKinshipCompanions;

internal static class FoxPerkTreeId
{
    public const string Combat = "combat";
    // Keep the serialized tree id stable while presenting this branch to
    // players as Survival. Existing foxes and network packets still use the
    // historical "mobility" value underneath.
    public const string Survival = "mobility";
    public const string Mobility = Survival;
    public const string Social = "social";
}

internal sealed class FoxPerkDefinition
{
    public required string Id { get; init; }
    public required string TreeId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string UsageRequirement { get; init; } = string.Empty;
    public required string IconPath { get; init; }
    public required int Row { get; init; }
    public required int Column { get; init; }
    public required int MaxRank { get; init; }
    public required int TreeInvestmentRequired { get; init; }
    public bool IsImplemented { get; init; }
    public bool NeedsTesting { get; init; }
    public bool IsRetired { get; init; }
    public bool CountsTowardTreeInvestment { get; init; } = true;
    public string SpeciesId { get; init; } = string.Empty;
    public string RequiredNodeId { get; init; } = string.Empty;
    public int RequiredNodeRank { get; init; }
    public string[] RequiredAnyNodeIds { get; init; } = Array.Empty<string>();
    public string ExclusiveGroup { get; init; } = string.Empty;
    public int[] RankCosts { get; init; } = Array.Empty<int>();
}

internal static class FoxPerkCatalog
{
    private static readonly IReadOnlyList<FoxPerkDefinition> all = BuildDefinitions();
    private static readonly IReadOnlyDictionary<string, FoxPerkDefinition> byId = all
        .ToDictionary(definition => definition.Id, StringComparer.Ordinal);

    public static IReadOnlyList<FoxPerkDefinition> All => all;

    public static IReadOnlyList<FoxPerkDefinition> ForTree(string treeId, string speciesId = "")
    {
        return all
            .Where(definition => !definition.IsRetired
                && string.Equals(definition.TreeId, treeId, StringComparison.Ordinal)
                && AppliesToSpecies(definition, speciesId))
            .ToArray();
    }

    public static bool AppliesToSpecies(FoxPerkDefinition definition, string speciesId)
    {
        return string.IsNullOrWhiteSpace(definition.SpeciesId)
            || string.Equals(definition.SpeciesId, speciesId, StringComparison.OrdinalIgnoreCase);
    }

    public static FoxPerkDefinition? Get(string id)
    {
        return byId.TryGetValue(id, out FoxPerkDefinition? definition) ? definition : null;
    }

    public static int GetRank(ITreeAttribute perks, FoxPerkDefinition definition)
    {
        return Math.Clamp(perks.GetInt(definition.Id, 0), 0, definition.MaxRank);
    }

    public static int GetRank(ITreeAttribute perks, string id)
    {
        return Get(id) is FoxPerkDefinition definition ? GetRank(perks, definition) : 0;
    }

    public static int GetRankCost(FoxPerkDefinition definition, int currentRank)
    {
        if (currentRank < 0 || currentRank >= definition.MaxRank || definition.RankCosts.Length == 0)
        {
            return 0;
        }

        return definition.RankCosts[Math.Min(currentRank, definition.RankCosts.Length - 1)];
    }

    public static int GetTotalSpent(ITreeAttribute perks)
    {
        return all.Sum(definition => GetSpentOnDefinition(perks, definition));
    }

    public static int GetSpentOn(ITreeAttribute perks, string id)
    {
        return Get(id) is FoxPerkDefinition definition
            ? GetSpentOnDefinition(perks, definition)
            : 0;
    }

    public static int GetTreeInvestment(ITreeAttribute perks, string treeId)
    {
        return all
            .Where(definition => definition.CountsTowardTreeInvestment
                && string.Equals(definition.TreeId, treeId, StringComparison.Ordinal))
            .Sum(definition => GetSpentOnDefinition(perks, definition));
    }

    public static bool CanBuy(
        ITreeAttribute perks,
        FoxPerkDefinition definition,
        int availablePoints,
        out string reason)
    {
        if (definition.IsRetired)
        {
            reason = "This talent is no longer available.";
            return false;
        }

        if (!definition.IsImplemented)
        {
            reason = "Coming later.";
            return false;
        }

        int currentRank = GetRank(perks, definition);
        if (currentRank >= definition.MaxRank)
        {
            reason = "Already at maximum rank.";
            return false;
        }

        int treeInvestment = GetTreeInvestment(perks, definition.TreeId);
        if (treeInvestment < definition.TreeInvestmentRequired)
        {
            reason = $"Requires {definition.TreeInvestmentRequired} points invested in this tree.";
            return false;
        }

        if (!string.IsNullOrEmpty(definition.RequiredNodeId)
            && GetRank(perks, definition.RequiredNodeId) < definition.RequiredNodeRank)
        {
            FoxPerkDefinition? required = Get(definition.RequiredNodeId);
            reason = $"Requires {required?.Name ?? definition.RequiredNodeId} {definition.RequiredNodeRank}/{required?.MaxRank ?? definition.RequiredNodeRank}.";
            return false;
        }

        if (definition.RequiredAnyNodeIds.Length > 0
            && !definition.RequiredAnyNodeIds.Any(id => GetRank(perks, id) > 0))
        {
            reason = "Requires one specialization in this branch.";
            return false;
        }

        foreach (FoxPerkDefinition other in all)
        {
            if (!string.IsNullOrEmpty(definition.ExclusiveGroup)
                && string.Equals(other.ExclusiveGroup, definition.ExclusiveGroup, StringComparison.Ordinal)
                && !string.Equals(other.Id, definition.Id, StringComparison.Ordinal)
                && GetRank(perks, other) > 0)
            {
                reason = $"Exclusive with {other.Name}.";
                return false;
            }
        }

        int cost = GetRankCost(definition, currentRank);
        if (availablePoints < cost)
        {
            reason = $"Needs {cost} available point{(cost == 1 ? string.Empty : "s")}.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static string GetLockReason(ITreeAttribute perks, FoxPerkDefinition definition, int availablePoints)
    {
        return CanBuy(perks, definition, availablePoints, out string reason)
            ? "Available"
            : reason;
    }

    private static int GetSpentOnDefinition(ITreeAttribute perks, FoxPerkDefinition definition)
    {
        int rank = GetRank(perks, definition);
        int spent = 0;
        for (int index = 0; index < rank; index++)
        {
            spent += GetRankCost(definition, index);
        }

        return spent;
    }

    private static IReadOnlyList<FoxPerkDefinition> BuildDefinitions()
    {
        List<FoxPerkDefinition> definitions = new();

        Add(definitions, FoxPerkTreeId.Combat, "hardiness", "Thick Hide", "More maximum health. Each rank adds 5 health.", 0, 0, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Combat, "combat-recovery", "Aftercare", "Improves health recovery after the companion has been out of combat for a short time.", 0, 1, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Combat, "feral-damage", "Sharpened Attack", "Increases the companion's damage by 10% per rank.", 0, 2, 3, costs: new[] { 1, 1, 1 }, implemented: true);
        Add(definitions, FoxPerkTreeId.Combat, "even-more-health", "Even More Health", "Adds 2 more maximum health per rank.", 1, 0, 3, requiredNodeId: "hardiness", requiredNodeRank: 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Combat, "armor", "Bone and Bark", "Reduces physical damage by 0.5 per rank.", 1, 1, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Combat, "attack-speed", "Quick Fang", "Improves attack speed by 5% per rank.", 1, 2, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Combat, "damage-training", "Damage Training", "A second route to 10% more damage per rank.", 1, 3, 3, costs: new[] { 1, 1, 1 }, implemented: true);
        Add(definitions, FoxPerkTreeId.Combat, "last-stand", "Refuse to Die", "Once per five-minute cooldown, a lethal hit leaves the companion at 1 health.", 2, 0, 1, requiredNodeId: "hardiness", requiredNodeRank: 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Combat, "battle-rhythm", "Fighting Rhythm", "Attacking grants a short attack-speed bonus.", 2, 1, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Combat, "thick-blood", "Hard to Bleed", "Improves health recovery for a short time after taking damage.", 2, 2, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Combat, "monster-knowledge", "Monster Knowledge", "Adds a Threat survey to this companion's social screen, naming nearby hostile creatures and their health.", 2, 3, 1, implemented: true);
        Add(definitions, FoxPerkTreeId.Combat, "hardened-frame", "Hardened Frame", "Reduces physical damage by another 0.5 per rank.", 3, 0, 3, requiredNodeId: "armor", requiredNodeRank: 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Combat, "predators-force", "Killer Instinct", "Adds 10% damage per rank beyond basic training.", 3, 1, 3, requiredNodeId: "damage-training", requiredNodeRank: 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Combat, "adrenaline-rush", "Adrenaline Rush", "When badly hurt, briefly improves movement and damage resistance.", 3, 2, 2, implemented: true);
        Add(definitions, FoxPerkTreeId.Combat, "fresh-meat", "Fresh Meat", "Attacking improves health recovery for one minute.", 3, 3, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Combat, "iron-fox", "Iron Hide", "Adds 10 maximum health.", 4, 0, 1, implemented: true);
        Add(definitions, FoxPerkTreeId.Combat, "predator", "Predator", "Adds 20% damage.", 4, 1, 1, implemented: true);
        Add(definitions, FoxPerkTreeId.Combat, "second-wind", "One More Breath", "Restores 2 health when Refuse to Die activates.", 4, 2, 1, requiredNodeId: "last-stand", requiredNodeRank: 1, implemented: true);
        Add(definitions, FoxPerkTreeId.Combat, "unshaken", "Unshaken", "Reduces incoming knockback by 75%.", 4, 3, 1, implemented: true);

        foreach (CompanionSpeciesTrait trait in CompanionSpeciesTraits.All)
        {
            AddSpeciesProof(definitions, trait.Species, trait.Species + "-Only Proof", 0, 4);
            Add(definitions, FoxPerkTreeId.Social, CompanionSpeciesTraits.TrainingId(trait.Species),
                trait.Name + " Training", CompanionSpeciesTraits.UpgradeDescription(trait.Species, 15, 20),
                0, 7, 1, implemented: true, testing: true, speciesId: trait.Species,
                treeInvestmentRequired: 0, iconPath: "perks/social/scout.png");
            Add(definitions, FoxPerkTreeId.Social, CompanionSpeciesTraits.MasteryId(trait.Species),
                trait.Name + " Mastery", CompanionSpeciesTraits.UpgradeDescription(trait.Species, 20, 25),
                1, 7, 1, requiredNodeId: CompanionSpeciesTraits.TrainingId(trait.Species), requiredNodeRank: 1,
                costs: new[] { 2 }, implemented: true, testing: true, speciesId: trait.Species,
                treeInvestmentRequired: 0, iconPath: "perks/social/pack-steward.png");
        }

        Add(definitions, FoxPerkTreeId.Mobility, "more-speed", "Fleet Paws", "Adds 10% movement speed per rank.", 0, 0, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Survival, "sure-feet", "Expedition Hardiness", "Reduces injury risk for the entire expedition party by 10% per rank on this companion.", 0, 1, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Survival, "alert-senses", "Alert Senses", "Increases useful environmental detection range by 2 blocks per rank.", 0, 2, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Mobility, "even-more-speed", "Even More Speed", "Adds 5% movement speed per rank beyond More Speed.", 1, 0, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Survival, "dodge", "Dodge", "Adds a 15% chance per rank to avoid direct physical attacks.", 1, 1, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Survival, "natural-resistance", "Weathered Hide", "Reduces fire, frost, heat, and other natural damage by 10% per rank.", 1, 2, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Mobility, "steeplechaser", "Steeplechaser", "Retired: this movement effect was too unreliable in practice.", 1, 3, 2, implemented: false, retired: true);
        Add(definitions, FoxPerkTreeId.Survival, "homeward-bound", "Homeward Bound", "Reduces backpack return time by 15% per rank after unloading at the Pack Cart.", 1, 3, 3,
            implemented: true,
            iconPath: "perks/mobility/steeplechaser.png");
        Add(definitions, FoxPerkTreeId.Survival, "predator-awareness", "Danger Sense", "Increases predator detection range by 8 blocks per rank.", 2, 0, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Mobility, "burst-movement", "Burst Movement", "Shortens expedition return time by 10% per rank through faster movement during the return leg.", 2, 1, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Survival, "featherfall", "Sturdy Traveler", "An injured companion contributes 0.125 additional expedition strength per rank.", 2, 2, 2, implemented: true);
        Add(definitions, FoxPerkTreeId.Mobility, "night-eyes", "Night Eyes", "Darkness no longer makes this companion anxious, and its useful detection range increases at night.", 2, 3, 1, implemented: true);
        Add(definitions, FoxPerkTreeId.Mobility, "roadrunner", "Roadrunner", "Adds 15% movement speed on roads or prepared paths.", 2, 4, 1, implemented: true);
        Add(definitions, FoxPerkTreeId.Mobility, "early-warning", "Early Warning", "The companion gives a distinct call when a predator first enters detection range.", 3, 0, 1, implemented: true);
        Add(definitions, FoxPerkTreeId.Survival, "panic-sprint", "Bolt for Safety", "Taking damage triggers a five-second movement-speed burst.", 3, 1, 1, implemented: true);
        Add(definitions, FoxPerkTreeId.Survival, "catlike-landing", "Stabilized", "Extends the mortally wounded rescue window by 12 in-game hours per rank.", 3, 2, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Survival, "self-stabilizing", "Deathless", "After becoming mortally wounded, the companion recovers after a random 6, 10, 14, or 18 in-game hours, with a guaranteed fallback at 20 hours.", 5, 2, 1, requiredNodeId: "catlike-landing", requiredNodeRank: 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Mobility, "quiet-paws", "Quiet Paws", "Deferred: safely changing every hostile creature's attention requires a dedicated engine hook.", 4, 3, 3);
        Add(definitions, FoxPerkTreeId.Mobility, "lightfooted", "Lightfooted", "Retired: this movement effect caused unsafe physics behavior.", 3, 4, 3, implemented: false, retired: true);
        Add(definitions, FoxPerkTreeId.Survival, "ghoststep", "Vanishing Step", "Adds a further 15% chance to dodge direct physical attacks.", 4, 0, 1, implemented: true);
        Add(definitions, FoxPerkTreeId.Mobility, "trailblazer", "Long Stride", "Shortens expedition travel time by adding a strong return-speed bonus.", 4, 1, 1, implemented: true);
        Add(definitions, FoxPerkTreeId.Mobility, "weatherwise", "Weatherwise", "Rain and harsh weather no longer bias this companion toward anxious moods.", 4, 2, 1, implemented: true);
        Add(definitions, FoxPerkTreeId.Mobility, "lantern-fox", "Foxfire", "Kindles a soft blue-green moving light. Rank 2 roughly doubles its reach to lantern-like strength.", 4, 4, 2, implemented: true);

        Add(definitions, FoxPerkTreeId.Social, "reduced-request-cooldown", "Reduced Request Cooldown", "Reduces the normal request cooldown by 30 seconds per rank.", 0, 0, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "patient-fox", "Patient Companion", "Reduces the request-cancellation cooldown by 30 seconds per rank.", 0, 1, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "goodwill", "Goodwill", "Makes the mood result of a fulfilled request last 60 seconds longer per rank.", 0, 2, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "pack-sense", "Pack Awareness", "Increases the range for noticing nearby owned pack members by 2 blocks per rank.", 0, 3, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "mouthful", "Mouthful", "Carries an entire dropped stack to a Pack Collection Box instead of taking one item at a time.", 5, 3, 1, implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "messenger", "Runner", "Notices dropped items from farther away and moves faster while carrying them to a Pack Collection Box.", 5, 4, 1, implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "scent-ledger", "Scent Ledger", "Keeps last-known position information visible for an additional half-day per rank.", 1, 2, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "gentle-routine", "Gentle Routine", "Keeps the post-request mood stable for 60 seconds longer per rank.", 1, 3, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "pack-contributor", "Pack Contributor", "Adds one extra pack point per rank when this companion completes a normal request.", 1, 4, 3, implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "scout", "Scout", "Adds 0.5 effective expedition strength when this companion joins a mission.", 2, 0, 1, implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "trail-marker", "Leave a Scent", "Reduces the expedition's MIA risk by 15%. Multiple markers do not stack.", 2, 1, 1, implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "rallying-scent", "Rallying Scent", "When this companion attacks, nearby packmates become Rallied (f) for 30 seconds.", 2, 2, 1, implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "pack-leader", "Lead the Way", "Adds 0.5 effective expedition strength and reduces mission risk when selected.", 2, 3, 1, implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "alarm-call", "Warning Cry", "When this companion is hit, nearby packmates become Alarmed (f) for 30 seconds.", 2, 4, 1, implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "shared-calm", "Settling Presence", "When this companion completes a restorative request, nearby packmates become Calm (f) for 30 seconds.", 2, 5, 1, implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "recruiters-nose", "Scent for Strays", "Adds 20% to the expedition's recruitment success chance.", 2, 6, 1,
            usageRequirement: "This companion must join the selected Recruitment mission. Party bonuses stack, up to a 100% total chance.",
            implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "husbandry-affinity", "Husbandry Affinity", "Choose animal cargo as this companion's expedition specialty.", 3, 0, 1, exclusiveGroup: "social-aura-specialization", implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "scavenger-affinity", "Scavenger Affinity", "Choose salvage and vessel cargo as this companion's expedition specialty.", 3, 1, 1, exclusiveGroup: "social-aura-specialization", implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "prospector-affinity", "Prospector Affinity", "Choose mineral and ruin cargo as this companion's expedition specialty.", 3, 2, 1, exclusiveGroup: "social-aura-specialization", implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "cultivator-affinity", "Cultivator Affinity", "Choose crop cargo as this companion's expedition specialty.", 3, 3, 1, exclusiveGroup: "social-aura-specialization", implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "forager-affinity", "Forager Affinity", "Choose wild plant cargo as this companion's expedition specialty.", 3, 4, 1, exclusiveGroup: "social-aura-specialization", implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "quiet-surveyor", "Quiet Surveyor", "When this companion joins a site search with three or fewer companions, avoidable incident chances fall by 25%.", 3, 5, 1, requiredNodeId: "scout", requiredNodeRank: 1, exclusiveGroup: "scavenge-party-style", implemented: true, iconPath: "perks/social/scout.png");
        Add(definitions, FoxPerkTreeId.Social, "many-paws", "Many Paws", "When this companion joins a site search with six or more companions, routine salvage rolls increase by 20%.", 3, 6, 1, requiredNodeId: "scout", requiredNodeRank: 1, exclusiveGroup: "scavenge-party-style", implemented: true, iconPath: "perks/social/pack-leader.png");
        Add(definitions, FoxPerkTreeId.Social, "aura-of-husbandry", "Husbandry Sense", "Adds 10% animal expedition cargo per rank. Only the party's strongest matching sense applies.", 4, 0, 3, requiredNodeId: "husbandry-affinity", requiredNodeRank: 1, exclusiveGroup: "social-aura", implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "aura-of-scavenging", "Scavenging Sense", "Adds 10% salvage expedition cargo per rank. Only the party's strongest matching sense applies.", 4, 1, 3, requiredNodeId: "scavenger-affinity", requiredNodeRank: 1, exclusiveGroup: "social-aura", implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "aura-of-prospecting", "Prospecting Sense", "Adds 10% mineral and ruin expedition cargo per rank. Only the strongest matching sense applies.", 4, 2, 3, requiredNodeId: "prospector-affinity", requiredNodeRank: 1, exclusiveGroup: "social-aura", implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "aura-of-cultivation", "Cultivation Sense", "Adds 10% crop expedition cargo per rank. Only the party's strongest matching sense applies.", 4, 3, 3, requiredNodeId: "cultivator-affinity", requiredNodeRank: 1, exclusiveGroup: "social-aura", implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "aura-of-foraging", "Foraging Sense", "Adds 10% wild-plant expedition cargo per rank. Only the party's strongest matching sense applies.", 4, 4, 3, requiredNodeId: "forager-affinity", requiredNodeRank: 1, exclusiveGroup: "social-aura", implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "greater-aura", "Greater Sense", "Adds another 10% cargo to this companion's chosen expedition specialty.", 5, 0, 1, requiredAnyNodeIds: new[] { "aura-of-husbandry", "aura-of-scavenging", "aura-of-prospecting", "aura-of-cultivation", "aura-of-foraging" }, implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "pack-steward", "Packwise", "Adds 0.5 effective expedition strength through better pack coordination.", 5, 1, 1, implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "true-companion", "Heart of the Pack", "Reduces both interaction cooldowns and keeps fulfilled-request moods stable for two additional minutes.", 5, 2, 1, implemented: true);
        Add(definitions, FoxPerkTreeId.Social, "far-seeker", "Far Seeker", "When assigned to scout a new site, this companion biases discoveries toward rarer, larger, more dangerous places. Multiple Far Seekers do not stack.", 5, 5, 1, requiredNodeId: "scout", requiredNodeRank: 1, implemented: true, iconPath: "perks/social/scent-ledger.png");

        return definitions;
    }

    private static void AddSpeciesProof(
        List<FoxPerkDefinition> definitions,
        string speciesId,
        string name,
        int row,
        int column)
    {
        Add(
            definitions,
            FoxPerkTreeId.Combat,
            $"species-proof-{speciesId}",
            name,
            $"This is a {speciesId}-only perk. It does absolutely nothing. "
                + $"If you can read this on a {speciesId}, species-specific perks are filtering correctly.",
            row,
            column,
            1,
            costs: new[] { 1 },
            implemented: true,
            testing: true,
            retired: true,
            speciesId: speciesId,
            treeInvestmentRequired: 0,
            countsTowardTreeInvestment: false,
            iconPath: "perks/combat/monster-knowledge.png"
        );
    }

    private static void Add(
        List<FoxPerkDefinition> definitions,
        string treeId,
        string id,
        string name,
        string description,
        int row,
        int column,
        int maxRank,
        string requiredNodeId = "",
        int requiredNodeRank = 0,
        string[]? requiredAnyNodeIds = null,
        string exclusiveGroup = "",
        int[]? costs = null,
        bool implemented = false,
        bool testing = false,
        bool retired = false,
        string speciesId = "",
        int treeInvestmentRequired = -1,
        bool countsTowardTreeInvestment = true,
        string iconPath = "",
        string usageRequirement = "")
    {
        definitions.Add(new FoxPerkDefinition
        {
            Id = id,
            TreeId = treeId,
            Name = name,
            Description = description,
            UsageRequirement = usageRequirement,
            IconPath = string.IsNullOrWhiteSpace(iconPath) ? $"perks/{treeId}/{id}.png" : iconPath,
            Row = row,
            Column = column,
            MaxRank = maxRank,
            IsImplemented = implemented,
            NeedsTesting = testing,
            IsRetired = retired,
            CountsTowardTreeInvestment = countsTowardTreeInvestment,
            SpeciesId = speciesId,
            TreeInvestmentRequired = treeInvestmentRequired >= 0
                ? treeInvestmentRequired
                : row switch
                {
                    0 => 0,
                    1 => 3,
                    2 => 8,
                    3 => 14,
                    4 => 20,
                    _ => 26
                },
            RequiredNodeId = requiredNodeId,
            RequiredNodeRank = requiredNodeRank,
            RequiredAnyNodeIds = requiredAnyNodeIds ?? Array.Empty<string>(),
            ExclusiveGroup = exclusiveGroup,
            RankCosts = costs ?? BuildCosts(row, maxRank)
        });
    }

    private static int[] BuildCosts(int row, int maxRank)
    {
        if (maxRank == 1)
        {
            return new[] { 1 };
        }

        return row <= 1
            ? new[] { 1, 2, 3 }
            : new[] { 2, 3, 4 };
    }
}
