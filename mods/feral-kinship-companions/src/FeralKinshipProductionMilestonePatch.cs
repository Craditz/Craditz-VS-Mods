#nullable enable

using System.Linq;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

// Observe completed vanilla conversions. Do not replace engine production,
// award on ignition, or discover milestones from pre-existing finished stacks.
[HarmonyPatch(typeof(BlockEntityPitKiln), nameof(BlockEntityPitKiln.OnFired))]
internal static class FeralKinshipPitKilnMilestonePatch
{
    [HarmonyPrefix]
    internal static void Prefix(BlockEntityPitKiln __instance, out ItemStack?[]? __state)
    {
        __state = __instance.Api?.Side == EnumAppSide.Server && __instance.Lit
            ? __instance.Inventory.Select(slot => slot.Itemstack).ToArray() : null;
    }

    [HarmonyPostfix]
    internal static void Postfix(BlockEntityPitKiln __instance, ItemStack?[]? __state)
    {
        if (__state == null) return;
        bool converted = false, refractory = false;
        for (int i = 0; i < __state.Length; i++)
        {
            ItemStack? after = __instance.Inventory[i].Itemstack;
            converted |= CompanionProductionMilestoneRules.WasConverted(__state[i], after);
            refractory |= CompanionProductionMilestoneRules.IsRefractoryConversion(__state[i], after);
        }
        if (converted) __instance.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()?
            .RecordProductionCompletion(__instance.Pos, "milestone.pit_kiln", refractory);
    }
}

[HarmonyPatch(typeof(BlockEntityBloomery), "DoSmelt")]
internal static class FeralKinshipBloomeryMilestonePatch
{
    [HarmonyPrefix]
    internal static void Prefix(BlockEntityBloomery __instance, InventoryGeneric ___bloomeryInv,
        out ItemStack? __state)
    {
        __state = __instance.Api?.Side == EnumAppSide.Server && __instance.IsBurning
            ? ___bloomeryInv[1].Itemstack : null;
    }

    [HarmonyPostfix]
    internal static void Postfix(BlockEntityBloomery __instance, InventoryGeneric ___bloomeryInv,
        ItemStack? __state)
    {
        // The original decrements the raw stack, so compare its collectible
        // rather than the now-consumed input quantity.
        ItemStack? output = ___bloomeryInv[2].Itemstack;
        if (__state?.Collectible?.Code == null || output?.StackSize <= 0 || output == null
            || __instance.IsBurning || __state.Collectible.Code.Equals(output.Collectible.Code)) return;
        __instance.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()?
            .RecordProductionCompletion(__instance.Pos, "milestone.bloomery", false);
    }
}

[HarmonyPatch(typeof(BlockEntityBeeHiveKiln), "ConvertItemToBurned")]
internal static class FeralKinshipBeeHiveKilnMilestonePatch
{
    [HarmonyPrefix]
    internal static void Prefix(BlockEntityBeeHiveKiln __instance, ItemSlot itemSlot, out ItemStack? __state)
    {
        __state = __instance.Api?.Side == EnumAppSide.Server ? itemSlot?.Itemstack : null;
    }

    [HarmonyPostfix]
    internal static void Postfix(BlockEntityBeeHiveKiln __instance, BlockEntityGroundStorage groundStorage,
        ItemSlot itemSlot, ItemStack? __state)
    {
        if (!CompanionProductionMilestoneRules.WasConverted(__state, itemSlot?.Itemstack)) return;
        __instance.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()?
            .RecordProductionCompletion(groundStorage.Pos, "milestone.beehive_kiln",
                CompanionProductionMilestoneRules.IsRefractoryConversion(__state, itemSlot?.Itemstack));
    }
}

// Firepit production is broader than smelting. Only the exact vanilla
// refractory recipe is objective here; ordinary meals/metals do not qualify.
[HarmonyPatch(typeof(BlockEntityFirepit), nameof(BlockEntityFirepit.smeltItems))]
internal static class FeralKinshipFirepitRefractoryMilestonePatch
{
    [HarmonyPrefix]
    internal static void Prefix(BlockEntityFirepit __instance, out ItemStack? __state)
    {
        __state = __instance.Api?.Side == EnumAppSide.Server ? __instance.inputSlot.Itemstack?.Clone() : null;
    }

    [HarmonyPostfix]
    internal static void Postfix(BlockEntityFirepit __instance, ItemStack? __state)
    {
        ItemStack? remaining = __instance.inputSlot.Itemstack;
        int remainingRaw = remaining?.Collectible?.Code.Equals(__state?.Collectible?.Code) == true
            ? remaining.StackSize : 0;
        if (__state == null || remainingRaw >= __state.StackSize
            || !CompanionProductionMilestoneRules.IsRefractoryConversion(__state, __instance.outputSlot.Itemstack)) return;
        __instance.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()?
            .RecordProductionCompletion(__instance.Pos, "milestone.refractory", false);
    }
}
