using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AnimalicaBodyTools.Items;
using AnimalicaBodyTools.Systems;
using AnimalicaBodyTools.Util;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace AnimalicaBodyTools.HarmonyPatches;

[HarmonyPatch(typeof(GridRecipe), nameof(GridRecipe.Resolve))]
internal static class BodyToolRecipeCompatibilityPatch
{
    private static readonly Dictionary<string, EnumTool> ToolFamilies = new(StringComparer.Ordinal)
    {
        ["knife-*"] = EnumTool.Knife,
        ["axe-*"] = EnumTool.Axe,
        ["pickaxe-*"] = EnumTool.Pickaxe,
        ["shovel-*"] = EnumTool.Shovel,
        ["hoe-*"] = EnumTool.Hoe,
        ["scythe-*"] = EnumTool.Scythe,
        ["prospectingpick-*"] = EnumTool.Probe,
        ["blade-*"] = EnumTool.Sword
    };

    private static readonly Dictionary<EnumTool, string> ToolTags = ToolFamilies.Values.ToDictionary(
        tool => tool, tool => "animalicabodytools:craftingtool-" + tool.ToString().ToLowerInvariant());

    private static readonly ConditionalWeakTable<IWorldAccessor, Dictionary<EnumTool, ComplexTagCondition<TagSet>>> WorldTags = new();

    private static void Prefix(GridRecipe __instance, IWorldAccessor world)
    {
        if (world.Side != EnumAppSide.Server || __instance.Ingredients == null)
        {
            return;
        }

        foreach (CraftingRecipeIngredient ingredient in __instance.Ingredients.Values)
        {
            if (ingredient.Type != EnumItemClass.Item || ingredient.ConsumeProperties.Consume
                || ingredient.Code is not { Domain: "game" } code || !ToolFamilies.TryGetValue(code.Path, out EnumTool tool)
                || ingredient.Name != null || ingredient.AllowedVariants != null || ingredient.SkipVariants != null
                || ingredient.Attributes != null || ingredient.RecipeAttributes != null || !ingredient.Tags.IsEmpty)
            {
                continue;
            }

            var conditions = WorldTags.GetOrCreateValue(world);
            if (!conditions.TryGetValue(tool, out var condition))
            {
                condition = RegisterFamily(world, code, tool);
                conditions.Add(tool, condition);
            }

            if (!condition.IsEmpty)
            {
                ingredient.Code = new AssetLocation("*", "*");
                ingredient.Tags = condition;
            }
        }
    }

    private static ComplexTagCondition<TagSet> RegisterFamily(IWorldAccessor world, AssetLocation code, EnumTool tool)
    {
        Item[] substitutes = world.Items.Where(item => item != null && IsBodyTool(item) && CanCraftAs(item, tool)).ToArray();
        if (substitutes.Length == 0)
        {
            return default;
        }

        var registry = world.Api.CollectibleTagRegistry;
        string tagName = ToolTags[tool];
        if (registry.TryRegisterAndCreateTagSet(out TagSet tag, tagName) != TagRegistryError.None)
        {
            world.Logger.Warning("[animalicabodytools] Could not register crafting tool tag {0}.", tagName);
            return default;
        }

        foreach (Item item in world.Items.Where(item => item?.Code != null && WildcardUtil.Match(code, item.Code)).Concat(substitutes))
        {
            item.Tags = registry.CreateMergedTagSet(item.Tags, tag);
        }

        return new ComplexTagCondition<TagSet>
        {
            conditions = new[] { new ComplexTagCondition<TagSet>.Condition { RequiredTags = tag, ForbiddenTags = TagSet.Empty } }
        };
    }

    internal static bool IsBodyTool(CollectibleObject item) => AnimalBodyToolCraftingRestrictionPatch.IsAnimalBodyTool(item);

    private static bool CanCraftAs(Item item, EnumTool tool) => item.Tool == tool || tool switch
    {
        EnumTool.Shovel => item is ItemSpeciesLockedClaws { SupportsDigging: true },
        EnumTool.Probe => item is ItemProspectingPick,
        EnumTool.Sword => BodyToolDurability.IsFangCap(item),
        _ => false
    };

    internal static bool IsSubstitutedTool(GridRecipe recipe, ItemStack stack, IWorldAccessor world)
    {
        if (stack.Item is not { } item || !IsBodyTool(item))
        {
            return false;
        }

        // Use the same family predicate on both sides. A dual-role tool can
        // match a family even when its primary Tool enum has another value.
        var registry = world.Api.CollectibleTagRegistry;
        foreach (var ingredient in recipe.RecipeIngredients)
        {
            if (ingredient.ConsumeProperties.Consume || ingredient.Code is not { Domain: "*", Path: "*" }
                || ingredient.Tags.conditions is not { Length: 1 } conditions || !ingredient.SatisfiesAsIngredient(stack, false))
            {
                continue;
            }

            foreach (var (tool, tagName) in ToolTags)
            {
                if (CanCraftAs(item, tool) && registry.TryCreateTagSet(out TagSet tag, tagName) == TagRegistryError.None
                    && conditions[0].RequiredTags.Equals(tag))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

[HarmonyPatch(typeof(GridRecipe), nameof(GridRecipe.Matches))]
internal static class BodyToolRecipeUsePermissionPatch
{
    private static void Postfix(GridRecipe __instance, IPlayer forPlayer, IWorldAccessor world, ItemSlot[] ingredients, ref bool __result)
    {
        if (__result && forPlayer?.Entity != null && AnimalicaBodyTools.Config.AnimalicaBodyToolsPolicy.RestrictAnimalToolUse
            && ingredients.Any(slot => slot.Itemstack is { } stack && BodyToolRecipeCompatibilityPatch.IsSubstitutedTool(__instance, stack, world))
            && !AnimalBodyToolTraitAccessUtil.HolderHasTraitFast(forPlayer.Entity))
        {
            __result = false;
        }
    }
}
