using System;
using AnimalicaBodyTools.Items;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace AnimalicaBodyTools.Systems;

internal static class BodyToolDurability
{
    private static readonly Type[] KnownTypes = { typeof(ItemSpeciesLockedBodyTool), typeof(ItemSpeciesLockedClaws),
        typeof(ItemSpeciesLockedCultivatingClaws), typeof(ItemSpeciesLockedProspectingPick),
        typeof(ItemSpeciesLockedRendingClaws), typeof(ItemSpeciesLockedScythe) };

    private static bool SameBuildType(CollectibleObject collectible, Type expected) => collectible.GetType().FullName == expected.FullName
        && collectible.GetType().Module.ModuleVersionId == expected.Module.ModuleVersionId;

    public static bool IsBodyTool(CollectibleObject collectible) => collectible is
        ItemSpeciesLockedBodyTool or ItemSpeciesLockedClaws or ItemSpeciesLockedCultivatingClaws
        or ItemSpeciesLockedProspectingPick or ItemSpeciesLockedRendingClaws or ItemSpeciesLockedScythe
        || Array.Exists(KnownTypes, expected => SameBuildType(collectible, expected));

    public static bool IsCultivatingClaws(CollectibleObject collectible) => collectible is ItemSpeciesLockedCultivatingClaws
        || SameBuildType(collectible, typeof(ItemSpeciesLockedCultivatingClaws));

    public static bool IsRendingClaws(CollectibleObject collectible) => collectible is ItemSpeciesLockedRendingClaws
        || SameBuildType(collectible, typeof(ItemSpeciesLockedRendingClaws));

    // Use the engine contract: decimal attributes and behavior overrides both matter.
    public static bool IsBroken(ItemStack stack) => stack.Collectible is { } collectible
        && collectible.GetMaxDurability(stack) > 0 && collectible.GetRemainingDurability(stack) <= 0;

    public static bool RemoveIfBroken(IWorldAccessor world, ItemSlot slot, ItemStack expected)
    {
        if (world.Side != EnumAppSide.Server || !IsBodyTool(expected.Collectible)
            || !ReferenceEquals(WearableToolResolver.GetRealItemstack(slot), expected) || !IsBroken(expected)) return false;

        return WearableToolResolver.RemoveBrokenSourceTool(slot, expected);
    }

    public static bool RemoveAfterDamage(IWorldAccessor world, Entity entity, ItemSlot slot, ItemStack expected, int beforeRemaining)
    {
        if (!RemoveIfBroken(world, slot, expected)) return false;
        // Removal of an already-zero stack is cleanup, not a new break event.
        // Only the server broadcasts; null dualCallByPlayer includes the owner,
        // because no corresponding predicted client sound is emitted by us.
        if (beforeRemaining > 0 && entity != null)
        {
            try
            {
                world.PlaySoundAt(HeldSounds.ToolBreak, entity, null);
            }
            catch (Exception ex)
            {
                world.Logger.Warning("[animalicabodytools] Tool removed but break sound failed: item={0}, error={1}: {2}",
                    expected.Collectible.Code, ex.GetType().Name, ex.Message);
            }
        }
        return true;
    }
}
