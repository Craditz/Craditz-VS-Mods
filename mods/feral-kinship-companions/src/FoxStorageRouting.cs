#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// The first storage pass deliberately uses broad, player-readable categories.
/// The foxes do not make logistics decisions; they only ask whether a target
/// accepts the primary category assigned to the carried item.
/// </summary>
internal static class FoxStorageRouting
{
    internal const int MaxExactItemRules = 512;

    [Flags]
    internal enum Category
    {
        None = 0,
        General = 1 << 0,
        Food = 1 << 1,
        Seeds = 1 << 2,
        Logs = 1 << 3,
        Sticks = 1 << 4,
        LeavesAndBranches = 1 << 5,
        Flowers = 1 << 6,
        GrassAndReeds = 1 << 7,
        Ore = 1 << 8,
        Stone = 1 << 9,
        Flint = 1 << 10,
        Boulders = 1 << 11,
        Clay = 1 << 12,
        RuinsSalvage = 1 << 13,
        SoilAndSand = 1 << 14,
        FiberAndTextiles = 1 << 15,
        AnimalMaterials = 1 << 16,
        MetalAndScrap = 1 << 17,
        Fuel = 1 << 18,
        Tools = 1 << 19,
        PotteryAndVessels = 1 << 20,
        WoodProducts = 1 << 21
    }

    private static readonly (Category Category, string Name, string[] Aliases)[] Definitions =
    {
        (Category.General, "General", new[] { "general", "all", "everything" }),
        (Category.Food, "Food", new[] { "food", "foods" }),
        (Category.Seeds, "Seeds", new[] { "seed", "seeds" }),
        (Category.Logs, "Logs", new[] { "log", "logs", "wood" }),
        (Category.Sticks, "Sticks", new[] { "stick", "sticks" }),
        (Category.LeavesAndBranches, "Leaves and branches", new[] { "leaves", "leaf", "branches", "branch" }),
        (Category.Flowers, "Flowers", new[] { "flower", "flowers" }),
        (Category.GrassAndReeds, "Grass and reeds", new[] { "grass", "reeds", "reed", "cattails", "cattail" }),
        (Category.Ore, "Ore", new[] { "ore", "ores" }),
        (Category.Stone, "Stone", new[] { "stone", "stones", "rock", "rocks", "gravel" }),
        (Category.Flint, "Flint", new[] { "flint" }),
        (Category.Clay, "Clay", new[] { "clay" }),
        (Category.RuinsSalvage, "Ruins salvage", new[] { "ruins", "ruin", "salvage", "ancient" }),
        (Category.SoilAndSand, "Soil and sand", new[] { "soil", "dirt", "sand", "sandy" }),
        (Category.FiberAndTextiles, "Fiber and textiles", new[] { "fiber", "flax", "hemp", "twine", "rope", "cloth", "linen" }),
        (Category.AnimalMaterials, "Animal materials", new[] { "hide", "pelt", "leather", "bone", "feather", "horn", "antler", "fang" }),
        (Category.MetalAndScrap, "Metal and scrap", new[] { "ingot", "nugget", "metal", "scrap", "plate", "wire" }),
        (Category.Fuel, "Fuel", new[] { "coal", "charcoal", "firewood", "peat" }),
        (Category.Tools, "Tools", new[] { "axe", "pickaxe", "shovel", "hoe", "chisel", "hammer", "saw", "knife" }),
        (Category.PotteryAndVessels, "Pottery and vessels", new[] { "vessel", "pottery", "ceramic", "crock", "jug", "urn" }),
        (Category.WoodProducts, "Wood products", new[] { "plank", "board", "lumber", "fence", "wooden" })
    };

    internal static IReadOnlyList<(Category Category, string Name)> Options =>
        Definitions.Select(definition => (definition.Category, definition.Name)).ToArray();

    internal static int NormalizeMask(int mask)
    {
        Category normalized = (Category)mask;
        if ((normalized & Category.General) != 0)
        {
            return (int)Category.General;
        }

        Category allowed = Definitions.Aggregate(Category.None, (current, definition) => current | definition.Category);
        return (int)(normalized & allowed);
    }

    internal static string AvailableCategories => string.Join(", ", Definitions.Select(definition => definition.Name));

    internal static bool TryParse(string? value, out Category category)
    {
        string normalized = Normalize(value);
        foreach ((Category candidate, _, string[] aliases) in Definitions)
        {
            if (aliases.Contains(normalized, StringComparer.Ordinal))
            {
                category = candidate;
                return true;
            }
        }

        category = Category.None;
        return false;
    }

    internal static string GetDisplayName(int mask)
    {
        Category category = (Category)mask;
        if (category == Category.None) return "Disabled";
        if (category == Category.General) return "General";

        string[] names = Definitions
            .Where(definition => (category & definition.Category) != 0)
            .Select(definition => definition.Name)
            .ToArray();
        return names.Length == 0 ? "Disabled" : string.Join(" + ", names);
    }

    internal static Category Classify(ItemStack stack, IWorldAccessor world)
    {
        if (stack.Collectible == null)
        {
            stack.ResolveBlockOrItem(world);
        }

        if (stack.Collectible == null)
        {
            return Category.General;
        }

        string path = stack.Collectible.Code?.Path?.ToLowerInvariant() ?? string.Empty;

        // Dry grass carries nutrition properties in the game data, but it is
        // tinder/plant material rather than player food for pack routing.
        // Handle it before the broad nutrition check below.
        if (path.Equals("drygrass", StringComparison.Ordinal))
        {
            return Category.GrassAndReeds;
        }

        // Food is deliberately first after explicit plant-material overrides.
        // If a mod author marks funeral bells, wine, seeds, or anything else
        // as edible, the pack treats it as food.
        if (stack.Collectible.GetNutritionProperties(world, stack, null) != null)
        {
            return Category.Food;
        }

        if (ContainsAny(path, "seed", "seeds")) return Category.Seeds;

        // Some source-native animal foods (notably redmeat-raw) expose their
        // nutrition through the consuming entity rather than the null entity
        // context available to storage routing. Keep those items in Food
        // instead of silently treating them as General. This is only a
        // fallback: modded food with normal nutrition metadata was handled by
        // the check above.
        if (ContainsAny(path,
            "meat", "fish", "poultry", "berry", "berries", "fruit",
            "vegetable", "grain", "bread", "cheese", "milk", "honey",
            "wine", "meal", "stew", "porridge", "mushroom", "cake",
            "pie", "jam")) return Category.Food;

        if (ContainsAny(path, "stick", "sticks")) return Category.Sticks;
        if (ContainsAny(path, "leaf", "leaves", "branch", "branches")) return Category.LeavesAndBranches;
        if (ContainsAny(path, "flower", "flowers")) return Category.Flowers;
        if (ContainsAny(path, "grass", "fern", "reed", "reeds", "cattail", "cattails")) return Category.GrassAndReeds;
        if (ContainsAny(path, "log", "logs")) return Category.Logs;
        if (ContainsAny(path, "flint")) return Category.Flint;
        // A loose boulder is a source block, not the cargo it produces. Once
        // broken, the fox carries ordinary rock/stone, so route boulder drops
        // through the Stone category rather than a destination that can only
        // accept intact boulder items.
        if (ContainsAny(path, "boulder", "boulders")) return Category.Stone;
        if (ContainsAny(path, "ore", "ores")) return Category.Ore;
        if (ContainsAny(path, "clay")) return Category.Clay;
        if (ContainsAny(path, "stone", "rock", "rocks", "gravel", "cobblestone")) return Category.Stone;
        if (ContainsAny(path, "ruin", "ruins", "salvage", "ancient")) return Category.RuinsSalvage;
        if (ContainsAny(path, "soil", "dirt", "sand", "sandy")) return Category.SoilAndSand;
        if (ContainsAny(path, "fiber", "flax", "hemp", "twine", "rope", "cloth", "linen")) return Category.FiberAndTextiles;
        if (ContainsAny(path, "hide", "pelt", "leather", "bone", "feather", "horn", "antler", "fang")) return Category.AnimalMaterials;
        if (ContainsAny(path, "ingot", "nugget", "metal", "scrap", "plate", "wire")) return Category.MetalAndScrap;
        if (ContainsAny(path, "coal", "charcoal", "firewood", "peat")) return Category.Fuel;
        if (ContainsAny(path, "axe", "pickaxe", "shovel", "hoe", "chisel", "hammer", "saw", "knife")) return Category.Tools;
        if (ContainsAny(path, "vessel", "pottery", "ceramic", "crock", "jug", "urn")) return Category.PotteryAndVessels;
        if (ContainsAny(path, "plank", "board", "lumber", "fence", "wooden")) return Category.WoodProducts;
        return Category.General;
    }

    internal static bool Accepts(int mask, ItemStack stack, IWorldAccessor world)
    {
        Category configured = (Category)mask;
        if (configured == Category.None) return false;
        if ((configured & Category.General) != 0) return true;
        return (configured & Classify(stack, world)) != 0;
    }

    internal static bool Accepts(
        FoxPackAmenityRecord record,
        int mask,
        ItemStack stack,
        IWorldAccessor world)
    {
        if (!record.StorageAdvancedMode)
        {
            return Accepts(mask, stack, world);
        }

        string? code = stack.Collectible?.Code?.ToString();
        if (string.IsNullOrWhiteSpace(code)) return false;
        if (record.StorageAdvancedExcludedItemCodes?.Contains(code, StringComparer.OrdinalIgnoreCase) == true)
        {
            return false;
        }
        return record.StorageAdvancedIncludedItemCodes?.Contains(code, StringComparer.OrdinalIgnoreCase) == true;
    }

    internal static bool IsGeneral(FoxPackAmenityRecord record, int mask) =>
        !record.StorageAdvancedMode
        && (((Category)mask & Category.General) != 0);

    /// <summary>
    /// Detects storage by the inventory capability exposed by the game API
    /// instead of maintaining a positive list of block paths. Ordinary
    /// modded containers that expose IBlockEntityContainer therefore work
    /// without a Companions patch.
    ///
    /// The default safety boundary rejects inventories that clearly expose
    /// output or liquid-only processing slots. A modded hybrid container can
    /// explicitly opt in with the block attribute
    /// "feralKinshipStorageRouting": "include"; a machine that exposes a
    /// generic inventory can opt out with "exclude". Extended-capacity slots
    /// can additionally expose a TryPutIntoBulk transfer contract, discovered
    /// without naming the storage mod.
    /// </summary>
    internal static bool IsSupportedStorageTarget(Block? block, BlockEntity? blockEntity)
    {
        if (block?.Code == null || blockEntity is not IBlockEntityContainer container)
        {
            return false;
        }

        IInventory? inventory;
        try
        {
            inventory = container.Inventory;
        }
        catch
        {
            return false;
        }

        if (inventory == null || inventory.Count <= 0 || inventory.PutLocked)
        {
            return false;
        }

        string overrideMode = block.Attributes?["feralKinshipStorageRouting"]?.AsString()
            ?.Trim()
            .ToLowerInvariant() ?? string.Empty;
        if (overrideMode is "exclude" or "false" or "off")
        {
            return false;
        }

        if (overrideMode is "include" or "true" or "on")
        {
            return true;
        }

        // This is deliberately a negative capability check. We do not need
        // to know every storage block a mod may add, but we should not send
        // ordinary cargo into slots that are visibly outputs or liquid-only
        // processing inputs. The normal CanInventoryAcceptOne check remains
        // the final per-item capacity test during routing.
        if (inventory is InventorySmelting or InventoryQuern)
        {
            return false;
        }

        return !inventory.Any(slot => slot is ItemSlotOutput
            || slot is ItemSlotLiquidOnly
            || slot is ItemSlotWatertight);
    }

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-');

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(value.Contains);
}
