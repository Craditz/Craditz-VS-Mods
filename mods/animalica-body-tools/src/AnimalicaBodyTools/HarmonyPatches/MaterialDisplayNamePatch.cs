using HarmonyLib;
using Vintagestory.API.Common;
using AnimalicaBodyTools.Systems;

namespace AnimalicaBodyTools.HarmonyPatches;

[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetHeldItemName))]
internal static class MaterialDisplayNamePatch
{
    private static void Postfix(CollectibleObject __instance, ItemStack itemStack, ref string __result)
    {
        string code = itemStack?.Collectible?.Code?.ToString() ?? __instance.Code?.ToString() ?? string.Empty;
        __result = MaterialDisplayNameResolver.Resolve(code, __result) ?? __result;
    }
}
