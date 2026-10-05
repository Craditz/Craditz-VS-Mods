#nullable enable

using System;
using System.Linq;

namespace FeralKinshipCompanions;

internal static class CompanionActivityMode
{
    public const string Follow = "follow";
    public const string AtEase = "atease";
    public const string Rest = "rest";
    public const string ReturnHome = "returnhome";

    public static bool IsValid(string? value) => value is Follow or AtEase or Rest or ReturnHome;

    public static string Normalize(string? value) => IsValid(value?.ToLowerInvariant())
        ? value!.ToLowerInvariant()
        : AtEase;

    public static string DisplayName(string? value) => Normalize(value) switch
    {
        Follow => "Follow",
        Rest => "Rest",
        ReturnHome => "Return Home",
        _ => "At Ease"
    };
}

internal static class CompanionDuty
{
    public const string GroundCleanup = "groundcleanup";
    public const string GroundDroppedItems = "ground-dropped";
    public const string GroundCattails = "ground-cattails";
    public const string GroundFlint = "ground-flint";
    public const string GroundSticks = "ground-sticks";
    public const string GroundBoulders = "ground-boulders";
    public const string GroundRocks = "ground-rocks";
    public const string MowLawn = "mowlawn";
    public const string GatherFinishedProducts = "gatherfinishedproducts";
    public const string FinishedCrops = "finished-crops";
    public const string FinishedBerries = "finished-berries";
    public const string FinishedMushrooms = "finished-mushrooms";
    public const string FlowerRemoval = "flowerremoval";
    public const string SnowShoveling = "snowshoveling";
    public const string CharcoalShoveling = "charcoalshoveling";
    public const string SnowballCollection = "snowballs";
    public const string StorageSorting = "storagesorting";

    public static readonly string[] Subtasks =
    {
        GroundDroppedItems, GroundCattails, GroundFlint, GroundSticks, GroundBoulders, GroundRocks,
        FinishedCrops, FinishedBerries, FinishedMushrooms, FlowerRemoval, SnowShoveling, SnowballCollection
    };

    public static bool IsSubtask(string? value) => Subtasks.Contains(value ?? string.Empty, StringComparer.Ordinal);

    public static string OptionKey(string option) => option switch
    {
        GroundDroppedItems => "feralKinshipGroundDroppedItemsEnabled",
        GroundCattails => "feralKinshipGroundCattailsEnabled",
        GroundFlint => "feralKinshipGroundFlintEnabled",
        GroundSticks => "feralKinshipGroundSticksEnabled",
        GroundBoulders => "feralKinshipGroundBouldersEnabled",
        GroundRocks => "feralKinshipGroundRocksEnabled",
        FinishedCrops => "feralKinshipFinishedCropsEnabled",
        FinishedBerries => "feralKinshipFinishedBerriesEnabled",
        FinishedMushrooms => "feralKinshipFinishedMushroomsEnabled",
        FlowerRemoval => "feralKinshipFlowerRemovalEnabled",
        SnowShoveling => "feralKinshipSnowShovelingEnabled",
        SnowballCollection => "feralKinshipSnowballCollectionEnabled",
        _ => string.Empty
    };

    public static string DisplaySubtaskName(string? value) => value switch
    {
        GroundDroppedItems => "Dropped items",
        GroundCattails => "Cattails",
        GroundFlint => "Flint",
        GroundSticks => "Sticks",
        GroundBoulders => "Boulders",
        GroundRocks => "Rocks",
        FinishedCrops => "Crops",
        FinishedBerries => "Berries",
        FinishedMushrooms => "Mushrooms",
        FlowerRemoval => "Flower removal",
        SnowShoveling => "Snow shoveling",
        SnowballCollection => "Snowball collection",
        StorageSorting => "Sort storage",
        _ => "Duty option"
    };

    public const int GroundDroppedItemsBit = 1 << 0;
    public const int GroundCattailsBit = 1 << 1;
    public const int GroundFlintBit = 1 << 2;
    public const int GroundSticksBit = 1 << 3;
    public const int GroundBouldersBit = 1 << 4;
    public const int GroundRocksBit = 1 << 5;
    public const int MowLawnBit = 1 << 6;
    public const int FinishedCropsBit = 1 << 7;
    public const int FinishedBerriesBit = 1 << 8;
    public const int FinishedMushroomsBit = 1 << 9;
    public const int FlowerRemovalBit = 1 << 10;
    public const int SnowShovelingBit = 1 << 11;
    public const int SnowballCollectionBit = 1 << 12;
    // Keep the storage bit after the original snowball bit so existing
    // custom-duty commands remain compatible with saved whistle selections.
    public const int StorageSortingBit = 1 << 13;
    public const int CharcoalShovelingBit = 1 << 14;
    public const int AllDutiesMask = (1 << 15) - 1;
    public const int AllExceptSnowballsMask = AllDutiesMask & ~SnowballCollectionBit;

    public static int GetDutyMask(string? value)
    {
        return value switch
        {
            GroundCleanup => GroundDroppedItemsBit | GroundCattailsBit | GroundFlintBit
                | GroundSticksBit | GroundBouldersBit | GroundRocksBit,
            GroundDroppedItems => GroundDroppedItemsBit,
            GroundCattails => GroundCattailsBit,
            GroundFlint => GroundFlintBit,
            GroundSticks => GroundSticksBit,
            GroundBoulders => GroundBouldersBit,
            GroundRocks => GroundRocksBit,
            MowLawn => MowLawnBit,
            GatherFinishedProducts => FinishedCropsBit | FinishedBerriesBit | FinishedMushroomsBit,
            FinishedCrops => FinishedCropsBit,
            FinishedBerries => FinishedBerriesBit,
            FinishedMushrooms => FinishedMushroomsBit,
            FlowerRemoval => FlowerRemovalBit,
            SnowShoveling => SnowShovelingBit,
            CharcoalShoveling => CharcoalShovelingBit,
            StorageSorting => StorageSortingBit,
            "all" => AllExceptSnowballsMask,
            _ => 0
        };
    }

    public static bool IsSet(int mask, int bit) => (mask & bit) != 0;

    public static bool IsValid(string? value) => value is GroundCleanup or MowLawn or GatherFinishedProducts or FlowerRemoval or SnowShoveling or CharcoalShoveling;

    public static string DisplayName(string? value) => value == GroundCleanup
        ? "Ground Cleanup"
        : value == MowLawn
            ? "Mow the Lawn"
            : value == GatherFinishedProducts
                ? "Gather Finished Products"
                : value == FlowerRemoval
                    ? "Flower Removal"
                    : value == SnowShoveling
                        ? "Snow Shoveling"
                        : value == CharcoalShoveling
                            ? "Charcoal Shoveling"
                        : value == StorageSorting
                            ? "Sort storage"
        : "Unknown duty";
}

internal static class CompanionFollowDistance
{
    public const string Close = "close";
    public const string Normal = "normal";
    public const string Back = "back";

    public static bool IsValid(string? value) => value is Close or Normal or Back;

    public static string Normalize(string? value) => IsValid(value?.ToLowerInvariant())
        ? value!.ToLowerInvariant()
        : Normal;

    public static string DisplayName(string? value) => Normalize(value) switch
    {
        Close => "Close",
        Back => "Back",
        _ => "Normal"
    };

    public static void GetRing(string? value, out float innerRadius, out float outerRadius)
    {
        switch (Normalize(value))
        {
            case Close:
                innerRadius = 2.25f;
                outerRadius = 3.5f;
                break;
            case Back:
                innerRadius = 7f;
                outerRadius = 9f;
                break;
            default:
                innerRadius = 4.5f;
                outerRadius = 6f;
                break;
        }
    }
}

internal static class CompanionCombatStyle
{
    public const string Passive = "passive";
    public const string Defensive = "defensive";
    public const string Protect = "protect";
    public const string Assist = "assist";
    public const string Aggressive = "aggressive";
    public const string Flee = "flee";

    public static bool IsValid(string? value) => value is Passive or Defensive or Protect or Assist or Aggressive or Flee;

    public static string Normalize(string? value) => IsValid(value?.ToLowerInvariant())
        ? value!.ToLowerInvariant()
        : Defensive;

    public static string DisplayName(string? value) => Normalize(value) switch
    {
        Passive => "Passive",
        Protect => "Protect",
        Assist => "Assist",
        Aggressive => "Aggressive",
        Flee => "Flee",
        _ => "Defensive"
    };
}

internal static class CompanionRiskTolerance
{
    public const string Cautious = "cautious";
    public const string Steady = "steady";
    public const string Fearless = "fearless";

    public static bool IsValid(string? value) => value is Cautious or Steady or Fearless;

    public static string Normalize(string? value) => IsValid(value?.ToLowerInvariant())
        ? value!.ToLowerInvariant()
        : Steady;

    public static string DisplayName(string? value) => Normalize(value) switch
    {
        Cautious => "Cautious",
        Fearless => "Fearless",
        _ => "Steady"
    };

    public static float RetreatHealthFraction(string? value) => Normalize(value) switch
    {
        Cautious => 0.40f,
        Fearless => 0f,
        _ => 0.20f
    };
}
