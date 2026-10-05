using System;
using AnimalicaBodyTools.Items;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace AnimalicaBodyTools.HarmonyPatches;

/// <summary>
/// Opt-in evidence for the genuine held-item DamageItem path. This observes
/// vanilla durability handling and does not alter the stack or slot.
/// </summary>
[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.DamageItem))]
internal static class HeldBodyToolDurabilityDiagnosticPatch
{
    private sealed class Trace
    {
        public required string Side { get; init; }
        public required string ItemType { get; init; }
        public required string Before { get; init; }
        public required int Amount { get; init; }
        public required int SlotHash { get; init; }
    }

    private static bool IsHeldBodyTool(CollectibleObject collectible)
    {
        return collectible is ItemSpeciesLockedBodyTool
            or ItemSpeciesLockedClaws
            or ItemSpeciesLockedCultivatingClaws
            or ItemSpeciesLockedProspectingPick
            or ItemSpeciesLockedRendingClaws
            or ItemSpeciesLockedScythe;
    }

    private static string Describe(ItemStack? stack)
    {
        if (stack == null) return "empty";
        string code = stack.Collectible?.Code?.ToShortString() ?? "unknown";
        int durability = stack.Attributes?.GetInt("durability", -1) ?? -1;
        return $"{code}x{stack.StackSize};stackHash={stack.GetHashCode()};durability={durability}";
    }

    private static void Prefix(
        CollectibleObject __instance,
        IWorldAccessor world,
        Entity byEntity,
        ItemSlot itemSlot,
        int amount,
        bool destroyOnZeroDurability,
        ref Trace? __state)
    {
        if (!WearableTargetedActionPatches.DiagnosticsEnabled || !IsHeldBodyTool(__instance)) return;

        __state = new Trace
        {
            Side = world.Side.ToString(),
            ItemType = __instance.GetType().FullName ?? __instance.GetType().Name,
            Before = Describe(itemSlot?.Itemstack),
            Amount = amount,
            SlotHash = itemSlot?.GetHashCode() ?? 0
        };
    }

    private static void Postfix(
        CollectibleObject __instance,
        Entity byEntity,
        ItemSlot itemSlot,
        Trace? __state)
    {
        if (__state == null) return;

        string after = Describe(itemSlot?.Itemstack);
        string key = $"held-damage:{__state.Side}:{__state.SlotHash}:{__state.Before}:{after}:{__state.Amount}";
        if (!WearableTargetedActionPatches.TryLogOnce(HeldBodyToolDurabilityDiagnosticPatchLog.Keys, key)) return;

        byEntity.World.Logger.VerboseDebug(
            "[animalicabodytools] Held body-tool DamageItem lifecycle: side={0}, itemType={1}, item={2}, slotHash={3}, amount={4}, before={5}, after={6}.",
            __state.Side,
            __state.ItemType,
            __instance.Code,
            __state.SlotHash,
            __state.Amount,
            __state.Before,
            after);
    }

    private static class HeldBodyToolDurabilityDiagnosticPatchLog
    {
        internal static readonly System.Collections.Generic.HashSet<string> Keys = new(StringComparer.Ordinal);
    }
}
