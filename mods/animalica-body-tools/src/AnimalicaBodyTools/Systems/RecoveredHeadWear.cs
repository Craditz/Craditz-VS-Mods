using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace AnimalicaBodyTools.Systems;

/// <summary>Only the explicit vanilla salvage pairs and their marked descendants.</summary>
internal static class RecoveredHeadWear
{
    internal const string Attribute = "animalicaRecoveredHeadWear";
    private const string RecipeFlag = "animalicaRecoverHead";
    internal sealed record Pair(string Tool, string Head, string Body);
    internal static readonly Pair[] Pairs = BuildPairs();

    private static Pair[] BuildPairs()
    {
        var result = new List<Pair>();
        string[] metals = ["copper", "tinbronze", "bismuthbronze", "blackbronze", "iron", "meteoriciron", "steel"];
        (string Tool, string Head, string Body)[] families = [
            ("knife-generic", "knifeblade", "clawtips"), ("axe-felling", "axehead", "rendingclaws"),
            ("pickaxe", "pickaxehead", "tunnelingclaws"), ("scythe", "scythehead", "harvestingclaws"),
            ("prospectingpick", "prospectingpickhead", "prospectingclaws"), ("hoe", "hoehead", "cultivatingclaws"),
            ("blade-falx", "bladehead-falx", "fangcaps")];
        foreach (var family in families)
            foreach (string metal in metals)
            {
                result.Add(new("game:" + family.Tool + "-" + metal, "game:" + family.Head + "-" + metal,
                    "animalicabodytools:" + family.Body + "-" + (metal == "tinbronze" ? "bronze" : metal)));
            }
        return result.ToArray();
    }

    private static bool Ordinary(ItemStack stack, bool tool) =>
        stack.Attributes.All(a => a.Key is "durability" or Attribute || (tool && a.Key == "toolMode"));
    private static bool IsSalvage(GridRecipe recipe) => recipe.Attributes?[RecipeFlag].AsBool(false) == true;
    private static bool HasWear(ItemStack stack) => stack.Attributes.HasAttribute(Attribute);
    private static bool BodyMatches(string code, Pair pair) => code == pair.Body || code == pair.Body + "-wearable";

    private static bool UnchangedToolCatalyst(GridRecipe recipe, ItemStack stack)
    {
        if (Pairs.Any(p => p.Head == stack.Collectible.Code.ToString())) return false;
        var matching = recipe.RecipeIngredients.Where(i => i.SatisfiesAsIngredient(stack)).ToArray();
        return matching.Length > 0 && matching.All(i => !i.ConsumeProperties.Consume
            && i.ConsumeProperties.DurabilityChange <= 0
            && recipe.CopyAttributesFrom != i.Id && !recipe.MergeAttributesFrom.Contains(i.Id));
    }

    private static bool ReadBudget(ItemStack source, bool head, out int numerator, out int denominator)
    {
        numerator = denominator = 0;
        if (!Ordinary(source, !head)) return false;
        if (HasWear(source))
        {
            ITreeAttribute? saved = source.Attributes.GetTreeAttribute(Attribute);
            if (saved == null || saved.GetInt("version") != 1) return false;
            numerator = saved.GetInt("numerator");
            denominator = saved.GetInt("denominator");
            if (numerator <= 0 || denominator <= 0 || numerator > denominator) return false;
            if (head) return !source.Attributes.HasAttribute("durability");
            int currentMax = source.Collectible.GetMaxDurability(source);
            int current = source.Collectible.GetRemainingDurability(source);
            if (currentMax <= 0 || current <= 0 || current > currentMax) return false;
            // Retain the exact pre-rounding fraction while the applied integer is unchanged.
            // Once used, cap at the new actual fraction; neither damage nor increased stats repairs it.
            if (saved.GetInt("appliedRemaining") != current || saved.GetInt("appliedMax") != currentMax)
                if ((long)current * denominator < (long)numerator * currentMax)
                {
                    numerator = current;
                    denominator = currentMax;
                }
            return true;
        }
        if (head) return false;
        numerator = source.Collectible.GetRemainingDurability(source);
        denominator = source.Collectible.GetMaxDurability(source);
        return numerator > 0 && denominator > 0 && numerator <= denominator;
    }

    // Return false only for an affected recipe which cannot preserve a single head's wear.
    // The caller must retain the engine's existing false result, including trait/material gates.
    internal static bool Allows(GridRecipe recipe, ItemSlot[] slots) =>
        TryPlan(recipe, slots, recipe.Output?.ResolvedItemStack, out _, out _, out _, out _);

    private static bool TryPlan(GridRecipe recipe, ItemSlot[] slots, ItemStack? output,
        out ItemStack? source, out int numerator, out int denominator, out bool headOutput)
    {
        source = null;
        numerator = denominator = 0;
        headOutput = false;
        bool salvage = IsSalvage(recipe);
        if (!salvage && !slots.Any(s => !s.Empty && HasWear(s.Itemstack!))) return true;
        ItemStack[] inputs = slots.Where(s => !s.Empty).Select(s => s.Itemstack!).ToArray();
        var marked = inputs.Where(HasWear).Where(s => !UnchangedToolCatalyst(recipe, s)).ToArray();
        if (!salvage && marked.Length == 0) return true;
        if (output == null || output.StackSize != 1) return false;
        string outputCode = output.Collectible.Code.ToString();
        Pair? pair;
        if (salvage)
        {
            if (inputs.Length != 1 || inputs[0].StackSize != 1) return false;
            source = inputs[0];
            string sourceCode = source.Collectible.Code.ToString();
            pair = Pairs.FirstOrDefault(p => p.Tool == sourceCode && p.Head == outputCode);
            headOutput = true;
            if (pair == null || !ReadBudget(source, false, out numerator, out denominator)) return false;
        }
        else
        {
            if (marked.Length != 1) return false;
            source = marked[0];
            string sourceCode = source.Collectible.Code.ToString();
            pair = Pairs.FirstOrDefault(p => p.Head == sourceCode || p.Tool == sourceCode || BodyMatches(sourceCode, p));
            if (pair == null || !ReadBudget(source, sourceCode == pair.Head, out numerator, out denominator)) return false;
            // A recovered head may make its matching tool or corresponding Body Tool.
            // A marked Body Tool may change held/wearable form within the same family.
            if (sourceCode == pair.Head)
            {
                if (outputCode != pair.Tool && !BodyMatches(outputCode, pair)) return false;
            }
            else if (!BodyMatches(sourceCode, pair) || !BodyMatches(outputCode, pair)) return false;
        }
        // Reject nonconsumed catalysts, multi-head inputs, or returned material outputs.
        var ingredients = recipe.RecipeIngredients.Where(i => i != null).ToArray();
        ItemStack consumedSource = source;
        var matching = ingredients.Where(i => i.SatisfiesAsIngredient(consumedSource)).ToArray();
        if (matching.Length != 1 || matching[0].Quantity != 1 || !matching[0].ConsumeProperties.Consume
            || ingredients.Any(i => i.ReturnedStack != null)
            || source.Collectible.Attributes?["noConsumeOnCrafting"].AsBool(false) == true) return false;
        if (headOutput) return true;
        // No Math.Max(1): a positive fraction smaller than one output point is ineligible.
        int maximum = output.Collectible.GetMaxDurability(output);
        return maximum > 0 && (long)numerator * maximum / denominator > 0;
    }

    internal static void Apply(RecipeBase recipe, ItemSlot[] slots, ItemSlot outputSlot)
    {
        if (recipe is not GridRecipe grid || outputSlot.Empty) return;
        if (!TryPlan(grid, slots, outputSlot.Itemstack, out ItemStack? source, out int n, out int d, out bool head))
        {
            outputSlot.Itemstack = null;
            return;
        }
        if (source == null) return;
        ItemStack output = outputSlot.Itemstack;
        // copyAttributesFrom may have copied a different tool's integer durability.
        output.Attributes.RemoveAttribute("durability");
        var wear = new TreeAttribute();
        wear.SetInt("version", 1);
        wear.SetInt("numerator", n);
        wear.SetInt("denominator", d);
        if (!head)
        {
            int max = output.Collectible.GetMaxDurability(output);
            int left = (int)((long)n * max / d);
            output.Collectible.SetDurability(output, left);
            wear.SetInt("appliedMax", max);
            wear.SetInt("appliedRemaining", left);
        }
        output.Attributes[Attribute] = wear;
    }

    internal static bool HeadWear(ItemStack stack, out int n, out int d)
    {
        n = d = 0;
        return HasWear(stack) && Pairs.Any(p => p.Head == stack.Collectible.Code.ToString()) && ReadBudget(stack, true, out n, out d);
    }
}
