using System;
using AnimalicaBodyTools.Config;
using AnimalicaBodyTools.Util;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace AnimalicaBodyTools.HarmonyPatches;

[HarmonyPatch(typeof(GridRecipe), nameof(GridRecipe.Matches))]
internal static class AnimalBodyToolCraftingRestrictionPatch
{
    private static void Prefix(GridRecipe __instance, out string? __state)
    {
        __state = null;

        // GridRecipe performs its own RequiresTrait check before our postfix runs.
        // Light and Disabled explicitly allow body-tool recipes for non-animal players,
        // so temporarily remove only that built-in gate for this match call.
        if (AnimalicaBodyToolsPolicy.RestrictAnimalToolCrafting)
        {
            return;
        }

        string? requiresTrait = __instance.RequiresTrait;
        if (string.IsNullOrWhiteSpace(requiresTrait))
        {
            return;
        }

        __state = requiresTrait;
        __instance.RequiresTrait = null!;
    }

    private static void Postfix(GridRecipe __instance, IPlayer forPlayer, ref bool __result, string? __state)
    {
        RestoreRequiresTrait(__instance, __state);

        if (!__result || !AnimalicaBodyToolsPolicy.RestrictAnimalToolCrafting)
        {
            return;
        }

        ItemStack? output = __instance.Output?.ResolvedItemStack;
        CollectibleObject? collectible = output?.Collectible;
        if (collectible == null || !IsAnimalBodyTool(collectible))
        {
            return;
        }

        if (forPlayer?.Entity is not EntityAgent entity
            || !AnimalBodyToolTraitAccessUtil.HolderHasTraitFast(entity))
        {
            __result = false;
        }
    }

    private static Exception? Finalizer(GridRecipe __instance, string? __state, Exception? __exception)
    {
        RestoreRequiresTrait(__instance, __state);
        return __exception;
    }

    private static void RestoreRequiresTrait(GridRecipe recipe, string? requiresTrait)
    {
        if (requiresTrait != null)
        {
            recipe.RequiresTrait = requiresTrait;
        }
    }

    internal static bool IsAnimalBodyTool(CollectibleObject collectible)
    {
        return collectible.Attributes?["animalicaBodyTool"].AsBool(false) == true
            || collectible.Attributes?["animalicaWearableBodyTool"].AsBool(false) == true;
    }
}

[HarmonyPatch(typeof(RecipeBase), "get_RequiresTrait")]
internal static class AnimalBodyToolRecipeTraitDisplayPatch
{
    private static void Postfix(RecipeBase __instance, ref string? __result)
    {
        if (AnimalicaBodyToolsPolicy.RestrictAnimalToolCrafting
            || __instance is not GridRecipe gridRecipe)
        {
            return;
        }

        CollectibleObject? output = gridRecipe.Output?.ResolvedItemStack?.Collectible;
        if (output != null && AnimalBodyToolCraftingRestrictionPatch.IsAnimalBodyTool(output))
        {
            __result = null;
        }
    }
}
