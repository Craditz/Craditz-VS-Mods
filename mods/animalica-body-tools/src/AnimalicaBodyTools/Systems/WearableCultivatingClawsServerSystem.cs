using System;
using System.Collections.Generic;
using AnimalicaBodyTools.Config;
using AnimalicaBodyTools.HarmonyPatches;
using AnimalicaBodyTools.Items;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace AnimalicaBodyTools.Systems;

internal sealed class WearableCultivatingClawsServerSystem
{
    private const int TillDelayMs = 600;
    private const double MaxUseDistanceSq = 64;

    private readonly ICoreServerAPI api;
    private readonly Dictionary<string, PendingAction> pendingByPlayer = new(StringComparer.Ordinal);

    public WearableCultivatingClawsServerSystem(ICoreServerAPI api)
    {
        this.api = api;
    }

    public void HandlePacket(IServerPlayer player, CultivatingClawsPacket packet)
    {
        Cancel(player.PlayerUID);
        if (packet.Action != CultivatingClawsPacket.StartAction)
        {
            return;
        }

        BlockPos pos = new(packet.X, packet.Y, packet.Z, packet.Dimension);
        PendingAction pending = new(player, pos);
        pending.CallbackId = api.Event.RegisterCallback(_ => Commit(pending), TillDelayMs);
        pendingByPlayer[player.PlayerUID] = pending;
    }

    public void Stop()
    {
        foreach (PendingAction pending in pendingByPlayer.Values)
        {
            api.Event.UnregisterCallback(pending.CallbackId);
        }

        pendingByPlayer.Clear();
    }

    private void Cancel(string playerUid)
    {
        if (!pendingByPlayer.Remove(playerUid, out PendingAction? pending))
        {
            return;
        }

        api.Event.UnregisterCallback(pending.CallbackId);
    }

    private void Commit(PendingAction pending)
    {
        if (!pendingByPlayer.TryGetValue(pending.Player.PlayerUID, out PendingAction? current)
            || !ReferenceEquals(current, pending))
        {
            return;
        }

        pendingByPlayer.Remove(pending.Player.PlayerUID);
        IServerPlayer player = pending.Player;
        if (player.Entity == null)
        {
            return;
        }

        ItemSlot? activeSlot = player.InventoryManager?.ActiveHotbarSlot;
        if (activeSlot == null || WearableToolResolver.GetRealItemstack(activeSlot) != null)
        {
            return;
        }

        BlockPos pos = pending.Position;
        double dx = player.Entity.Pos.X - (pos.X + 0.5);
        double dy = player.Entity.Pos.Y - (pos.Y + 0.5);
        double dz = player.Entity.Pos.Z - (pos.Z + 0.5);
        if (dx * dx + dy * dy + dz * dz > MaxUseDistanceSq
            || !player.Entity.World.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak))
        {
            return;
        }

        IWorldAccessor world = player.Entity.World;
        Block block = world.BlockAccessor.GetBlock(pos);
        if (block?.Code == null
            || !block.Code.PathStartsWith("soil")
            || world.BlockAccessor.GetBlock(pos.UpCopy()).BlockId != 0)
        {
            return;
        }

        if (!WearableTargetedActionPatches.TryCreateVirtualDummySlot(
                player,
                activeSlot,
                ScoreCultivator,
                out ItemStack virtualTool,
                out string sourceCode,
                out _,
                out ItemSlot sourceSlot,
                out _)
            || virtualTool.Collectible is not ItemSpeciesLockedCultivatingClaws)
        {
            return;
        }

        TreeAttribute? previousNutrition = null;
        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntitySoilNutrition soilNutrition)
        {
            previousNutrition = new TreeAttribute();
            soilNutrition.ToTreeAttributes(previousNutrition);
        }

        Block? farmland = world.GetBlock(new AssetLocation("farmland-dry-" + block.LastCodePart(1)));
        if (farmland == null)
        {
            return;
        }

        if (block.Sounds != null)
        {
            world.PlaySoundAt(block.Sounds.Place, pos, 0.4, null);
        }

        world.BlockAccessor.SetBlock(farmland.BlockId, pos);
        WearableTargetedActionPatches.TryDamageSourceTool(
            player.Entity,
            sourceSlot,
            sourceCode,
            "cultivating-claws-till");

        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityFarmland farmlandEntity)
        {
            farmlandEntity.OnCreatedFromSoil(block, previousNutrition);
        }

        world.BlockAccessor.MarkBlockDirty(pos);
        if (WearableTargetedActionPatches.DiagnosticsEnabled)
        {
            world.Logger.VerboseDebug(
                "[animalicabodytools] Direct wearable cultivation completed: player={0}, source={1}, block={2}, farmland={3}.",
                player.PlayerName,
                sourceCode,
                block.Code,
                farmland.Code);
        }
    }

    private static float ScoreCultivator(ItemStack stack)
    {
        return stack.Collectible is ItemSpeciesLockedCultivatingClaws
               && stack.Collectible.Tool == EnumTool.Hoe
            ? 1f
            : 0f;
    }

    private sealed class PendingAction
    {
        public PendingAction(IServerPlayer player, BlockPos position)
        {
            Player = player;
            Position = position;
        }

        public IServerPlayer Player { get; }
        public BlockPos Position { get; }
        public long CallbackId { get; set; }
    }
}
