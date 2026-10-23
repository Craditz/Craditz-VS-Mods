#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

// Owned modular blocks instead of vanilla's unqualified monolithic fillers:
// each cell has local geometry and persisted instance identity. Compact flaps
// occupy Size x Size total. Old largefoxflap codes retain their exact lifecycle
// until broken; their single returned item places a compact replacement.
public sealed class BlockFeralKinshipLargeFlap : Block, IClaimTraverseable
{
    internal readonly record struct Cell(int Size, string Side, int U, int V, bool Legacy = false)
    {
        internal int Width => Legacy ? Size + 2 : Size;
        internal int Height => Legacy ? Size + 1 : Size;
        internal bool Root => U == 0 && V == 0;
        internal (int X, int Z) Axis => Side switch { "north" => (1, 0), "east" => (0, 1), "south" => (-1, 0), _ => (0, -1) };
        internal BlockPos At(BlockPos root, int u, int v) { var a = Axis; return root.AddCopy(a.X * u, v, a.Z * u); }
        internal BlockPos RootAt(BlockPos pos) => At(pos, -U, -V);
        internal AssetLocation Code(int u, int v, string? side = null) => new("feralkinshipcompanions", $"{(Legacy ? "largefoxflap" : "compactfoxflap")}-{Size}-{u}-{v}-{side ?? Side}");
        internal AssetLocation ItemCode => new("feralkinshipcompanions", $"compactfoxflap-{Size}-0-0-north");
    }
    internal static bool Describe(Block block, out Cell cell)
    {
        cell = default;
        if (block is not BlockFeralKinshipLargeFlap || block.Code?.Domain != "feralkinshipcompanions") return false;
        var p = block.Code.Path.Split('-');
        if (p.Length != 5 || p[0] is not ("largefoxflap" or "compactfoxflap") || !int.TryParse(p[1], out int n) || n is not (2 or 3)
            || !int.TryParse(p[2], out int u) || u < 0 || u >= (p[0] == "largefoxflap" ? n + 2 : n)
            || !int.TryParse(p[3], out int v) || v < 0 || v >= (p[0] == "largefoxflap" ? n + 1 : n)
            || p[4] is not ("north" or "east" or "south" or "west")) return false;
        cell = new(n, p[4], u, v, p[0] == "largefoxflap"); return true;
    }
    private sealed class Operations { internal readonly HashSet<(int, int, int, int)> Active = new(); }
    private static readonly ConditionalWeakTable<IWorldAccessor, Operations> operations = new();
    private static (int, int, int, int) Key(BlockPos p) => (p.dimension, p.X, p.Y, p.Z);
    private static bool Busy(IWorldAccessor w, BlockPos root) => operations.GetOrCreateValue(w).Active.Contains(Key(root));
    private Cuboidf[]? cachedBoxes;
    internal static Cuboidf[] Geometry(Cell cell)
    {
        if (!cell.Legacy) return Array.Empty<Cuboidf>(); // Flexible leather, no rigid posts/header.
        // Posts occupy complete side cells; header starts above the passage.
        if (cell.U != 0 && cell.U != cell.Size + 1 && cell.V != cell.Size) return Array.Empty<Cuboidf>();
        var b = new Cuboidf(0, 0, .4375f, 1, cell.V == cell.Size ? .125f : 1, .5625f);
        if (cell.Side is "east" or "west") b = new Cuboidf(.4375f, b.Y1, 0, .5625f, b.Y2, 1);
        return new[] { b };
    }
    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor accessor, BlockPos pos)
        => cachedBoxes ??= Describe(this, out Cell c) ? Geometry(c) : Array.Empty<Cuboidf>();
    public override int GetRetention(BlockPos pos, BlockFacing facing, EnumRetentionType type) => 3;
    public override float GetTraversalCost(BlockPos pos, EnumAICreatureType type) => 0;

    private static IEnumerable<(BlockPos Pos, int U, int V)> Cells(Cell c, BlockPos root)
    { for (int v = 0; v < c.Height; v++) for (int u = 0; u < c.Width; u++) yield return (c.At(root, u, v), u, v); }
    private static bool Loaded(IBlockAccessor a, BlockPos p) => a.IsValidPos(p) && a.GetChunkAtBlockPos(p) is { Disposed: false };
    private static bool AllLoaded(IBlockAccessor a, Cell c, BlockPos root)
        => Cells(c, root).All(x => Loaded(a, x.Pos)) && Loaded(a, root.DownCopy()) && Loaded(a, c.At(root, c.Width - 1, -1));
    private bool Supported(IBlockAccessor a, Cell c, BlockPos root)
        => new[] { root.DownCopy(), c.At(root, c.Width - 1, -1) }
            .All(p => a.GetBlock(p, BlockLayersAccess.Solid) is Block support && support.Id != 0
                && support.CanAttachBlockAt(a, this, p, BlockFacing.UP));

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack stack, BlockSelection selection, ref string failureCode)
    {
        if (!Describe(this, out Cell c) || !c.Root) { failureCode = "largeflap-root-only"; return false; }
        c = c with { Legacy = false }; // Includes old inventory stacks without converting placed blocks.
        string side = byPlayer == null ? c.Side : SuggestedHVOrientation(byPlayer, selection)[0].Code;
        if (world.GetBlock(c.Code(0, 0, side)) is not BlockFeralKinshipLargeFlap root) { failureCode = "largeflap-missing-variant"; return false; }
        if (!root.CanPlaceBlock(world, byPlayer!, selection, ref failureCode)) return false;
        bool placed = root.DoPlaceBlock(world, byPlayer!, selection, stack);
        if (!placed) failureCode = "largeflap-placement-failed";
        return placed;
    }
    public override bool CanPlaceBlock(IWorldAccessor world, IPlayer player, BlockSelection selection, ref string failureCode)
    {
        if (!Describe(this, out Cell c) || !c.Root) { failureCode = "largeflap-root-only"; return false; }
        var a = world.BlockAccessor; var root = selection.Position;
        if (!AllLoaded(a, c, root)) { failureCode = "largeflap-unloaded"; return false; }
        foreach (var x in Cells(c, root))
        {
            if (player != null && !world.Claims.TryAccess(player, x.Pos, EnumBlockAccessFlags.BuildOrBreak)) { failureCode = "claimed"; return false; }
            // Air only: never erase containers, decors, plants or fluid, even
            // when a provider calls those blocks replaceable.
            if (a.GetBlock(x.Pos, BlockLayersAccess.Solid).Id != 0 || a.GetBlock(x.Pos, BlockLayersAccess.Fluid).Id != 0
                || a.GetBlockEntity(x.Pos) != null) { failureCode = "largeflap-clear-footprint"; return false; }
            if (world.GetBlock(c.Code(x.U, x.V)) is not BlockFeralKinshipLargeFlap part) { failureCode = "largeflap-missing-variant"; return false; }
            var boxes = part.GetCollisionBoxes(a, x.Pos);
            if (boxes.Length > 0 && world.GetIntersectingEntities(x.Pos, boxes, e => e.IsInteractable).Length > 0) { failureCode = "entityintersecting"; return false; }
        }
        if (!Supported(a, c, root)) { failureCode = "largeflap-support"; return false; }
        return true;
    }
    public override bool DoPlaceBlock(IWorldAccessor world, IPlayer player, BlockSelection selection, ItemStack stack)
    {
        string failure = string.Empty;
        if (!CanPlaceBlock(world, player, selection, ref failure)) return false;
        if (world.Side == EnumAppSide.Client) return true; // Server sends the entire committed structure.
        if (RuntimeEnv.ServerMainThreadId <= 0 || RuntimeEnv.ServerMainThreadId != Environment.CurrentManagedThreadId) return false;
        Describe(this, out Cell c); BlockPos root = selection.Position.Copy();
        var active = operations.GetOrCreateValue(world).Active;
        if (!active.Add(Key(root))) return false;
        string token = Guid.NewGuid().ToString("N"); var written = new List<(BlockPos Pos, int Id)>();
        try
        {
            // Resolve every type before the first mutation; hold root/part
            // neighbour audits until the transaction is complete.
            var plan = Cells(c, root).Select(x => (x.Pos, Part: world.GetBlock(c.Code(x.U, x.V))
                ?? throw new InvalidOperationException("Missing flap type"))).ToArray();
            foreach (var x in plan)
            {
                written.Add((x.Pos, x.Part.Id));
                world.BlockAccessor.SetBlock(x.Part.Id, x.Pos);
                if (world.BlockAccessor.GetBlockEntity(x.Pos) is not BlockEntityFeralKinshipLargeFlap be)
                    throw new InvalidOperationException("Flap part block entity was not created");
                be.SetIdentity(token, false);
            }
            foreach (var x in plan)
                if (world.BlockAccessor.GetBlock(x.Pos, BlockLayersAccess.Solid).Id != x.Part.Id
                    || world.BlockAccessor.GetBlockEntity(x.Pos) is not BlockEntityFeralKinshipLargeFlap be || be.Token != token)
                    throw new InvalidOperationException("Flap part publication did not match the placement plan");
            foreach (var p in written) ((BlockEntityFeralKinshipLargeFlap)world.BlockAccessor.GetBlockEntity(p.Pos)).SetIdentity(token, true);
            return true;
        }
        catch (Exception error)
        {
            foreach (var p in written.AsEnumerable().Reverse())
            {
                var current = world.BlockAccessor.GetBlock(p.Pos, BlockLayersAccess.Solid);
                if (current.Id == p.Id && Describe(current, out Cell part) && part.RootAt(p.Pos).Equals(root)
                    && (world.BlockAccessor.GetBlockEntity(p.Pos) is not BlockEntityFeralKinshipLargeFlap be || be.Token is "" || be.Token == token))
                    world.BlockAccessor.SetBlock(0, p.Pos);
            }
            world.Logger.Warning("[FeralKinshipCompanions] Large flap placement rolled back: {0}", error.Message);
            return false;
        }
        finally { active.Remove(Key(root)); }
    }
    private static bool Matching(IBlockAccessor a, Cell c, BlockPos root, BlockPos p, int u, int v, string token)
        => a.GetBlock(p, BlockLayersAccess.Solid).Code?.Equals(c.Code(u, v)) == true
            && a.GetBlockEntity(p) is BlockEntityFeralKinshipLargeFlap be && be.Token == token;

    private bool RemoveStructure(IWorldAccessor world, BlockPos root, Cell c, string token, IPlayer? player, bool drop, BlockPos? skip = null)
    {
        var a = world.BlockAccessor;
        if (world.Side != EnumAppSide.Server || string.IsNullOrEmpty(token) || !AllLoaded(a, c, root)) return false;
        var members = Cells(c, root).Where(x => Matching(a, c, root, x.Pos, x.U, x.V, token)).ToArray();
        if (player != null && members.Any(x => !world.Claims.TryAccess(player, x.Pos, EnumBlockAccessFlags.BuildOrBreak))) return false;
        var active = operations.GetOrCreateValue(world).Active;
        if (!active.Add(Key(root))) return false;
        bool hadRoot = Matching(a, c, root, root, 0, 0, token)
            && a.GetBlockEntity(root) is BlockEntityFeralKinshipLargeFlap rootBe && rootBe.Committed;
        try
        {
            // Root last: a failed removal leaves its persisted instance ID to
            // finish exact matching cleanup later, without a premature drop.
            foreach (var x in members.OrderBy(x => x.U == 0 && x.V == 0 ? 1 : 0))
                if (skip == null || !x.Pos.Equals(skip)) a.SetBlock(0, x.Pos);
            if (drop && hadRoot && (player == null || player.WorldData.CurrentGameMode != EnumGameMode.Creative)
                && world.GetBlock(c.ItemCode) is Block item)
                world.SpawnItemEntity(new ItemStack(item), root.ToVec3d().Add(.5, .2, .5));
            return true;
        }
        catch (Exception error)
        {
            world.Logger.Warning("[FeralKinshipCompanions] Large flap removal deferred; inspect remaining root/parts: {0}", error.Message);
            return false;
        }
        finally { active.Remove(Key(root)); }
    }
    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer player, float dropQuantityMultiplier = 1)
    {
        if (!Describe(this, out Cell c) || world.Side != EnumAppSide.Server) return;
        BlockPos root = c.RootAt(pos);
        if (Busy(world, root) || world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid).Id != Id) return;
        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityFeralKinshipLargeFlap be && be.Token.Length == 32)
            RemoveStructure(world, root, c, be.Token, player, dropQuantityMultiplier > 0);
        else if (player == null || world.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak)) world.BlockAccessor.SetBlock(0, pos);
    }
    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        if (world.Side == EnumAppSide.Server && Describe(this, out Cell c))
        {
            BlockPos root = c.RootAt(pos);
            if (!Busy(world, root) && world.BlockAccessor.GetBlockEntity(pos) is BlockEntityFeralKinshipLargeFlap be)
                RemoveStructure(world, root, c, be.Token, null, false, pos);
        }
        base.OnBlockRemoved(world, pos);
    }
    public override void OnBlockExploded(IWorldAccessor world, BlockPos pos, BlockPos center, EnumBlastType type, string ignitedByPlayerUid)
    {
        IPlayer? actor = string.IsNullOrEmpty(ignitedByPlayerUid) ? null : world.PlayerByUid(ignitedByPlayerUid);
        if (!string.IsNullOrEmpty(ignitedByPlayerUid) && actor == null) return;
        // One structure, one native probability roll, never one roll per cell.
        if (Describe(this, out Cell c) && world.BlockAccessor.GetBlockEntity(pos) is BlockEntityFeralKinshipLargeFlap be)
            RemoveStructure(world, c.RootAt(pos), c, be.Token,
                actor,
                world.Rand.NextDouble() < ExplosionDropChance(world, pos, type));
    }
    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neighbour) => Audit(world, pos);
    internal void TickAudit(IWorldAccessor world, BlockPos pos)
    {
        // Stable followers only check the root link; the root performs the
        // complete bounded support/member audit. Neighbour events still run
        // the full check immediately, including at the far support post.
        if (Describe(this, out Cell c) && !c.Root)
        {
            var a = world.BlockAccessor; BlockPos root = c.RootAt(pos);
            if (!Loaded(a, root)) return;
            if (a.GetBlockEntity(pos) is BlockEntityFeralKinshipLargeFlap be
                && a.GetBlockEntity(root) is BlockEntityFeralKinshipLargeFlap rootBe && rootBe.Committed
                && Matching(a, c, root, root, 0, 0, be.Token)) return;
        }
        Audit(world, pos);
    }
    internal void Audit(IWorldAccessor world, BlockPos pos)
    {
        if (world.Side != EnumAppSide.Server || !Describe(this, out Cell c) || world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid).Id != Id) return;
        var a = world.BlockAccessor; BlockPos root = c.RootAt(pos);
        if (Busy(world, root) || !AllLoaded(a, c, root)) return; // Unloaded is UNKNOWN, never missing.
        if (a.GetBlockEntity(pos) is not BlockEntityFeralKinshipLargeFlap be || be.Token.Length != 32)
        { a.SetBlock(0, pos); return; }
        if (!Matching(a, c, root, root, 0, 0, be.Token)) { a.SetBlock(0, pos); return; }
        bool ready = ((BlockEntityFeralKinshipLargeFlap)a.GetBlockEntity(root)).Committed;
        if (!ready || !Supported(a, c, root) || Cells(c, root).Any(x => !Matching(a, c, root, x.Pos, x.U, x.V, be.Token)))
            RemoveStructure(world, root, c, be.Token, null, ready);
    }
    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer player, float multiplier = 1)
    {
        if (!Describe(this, out Cell c) || !c.Root || multiplier <= 0
            || world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityFeralKinshipLargeFlap be || !be.Committed) return Array.Empty<ItemStack>();
        return new[] { new ItemStack(world.GetBlock(c.ItemCode)) };
    }
    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        => Describe(this, out Cell c) ? new ItemStack(world.GetBlock(c.ItemCode)) : base.OnPickBlock(world, pos);
    public override AssetLocation GetRotatedBlockCode(int angle)
    {
        if (!Describe(this, out Cell c)) return Code;
        var face = BlockFacing.FromCode(c.Side);
        string side = BlockFacing.HORIZONTALS_ANGLEORDER[GameMath.Mod(face.HorizontalAngleIndex - angle / 90, 4)].Code;
        return c.Code(c.U, c.V, side);
    }
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if (Describe(this, out Cell c)) dsc.AppendLine(Lang.Get("feralkinshipcompanions:largeflap-info", c.Size, c.Width, c.Height));
    }
}

public sealed class BlockEntityFeralKinshipLargeFlap : BlockEntity
{
    internal string Token { get; private set; } = string.Empty;
    internal bool Committed { get; private set; }
    internal void SetIdentity(string token, bool committed) { Token = token; Committed = committed; MarkDirty(true); }
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(_ => { if (Block is BlockFeralKinshipLargeFlap flap) flap.TickAudit(Api.World, Pos); }, 10000, 500);
    }
    public override void ToTreeAttributes(ITreeAttribute tree)
    { base.ToTreeAttributes(tree); tree.SetString("largeFlapInstance", Token); tree.SetBool("largeFlapCommitted", Committed); }
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
    {
        base.FromTreeAttributes(tree, world);
        string value = tree.GetString("largeFlapInstance", string.Empty);
        Token = value.Length == 32 && value.All(Uri.IsHexDigit) ? value : string.Empty;
        Committed = Token.Length == 32 && tree.GetBool("largeFlapCommitted");
    }
}
