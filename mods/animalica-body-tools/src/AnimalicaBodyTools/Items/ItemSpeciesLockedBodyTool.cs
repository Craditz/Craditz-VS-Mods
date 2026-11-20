using System;
using System.Linq;
using System.Runtime.CompilerServices;
using AnimalicaBodyTools.Util;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace AnimalicaBodyTools.Items;

/// <summary>
/// Body tools whose permissions are defined by the Animal Body Tools trait in
/// Trait Restrictions. Tunneling Claws can also use HoD's wellspring mode.
/// </summary>
public sealed class ItemSpeciesLockedBodyTool : Item
{
    private static readonly ConditionalWeakTable<ItemSpeciesLockedBodyTool, ItemSpeciesLockedBodyTool> SwordViews = new();

    internal ItemStack FangCapAttackView(ItemStack stack)
    {
        var item = SwordViews.GetValue(this, static fangCap =>
        {
            var view = (ItemSpeciesLockedBodyTool)fangCap.MemberwiseClone();
            view.Tool = EnumTool.Sword;
            return view;
        });
        return new ItemStack(item, stack.StackSize) { Attributes = stack.Attributes };
    }

    private const string HydrateModId = "hydrateordiedrate";
    private const string DigModeCode = "hydrateordiedrate:digmode";
    private const string WellModeCode = "hydrateordiedrate:digwellspring";
    private const string WellBehaviorType = "HydrateOrDiedrate.Wells.WellWater.BehaviorPickaxeWellMode";

    public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
    {
        return FindWellModes(slot, forPlayer, blockSel)?.Modes ?? base.GetToolModes(slot, forPlayer, blockSel);
    }

    public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection)
    {
        return FindWellModes(slot, byPlayer as IClientPlayer, blockSelection)?.Behavior.GetToolMode(slot, byPlayer, blockSelection)
            ?? base.GetToolMode(slot, byPlayer, blockSelection);
    }

    public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
    {
        var wellModes = FindWellModes(slot, byPlayer as IClientPlayer, blockSelection);
        if (wellModes is not { } provider)
        {
            base.SetToolMode(slot, byPlayer, blockSelection, toolMode);
            return;
        }

        if ((uint)toolMode < (uint)provider.Modes.Length && slot.Itemstack?.Collectible == this
            && byPlayer?.Entity != null && AnimalBodyToolTraitAccessUtil.HolderHasTraitFast(byPlayer.Entity))
        {
            provider.Behavior.SetToolMode(slot, byPlayer, blockSelection, toolMode);
        }
    }

    public override bool OnBlockBrokenWith(IWorldAccessor world, Entity byEntity, ItemSlot itemslot,
        BlockSelection blockSel, float dropQuantityMultiplier = 1)
    {
        return TryDigWell(world, byEntity, itemslot, blockSel, dropQuantityMultiplier)
            || base.OnBlockBrokenWith(world, byEntity, itemslot, blockSel, dropQuantityMultiplier);
    }

    internal bool TryDigWell(IWorldAccessor world, Entity byEntity, ItemSlot slot, BlockSelection selection,
        float dropQuantityMultiplier)
    {
        if (slot.Itemstack?.Collectible != this || byEntity is not EntityPlayer player
            || player.Player is not IPlayer byPlayer || !AnimalBodyToolTraitAccessUtil.HolderHasTraitFast(player)
            || FindWellModes(slot, byPlayer as IClientPlayer, selection) is not { } provider)
        {
            return false;
        }

        int selected = provider.Behavior.GetToolMode(slot, byPlayer, selection);
        if ((uint)selected >= (uint)provider.Modes.Length || provider.Modes[selected].Code?.ToString() != WellModeCode)
        {
            return false;
        }

        var handling = EnumHandling.PassThrough;
        // HoD owns target eligibility, block placement, and the wear/consumption
        // decision. Its 2.5.10 wellspring path skips normal block break and wear.
        return provider.Behavior.OnBlockBrokenWith(world, byEntity, slot, selection, dropQuantityMultiplier, ref handling)
            && handling == EnumHandling.PreventDefault;
    }

    private (CollectibleBehavior Behavior, SkillItem[] Modes)? FindWellModes(
        ItemSlot slot, IClientPlayer? forPlayer, BlockSelection selection)
    {
        if (Tool != EnumTool.Pickaxe || Code?.Domain != "animalicabodytools"
            || !Code.Path.StartsWith("tunnelingclaws-", StringComparison.Ordinal)
            || api?.ModLoader.GetMod(HydrateModId)?.Info.Version != "2.5.10")
        {
            return null;
        }

        // Reuse HoD's loaded behavior and icons. A different mode list (for
        // example one extended by XSkills) stays outside this integration.
        var behavior = api.World.GetItem(new AssetLocation("game:pickaxe-copper"))?.CollectibleBehaviors
            .FirstOrDefault(candidate => candidate.GetType().FullName == WellBehaviorType);
        if (behavior?.GetToolModes(slot, forPlayer, selection) is not { Length: 2 } modes
            || modes[0].Code?.ToString() != DigModeCode || modes[1].Code?.ToString() != WellModeCode)
        {
            return null;
        }

        return (behavior, modes);
    }
}
