#nullable enable
using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
namespace FeralKinshipCompanions;

internal sealed record TroughCare(string OwnerUid, string Token, ItemStack Food);
internal static class CompanionTroughCare
{
    internal const string TreeKey = "feralKinshipTroughCare";
    internal sealed record ManualFeed(ItemSlot Source, ItemStack Food, int SourceCount, int TroughCount, int Portion, string OwnerUid);
    internal static bool Supported(BlockEntityTrough? trough)
        => trough?.GetType() == typeof(BlockEntityTrough)
            && trough.Inventory[0]?.GetType() == typeof(ItemSlotTrough)
            && (trough.Block?.GetType() == typeof(BlockTrough) || trough.Block?.GetType() == typeof(BlockTroughDoubleBlock));
    internal static bool SameFood(IWorldAccessor world, ItemStack? a, ItemStack? b)
    {
        try { return a?.Collectible != null && b?.Collectible != null && a.Class == b.Class
            && a.Collectible.Code.Equals(b.Collectible.Code)
            && a.Equals(world, b, GlobalConstants.IgnoredStackAttributes); }
        catch { return false; }
    }
    internal static TroughCare? Get(BlockEntityTrough trough) => trough.GetBehavior<CompanionTroughCareBehavior>()?.State;
    internal static void Clear(BlockEntityTrough trough)
    {
        if (trough.GetBehavior<CompanionTroughCareBehavior>() is { } behavior) behavior.State = null;
    }
    internal static CompanionTroughCareBehavior Attach(BlockEntityTrough trough)
    {
        var behavior = trough.GetBehavior<CompanionTroughCareBehavior>();
        if (behavior == null)
        {
            behavior = new CompanionTroughCareBehavior(trough);
            trough.Behaviors.Add(behavior);
        }
        return behavior;
    }
    private static void Set(BlockEntityTrough trough, TroughCare state) => Attach(trough).State = state;
    internal static bool TryPortion(BlockEntityTrough trough, ItemStack food, out int portion)
    {
        portion = 0;
        try
        {
            if (!Supported(trough) || trough.Api == null || food.Collectible == null) return false;
            var cfg = ItemSlotTrough.getContentConfig(trough.Api.World, trough.contentConfigs, new DummySlot(food));
            if (cfg == null || cfg.QuantityPerFillLevel <= 0 || cfg.MaxFillLevels <= 0) return false;
            long capacity = (long)cfg.QuantityPerFillLevel * cfg.MaxFillLevels;
            if (capacity > int.MaxValue) return false;
            for (int i = 1; i < trough.Inventory.Count; i++) if (!trough.Inventory[i].Empty) return false;
            var current = trough.Inventory[0].Itemstack;
            if (current != null && (!SameFood(trough.Api.World, current, food) || current.StackSize < 0)) return false;
            if (capacity - (current?.StackSize ?? 0) < cfg.QuantityPerFillLevel) return false;
            portion = cfg.QuantityPerFillLevel; return true;
        }
        catch { return false; }
    }
    internal static ManualFeed? BeforeManual(BlockEntityTrough trough, IPlayer player)
    {
        try
        {
            if (trough.Api?.Side != EnumAppSide.Server || player is FertilizerPlayer || !Supported(trough)
                || string.IsNullOrWhiteSpace(player.PlayerUID)
                || !ReferenceEquals(trough.Api.World.PlayerByUid(player.PlayerUID), player)
                || trough.Api.World.Claims.TestAccess(player, trough.Pos, EnumBlockAccessFlags.Use) != EnumWorldAccessResponse.Granted) return null;
            var source = player.InventoryManager.ActiveHotbarSlot;
            if (source?.Itemstack == null || !TryPortion(trough, source.Itemstack, out int portion)
                || source.StackSize < portion) return null;
            var food = source.Itemstack.Clone(); food.StackSize = 1;
            return new(source, food, source.StackSize, trough.Inventory[0].StackSize, portion, player.PlayerUID);
        }
        catch { return null; }
    }
    internal static void AfterManual(BlockEntityTrough trough, bool success, ManualFeed? before)
    {
        try
        {
            if (!success || before == null || !Supported(trough) || trough.Api?.Side != EnumAppSide.Server
                || !ReferenceEquals(trough.Api.World.BlockAccessor.GetBlockEntity(trough.Pos), trough)
                || trough.Inventory[0].StackSize != before.TroughCount + before.Portion
                || before.Source.StackSize != before.SourceCount - before.Portion
                || !SameFood(trough.Api.World, trough.Inventory[0].Itemstack, before.Food)) return;
            // Manual feeds update the food, but never transfer an existing target
            // to another player's companions. Companion/automation fills are excluded.
            string owner = Get(trough)?.OwnerUid ?? before.OwnerUid;
            Set(trough, new(owner, Guid.NewGuid().ToString("N"), before.Food));
            trough.MarkDirty();
        }
        catch (Exception error) { trough.Api?.Logger.Warning("[FeralKinshipCompanions] Trough food memory failed: {0}", error); }
    }
    internal static void Write(BlockEntityTrough trough, ITreeAttribute tree)
    {
        var state = Get(trough);
        if (state == null) { tree.RemoveAttribute(TreeKey); return; }
        var saved = new TreeAttribute(); saved.SetInt("version", 1); saved.SetString("owner", state.OwnerUid);
        saved.SetString("token", state.Token); saved.SetItemstack("food", state.Food);
        tree[TreeKey] = saved;
    }
    internal static void Read(BlockEntityTrough trough, ITreeAttribute tree, IWorldAccessor world)
    {
        Clear(trough);
        if (world.Side != EnumAppSide.Server) return;
        try
        {
            var saved = tree.GetTreeAttribute(TreeKey); var food = saved?.GetItemstack("food");
            string owner = saved?.GetString("owner", "") ?? "", token = saved?.GetString("token", "") ?? "";
            if (saved?.GetInt("version") != 1 || string.IsNullOrWhiteSpace(owner) || owner.Length > 128
                || !Guid.TryParseExact(token, "N", out _) || food == null || !food.ResolveBlockOrItem(world)) return;
            food.StackSize = 1; Set(trough, new(owner, token, food));
        }
        catch { Clear(trough); }
    }
}
