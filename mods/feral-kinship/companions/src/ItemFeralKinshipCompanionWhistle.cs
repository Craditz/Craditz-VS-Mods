#nullable enable

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;

/// <summary>
/// Companions' replacement for PetAI's whistle item. The item keeps PetAI's
/// shape, sound, and interaction help through the patched item definition,
/// but owns the command-selection interaction so it cannot be outraced by
/// PetAI's original item class during asset loading.
/// </summary>
public sealed class ItemFeralKinshipCompanionWhistle : Item
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
                system.TryOpenWhistleGui();
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
            system.TryApplyWhistleCommand(serverPlayer);
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
                ActionLangCode = "petai:interact-whistle-select",
                HotKeyCode = "sneak",
                MouseButton = EnumMouseButton.Right
            },
            new WorldInteraction
            {
                ActionLangCode = "petai:interact-whistle-command",
                MouseButton = EnumMouseButton.Right
            }
        };
    }
}
