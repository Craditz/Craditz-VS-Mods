using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace RustboundExpansion;

internal sealed class GemTokens
{
    internal Dictionary<string, string> Active = new();
    internal Dictionary<string, int> Refunds = new();
    private sealed record Saved(Dictionary<string, string> Tokens, Dictionary<string, int> Refunds);
    internal byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(new Saved(Active, Refunds));
    internal void Restore(byte[] bytes)
    {
        if (JsonSerializer.Deserialize<Saved>(bytes) is { } saved) { Active = saved.Tokens; Refunds = saved.Refunds; }
    }
    internal bool CanCreate(string owner) => !Active.ContainsKey(owner);
    internal bool Valid(string owner, string token) => Active.TryGetValue(owner, out var current) && current == token;
    internal bool Consume(string owner, string token) { if (!Valid(owner, token)) return false; Active.Remove(owner); Refunds.Remove(owner); return true; }
}
public sealed class ManaGemItem : Item
{
    internal static bool Conjure(EntityPlayer player)
    {
        var runtime = ExpansionRuntime.For(player.World);
        if (runtime == null || !runtime.Gems.CanCreate(player.PlayerUID)) return false;
        var item = player.World.GetItem(new AssetLocation("rustboundexpansion:managem")); if (item == null) return false;
        string token = Guid.NewGuid().ToString("N");
        var stack = new ItemStack(item); stack.Attributes.SetString("rbx-owner", player.PlayerUID); stack.Attributes.SetString("rbx-token", token);
        int restore = Math.Max(0, rustboundmagic.src.RustboundMagic.FullSpellDictionary["rbxmanagem1"].GetModifiedManaCost(player, "rbxmanagem1")) / 2;
        stack.Attributes.SetInt("rbx-restore", restore);
        if (!player.Player.InventoryManager.TryGiveItemstack(stack)) return false;
        runtime.Gems.Active[player.PlayerUID] = token; runtime.Gems.Refunds[player.PlayerUID] = restore; runtime.PersistGems(); return true;
    }
    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent entity, BlockSelection block, EntitySelection selection, bool first, ref EnumHandHandling handling)
    {
        handling = EnumHandHandling.PreventDefault;
        if (first && entity.Api != null) entity.StartAnimation("eat");
    }
    public override bool OnHeldInteractStep(float seconds, ItemSlot slot, EntityAgent entity, BlockSelection block, EntitySelection selection) => seconds < 1;
    public override bool OnHeldInteractCancel(float seconds, ItemSlot slot, EntityAgent entity, BlockSelection block, EntitySelection selection, EnumItemUseCancelReason reason) { if (entity.Api != null) entity.StopAnimation("eat"); return true; }
    public override void OnHeldInteractStop(float seconds, ItemSlot slot, EntityAgent entity, BlockSelection block, EntitySelection selection)
    {
        if (entity.Api != null && entity.AnimManager != null) entity.StopAnimation("eat");
        if (seconds < 1 || entity is not EntityPlayer p || p.World.Side != EnumAppSide.Server || ControlEffects.Banished(p) || slot.Empty) return;
        var runtime = ExpansionRuntime.For(p.World); if (runtime == null) return;
        string owner = slot.Itemstack.Attributes.GetString("rbx-owner", ""), token = slot.Itemstack.Attributes.GetString("rbx-token", "");
        int restore = runtime.Gems.Refunds.GetValueOrDefault(owner, 50);
        if (!runtime.Gems.Consume(owner, token)) return;
        slot.TakeOut(1); slot.MarkDirty();
        int current = p.WatchedAttributes.GetInt("entitybehavior-resource-currentmana_rm");
        int max = p.WatchedAttributes.GetInt("entitybehavior-resource-totalmaxmana_rm", int.MaxValue);
        p.WatchedAttributes.SetInt("entitybehavior-resource-currentmana_rm", (int)Math.Min(max, (long)current + restore));
        p.WatchedAttributes.MarkPathDirty("entitybehavior-resource-currentmana_rm"); runtime.PersistGems(); Vfx.Mark(p, "arcane");
    }
    // Optional Harmony postfixes bind these vanilla argument names by name.
    public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world, bool debug)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, debug);
        dsc.AppendLine($"Restores {inSlot.Itemstack!.Attributes.GetInt("rbx-restore", 50)} mana. Hold to consume.");
    }
}

internal sealed record TravelPoint(double X, double Y, double Z, int Dimension, float Yaw);
public sealed partial class TravelMarkItem : TintedTravelItem, IContainedMeshSource
{
    internal const string GlyphAssetPath = "rustboundexpansion:shapes/item/travelmark-glyph.json";
    private Shape? glyphShape;
    private bool glyphErrorReported;
    protected override int ItemTint(ItemStack stack) => RuneColors.Tint(stack);
    protected override MeshData BuildMesh(ICoreClientAPI capi, int tint)
        => BuildRuneMesh(capi, tint);

    private MeshData BuildRuneMesh(ICoreClientAPI capi, int tint, ITexPositionSource? textureSource = null)
    {
        // Separate tesselations keep the stone color independent of the rune.
        MeshData stone;
        if (textureSource == null) capi.Tesselator.TesselateItem(this, out stone);
        else capi.Tesselator.TesselateItem(this, out stone, textureSource);
        // Explicit terrain textures already contain their material colors;
        // inventory/held meshes still need the existing vertex tint.
        if (textureSource == null) Tint(stone, RuneColors.StoneBrown);
        try
        {
            // Runtime-only auxiliary shapes are not in the engine's startup
            // item-shape cache. Load the asset and tesselate its Shape directly,
            // as vanilla does for alternate fishing-pole geometry.
            glyphShape ??= capi.Assets.Get(new(GlyphAssetPath)).ToObject<Shape>()
                ?? throw new InvalidDataException("Recall-rune glyph shape is empty.");
            MeshData glyph;
            if (textureSource == null) capi.Tesselator.TesselateShape(this, glyphShape, out glyph);
            else capi.Tesselator.TesselateShape("recall rune symbol", glyphShape, out glyph, textureSource);
            if (textureSource == null) Tint(glyph, tint);
            stone.AddMeshData(glyph);
        }
        catch (Exception error)
        {
            // Optional decoration must never make inventory inaccessible.
            if (!glyphErrorReported)
            {
                glyphErrorReported = true;
                capi.Logger?.Warning("[RustboundExpansion] Could not render recall-rune symbol; using dark-stone model. {0}: {1}", GlyphAssetPath, error);
            }
        }
        return stone;
    }
    public override void OnUnloaded(ICoreAPI api)
    {
        glyphShape = null; glyphErrorReported = false; travelCastTransform = null;
        base.OnUnloaded(api);
    }
    internal static bool IsMark(ItemStack? stack) => stack?.Collectible is TravelMarkItem;
    internal static bool Record(EntityPlayer caster)
    {
        var slot = caster.LeftHandItemSlot; if (!IsMark(slot?.Itemstack)) return false;
        var a = slot!.Itemstack!.Attributes;
        a.SetDouble("rbx-x", caster.Pos.X); a.SetDouble("rbx-y", caster.Pos.Y); a.SetDouble("rbx-z", caster.Pos.Z);
        a.SetInt("rbx-dimension", caster.Pos.Dimension); a.SetFloat("rbx-yaw", caster.Pos.Yaw); a.SetBool("rbx-marked", true);
        a.SetInt("rbx-rune-color", RuneColors.DestinationIndex(caster.Pos.X, caster.Pos.Y, caster.Pos.Z, caster.Pos.Dimension));
        slot.MarkDirty(); return true;
    }
    internal static bool Read(EntityPlayer caster, out TravelPoint destination)
    {
        destination = null!; var stack = caster.LeftHandItemSlot?.Itemstack;
        if (!IsMark(stack) || !stack!.Attributes.GetBool("rbx-marked")) return false;
        var a = stack.Attributes;
        destination = new(a.GetDouble("rbx-x"), a.GetDouble("rbx-y"), a.GetDouble("rbx-z"), a.GetInt("rbx-dimension"), a.GetFloat("rbx-yaw"));
        return Finite(destination);
    }
    internal static bool Finite(TravelPoint point) => double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z)
        && Math.Abs(point.X) < 100000000 && Math.Abs(point.Z) < 100000000 && point.Y > 0 && point.Y < 32768 && point.Dimension >= 0;
    internal static bool Safe(IWorldAccessor world, TravelPoint point)
    {
        if (!Finite(point)) return false;
        var api = world.Api as ICoreServerAPI;
        if (api != null) api.WorldManager.LoadChunkColumnForDimension((int)Math.Floor(point.X / 32), (int)Math.Floor(point.Z / 32), point.Dimension);
        var body = new Cuboidd(point.X - .3, point.Y + .05, point.Z - .3, point.X + .3, point.Y + 1.75, point.Z + .3);
        for (int x = (int)Math.Floor(point.X - .3); x <= (int)Math.Floor(point.X + .3); x++)
            for (int z = (int)Math.Floor(point.Z - .3); z <= (int)Math.Floor(point.Z + .3); z++)
                for (int y = (int)Math.Floor(point.Y + .05); y <= (int)Math.Floor(point.Y + 1.75); y++)
                {
                    var pos = new BlockPos(x, y, z, point.Dimension); var block = world.BlockAccessor.GetBlock(pos);
                    if (block == null || block.IsLiquid() || block.GetCollisionBoxes(world.BlockAccessor, pos)?.Any(b => body.Intersects(b, x, y, z)) == true) return false;
                }
        var below = new BlockPos((int)Math.Floor(point.X), (int)Math.Floor(point.Y - .05), (int)Math.Floor(point.Z), point.Dimension);
        var support = new Cuboidd(point.X - .2, point.Y - .2, point.Z - .2, point.X + .2, point.Y + .01, point.Z + .2);
        return world.BlockAccessor.GetBlock(below)?.GetCollisionBoxes(world.BlockAccessor, below)?.Any(b => support.Intersects(b, below.X, below.Y, below.Z)) == true;
    }
    internal static bool Travel(EntityPlayer caster) => Read(caster, out var point) && TravelTo(caster, point);
    internal static bool TravelTo(EntityPlayer player, TravelPoint point)
    {
        if (!player.Alive || player.MountedOn != null || ControlEffects.Frozen(player) || !Safe(player.World, point)) return false;
        ChannelHooks.Stop(player);
        if (player.Pos.Dimension != point.Dimension) player.ChangeDimension(point.Dimension);
        player.Pos.Yaw = point.Yaw;
        player.TeleportToDouble(point.X, point.Y, point.Z);
        return true;
    }
    public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world, bool debug)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, debug);
        var a = inSlot.Itemstack!.Attributes;
        dsc.AppendLine(a.GetBool("rbx-marked") ? $"Marked: {a.GetDouble("rbx-x"):0.0}, {a.GetDouble("rbx-y"):0.0}, {a.GetDouble("rbx-z"):0.0} (dimension {a.GetInt("rbx-dimension")})" : "Unmarked. Hold in your offhand and cast Mark.");
        dsc.AppendLine($"Rune color: {RuneColors.Name(inSlot.Itemstack)}");
    }
}

public sealed class MarkBagItem : TintedTravelItem, IHeldBag
{
    internal const string SlotColor = "#9B59D0";
    internal const int SackTint = 0xB893FF;
    protected override int ItemTint(ItemStack stack) => SackTint;
    private CollectibleBehaviorHeldBag? implementation;
    private CollectibleBehaviorHeldBag Bag => implementation ??= new(this);
    public bool IsEmpty(ItemStack stack) => Bag.IsEmpty(stack);
    public int GetQuantitySlots(ItemStack stack) => 20;
    public ItemStack[] GetContents(ItemStack stack, IWorldAccessor world) => Bag.GetContents(stack, world);
    public void Store(ItemStack stack, ItemSlotBagContent slot) => Bag.Store(stack, slot);
    public void Clear(ItemStack stack) => Bag.Clear(stack);
    public string GetSlotBgColor(ItemStack stack) => SlotColor;
    EnumItemStorageFlags IHeldBag.GetStorageFlags(ItemStack stack) => EnumItemStorageFlags.General;
    public TagSet GetStorageTags(ItemStack stack) => default;
    public List<ItemSlotBagContent> GetOrCreateSlots(ItemStack stack, InventoryBase inventory, int index, IWorldAccessor world)
    {
        return Bag.GetOrCreateSlots(stack, inventory, index, world).Select(slot => new MarkOnlySlot(inventory, index, slot.SlotIndex)
        { Itemstack = slot.Itemstack, HexBackgroundColor = SlotColor, BackgroundIcon = slot.BackgroundIcon, CanStoreTags = slot.CanStoreTags }).Cast<ItemSlotBagContent>().ToList();
    }
}
internal sealed class MarkOnlySlot : ItemSlotBagContent
{
    internal MarkOnlySlot(InventoryBase inventory, int bagIndex, int slotIndex) : base(inventory, bagIndex, slotIndex, EnumItemStorageFlags.General) { }
    public override bool CanHold(ItemSlot source) => TravelMarkItem.IsMark(source?.Itemstack) && base.CanHold(source!);
    public override bool CanTakeFrom(ItemSlot source, EnumMergePriority priority = EnumMergePriority.AutoMerge) => TravelMarkItem.IsMark(source?.Itemstack) && base.CanTakeFrom(source!, priority);
}

public sealed partial class ExpansionRuntime
{
    internal readonly GemTokens Gems = new();
    private const string GemSaveKey = "rustboundexpansion-mana-gems-v1";
    private void LoadGems()
    {
        var bytes = server?.WorldManager.SaveGame.GetData(GemSaveKey);
        if (bytes != null) Gems.Restore(bytes);
    }
    internal void PersistGems() => server?.WorldManager.SaveGame.StoreData(GemSaveKey, Gems.Serialize());
}
