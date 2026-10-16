using System.Text;
using AnimalicaBodyTools.Systems;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace AnimalicaBodyTools.HarmonyPatches;

[HarmonyPatch(typeof(GridRecipe), nameof(GridRecipe.Matches))]
internal static class RecoveredHeadMatchPatch
{
    private static void Postfix(GridRecipe __instance, ItemSlot[] ingredients, ref bool __result)
    {
        if (__result) __result = RecoveredHeadWear.Allows(__instance, ingredients);
    }
}

[HarmonyPatch(typeof(GridRecipe), nameof(GridRecipe.ConsumeInput))]
internal static class RecoveredHeadConsumePatch
{
    private static bool Prefix(GridRecipe __instance, ItemSlot[] inputSlots, ref bool __result)
    {
        if (RecoveredHeadWear.Allows(__instance, inputSlots)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(RecipeBase), nameof(RecipeBase.GenerateOutputStack))]
internal static class RecoveredHeadOutputPatch
{
    // Apply after the engine's copying, averaging, and collectible callbacks.
    private static void Postfix(RecipeBase __instance, ItemSlot[] inputSlots, ItemSlot outputSlot) =>
        RecoveredHeadWear.Apply(__instance, inputSlots, outputSlot);
}

[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetHeldItemInfo))]
internal static class RecoveredHeadInfoPatch
{
    private static void Postfix(ItemSlot inSlot, StringBuilder dsc)
    {
        if (!inSlot.Empty && RecoveredHeadWear.HeadWear(inSlot.Itemstack, out int n, out int d))
        {
            string line = Lang.Get("animalicabodytools:recovered-head-wear", (100.0 * n / d).ToString("0.##"));
            // Client/server initialization can register this postfix twice in one process.
            if (!dsc.ToString().Contains(line, System.StringComparison.Ordinal)) dsc.AppendLine(line);
        }
    }
}
