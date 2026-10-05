using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace AnimalicaBodyTools.Items;

/// <summary>
/// Animal prospecting claws. Equipped use deliberately delegates to the
/// vanilla prospecting-pick implementation with a transient dummy slot.
/// </summary>
public sealed class ItemSpeciesLockedProspectingPick : ItemProspectingPick
{
    public bool ProbeForWearable(
        IWorldAccessor world,
        Entity byEntity,
        ItemSlot itemslot,
        BlockSelection blockSel,
        float dropQuantityMultiplier)
    {
        Item? vanillaItem = world.GetItem(new AssetLocation("prospectingpick-copper"));
        if (vanillaItem is not ItemProspectingPick vanillaProspectingPick || itemslot.Itemstack == null)
        {
            return base.OnBlockBrokenWith(world, byEntity, itemslot, blockSel, dropQuantityMultiplier);
        }

        // Use the game's fully initialized prospecting-pick collectible as the
        // execution host. Only this transient stack carries the wearable's mode
        // and sample positions; it is never inserted into an inventory.
        ItemStack vanillaStack = new(vanillaItem)
        {
            Attributes = itemslot.Itemstack.Attributes.Clone()
        };
        DummySlot vanillaSlot = new(vanillaStack);

        try
        {
            return vanillaProspectingPick.OnBlockBrokenWith(
                world,
                byEntity,
                vanillaSlot,
                blockSel,
                dropQuantityMultiplier);
        }
        finally
        {
            itemslot.Itemstack.Attributes = vanillaStack.Attributes.Clone();
        }
    }

}
