using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace AnimalicaCore;

/// <summary>Starts only Caribou's skin preview at a full-body camera distance.</summary>
public sealed class AnimalicaCaribouSkinPreviewSystem : ModSystem
{
    private const string HarmonyId = "craditz.animalicacore.caribouskinpreview";
    private static readonly ConditionalWeakTable<object, CaribouSkinPreviewCamera> Cameras = new();
    private static MethodInfo? getModel;
    private static PropertyInfo? zoomProperty;
    private static PropertyInfo? heightProperty;
    private static ILogger? logger;
    private static bool reportedFailure;
    private Harmony? harmony;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        Type? dialog = AccessTools.TypeByName("PlayerModelLib.GuiDialogCreateCustomCharacter");
        MethodInfo? composeSkin = dialog == null ? null : AccessTools.Method(dialog, "ComposeSkinTab");
        getModel = dialog?.GetMethod("GetCurrentModel", Type.EmptyTypes);
        zoomProperty = dialog?.GetProperty("CharZoom");
        heightProperty = dialog?.GetProperty("Height");
        if (composeSkin?.ReturnType != typeof(void) || getModel?.ReturnType != typeof(string)
            || !CameraProperty(zoomProperty) || !CameraProperty(heightProperty))
        {
            api.Logger.Warning("[AnimalicaCore] Caribou skin-preview camera hook unavailable; keeping PML's camera.");
            return;
        }

        logger = api.Logger;
        reportedFailure = false;
        harmony = new Harmony(HarmonyId);
        harmony.Patch(composeSkin, prefix: new HarmonyMethod(
            AccessTools.Method(typeof(AnimalicaCaribouSkinPreviewSystem), nameof(BeforeComposeSkin))));
        api.Logger.Notification("[AnimalicaCore] Caribou skin-preview camera framing enabled.");
    }

    private static bool CameraProperty(PropertyInfo? property) =>
        property?.PropertyType == typeof(float) && property.CanRead && property.CanWrite;

    private static void BeforeComposeSkin(object __instance)
    {
        try
        {
            if (getModel?.Invoke(__instance, null) is not string modelCode) return;
            float zoom = (float)zoomProperty!.GetValue(__instance)!;
            float height = (float)heightProperty!.GetValue(__instance)!;
            Cameras.GetValue(__instance, _ => new CaribouSkinPreviewCamera()).Apply(modelCode, ref zoom, ref height);
            zoomProperty.SetValue(__instance, zoom);
            heightProperty.SetValue(__instance, height);
        }
        catch (Exception exception)
        {
            if (!reportedFailure)
            {
                logger?.Warning("[AnimalicaCore] Caribou skin-preview camera could not be applied: {0}", exception.Message);
                reportedFailure = true;
            }
        }
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
        Cameras.Clear();
        getModel = null;
        zoomProperty = null;
        heightProperty = null;
        logger = null;
    }
}
