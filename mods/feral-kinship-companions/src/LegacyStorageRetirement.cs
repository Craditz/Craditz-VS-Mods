#nullable enable
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;

internal static class LegacyStorageRetirement
{
    internal const int Capacity = 36;
    internal const string Shape = "block/legacy-storage-warning";

    internal static bool CanRemove(IWorldAccessor world, BlockPos pos, IPlayer? player)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityFeralKinshipPackStorage storage
            && !storage.RetirementBlocked) return true;
        if (world.Side == EnumAppSide.Server)
        {
            world.Logger.Warning("[FeralKinshipCompanions] Legacy warning container at {0} retained: saved contents are unavailable or could not be safely restored. No contents were reconstructed or discarded.", pos);
            if (player is IServerPlayer serverPlayer)
                serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get("feralkinshipcompanions:legacy-warning-blocked"), EnumChatType.Notification);
            world.BlockAccessor.MarkBlockDirty(pos);
        }
        return false;
    }

    internal static bool CanInteract(IWorldAccessor world, BlockPos pos, IPlayer player)
        => world.Side != EnumAppSide.Server || CanRemove(world, pos, player);
}
