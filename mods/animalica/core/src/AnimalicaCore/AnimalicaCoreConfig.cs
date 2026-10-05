using Newtonsoft.Json;

namespace AnimalicaCore;

public sealed class AnimalicaCoreConfig
{
    [JsonProperty("enableRaceTraits")]
    public bool EnableRaceTraits { get; set; } = true;

    [JsonProperty("enableSizeOverrides")]
    public bool EnableSizeOverrides { get; set; } = true;

    [JsonProperty("sizeMinimum")]
    public double SizeMinimum { get; set; } = 0.5;

    [JsonProperty("sizeMaximum")]
    public double SizeMaximum { get; set; } = 3.0;

    [JsonProperty("defaultSize")]
    public double DefaultSize { get; set; } = 1.0;

    [JsonProperty("keepSeraphEyeHeight")]
    public bool KeepSeraphEyeHeight { get; set; } = false;

    [JsonProperty("keepSeraphEyeHeightDescription")]
    public string KeepSeraphEyeHeightDescription { get; set; } = "Client-only accessibility option. When enabled, Animalica models use the default Seraph eye level without changing the selected model size.";

    [JsonProperty("useDefaultSwimmingDepth")]
    public bool UseDefaultSwimmingDepth { get; set; } = false;

    [JsonProperty("useDefaultSwimmingDepthDescription")]
    public string UseDefaultSwimmingDepthDescription { get; set; } = "Server-authoritative compatibility option. When enabled, Animalica players use the vanilla two-block swimming threshold, so held torches remain lit in one-block water.";

    [JsonProperty("animalBalanceMode")]
    public string AnimalBalanceMode { get; set; } = "Balanced";

    [JsonProperty("animalBalanceModeDescription")]
    public string AnimalBalanceModeDescription { get; set; } = "Options: Balanced, Distinct, Wild. Balanced uses the normal Core trait values. Distinct makes species traits stronger and more specialized while staying grounded. Wild prioritizes animal identity and comparison-to-human extremes over balance.";

    [JsonProperty("animalClassPowerMode")]
    public string AnimalClassPowerMode { get; set; } = "Balanced";

    [JsonProperty("animalClassPowerModeDescription")]
    public string AnimalClassPowerModeDescription { get; set; } = "Options: Balanced, Heroic. Balanced keeps Animalica Body Tools classes near vanilla class strength. Heroic is intended for worlds using stronger modded class packs and gives animal classes larger bonuses with sharper drawbacks. This setting only does anything when Animalica Body Tools is installed.";

    [JsonProperty("enableAnimalFoodNutrition")]
    public bool EnableAnimalFoodNutrition { get; set; } = true;

    [JsonProperty("enableAnimalFoodNutritionDescription")]
    public string EnableAnimalFoodNutritionDescription { get; set; } = "Server-authoritative switch for Animalica's special animal foods. When disabled, Animalica models cannot eat recognized animal foods; ordinary foods keep their normal game rules.";

    [JsonProperty("muzzleMode")]
    public bool MuzzleMode { get; set; } = false;

    [JsonProperty("muzzleModeDescription")]
    public string MuzzleModeDescription { get; set; } = "Server-authoritative compatibility switch. When enabled, Core disables the Animalica vocalization hotkeys and rejects vocalization requests.";

    [JsonProperty("oldSit")]
    public bool OldSit { get; set; } = false;

    [JsonProperty("oldSitDescription")]
    public string OldSitDescription { get; set; } = "Server-authoritative compatibility switch. When enabled, Core leaves vanilla sitting behavior in place and disables Core's sit, lay, and posture eye-height changes.";
}
