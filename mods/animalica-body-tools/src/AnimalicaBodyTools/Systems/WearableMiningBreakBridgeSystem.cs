using System;
using System.Collections.Generic;
using AnimalicaBodyTools.HarmonyPatches;
using AnimalicaBodyTools.Items;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace AnimalicaBodyTools.Systems;

/// <summary>
/// Routes ordinary equipped pickaxe and digging actions when the server
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
                out float selectedScore))
        {
            return;
        }

        bool pickaxe = virtualTool.Collectible?.GetTool(dummySlot) == EnumTool.Pickaxe;
        if (!pickaxe && !ItemSpeciesLockedClaws.CanDig(virtualTool, api.World, miningBlockSel))
        {
            return;
        }

        if (pickaxe && sourceSlot.Itemstack?.Collectible is ItemSpeciesLockedBodyTool tunnelingClaws
            && tunnelingClaws.TryDigWell(api.World, byPlayer.Entity, sourceSlot, miningBlockSel, dropQuantityMultiplier))
        {
            // HoD has handled wear for this action, even if a listener queried ActiveTool.
            WearableBlockBreakToolContext.MarkDurabilityHandled(byPlayer, miningBlockSel.Position);
            handling = EnumHandling.PreventDefault;
            return;
        }

        // Let the ordinary server dispatch deliver the real tool to collectible
        // and block hooks once all BreakBlock listeners have allowed the action.
        if (WearableServerBreakTierPatch.HasNativeBreakRoute
            && WearableBlockBreakToolContext.Current is { } context
            && ReferenceEquals(context.Player, byPlayer) && context.Position.Equals(blockSel.Position)
            && sourceSlot.Itemstack is { } sourceStack
            && (sourceStack.Collectible.GetTool(sourceSlot) == EnumTool.Pickaxe
                || ItemSpeciesLockedClaws.CanDig(sourceStack, api.World, miningBlockSel)))
        {
            context.NativeSourceSlot = sourceSlot;
            context.NativeSourceStack = sourceStack;
            context.NativeSelection = miningBlockSel;
            return;
        }

        if (!pickaxe)
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


