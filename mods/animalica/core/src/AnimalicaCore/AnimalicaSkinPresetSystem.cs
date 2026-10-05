using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace AnimalicaCore;

/// <summary>
/// Adds data-driven, multi-part appearance presets to PlayerModelLib.
/// Animal packs own their preset definitions under config/animalica/skinpresets;
/// Core only supplies the generic bridge to PML's existing preselection API.
/// </summary>
public sealed class AnimalicaSkinPresetSystem : ModSystem
{
    private const string HarmonyId = "craditz.animalicacore.skinpresets";
    private const string PresetAssetPath = "config/animalica/skinpresets";

    private static readonly Dictionary<string, AnimalicaSkinPresetSelector> Selectors =
        new(StringComparer.Ordinal);

    [ThreadStatic]
    private static AnimalicaSkinPresetSelector? applyingPresetSelector;

    [ThreadStatic]
    private static AnimalicaSkinPresetSelector? selectingModelSelector;

    private static MethodInfo? randomizeSkinMethod;
    private static MethodInfo? appliedSkinPartsMethod;
    private static ILogger? logger;
    private Harmony? harmony;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void AssetsLoaded(ICoreAPI api)
    {
        Selectors.Clear();

        foreach (IAsset asset in api.Assets.GetMany(PresetAssetPath, null, true)
                     .OrderBy(value => value.Location.ToString(), StringComparer.Ordinal))
        {
            AnimalicaSkinPresetDocument? document;
            try
            {
                document = JsonConvert.DeserializeObject<AnimalicaSkinPresetDocument>(asset.ToText());
            }
            catch (Exception exception)
            {
                api.Logger.Error("[AnimalicaCore] Could not read skin preset file {0}: {1}", asset.Location, exception.Message);
                continue;
            }

            if (document?.Selectors == null)
            {
                continue;
            }

            foreach ((string partCode, AnimalicaSkinPresetSelector selector) in document.Selectors)
            {
                selector.PartCode = partCode;
                if (!ValidateSelector(selector, asset.Location, api.Logger))
                {
                    continue;
                }

                if (Selectors.ContainsKey(partCode))
                {
                    api.Logger.Warning(
                        "[AnimalicaCore] Skin preset selector '{0}' is defined more than once; {1} takes precedence.",
                        partCode,
                        asset.Location
                    );
                }

                Selectors[partCode] = selector;
            }
        }

        api.Logger.Notification(
            "[AnimalicaCore] Loaded {0} data-driven PML appearance preset selector(s).",
            Selectors.Count
        );
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        Type? dialogType = AccessTools.TypeByName("PlayerModelLib.GuiDialogCreateCustomCharacter");
        Type? skinBehaviorType = AccessTools.TypeByName("PlayerModelLib.PlayerSkinBehavior");
        MethodInfo? toggleMethod = dialogType == null
            ? null
            : AccessTools.Method(dialogType, "OnToggleSkinPartColor", new[] { typeof(string), typeof(string) });
        randomizeSkinMethod = dialogType == null
            ? null
            : AccessTools.Method(dialogType, "OnRandomizeSkin", new[] { typeof(Dictionary<string, string>) });
        MethodInfo? randomizeMethod = skinBehaviorType == null
            ? null
            : AccessTools.Method(skinBehaviorType, "RandomizeSkin", new[]
            {
                typeof(Entity), typeof(Dictionary<string, string>), typeof(bool)
            });
        appliedSkinPartsMethod = skinBehaviorType == null
            ? null
            : AccessTools.Method(skinBehaviorType, "GetAppliedSkinParts");
        MethodInfo? toggleModelMethod = dialogType == null
            ? null
            : AccessTools.Method(dialogType, "OnToggleModel", new[] { typeof(string), typeof(GuiComposer) });
        MethodInfo? togglePrefix = AccessTools.Method(typeof(AnimalicaSkinPresetSystem), nameof(BeforeToggleSkinPart));
        MethodInfo? randomizePrefix = AccessTools.Method(typeof(AnimalicaSkinPresetSystem), nameof(BeforeRandomizeSkin));
        MethodInfo? modelPrefix = AccessTools.Method(typeof(AnimalicaSkinPresetSystem), nameof(BeforeToggleModel));
        MethodInfo? modelPostfix = AccessTools.Method(typeof(AnimalicaSkinPresetSystem), nameof(AfterToggleModel));

        if (toggleMethod == null || randomizeSkinMethod == null || randomizeMethod == null
            || appliedSkinPartsMethod == null || toggleModelMethod == null
            || togglePrefix == null || randomizePrefix == null || modelPrefix == null || modelPostfix == null)
        {
            api.Logger.Warning("[AnimalicaCore] PML appearance preset hook was unavailable; preset selectors will act as ordinary selectors.");
            return;
        }

        logger = api.Logger;
        harmony = new Harmony(HarmonyId);
        harmony.Patch(toggleMethod, prefix: new HarmonyMethod(togglePrefix));
        harmony.Patch(randomizeMethod, prefix: new HarmonyMethod(randomizePrefix));
        harmony.Patch(toggleModelMethod, prefix: new HarmonyMethod(modelPrefix), postfix: new HarmonyMethod(modelPostfix));
        api.Logger.Notification("[AnimalicaCore] PML appearance preset support enabled.");
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
        randomizeSkinMethod = null;
        appliedSkinPartsMethod = null;
        logger = null;
        Selectors.Clear();
    }

    private static bool BeforeToggleSkinPart(object __instance, string partCode, string variantCode)
    {
        if (applyingPresetSelector != null || !Selectors.TryGetValue(partCode, out AnimalicaSkinPresetSelector? selector))
        {
            return true;
        }

        if (!selector.Presets.TryGetValue(variantCode, out Dictionary<string, string>? preset)
            || randomizeSkinMethod == null)
        {
            return true;
        }

        var selection = new Dictionary<string, string>(preset, StringComparer.Ordinal)
        {
            [selector.PartCode] = selector.ResetVariant
        };

        try
        {
            applyingPresetSelector = selector;
            randomizeSkinMethod.Invoke(__instance, new object[] { selection });
            return false;
        }
        catch (Exception exception)
        {
            logger?.Error(
                "[AnimalicaCore] Could not apply appearance preset '{0}' from selector '{1}': {2}",
                variantCode,
                partCode,
                exception
            );
            return true;
        }
        finally
        {
            applyingPresetSelector = null;
        }
    }

    private static void BeforeRandomizeSkin(object __instance, Dictionary<string, string> preSelection)
    {
        if (selectingModelSelector != null)
        {
            preSelection.Clear();
            CopySelection(selectingModelSelector.Presets[selectingModelSelector.DefaultPreset], preSelection);
            CopySelection(selectingModelSelector.DefaultParts, preSelection);
        }
        else if (applyingPresetSelector != null && applyingPresetSelector.PreserveUnspecified)
        {
            PreserveCurrentNonPresetParts(__instance, preSelection);
        }

        // Preset selectors behave as action menus. Resetting them prevents a
        // customized or randomized appearance from being mislabeled as a preset.
        foreach (AnimalicaSkinPresetSelector selector in Selectors.Values)
        {
            if (!preSelection.ContainsKey(selector.PartCode))
            {
                preSelection[selector.PartCode] = selector.ResetVariant;
            }
        }
    }

    private static void BeforeToggleModel(string modelCode)
    {
        selectingModelSelector = Selectors.Values.FirstOrDefault(selector =>
            selector.ModelCodes.Any(code => string.Equals(code, modelCode, StringComparison.OrdinalIgnoreCase))
        );
    }

    private static void AfterToggleModel()
    {
        selectingModelSelector = null;
    }

    private static void PreserveCurrentNonPresetParts(object skinBehavior, Dictionary<string, string> selection)
    {
        if (appliedSkinPartsMethod?.Invoke(skinBehavior, null) is not IEnumerable appliedParts)
        {
            return;
        }

        foreach (object? appliedPart in appliedParts)
        {
            if (appliedPart == null)
            {
                continue;
            }

            string? partCode = ReadStringMember(appliedPart, "PartCode");
            string? variantCode = ReadStringMember(appliedPart, "Code");
            if (!string.IsNullOrEmpty(partCode) && variantCode != null && !selection.ContainsKey(partCode))
            {
                selection[partCode] = variantCode;
            }
        }
    }

    private static void CopySelection(
        IReadOnlyDictionary<string, string> source,
        IDictionary<string, string> destination)
    {
        foreach ((string partCode, string variantCode) in source)
        {
            destination[partCode] = variantCode;
        }
    }

    private static string? ReadStringMember(object value, string name)
    {
        Type type = value.GetType();
        return AccessTools.Property(type, name)?.GetValue(value) as string
            ?? AccessTools.Field(type, name)?.GetValue(value) as string;
    }

    private static bool ValidateSelector(
        AnimalicaSkinPresetSelector selector,
        AssetLocation source,
        ILogger assetLogger)
    {
        if (string.IsNullOrWhiteSpace(selector.PartCode)
            || selector.ModelCodes.Count == 0
            || string.IsNullOrWhiteSpace(selector.DefaultPreset)
            || string.IsNullOrWhiteSpace(selector.ResetVariant)
            || selector.Presets.Count == 0)
        {
            assetLogger.Error("[AnimalicaCore] Skin preset selector in {0} is missing required fields.", source);
            return false;
        }

        if (!selector.Presets.ContainsKey(selector.DefaultPreset))
        {
            assetLogger.Error(
                "[AnimalicaCore] Skin preset selector '{0}' in {1} references missing default preset '{2}'.",
                selector.PartCode,
                source,
                selector.DefaultPreset
            );
            return false;
        }

        return true;
    }
}

internal sealed class AnimalicaSkinPresetDocument
{
    [JsonProperty("selectors")]
    public Dictionary<string, AnimalicaSkinPresetSelector> Selectors { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class AnimalicaSkinPresetSelector
{
    [JsonIgnore]
    public string PartCode { get; set; } = string.Empty;

    [JsonProperty("modelCodes")]
    public List<string> ModelCodes { get; set; } = new();

    [JsonProperty("defaultPreset")]
    public string DefaultPreset { get; set; } = string.Empty;

    [JsonProperty("resetVariant")]
    public string ResetVariant { get; set; } = "custom";

    [JsonProperty("preserveUnspecified")]
    public bool PreserveUnspecified { get; set; } = true;

    [JsonProperty("defaultParts")]
    public Dictionary<string, string> DefaultParts { get; set; } = new(StringComparer.Ordinal);

    [JsonProperty("presets")]
    public Dictionary<string, Dictionary<string, string>> Presets { get; set; } = new(StringComparer.Ordinal);
}
