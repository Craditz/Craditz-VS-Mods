#nullable enable

using System;
using Vintagestory.API.Common;

namespace FeralKinshipCompanions;

/// <summary>Four real stacks; runtime tree reservations never own or copy items.</summary>
internal sealed class WorkCartAxeInventory : InventoryGeneric
{
    internal const int ToolSlotCount = 4;
    private readonly object?[] reservations = new object?[ToolSlotCount];
    internal int ClientReservedMask;
    internal readonly string[] ClientDescriptions = new string[ToolSlotCount];
    internal Action? ReservationChanged;
    internal System.Func<IPlayer, bool>? AccessCheck;
    internal bool QuarryMode { get; private set; }
    internal WorkCartQuarryState? QuarryState;
    internal WorkCartMiningState? MiningState;

    public override bool CanPlayerAccess(IPlayer player, Vintagestory.API.Common.Entities.EntityPos position)
        => Api?.Side == EnumAppSide.Client || (HasOpened(player) && AccessCheck?.Invoke(player) == true);

    internal WorkCartAxeInventory(ICoreAPI? api = null, bool quarry = false)
        : base(quarry ? 5 : ToolSlotCount, "feralkinshipworkcarttools", null!, api!,
            (index, inventory) => new ItemSlotWorkCartAxe(inventory, index == 4))
        { QuarryMode = quarry; }

    // A snapshot may arrive after the native container and dialog have opened.
    // Keep their inventory and slot references, event handlers and stacks intact.
    internal void Configure(bool quarry)
    {
        QuarryMode = quarry;
        if (quarry && Count < 5) AddSlots(5 - Count);
    }

    internal bool IsReserved(int index) => index >= 0 && index < ToolSlotCount
        && (reservations[index] != null || (ClientReservedMask & (1 << index)) != 0);

    internal object? ReservationAt(int index) => reservations[index];

    internal static bool IsAxe(ItemStack? stack) => stack?.Collectible != null
        && stack.Collectible.GetTool(new DummySlot(stack)) == EnumTool.Axe;

    internal static bool IsRock(ItemStack? stack) => stack?.Collectible?.Code is AssetLocation code
        && code.Domain == "game" && code.Path.StartsWith("stone-", StringComparison.Ordinal)
        && stack.Class == EnumItemClass.Item;

    internal bool Accepts(int index, ItemStack? stack) => QuarryMode && index == 4 ? IsRock(stack)
        : stack?.Collectible != null && stack.Collectible.GetTool(new DummySlot(stack))
            == (QuarryMode ? EnumTool.Pickaxe : EnumTool.Axe);

    internal bool IsUsableTool(ItemSlot slot, int tier) => Accepts(GetSlotId(slot), slot.Itemstack)
        && slot.Itemstack!.Collectible.GetRemainingDurability(slot.Itemstack) > 0
        && slot.Itemstack.Collectible.GetToolTier(slot) >= tier;

    internal static bool IsUsable(ItemSlot slot, int requiredTier) => IsAxe(slot.Itemstack)
        && slot.Itemstack!.Collectible.GetRemainingDurability(slot.Itemstack) > 0
        && slot.Itemstack.Collectible.GetToolTier(slot) >= requiredTier;

    internal int FindAvailable(int requiredTier)
    {
        for (int i = 0; i < ToolSlotCount; i++)
            if (!IsReserved(i) && IsUsableTool(this[i], requiredTier)) return i;
        return -1;
    }

    internal int Reserve(object job, int requiredTier)
    {
        for (int i = 0; i < ToolSlotCount; i++)
            if (ReferenceEquals(reservations[i], job))
                return IsUsableTool(this[i], requiredTier) ? i : -1;
        int index = FindAvailable(requiredTier);
        if (index < 0) return -1;
        reservations[index] = job;
        ReservationChanged?.Invoke();
        return index;
    }

    internal bool Owns(int index, object job) => index >= 0 && index < ToolSlotCount
        && ReferenceEquals(reservations[index], job);

    internal void Release(object job)
    {
        bool changed = false;
        for (int i = 0; i < ToolSlotCount; i++)
        {
            if (!ReferenceEquals(reservations[i], job)) continue;
            reservations[i] = null;
            changed = true;
        }
        if (changed) ReservationChanged?.Invoke();
    }

    internal int ReservedMask
    {
        get
        {
            int mask = 0;
            for (int i = 0; i < ToolSlotCount; i++) if (IsReserved(i)) mask |= 1 << i;
            return mask;
        }
    }
}

internal sealed class ItemSlotWorkCartAxe : ItemSlot
{
    internal ItemSlotWorkCartAxe(InventoryBase inventory, bool rocks = false) : base(inventory)
    {
        if (!rocks) MaxSlotStackSize = 1;
    }

    private bool Reserved => ((WorkCartAxeInventory)inventory).IsReserved(inventory.GetSlotId(this));
    private bool Accepts(ItemStack? stack) => ((WorkCartAxeInventory)inventory).Accepts(inventory.GetSlotId(this), stack);
    public override bool CanTake() => !Reserved && base.CanTake();
    public override bool CanHold(ItemSlot sourceSlot) => !Reserved
        && Accepts(sourceSlot?.Itemstack) && base.CanHold(sourceSlot!);
    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        => CanHold(sourceSlot) && base.CanTakeFrom(sourceSlot, priority);
    public override bool TryFlipWith(ItemSlot other) => !Reserved && base.TryFlipWith(other);
    public override int GetRemainingSlotSpace(ItemStack stack) => !Reserved && Accepts(stack)
        && (Itemstack == null || Itemstack.Collectible.Code.Equals(stack.Collectible.Code))
        ? Math.Min(base.GetRemainingSlotSpace(stack), Math.Max(0, stack.Collectible.MaxStackSize - StackSize)) : 0;
    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        // Includes creative middle-click, drag, swap and ordinary clicks.
        if (!Reserved) base.ActivateSlot(sourceSlot, ref op);
    }
}
