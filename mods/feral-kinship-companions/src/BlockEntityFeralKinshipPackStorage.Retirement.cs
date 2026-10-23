#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;

public partial class BlockEntityFeralKinshipPackStorage
{
    private const string BackupKey = "feralKinshipLegacyWarningOriginal";
    private const string StateKey = "feralKinshipLegacyWarningState";
    private const string LightKey = "feralKinshipLegacyWarningLight";
    private const int SafeSlotLimit = 4096;
    private ITreeAttribute? originalSource, sourceMetadata, inventoryMetadata, pendingSource;
    private readonly Dictionary<int, ItemStack> expectedItems = new();
    private int restoreCapacity = LegacyStorageRetirement.Capacity;
    private string? preflightError;
    private bool retirementBlocked, savedLocks, oldPutLocked, oldTakeLocked;
    private int lightVersion;
    internal bool RetirementBlocked => retirementBlocked;
    internal int RetirementCapacity => Inventory?.Count ?? 0;

    private static byte[] AttributeBytes(IAttribute attribute)
    {
        using MemoryStream data = new();
        using BinaryWriter writer = new(data);
        attribute.ToBytes(writer); writer.Flush(); return data.ToArray();
    }
    private static ITreeAttribute Clone(ITreeAttribute tree) => tree.Clone();
    private static void CopyMissing(ITreeAttribute source, ITreeAttribute target)
    {
        foreach (var pair in source)
            if (!target.HasAttribute(pair.Key)) target[pair.Key] = pair.Value.Clone();
    }
    private static ITreeAttribute WithoutReceipt(ITreeAttribute tree)
    {
        ITreeAttribute result = Clone(tree);
        result.RemoveAttribute(BackupKey); result.RemoveAttribute(StateKey); result.RemoveAttribute(LightKey);
        return result;
    }
    private void RetainBlocked(string reason)
    {
        retirementBlocked = true;
        if (Inventory != null)
        {
            if (!savedLocks) { oldPutLocked = Inventory.PutLocked; oldTakeLocked = Inventory.TakeLocked; savedLocks = true; }
            Inventory.PutLocked = Inventory.TakeLocked = true;
        }
        if (Api?.Side == EnumAppSide.Server)
            Api.Logger.Warning("[FeralKinshipCompanions] Legacy warning container at {0} is blocked: {1}. Original serialized state is retained; this is not an empty successful conversion.", Pos, reason);
    }
    private void PrepareRestore(ITreeAttribute tree)
    {
        pendingSource = Clone(tree);
        originalSource = tree.GetTreeAttribute(BackupKey) is ITreeAttribute previous ? Clone(previous) : WithoutReceipt(tree);
        sourceMetadata = WithoutReceipt(tree); sourceMetadata.RemoveAttribute("inventory");
        inventoryMetadata = tree.GetTreeAttribute("inventory") is ITreeAttribute inv ? Clone(inv) : null;
        lightVersion = tree.GetInt(LightKey);
        expectedItems.Clear(); restoreCapacity = LegacyStorageRetirement.Capacity; preflightError = null;
        if (tree.GetBool("isPerPlayer")) { preflightError = "unsupported per-player inventory profile"; return; }
        if (inventoryMetadata == null) { preflightError = "saved inventory tree is absent"; return; }
        int count = inventoryMetadata.GetInt("qslots", -1);
        if (count < 0 || count > SafeSlotLimit) { preflightError = "saved capacity is invalid or exceeds the safe restoration bound"; return; }
        restoreCapacity = Math.Max(restoreCapacity, count);
        ITreeAttribute? slots = inventoryMetadata.GetTreeAttribute("slots");
        if (slots == null) { preflightError = "saved slot tree is absent"; return; }
        foreach (var pair in slots)
        {
            if (!int.TryParse(pair.Key, out int index) || index < 0 || index >= SafeSlotLimit)
            { preflightError = "saved slot index is invalid or exceeds the safe restoration bound"; return; }
            ItemStack? stack = slots.GetItemstack(pair.Key);
            if (stack == null || stack.StackSize <= 0)
            { preflightError = "saved slot contains an unsupported or invalid item stack"; return; }
            expectedItems[index] = stack.Clone();
            restoreCapacity = Math.Max(restoreCapacity, index + 1);
        }
    }
    private void GrowInventory()
    {
        if (Inventory is InventoryGeneric generic && generic.Count < restoreCapacity)
            generic.AddSlots(restoreCapacity - generic.Count);
        quantitySlots = Inventory?.Count ?? restoreCapacity;
        quantityColumns = 9;
    }
    private void CompleteRestore(IWorldAccessor world)
    {
        GrowInventory();
        if (pendingSource == null) return;
        if (preflightError != null) { RetainBlocked(preflightError); return; }
        foreach (var pair in expectedItems)
        {
            ItemStack? live = Inventory[pair.Key].Itemstack;
            if (live == null) { live = pair.Value.Clone(); Inventory[pair.Key].Itemstack = live; }
            if (!live.ResolveBlockOrItem(world) || live.Collectible == null)
            { RetainBlocked("an original saved item provider or mapping is unavailable"); return; }
            if (live.Class != pair.Value.Class || live.StackSize != pair.Value.StackSize
                || !AttributeBytes(live.Attributes).SequenceEqual(AttributeBytes(pair.Value.Attributes)))
            { RetainBlocked("an original saved item changed during native restoration"); return; }
        }
        retirementBlocked = false;
        if (savedLocks) { Inventory.PutLocked = oldPutLocked; Inventory.TakeLocked = oldTakeLocked; savedLocks = false; }
        pendingSource = null; expectedItems.Clear();
    }
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        PrepareRestore(tree);
        if (preflightError != null)
        {
            type = tree.GetString("type", "storage");
            InitInventory(Block);
            RetainBlocked(preflightError);
            return;
        }
        // Native slot restoration iterates the existing slot array. Grow before it
        // reads old qslots/indices; never let an unexpected larger inventory truncate.
        GrowInventory();
        try
        {
            base.FromTreeAttributes(Clone(tree), worldForResolving);
            CompleteRestore(worldForResolving);
        }
        catch (Exception error)
        {
            // Retain the input tree if native restoration rejects malformed data.
            // Do not reinterpret that failure as an empty inventory.
            RetainBlocked("native restoration rejected saved data (" + error.GetType().Name + ")");
        }
    }
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        CompleteRestore(api.World);
        if (api is ICoreServerAPI server && lightVersion < 1)
        {
            server.Event.EnqueueMainThreadTask(() =>
            {
                if (Api == null || Api.World.BlockAccessor.GetBlockEntity(Pos) != this) return;
                try
                {
                    server.WorldManager.FullRelight(Pos.Copy(), Pos.Copy(), true);
                    lightVersion = 1;
                    MarkDirty(true);
                }
                catch (Exception error)
                {
                    api.Logger.Warning("[FeralKinshipCompanions] Legacy warning local relight remains pending at {0}: {1}.", Pos, error.GetType().Name);
                }
            }, "feralkinship-legacy-warning-relight");
        }
    }
    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);
        if (byItemStack != null)
        {
            sourceMetadata ??= new TreeAttribute();
            sourceMetadata.SetItemstack("feralKinshipLegacySourceItem", byItemStack.Clone());
        }
        GrowInventory();
        if (isPerPlayer) RetainBlocked("unsupported per-player item profile");
    }
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (retirementBlocked && pendingSource != null)
        {
            foreach (var pair in pendingSource) tree[pair.Key] = pair.Value.Clone();
        }
        else
        {
            if (sourceMetadata != null) CopyMissing(sourceMetadata, tree);
            if (inventoryMetadata != null && tree.GetTreeAttribute("inventory") is ITreeAttribute target)
            {
                // Opaque metadata survives, but old slots must never be merged back
                // into a live inventory after the player has removed their contents.
                foreach (var pair in inventoryMetadata)
                    if (pair.Key != "qslots" && pair.Key != "slots" && !target.HasAttribute(pair.Key)) target[pair.Key] = pair.Value.Clone();
            }
        }
        if (originalSource != null) tree[BackupKey] = originalSource.Clone();
        tree.SetString(StateKey, retirementBlocked ? "blocked" : "ready");
        tree.SetInt(LightKey, lightVersion);
    }
    public override bool OnPlayerRightClick(IPlayer byPlayer, Vintagestory.API.Common.BlockSelection blockSel)
    {
        if (retirementBlocked) return true;
        return base.OnPlayerRightClick(byPlayer, blockSel);
    }
    public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
    {
        if (retirementBlocked) return;
        base.OnReceivedClientPacket(player, packetid, data);
    }
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        if (retirementBlocked) { dsc.AppendLine(Lang.Get("feralkinshipcompanions:legacy-warning-blocked")); return; }
        base.GetBlockInfo(forPlayer, dsc);
        dsc.AppendLine(Lang.Get("feralkinshipcompanions:legacy-warning-salvage"));
    }
}
