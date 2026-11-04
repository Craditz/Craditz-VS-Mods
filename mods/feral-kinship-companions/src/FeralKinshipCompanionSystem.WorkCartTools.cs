#nullable enable

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    internal BlockEntityFeralKinshipWorkCart? EnsureWorkCartTools(BlockPos pos)
    {
        ICoreAPI? api = (ICoreAPI?)serverApi ?? clientApi;
        if (api == null || api.World.BlockAccessor.GetChunkAtBlockPos(pos) == null
            || !IsWorkCartCode(api.World.BlockAccessor.GetBlock(pos).Code)) return null;
        // Earlier assets declared only "side" but used a hyphenated base code.
        // Native orientation placed a generic cart while its record kept the role.
        // Exchange in place: do not remove the BE, its tools or its assignments.
        Block placed = api.World.BlockAccessor.GetBlock(pos);
        if (api.Side == EnumAppSide.Server && GetWorkCartKind(placed.Code) == "generic"
            && packRepository?.Loaded == true
            && packRepository.GetWorkCart(pos) is FoxWorkCartRecord record
            && record.Kind is "logging" or "mining" or "quarrying")
        {
            string side = placed.LastCodePart();
            if (side is "north" or "east" or "south" or "west")
            {
                AssetLocation code = new("feralkinshipcompanions", "workcart-" + record.Kind + "-" + side);
                Block? correct = api.World.GetBlock(code);
                if (correct?.Code?.Equals(code) == true)
                {
                    api.World.BlockAccessor.ExchangeBlock(correct.Id, pos);
                    api.World.BlockAccessor.MarkBlockDirty(pos);
                    api.Logger.Notification("[WorkCartInventory] Recovered {0} cart at {1} without replacing its inventory.", record.Kind, pos);
                }
            }
        }
        if (api.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityFeralKinshipWorkCart existing)
        {
            existing.ConfigureForBlock(api.World.BlockAccessor.GetBlock(pos));
            return existing;
        }
        if (api.World.BlockAccessor.GetBlockEntity(pos) != null) return null;
        api.World.BlockAccessor.SpawnBlockEntity("FeralKinshipWorkCart", pos);
        var created = api.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityFeralKinshipWorkCart;
        created?.ConfigureForBlock(api.World.BlockAccessor.GetBlock(pos));
        if (api.Side == EnumAppSide.Server) created?.MarkDirty(true);
        return created;
    }

    internal bool CanPlayerUseWorkCartTools(IPlayer player, BlockPos pos)
    {
        if (serverApi == null || packRepository?.Loaded != true || player.Entity == null
            || player.Entity.Pos.Dimension != pos.dimension
            || player.Entity.Pos.SquareDistanceTo(pos.ToVec3d().Add(0.5, 0.5, 0.5)) > 64
            || !IsWorkCartCode(serverApi.World.BlockAccessor.GetBlock(pos).Code)
            || serverApi.World.BlockAccessor.GetChunkAtBlockPos(pos) == null) return false;
        FoxWorkCartRecord? cart = packRepository.GetWorkCart(pos);
        return cart != null && cart.OwnerUid == player.PlayerUID && (cart.Kind is "generic" or "logging" or "quarrying" or "mining")
            && serverApi.World.Claims.TestAccess(player, pos, EnumBlockAccessFlags.Use)
                == EnumWorldAccessResponse.Granted;
    }

    internal void RefreshWorkCartToolsDialog(BlockPos pos)
    {
        if (clientApi == null || workCartDialog?.IsAt(pos) != true) return;
        clientApi.Event.EnqueueMainThreadTask(() => workCartDialog?.RefreshTools(),
            "feralkinship-workcart-tools");
    }

    private int RequiredWorkCartAxeTier(WorkCartLoggingPlan plan)
    {
        Block block = serverApi!.World.BlockAccessor.GetBlock(plan.Root);
        return System.Math.Max(0, (block.Attributes?["treeFellingGroupSpreadIndex"].AsInt() ?? 0) - 3);
    }

    private bool ReserveWorkCartAxe(WorkCartLoggingPlan plan)
    {
        var cart = EnsureWorkCartTools(plan.CartPos);
        if (cart == null) return false;
        int index = cart.Tools.Reserve(plan, plan.RequiredAxeTier);
        if (index < 0) return false;
        plan.ToolCart = cart;
        plan.AxeSlotIndex = index;
        return true;
    }

    private ItemSlot? GetWorkCartAxe(WorkCartLoggingPlan plan)
    {
        var cart = plan.ToolCart;
        if (cart == null || serverApi?.World.BlockAccessor.GetBlockEntity(plan.CartPos) != cart
            || !cart.Tools.Owns(plan.AxeSlotIndex, plan)) return null;
        ItemSlot slot = cart.Tools[plan.AxeSlotIndex];
        return WorkCartAxeInventory.IsUsable(slot, plan.RequiredAxeTier) ? slot : null;
    }

    private bool ReplaceWorkCartAxe(WorkCartLoggingPlan plan)
    {
        plan.ToolCart?.Tools.Release(plan);
        plan.AxeSlotIndex = -1;
        return ReserveWorkCartAxe(plan);
    }
}
