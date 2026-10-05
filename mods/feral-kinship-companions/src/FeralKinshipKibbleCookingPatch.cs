#nullable enable

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// Keeps the vanilla pot conversion and freshness path while treating a whole
/// same-diet kibble batch as one native serving.
/// </summary>
[HarmonyPatch]
internal static class FeralKinshipKibbleCookingMatchPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(
        typeof(BlockCookingContainer),
        nameof(BlockCookingContainer.GetMatchingCookingRecipe),
        new[] { typeof(IWorldAccessor), typeof(ItemStack[]), typeof(int).MakeByRefType() })!;

    internal static bool ContainsKibbleInput(ItemStack?[] stacks) =>
        stacks.Any(stack => stack != null && TryGetRawKibbleDiet(stack, out _));

    internal static bool CanOverrideForPot(bool isDirtyPot) => !isDirtyPot;

    internal static bool TryBuildBatch(
        CookingRecipe[] recipes,
        ItemStack?[] stacks,
        out CookingRecipe? batchRecipe,
        out int outputCount,
        out bool containsKibble,
        out bool overStackLimit)
    {
        batchRecipe = null;
        outputCount = 0;
        containsKibble = false;
        overStackLimit = false;

        string? diet = null;
        long total = 0;
        bool sawNonKibble = false;
        foreach (ItemStack? stack in stacks)
        {
            if (stack == null) continue;
            if (!TryGetRawKibbleDiet(stack, out string stackDiet))
            {
                if (containsKibble) return false;
                sawNonKibble = true;
                continue;
            }
            containsKibble = true;
            if (sawNonKibble) return false;
            if (diet != null && !string.Equals(diet, stackDiet, StringComparison.Ordinal)) return false;
            diet = stackDiet;
            if (stack.StackSize <= 0) return false;
            total += stack.StackSize;
            if (total > int.MaxValue) return false;
        }

        if (!containsKibble || diet == null || total <= 0 || stacks.Length > 4) return false;

        CookingRecipe? registered = recipes.FirstOrDefault(recipe => IsOwnedKibbleRecipe(recipe, diet));
        ItemStack? resolvedOutput = registered?.CooksInto?.ResolvedItemstack;
        if (registered == null || resolvedOutput == null) return false;

        int maxStackSize = resolvedOutput.Collectible.MaxStackSize;
        if (total > maxStackSize)
        {
            overStackLimit = true;
            return false;
        }
        int batchCount = checked((int)total);

        JsonItemStack cooksInto = registered.CooksInto!.Clone();
        cooksInto.StackSize = batchCount;
        cooksInto.ResolvedItemstack = resolvedOutput.Clone();
        cooksInto.ResolvedItemstack.StackSize = batchCount;

        batchRecipe = new CookingRecipe
        {
            Code = registered.Code,
            Ingredients = registered.Ingredients,
            Enabled = registered.Enabled,
            Shape = registered.Shape,
            PerishableProps = registered.PerishableProps?.Clone(),
            CooksInto = cooksInto,
            IsFood = registered.IsFood
        };
        outputCount = batchCount;
        return true;
    }

    private static bool IsOwnedKibbleRecipe(CookingRecipe recipe, string diet)
    {
        string expectedRaw = "feralkinshipcompanions:kibble-" + diet + "-raw";
        string expectedDry = "feralkinshipcompanions:kibble-" + diet + "-dry";
        string expectedRecipe = "kibble-" + diet + "-dry";
        JsonItemStack? output = recipe.CooksInto;
        CookingRecipeIngredient[]? ingredients = recipe.Ingredients;
        if (!recipe.Enabled || !recipe.IsFood || (recipe.Code != expectedRecipe && recipe.Code != "feralkinshipcompanions:" + expectedRecipe)
            || output == null || output.Type != EnumItemClass.Item
            || output.Code?.ToString() != expectedDry || output.ResolvedItemstack?.Collectible.Code.ToString() != expectedDry
            || ingredients == null || ingredients.Length != 1 || ingredients[0].ValidStacks == null
            || ingredients[0].ValidStacks.Length != 1)
        {
            return false;
        }

        CookingRecipeStack input = ingredients[0].ValidStacks[0];
        return input.Type == EnumItemClass.Item
            && input.Code?.ToString() == expectedRaw
            && input.StackSize == 1
            && ingredients[0].MinQuantity == 3
            && ingredients[0].MaxQuantity == 3;
    }

    private static bool TryGetRawKibbleDiet(ItemStack stack, out string diet)
    {
        diet = string.Empty;
        if (stack.Class != EnumItemClass.Item) return false;
        AssetLocation code = stack.Collectible.Code;
        if (code.Domain != "feralkinshipcompanions") return false;
        if (code.Path == "kibble-meat-raw")
        {
            diet = "meat";
            return true;
        }
        if (code.Path == "kibble-plant-raw")
        {
            diet = "plant";
            return true;
        }
        return false;
    }

    private static bool Prefix(
        BlockCookingContainer __instance,
        IWorldAccessor __0,
        ItemStack[] __1,
        ref int __2,
        ref CookingRecipe? __result)
    {
        if (!CanOverrideForPot(__instance.Attributes?["isDirtyPot"].AsBool(false) ?? false)) return true;
        if (!ContainsKibbleInput(__1)) return true;
        CookingRecipe[] recipes = __0.Api.GetCookingRecipes()?.ToArray() ?? Array.Empty<CookingRecipe>();
        if (TryBuildBatch(recipes, __1, out CookingRecipe? batch, out _, out bool containsKibble, out _))
        {
            __result = batch;
            __2 = 1;
            return false;
        }

        if (containsKibble)
        {
            __result = null;
            __2 = 0;
            return false;
        }

        return true;
    }
}

[HarmonyPatch]
internal static class FeralKinshipKibbleCookingOutputPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(
        typeof(BlockCookingContainer),
        nameof(BlockCookingContainer.GetOutputText),
        new[] { typeof(IWorldAccessor), typeof(ISlotProvider), typeof(ItemSlot) })!;

    private static bool Prefix(BlockCookingContainer __instance, IWorldAccessor __0, ISlotProvider __1, ItemSlot __2, ref string? __result)
    {
        if (__2.Itemstack == null || __2.Itemstack.Collectible is not BlockCookingContainer) return true;
        if (__instance.Attributes?["isDirtyPot"].AsBool(false) == true) return true;
        ItemStack[] stacks = __instance.GetCookingStacks(__1);
        if (!FeralKinshipKibbleCookingMatchPatch.ContainsKibbleInput(stacks)) return true;
        CookingRecipe[] recipes = __0.Api.GetCookingRecipes()?.ToArray() ?? Array.Empty<CookingRecipe>();
        if (FeralKinshipKibbleCookingMatchPatch.TryBuildBatch(
                recipes, stacks, out CookingRecipe? recipe, out int outputCount,
                out bool containsKibble, out bool overStackLimit))
        {
            string outputName = recipe!.CooksInto!.ResolvedItemstack!.GetName().ToLowerInvariant();
            __result = outputCount == 1
                ? Lang.Get("feralkinshipcompanions:kibble-pot-output-singular", outputName)
                : Lang.Get("feralkinshipcompanions:kibble-pot-output-plural", outputCount, outputName);
            return false;
        }

        if (containsKibble && overStackLimit)
        {
            int maxStackSize = recipes
                .Select(candidate => candidate.CooksInto?.ResolvedItemstack)
                .Where(stack => stack != null && stack.Collectible.Code.Domain == "feralkinshipcompanions"
                    && (stack.Collectible.Code.Path == "kibble-meat-dry"
                        || stack.Collectible.Code.Path == "kibble-plant-dry"))
                .Select(stack => stack!.Collectible.MaxStackSize)
                .DefaultIfEmpty(64)
                .Min();
            __result = Lang.Get("feralkinshipcompanions:kibble-pot-overstack", maxStackSize);
            return false;
        }

        return true;
    }
}
