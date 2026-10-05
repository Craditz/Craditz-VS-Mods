#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;

namespace FeralKinshipCompanions;

internal static class ExpeditionCalculations
{
    private const float MaximumOvercapBonus = 0.30f;
    private const double OvercapCurveRate = 1.0986122886681098d; // ln(3): 2x strength -> +20%.

    public static float GetRawCompletion(float expeditionStrength, float targetStrength) =>
        targetStrength <= 0f ? 0f : Math.Max(0f, expeditionStrength / targetStrength);

    public static float GetCompletion(float expeditionStrength, float targetStrength) =>
        Math.Clamp(GetRawCompletion(expeditionStrength, targetStrength), 0f, 1f);

    public static float GetOvercapRewardFactor(float rawCompletion)
    {
        if (rawCompletion <= 1f)
        {
            return 1f;
        }

        double bonus = MaximumOvercapBonus
            * (1d - Math.Exp(-OvercapCurveRate * (rawCompletion - 1d)));
        return 1f + (float)Math.Clamp(bonus, 0d, MaximumOvercapBonus);
    }

    public static float GetOvercapScore(float overcapRewardFactor) =>
        Math.Clamp((overcapRewardFactor - 1f) / MaximumOvercapBonus, 0f, 1f);
}

[ProtoContract]
public sealed class ExpeditionRewardModifiers
{
    [ProtoMember(1)] public int AdditionalOrdinaryRolls;
    [ProtoMember(2)] public int AdditionalGoalRolls;
    [ProtoMember(3)] public float GoalQuantityMultiplier = 1f;
    [ProtoMember(4)] public int AdditionalSpecialRolls;
    [ProtoMember(5)] public int AdditionalDebrisRolls;
    [ProtoMember(6)] public int AdditionalVesselRolls;
}

[ProtoContract]
public sealed class ExpeditionStoryEventRecord
{
    [ProtoMember(1)] public string Id = string.Empty;
    [ProtoMember(2)] public string Category = string.Empty;
    [ProtoMember(3)] public string Severity = string.Empty;
    [ProtoMember(4)] public string ActorFoxId = string.Empty;
    [ProtoMember(5)] public string CauseEventId = string.Empty;
    [ProtoMember(6)] public string Outcome = string.Empty;
    [ProtoMember(7)] public List<string> AddedTags = new();
}

[ProtoContract]
public sealed class ExpeditionStoryState
{
    [ProtoMember(1)] public int Version = 1;
    [ProtoMember(2)] public string Route = string.Empty;
    [ProtoMember(3)] public string PrimaryProfile = string.Empty;
    [ProtoMember(4)] public string SecondaryProfile = string.Empty;
    [ProtoMember(5)] public float ExpeditionStrength;
    [ProtoMember(6)] public float TargetStrength;
    [ProtoMember(7)] public float RawCompletion;
    [ProtoMember(8)] public float Completion;
    [ProtoMember(9)] public float OvercapRewardFactor = 1f;
    [ProtoMember(10)] public int PartySize;
    [ProtoMember(11)] public string WeatherState = "unspecified";
    [ProtoMember(12)] public string NavigationState = "normal";
    [ProtoMember(13)] public string ReturnState = "on_time";
    [ProtoMember(14)] public string CargoState = "intact";
    [ProtoMember(15)] public string InjuryState = "none";
    [ProtoMember(16)] public string InjuryCause = string.Empty;
    [ProtoMember(17)] public string MiaCause = string.Empty;
    [ProtoMember(18)] public string DelayCause = string.Empty;
    [ProtoMember(19)] public string CargoDamageCause = string.Empty;
    [ProtoMember(20)] public List<string> Tags = new();
    [ProtoMember(21)] public List<ExpeditionStoryEventRecord> Events = new();
    [ProtoMember(22)] public ExpeditionRewardModifiers RewardModifiers = new();
    [ProtoMember(23)] public List<FoxExpeditionStoryOutcome> MemberOutcomes = new();
    [ProtoMember(24)] public float EffectiveRiskMultiplier = 1f;
    [ProtoMember(25)] public float PlannedLateDurationHours;
}

[ProtoContract]
public sealed class FoxExpeditionStoryOutcome
{
    [ProtoMember(1)] public string FoxId = string.Empty;
    [ProtoMember(2)] public string Status = string.Empty;
    [ProtoMember(3)] public string CauseEventId = string.Empty;
}

internal enum ExpeditionEventCategory
{
    Opportunity,
    Hazard,
    Mixed,
    FieldNote
}

internal enum ExpeditionEventEffect
{
    None,
    SmoothJourney,
    GoodWeather,
    FoundBearings,
    OrdinaryRoll,
    GoalBoost,
    SpecialRoll,
    HiddenCache,
    Debris,
    Vessel,
    DelayOnly,
    DelayAndGoal,
    DelayAndOrdinary
}

internal sealed class ExpeditionEventDefinition
{
    public string Id { get; init; } = string.Empty;
    public ExpeditionEventCategory Category { get; init; }
    public float Weight { get; init; } = 1f;
    public int Tier { get; init; } = 1;
    public string[] AllowedFamilies { get; init; } = Array.Empty<string>();
    public string[] AllowedProfiles { get; init; } = Array.Empty<string>();
    public float MinimumCompletion { get; init; }
    public string[] RequiredTags { get; init; } = Array.Empty<string>();
    public string[] ForbiddenTags { get; init; } = Array.Empty<string>();
    public string[] AddedTags { get; init; } = Array.Empty<string>();
    public string[] HardConflicts { get; init; } = Array.Empty<string>();
    public string[] SoftConflicts { get; init; } = Array.Empty<string>();
    public string[] SynergyTags { get; init; } = Array.Empty<string>();
    public string[] Consequences { get; init; } = Array.Empty<string>();
    public ExpeditionEventEffect Effect { get; init; }
}

internal sealed class ExpeditionStoryDebugOptions
{
    public float RiskScale { get; init; } = 1f;
    public string ForceEventId { get; init; } = string.Empty;
    public string ForceConsequence { get; init; } = string.Empty;
}

internal static class ExpeditionEventRegistry
{
    private static readonly IReadOnlyList<ExpeditionEventDefinition> Definitions = BuildDefinitions();
    private static readonly Dictionary<string, ExpeditionEventDefinition> ById = Definitions
        .ToDictionary(definition => definition.Id, StringComparer.Ordinal);

    public static IReadOnlyList<ExpeditionEventDefinition> All => Definitions;

    public static ExpeditionEventDefinition? Get(string id) =>
        ById.TryGetValue(id ?? string.Empty, out ExpeditionEventDefinition? definition)
            ? definition
            : null;

    public static IReadOnlyList<string> Validate()
    {
        List<string> errors = new();
        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> validFamilies = new(StringComparer.Ordinal)
        {
            "shared", "forage", "hunt", "scavenge", "ruin"
        };
        HashSet<string> validProfiles = FoxExpeditionCatalog.All
            .Select(definition => definition.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (ExpeditionEventDefinition definition in Definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.Id) || !ids.Add(definition.Id))
                errors.Add($"duplicate or blank event id '{definition.Id}'");
            if (definition.AllowedFamilies.Length == 0 && definition.AllowedProfiles.Length == 0)
                errors.Add($"event '{definition.Id}' has no valid route family or profile");
            if (definition.AllowedFamilies.Any(family => !validFamilies.Contains(family)))
                errors.Add($"event '{definition.Id}' declares an unknown route family");
            if (definition.AllowedProfiles.Any(profile => !validProfiles.Contains(profile)))
                errors.Add($"event '{definition.Id}' declares an unknown route profile");
            if (definition.RequiredTags.Intersect(definition.ForbiddenTags, StringComparer.Ordinal).Any())
                errors.Add($"event '{definition.Id}' requires and forbids the same tag");
            if (definition.HardConflicts.Contains(definition.Id, StringComparer.Ordinal))
                errors.Add($"event '{definition.Id}' conflicts with itself");
            if (definition.Weight <= 0f)
                errors.Add($"event '{definition.Id}' has a non-positive weight");
            if (definition.Category == ExpeditionEventCategory.FieldNote
                && (definition.Effect != ExpeditionEventEffect.None
                    || definition.AddedTags.Length > 0
                    || definition.Consequences.Length > 0))
                errors.Add($"field note '{definition.Id}' declares a gameplay effect");
        }
        return errors;
    }

    private static IReadOnlyList<ExpeditionEventDefinition> BuildDefinitions() =>
        new List<ExpeditionEventDefinition>
        {
            Event("smooth-journey", ExpeditionEventCategory.Opportunity, 16, new[] { "shared" },
                added: new[] { "smooth_journey" }, forbidden: new[] { "hazard", "delayed" },
                soft: new[] { "heavy-rain", "difficult-terrain", "lost-trail", "dangerous-crossing" },
                effect: ExpeditionEventEffect.SmoothJourney),
            Event("good-weather", ExpeditionEventCategory.Opportunity, 13, new[] { "shared" },
                added: new[] { "weather_clear" }, forbidden: new[] { "weather_bad" },
                hard: new[] { "heavy-rain" }, effect: ExpeditionEventEffect.GoodWeather),
            Event("found-bearings", ExpeditionEventCategory.Opportunity, 10, new[] { "shared" },
                added: new[] { "navigation_easy" }, forbidden: new[] { "navigation_mia" },
                hard: new[] { "lost-trail", "lost-underground" }, effect: ExpeditionEventEffect.FoundBearings),

            Event("heavy-rain", ExpeditionEventCategory.Hazard, 15, new[] { "shared" },
                added: new[] { "hazard", "weather_bad", "wet_conditions", "poor_footing" },
                hard: new[] { "good-weather" }, consequences: new[] { "late", "cargo", "injury" }),
            Event("difficult-terrain", ExpeditionEventCategory.Hazard, 16, new[] { "shared" },
                added: new[] { "hazard", "terrain_difficult" }, consequences: new[] { "late", "injury" }),
            Event("lost-trail", ExpeditionEventCategory.Hazard, 10, new[] { "shared" },
                added: new[] { "hazard", "navigation_lost" }, forbidden: new[] { "navigation_easy" },
                hard: new[] { "found-bearings" }, consequences: new[] { "late", "mia" }),
            Event("dangerous-crossing", ExpeditionEventCategory.Hazard, 12, new[] { "shared" },
                added: new[] { "hazard", "dangerous_crossing" }, synergy: new[] { "wet_conditions" },
                consequences: new[] { "injury", "mia", "mortal" }),
            Event("pack-tore", ExpeditionEventCategory.Hazard, 14, new[] { "shared" },
                added: new[] { "hazard", "cargo_damaged" }, synergy: new[] { "large_find", "cargo_overloaded" },
                consequences: new[] { "cargo" }),
            Event("overloaded-cargo", ExpeditionEventCategory.Mixed, 7, new[] { "shared" }, tier: 2,
                required: new[] { "large_find" }, added: new[] { "cargo_overloaded", "delayed" },
                effect: ExpeditionEventEffect.DelayOnly),

            Event("rich-patch", ExpeditionEventCategory.Opportunity, 18, new[] { "forage" },
                added: new[] { "discovery_food" }, effect: ExpeditionEventEffect.OrdinaryRoll),
            Event("untouched-grove", ExpeditionEventCategory.Opportunity, 10, new[] { "forage" }, tier: 2,
                added: new[] { "discovery_food", "large_find" }, effect: ExpeditionEventEffect.GoalBoost),
            Event("bee-tree", ExpeditionEventCategory.Opportunity, 8, new[] { "forage" },
                added: new[] { "discovery_food" }, effect: ExpeditionEventEffect.SpecialRoll),
            Event("excellent-harvest", ExpeditionEventCategory.Opportunity, 7, new[] { "forage" }, tier: 2,
                added: new[] { "discovery_food", "large_find" }, effect: ExpeditionEventEffect.GoalBoost),
            Event("too-good-to-leave", ExpeditionEventCategory.Mixed, 5, new[] { "forage" }, tier: 2,
                required: new[] { "discovery_food" }, added: new[] { "delayed", "large_find" },
                effect: ExpeditionEventEffect.DelayAndGoal),

            Event("fresh-tracks", ExpeditionEventCategory.Opportunity, 18, new[] { "hunt" },
                added: new[] { "quarry_found" }, effect: ExpeditionEventEffect.OrdinaryRoll),
            Event("large-herd", ExpeditionEventCategory.Opportunity, 10, new[] { "hunt" }, tier: 2,
                added: new[] { "quarry_found", "large_find" }, effect: ExpeditionEventEffect.GoalBoost),
            Event("excellent-quarry", ExpeditionEventCategory.Opportunity, 7, new[] { "hunt" }, tier: 2,
                added: new[] { "quarry_found", "valuable_quarry" }, effect: ExpeditionEventEffect.SpecialRoll),
            Event("hard-won-quarry", ExpeditionEventCategory.Hazard, 12, new[] { "hunt" }, tier: 2,
                required: new[] { "valuable_quarry" }, added: new[] { "hazard", "hard_won" },
                synergy: new[] { "valuable_quarry" }, consequences: new[] { "injury", "mortal" }),
            Event("quarry-fought-back", ExpeditionEventCategory.Hazard, 14, new[] { "hunt" },
                added: new[] { "hazard", "animal_attack" }, consequences: new[] { "injury", "mortal" }),
            Event("long-chase", ExpeditionEventCategory.Mixed, 7, new[] { "hunt" },
                required: new[] { "quarry_found" }, added: new[] { "delayed" },
                effect: ExpeditionEventEffect.DelayAndOrdinary),

            Event("hidden-cache", ExpeditionEventCategory.Opportunity, 4,
                new[] { "forage", "hunt", "scavenge", "ruin" }, tier: 2,
                minimumCompletion: 0.50f, added: new[] { "discovery_cache", "large_find" },
                effect: ExpeditionEventEffect.HiddenCache),
            Event("forgotten-supplies", ExpeditionEventCategory.Opportunity, 13, new[] { "scavenge" },
                added: new[] { "discovery_cache" }, effect: ExpeditionEventEffect.OrdinaryRoll),
            Event("intact-container", ExpeditionEventCategory.Opportunity, 9, new[] { "scavenge" },
                added: new[] { "discovery_cache" }, effect: ExpeditionEventEffect.Vessel),
            Event("abandoned-camp", ExpeditionEventCategory.Opportunity, 11, new[] { "scavenge" },
                added: new[] { "discovery_cache" }, effect: ExpeditionEventEffect.OrdinaryRoll),
            Event("difficult-cache", ExpeditionEventCategory.Hazard, 12, new[] { "scavenge" },
                required: new[] { "discovery_cache" }, added: new[] { "hazard" },
                synergy: new[] { "discovery_cache" }, consequences: new[] { "injury", "cargo" }),

            Event("sealed-chamber", ExpeditionEventCategory.Opportunity, 9, new[] { "ruin" }, tier: 2,
                added: new[] { "ruin_chamber", "large_find" }, effect: ExpeditionEventEffect.Debris),
            Event("intact-storeroom", ExpeditionEventCategory.Opportunity, 7, new[] { "ruin" }, tier: 2,
                added: new[] { "ruin_chamber", "discovery_cache", "large_find" },
                effect: ExpeditionEventEffect.HiddenCache),
            Event("undisturbed-debris", ExpeditionEventCategory.Opportunity, 13, new[] { "ruin" },
                added: new[] { "ruin_debris" }, effect: ExpeditionEventEffect.Debris),
            Event("collapsed-storeroom", ExpeditionEventCategory.Hazard, 12, new[] { "ruin" }, tier: 2,
                required: new[] { "ruin_chamber" }, added: new[] { "hazard", "cargo_damaged" },
                synergy: new[] { "ruin_chamber" }, consequences: new[] { "cargo", "injury" }),
            Event("structural-collapse", ExpeditionEventCategory.Hazard, 14, new[] { "ruin" }, tier: 2,
                added: new[] { "hazard", "structural_collapse" },
                consequences: new[] { "cargo", "injury", "mortal" }),
            Event("dangerous-descent", ExpeditionEventCategory.Hazard, 15, new[] { "ruin" },
                added: new[] { "hazard", "dangerous_descent" }, consequences: new[] { "injury", "mortal" }),
            Event("collapsed-passage", ExpeditionEventCategory.Hazard, 11, new[] { "ruin" }, tier: 2,
                added: new[] { "hazard", "navigation_lost", "trapped" },
                forbidden: new[] { "navigation_easy" }, hard: new[] { "found-bearings" },
                consequences: new[] { "late", "mia" }),
            Event("lost-underground", ExpeditionEventCategory.Hazard, 9, new[] { "ruin" }, tier: 2,
                added: new[] { "hazard", "navigation_lost" }, forbidden: new[] { "navigation_easy" },
                hard: new[] { "found-bearings" }, consequences: new[] { "late", "mia" }),

            // Field notes are deliberately non-mechanical. The resolver chooses exactly one
            // only when a trip would otherwise have no event to tell the player about.
            Event("familiar-scents", ExpeditionEventCategory.FieldNote, 10, new[] { "shared" }),
            Event("rested-under-boughs", ExpeditionEventCategory.FieldNote, 10, new[] { "shared" }),
            Event("followed-old-trail", ExpeditionEventCategory.FieldNote, 10, new[] { "shared" }),
            Event("shared-watch", ExpeditionEventCategory.FieldNote, 10, new[] { "shared" }),
            Event("sampled-berries", ExpeditionEventCategory.FieldNote, 12, new[] { "forage" }),
            Event("followed-pollinators", ExpeditionEventCategory.FieldNote, 12, new[] { "forage" }),
            Event("patient-stalk", ExpeditionEventCategory.FieldNote, 12, new[] { "hunt" }),
            Event("false-tracks", ExpeditionEventCategory.FieldNote, 12, new[] { "hunt" }),
            Event("sorted-old-scraps", ExpeditionEventCategory.FieldNote, 12, new[] { "scavenge" }),
            Event("empty-cache", ExpeditionEventCategory.FieldNote, 12, new[] { "scavenge" }),
            Event("studied-old-marks", ExpeditionEventCategory.FieldNote, 12, new[] { "ruin" }),
            Event("echoes-in-stone", ExpeditionEventCategory.FieldNote, 12, new[] { "ruin" })
        };

    private static ExpeditionEventDefinition Event(
        string id,
        ExpeditionEventCategory category,
        float weight,
        string[] families,
        int tier = 1,
        float minimumCompletion = 0f,
        string[]? profiles = null,
        string[]? required = null,
        string[]? forbidden = null,
        string[]? added = null,
        string[]? hard = null,
        string[]? soft = null,
        string[]? synergy = null,
        string[]? consequences = null,
        ExpeditionEventEffect effect = ExpeditionEventEffect.None) =>
        new()
        {
            Id = id,
            Category = category,
            Weight = weight,
            Tier = tier,
            MinimumCompletion = minimumCompletion,
            AllowedFamilies = families,
            AllowedProfiles = profiles ?? Array.Empty<string>(),
            RequiredTags = required ?? Array.Empty<string>(),
            ForbiddenTags = forbidden ?? Array.Empty<string>(),
            AddedTags = added ?? Array.Empty<string>(),
            HardConflicts = hard ?? Array.Empty<string>(),
            SoftConflicts = soft ?? Array.Empty<string>(),
            SynergyTags = synergy ?? Array.Empty<string>(),
            Consequences = consequences ?? Array.Empty<string>(),
            Effect = effect
        };
}

internal static class ExpeditionStoryResolver
{
    private static readonly string[] DeepWildsProfiles =
    {
        FoxExpeditionType.ApexHunt,
        FoxExpeditionType.ResonantDepths,
        FoxExpeditionType.PrimevalReach
    };

    public static ExpeditionStoryState Resolve(
        FoxExpeditionRecord expedition,
        FoxExpeditionDefinition definition,
        Random random,
        int injuredPartyMembers = 0,
        ExpeditionStoryDebugOptions? debug = null)
    {
        float rawCompletion = ExpeditionCalculations.GetRawCompletion(
            expedition.ExpeditionStrength,
            expedition.TargetStrength);
        float completion = Math.Clamp(rawCompletion, 0f, 1f);
        float overcapFactor = ExpeditionCalculations.GetOvercapRewardFactor(rawCompletion);
        float overcapScore = ExpeditionCalculations.GetOvercapScore(overcapFactor);
        ExpeditionStoryState story = new()
        {
            Route = expedition.Type,
            ExpeditionStrength = expedition.ExpeditionStrength,
            TargetStrength = expedition.TargetStrength,
            RawCompletion = rawCompletion,
            Completion = completion,
            OvercapRewardFactor = overcapFactor,
            PartySize = expedition.SelectedFoxIds?.Count ?? 0,
            PrimaryProfile = definition.LootProfile
        };
        if (string.Equals(definition.LootProfile, FoxExpeditionType.DeepWilds, StringComparison.Ordinal))
        {
            story.PrimaryProfile = DeepWildsProfiles[random.Next(DeepWildsProfiles.Length)];
            story.SecondaryProfile = DeepWildsProfiles
                .Where(profile => !string.Equals(profile, story.PrimaryProfile, StringComparison.Ordinal))
                .ElementAt(random.Next(2));
        }

        float riskMultiplier = Math.Max(
            0.50f,
            1f + (1f - completion) * 1.50f + Math.Max(0, injuredPartyMembers) * 0.10f
                - expedition.PerkRiskReduction
                - expedition.PreparationRiskReduction
                - expedition.PatrolRiskReduction);
        riskMultiplier *= Math.Max(0f, debug?.RiskScale ?? 1f);
        story.EffectiveRiskMultiplier = riskMultiplier;

        HashSet<string> tags = new(StringComparer.Ordinal);
        List<ExpeditionEventDefinition> selected = new();
        if (!string.IsNullOrWhiteSpace(debug?.ForceEventId)
            && ExpeditionEventRegistry.Get(debug.ForceEventId) is ExpeditionEventDefinition forced
            && IsEligible(forced, story, tags, selected))
        {
            if (forced.Category == ExpeditionEventCategory.Hazard && forced.Consequences.Length > 0)
            {
                string consequence = forced.Consequences[0];
                string actorFoxId = consequence == "cargo"
                    ? string.Empty
                    : PickActor(expedition.SelectedFoxIds, story.MemberOutcomes, random);
                AddEvent(story, forced, tags, selected, actorFoxId, consequence);
                ApplyConsequence(story, forced, consequence, actorFoxId);
            }
            else
            {
                AddEvent(story, forced, tags, selected, string.Empty, string.Empty);
                ApplyOpportunityEffect(story, forced, random, overcapScore);
            }
        }

        float opportunityChance = definition.IsCrownJewel
            ? 0.86f
            : definition.Tier switch
            {
                >= 3 => 0.72f,
                2 => 0.56f,
                _ => 0.38f
            };
        opportunityChance = Math.Clamp(
            opportunityChance * (0.65f + completion * 0.35f) + overcapScore * 0.06f,
            0f,
            0.92f);
        int routeTier = definition.IsCrownJewel ? 3 : definition.Tier;
        if (random.NextDouble() < opportunityChance)
        {
            TrySelectAndAdd(story, tags, selected, random, overcapScore,
                routeTier, ExpeditionEventCategory.Opportunity);
        }
        float secondOpportunityChance = definition.IsCrownJewel ? 0.50f : definition.Tier >= 3 ? 0.22f : 0f;
        if (random.NextDouble() < secondOpportunityChance)
        {
            TrySelectAndAdd(story, tags, selected, random, overcapScore,
                routeTier, ExpeditionEventCategory.Opportunity);
        }
        if (random.NextDouble() < (definition.IsCrownJewel ? 0.30f : definition.Tier >= 2 ? 0.12f : 0.05f))
        {
            TrySelectAndAdd(story, tags, selected, random, overcapScore,
                routeTier, ExpeditionEventCategory.Mixed);
        }

        int riskRollCount = 1;
        float shortfall = 1f - completion;
        if (definition.IsCrownJewel)
            riskRollCount = 2 + (int)Math.Ceiling(shortfall * 6f);
        else if (definition.Tier >= 3)
            riskRollCount = 1 + (int)Math.Ceiling(shortfall * 3f);

        string forcedConsequence = NormalizeConsequence(debug?.ForceConsequence);
        for (int rollIndex = 0; rollIndex < riskRollCount; rollIndex++)
        {
            string consequence = rollIndex == 0 && !string.IsNullOrWhiteSpace(forcedConsequence)
                ? forcedConsequence
                : RollConsequence(expedition, definition, random, riskMultiplier, overcapScore);
            if (string.IsNullOrWhiteSpace(consequence))
                continue;

            if (consequence == "mia" && tags.Contains("navigation_easy"))
            {
                story.Events.RemoveAll(item => item.Id == "found-bearings");
                selected.RemoveAll(item => item.Id == "found-bearings");
                tags.Remove("navigation_easy");
                story.NavigationState = "normal";
            }

            ExpeditionEventDefinition? cause = SelectWeighted(
                ExpeditionEventRegistry.All.Where(candidate =>
                    candidate.Category == ExpeditionEventCategory.Hazard
                    && candidate.Consequences.Contains(consequence, StringComparer.Ordinal)
                    && IsEligible(candidate, story, tags, selected)),
                tags,
                random,
                0f);
            if (cause == null)
                continue;

            string actorFoxId = consequence == "cargo"
                ? string.Empty
                : PickActor(expedition.SelectedFoxIds, story.MemberOutcomes, random);
            consequence = CompanionSpeciesTraits.Mitigate(expedition.SpeciesBonus, cause.Id, consequence, random);
            if (consequence.Length == 0) continue;
            AddEvent(story, cause, tags, selected, actorFoxId, consequence);
            ApplyConsequence(story, cause, consequence, actorFoxId);
        }

        bool eventForcesDelay = tags.Contains("delayed");
        float lateChance = definition.LateChance * riskMultiplier * (1f - overcapScore * 0.10f);
        if (eventForcesDelay || random.NextDouble() < lateChance)
        {
            ExpeditionEventDefinition? delayCause = selected.LastOrDefault(candidate =>
                candidate.Consequences.Contains("late", StringComparer.Ordinal)
                || candidate.Effect is ExpeditionEventEffect.DelayAndGoal or ExpeditionEventEffect.DelayAndOrdinary);
            delayCause ??= SelectWeighted(
                ExpeditionEventRegistry.All.Where(candidate =>
                    candidate.Category == ExpeditionEventCategory.Hazard
                    && candidate.Consequences.Contains("late", StringComparer.Ordinal)
                    && IsEligible(candidate, story, tags, selected)),
                tags,
                random,
                0f);
            if (delayCause != null && selected.All(item => item.Id != delayCause.Id))
                AddEvent(story, delayCause, tags, selected, string.Empty, "late");
            story.ReturnState = "late";
            story.DelayCause = delayCause?.Id ?? selected.LastOrDefault()?.Id ?? string.Empty;
            if (delayCause?.Id == "heavy-rain") story.WeatherState = "rain";
            if (delayCause?.Id is "lost-trail" or "lost-underground" or "collapsed-passage")
                story.NavigationState = "temporarily_lost";
            story.PlannedLateDurationHours = Math.Max(0.25f,
                expedition.BaseDurationHours * (0.20f + random.NextSingle() * 0.30f));
            tags.Add("delayed");
        }

        if (story.ReturnState == "late" && CompanionSpeciesTraits.Mitigate(
                expedition.SpeciesBonus, story.DelayCause, "late", random).Length == 0)
        {
            story.ReturnState = "on_time";
            story.DelayCause = string.Empty;
            story.PlannedLateDurationHours = 0f;
            tags.Remove("delayed");
            story.Events.RemoveAll(item => item.Outcome == "late");
        }

        if (tags.Contains("hazard") || story.ReturnState == "late")
        {
            story.Events.RemoveAll(item => item.Id == "smooth-journey");
            selected.RemoveAll(item => item.Id == "smooth-journey");
            tags.Remove("smooth_journey");
        }

        if (story.Events.Count == 0)
        {
            ExpeditionEventDefinition? fieldNote = SelectWeighted(
                ExpeditionEventRegistry.All.Where(candidate =>
                    candidate.Category == ExpeditionEventCategory.FieldNote
                    && IsEligible(candidate, story, tags, selected)),
                tags,
                random,
                0f);
            if (fieldNote != null)
                AddEvent(story, fieldNote, tags, selected, string.Empty, string.Empty);
        }

        story.Tags = tags.OrderBy(tag => tag, StringComparer.Ordinal).ToList();
        NormalizeStory(story);
        return story;
    }

    public static void NormalizeStory(ExpeditionStoryState story)
    {
        story.Events ??= new List<ExpeditionStoryEventRecord>();
        story.Tags ??= new List<string>();
        story.MemberOutcomes ??= new List<FoxExpeditionStoryOutcome>();
        story.RewardModifiers ??= new ExpeditionRewardModifiers();
        if (story.RewardModifiers.GoalQuantityMultiplier <= 0f)
            story.RewardModifiers.GoalQuantityMultiplier = 1f;
        if (story.OvercapRewardFactor < 1f)
            story.OvercapRewardFactor = ExpeditionCalculations.GetOvercapRewardFactor(story.RawCompletion);
        story.Completion = Math.Clamp(story.Completion, 0f, 1f);
        story.RawCompletion = Math.Max(story.Completion, story.RawCompletion);
        story.WeatherState = string.IsNullOrWhiteSpace(story.WeatherState) ? "unspecified" : story.WeatherState;
        story.NavigationState = string.IsNullOrWhiteSpace(story.NavigationState) ? "normal" : story.NavigationState;
        story.ReturnState = string.IsNullOrWhiteSpace(story.ReturnState) ? "on_time" : story.ReturnState;
        story.CargoState = string.IsNullOrWhiteSpace(story.CargoState) ? "intact" : story.CargoState;
        story.InjuryState = string.IsNullOrWhiteSpace(story.InjuryState) ? "none" : story.InjuryState;
    }

    private static void TrySelectAndAdd(
        ExpeditionStoryState story,
        HashSet<string> tags,
        List<ExpeditionEventDefinition> selected,
        Random random,
        float overcapScore,
        int routeTier,
        ExpeditionEventCategory category)
    {
        ExpeditionEventDefinition? definition = SelectWeighted(
            ExpeditionEventRegistry.All.Where(candidate =>
                candidate.Category == category
                && candidate.Tier <= Math.Max(1, routeTier)
                && IsEligible(candidate, story, tags, selected)),
            tags,
            random,
            overcapScore);
        if (definition == null)
            return;
        AddEvent(story, definition, tags, selected, string.Empty, string.Empty);
        ApplyOpportunityEffect(story, definition, random, overcapScore);
    }

    private static bool IsEligible(
        ExpeditionEventDefinition definition,
        ExpeditionStoryState story,
        HashSet<string> tags,
        List<ExpeditionEventDefinition> selected)
    {
        string[] profiles = string.IsNullOrWhiteSpace(story.SecondaryProfile)
            ? new[] { story.PrimaryProfile }
            : new[] { story.PrimaryProfile, story.SecondaryProfile };
        HashSet<string> families = profiles.Select(GetFamily).ToHashSet(StringComparer.Ordinal);
        families.Add("shared");
        bool routeAllowed = definition.AllowedFamilies.Any(families.Contains)
            || definition.AllowedProfiles.Any(profile => profiles.Contains(profile, StringComparer.Ordinal));
        if (!routeAllowed || story.Completion < definition.MinimumCompletion)
            return false;
        if (definition.RequiredTags.Any(required => !tags.Contains(required)))
            return false;
        if (definition.ForbiddenTags.Any(tags.Contains))
            return false;
        if (selected.Any(existing =>
            definition.HardConflicts.Contains(existing.Id, StringComparer.Ordinal)
            || existing.HardConflicts.Contains(definition.Id, StringComparer.Ordinal)))
            return false;
        return selected.All(existing => !string.Equals(existing.Id, definition.Id, StringComparison.Ordinal));
    }

    private static ExpeditionEventDefinition? SelectWeighted(
        IEnumerable<ExpeditionEventDefinition> source,
        HashSet<string> tags,
        Random random,
        float overcapScore)
    {
        List<(ExpeditionEventDefinition Definition, double Weight)> weighted = source
            .Select(definition =>
            {
                double weight = definition.Weight;
                if (definition.SynergyTags.Any(tags.Contains)) weight *= 2d;
                if (definition.SoftConflicts.Any(conflict => tags.Contains(conflict))) weight *= 0.15d;
                if (definition.Category == ExpeditionEventCategory.Opportunity && definition.Tier >= 2)
                    weight *= 1d + overcapScore * 0.20d;
                return (definition, weight);
            })
            .Where(entry => entry.weight > 0d)
            .ToList();
        double total = weighted.Sum(entry => entry.Weight);
        if (total <= 0d) return null;
        double roll = random.NextDouble() * total;
        foreach ((ExpeditionEventDefinition definition, double weight) in weighted)
        {
            roll -= weight;
            if (roll <= 0d) return definition;
        }
        return weighted[^1].Definition;
    }

    private static void AddEvent(
        ExpeditionStoryState story,
        ExpeditionEventDefinition definition,
        HashSet<string> tags,
        List<ExpeditionEventDefinition> selected,
        string actorFoxId,
        string outcome)
    {
        selected.Add(definition);
        foreach (string tag in definition.AddedTags) tags.Add(tag);
        story.Events.Add(new ExpeditionStoryEventRecord
        {
            Id = definition.Id,
            Category = definition.Category == ExpeditionEventCategory.FieldNote
                ? "field-note"
                : definition.Category.ToString().ToLowerInvariant(),
            Severity = definition.Category == ExpeditionEventCategory.FieldNote
                ? "flavor"
                : definition.Tier switch { >= 3 => "major", 2 => "moderate", _ => "minor" },
            ActorFoxId = actorFoxId,
            CauseEventId = definition.Id,
            Outcome = outcome,
            AddedTags = definition.AddedTags.ToList()
        });
    }

    private static void ApplyOpportunityEffect(
        ExpeditionStoryState story,
        ExpeditionEventDefinition definition,
        Random random,
        float overcapScore)
    {
        ExpeditionRewardModifiers modifiers = story.RewardModifiers;
        switch (definition.Effect)
        {
            case ExpeditionEventEffect.SmoothJourney:
                story.ReturnState = "on_time";
                break;
            case ExpeditionEventEffect.GoodWeather:
                story.WeatherState = "clear";
                break;
            case ExpeditionEventEffect.FoundBearings:
                story.NavigationState = "easy";
                break;
            case ExpeditionEventEffect.OrdinaryRoll:
                modifiers.AdditionalOrdinaryRolls++;
                break;
            case ExpeditionEventEffect.GoalBoost:
                modifiers.GoalQuantityMultiplier += 0.10f + overcapScore * 0.03f;
                break;
            case ExpeditionEventEffect.SpecialRoll:
                modifiers.AdditionalSpecialRolls++;
                break;
            case ExpeditionEventEffect.Debris:
                modifiers.AdditionalDebrisRolls++;
                break;
            case ExpeditionEventEffect.Vessel:
                modifiers.AdditionalVesselRolls++;
                break;
            case ExpeditionEventEffect.DelayOnly:
                story.ReturnState = "late";
                story.DelayCause = definition.Id;
                break;
            case ExpeditionEventEffect.HiddenCache:
            {
                string family = GetFamily(story.PrimaryProfile);
                if (family == "ruin")
                {
                    modifiers.AdditionalDebrisRolls += 2;
                    modifiers.AdditionalVesselRolls++;
                }
                else if (family == "scavenge")
                {
                    modifiers.AdditionalOrdinaryRolls++;
                    modifiers.AdditionalVesselRolls++;
                }
                else
                {
                    modifiers.AdditionalOrdinaryRolls += 2;
                }
                if (overcapScore > 0f && random.NextDouble() < overcapScore * 0.35f)
                    modifiers.AdditionalOrdinaryRolls++;
                break;
            }
            case ExpeditionEventEffect.DelayAndGoal:
                modifiers.GoalQuantityMultiplier += 0.15f;
                story.ReturnState = "late";
                story.DelayCause = definition.Id;
                break;
            case ExpeditionEventEffect.DelayAndOrdinary:
                modifiers.AdditionalOrdinaryRolls++;
                story.ReturnState = "late";
                story.DelayCause = definition.Id;
                break;
        }
    }

    private static string RollConsequence(
        FoxExpeditionRecord expedition,
        FoxExpeditionDefinition definition,
        Random random,
        float riskMultiplier,
        float overcapScore)
    {
        float injuryMultiplier = Math.Max(0.50f, riskMultiplier - expedition.PerkInjuryRiskReduction);
        float cargo = definition.CargoChance
            * Math.Max(0.25f, riskMultiplier - expedition.PerkCargoRiskReduction)
            * (1f - overcapScore * 0.05f);
        float injury = definition.InjuryChance * injuryMultiplier * (1f - overcapScore * 0.08f);
        float mia = definition.MiaChance
            * Math.Max(0.35f, riskMultiplier - expedition.PerkMiaRiskReduction)
            * (1f - overcapScore * 0.15f);
        float mortal = definition.MortalChance * injuryMultiplier * (1f - overcapScore * 0.20f);
        double roll = random.NextDouble();
        if (roll < mortal) return "mortal";
        roll -= mortal;
        if (roll < mia) return "mia";
        roll -= mia;
        if (roll < injury) return "injury";
        roll -= injury;
        return roll < cargo ? "cargo" : string.Empty;
    }

    private static void ApplyConsequence(
        ExpeditionStoryState story,
        ExpeditionEventDefinition cause,
        string consequence,
        string actorFoxId)
    {
        switch (consequence)
        {
            case "cargo":
                story.CargoState = "damaged";
                story.CargoDamageCause = cause.Id;
                break;
            case "injury":
                story.InjuryState = story.InjuryState == "mortal" ? "mortal" : "injured";
                story.InjuryCause = cause.Id;
                AddMemberOutcome(story, actorFoxId, "Returned injured", cause.Id);
                break;
            case "mortal":
                story.InjuryState = "mortal";
                story.InjuryCause = cause.Id;
                AddMemberOutcome(story, actorFoxId, "Mortally wounded", cause.Id);
                break;
            case "mia":
                story.NavigationState = "MIA";
                story.MiaCause = cause.Id;
                AddMemberOutcome(story, actorFoxId, "MIA", cause.Id);
                break;
        }
        if (cause.Id == "heavy-rain") story.WeatherState = "rain";
        if (cause.Id is "lost-trail" or "lost-underground" or "collapsed-passage")
            story.NavigationState = consequence == "mia" ? "MIA" : "temporarily_lost";
    }

    private static void AddMemberOutcome(
        ExpeditionStoryState story,
        string foxId,
        string status,
        string causeId)
    {
        if (string.IsNullOrWhiteSpace(foxId)) return;
        FoxExpeditionStoryOutcome? existing = story.MemberOutcomes
            .FirstOrDefault(outcome => string.Equals(outcome.FoxId, foxId, StringComparison.Ordinal));
        if (existing == null)
        {
            story.MemberOutcomes.Add(new FoxExpeditionStoryOutcome
            {
                FoxId = foxId,
                Status = status,
                CauseEventId = causeId
            });
            return;
        }
        if (OutcomeRank(status) > OutcomeRank(existing.Status))
        {
            existing.Status = status;
            existing.CauseEventId = causeId;
        }
    }

    private static int OutcomeRank(string status) => status switch
    {
        "Mortally wounded" => 3,
        "MIA" => 2,
        "Returned injured" => 1,
        _ => 0
    };

    private static string PickActor(
        IReadOnlyList<string>? foxIds,
        IReadOnlyList<FoxExpeditionStoryOutcome> existing,
        Random random)
    {
        List<string> candidates = (foxIds ?? Array.Empty<string>())
            .Where(foxId => existing.All(outcome => !string.Equals(outcome.FoxId, foxId, StringComparison.Ordinal)))
            .ToList();
        if (candidates.Count == 0) candidates = (foxIds ?? Array.Empty<string>()).ToList();
        return candidates.Count == 0 ? string.Empty : candidates[random.Next(candidates.Count)];
    }

    private static string NormalizeConsequence(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "injury" or "injured" => "injury",
        "mortal" or "mortallywounded" or "mortally-wounded" => "mortal",
        "mia" or "missing" => "mia",
        "cargo" or "cargodamage" or "cargo-damage" => "cargo",
        _ => string.Empty
    };

    internal static string GetFamily(string profile) => profile switch
    {
        FoxExpeditionType.Forage or FoxExpeditionType.DistantForage or FoxExpeditionType.PrimevalReach => "forage",
        FoxExpeditionType.Hunt or FoxExpeditionType.GreatHunt or FoxExpeditionType.ApexHunt => "hunt",
        FoxExpeditionType.Scavenge => "scavenge",
        FoxExpeditionType.RuinDelve or FoxExpeditionType.ResonantDepths => "ruin",
        _ => "shared"
    };
}
