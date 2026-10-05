#nullable enable

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace FeralKinshipCompanions;

/// <summary>
/// Creative test tool for forcing one of the current ambient social behaviors
/// on a selected owned companion. Shift-right-click selects the behavior;
/// ordinary right-click attempts it on the looked-at companion.
/// </summary>
public sealed class ItemFeralKinshipCompanionIdleTester : Item
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

        FeralKinshipCompanionSystem system = byEntity.Api.ModLoader
            .GetModSystem<FeralKinshipCompanionSystem>();

        if (player.Controls.Sneak)
        {
            if (byEntity.Api.Side == EnumAppSide.Client)
            {
                system.TryOpenIdleTestGui();
                handling = EnumHandHandling.PreventDefaultAction;
            }

            return;
        }

        handling = EnumHandHandling.PreventDefaultAnimation;
        player.AnimManager?.StartAnimation("eat");

        if (byEntity.Api.Side != EnumAppSide.Server)
        {
            return;
        }

        ICoreServerAPI serverApi = (ICoreServerAPI)byEntity.Api;
        IServerPlayer? serverPlayer = serverApi.World.PlayerByUid(player.PlayerUID) as IServerPlayer;
        if (serverPlayer != null)
        {
            system.TryApplyIdleTest(serverPlayer, entitySel?.Entity);
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
        return secondsUsed < 3;
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
    {
        return new[]
        {
            new WorldInteraction
            {
                ActionLangCode = "feralkinshipcompanions:heldhelp-idle-test-select",
                HotKeyCode = "sneak",
                MouseButton = EnumMouseButton.Right
            },
            new WorldInteraction
            {
                ActionLangCode = "feralkinshipcompanions:heldhelp-idle-test-try",
                MouseButton = EnumMouseButton.Right
            }
        };
    }
}
