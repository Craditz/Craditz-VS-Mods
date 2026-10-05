#nullable enable

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;

/// <summary>
/// Reusable player tool for configuring storage destinations without chat
/// commands. It intentionally uses the same book interaction texture as the
/// existing Companion Developer Ledger.
/// </summary>
public sealed class ItemFeralKinshipPackLedger : Item
{
    public override void OnHeldInteractStart(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        bool firstEvent,
        ref EnumHandHandling handling)
    {
        if (!firstEvent || byEntity is not EntityPlayer player)
        {
            return;
        }

        handling = EnumHandHandling.PreventDefaultAction;
        if (byEntity.Api.Side == EnumAppSide.Client)
        {
            // The engine passes its authoritative crosshair selection here.
            // Send that exact world position; the server resolves a genuine
            // multiblock helper to its controller and validates the result.
            // ControllerPositionRel is placement geometry, not a world-space
            // offset, so it must never be applied to this position.
            if (blockSel?.Position == null) return;
            byEntity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()
                ?.TryOpenStorageRoutingGui(blockSel.Position);
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
                ActionLangCode = "feralkinshipcompanions:heldhelp-pack-ledger",
                MouseButton = EnumMouseButton.Right
            }
        };
    }
}
