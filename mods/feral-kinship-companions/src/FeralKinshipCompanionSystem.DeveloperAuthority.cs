#nullable enable
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private sealed record DeveloperCartTarget(string Owner, BlockPos Position);
    private readonly Dictionary<string, DeveloperCartTarget> developerCartTargets = new(StringComparer.Ordinal);
    private string developerClientTargetOwner = string.Empty;

    private bool TryResolveDeveloperCartOwner(IServerPlayer actor, BlockPos pos, out string owner, out string reason)
    {
        owner = string.Empty; reason = "Creative/admin access and a held Developer Ledger are required";
        if (serverApi == null || packRepository?.Loaded != true || !CanUseDeveloperTools(actor)
            || actor.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible is not ItemFeralKinshipDeveloperLedger) return false;
        reason = "The selected Pack Cart must remain nearby and loaded in your dimension";
        if (actor.Entity == null || actor.Entity.Pos.Dimension != pos.dimension
            || actor.Entity.Pos.SquareDistanceTo(pos.X + .5, pos.Y + .5, pos.Z + .5) > SocialActionRangeSquared
            || serverApi.World.BlockAccessor.GetChunkAtBlockPos(pos) == null) return false;
        Block block = serverApi.World.BlockAccessor.GetBlock(pos);
        reason = "The selected Pack Cart no longer exists";
        if (block.Code == null || !IsPackMarkerCode(block.Code)) return false;
        var cart = serverApi.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityFeralKinshipPackCart;
        var ownership = PackCartOwnershipPolicy.Resolve(cart?.OwnerUid, packRepository.GetCairn(pos)?.OwnerUid);
        reason = "The selected cart has no recorded owner";
        if (!ownership.HasOwner) return false;
        owner = ownership.OwnerUid; reason = string.Empty; return true;
    }

    private bool DeveloperMayControlOwner(IServerPlayer actor, string owner)
    {
        if (string.IsNullOrWhiteSpace(owner)) return false;
        if (owner == actor.PlayerUID) return true;
        return developerCartTargets.TryGetValue(actor.PlayerUID, out DeveloperCartTarget? target)
            && target.Owner == owner
            && TryResolveDeveloperCartOwner(actor, target.Position, out string actual, out _) && actual == owner;
    }

    private string DeveloperTargetOwnerUid(IServerPlayer actor)
    {
        if (developerCartTargets.TryGetValue(actor.PlayerUID, out DeveloperCartTarget? target)
            && DeveloperMayControlOwner(actor, target.Owner)) return target.Owner;
        return actor.PlayerUID;
    }

    private bool AuthorizeRequestedDeveloperTarget(IServerPlayer actor, string requested, out string target)
    {
        target = string.IsNullOrWhiteSpace(requested) ? actor.PlayerUID : requested;
        if (!CanUseDeveloperTools(actor) || !DeveloperMayControlOwner(actor, target)) return false;
        if (target == actor.PlayerUID) developerCartTargets.Remove(actor.PlayerUID);
        return true;
    }

    internal void OpenDeveloperCartAdministration(DeveloperCartInspectionStatePacket state)
    {
        if (clientApi == null || !state.Available || !CanUseDeveloperToolsClient()) return;
        developerClientTargetOwner = state.OwnerUid;
        developerCartInspectionDialog?.TryClose();
        developerDialog?.TryClose();
        developerDialog = new GuiDialogFeralKinshipFoxDeveloper(clientApi, this, 0);
        developerDialog.TryOpen();
        SendCompanionRecoveryAction(CompanionRecoveryRequestPacket.Open);
    }
}
