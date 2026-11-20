using System;
using System.Linq;
using System.Runtime.CompilerServices;
using AnimalicaBodyTools.HarmonyPatches;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace AnimalicaBodyTools.Items;

/// <summary>
/// Animal claw tool. Trait Restrictions owns permission to use or equip it.
///
/// This derives from the base game's ItemKnife so right-click corpse harvesting
/// follows vanilla knife behavior.
/// </summary>
public sealed class ItemSpeciesLockedClaws : ItemKnife
{
    private static readonly ConditionalWeakTable<ItemSpeciesLockedClaws, ItemSpeciesLockedClaws> DiggingViews = new();

    internal bool SupportsDigging => Tool == EnumTool.Knife && MiningSpeed?.Keys.Any(IsDiggingMaterial) == true;

    internal static bool CanDig(ItemStack stack, IWorldAccessor world, BlockSelection selection)
    {
        if (stack.Item is not ItemSpeciesLockedClaws { Tool: EnumTool.Knife } claws
            || selection.Position == null)
        {
            return false;
        }

        var block = selection.Block ?? world.BlockAccessor.GetBlock(selection.Position);
        var material = block.GetBlockMaterial(world.BlockAccessor, selection.Position);
        return IsDiggingMaterial(material) && claws.MiningSpeed?.ContainsKey(material) == true;
    }

    private static bool IsDiggingMaterial(EnumBlockMaterial material) =>
        material is EnumBlockMaterial.Soil or EnumBlockMaterial.Gravel or EnumBlockMaterial.Sand or EnumBlockMaterial.Snow;

    internal static ItemStack DiggingView(ItemStack original)
    {
        var claws = (ItemSpeciesLockedClaws)original.Item;
        var view = DiggingViews.GetValue(claws, static item => item.CreateDiggingView());
        return new ItemStack(view, original.StackSize) { Attributes = original.Attributes };
    }

    private ItemSpeciesLockedClaws CreateDiggingView()
    {
        // Keep the actual item's stats and collectible behaviors. This copy
        // is only used in the action slot and is never registered or saved.
        var view = (ItemSpeciesLockedClaws)MemberwiseClone();
        view.Tool = EnumTool.Shovel;

        // XLib stores tool-type speed multipliers in its effect behavior.
        // Use its own speed reader, including on third-party effects, without
        // copying xSkills' skill rules or Toolsmith's component behavior.
        var effects = api.ModLoader.GetModSystem("XLib.XEffects.XEffectsSystem");
        Type? momentum = effects?.GetType().Assembly.GetType("XLib.XEffects.XLibToolBehavior");
        var constructor = momentum?.GetConstructor([typeof(CollectibleObject)]);
        if (constructor != null && !view.CollectibleBehaviors.Any(momentum!.IsInstanceOfType)
            && constructor.Invoke([view]) is CollectibleBehavior behavior)
        {
            view.CollectibleBehaviors = view.CollectibleBehaviors.Append(behavior).ToArray();
        }
        return view;
    }

    public override bool OnBlockBrokenWith(IWorldAccessor world, Entity byEntity, ItemSlot itemslot,
        BlockSelection selection, float dropQuantityMultiplier = 1)
    {
        if (itemslot.Itemstack is not ItemStack stack || !CanDig(stack, world, selection)
            || byEntity is not EntityPlayer { Player: { } player })
        {
            return base.OnBlockBrokenWith(world, byEntity, itemslot, selection, dropQuantityMultiplier);
        }

        var slot = new DiggingSlot(itemslot, stack);
        var scope = WearableActionSlot.Enter(player, slot, itemslot, stack);
        try
        {
            var view = (ItemSpeciesLockedClaws)slot.Itemstack!.Item;
            return view.BreakWithShovel(world, byEntity, slot, selection, dropQuantityMultiplier);
        }
        finally
        {
            WearableActionSlot.Exit(scope);
        }
    }

    private bool BreakWithShovel(IWorldAccessor world, Entity byEntity, DiggingSlot slot,
        BlockSelection selection, float dropQuantityMultiplier) =>
        base.OnBlockBrokenWith(world, byEntity, slot, selection, dropQuantityMultiplier);

    public override void DamageItem(IWorldAccessor world, Entity byEntity, ItemSlot itemSlot,
        int amount = 1, bool destroyOnZeroDurability = true)
    {
        if (itemSlot is DiggingSlot view)
        {
            view.Damage(world, byEntity, amount, destroyOnZeroDurability);
            return;
        }
        base.DamageItem(world, byEntity, itemSlot, amount, destroyOnZeroDurability);
    }

    private sealed class DiggingSlot(ItemSlot source, ItemStack original) : DummySlot(DiggingView(original))
    {
        // The view shares the source's attributes for native post-break
        // effects, but damage and destruction must use the actual slot.
        public override void MarkDirty()
        {
            if (ReferenceEquals(source.Itemstack, original))
            {
                source.MarkDirty();
            }
        }

        internal void Damage(IWorldAccessor world, Entity actor, int amount, bool destroy)
        {
            if (ReferenceEquals(source.Itemstack, original))
            {
                original.Collectible.DamageItem(world, actor, source, amount, destroy);
            }
            if (!ReferenceEquals(source.Itemstack, original))
            {
                Itemstack = null;
            }
            else if (Itemstack is { } stack)
            {
                stack.Attributes = original.Attributes;
            }
        }
    }
}
