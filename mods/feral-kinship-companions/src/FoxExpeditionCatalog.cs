#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace FeralKinshipCompanions;

internal sealed class FoxExpeditionDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string LootProfile { get; init; } = string.Empty;
    public int Tier { get; init; }
    public int MinimumFoxes { get; init; }
    public int MaximumFoxes { get; init; }
    public float TargetStrength { get; init; }
    public float BaseDurationHours { get; init; }
    public int UnlockCost { get; init; }
    public int PreparationCost { get; init; }
    public float RewardScale { get; init; } = 1f;
    public float LateChance { get; init; }
    public float CargoChance { get; init; }
    public float InjuryChance { get; init; }
    public float MiaChance { get; init; }
    public float MortalChance { get; init; }
    public bool DefaultUnlocked { get; init; }
    public bool Retired { get; init; }
    public bool IsSearch { get; init; }
    public bool IsRecruitment { get; init; }
    public bool IsPatrol { get; init; }
    public bool IsCrownJewel { get; init; }
    public float AncientFindChance { get; init; }
    public int AncientFindRolls { get; init; }
    public IReadOnlyList<string> RequiredRouteIds { get; init; } = Array.Empty<string>();

    public bool ProducesRandomLoot => !string.IsNullOrWhiteSpace(LootProfile);

    public string TierLabel => IsCrownJewel
        ? "Crown jewel"
        : Tier > 0 ? $"Tier {Tier}" : "Utility";

    public string RiskLabel
    {
        get
        {
            float combined = CargoChance + InjuryChance + MiaChance + MortalChance;
            if (combined <= 0.10f) return "Low";
            if (combined <= 0.25f) return "Moderate";
            if (combined <= 0.50f) return "High";
            return "Extreme";
        }
    }
}

internal static class FoxExpeditionCatalog
{
    public const float PreparationRiskReduction = 0.20f;
    public const float PatrolRiskReduction = 0.10f;

    private static readonly IReadOnlyList<FoxExpeditionDefinition> Definitions =
        new List<FoxExpeditionDefinition>
        {
            new()
            {
                Id = FoxExpeditionType.Forage,
                Name = "Forage",
                Description = "A nearby search for ordinary food and useful plants.",
                LootProfile = FoxExpeditionType.Forage,
                Tier = 1,
                MinimumFoxes = 2,
                MaximumFoxes = 4,
                TargetStrength = 4f,
                BaseDurationHours = 6f,
                PreparationCost = 1,
                LateChance = 0.05f,
                CargoChance = 0.03f,
                InjuryChance = 0.02f,
                DefaultUnlocked = true
            },
            new()
            {
                Id = FoxExpeditionType.Hunt,
                Name = "Hunt",
                Description = "A routine hunt for meat, bones, fat, and common pelts.",
                LootProfile = FoxExpeditionType.Hunt,
                Tier = 1,
                MinimumFoxes = 3,
                MaximumFoxes = 6,
                TargetStrength = 6f,
                BaseDurationHours = 10f,
                PreparationCost = 1,
                LateChance = 0.08f,
                CargoChance = 0.06f,
                InjuryChance = 0.06f,
                MiaChance = 0.015f,
                MortalChance = 0.005f,
                DefaultUnlocked = true
            },
            new()
            {
                Id = FoxExpeditionType.Scavenge,
                Name = "Scavenge",
                Description = "Search a discovered site for a chosen kind of salvage.",
                LootProfile = FoxExpeditionType.Scavenge,
                Tier = 1,
                MinimumFoxes = 1,
                MaximumFoxes = int.MaxValue,
                TargetStrength = 7f,
                BaseDurationHours = 14f,
                PreparationCost = 1,
                LateChance = 0.10f,
                CargoChance = 0.10f,
                InjuryChance = 0.10f,
                MiaChance = 0.04f,
                MortalChance = 0.01f,
                DefaultUnlocked = true
            },
            new()
            {
                Id = FoxExpeditionType.Scout,
                Name = "Scout",
                Description = "Find a new site or learn more about a discovered one.",
                Tier = 1,
                MinimumFoxes = 1,
                MaximumFoxes = int.MaxValue,
                TargetStrength = 3f,
                BaseDurationHours = 5f,
                PreparationCost = 0,
                DefaultUnlocked = true
            },
            new()
            {
                Id = FoxExpeditionType.SearchLost,
                Name = "Search for lost",
                Description = "Find a particular recoverable pack member. This route is never locked.",
                MinimumFoxes = 1,
                MaximumFoxes = 4,
                TargetStrength = 4f,
                BaseDurationHours = 12f,
                PreparationCost = 2,
                LateChance = 0.08f,
                InjuryChance = 0.08f,
                DefaultUnlocked = true,
                IsSearch = true
            },
            new()
            {
                Id = FoxExpeditionType.Recruitment,
                Name = "Recruitment",
                Description = "Search for a wild companion willing to meet the pack.",
                MinimumFoxes = 3,
                MaximumFoxes = 6,
                TargetStrength = 6f,
                BaseDurationHours = 18f,
                PreparationCost = 2,
                LateChance = 0.08f,
                DefaultUnlocked = true,
                IsRecruitment = true
            },
            new()
            {
                Id = FoxExpeditionType.DistantForage,
                Name = "Distant forage",
                Description = "Range farther for rare seeds, uncommon plants, resin, and honey.",
                LootProfile = FoxExpeditionType.DistantForage,
                Tier = 2,
                MinimumFoxes = 5,
                MaximumFoxes = 7,
                TargetStrength = 7f,
                BaseDurationHours = 12f,
                UnlockCost = 5,
                PreparationCost = 2,
                RewardScale = 1.35f,
                LateChance = 0.08f,
                CargoChance = 0.05f,
                InjuryChance = 0.05f,
                MiaChance = 0.01f,
                MortalChance = 0.002f
            },
            new()
            {
                Id = FoxExpeditionType.PackPatrol,
                Name = "Pack patrol",
                Description = "Secure the local trails and prepare the next expedition.",
                MinimumFoxes = 6,
                MaximumFoxes = 8,
                TargetStrength = 8f,
                BaseDurationHours = 8f,
                UnlockCost = 8,
                PreparationCost = 2,
                LateChance = 0.06f,
                InjuryChance = 0.04f,
                MiaChance = 0.005f,
                IsPatrol = true
            },
            new()
            {
                Id = FoxExpeditionType.GreatHunt,
                Name = "Great hunt",
                Description = "Pursue large game for major meat hauls, fat, and large pelts.",
                LootProfile = FoxExpeditionType.GreatHunt,
                Tier = 2,
                MinimumFoxes = 8,
                MaximumFoxes = 12,
                TargetStrength = 12f,
                BaseDurationHours = 18f,
                UnlockCost = 12,
                PreparationCost = 3,
                RewardScale = 1.75f,
                LateChance = 0.14f,
                CargoChance = 0.10f,
                InjuryChance = 0.13f,
                MiaChance = 0.04f,
                MortalChance = 0.015f
            },
            new()
            {
                Id = FoxExpeditionType.RuinDelve,
                Name = "Ruin delve",
                Description = "Search dangerous ruins for valuable salvage and ancient debris.",
                LootProfile = FoxExpeditionType.RuinDelve,
                Retired = true,
                Tier = 2,
                MinimumFoxes = 9,
                MaximumFoxes = 14,
                TargetStrength = 14f,
                BaseDurationHours = 24f,
                UnlockCost = 18,
                PreparationCost = 4,
                RewardScale = 1.75f,
                LateChance = 0.17f,
                CargoChance = 0.15f,
                InjuryChance = 0.15f,
                MiaChance = 0.06f,
                MortalChance = 0.02f
            },
            new()
            {
                Id = FoxExpeditionType.PrimevalReach,
                Name = "Primeval reach",
                Description = "Cross unmapped wilds for rare plants and ancient finds.",
                LootProfile = FoxExpeditionType.PrimevalReach,
                Tier = 3,
                MinimumFoxes = 8,
                MaximumFoxes = 12,
                TargetStrength = 22f,
                BaseDurationHours = 24f,
                UnlockCost = 25,
                PreparationCost = 5,
                RewardScale = 1.75f,
                LateChance = 0.25f,
                CargoChance = 0.20f,
                InjuryChance = 0.22f,
                MiaChance = 0.08f,
                MortalChance = 0.03f,
                AncientFindChance = 0.40f,
                AncientFindRolls = 1,
                RequiredRouteIds = new[] { FoxExpeditionType.DistantForage }
            },
            new()
            {
                Id = FoxExpeditionType.ApexHunt,
                Name = "Apex hunt",
                Description = "Track dangerous quarry through remote, hostile territory.",
                LootProfile = FoxExpeditionType.ApexHunt,
                Tier = 3,
                MinimumFoxes = 10,
                MaximumFoxes = 14,
                TargetStrength = 26f,
                BaseDurationHours = 30f,
                UnlockCost = 35,
                PreparationCost = 6,
                RewardScale = 2f,
                LateChance = 0.30f,
                CargoChance = 0.25f,
                InjuryChance = 0.28f,
                MiaChance = 0.10f,
                MortalChance = 0.05f,
                AncientFindChance = 0.40f,
                AncientFindRolls = 1,
                RequiredRouteIds = new[] { FoxExpeditionType.GreatHunt }
            },
            new()
            {
                Id = FoxExpeditionType.ResonantDepths,
                Name = "Resonant depths",
                Description = "Search deep ruins for machinery, records, and Resonance relics.",
                LootProfile = FoxExpeditionType.ResonantDepths,
                Retired = true,
                Tier = 3,
                MinimumFoxes = 12,
                MaximumFoxes = 16,
                TargetStrength = 30f,
                BaseDurationHours = 48f,
                UnlockCost = 45,
                PreparationCost = 7,
                RewardScale = 2f,
                LateChance = 0.35f,
                CargoChance = 0.30f,
                InjuryChance = 0.30f,
                MiaChance = 0.12f,
                MortalChance = 0.06f,
                AncientFindChance = 1f,
                AncientFindRolls = 1,
                RequiredRouteIds = new[] { FoxExpeditionType.RuinDelve }
            },
            new()
            {
                Id = FoxExpeditionType.DeepWilds,
                Name = "Deep wilds",
                Description = "The crown-jewel journey through the wilds' worst dangers.",
                LootProfile = FoxExpeditionType.DeepWilds,
                MinimumFoxes = 14,
                MaximumFoxes = 18,
                TargetStrength = 36f,
                BaseDurationHours = 60f,
                UnlockCost = 60,
                PreparationCost = 10,
                RewardScale = 2.25f,
                LateChance = 0.38f,
                CargoChance = 0.32f,
                InjuryChance = 0.32f,
                MiaChance = 0.14f,
                MortalChance = 0.07f,
                IsCrownJewel = true,
                AncientFindChance = 1f,
                AncientFindRolls = 2,
                RequiredRouteIds = new[]
                {
                    FoxExpeditionType.PrimevalReach,
                    FoxExpeditionType.ApexHunt
                }
            }
        };

    private static readonly Dictionary<string, FoxExpeditionDefinition> ById = Definitions
        .ToDictionary(definition => definition.Id, StringComparer.Ordinal);

    public static IReadOnlyList<FoxExpeditionDefinition> All => Definitions;

    public static IReadOnlyList<FoxExpeditionDefinition> Unlockable => Definitions
        .Where(definition => !definition.DefaultUnlocked && !definition.Retired)
        .ToList();

    public static bool TryGet(string id, out FoxExpeditionDefinition? definition)
    {
        return ById.TryGetValue(id ?? string.Empty, out definition);
    }

    public static FoxExpeditionDefinition? Get(string id)
    {
        return TryGet(id, out FoxExpeditionDefinition? definition) ? definition : null;
    }
}
