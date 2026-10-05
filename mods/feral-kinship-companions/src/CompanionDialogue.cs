#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;

/// <summary>Stable event identifiers shared by authored data and code hooks.</summary>
internal static class CompanionDialogueEvent
{
    public const string DebugFoxJoke = "debug.fox_joke";
    public const string GuideTaxComplaint = "guide.tax_complaint";
    public const string RequestPrefix = "request.";
    public const string ThoughtMood = "thought.mood";
    public const string ThoughtPersonality = "thought.personality";
    public const string CommandPrefix = "command.";
    public const string DutyPrefix = "duty.";
    public const string FoodPrefix = "food.";
    public const string CombatPrefix = "combat.";
    public const string DenPrefix = "den.";
    public const string ExpeditionPrefix = "expedition.";
    public const string FamilyPrefix = "family.";
    public const string MilestonePrefix = "milestone.";
    public const string ExpPrefix = "exp.";
}

internal sealed record CompanionDialogueContext
{
    internal static readonly string[] KnownTokenNames =
    {
        "companion", "owner", "personality", "mood", "species", "age", "item", "target",
        "location", "duty", "request", "command", "reason", "count", "milestone", "enemy"
    };

    public string EventId { get; init; } = string.Empty;
    public string FoxId { get; init; } = string.Empty;
    public string Personality { get; init; } = string.Empty;
    public string SpeciesId { get; init; } = string.Empty;
    public string SpeciesSource { get; init; } = string.Empty;
    public string AgeStage { get; init; } = "adult";
    public string Mood { get; init; } = string.Empty;
    public Dictionary<string, string> Facts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class CompanionDialogueDocument
{
    public List<CompanionDialogueEntry> Entries { get; set; } = new();
}

internal sealed class CompanionDialogueCoverageDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<string> RequiredPersonalities { get; set; } = new();
    public List<CompanionDialogueSpeciesContract> SupportedSpecies { get; set; } = new();
    public List<CompanionDialogueCoverageEvent> Events { get; set; } = new();
}

internal sealed class CompanionDialogueSpeciesContract
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

internal sealed class CompanionDialogueCoverageEvent
{
    public string Id { get; set; } = string.Empty;
    public bool SpeechExpected { get; set; } = true;
    public int TargetVariants { get; set; } = 3;
    public string Frequency { get; set; } = "future";
    public string Priority { get; set; } = "low";
    public bool Hooked { get; set; }
    public string Notes { get; set; } = string.Empty;
}

internal static class CompanionDialoguePriority
{
    public const int Debug = 0;
    public const int Low = 10;
    public const int Medium = 40;
    public const int High = 70;
    public const int Critical = 90;

    public static string Describe(int priority) => priority switch
    {
        <= Debug => "debug",
        < Medium => "low",
        < High => "medium",
        < Critical => "high",
        _ => "critical"
    };

    public static bool IsValid(int priority) => priority is >= 0 and <= 100;

    public static bool IsValidLabel(string? label) =>
        label is "debug" or "low" or "medium" or "high" or "critical";
}

internal sealed class CompanionDialogueEntry
{
    public string Event { get; set; } = string.Empty;
    public string Personality { get; set; } = "*";
    public string Channel { get; set; } = "bubble";
    public int Priority { get; set; } = 10;
    public double Chance { get; set; } = 1d;
    public double CooldownSeconds { get; set; } = 0d;
    public string SemanticGroup { get; set; } = string.Empty;
    public bool OnceOnly { get; set; }
    /// <summary>When true, a species-specific pool augments the generic pool instead of replacing it.</summary>
    public bool Additive { get; set; }
    public bool Enabled { get; set; } = true;
    public string Species { get; set; } = "*";
    public string Age { get; set; } = "any";
    public List<string> Moods { get; set; } = new();
    public Dictionary<string, string> Requires { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string>? Excludes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<CompanionDialogueVariant> Variants { get; set; } = new();
}

internal sealed class CompanionDialogueVariant
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public double Weight { get; set; } = 1d;
    public bool Enabled { get; set; } = true;
    public string SemanticGroup { get; set; } = string.Empty;
    public bool OnceOnly { get; set; }
    public double CooldownSeconds { get; set; }
    public double Chance { get; set; } = 1d;
    public string Age { get; set; } = "any";
    public List<string> Moods { get; set; } = new();
    public Dictionary<string, string> Requires { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string>? Excludes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class CompanionDialogueSelection
{
    public string EventId { get; init; } = string.Empty;
    public string LineId { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public string Channel { get; init; } = "bubble";
    public string SemanticGroup { get; init; } = string.Empty;
    public int Priority { get; init; }
    public double CooldownSeconds { get; init; }
    public bool OnceOnlyEvent { get; init; }
    public bool OnceOnlyVariant { get; init; }
    public long ExpiresAtUtcMs { get; init; }
}

internal sealed class CompanionDialogueRegistry
{
    // Custom top-level asset folders are not indexed by Vintage Story's asset manager.
    // Keep the catalogs under the recognized config category so they load on the server.
    internal const string DialogueAssetPath = "config/dialogue/dialogue.json";
    internal const string CoverageAssetPath = "config/dialogue/dialogue-coverage.json";

    private readonly Dictionary<string, List<CompanionDialogueEntry>> entries =
        new(StringComparer.OrdinalIgnoreCase);
    private List<CompanionDialogueCoverageEvent> coverageEvents = new();
    private List<string> requiredPersonalities = new();
    private List<CompanionDialogueSpeciesContract> supportedSpecies = new();

    public int EntryCount => entries.Values.Sum(list => list.Count);
    public int VariantCount => entries.Values.Sum(list => list.Sum(entry =>
        (entry.Variants ?? new List<CompanionDialogueVariant>()).Count(variant => variant.Enabled)));
    public int RegisteredConcreteEventCount => coverageEvents.Count;
    public IReadOnlyList<CompanionDialogueCoverageEvent> CoverageEvents => coverageEvents;
    public IReadOnlyList<string> RequiredPersonalities => requiredPersonalities;
    public IReadOnlyList<CompanionDialogueSpeciesContract> SupportedSpecies => supportedSpecies;

    public void Load(ICoreAPI api)
    {
        entries.Clear();
        coverageEvents = DialogueCoverage.CreateFallbackCoverage();
        requiredPersonalities = DialogueCoverage.Personalities.ToList();
        supportedSpecies = DialogueCoverage.CreateFallbackSpecies();
        try
        {
            IAsset? asset = api.Assets.TryGet(new AssetLocation("feralkinshipcompanions", DialogueAssetPath));
            CompanionDialogueDocument? document = asset?.ToObject<CompanionDialogueDocument>();
            foreach (CompanionDialogueEntry entry in document?.Entries ?? new List<CompanionDialogueEntry>())
            {
                if (string.IsNullOrWhiteSpace(entry.Event)) continue;
                if (!entries.TryGetValue(entry.Event, out List<CompanionDialogueEntry>? list))
                {
                    list = new List<CompanionDialogueEntry>();
                    entries[entry.Event] = list;
                }
                list.Add(entry);
            }

            api.Logger.Notification(
                "[FeralKinshipCompanions] Dialogue catalog loaded: {0} runtime pools, {1} authored lines.",
                EntryCount,
                VariantCount);
        }
        catch (Exception exception)
        {
            api.Logger.Error(
                "[FeralKinshipCompanions] Dialogue catalog could not be loaded; legacy fallback remains active. {0}",
                exception.Message);
        }

        try
        {
            IAsset? coverageAsset = api.Assets.TryGet(new AssetLocation("feralkinshipcompanions", CoverageAssetPath));
            CompanionDialogueCoverageDocument? coverage = coverageAsset?.ToObject<CompanionDialogueCoverageDocument>();
            if (coverage?.Events is { Count: > 0 }) coverageEvents = coverage.Events;
            if (coverage?.RequiredPersonalities is { Count: > 0 })
                requiredPersonalities = coverage.RequiredPersonalities;
            if (coverage?.SupportedSpecies is { Count: > 0 })
                supportedSpecies = coverage.SupportedSpecies;
        }
        catch (Exception exception)
        {
            api.Logger.Error(
                "[FeralKinshipCompanions] Dialogue coverage could not be loaded; code fallback remains active. {0}",
                exception.Message);
        }
    }

    public bool TrySelect(
        CompanionDialogueContext context,
        IReadOnlyCollection<string>? recentLineIds,
        IReadOnlyCollection<string>? recentGroups,
        IReadOnlyCollection<string>? usedOnceEvents,
        IReadOnlyCollection<string>? usedOnceLines,
        long nowUtcMs,
        out CompanionDialogueSelection? selection,
        string? excludedFollowText = null)
    {
        selection = null;
        if (string.IsNullOrWhiteSpace(context.EventId)
            || !entries.TryGetValue(context.EventId, out List<CompanionDialogueEntry>? eventEntries))
        {
            return false;
        }

        var candidates = new List<(CompanionDialogueEntry Entry, CompanionDialogueVariant Variant, int Score)>();
        foreach (CompanionDialogueEntry entry in eventEntries)
        {
            if (!MatchesEntry(entry, context)) continue;
            foreach (CompanionDialogueVariant variant in entry.Variants ?? new List<CompanionDialogueVariant>())
            {
                string lineId = string.IsNullOrWhiteSpace(variant.Id)
                    ? entry.Event + ":" + variant.Text
                    : variant.Id;
                if ((entry.OnceOnly && usedOnceEvents?.Contains(entry.Event) == true)
                    || (variant.OnceOnly && usedOnceLines?.Contains(lineId) == true))
                {
                    continue;
                }
                if (!MatchesVariant(entry, variant, context)
                    || string.IsNullOrWhiteSpace(variant.Text)
                    || !variant.Enabled)
                {
                    continue;
                }

                if (entry.Event.StartsWith("idle.follow.", StringComparison.Ordinal)
                    && string.Equals(ExpandTokens(variant.Text, context), excludedFollowText, StringComparison.Ordinal)) continue;
                string group = string.IsNullOrWhiteSpace(variant.SemanticGroup)
                    ? entry.SemanticGroup
                    : variant.SemanticGroup;
                int specificity = (IsExact(entry.Personality, context.Personality) ? 1000 : 0)
                    + (IsExact(entry.Species, context.SpeciesId) && !entry.Additive ? 100 : 0)
                    + (IsExact(entry.Age, context.AgeStage) ? 20 : 0)
                    + entry.Requires.Count + variant.Requires.Count;
                // Follow pools deliberately combine generic and matching-personality lines.
                // Eligibility (including safety facts) is still checked above; weights supply the bonus.
                if (entry.Event.StartsWith("idle.follow.", StringComparison.Ordinal)) specificity = 0;
                candidates.Add((entry, variant, specificity));
            }
        }

        if (candidates.Count == 0) return false;
        int bestScore = candidates.Max(candidate => candidate.Score);
        List<(CompanionDialogueEntry Entry, CompanionDialogueVariant Variant, int Score)> best =
            candidates.Where(candidate => candidate.Score == bestScore).ToList();
        List<(CompanionDialogueEntry Entry, CompanionDialogueVariant Variant, int Score)> freshLines =
            best.Where(candidate =>
                !IsRecent(recentLineIds, GetLineId(candidate.Entry, candidate.Variant))
                && !IsRecent(recentGroups, GetSemanticGroup(candidate.Entry, candidate.Variant)))
                .ToList();
        if (freshLines.Count > 0)
        {
            best = freshLines;
        }
        else
        {
            List<(CompanionDialogueEntry Entry, CompanionDialogueVariant Variant, int Score)> freshLineOnly =
                best.Where(candidate => !IsRecent(recentLineIds, GetLineId(candidate.Entry, candidate.Variant)))
                    .ToList();
            if (freshLineOnly.Count > 0) best = freshLineOnly;
            else
            {
                List<(CompanionDialogueEntry Entry, CompanionDialogueVariant Variant, int Score)> freshGroupOnly =
                    best.Where(candidate => !IsRecent(recentGroups, GetSemanticGroup(candidate.Entry, candidate.Variant)))
                        .ToList();
                if (freshGroupOnly.Count > 0) best = freshGroupOnly;
            }
        }
        double totalWeight = best.Sum(candidate => Math.Max(0.01d, candidate.Variant.Weight));
        double roll = Random.Shared.NextDouble() * totalWeight;
        (CompanionDialogueEntry Entry, CompanionDialogueVariant Variant, int Score) chosen = best[^1];
        foreach (var candidate in best)
        {
            roll -= Math.Max(0.01d, candidate.Variant.Weight);
            if (roll <= 0d)
            {
                chosen = candidate;
                break;
            }
        }

        double chance = chosen.Entry.Chance * chosen.Variant.Chance;
        if (chance < 1d && Random.Shared.NextDouble() > Math.Max(0d, chance)) return false;

        string selectedLineId = string.IsNullOrWhiteSpace(chosen.Variant.Id)
            ? chosen.Entry.Event + ":" + chosen.Variant.Text
            : chosen.Variant.Id;
        string semanticGroup = string.IsNullOrWhiteSpace(chosen.Variant.SemanticGroup)
            ? chosen.Entry.SemanticGroup
            : chosen.Variant.SemanticGroup;
        double cooldown = Math.Max(chosen.Entry.CooldownSeconds, chosen.Variant.CooldownSeconds);
        selection = new CompanionDialogueSelection
        {
            EventId = chosen.Entry.Event,
            LineId = selectedLineId,
            Text = ExpandTokens(chosen.Variant.Text, context),
            Channel = chosen.Entry.Channel,
            SemanticGroup = semanticGroup,
            Priority = chosen.Entry.Priority,
            CooldownSeconds = cooldown,
            OnceOnlyEvent = chosen.Entry.OnceOnly,
            OnceOnlyVariant = chosen.Variant.OnceOnly,
            ExpiresAtUtcMs = nowUtcMs + (chosen.Entry.Priority >= CompanionDialoguePriority.Critical ? 15000L : 10000L)
        };
        return true;
    }

    public IEnumerable<string> Validate(
        IReadOnlyCollection<string> personalities,
        IReadOnlyCollection<string> eventIds)
    {
        var errors = new List<string>();
        var seenLineIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenCoverageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CompanionDialogueCoverageEvent coverage in coverageEvents)
        {
            if (string.IsNullOrWhiteSpace(coverage.Id) || coverage.Id.Contains('*'))
                errors.Add("Coverage contains a non-concrete event ID");
            else if (!seenCoverageIds.Add(coverage.Id))
                errors.Add($"Duplicate coverage event ID: {coverage.Id}");
            else if (!eventIds.Contains(coverage.Id, StringComparer.OrdinalIgnoreCase))
                errors.Add($"Unknown coverage event ID: {coverage.Id}");
            if (!CompanionDialoguePriority.IsValidLabel(coverage.Priority))
                errors.Add($"Unknown coverage priority '{coverage.Priority}' on {coverage.Id}");
        }
        foreach (string personality in requiredPersonalities)
        {
            if (!personalities.Contains(personality, StringComparer.OrdinalIgnoreCase))
                errors.Add($"Unknown required coverage personality: {personality}");
        }
        foreach ((string eventId, List<CompanionDialogueEntry> eventEntries) in entries)
        {
            if (!eventIds.Contains(eventId, StringComparer.OrdinalIgnoreCase))
                errors.Add($"Unknown event ID: {eventId}");
            foreach (CompanionDialogueEntry entry in eventEntries)
            {
                if (entry.Personality != "*"
                    && !personalities.Contains(entry.Personality, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"Unknown personality '{entry.Personality}' on {eventId}");
                }
                if (!CompanionDialoguePriority.IsValid(entry.Priority))
                    errors.Add($"Priority {entry.Priority} is outside 0-100 on {eventId}");
                foreach (CompanionDialogueVariant variant in entry.Variants ?? new List<CompanionDialogueVariant>())
                {
                    if (string.IsNullOrWhiteSpace(variant.Text)) continue;
                    string lineId = string.IsNullOrWhiteSpace(variant.Id)
                        ? eventId + ":" + variant.Text
                        : variant.Id;
                    if (!seenLineIds.Add(lineId)) errors.Add($"Duplicate line ID: {lineId}");
                    if (variant.Text.Length > 240) errors.Add($"Long line ({variant.Text.Length} chars): {lineId}");
                    if (variant.Text.Contains('—')) errors.Add($"Em dash is forbidden in authored dialogue: {lineId}");
                    foreach (Match token in Regex.Matches(variant.Text, "\\{([A-Za-z0-9_.-]+)\\}"))
                    {
                        string tokenName = token.Groups[1].Value;
                        if (tokenName is not "companion" and not "owner"
                            && !entry.Requires.ContainsKey(tokenName)
                            && !variant.Requires.ContainsKey(tokenName)
                            && !CompanionDialogueContext.KnownTokenNames.Contains(tokenName, StringComparer.OrdinalIgnoreCase))
                        {
                            errors.Add($"Unknown placeholder '{tokenName}' on {lineId}");
                        }
                    }
                }

                if (entry.Channel is not ("bubble" or "ui" or "narrative"))
                    errors.Add($"Unknown channel '{entry.Channel}' on {eventId}");
            }
        }
        return errors;
    }

    public string BuildValidationReport(
        IReadOnlyCollection<string> personalities,
        IReadOnlyCollection<string> eventIds)
    {
        string[] malformed = Validate(personalities, eventIds).ToArray();
        string[] matcherChecks = ValidateMatcherSemantics().ToArray();
        var report = new StringBuilder();
        report.AppendLine("Dialogue validation report");
        report.AppendLine($"Authored entries: {EntryCount}");
        report.AppendLine($"Loaded authored lines: {VariantCount}");
        report.AppendLine(malformed.Length == 0
            ? "Malformed/unknown authored data: none"
            : $"Malformed/unknown authored data: {malformed.Length}");
        foreach (string error in malformed) report.AppendLine("  - " + error);
        report.AppendLine(matcherChecks.Length == 0
            ? "Matcher semantics self-check: passed"
            : $"Matcher semantics self-check: {matcherChecks.Length} failure(s)");
        foreach (string error in matcherChecks) report.AppendLine("  - " + error);

        int requiredCombinations = 0;
        int filledCombinations = 0;
        int missingCombinations = 0;
        var missingByEvent = new List<string>();
        foreach (CompanionDialogueCoverageEvent coverage in coverageEvents)
        {
            if (!coverage.SpeechExpected || coverage.Id.StartsWith("conversation.", StringComparison.OrdinalIgnoreCase)) continue;
            List<string> required = requiredPersonalities.Count > 0
                ? requiredPersonalities
                : personalities.ToList();
            var missing = new List<string>();
            int minimumActual = int.MaxValue;
            int maximumActual = 0;
            foreach (string personality in required)
            {
                int actual = CountVariants(coverage.Id, personality);
                minimumActual = Math.Min(minimumActual, actual);
                maximumActual = Math.Max(maximumActual, actual);
                requiredCombinations++;
                if (actual > 0) filledCombinations++;
                else
                {
                    missingCombinations++;
                    missing.Add(personality);
                }
            }

            if (minimumActual == int.MaxValue) minimumActual = 0;
            string actualRange = minimumActual == maximumActual
                ? minimumActual.ToString()
                : $"{minimumActual}-{maximumActual}";
            report.AppendLine(
                $"  {coverage.Id}: target {coverage.TargetVariants}, actual {actualRange}, "
                + $"filled {required.Count - missing.Count}/{required.Count}");
            if (missing.Count > 0)
                missingByEvent.Add($"{coverage.Id}: {string.Join(", ", missing)}");
        }

        report.AppendLine(
            $"Required event/personality combinations: {requiredCombinations}; "
            + $"filled: {filledCombinations}; missing: {missingCombinations}");
        report.AppendLine("Missing combinations:");
        if (missingByEvent.Count == 0) report.AppendLine("  none");
        else foreach (string missing in missingByEvent) report.AppendLine("  - " + missing);

        string[] silent = coverageEvents
            .Where(coverage => !coverage.SpeechExpected)
            .Select(coverage => coverage.Id)
            .ToArray();
        report.AppendLine("Intentionally silent events: "
            + (silent.Length == 0 ? "none" : string.Join(", ", silent)));

        string[] hooked = coverageEvents
            .Where(coverage => coverage.Hooked)
            .Select(coverage => coverage.Id)
            .ToArray();
        string[] unhooked = coverageEvents
            .Where(coverage => !coverage.Hooked)
            .Select(coverage => coverage.Id)
            .ToArray();
        report.AppendLine($"Hooked events: {hooked.Length}");
        report.AppendLine($"Registered but unhooked events: {unhooked.Length}");
        report.AppendLine("Developer joke excluded from required personality coverage: "
            + (coverageEvents.Any(coverage => coverage.Id == CompanionDialogueEvent.DebugFoxJoke
                && !coverage.SpeechExpected && coverage.TargetVariants == 0)
                ? "yes"
                : "NO"));
        return report.ToString().TrimEnd();
    }

    private static IEnumerable<string> ValidateMatcherSemantics()
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["state"] = "calm",
            ["item"] = "berry"
        };
        var context = new CompanionDialogueContext
        {
            EventId = "test",
            Personality = "Curious",
            SpeciesId = "fox",
            Facts = facts
        };

        var empty = new CompanionDialogueEntry();
        if (!MatchesEntry(empty, context))
            yield return "empty Requires/Excludes should match";

        empty.Requires["state"] = "calm";
        if (!MatchesEntry(empty, context))
            yield return "matching entry Requires should match";

        empty.Requires["state"] = "alert";
        if (MatchesEntry(empty, context))
            yield return "nonmatching entry Requires should reject";

        empty.Requires.Clear();
        empty.Excludes!["state"] = "calm";
        if (MatchesEntry(empty, context))
            yield return "matching entry Excludes should reject";

        empty.Excludes!["state"] = "alert";
        if (!MatchesEntry(empty, context))
            yield return "nonmatching entry Excludes should allow";

        empty.Excludes = null;
        if (!MatchesEntry(empty, context))
            yield return "null entry Excludes should allow";

        var variant = new CompanionDialogueVariant();
        if (!MatchesVariant(empty, variant, context))
            yield return "empty variant Requires/Excludes should match";

        variant.Requires["item"] = "berry";
        if (!MatchesVariant(empty, variant, context))
            yield return "matching variant Requires should match";

        variant.Requires["item"] = "apple";
        if (MatchesVariant(empty, variant, context))
            yield return "nonmatching variant Requires should reject";

        variant.Requires.Clear();
        variant.Excludes!["state"] = "calm";
        if (MatchesVariant(empty, variant, context))
            yield return "matching variant Excludes should reject";

        variant.Excludes!["state"] = "alert";
        if (!MatchesVariant(empty, variant, context))
            yield return "nonmatching variant Excludes should allow";

        variant.Excludes = null;
        if (!MatchesVariant(empty, variant, context))
            yield return "null variant Excludes should allow";
    }

    private int CountVariants(string eventId, string personality)
    {
        if (!entries.TryGetValue(eventId, out List<CompanionDialogueEntry>? eventEntries)) return 0;
        return eventEntries
            .Where(entry => entry.Enabled
                && (entry.Personality == "*"
                    || string.Equals(entry.Personality, personality, StringComparison.OrdinalIgnoreCase)))
            .Sum(entry => (entry.Variants ?? new List<CompanionDialogueVariant>())
                .Count(variant => variant.Enabled && !string.IsNullOrWhiteSpace(variant.Text)));
    }

    private static bool MatchesEntry(CompanionDialogueEntry entry, CompanionDialogueContext context)
    {
        return entry.Enabled
            && IsPersonalityMatch(entry.Personality, context.Personality)
            && IsSpeciesMatch(entry.Species, context.SpeciesId, context.SpeciesSource)
            && IsAgeMatch(entry.Age, context.AgeStage)
            && IsMoodMatch(entry.Moods, context.Mood)
            && MatchesFacts(entry.Requires, context.Facts)
            && !IsExcluded(entry.Excludes, context.Facts);
    }

    private static bool MatchesVariant(
        CompanionDialogueEntry entry,
        CompanionDialogueVariant variant,
        CompanionDialogueContext context)
    {
        return IsAgeMatch(variant.Age, context.AgeStage)
            && IsMoodMatch(variant.Moods, context.Mood)
            && MatchesFacts(variant.Requires, context.Facts)
            && !IsExcluded(variant.Excludes, context.Facts);
    }

    private static bool IsRecent(IReadOnlyCollection<string>? values, string value) =>
        values?.Contains(value) == true;

    private static bool IsExcluded(
        Dictionary<string, string>? exclusions,
        Dictionary<string, string> facts) =>
        exclusions is { Count: > 0 } && MatchesFacts(exclusions, facts);

    private static string GetLineId(CompanionDialogueEntry entry, CompanionDialogueVariant variant) =>
        string.IsNullOrWhiteSpace(variant.Id) ? entry.Event + ":" + variant.Text : variant.Id;

    private static string GetSemanticGroup(CompanionDialogueEntry entry, CompanionDialogueVariant variant) =>
        string.IsNullOrWhiteSpace(variant.SemanticGroup) ? entry.SemanticGroup : variant.SemanticGroup;

    private static bool MatchesFacts(
        Dictionary<string, string>? requirements,
        Dictionary<string, string> facts)
    {
        foreach ((string key, string expected) in requirements ?? new Dictionary<string, string>())
        {
            if (!facts.TryGetValue(key, out string? actual)
                || (!string.Equals(expected, "*", StringComparison.Ordinal)
                    && !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsPersonalityMatch(string value, string personality) =>
        value == "*" || string.Equals(value, personality, StringComparison.OrdinalIgnoreCase);

    private static bool IsSpeciesMatch(string value, string species, string source) =>
        value == "*"
        || string.Equals(value, species, StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, source, StringComparison.OrdinalIgnoreCase);

    private static bool IsAgeMatch(string value, string age) =>
        string.IsNullOrWhiteSpace(value) || value == "*" || value.Equals("any", StringComparison.OrdinalIgnoreCase)
        || value.Equals(age, StringComparison.OrdinalIgnoreCase);

    private static bool IsMoodMatch(List<string>? moods, string mood) =>
        moods == null || moods.Count == 0 || moods.Any(value => value == "*" || value.Equals(mood, StringComparison.OrdinalIgnoreCase));

    private static bool IsExact(string value, string actual) =>
        !string.IsNullOrWhiteSpace(value) && value != "*" && value.Equals(actual, StringComparison.OrdinalIgnoreCase);

    private static string ExpandTokens(string text, CompanionDialogueContext context)
    {
        string result = text;
        foreach ((string key, string value) in context.Facts)
        {
            result = result.Replace("{" + key + "}", value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        result = result.Replace("{companion}", context.Facts.GetValueOrDefault("companion", string.Empty), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{owner}", context.Facts.GetValueOrDefault("owner", string.Empty), StringComparison.OrdinalIgnoreCase);
        return result.Contains('{') ? string.Empty : result;
    }
}

internal sealed class CompanionDialogueService
{
    private const int RecentLineLimit = 8;
    private const int RecentEventLimit = 8;
    private const long SharedEventSuppressionMs = 2500;
    private const long DefaultCompanionIntervalMs = 3500;
    private const int ResolveCacheLimit = 256;

    private readonly CompanionDialogueRegistry registry = new();
    private readonly Dictionary<string, DialogueMemory> memoryByCompanion = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> resolvedTextByContext = new(StringComparer.Ordinal);
    private readonly Queue<string> resolvedTextOrder = new();
    private readonly Dictionary<string, long> lastSharedEventByOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> lastSpeakByOwner = new(StringComparer.Ordinal);
    private readonly Action<IServerPlayer, Entity, CompanionDialogueSelection> send;

    public Action<Entity, CompanionDialogueSelection>? Emitted { get; set; }

    public CompanionDialogueService(Action<IServerPlayer, Entity, CompanionDialogueSelection> send)
    {
        this.send = send;
    }

    public CompanionDialogueRegistry Registry => registry;

    public void Load(ICoreAPI api)
    {
        registry.Load(api);
        resolvedTextByContext.Clear();
        resolvedTextOrder.Clear();
    }

    public string Resolve(
        string eventId,
        string fallback,
        CompanionDialogueContext context)
    {
        string cacheKey = BuildResolveCacheKey(eventId, context);
        if (resolvedTextByContext.TryGetValue(cacheKey, out string? cached)) return cached;

        string resolved = fallback;
        if (registry.TrySelect(
                context with { EventId = eventId },
                null,
                null,
                null,
                null,
                UtcNowMs(),
                out CompanionDialogueSelection? selection)
            && selection != null
            && !string.IsNullOrWhiteSpace(selection.Text))
        {
            resolved = selection.Text;
        }
        CacheResolvedText(cacheKey, resolved);
        return resolved;
    }

    public bool TryEmit(
        Entity entity,
        IServerPlayer owner,
        string eventId,
        CompanionDialogueContext context,
        string fallback,
        int fallbackPriority = 10,
        string fallbackSemanticGroup = "")
    {
        if (!entity.Alive || FeralKinshipCompanionSystem.IsFoxAwayFromWorld(entity)) return false;
        long now = UtcNowMs();
        string companionKey = string.IsNullOrWhiteSpace(context.FoxId)
            ? "entity:" + entity.EntityId
            : context.FoxId;
        DialogueMemory memory = GetMemory(companionKey);
        if (!registry.TrySelect(
                context with { EventId = eventId },
                memory.RecentLines,
                memory.RecentGroups,
                memory.UsedOnceEvents,
                memory.UsedOnceLines,
                now,
                out CompanionDialogueSelection? selection,
                memory.LastFollowText)
            || selection == null)
        {
            if (string.IsNullOrWhiteSpace(fallback)) return false;
            selection = new CompanionDialogueSelection
            {
                EventId = eventId,
                LineId = "legacy:" + eventId + ":" + fallback,
                Text = fallback,
                SemanticGroup = fallbackSemanticGroup,
                Priority = fallbackPriority,
                CooldownSeconds = 0d,
                ExpiresAtUtcMs = now + 10000L
            };
        }

        // Only bubble-channel selections may enter the floating-text queue.
        // UI and narrative entries remain available to Resolve() without
        // accidentally bypassing the existing renderer.
        if (!string.Equals(selection.Channel, "bubble", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(selection.Text))
        {
            if (string.IsNullOrWhiteSpace(fallback)) return false;
            selection = new CompanionDialogueSelection
            {
                EventId = eventId,
                LineId = "legacy:" + eventId + ":" + fallback,
                Text = fallback,
                Channel = "bubble",
                SemanticGroup = fallbackSemanticGroup,
                Priority = fallbackPriority,
                CooldownSeconds = 0d,
                ExpiresAtUtcMs = now + 10000L
            };
        }

        string ownerKey = owner.PlayerUID;
        long lastSpeak = lastSpeakByOwner.GetValueOrDefault(ownerKey);
        if (selection.Priority < CompanionDialoguePriority.High && now - lastSpeak < DefaultCompanionIntervalMs) return false;
        if (selection.Priority < CompanionDialoguePriority.High
            && memory.LastSpokeAtUtcMs > 0
            && now - memory.LastSpokeAtUtcMs < DefaultCompanionIntervalMs)
        {
            return false;
        }
        string sharedKey = ownerKey + "|" + selection.EventId + "|" + selection.SemanticGroup;
        if (selection.Priority < CompanionDialoguePriority.High
            && lastSharedEventByOwner.TryGetValue(sharedKey, out long sharedAt)
            && now - sharedAt < SharedEventSuppressionMs)
        {
            return false;
        }

        if (memory.LastByEvent.TryGetValue(selection.EventId, out long lastEvent)
            && selection.CooldownSeconds > 0d
            && now - lastEvent < selection.CooldownSeconds * 1000d)
        {
            return false;
        }

        if (selection.OnceOnlyEvent && memory.UsedOnceEvents.Contains(selection.EventId)) return false;
        if (selection.OnceOnlyVariant && memory.UsedOnceLines.Contains(selection.LineId)) return false;

        memory.Remember(selection, now);
        lastSpeakByOwner[ownerKey] = now;
        lastSharedEventByOwner[sharedKey] = now;
        send(owner, entity, selection);
        Emitted?.Invoke(entity, selection);
        return true;
    }

    public string Validate()
    {
        string[] personalities =
        {
            "Timid", "Bold", "Curious", "Affectionate", "Independent", "Playful", "Restless", "Homebody",
            "Social", "Solitary", "Protective", "Territorial", "Greedy", "Demanding", "Stubborn", "Skittish"
        };
        string[] eventIds = registry.CoverageEvents.Select(entry => entry.Id).ToArray();
        return registry.BuildValidationReport(personalities, eventIds);
    }

    private DialogueMemory GetMemory(string companionKey)
    {
        if (!memoryByCompanion.TryGetValue(companionKey, out DialogueMemory? memory))
        {
            memory = new DialogueMemory();
            memoryByCompanion[companionKey] = memory;
        }
        return memory;
    }

    private static long UtcNowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void CacheResolvedText(string key, string value)
    {
        if (resolvedTextByContext.ContainsKey(key))
        {
            resolvedTextByContext[key] = value;
            return;
        }
        resolvedTextByContext[key] = value;
        resolvedTextOrder.Enqueue(key);
        while (resolvedTextOrder.Count > ResolveCacheLimit)
        {
            string oldest = resolvedTextOrder.Dequeue();
            resolvedTextByContext.Remove(oldest);
        }
    }

    private static string BuildResolveCacheKey(string eventId, CompanionDialogueContext context)
    {
        string facts = string.Join(
            ";",
            context.Facts
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Key + "=" + pair.Value));
        return string.Join(
            "|",
            context.FoxId,
            eventId,
            context.Personality,
            context.SpeciesId,
            context.SpeciesSource,
            context.AgeStage,
            context.Mood,
            facts);
    }

    private sealed class DialogueMemory
    {
        public Queue<string> RecentLines { get; } = new();
        public Queue<string> RecentEvents { get; } = new();
        public Queue<string> RecentGroups { get; } = new();
        public HashSet<string> UsedOnceEvents { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> UsedOnceLines { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, long> LastByEvent { get; } = new(StringComparer.OrdinalIgnoreCase);
        public long LastSpokeAtUtcMs { get; private set; }
        public string? LastFollowText { get; private set; }

        public void Remember(CompanionDialogueSelection selection, long now)
        {
            RememberQueue(RecentLines, selection.LineId, RecentLineLimit);
            RememberQueue(RecentEvents, selection.EventId, RecentEventLimit);
            if (!string.IsNullOrWhiteSpace(selection.SemanticGroup))
                RememberQueue(RecentGroups, selection.SemanticGroup, RecentLineLimit);
            LastByEvent[selection.EventId] = now;
            LastSpokeAtUtcMs = now;
            if (selection.EventId.StartsWith("idle.follow.", StringComparison.Ordinal)) LastFollowText = selection.Text;
            if (selection.OnceOnlyEvent) UsedOnceEvents.Add(selection.EventId);
            if (selection.OnceOnlyVariant) UsedOnceLines.Add(selection.LineId);
        }

        private static void RememberQueue(Queue<string> queue, string value, int limit)
        {
            queue.Enqueue(value);
            while (queue.Count > limit) queue.Dequeue();
        }
    }
}

internal static class DialogueCoverage
{
    public static readonly string[] Personalities =
    {
        "Timid", "Bold", "Curious", "Affectionate", "Independent", "Playful", "Restless", "Homebody",
        "Social", "Solitary", "Protective", "Territorial", "Greedy", "Demanding", "Stubborn", "Skittish"
    };

    private static readonly HashSet<string> HookedEventSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "debug.fox_joke", "thought.mood",
        "request.predator", "request.stayclose", "request.stayaway", "request.inside",
        "request.nearownerstill", "request.traveldistance", "request.higherground",
        "request.nearwater", "request.nearlight", "request.nearheat", "request.shelter", "request.outsideclear",
        "request.neartamedanimal", "request.largetree", "request.cropfield", "request.trader",
        "request.mechanicaldevice", "request.anotheranimal", "request.regularfood", "request.luxuryfood",
        "request.nonfoodconsumed", "request.nonfoodnearby", "request.healingitem", "request.placeablenearby",
        "request.outsideuntilmorning", "request.insideuntilmorning"
    };

    private static readonly HashSet<string> SilentEventSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "debug.fox_joke", "combat.death", "expedition.return_missing",
        "exp.duty", "exp.kill", "exp.proximity", "exp.work_cart", "exp.expedition", "exp.milestone",
        "exp.adulthood"
    };

    public static List<CompanionDialogueSpeciesContract> CreateFallbackSpecies() => new()
    {
        new() { Id = "bear", Name = "Bear" },
        new() { Id = "chicken", Name = "Chicken" },
        new() { Id = "deer", Name = "Deer" },
        new() { Id = "fox", Name = "Fox" },
        new() { Id = "gazelle", Name = "Gazelle" },
        new() { Id = "goat", Name = "Goat" },
        new() { Id = "hare", Name = "Hare" },
        new() { Id = "hyena", Name = "Hyena" },
        new() { Id = "pig", Name = "Pig" },
        new() { Id = "raccoon", Name = "Raccoon" },
        new() { Id = "sheep", Name = "Sheep" },
        new() { Id = "wolf", Name = "Wolf" }
    };

    public static List<CompanionDialogueCoverageEvent> CreateFallbackCoverage() =>
        EventIds.Select(id => new CompanionDialogueCoverageEvent
        {
            Id = id,
            SpeechExpected = !SilentEventSet.Contains(id),
            TargetVariants = SilentEventSet.Contains(id) ? 0 : 3,
            Frequency = id.StartsWith("milestone.", StringComparison.OrdinalIgnoreCase) ? "once" : "future",
            Priority = id.StartsWith("combat.mortal", StringComparison.OrdinalIgnoreCase) ? "critical" : "low",
            Hooked = HookedEventSet.Contains(id),
            Notes = SilentEventSet.Contains(id) ? "Intentionally silent." : "Registered authoring slot."
        }).ToList();

    public static readonly string[] EventIds =
    {
        "debug.fox_joke",
        "request.predator", "request.stayclose", "request.stayaway", "request.inside",
        "request.nearownerstill", "request.traveldistance", "request.walkdistance", "request.higherground",
        "request.nearwater", "request.nearlight", "request.nearheat", "request.shelter", "request.outsideclear",
        "request.neartamedanimal", "request.largetree", "request.cropfield", "request.trader",
        "request.mechanicaldevice", "request.anotheranimal", "request.regularfood", "request.luxuryfood",
        "request.nonfoodconsumed", "request.nonfoodnearby", "request.healingitem", "request.placeablenearby",
        "request.outsideuntilmorning", "request.insideuntilmorning",
        "request_lifecycle.generated", "request_lifecycle.accepted", "request_lifecycle.nearly_complete",
        "request_lifecycle.completed", "request_lifecycle.failed", "request_lifecycle.cancelled",
        "request_lifecycle.cooldown", "request_lifecycle.invalid_target", "request_lifecycle.interrupted.danger",
        "request_lifecycle.interrupted.command", "request_lifecycle.interrupted.food", "request_lifecycle.interrupted.duty",
        "request_lifecycle.interrupted.expedition", "request_lifecycle.expired", "request_lifecycle.point_awarded",
        "request_lifecycle.pack_point_awarded", "request_lifecycle.repeated_after_failure",
        "thought.mood", "thought.personality",
        "command.follow.close", "command.follow.normal", "command.follow.back",
        "command.at_ease", "command.rest", "command.return_home",
        "command.combat.passive", "command.combat.defensive", "command.combat.protect",
        "command.combat.assist", "command.combat.aggressive", "command.combat.flee",
        "command.risk.cautious", "command.risk.steady", "command.risk.fearless",
        "command.attack_target", "command.clear_target", "command.drop_held_items", "command.stand_still",
        "command.clear_duties",
        "duty.ground_cleanup.enable", "duty.ground_cleanup.disable", "duty.dropped_items.enable", "duty.dropped_items.disable",
        "duty.cattails.enable", "duty.cattails.disable", "duty.flint.enable", "duty.flint.disable",
        "duty.sticks.enable", "duty.sticks.disable", "duty.rocks.enable", "duty.rocks.disable",
        "duty.crops.enable", "duty.crops.disable", "duty.berries.enable", "duty.berries.disable",
        "duty.mushrooms.enable", "duty.mushrooms.disable", "duty.flower_removal.enable", "duty.flower_removal.disable",
        "duty.snow_shoveling.enable", "duty.snow_shoveling.disable", "duty.snowball_collection.enable", "duty.snowball_collection.disable",
        "duty.mowing.enable", "duty.mowing.disable", "duty.general_sorting.enable", "duty.general_sorting.disable",
        "duty.breeding.enable", "duty.breeding.disable",
        "duty.ground_cleanup.begin", "duty.item.unreachable", "duty.item.pickup", "duty.storage.full",
        "duty.storage.incompatible", "duty.general_sort", "duty.crop_harvest.begin", "duty.berry_harvest.begin",
        "duty.mushroom_harvest.begin", "duty.flower_removal.begin", "duty.mowing.begin", "duty.snow.begin",
        "duty.work_cart.chore_complete", "duty.work_cart.no_jobs", "duty.work_cart.assign.success",
        "duty.work_cart.assign.failure.juvenile", "duty.work_cart.assign.failure.away", "duty.work_cart.assign.failure.dead",
        "duty.logging.scan_begin", "duty.logging.no_tree", "duty.logging.claim", "duty.logging.resume",
        "duty.logging.wait_debris", "duty.logging.abandon.changed", "duty.logging.abandon.unloaded",
        "duty.logging.fell_complete", "duty.logging.no_storage", "duty.logging.multifox_join",
        "duty.logging.restart_recovered",
        "food.hungry", "food.find", "food.eat.dining_board", "food.owner_fed", "food.owner_invalid",
        "food.owner_above_cutoff", "food.rejected_full", "food.leave_after_eating", "food.unreachable_source",
        "combat.predator_notice", "combat.request_help", "combat.enter", "combat.attack", "combat.hit",
        "combat.rallying_scent", "combat.warning_cry", "combat.retreat.cautious", "combat.retreat.steady",
        "combat.retreat.fearless", "combat.badly_wounded", "combat.mortally_wounded", "combat.mortal_warning",
        "combat.healing_required", "combat.recovery.begin", "combat.recovery.complete", "combat.refuses_to_die",
        "combat.one_more_breath", "combat.death", "combat.archive", "combat.unarchive",
        "den.select", "den.move", "den.arrive", "den.settle", "den.wake.later_bedtime", "den.wake.earlier_morning",
        "family.pregnancy.start", "family.pregnancy.penalty", "family.birth", "family.newborn_named",
        "family.juvenile.seek_parent", "family.juvenile.parent_missing", "family.juvenile.panic",
        "family.child_adult", "family.proximity_exp", "family.breeding.enable", "family.breeding.disable",
        "family.breeding.unsupported",
        "pack.join", "pack.expedition.selected", "expedition.depart", "expedition.drop_held",
        "expedition.away_no_duties", "expedition.return_on_time", "expedition.return_late",
        "expedition.return_cargo", "expedition.cargo_abandoned", "expedition.return_injured",
        "expedition.return_mortally_wounded", "expedition.return_missing", "expedition.search.recoverable",
        "expedition.search.unrecoverable", "expedition.search.success", "expedition.search.failure",
        "expedition.recruitment.success", "expedition.recruitment.failure", "expedition.recruitment.cache_ready",
        "expedition.recruitment.wild_claimed", "expedition.patrol.prepare", "expedition.cache.full",
        "pack.member.archived", "pack.member.located",
        "milestone.snow", "milestone.mature_crop", "milestone.mechanical_piece", "milestone.corrupt_sawblade_locust",
        "milestone.double_headed_drifter", "milestone.bellhead_shiver", "milestone.gearfoot_bowtorn",
        "milestone.bell", "milestone.hidden_boss", "milestone.trader", "milestone.zero_temporal_stability",
        "milestone.anvil", "milestone.copper_ore", "milestone.tin_ore", "milestone.iron_ore",
        "milestone.gold_ore", "milestone.bountiful_ore", "milestone.gem", "milestone.natural_beehive",
        "exp.level_gain", "exp.duty", "exp.kill", "exp.proximity", "exp.work_cart", "exp.expedition",
        "exp.milestone", "exp.adulthood"
    };
}
