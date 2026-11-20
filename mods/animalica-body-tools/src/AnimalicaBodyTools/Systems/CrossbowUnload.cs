using System;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace AnimalicaBodyTools.Systems;

internal static class CrossbowUnload
{
    private const string Pending = "animalicabodytools:crossbowRefunds";

    // The slot can already hold the replacement. TakeOutWhole also clones the outgoing weapon.
    [HarmonyPatch(typeof(InventoryBase), nameof(InventoryBase.DidModifyItemSlot))]
    internal static class Extraction
    {
        private static void Prefix(InventoryBase __instance, ItemSlot slot, ItemStack? extractedStack)
        {
            if (slot is not ItemSlotCharacter { Type: EnumCharacterDressType.UpperBodyOver }
                || extractedStack?.ItemAttributes?["crossbowPattern"].Exists != true
                || ReferenceEquals(slot.Itemstack, extractedStack)
                || __instance is not InventoryBasePlayer { Api.Side: EnumAppSide.Server, Player: IServerPlayer player }
                || __instance.ClassName != GlobalConstants.characterInvClassName) return;

            extractedStack.TempAttributes.SetBool(RangedCrossbowSystem.RemovedDuringLoad, true);
            QueueRound(player, extractedStack);
        }
    }

    // Death drops can spawn the worn stack directly without running an extraction operation.
    [HarmonyPatch(typeof(InventoryBasePlayer), "spawnItemEntity")]
    internal static class WornDrop
    {
        private static void Prefix(InventoryBasePlayer __instance, ItemStack itemstack)
        {
            if (__instance.Api.Side == EnumAppSide.Server && __instance.ClassName == GlobalConstants.characterInvClassName
                && itemstack.Attributes.HasAttribute(RangedCrossbowSystem.Loaded)
                && ReferenceEquals(__instance[(int)EnumCharacterDressType.UpperBodyOver].Itemstack, itemstack)
                && __instance.Player is IServerPlayer player) QueueRound(player, itemstack);
        }
    }

    private static void QueueRound(IServerPlayer player, ItemStack weapon)
    {
        if (weapon.Attributes.GetItemstack(RangedCrossbowSystem.Loaded) is not { } round) return;
        // Persist with the player until the move commits. Giving it immediately can fill the
        // very hotbar slot into which the engine is about to place the crossbow.
        var pending = player.Entity.Attributes.GetOrAddTreeAttribute(Pending);
        pending.SetItemstack(Guid.NewGuid().ToString("N"), round);
        weapon.Attributes.RemoveAttribute(RangedCrossbowSystem.Loaded);
    }

    internal static void DeliverPending(IServerPlayer player)
    {
        var entity = player.Entity;
        if (entity.Attributes.GetTreeAttribute(Pending) is not { } pending) return;
        foreach (var entry in pending.ToArray())
        {
            // Keep unknown rounds recoverable if their provider is temporarily absent.
            if (entry.Value.GetValue() is not ItemStack round || !round.ResolveBlockOrItem(entity.World)) continue;
            if (entity.Alive) player.InventoryManager.TryGiveItemstack(round, true);
            if (round.StackSize > 0) entity.World.SpawnItemEntity(round, entity.Pos.XYZ);
            pending.RemoveAttribute(entry.Key);
        }
        if (pending.Count == 0) entity.Attributes.RemoveAttribute(Pending);
    }
}
