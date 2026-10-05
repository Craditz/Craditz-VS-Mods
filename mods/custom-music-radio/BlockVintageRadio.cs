using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CustomMusicRadio;

public sealed class BlockVintageRadio : Block
{
    public bool Upper => Variant["part"] == "upper";
    public string Facing => Variant["side"];
    // One source of truth for the code and generated assets (validated by the asset checker).
    public static readonly string[] Controls = { "previous", "playpause", "next", "quieter", "louder", "range", "mode", "playlist" };
    public static Cuboidf Button(int index) => new((12 - index * 2.5f) / 16, 5f / 16, .5f / 16,
        (14 - index * 2.5f) / 16, 8f / 16, 2.25f / 16);

    public BlockPos BasePosition(BlockPos pos) => Upper ? pos.DownCopy() : pos.Copy();
    public BlockPos PartnerPosition(BlockPos pos) => Upper ? pos.DownCopy() : pos.UpCopy();
    public bool IsPartner(Block other) => other is BlockVintageRadio radio && radio.Upper != Upper && radio.Facing == Facing;

    public static Vec3d ToLocal(Vec3d hit, string side) => side switch
    {
        "east" => new Vec3d(hit.Z, hit.Y, 1 - hit.X),
        "south" => new Vec3d(1 - hit.X, hit.Y, 1 - hit.Z),
        "west" => new Vec3d(1 - hit.Z, hit.Y, hit.X),
        _ => hit.Clone()
    };

    public static Vec3d ToWorld(Vec3d hit, string side) => side switch
    {
        "east" => new Vec3d(1 - hit.Z, hit.Y, hit.X),
        "south" => new Vec3d(1 - hit.X, hit.Y, 1 - hit.Z),
        "west" => new Vec3d(hit.Z, hit.Y, 1 - hit.X),
        _ => hit.Clone()
    };

    private Cuboidf Rotated(Cuboidf box) => RadioControls.Rotate(box, Facing);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor accessor, BlockPos pos)
    {
        var boxes = new List<Cuboidf>();
        if (Upper)
        {
            for (int i = 0; i < 5; i++) boxes.Add(Rotated(Button(i)));
            boxes.Add(Rotated(RadioControls.SliderRail));
            boxes.Add(Rotated(RadioControls.SliderKnob(VisualRange(accessor, pos), VisualCap(accessor, pos))));
            boxes.Add(Rotated(RadioControls.ModeButton));
            boxes.Add(Rotated(RadioControls.PlaylistButton));
        }
        // Body starts behind the protruding buttons, so it cannot intercept their front hits.
        boxes.Add(Rotated(new Cuboidf(0, 0, 2.5f / 16, 1, 1, 1)));
        return boxes.ToArray();
    }

    private static float VisualRange(IBlockAccessor? accessor, BlockPos pos)
        => (accessor?.GetBlockEntity(pos) as BlockEntityVintageRadio)?.ListeningRange ?? RadioControls.DefaultListeningRange;
    private static float VisualCap(IBlockAccessor? accessor, BlockPos pos)
        => (accessor?.GetBlockEntity(pos) as BlockEntityVintageRadio)?.RangeCap ?? RadioControls.MaxListeningRange;

    public int HitControl(BlockSelection selection, float range = RadioControls.DefaultListeningRange, float cap = RadioControls.MaxListeningRange)
    {
        if (!Upper || selection.HitPosition == null) return -1;
        Vec3d hit = ToLocal(selection.HitPosition, Facing);
        const double epsilon = .0001;
        for (int i = 0; i < 9; i++)
        {
            Cuboidf b = i < 5 ? Button(i) : i == 5 ? RadioControls.SliderRail
                : i == 6 ? RadioControls.SliderKnob(range, cap) : i == 7 ? RadioControls.ModeButton : RadioControls.PlaylistButton;
            if (hit.X >= b.X1 - epsilon && hit.X <= b.X2 + epsilon
                && hit.Y >= b.Y1 - epsilon && hit.Y <= b.Y2 + epsilon
                && hit.Z >= b.Z1 - epsilon && hit.Z <= b.Z2 + epsilon) return i == 8 ? 7 : i == 7 ? 6 : Math.Min(i, 5);
        }
        return -1;
    }

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer player, ItemStack stack, BlockSelection selection, ref string failureCode)
    {
        BlockSelection above = selection.AddPosCopy(0, 1, 0);
        if (!world.BlockAccessor.IsValidPos(selection.Position)
            || world.BlockAccessor.GetChunkAtBlockPos(selection.Position) == null
            || !world.BlockAccessor.IsValidPos(above.Position)
            || world.BlockAccessor.GetChunkAtBlockPos(above.Position) == null)
        {
            failureCode = "notallloaded";
            return false;
        }
        string facing = RadioControls.PlacementFacing(player.Entity.Pos.X, player.Entity.Pos.Z, selection.Position);
        Block? lower = world.GetBlock(CodeWithParts("lower", facing, "off"));
        Block? upper = world.GetBlock(CodeWithParts("upper", facing, "off"));
        if (lower == null || upper == null) { failureCode = "missingradiohalf"; return false; }
        if (!lower.CanPlaceBlock(world, player, selection, ref failureCode)
            || !upper.CanPlaceBlock(world, player, above, ref failureCode)) return false;

        // All validation occurs before either write. These stock placements are synchronous.
        if (!lower.DoPlaceBlock(world, player, selection, stack)) return false;
        if (upper.DoPlaceBlock(world, player, above, stack))
        {
            if (world.Side == EnumAppSide.Server && world.BlockAccessor.GetBlockEntity(selection.Position) is BlockEntityVintageRadio radio)
                radio.SetPlacer(player.PlayerUID);
            return true;
        }
        world.BlockAccessor.SetBlock(0, selection.Position);
        return false;
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer player, BlockSelection selection)
    {
        int control = HitControl(selection, VisualRange(world.BlockAccessor, selection.Position), VisualCap(world.BlockAccessor, selection.Position));
        if (control < 0) return false;
        BlockPos master = BasePosition(selection.Position);
        if (player.Entity.Pos.Dimension != master.dimension
            || player.Entity.Pos.XYZ.SquareDistanceTo(master.X + .5, master.Y + 1.4, master.Z + .5) > 64) return false;
        if (world.Side == EnumAppSide.Server)
        {
            if (world.BlockAccessor.GetBlockEntity(master) is BlockEntityVintageRadio radio)
                radio.Press(control, player, RadioControls.ListeningRangeAtX(ToLocal(selection.HitPosition, Facing).X, radio.RangeCap));
        }
        return true;
    }

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        // Remove this BE first to stop audio before paired block notifications.
        base.OnBlockRemoved(world, pos);
        if (world.Side != EnumAppSide.Server) return;
        BlockPos other = PartnerPosition(pos);
        // SetBlock normally removes the old block before notifying; guard re-entrant pair removal too.
        var system = api.ModLoader.GetModSystem<CustomMusicRadioSystem>();
        if (!system.RemovingRadios.Add(BasePosition(pos))) return;
        try
        {
            if (IsPartner(world.BlockAccessor.GetBlock(other))) world.BlockAccessor.SetBlock(0, other);
        }
        finally { system.RemovingRadios.Remove(BasePosition(pos)); }
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer player, float multiplier = 1)
    {
        if (player != null && (!world.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak)
            || !world.Claims.TryAccess(player, PartnerPosition(pos), EnumBlockAccessFlags.BuildOrBreak))) return;
        base.OnBlockBroken(world, pos, player, multiplier);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer player, float multiplier = 1)
        => new[] { OnPickBlock(world, pos) };

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        => new(world.GetBlock(CodeWithParts("lower", "north", "off")));

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer player)
    {
        int control = HitControl(selection, VisualRange(world.BlockAccessor, selection.Position), VisualCap(world.BlockAccessor, selection.Position));
        return control < 0 ? Array.Empty<WorldInteraction>() : new[] { new WorldInteraction
        {
            ActionLangCode = "custommusicradio:control-" + Controls[control],
            MouseButton = EnumMouseButton.Right
        }};
    }
}
