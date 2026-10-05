#nullable enable

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

/// <summary>Creative/admin developer ledger opened directly from the held item.</summary>
public sealed class ItemFeralKinshipDeveloperLedger : Item
{
    public override void OnHeldInteractStart(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        bool firstEvent,
        ref EnumHandHandling handling)
    {
        if (!firstEvent || byEntity is not EntityPlayer) return;
        handling = EnumHandHandling.PreventDefaultAction;
        if (byEntity.Api.Side == EnumAppSide.Client)
        {
            FeralKinshipCompanionSystem? system = byEntity.Api.ModLoader
                .GetModSystem<FeralKinshipCompanionSystem>();
            if (blockSel?.Position is BlockPos pos
                && system?.TryOpenDeveloperCartInspection(pos) == true)
            {
                return;
            }
            if (entitySel?.Entity != null)
            {
                system?.TryOpenFoxDeveloperGui(entitySel.Entity);
            }
            else
            {
                system?.TryOpenFoxDeveloperGui();
            }
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
                ActionLangCode = "feralkinshipcompanions:heldhelp-companion-dev-ledger",
                MouseButton = EnumMouseButton.Right
            }
        };
    }
}
