using HarmonyLib;
using Vintagestory.API.Common;

namespace AnimalicaBodyTools.HarmonyPatches;

internal sealed class HarmonyPatchBootstrap
{
    private const string HarmonyId = "animalicabodytools.emptyhand.directactionpoc";

    private readonly ICoreAPI api;
    private Harmony? harmony;

    public HarmonyPatchBootstrap(ICoreAPI api)
    {
        this.api = api;
    }

    public void Start()
    {
        harmony = new Harmony(HarmonyId);
        harmony.PatchAll(typeof(HarmonyPatchBootstrap).Assembly);
        api.World.Logger.Notification("[animalicabodytools] Targeted wearable empty-hand Harmony patches applied with cached equipped-tool resolution.");
    }

    public void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
    }
}
