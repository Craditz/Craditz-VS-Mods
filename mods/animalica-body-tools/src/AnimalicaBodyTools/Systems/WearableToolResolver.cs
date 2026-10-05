using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using AnimalicaBodyTools.Util;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;

namespace AnimalicaBodyTools.Systems;

/// <summary>
/// Resolves equipped body-tool wearables into transient empty-hand tool stacks.
/// Inventory discovery uses the public VS API and candidate metadata is cached
/// until an equipped slot changes.
/// </summary>
internal static class WearableToolResolver
{
    private static readonly FieldInfo? ItemstackField = typeof(ItemSlot).GetField("itemstack", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly AccessTools.FieldRef<ItemSlot, ItemStack?>? ItemstackRef =
        ItemstackField == null ? null : AccessTools.FieldRefAccess<ItemSlot, ItemStack?>(ItemstackField);
    private static readonly HashSet<string> MissingVirtualToolsLogged = new(StringComparer.Ordinal);
    private static readonly string[] EquippedInventoryIds = { GlobalConstants.characterInvClassName, "equipment", "clothing" };

    private static ICoreAPI? api;
    private static ConditionalWeakTable<IPlayer, EquippedToolCache> equippedCaches = new();

    public static void Configure(ICoreAPI coreApi)
    {
        api = coreApi;
        equippedCaches = new ConditionalWeakTable<IPlayer, EquippedToolCache>();
        MissingVirtualToolsLogged.Clear();
    }

    public static bool TryGetVirtualToolForEmptyHand(EntityAgent byEntity, ItemSlot? candidateSlot, out ItemStack virtualStack, out string sourceCode)
    {
        return TryGetVirtualToolForEmptyHand(byEntity, candidateSlot, out virtualStack, out sourceCode, out _);
    }

    public static bool TryGetVirtualToolForEmptyHand(EntityAgent byEntity, ItemSlot? candidateSlot, out ItemStack virtualStack, out string sourceCode, out ItemSlot sourceSlot)
    {
        return TryGetVirtualToolForEmptyHand(byEntity, candidateSlot, _ => 1f, out virtualStack, out sourceCode, out sourceSlot, out _);
    }

    public static bool TryGetVirtualToolForEmptyHand(
        EntityAgent byEntity,
        ItemSlot? candidateSlot,
        System.Func<ItemStack, float> scoreSelector,
        out ItemStack virtualStack,
        out string sourceCode,
        out ItemSlot sourceSlot,
        out float selectedScore)
    {
        return TryGetVirtualToolForEmptyHand(
            byEntity,
            candidateSlot,
            scoreSelector,
            static (stack, selector) => selector(stack),
            out virtualStack,
            out sourceCode,
            out sourceSlot,
            out selectedScore);
    }

    public static bool TryGetVirtualMiningToolForEmptyHand(
        EntityAgent byEntity,
        ItemSlot? candidateSlot,
        Block block,
        BlockSelection blockSel,
        IPlayer player,
        out ItemStack virtualStack,
        out string sourceCode,
        out ItemSlot sourceSlot,
        out float selectedScore)
    {
        MiningScoreContext context = new(block, blockSel, player);
        return TryGetVirtualToolForEmptyHand(
            byEntity,
            candidateSlot,
            context,
            static (stack, state) => stack.Collectible?.GetMiningSpeed(stack, state.BlockSelection, state.Block, state.Player) ?? 0f,
            out virtualStack,
            out sourceCode,
            out sourceSlot,
            out selectedScore);
    }

    private static bool TryGetVirtualToolForEmptyHand<TState>(
        EntityAgent byEntity,
        ItemSlot? candidateSlot,
        TState scoreState,
        System.Func<ItemStack, TState, float> scoreSelector,
        out ItemStack virtualStack,
        out string sourceCode,
        out ItemSlot sourceSlot,
        out float selectedScore)
    {
        virtualStack = null!;
        sourceCode = string.Empty;
        sourceSlot = null!;
        selectedScore = 0f;

        if (api == null || byEntity is not EntityPlayer entityPlayer || entityPlayer.Player == null)
        {
            return false;
        }

        IPlayer player = entityPlayer.Player;
        ItemSlot? activeSlot = candidateSlot ?? player.InventoryManager?.ActiveHotbarSlot;
        if (activeSlot == null || !ReferenceEquals(activeSlot, player.InventoryManager?.ActiveHotbarSlot) || GetRealItemstack(activeSlot) != null)
        {
            return false;
        }

        EquippedToolCache cache = equippedCaches.GetValue(player, static currentPlayer => new EquippedToolCache(currentPlayer));
        cache.EnsureCurrent(api);

        if (cache.Candidates.Count == 0 || !AnimalBodyToolTraitAccessUtil.HolderHasTraitFast(player.Entity))
            return false;

        CachedWearableCandidate? best = null;
        foreach (CachedWearableCandidate candidate in cache.Candidates)
        {
            ItemStack? sourceStack = GetRealItemstack(candidate.SourceSlot);
            if (sourceStack?.Collectible != candidate.SourceCollectible || IsBroken(sourceStack))
            {
                cache.MarkDirty();
                continue;
            }

            float score;
            try
            {
                score = scoreSelector(candidate.ScoringStack, scoreState);
            }
            catch
            {
                continue;
            }

            if (score <= 0f)
            {
                continue;
            }

            if (best == null || score > selectedScore || (Math.Abs(score - selectedScore) < 0.0001f && candidate.Priority > best.Priority))
            {
                best = candidate;
                selectedScore = score;
            }
        }

        if (best == null)
        {
            return false;
        }

        ItemStack? selectedSourceStack = GetRealItemstack(best.SourceSlot);
        if (selectedSourceStack?.Collectible != best.SourceCollectible)
        {
            cache.MarkDirty();
            return false;
        }

        virtualStack = CreateVirtualToolStack(best.VirtualItem, selectedSourceStack);
        sourceCode = best.SourceItemCode;
        sourceSlot = best.SourceSlot;
        return true;
    }

    public static string GetDebugStatus(IPlayer player) => BuildDebugStatus(player, false);
    public static string GetVerboseDebugStatus(IPlayer player) => BuildDebugStatus(player, true);

    private static string BuildDebugStatus(IPlayer player, bool verbose)
    {
        if (api == null)
        {
            return "api=null";
        }

        ItemSlot? activeSlot = player.InventoryManager?.ActiveHotbarSlot;
        if (activeSlot == null)
        {
            return "activeSlot=null";
        }

        bool activeEmpty = GetRealItemstack(activeSlot) == null;
        string activeCode = GetRealItemstack(activeSlot)?.Collectible?.Code?.ToShortString() ?? "empty";
        List<string> nonEmptyParts = new();
        List<string> wearableParts = new();
        int scannedSlots = 0;

        foreach (ItemSlot slot in EnumeratePlayerInventorySlots(player))
        {
            scannedSlots++;
            CollectibleObject? collectible = GetRealItemstack(slot)?.Collectible;
            if (collectible == null)
            {
                continue;
            }

            string code = collectible.Code?.ToShortString() ?? "unknown";
            if (nonEmptyParts.Count < (verbose ? 120 : 30))
            {
                nonEmptyParts.Add(code + ":attrs=" + GetAttributeSummary(collectible));
            }

            if (collectible.Attributes?["animalicaWearableBodyTool"].AsBool(false) != true)
            {
                continue;
            }

            bool traitOk = AnimalBodyToolTraitAccessUtil.HolderHasTrait(player.Entity, out string traitReason);
            string? virtualCode = collectible.Attributes?["animalicaVirtualToolItemCode"].AsString(null);
            bool virtualExists = !string.IsNullOrWhiteSpace(virtualCode) && api.World.GetItem(AssetLocation.Create(virtualCode)) != null;
            if (wearableParts.Count < (verbose ? 120 : 30))
            {
                wearableParts.Add($"{code}:traitOk={traitOk}({traitReason}):virtual={virtualCode ?? "null"}:virtualExists={virtualExists}");
            }
        }

        bool resolvedOk = TryGetVirtualToolForEmptyHand(player.Entity, activeSlot, out ItemStack virtualStack, out string sourceCode);
        string resolved = resolvedOk ? $"yes source={sourceCode} virtual={virtualStack.Collectible?.Code}" : "no";
        return $"invManager={player.InventoryManager?.GetType().FullName ?? "null"}; activeEmpty={activeEmpty}; activeCode={activeCode}; "
               + $"scannedSlots={scannedSlots}; nonEmpty=[{string.Join(" | ", nonEmptyParts)}]; "
               + $"wearableCount={wearableParts.Count}; resolved={resolved}; wearables=[{string.Join(" | ", wearableParts)}]";
    }

    public static string GetInventoryManagerMemberSummary(IPlayer player)
    {
        IPlayerInventoryManager? manager = player.InventoryManager;
        if (manager == null)
        {
            return "inventoryManager=null";
        }

        string inventories = string.Join(" | ", manager.Inventories.Values.Select(inv => $"{inv.ClassName}:{inv.InventoryID}:{inv.Count}"));
        return $"inventoryManagerType={manager.GetType().FullName}; inventories=[{inventories}]";
    }

    private static IEnumerable<ItemSlot> EnumeratePlayerInventorySlots(IPlayer player)
    {
        IPlayerInventoryManager? manager = player.InventoryManager;
        if (manager == null)
        {
            yield break;
        }

        foreach (InventoryBase inventory in manager.InventoriesOrdered)
        {
            if (IsRiskyInventory(inventory))
            {
                continue;
            }

            for (int i = 0; i < inventory.Count; i++)
            {
                ItemSlot? slot = inventory[i];
                if (slot != null)
                {
                    yield return slot;
                }
            }
        }
    }

    public static ItemStack? GetRealItemstack(ItemSlot slot)
    {
        return ItemstackRef == null ? slot.Itemstack : ItemstackRef(slot);
    }

    public static bool RemoveBrokenSourceTool(ItemSlot sourceSlot)
    {
        if (GetRealItemstack(sourceSlot) == null)
        {
            return true;
        }

        sourceSlot.TakeOutWhole();
        if (GetRealItemstack(sourceSlot) != null)
        {
            sourceSlot.Itemstack = null;
        }

        if (GetRealItemstack(sourceSlot) != null && ItemstackRef != null)
        {
            ItemstackRef(sourceSlot) = null;
        }

        sourceSlot.MarkDirty();
        return GetRealItemstack(sourceSlot) == null;
    }

    public static bool SyncVirtualToolStateToSource(ItemStack virtualStack, ItemSlot sourceSlot, bool preserveDurability)
    {
        ItemStack? sourceStack = GetRealItemstack(sourceSlot);
        if (virtualStack?.Attributes == null || sourceStack == null)
        {
            return false;
        }

        bool hadDurability = sourceStack.Attributes.TryGetInt("durability").HasValue;
        int durability = sourceStack.Attributes.GetInt("durability", sourceStack.Collectible?.GetMaxDurability(sourceStack) ?? 0);
        sourceStack.Attributes = virtualStack.Attributes.Clone();

        if (preserveDurability)
        {
            if (hadDurability) sourceStack.Attributes.SetInt("durability", durability);
            else sourceStack.Attributes.RemoveAttribute("durability");
        }

        sourceSlot.MarkDirty();
        return true;
    }

    private static ItemStack CreateVirtualToolStack(Item virtualItem, ItemStack sourceStack)
    {
        ItemStack virtualStack = new(virtualItem);
        if (sourceStack.Attributes != null)
        {
            virtualStack.Attributes = sourceStack.Attributes.Clone();
        }
        return virtualStack;
    }

    private static bool IsBroken(ItemStack stack)
    {
        CollectibleObject? collectible = stack.Collectible;
        if (collectible == null) return false;
        int maxDurability = collectible.GetMaxDurability(stack);
        return maxDurability > 0 && (stack.Attributes?.GetInt("durability", maxDurability) ?? maxDurability) <= 0;
    }

    private static bool IsRiskyInventory(IInventory inventory)
    {
        string typeName = inventory.GetType().FullName ?? inventory.GetType().Name;
        return typeName.Contains("Creative", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEquippedInventory(IInventory inventory)
    {
        foreach (string id in EquippedInventoryIds)
        {
            if (string.Equals(inventory.ClassName, id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(inventory.InventoryID, id, StringComparison.OrdinalIgnoreCase)
                || inventory.InventoryID.StartsWith(id + "-", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static int GetEquippedInventoryPriority(IInventory inventory)
    {
        for (int i = 0; i < EquippedInventoryIds.Length; i++)
        {
            string id = EquippedInventoryIds[i];
            if (string.Equals(inventory.ClassName, id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(inventory.InventoryID, id, StringComparison.OrdinalIgnoreCase)
                || inventory.InventoryID.StartsWith(id + "-", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return EquippedInventoryIds.Length;
    }

    private static string GetAttributeSummary(CollectibleObject collectible)
    {
        string json = collectible.Attributes?.ToString() ?? "none";
        if (json.Length > 180) json = json[..180] + "...";
        return json.Replace('\n', ' ').Replace('\r', ' ');
    }

    private sealed class EquippedToolCache
    {
        private readonly IPlayer player;
        private readonly List<IInventory> inventories = new();
        private readonly Action<int> slotModifiedHandler;
        private int knownInventoryCount = -1;
        private bool dirty = true;

        public EquippedToolCache(IPlayer player)
        {
            this.player = player;
            slotModifiedHandler = _ => dirty = true;
        }

        public List<CachedWearableCandidate> Candidates { get; } = new();
        public void MarkDirty() => dirty = true;

        public void EnsureCurrent(ICoreAPI coreApi)
        {
            IPlayerInventoryManager manager = player.InventoryManager;
            if (knownInventoryCount != manager.Inventories.Count) RefreshInventories(manager);
            if (!dirty) return;

            dirty = false;
            Candidates.Clear();
            foreach (IInventory inventory in inventories)
            {
                for (int i = 0; i < inventory.Count; i++)
                {
                    ItemSlot? slot = inventory[i];
                    ItemStack? sourceStack = slot == null ? null : GetRealItemstack(slot);
                    CollectibleObject? collectible = sourceStack?.Collectible;
                    if (slot == null || sourceStack == null || collectible?.Attributes?["animalicaWearableBodyTool"].AsBool(false) != true) continue;

                    string? virtualCodeRaw = collectible.Attributes?["animalicaVirtualToolItemCode"].AsString(null);
                    if (string.IsNullOrWhiteSpace(virtualCodeRaw)) continue;

                    Item? virtualItem = coreApi.World.GetItem(AssetLocation.Create(virtualCodeRaw));
                    if (virtualItem == null)
                    {
                        if (MissingVirtualToolsLogged.Add(virtualCodeRaw))
                        {
                            coreApi.World.Logger.Warning("[animalicabodytools] Wearable empty-hand virtual tool {0} could not be found.", virtualCodeRaw);
                        }
                        continue;
                    }

                    Candidates.Add(new CachedWearableCandidate(
                        slot,
                        collectible,
                        collectible.Code?.ToShortString() ?? "unknown",
                        virtualItem,
                        CreateVirtualToolStack(virtualItem, sourceStack),
                        collectible.Attributes?["animalicaVirtualToolPriority"].AsInt(0) ?? 0));
                }
            }
        }

        private void RefreshInventories(IPlayerInventoryManager manager)
        {
            foreach (IInventory inventory in inventories) inventory.SlotModified -= slotModifiedHandler;
            inventories.Clear();
            foreach (IInventory inventory in manager.Inventories.Values
                         .Where(IsEquippedInventory)
                         .OrderBy(GetEquippedInventoryPriority)
                         .ThenBy(inv => inv.InventoryID, StringComparer.Ordinal))
            {
                if (!IsRiskyInventory(inventory) && !inventories.Contains(inventory))
                {
                    inventories.Add(inventory);
                    inventory.SlotModified += slotModifiedHandler;
                }
            }
            knownInventoryCount = manager.Inventories.Count;
            dirty = true;
        }
    }

    private sealed record CachedWearableCandidate(
        ItemSlot SourceSlot,
        CollectibleObject SourceCollectible,
        string SourceItemCode,
        Item VirtualItem,
        ItemStack ScoringStack,
        int Priority);

    private readonly record struct MiningScoreContext(Block Block, BlockSelection BlockSelection, IPlayer Player);
}
