using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Druidry;

public sealed class BlockDruidRitualAltar : Block
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.Side == EnumAppSide.Client) return true;
        if (byPlayer is not IServerPlayer serverPlayer) return true;

        DruidrySystem? system = world.Api.ModLoader.GetModSystem<DruidrySystem>();
        if (system == null) return true;

        ItemSlot activeSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
        string path = activeSlot.Itemstack?.Collectible?.Code?.Path ?? string.Empty;
        const string tokenPrefix = "spirit-token-";
        if (path.StartsWith(tokenPrefix, StringComparison.Ordinal))
        {
            system.PerformRitual(serverPlayer, path[tokenPrefix.Length..], activeSlot, blockSel.Position);
        }
        else
        {
            system.OpenRites(serverPlayer);
        }

        return true;
    }
}
