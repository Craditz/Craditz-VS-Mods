using System;

namespace FeralKinshipCompanions;

/// <summary>
/// Pure asset-code recognition for first-seen Companion milestones. Keeping
/// these checks independent of world state makes the block/item distinctions
/// testable without launching Vintage Story.
/// </summary>
public static class CompanionMilestoneRules
{
    private static readonly string[] GradedOreGrades = { "poor", "medium", "rich", "bountiful" };
    private static readonly string[] CopperOreTypes = { "nativecopper", "malachite" };
    private static readonly string[] IronOreTypes = { "limonite", "hematite", "magnetite" };

    public static bool IsAnvilBlockPath(string? path) =>
        HasPathPrefix(path, "anvil-");

    public static bool IsCopperOreBlockPath(string? path) =>
        IsGradedOreOfType(path, CopperOreTypes);

    public static bool IsTinOreBlockPath(string? path) =>
        IsGradedOreOfType(path, "cassiterite");

    public static bool IsIronOreBlockPath(string? path) =>
        IsGradedOreOfType(path, IronOreTypes);

    public static bool IsGoldOreBlockPath(string? path) =>
        IsGradedOreOfType(path, "quartz_nativegold", "nativegold");

    public static bool IsBountifulOreBlockPath(string? path) =>
        HasPathPrefix(path, "ore-bountiful-");

    public static bool IsGemItemPath(string? path) =>
        HasPathPrefix(path, "gem-");

    public static bool IsNaturalBeehiveBlockPath(string? path) =>
        string.Equals(path, "wildbeehive", StringComparison.OrdinalIgnoreCase)
        || HasPathPrefix(path, "wildbeehive-");

    public static bool IsVisibleItemDisplayBlockPath(string? path) =>
        string.Equals(path, "groundstorage", StringComparison.OrdinalIgnoreCase)
        || HasPathPrefix(path, "groundstorage-")
        || string.Equals(path, "displaycase", StringComparison.OrdinalIgnoreCase)
        || HasPathPrefix(path, "displaycase-")
        || string.Equals(path, "shelf", StringComparison.OrdinalIgnoreCase)
        || HasPathPrefix(path, "shelf-");

    private static bool IsGradedOreOfType(string? path, params string[] oreTypes)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string[] parts = path.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4
            || !string.Equals(parts[0], "ore", StringComparison.OrdinalIgnoreCase)
            || Array.FindIndex(GradedOreGrades, grade =>
                string.Equals(grade, parts[1], StringComparison.OrdinalIgnoreCase)) < 0)
        {
            return false;
        }

        return Array.FindIndex(oreTypes, oreType =>
            string.Equals(oreType, parts[2], StringComparison.OrdinalIgnoreCase)) >= 0;
    }

    private static bool HasPathPrefix(string? path, string prefix) =>
        !string.IsNullOrWhiteSpace(path)
        && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
}
