using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vintagestory.API.Common;

namespace AnimalicaBodyTools.Systems;

public static class AnimalClassPowerSystem
{
    private const string CoreConfigFileName = "AnimalicaCore.json";

    public static void Apply(ICoreAPI api)
    {
        string mode = LoadClassPowerMode(api);
        if (!string.Equals(mode, "Heroic", StringComparison.Ordinal))
        {
            api.Logger.Notification("[animalicabodytools] Animal class power mode Balanced: using baseline animal class trait definitions.");
            return;
        }

        int patched = PatchClassTraits(api, HeroicClassTraitAttributes);
        api.Logger.Notification("[animalicabodytools] Animal class power mode Heroic: patched {0} animal class trait definition(s).", patched);
    }

    private static string LoadClassPowerMode(ICoreAPI api)
    {
        try
        {
            AnimalicaCoreConfigProxy config = api.LoadModConfig<AnimalicaCoreConfigProxy>(CoreConfigFileName) ?? new AnimalicaCoreConfigProxy();
            return string.Equals(config.AnimalClassPowerMode, "Heroic", StringComparison.OrdinalIgnoreCase) ? "Heroic" : "Balanced";
        }
        catch (Exception exception)
        {
            api.Logger.Warning("[animalicabodytools] Could not read {0}; using Balanced class power. {1}", CoreConfigFileName, exception.Message);
            return "Balanced";
        }
    }

    private static int PatchClassTraits(ICoreAPI api, Dictionary<string, Dictionary<string, double>> presets)
    {
        IAsset? traitAsset = api.Assets.TryGet(new AssetLocation("animalicabodytools:config/traits.json"), true);
        if (traitAsset?.Data == null || traitAsset.Data.Length == 0)
        {
            return 0;
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(Encoding.UTF8.GetString(traitAsset.Data));
        }
        catch (JsonException exception)
        {
            api.Logger.Warning("[animalicabodytools] Could not read animalicabodytools:config/traits.json for class power patching: {0}", exception.Message);
            return 0;
        }

        if (parsed is not JsonArray traits)
        {
            return 0;
        }

        int patched = 0;
        foreach (JsonObject trait in traits.OfType<JsonObject>())
        {
            string code = trait["code"]?.GetValue<string>() ?? string.Empty;
            if (!presets.TryGetValue(code, out Dictionary<string, double>? attributes))
            {
                continue;
            }

            trait["attributes"] = ToJsonObject(attributes);
            patched++;
        }

        if (patched > 0)
        {
            traitAsset.Data = Encoding.UTF8.GetBytes(traits.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            traitAsset.IsPatched = true;
            PatchLang(api, presets);
        }

        return patched;
    }

    private static void PatchLang(ICoreAPI api, Dictionary<string, Dictionary<string, double>> presets)
    {
        IAsset? langAsset = api.Assets.TryGet(new AssetLocation("game:lang/en.json"), true);
        if (langAsset?.Data == null || langAsset.Data.Length == 0)
        {
            return;
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(Encoding.UTF8.GetString(langAsset.Data));
        }
        catch (JsonException exception)
        {
            api.Logger.Warning("[animalicabodytools] Could not read game:lang/en.json for class power patching: {0}", exception.Message);
            return;
        }

        if (parsed is not JsonObject lang)
        {
            return;
        }

        foreach ((string code, Dictionary<string, double> attributes) in presets)
        {
            string description = BuildTraitDescription(attributes);
            lang[$"traitdesc-{code}"] = description;
            lang[$"game:traitdesc-{code}"] = description;

            foreach ((string attribute, double value) in attributes)
            {
                string key = $"charattribute-{attribute}-{FormatKeyNumber(value)}";
                string text = FormatAttributeDescription(attribute, value);
                lang[key] = text;
                lang[$"game:{key}"] = text;
            }
        }

        langAsset.Data = Encoding.UTF8.GetBytes(lang.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        langAsset.IsPatched = true;
    }

    private static JsonObject ToJsonObject(Dictionary<string, double> attributes)
    {
        JsonObject result = [];
        foreach ((string attribute, double value) in attributes)
        {
            result[attribute] = value;
        }

        return result;
    }

    private static string BuildTraitDescription(Dictionary<string, double> attributes)
    {
        return string.Join(", ", AttributeDisplayOrder
            .Where(attributes.ContainsKey)
            .Concat(attributes.Keys.Where(key => !AttributeDisplayOrder.Contains(key)))
            .Select(key => FormatAttributeDescription(key, attributes[key])));
    }

    private static string FormatAttributeDescription(string attribute, double value)
    {
        return attribute switch
        {
            "maxhealthExtraPoints" => FormatHealth(value),
            "animalicaNaturalProtection" => $"+{FormatDisplayNumber(value)} flat physical protection",
            "warmthBonus" => FormatWarmth(value),
            "walkspeed" => FormatPercent(value, "walk speed"),
            "sprintSpeed" => FormatPercent(value, "sprint speed"),
            "sneakSpeed" => FormatPercent(value, "sneak speed"),
            "jumpHeightMul" => FormatPercent(value, "jump height"),
            "fallDamageFactor" => FormatPercent(value, "fall damage"),
            "animalSeekingRange" => FormatPercent(value, "animal detection range"),
            "meleeWeaponsDamage" => FormatPercent(value, "bite damage"),
            "vegetableNutritionFactor" => FormatPercent(value, "vegetable nutrition"),
            "grainNutritionFactor" => FormatPercent(value, "grain nutrition"),
            "forageDropRate" => FormatPercent(value, "loot from foraging"),
            "wildCropDropRate" => FormatPercent(value, "wild crop drop rate"),
            "vesselContentsDropRate" => FormatPercent(value, "loot from cracked vessels"),
            "rustyGearDropRate" => FormatPercent(value, "rusty gear drop rate"),
            "animalLootDropRate" => FormatPercent(value, "animal loot"),
            "animalHarvestingTime" => FormatPercent(-value, "animal harvesting speed"),
            "oreDropRate" => FormatPercent(value, "ore drop rate"),
            "miningSpeedMul" => FormatPercent(value, "mining speed"),
            "armorDurabilityLoss" => FormatPercent(value, "armor durability loss"),
            "armorWalkSpeedAffectedness" => FormatPercent(value, "armor walk speed penalty"),
            "hungerrate" => FormatPercent(value, "hunger rate"),
            _ => FormatPercent(value, attribute)
        };
    }

    private static string FormatHealth(double value)
    {
        int points = (int)Math.Round(value);
        string sign = points > 0 ? "+" : string.Empty;
        string pointWord = Math.Abs(points) == 1 ? "point" : "points";
        return $"{sign}{points} health {pointWord}";
    }

    private static string FormatWarmth(double value)
    {
        string sign = value > 0 ? "+" : string.Empty;
        return $"{sign}{FormatDisplayNumber(value)}°C bonus warmth";
    }

    private static string FormatPercent(double value, string label)
    {
        double percent = value * 100;
        string sign = percent > 0 ? "+" : string.Empty;
        return $"{sign}{FormatDisplayNumber(percent)}% {label}";
    }

    private static string FormatDisplayNumber(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string FormatKeyNumber(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static readonly string[] AttributeDisplayOrder =
    [
        "maxhealthExtraPoints",
        "animalicaNaturalProtection",
        "warmthBonus",
        "walkspeed",
        "sprintSpeed",
        "sneakSpeed",
        "jumpHeightMul",
        "fallDamageFactor",
        "animalSeekingRange",
        "meleeWeaponsDamage",
        "vegetableNutritionFactor",
        "grainNutritionFactor",
        "forageDropRate",
        "wildCropDropRate",
        "vesselContentsDropRate",
        "rustyGearDropRate",
        "animalLootDropRate",
        "animalHarvestingTime",
        "oreDropRate",
        "miningSpeedMul",
        "armorDurabilityLoss",
        "armorWalkSpeedAffectedness",
        "hungerrate"
    ];

    private static readonly Dictionary<string, Dictionary<string, double>> HeroicClassTraitAttributes = new(StringComparer.Ordinal)
    {
        ["animalica-class-hunter-gifts"] = new(StringComparer.Ordinal)
        {
            ["animalLootDropRate"] = 0.35,
            ["animalHarvestingTime"] = -0.35,
            ["animalSeekingRange"] = -0.4,
            ["meleeWeaponsDamage"] = 0.1
        },
        ["animalica-class-hunter-drawbacks"] = new(StringComparer.Ordinal)
        {
            ["miningSpeedMul"] = -0.25,
            ["oreDropRate"] = -0.1
        },
        ["animalica-class-stalker-gifts"] = new(StringComparer.Ordinal)
        {
            ["sneakSpeed"] = 0.35,
            ["animalSeekingRange"] = -0.6,
            ["meleeWeaponsDamage"] = 0.25,
            ["sprintSpeed"] = 0.15
        },
        ["animalica-class-stalker-drawbacks"] = new(StringComparer.Ordinal)
        {
            ["maxhealthExtraPoints"] = -3,
            ["miningSpeedMul"] = -0.35
        },
        ["animalica-class-brute-gifts"] = new(StringComparer.Ordinal)
        {
            ["maxhealthExtraPoints"] = 6,
            ["meleeWeaponsDamage"] = 0.35,
            ["armorDurabilityLoss"] = -0.2
        },
        ["animalica-class-brute-drawbacks"] = new(StringComparer.Ordinal)
        {
            ["hungerrate"] = 0.3,
            ["sneakSpeed"] = -0.2,
            ["forageDropRate"] = -0.15
        },
        ["animalica-class-guardian-gifts"] = new(StringComparer.Ordinal)
        {
            ["maxhealthExtraPoints"] = 8,
            ["animalicaNaturalProtection"] = 1.5,
            ["armorDurabilityLoss"] = -0.35,
            ["armorWalkSpeedAffectedness"] = -0.3,
            ["fallDamageFactor"] = -0.25
        },
        ["animalica-class-guardian-drawbacks"] = new(StringComparer.Ordinal)
        {
            ["hungerrate"] = 0.25,
            ["sprintSpeed"] = -0.2,
            ["sneakSpeed"] = -0.2
        },
        ["animalica-class-runner-gifts"] = new(StringComparer.Ordinal)
        {
            ["walkspeed"] = 0.2,
            ["sprintSpeed"] = 0.35,
            ["jumpHeightMul"] = 0.25,
            ["fallDamageFactor"] = -0.35
        },
        ["animalica-class-runner-drawbacks"] = new(StringComparer.Ordinal)
        {
            ["maxhealthExtraPoints"] = -3,
            ["miningSpeedMul"] = -0.3,
            ["hungerrate"] = 0.15
        },
        ["animalica-class-mountaineer-gifts"] = new(StringComparer.Ordinal)
        {
            ["sprintSpeed"] = 0.2,
            ["jumpHeightMul"] = 0.35,
            ["fallDamageFactor"] = -0.6,
            ["warmthBonus"] = 3,
            ["miningSpeedMul"] = 0.1
        },
        ["animalica-class-mountaineer-drawbacks"] = new(StringComparer.Ordinal)
        {
            ["forageDropRate"] = -0.2,
            ["animalLootDropRate"] = -0.15
        },
        ["animalica-class-miner-gifts"] = new(StringComparer.Ordinal)
        {
            ["maxhealthExtraPoints"] = 3,
            ["miningSpeedMul"] = 0.75,
            ["oreDropRate"] = 0.35
        },
        ["animalica-class-miner-drawbacks"] = new(StringComparer.Ordinal)
        {
            ["animalLootDropRate"] = -0.35,
            ["animalHarvestingTime"] = 0.35,
            ["sprintSpeed"] = -0.2
        },
        ["animalica-class-digger-gifts"] = new(StringComparer.Ordinal)
        {
            ["miningSpeedMul"] = 0.5,
            ["forageDropRate"] = 0.35,
            ["fallDamageFactor"] = -0.35
        },
        ["animalica-class-digger-drawbacks"] = new(StringComparer.Ordinal)
        {
            ["sprintSpeed"] = -0.2,
            ["meleeWeaponsDamage"] = -0.2
        },
        ["animalica-class-forager-gifts"] = new(StringComparer.Ordinal)
        {
            ["forageDropRate"] = 0.5,
            ["wildCropDropRate"] = 0.35,
            ["hungerrate"] = -0.25,
            ["vegetableNutritionFactor"] = 0.15,
            ["grainNutritionFactor"] = 0.15
        },
        ["animalica-class-forager-drawbacks"] = new(StringComparer.Ordinal)
        {
            ["animalLootDropRate"] = -0.35,
            ["meleeWeaponsDamage"] = -0.2
        },
        ["animalica-class-fieldhand-gifts"] = new(StringComparer.Ordinal)
        {
            ["wildCropDropRate"] = 0.5,
            ["forageDropRate"] = 0.25,
            ["vegetableNutritionFactor"] = 0.25,
            ["grainNutritionFactor"] = 0.25,
            ["hungerrate"] = -0.2
        },
        ["animalica-class-fieldhand-drawbacks"] = new(StringComparer.Ordinal)
        {
            ["meleeWeaponsDamage"] = -0.3,
            ["animalLootDropRate"] = -0.35
        },
        ["animalica-class-scavenger-gifts"] = new(StringComparer.Ordinal)
        {
            ["rustyGearDropRate"] = 0.35,
            ["vesselContentsDropRate"] = 0.35,
            ["forageDropRate"] = 0.25,
            ["animalLootDropRate"] = 0.25
        },
        ["animalica-class-scavenger-drawbacks"] = new(StringComparer.Ordinal)
        {
            ["miningSpeedMul"] = -0.25,
            ["meleeWeaponsDamage"] = -0.2,
            ["hungerrate"] = 0.15
        }
    };

    private sealed class AnimalicaCoreConfigProxy
    {
        public string AnimalClassPowerMode { get; set; } = "Balanced";
    }
}
