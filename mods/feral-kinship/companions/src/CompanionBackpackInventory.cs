#nullable enable

using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

internal sealed class ItemSlotCompanionBackpack : ItemSlotBackpack
{
    internal ItemSlotCompanionBackpack(InventoryBase inventory) : base(inventory)
    {
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        ItemStack? stack = sourceSlot?.Itemstack;
        // ItemSlotSurvival deliberately rejects non-empty held bags. This is
        // an equipment slot, so use the native backpack-slot rules instead:
        // both empty and filled IHeldBag stacks are valid, still one at a time.
        return stack?.Collectible?.GetCollectibleInterface<IHeldBag>() != null
            && base.CanHold(sourceSlot);
    }

    public override int GetRemainingSlotSpace(ItemStack forItemstack)
    {
        return forItemstack?.Collectible?.GetCollectibleInterface<IHeldBag>() == null
            ? 0
            : Math.Min(1, base.GetRemainingSlotSpace(forItemstack));
    }
}

public sealed partial class EntityBehaviorFeralKinshipFoxSocial
{
    internal const int BackpackEquipmentPacketOffset = 1 << 16;
    internal const int BackpackContentsPacketOffset = 2 << 16;
    private const int BackpackPacketLowBitsMask = (1 << 16) - 1;

    private InventoryGeneric? companionBackpackEquipment;
    private InventoryGeneric? companionBackpackContents;
    private BagInventory? companionBagInventory;
    private GuiDialogFeralKinshipCompanionBackpack? companionBackpackDialog;
    private bool rebuildingCompanionBackpack;
    private ItemStack? pendingCompanionBackpackStack;
    private bool pendingCompanionBackpackLocked;
    private string pendingCompanionBackpackTitle = string.Empty;
    private bool pendingCompanionBackpackRefresh;
    private bool companionBackpackRefreshQueued;

    internal InventoryGeneric? CompanionBackpackEquipment => companionBackpackEquipment;
    internal InventoryGeneric? CompanionBackpackContents => companionBackpackContents;
    internal ItemStack? CompanionBackpackStack => companionBackpackEquipment?[0].Itemstack;
    internal bool HasCompanionBackpack =>
        CompanionBackpackStack?.Collectible?.GetCollectibleInterface<IHeldBag>() != null;

    private void InitializeCompanionBackpack()
    {
        if (companionBackpackEquipment != null) return;

        companionBackpackEquipment = new InventoryGeneric(
            1,
            "companionbackpackequipment",
            entity.EntityId.ToString(),
            entity.Api,
            (_, inventory) => new ItemSlotCompanionBackpack(inventory));
        companionBackpackEquipment.SlotModified += OnCompanionBackpackEquipmentModified;

        ItemStack? saved = entity.WatchedAttributes.GetItemstack(
            FeralKinshipCompanionSystem.CompanionBackpackItemKey);
        if (saved != null)
        {
            saved.ResolveBlockOrItem(entity.World);
            if (saved.Collectible?.GetCollectibleInterface<IHeldBag>() != null)
            {
                companionBackpackEquipment[0].Itemstack = saved;
            }
        }

        RebuildCompanionBackpackContents();
        RefreshCompanionBackpackLock();
    }

    internal void SetCompanionBackpackFromLedger(ItemStack? stack)
    {
        InitializeCompanionBackpack();
        if (companionBackpackEquipment == null) return;

        rebuildingCompanionBackpack = true;
        companionBackpackEquipment[0].Itemstack = stack?.Clone();
        rebuildingCompanionBackpack = false;
        SaveCompanionBackpackToEntity();
        RebuildCompanionBackpackContents();
    }

    internal ItemStack? RemoveCompanionBackpackForDeath()
    {
        InitializeCompanionBackpack();
        if (companionBackpackEquipment == null || companionBackpackEquipment[0].Empty) return null;

        rebuildingCompanionBackpack = true;
        ItemStack stack = companionBackpackEquipment[0].TakeOutWhole();
        rebuildingCompanionBackpack = false;
        SaveCompanionBackpackToEntity();
        RebuildCompanionBackpackContents();
        return stack;
    }

    internal void RefreshCompanionBackpackLock()
    {
        bool locked = FeralKinshipCompanionSystem.IsBackpackDeliveryActive(entity);
        if (companionBackpackEquipment != null)
        {
            companionBackpackEquipment.PutLocked = locked;
            companionBackpackEquipment.TakeLocked = locked;
        }
        if (companionBackpackContents != null)
        {
            companionBackpackContents.PutLocked = locked;
            companionBackpackContents.TakeLocked = locked;
        }
    }

    private void OnCompanionBackpackEquipmentModified(int slotId)
    {
        if (rebuildingCompanionBackpack) return;

        ItemStack? stack = CompanionBackpackStack;
        if (stack != null && stack.Collectible?.GetCollectibleInterface<IHeldBag>() == null)
        {
            // The slot's CanHold check handles normal interaction. This is a
            // second boundary for malformed or forged inventory packets.
            rebuildingCompanionBackpack = true;
            companionBackpackEquipment![0].Itemstack = null;
            rebuildingCompanionBackpack = false;
        }

        SaveCompanionBackpackToEntity();
        RebuildCompanionBackpackContents();
        GetSystem().OnCompanionBackpackChanged(entity);
    }

    private void OnCompanionBackpackContentsModified(int slotId)
    {
        if (companionBackpackContents == null || companionBagInventory == null) return;
        if (companionBackpackContents[slotId] is ItemSlotBagContent bagSlot)
        {
            companionBagInventory.SaveSlotIntoBag(bagSlot);
            SaveCompanionBackpackToEntity();
            GetSystem().OnCompanionBackpackChanged(entity);
        }
    }

    private void SaveCompanionBackpackToEntity()
    {
        ItemStack? stack = CompanionBackpackStack;
        if (stack == null)
        {
            entity.WatchedAttributes.RemoveAttribute(FeralKinshipCompanionSystem.CompanionBackpackItemKey);
        }
        else
        {
            entity.WatchedAttributes.SetItemstack(
                FeralKinshipCompanionSystem.CompanionBackpackItemKey,
                stack);
        }
        entity.WatchedAttributes.MarkPathDirty(FeralKinshipCompanionSystem.CompanionBackpackItemKey);
    }

    private void RebuildCompanionBackpackContents()
    {
        InventoryGeneric? previousContents = companionBackpackContents;
        string[] previousViewers = entity.Api.Side == EnumAppSide.Server && previousContents != null
            ? previousContents.openedByPlayerGUIds.ToArray()
            : Array.Empty<string>();
        if (companionBackpackContents != null)
        {
            companionBackpackContents.SlotModified -= OnCompanionBackpackContentsModified;
        }

        companionBagInventory = new BagInventory(
            entity.Api,
            companionBackpackEquipment == null
                ? Array.Empty<ItemSlot>()
                : new ItemSlot[] { companionBackpackEquipment[0] });
        companionBackpackContents = new InventoryGeneric(entity.Api);
        companionBagInventory.ReloadBagInventory(
            companionBackpackContents!,
            companionBackpackEquipment == null
                ? Array.Empty<ItemSlot>()
                : new ItemSlot[] { companionBackpackEquipment[0] });
        companionBackpackContents.Init(
            companionBagInventory.Count,
            "companionbackpackcontents",
            entity.EntityId.ToString(),
            (slotId, _) => companionBagInventory[slotId]);
        companionBackpackContents.SlotModified += OnCompanionBackpackContentsModified;
        if (entity.Api.Side == EnumAppSide.Server)
        {
            foreach (string playerUid in previousViewers)
            {
                IPlayer? player = entity.World.PlayerByUid(playerUid);
                if (player == null) continue;
                if (previousContents != null) player.InventoryManager.CloseInventory(previousContents);
                player.InventoryManager.OpenInventory(companionBackpackContents);
            }
        }
        RefreshCompanionBackpackLock();
    }

    internal void OpenCompanionBackpackServer(IServerPlayer player)
    {
        InitializeCompanionBackpack();
        RefreshCompanionBackpackLock();
        if (companionBackpackEquipment == null || companionBackpackContents == null) return;
        player.InventoryManager.OpenInventory(companionBackpackEquipment);
        player.InventoryManager.OpenInventory(companionBackpackContents);
    }

    internal void CloseCompanionBackpackServer(IServerPlayer player)
    {
        if (companionBackpackEquipment != null)
        {
            player.InventoryManager.CloseInventory(companionBackpackEquipment);
        }
        if (companionBackpackContents != null)
        {
            player.InventoryManager.CloseInventory(companionBackpackContents);
        }
    }

    internal void OpenCompanionBackpackClient(ItemStack? stack, bool locked, string title)
    {
        InitializeCompanionBackpack();
        if (companionBackpackEquipment == null) return;

        ICoreClientAPI capi = (ICoreClientAPI)entity.Api;
        if (companionBackpackDialog?.IsOpened() == true
            && !capi.World.Player.InventoryManager.MouseItemSlot.Empty)
        {
            pendingCompanionBackpackStack = stack?.Clone();
            pendingCompanionBackpackLocked = locked;
            pendingCompanionBackpackTitle = title;
            pendingCompanionBackpackRefresh = true;
            companionBackpackEquipment.PutLocked = true;
            companionBackpackEquipment.TakeLocked = true;
            if (companionBackpackContents != null)
            {
                companionBackpackContents.PutLocked = true;
                companionBackpackContents.TakeLocked = true;
            }
            QueueCompanionBackpackRefresh(capi);
            return;
        }

        ApplyCompanionBackpackClientState(capi, stack, locked, title);
    }

    private void QueueCompanionBackpackRefresh(ICoreClientAPI capi)
    {
        if (companionBackpackRefreshQueued) return;
        companionBackpackRefreshQueued = true;
        capi.Event.RegisterCallback(_ =>
        {
            companionBackpackRefreshQueued = false;
            if (!pendingCompanionBackpackRefresh
                || companionBackpackDialog?.IsOpened() != true)
            {
                pendingCompanionBackpackRefresh = false;
                pendingCompanionBackpackStack = null;
                return;
            }
            if (!capi.World.Player.InventoryManager.MouseItemSlot.Empty)
            {
                QueueCompanionBackpackRefresh(capi);
                return;
            }

            ItemStack? pendingStack = pendingCompanionBackpackStack;
            bool pendingLocked = pendingCompanionBackpackLocked;
            string pendingTitle = pendingCompanionBackpackTitle;
            pendingCompanionBackpackRefresh = false;
            pendingCompanionBackpackStack = null;
            ApplyCompanionBackpackClientState(capi, pendingStack, pendingLocked, pendingTitle);
        }, 100);
    }

    private void ApplyCompanionBackpackClientState(
        ICoreClientAPI capi,
        ItemStack? stack,
        bool locked,
        string title)
    {
        if (companionBackpackEquipment == null) return;

        rebuildingCompanionBackpack = true;
        companionBackpackEquipment[0].Itemstack = stack;
        rebuildingCompanionBackpack = false;
        RebuildCompanionBackpackContents();
        InventoryGeneric contents = companionBackpackContents!;
        companionBackpackEquipment.PutLocked = locked;
        companionBackpackEquipment.TakeLocked = locked;
        if (companionBackpackContents != null)
        {
            companionBackpackContents.PutLocked = locked;
            companionBackpackContents.TakeLocked = locked;
        }

        companionBackpackDialog?.CloseForRefresh();
        companionBackpackDialog?.Dispose();
        companionBackpackDialog = new GuiDialogFeralKinshipCompanionBackpack(
            capi,
            GetSystem(),
            entity,
            companionBackpackEquipment,
            contents,
            title,
            locked);
        if (companionBackpackDialog.TryOpen())
        {
            IPlayer player = capi.World.Player;
            player.InventoryManager.OpenInventory(companionBackpackEquipment);
            player.InventoryManager.OpenInventory(contents);
        }
    }

    public override void OnReceivedClientPacket(
        IServerPlayer player,
        int packetid,
        byte[] data,
        ref EnumHandling handled)
    {
        base.OnReceivedClientPacket(player, packetid, data, ref handled);
        int workspace = packetid >> 16;
        if (workspace is not 1 and not 2) return;

        handled = EnumHandling.PreventSubsequent;
        if (!GetSystem().CanPlayerUseCompanionBackpack(player, entity)
            || FeralKinshipCompanionSystem.IsBackpackDeliveryActive(entity))
        {
            return;
        }

        int inventoryPacketId = packetid & BackpackPacketLowBitsMask;
        if (inventoryPacketId >= 1000) return;

        InitializeCompanionBackpack();
        InventoryGeneric? inventory = workspace == 1
            ? companionBackpackEquipment
            : companionBackpackContents;
        if (inventory?.HasOpened(player) != true) return;

        inventory.InvNetworkUtil.HandleClientPacket(player, inventoryPacketId, data);
        SaveCompanionBackpackToEntity();
        GetSystem().OnCompanionBackpackChanged(entity);
        if (workspace == 1)
        {
            if (companionBackpackContents != null)
            {
                player.InventoryManager.OpenInventory(companionBackpackContents);
            }
            GetSystem().SendCompanionBackpackState(player, entity, reopen: true);
        }
    }

    private void DisposeCompanionBackpack()
    {
        if (entity.Api.Side == EnumAppSide.Server)
        {
            System.Collections.Generic.IEnumerable<string> equipmentViewers =
                companionBackpackEquipment?.openedByPlayerGUIds ?? Enumerable.Empty<string>();
            System.Collections.Generic.IEnumerable<string> contentViewers =
                companionBackpackContents?.openedByPlayerGUIds ?? Enumerable.Empty<string>();
            string[] viewers = equipmentViewers
                .Concat(contentViewers)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            foreach (string playerUid in viewers)
            {
                IPlayer? player = entity.World.PlayerByUid(playerUid);
                if (player == null) continue;
                if (companionBackpackEquipment != null)
                    player.InventoryManager.CloseInventory(companionBackpackEquipment);
                if (companionBackpackContents != null)
                    player.InventoryManager.CloseInventory(companionBackpackContents);
            }
        }
        companionBackpackDialog?.TryClose();
        companionBackpackDialog?.Dispose();
        companionBackpackDialog = null;
        pendingCompanionBackpackRefresh = false;
        pendingCompanionBackpackStack = null;
        companionBackpackRefreshQueued = false;
        companionBackpackEquipment = null;
        companionBackpackContents = null;
        companionBagInventory = null;
    }
}
