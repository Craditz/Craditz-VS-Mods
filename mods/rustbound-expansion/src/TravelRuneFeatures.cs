using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace RustboundExpansion;

public sealed partial class TravelMarkItem
{
    private ModelTransform? travelCastTransform;

    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack stack, EnumItemRenderTarget target, ref ItemRenderInfo info)
    {
        base.OnBeforeRender(capi, stack, target, ref info);
        var player = capi.World?.Player?.Entity;
        if (UseCastingPose(player, stack, target))
        {
            // Restore the complete previous transform, leaving Rustbound's
            // animated casting hand/attachment position untouched.
            travelCastTransform ??= Attributes["travelCastTransform"].AsObject<ModelTransform>();
            info.Transform = travelCastTransform;
        }
    }

    internal static bool UseCastingPose(EntityPlayer? player, ItemStack stack, EnumItemRenderTarget target)
        => player != null && target == EnumItemRenderTarget.HandTpOff
        && ReferenceEquals(player.LeftHandItemSlot?.Itemstack, stack)
        && player.WatchedAttributes.GetBool("entitybehavior-iscastingspell_rm")
        && player.WatchedAttributes.GetString("entitybehavior-castingtool-currentlyselectedspell-name_rm", "")
            is "rbxmark1" or "rbxteleport1" or "rbxgatetravel1" or "rbxtwowaygate1";

    public MeshData GenMesh(ItemSlot slot, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
    {
        var capi = (ICoreClientAPI)api;
        // Ground storage is terrain geometry and therefore needs the BLOCK
        // atlas, whereas held/inventory meshes use the ITEM atlas.
        // Chunk terrain overwrites vertex RGBA with lighting. Its material
        // colors must live in texture pixels rather than vertex tints.
        var textures = new Dictionary<string, AssetLocation>
        {
            ["skin"] = new("rustboundexpansion:item/travelmark/stone"),
            ["rune"] = new("rustboundexpansion:item/travelmark/rune-" + RuneColors.Tint(slot.Itemstack!).ToString("x6"))
        };
        var source = new ContainedTextureSource(capi, targetAtlas, textures, "For placed recall rune " + Code);
        return BuildRuneMesh(capi, RuneColors.Tint(slot.Itemstack!), source);
    }

    public string GetMeshCacheKey(ItemSlot slot) => Code + ":rune-" + RuneColors.Tint(slot.Itemstack!).ToString("X6");

    public override string GetHeldItemName(ItemStack stack)
    {
        string name = stack.Attributes.GetString(RuneNames.Attribute, "");
        return name.Length == 0 ? base.GetHeldItemName(stack) : "Travel Mark: " + name;
    }
}

internal static class RuneNames
{
    internal const string Attribute = "rbx-rune-name";

    internal static void Register(ICoreServerAPI api)
    {
        api.ChatCommands.Create("rune")
            .WithDescription("Name the Travel Mark in your offhand, or main hand if no rune is in the offhand.")
            .RequiresPrivilege(Privilege.chat).RequiresPlayer()
            .BeginSubCommand("name")
                .WithDescription("Rename a held Travel Mark (1–48 characters).")
                .WithArgs(api.ChatCommands.Parsers.All("name"))
                .HandleWith(args => Rename(args, (string)args[0]))
            .EndSubCommand()
            .BeginSubCommand("clearname")
                .WithDescription("Remove the held Travel Mark's custom name.")
                .HandleWith(args => Rename(args, null))
            .EndSubCommand();
    }

    internal static ItemSlot? HeldRune(EntityPlayer player)
        => TravelMarkItem.IsMark(player.LeftHandItemSlot?.Itemstack) ? player.LeftHandItemSlot
         : TravelMarkItem.IsMark(player.RightHandItemSlot?.Itemstack) ? player.RightHandItemSlot : null;

    internal static bool ValidName(string value) => value.Length is > 0 and <= 48
        && value.All(c => !char.IsControl(c) && c is not '<' and not '>');

    internal static bool Set(ItemSlot slot, string? text)
    {
        var stack = slot.Itemstack;
        if (!TravelMarkItem.IsMark(stack)) return false;
        if (text == null) stack!.Attributes.RemoveAttribute(Attribute);
        else
        {
            text = text.Trim();
            if (!ValidName(text)) return false;
            stack!.Attributes.SetString(Attribute, text);
        }
        slot.MarkDirty();
        return true;
    }

    private static TextCommandResult Rename(TextCommandCallingArgs args, string? name)
    {
        var player = args.Caller.Player?.Entity;
        var slot = player == null ? null : HeldRune(player);
        if (slot == null) return TextCommandResult.Error("Hold a Travel Mark in your offhand or main hand first.");
        if (!Set(slot, name)) return TextCommandResult.Error("Use 1–48 characters without line breaks or < >.");
        return TextCommandResult.Success(name == null ? "Travel Mark name cleared." : "Named Travel Mark: " + slot.Itemstack!.Attributes.GetString(Attribute));
    }
}
