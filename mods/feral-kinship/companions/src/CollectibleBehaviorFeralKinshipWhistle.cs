#nullable enable

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;

/// <summary>
/// Adds the Companions command layer to PetAI's existing whistle without
/// replacing PetAI's item class. The original behavior remains available for
/// non-Companion tameables and when this mod is removed.
/// </summary>
public sealed class CollectibleBehaviorFeralKinshipWhistle : CollectibleBehavior
{
    private ICoreAPI? api;
    private FeralKinshipCompanionSystem? system;

    public CollectibleBehaviorFeralKinshipWhistle(CollectibleObject collObj)
        : base(collObj)
    {
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        this.api = api;
        system = api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
    }

    public override void OnHeldInteractStart(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        bool firstEvent,
        ref EnumHandHandling handHandling,
        ref EnumHandling handling)
    {
        if (system == null
            || byEntity is not EntityPlayer player
            || !firstEvent)
        {
            return;
        }

        if (player.Controls.Sneak)
        {
            // The whistle itself is the command selector. It does not matter
            // what the player is looking at; the selected command can later
            // be broadcast to both Companions and ordinary PetAI pets.
            if (api?.Side == EnumAppSide.Client)
            {
                system.TryOpenWhistleGui();
                handHandling = EnumHandHandling.Handled;
                handling = EnumHandling.PreventSubsequent;
            }
            return;
        }

        if (api?.Side == EnumAppSide.Server)
        {
            ICoreServerAPI? server = api as ICoreServerAPI;
            IServerPlayer? serverPlayer = server?.World.PlayerByUid(player.PlayerUID) as IServerPlayer;
            if (serverPlayer != null && system.TryApplyWhistleCommand(serverPlayer))
            {
                handHandling = EnumHandHandling.PreventDefaultAnimation;
                handling = EnumHandling.PreventSubsequent;
            }
            return;
        }

        // Only suppress PetAI's original whistle if this player has selected a
        // Companions command and a Companions animal is actually in range.
        // Otherwise PetAI remains fully responsible for its own pets.
        if (system.HasClientWhistleCommand && system.HasNearbyWhistleTarget(player))
        {
            player.AnimManager?.StartAnimation("eat");
            handHandling = EnumHandHandling.PreventDefaultAnimation;
            handling = EnumHandling.PreventSubsequent;
        }
    }
}
