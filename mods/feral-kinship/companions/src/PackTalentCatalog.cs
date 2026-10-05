#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;

namespace FeralKinshipCompanions;

/// <summary>
/// Pack Talent board data. The column and row values remain part of the catalog
/// so the board can be rearranged without touching the renderer. The first
/// implementation slices are deliberately single-rank; future nodes remain
/// visible as proof-of-concept placeholders until their hooks are approved.
/// </summary>
internal sealed class PackTalentDefinition
{
    public string Id { get; }
    public string Name { get; }
    public string ParentId { get; }
    public int Column { get; }
    public int Row { get; }
    public bool Implemented { get; }
    public int UnlockCost { get; }
    public string Description { get; }
    public bool Repeatable { get; }
    public bool Permanent { get; }

    public PackTalentDefinition(
        string id,
        string name,
        int column,
        int row,
        string parentId = null,
        bool implemented = false,
        int unlockCost = 0,
        string description = "",
        bool repeatable = false,
        bool permanent = false)
    {
        Id = id;
        Name = name;
        Column = column;
        Row = row;
        ParentId = parentId ?? string.Empty;
        Implemented = implemented;
        UnlockCost = Math.Max(0, unlockCost);
        Description = description ?? string.Empty;
        Repeatable = repeatable;
        Permanent = permanent;
    }
}

internal sealed class PackTalentGroup
{
    public string Id { get; }
    public string Name { get; }
    public IReadOnlyList<PackTalentDefinition> Talents { get; }

    public PackTalentGroup(string id, string name, params PackTalentDefinition[] talents)
    {
        Id = id;
        Name = name;
        Talents = talents;
    }
}

internal static class PackTalentCatalog
{
    public static IReadOnlyList<PackTalentGroup> Groups { get; } = new[]
    {
        new PackTalentGroup("physique", "Pack physique",
            new PackTalentDefinition("hardy-pack", "Hardy Pack", 0, 0, implemented: true, unlockCost: 3, description: "+10% maximum health for every companion in the pack."),
            new PackTalentDefinition("pack-strength", "Pack Strength", 0, 1, implemented: true, unlockCost: 3, description: "+10% damage for every companion in the pack."),
            new PackTalentDefinition("pack-guard", "Pack Guard", 0, 2, implemented: true, unlockCost: 3, description: "Reduces physical damage by 10% after flat armor for every companion in the pack."),
            new PackTalentDefinition("weathered-pack", "Weathered Pack", 0, 3, implemented: true, unlockCost: 3, description: "Reduces fire, frost, heat, and gravity damage by 10% for every companion in the pack."),
            new PackTalentDefinition("quick-fangs", "Quick Fangs", 1, 0, "hardy-pack", implemented: true, unlockCost: 3, description: "+10% attack speed for every companion in the pack."),
            new PackTalentDefinition("fleet-pack", "Fleet Pack", 1, 1, "hardy-pack", implemented: true, unlockCost: 3, description: "+10% movement speed for every companion in the pack."),
            new PackTalentDefinition("shared-recovery", "Shared Recovery", 1, 2, "pack-guard", implemented: true, unlockCost: 3, description: "+20% health regeneration for every companion in the pack."),
            new PackTalentDefinition("danger-sense", "Danger Sense", 1, 3, "weathered-pack", implemented: true, unlockCost: 3, description: "Companions detect predators 8 blocks farther away.")
        ),
        new PackTalentGroup("cohesion", "Pack cohesion",
            new PackTalentDefinition("far-call", "Far Call", 0, 0, implemented: true, unlockCost: 3, description: "+25% range for companion social actions."),
            new PackTalentDefinition("scent-ledger", "Scent Ledger", 0, 1, implemented: true, unlockCost: 3, description: "Keeps every companion's last-known position available for one additional day."),
            new PackTalentDefinition("pack-awareness", "Pack Awareness", 0, 2, implemented: true, unlockCost: 3, description: "Companions notice owned packmates 4 blocks farther away."),
            new PackTalentDefinition("patient-pack", "Patient Pack", 1, 0, "far-call", implemented: true, unlockCost: 3, description: "Reduces the time before a companion can ask for another request by 20%."),
            new PackTalentDefinition("cancellation-discipline", "Cancellation Discipline", 1, 1, implemented: true, unlockCost: 3, description: "Reduces the cancellation cooldown by 20%."),
            new PackTalentDefinition("goodwill", "Goodwill", 1, 2, implemented: true, unlockCost: 3, description: "Companions keep their post-request good mood 20% longer."),
            new PackTalentDefinition("rallying-scent", "Rallying Scent", 2, 0, "goodwill", implemented: true, unlockCost: 3, description: "When any companion attacks, nearby packmates become Rallied for 30 seconds."),
            new PackTalentDefinition("warning-cry", "Warning Cry", 2, 1, "goodwill", implemented: true, unlockCost: 3, description: "When any companion is hurt, nearby packmates become Alarmed for 30 seconds."),
            new PackTalentDefinition("settling-presence", "Settling Presence", 2, 2, "goodwill", implemented: true, unlockCost: 3, description: "Completing a restorative request calms nearby packmates for 30 seconds.")
        ),
        new PackTalentGroup("routine", "Daily routine",
            new PackTalentDefinition("later-bedtime", "Later Bedtime", 0, 0, implemented: true, unlockCost: 3, description: "Companions settle for the night one hour later."),
            new PackTalentDefinition("earlier-morning", "Earlier Morning", 0, 1, implemented: true, unlockCost: 3, description: "Companions leave shelter one hour earlier in the morning."),
            new PackTalentDefinition("efficient-metabolism", "Efficient Metabolism", 0, 2, implemented: true, unlockCost: 3, description: "Pack food drains 20% more slowly."),
            new PackTalentDefinition("better-portions", "Better Portions", 1, 0, "efficient-metabolism", implemented: true, unlockCost: 3, description: "Food restores 20% more of a companion's food meter."),
            new PackTalentDefinition("dutiful-pack", "Dutiful Pack", 1, 1, "earlier-morning", implemented: true, unlockCost: 3, description: "Companions check for assigned duty work about 40% more often."),
            new PackTalentDefinition("work-rhythm", "Work Rhythm", 2, 1, "dutiful-pack", implemented: true, unlockCost: 3, description: "Reduces the cooldown after successfully completing duty work by 30%."),
            new PackTalentDefinition("working-paws", "Working Paws", 3, 1, "work-rhythm", implemented: true, unlockCost: 3, description: "+10% movement speed while performing assigned duties."),
            new PackTalentDefinition("reliable-routine", "Reliable Routine", 2, 2, "dutiful-pack", implemented: true, unlockCost: 3, description: "Halves the retry delay after a duty route fails or becomes stuck.")
        ),
        new PackTalentGroup("labor", "Labor & harvest",
            new PackTalentDefinition("mouthful", "Mouthful", 0, 0, implemented: true, unlockCost: 3, description: "Every companion carries an entire dropped stack instead of taking one item at a time."),
            new PackTalentDefinition("couriers-pace", "Courier's Pace", 0, 1, implemented: true, unlockCost: 3, description: "+20% movement speed while carrying collection cargo."),
            new PackTalentDefinition("harvest-crew", "Harvest Crew", 1, 0, "mouthful", implemented: true, unlockCost: 3, description: "Reduces finished-product duty scans and successful harvest cooldowns by 30%."),
            new PackTalentDefinition("bountiful-crops", "Bountiful Crops", 1, 1, "harvest-crew", implemented: true, unlockCost: 3, description: "+20% crop yield when companions harvest mature crops."),
            new PackTalentDefinition("berry-keeper", "Berry Keeper", 2, 0, "bountiful-crops", implemented: true, unlockCost: 3, description: "+20% berry yield when companions harvest ripe bushes; fractional bonuses use fair random rounding."),
            new PackTalentDefinition("mushroom-lore", "Mushroom Lore", 2, 1, "bountiful-crops", implemented: true, unlockCost: 3, description: "+20% mushroom yield when companions harvest mature mushrooms.")
        ),
        new PackTalentGroup("expeditions", "Expeditions",
            new PackTalentDefinition("trailcraft", "Trailcraft", 0, 0, implemented: true, unlockCost: 3, description: "Reduces expedition travel time by 10%."),
            new PackTalentDefinition("pathfinder", "Pathfinder", 0, 1, implemented: true, unlockCost: 3, description: "Reduces expedition risk by 10%."),
            new PackTalentDefinition("safe-passage", "Safe Passage", 0, 2, implemented: true, unlockCost: 3, description: "Reduces expedition injury risk by 10%."),
            new PackTalentDefinition("expedition-hardiness", "Expedition Hardiness", 0, 3, implemented: true, unlockCost: 3, description: "Reduces expedition risk by another 10%."),
            new PackTalentDefinition("scent-trail", "Scent Trail", 1, 0, "trailcraft", implemented: true, unlockCost: 3, description: "Reduces expedition MIA risk by 15%."),
            new PackTalentDefinition("recruiters-nose", "Recruiter's Nose", 1, 1, "pathfinder", implemented: true, unlockCost: 3, description: "+20 percentage points to recruitment chance."),
            new PackTalentDefinition("cargo-care", "Cargo Care", 1, 2, "safe-passage", implemented: true, unlockCost: 3, description: "Reduces expedition abandoned-cargo risk by 25%."),
            new PackTalentDefinition("deep-pockets", "Deep Pockets", 1, 3, "cargo-care", implemented: true, unlockCost: 3, description: "+10% cargo from every loot-bearing expedition."),
            new PackTalentDefinition("husbandry-cargo", "Husbandry Cargo", 2, 0, "cargo-care", implemented: true, unlockCost: 3, description: "+10% cargo from hunting and animal-focused expeditions."),
            new PackTalentDefinition("scavenger-cargo", "Scavenger Cargo", 2, 1, "cargo-care", implemented: true, unlockCost: 3, description: "+10% cargo from scavenging and salvage expeditions."),
            new PackTalentDefinition("prospector-cargo", "Prospector Cargo", 2, 2, "cargo-care", implemented: true, unlockCost: 3, description: "+10% cargo from mineral and ruin expeditions."),
            new PackTalentDefinition("cultivator-cargo", "Cultivator Cargo", 2, 3, "cargo-care", implemented: true, unlockCost: 3, description: "+10% cargo from crop-focused expeditions."),
            new PackTalentDefinition("forager-cargo", "Forager Cargo", 3, 0, "cargo-care", implemented: true, unlockCost: 3, description: "+10% cargo from wild-plant expeditions."),
            new PackTalentDefinition("survey-training", "Survey Training", 4, 0, implemented: true, unlockCost: 4, description: "Scouting reveals one extra clue and one extra level of site knowledge per trip."),
            new PackTalentDefinition("many-trails", "Many Trails", 3, 1,
                implemented: true,
                unlockCost: 1,
                description: "Permanently adds one active expedition slot per rank. This cannot be refunded or removed by respec.",
                repeatable: true,
                permanent: true)
        ),
        new PackTalentGroup("den-life", "Den life",
            new PackTalentDefinition("fertile-den", "Fertile Den", 0, 0, implemented: true, unlockCost: 3, description: "Raises the chance that a compatible breeding attempt succeeds by 20 percentage points."),
            new PackTalentDefinition("short-gestation", "Short Gestation", 1, 0, "fertile-den", implemented: true, unlockCost: 3, description: "Reduces pregnancy duration by 20%."),
            new PackTalentDefinition("steady-mothers", "Steady Mothers", 1, 1, "fertile-den", implemented: true, unlockCost: 3, description: "Halves the movement and expedition-strength penalties of pregnancy."),
            new PackTalentDefinition("quiet-nursery", "Quiet Nursery", 2, 0, "steady-mothers", implemented: true, unlockCost: 3, description: "Children check for a distant parent 50% less often and panic only beyond 18 blocks."),
            new PackTalentDefinition("healthy-lineage", "Healthy Lineage", 2, 1, "steady-mothers", implemented: true, unlockCost: 3, description: "+10% maximum health for juveniles in the pack.")
        ),
        new PackTalentGroup("work-cart", "Work Cart",
            new PackTalentDefinition("far-reaching-pack", "Far-Reaching Pack", 0, 0,
                implemented: true,
                unlockCost: 1,
                description: "Permanently increases both the pack home radius and Work Cart radius by about 10% of their base range per rank. This cannot be refunded or removed by respec.",
                repeatable: true,
                permanent: true),
            new PackTalentDefinition("careful-extraction", "Careful Extraction", 0, 1),
            new PackTalentDefinition("hauling-pace", "Hauling Pace", 0, 2),
            new PackTalentDefinition("quarry-crew", "Quarry Crew", 1, 0, "careful-extraction"),
            new PackTalentDefinition("timber-crew", "Timber Crew", 1, 1, "careful-extraction"),
            new PackTalentDefinition("mining-crew", "Mining Crew", 1, 2, "careful-extraction"),
            new PackTalentDefinition("camp-supply", "Camp Supply", 2, 0, "hauling-pace"),
            new PackTalentDefinition("shift-bell", "Shift Bell", 2, 1, "far-reaching-pack"),
            new PackTalentDefinition("stockpile-relay", "Stockpile Relay", 2, 2, "camp-supply")
        )
    };

    public static PackTalentGroup Get(string id)
    {
        return Groups.FirstOrDefault(group => string.Equals(group.Id, id, StringComparison.Ordinal))
            ?? Groups[0];
    }

    public static PackTalentDefinition GetTalent(string id)
    {
        return Groups.SelectMany(group => group.Talents)
            .FirstOrDefault(talent => string.Equals(talent.Id, id, StringComparison.Ordinal));
    }
}
