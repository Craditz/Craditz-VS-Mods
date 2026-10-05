using System;
using System.Collections.Generic;
using AnimalicaBodyTools.HarmonyPatches;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace AnimalicaBodyTools.Systems;

/// <summary>
/// Charges ordinary equipped pickaxe-style body tools when the server
/// confirms that a block is about to break. Specialized multi-break bridges
/// run first and mark the event handled, so they retain their own accounting.
/// </summary>
internal sealed class WearableMiningBreakBridgeSystem
{
    private readonly ICoreServerAPI api;
    private readonly HashSet<string> logged = new(StringComparer.Ordinal);

    public WearableMiningBreakBridgeSystem(ICoreServerAPI api)
    {
        this.api = api;
    }

    public void Start()
    {
        api.Event.BreakBlock += OnBreakBlock;
    }

    public void Stop()
    {
        api.Event.BreakBlock -= OnBreakBlock;
    }

    private void OnBreakBlock(IServerPlayer byPlayer, BlockSelection blockSel, ref float dropQuantityMultiplier, ref EnumHandling handling)
    {
        if (handling != EnumHandling.PassThrough
            || byPlayer?.Entity == null
            || blockSel?.Position == null
            || WearableTargetedActionPatches.IsBlockBreakDurabilitySuppressed)
        {
            return;
        }

        Block block = api.World.BlockAccessor.GetBlock(blockSel.Position);
        ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
        if (activeSlot == null)
        {
            return;
        }

        BlockSelection miningBlockSel = new(blockSel.Position, blockSel.Face ?? BlockFacing.UP, block)
        {
            HitPosition = blockSel.HitPosition ?? new Vec3d(0.5, 0.5, 0.5),
            SelectionBoxIndex = blockSel.SelectionBoxIndex,
            SelectionBoxId = blockSel.SelectionBoxId,
            DidOffset = blockSel.DidOffset
        };

        if (!WearableTargetedActionPatches.TryCreateVirtualMiningDummySlot(
                byPlayer,
                activeSlot,
                block,
                miningBlockSel,
                out ItemStack virtualTool,
                out string sourceCode,
                out ItemSlot dummySlot,
                out ItemSlot sourceSlot,
                out float selectedScore)
            || virtualTool.Collectible?.GetTool(dummySlot) != EnumTool.Pickaxe)
        {
            return;
        }

        bool damaged = WearableTargetedActionPatches.TryDamageSourceTool(
            byPlayer.Entity,
            sourceSlot,
            sourceCode,
            "pickaxe-block-break:" + (block.Code?.ToShortString() ?? "unknown"));
        if (damaged)
        {
            WearableBlockBreakToolContext.MarkDurabilityCharged(byPlayer, blockSel.Position);
        }

        if (WearableTargetedActionPatches.DiagnosticsEnabled)
        {
            string logKey = $"{byPlayer.PlayerUID}:{sourceCode}:{block.Code?.ToShortString() ?? "unknown"}";
            if (logged.Add(logKey))
            {
                api.World.Logger.VerboseDebug(
                    "[animalicabodytools] Equipped pickaxe break durability charged: damaged={0}, player={1}, block={2}, source={3}, virtual={4}, score={5}.",
                    damaged,
                    byPlayer.PlayerName,
                    block.Code,
                    sourceCode,
                    virtualTool.Collectible?.Code,
                    selectedScore);
            }
        }
    }
}
