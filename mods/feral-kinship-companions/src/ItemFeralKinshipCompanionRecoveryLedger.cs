#nullable enable

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;

/// <summary>
/// Creative/admin-only ledger for rebuilding missing companions from the
/// persistent pack record.
/// </summary>
public sealed class ItemFeralKinshipCompanionRecoveryLedger : Item
{
    public override void OnHeldInteractStart(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        bool firstEvent,
        ref EnumHandHandling handling)
    {
        if (!firstEvent || byEntity is not EntityPlayer)
        {
            return;
        }

        handling = EnumHandHandling.PreventDefaultAction;
        if (byEntity.Api.Side == EnumAppSide.Client)
        {
            byEntity.Api.ModLoader
                .GetModSystem<FeralKinshipCompanionSystem>()
                ?.TryOpenCompanionRecoveryGui();
        }
    }

    public override bool OnHeldInteractStep(
        float secondsUsed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel)
    {
        byEntity.AnimManager?.StopAnimation("PlaceBlock");
        return secondsUsed < 1;
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
    {
        return new[]
        {
            new WorldInteraction
            {
                ActionLangCode = "feralkinshipcompanions:heldhelp-companion-recovery-ledger",
                MouseButton = EnumMouseButton.Right
            }
        };
    }
}
