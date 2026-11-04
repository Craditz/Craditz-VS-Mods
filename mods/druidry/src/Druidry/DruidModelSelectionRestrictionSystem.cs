using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Druidry;

/// <summary>
/// Keeps Animalica bodies out of PlayerModelLib's ordinary character-model picker.
/// Druidry still applies those same bodies through its own wildshape path.
/// </summary>
public sealed class DruidModelSelectionRestrictionSystem : ModSystem
{
    private const string HarmonyId = "craditz.druidry.modelselection";

    private static ICoreClientAPI? clientApi;
    private Harmony? harmony;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        clientApi = api;

        Type? dialogType = AccessTools.TypeByName("PlayerModelLib.GuiDialogCreateCustomCharacter");
        Type? customModelsType = AccessTools.TypeByName("PlayerModelLib.CustomModelsSystem");
        MethodInfo? toggleModelMethod = dialogType == null
            ? null
            : AccessTools.Method(dialogType, "OnToggleModel", new[] { typeof(string), typeof(GuiComposer) });
        MethodInfo? availableModelsMethod = dialogType == null || customModelsType == null
            ? null
            : AccessTools.Method(dialogType, "GetAvailableModels", new[] { customModelsType });
        MethodInfo? togglePrefix = AccessTools.Method(
            typeof(DruidModelSelectionRestrictionSystem), nameof(BeforeToggleModel));
        MethodInfo? availableModelsPostfix = AccessTools.Method(
            typeof(DruidModelSelectionRestrictionSystem), nameof(AfterGetAvailableModels));

        if (toggleModelMethod == null || togglePrefix == null)
        {
            api.Logger.Warning("[Druidry] PlayerModelLib model-selection hook was unavailable; Animalica models cannot be hidden from the picker.");
            return;
        }

        harmony = new Harmony(HarmonyId);
        harmony.Patch(toggleModelMethod, prefix: new HarmonyMethod(togglePrefix));

        if (availableModelsMethod != null && availableModelsPostfix != null)
        {
            harmony.Patch(availableModelsMethod, postfix: new HarmonyMethod(availableModelsPostfix));
            api.Logger.Notification("[Druidry] Animalica bodies hidden from the ordinary character-model picker.");
        }
        else
        {
            api.Logger.Warning("[Druidry] PlayerModelLib model-list hook was unavailable; Animalica models remain visible but are still blocked.");
        }
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
        clientApi = null;
    }

    private static bool BeforeToggleModel(string modelCode)
    {
        if (!DruidForms.LooksLikeAnimalicaModel(modelCode))
        {
            return true;
        }

        clientApi?.TriggerIngameError(
            typeof(DruidModelSelectionRestrictionSystem),
            "druidry-animalica-model-restricted",
            "Animalica bodies are Druidry wildshapes. Choose a non-Animalica true form.");
        return false;
    }

    private static void AfterGetAvailableModels(ref string[] __result)
    {
        if (__result.Length == 0)
        {
            return;
        }

        string[] filtered = Array.FindAll(__result, modelCode => !DruidForms.LooksLikeAnimalicaModel(modelCode));
        if (filtered.Length > 0)
        {
            __result = filtered;
        }
    }
}
