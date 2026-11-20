using AnimalicaBodyTools.Util;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace AnimalicaBodyTools.Systems;

public sealed class CrossbowAmmo(CollectibleObject ammo) : CollectibleBehavior(ammo)
{
    private const string HoldingAmmo = "animalicaCrossbowHeldAmmo";

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel,
        EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling, ref EnumHandling handling)
    {
        if (byEntity is not EntityPlayer { Player: {} player } || !byEntity.Alive
            || player.WorldData.CurrentGameMode == EnumGameMode.Spectator
            || player.InventoryManager.ActiveHotbarSlot != slot
            || RangedCrossbowSystem.Equipped(player)?.Itemstack is not {} weapon
            || weapon.Attributes.HasAttribute("animalicaLoadedArrow")
            || !AnimalBodyToolTraitAccessUtil.HolderHasTraitFast(byEntity)) return;

        handHandling = EnumHandHandling.PreventDefault;
        handling = EnumHandling.PreventSubsequent;
        if (!firstEvent) return;
        byEntity.Attributes.SetBool(HoldingAmmo, true);
        if (player is IServerPlayer serverPlayer)
            byEntity.Api.ModLoader.GetModSystem<RangedCrossbowSystem>().StartReload(serverPlayer, slot);
    }

    public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity,
        BlockSelection blockSel, EntitySelection entitySel, ref EnumHandling handling)
    {
        if (!byEntity.Attributes.GetBool(HoldingAmmo)) return false;
        handling = EnumHandling.PreventSubsequent;
        return true;
    }

    public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity,
        BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason, ref EnumHandling handling)
    {
        EndUse(slot, byEntity, ref handling);
        return true;
    }

    public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity,
        BlockSelection blockSel, EntitySelection entitySel, ref EnumHandling handling) => EndUse(slot, byEntity, ref handling);

    private static void EndUse(ItemSlot slot, EntityAgent entity, ref EnumHandling handling)
    {
        if (!entity.Attributes.GetBool(HoldingAmmo)) return;
        entity.Attributes.RemoveAttribute(HoldingAmmo);
        handling = EnumHandling.PreventSubsequent;
        if (entity is EntityPlayer { Player: IServerPlayer player })
            entity.Api.ModLoader.GetModSystem<RangedCrossbowSystem>().CancelReload(player, slot);
    }
}
