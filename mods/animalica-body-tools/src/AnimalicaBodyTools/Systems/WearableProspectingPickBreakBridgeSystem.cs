using System;
using System.Collections.Generic;
using AnimalicaBodyTools.HarmonyPatches;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace AnimalicaBodyTools.Systems;

internal sealed class WearableProspectingPickBreakBridgeSystem
{
    private readonly ICoreServerAPI api;
    private readonly HashSet<string> logged = new(StringComparer.Ordinal);
    private int bridgeDepth;

    public WearableProspectingPickBreakBridgeSystem(ICoreServerAPI api)
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
        if (bridgeDepth > 0 || handling == EnumHandling.PreventDefault || byPlayer?.Entity == null || blockSel?.Position == null)
        {
            return;
        }

        Block block = api.World.BlockAccessor.GetBlock(blockSel.Position);
        if (!IsLikelyProspectableBlock(block))
        {
            return;
        }

        ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
        if (activeSlot == null)
        {
            return;
        }

        BlockSelection propickBlockSel = new(blockSel.Position, blockSel.Face ?? BlockFacing.UP, block)
        {
            HitPosition = blockSel.HitPosition ?? new Vec3d(0.5, 0.5, 0.5),
            SelectionBoxIndex = blockSel.SelectionBoxIndex,
            SelectionBoxId = blockSel.SelectionBoxId,
            DidOffset = blockSel.DidOffset
        };

        if (!WearableTargetedActionPatches.TryCreateVirtualDummySlot(
                byPlayer,
                activeSlot,
                stack => WearableTargetedActionPatches.ScoreProspectingPickTool(stack, block, propickBlockSel, byPlayer),
                out ItemStack virtualTool,
                out string sourceCode,
                out ItemSlot dummySlot,
                out ItemSlot sourceSlot,
                out float selectedScore))
        {
            return;
        }

        if (virtualTool.Collectible is not Items.ItemSpeciesLockedProspectingPick prospectingPick)
        {
            return;
        }

        handling = EnumHandling.PreventDefault;

        bool vanillaHandled = false;
        bool failed = false;
        int suppressedBreaks = 0;
        int toolMode = prospectingPick.GetToolMode(dummySlot, byPlayer, propickBlockSel);
        bridgeDepth++;
        WearableTargetedActionPatches.BeginBlockBreakDurabilitySuppression();
        try
        {
            vanillaHandled = prospectingPick.ProbeForWearable(
                api.World,
                byPlayer.Entity,
                dummySlot,
                propickBlockSel,
                dropQuantityMultiplier);
        }
        catch (Exception ex)
        {
            failed = true;
            handling = EnumHandling.PassThrough;
            api.World.Logger.Warning("[animalicabodytools] Wearable prospecting pick bridge failed: player={0}, block={1}, source={2}, virtual={3}, error={4}: {5}",
                byPlayer.PlayerName,
                block.Code,
                sourceCode,
                virtualTool.Collectible.Code,
                ex.GetType().Name,
                ex.Message);
            api.World.Logger.VerboseDebug("[animalicabodytools] Wearable prospecting pick bridge stack trace: {0}", ex);
        }
        finally
        {
            suppressedBreaks = WearableTargetedActionPatches.EndBlockBreakDurabilitySuppression();
            bridgeDepth--;
        }

        if (failed)
        {
            return;
        }

        bool synced = WearableToolResolver.SyncVirtualToolStateToSource(virtualTool, sourceSlot, preserveDurability: true);
        int durabilityCost = toolMode == 1 ? 2 : 1;
        bool damaged = WearableTargetedActionPatches.TryDamageSourceTool(
            byPlayer.Entity,
            sourceSlot,
            sourceCode,
            "prospecting-pick:" + (block.Code?.ToShortString() ?? "unknown"),
            durabilityCost);
        if (damaged)
        {
            WearableBlockBreakToolContext.MarkDurabilityCharged(byPlayer, blockSel.Position);
        }

        string logKey = $"{byPlayer.PlayerUID}:{sourceCode}:{block.Code?.ToShortString() ?? "unknown"}:{toolMode}:{vanillaHandled}:{synced}";
        if (WearableTargetedActionPatches.DiagnosticsEnabled && WearableTargetedActionPatches.TryLogOnce(logged, logKey))
        {
            api.World.Logger.VerboseDebug("[animalicabodytools] Wearable prospecting pick used vanilla probe: handled={0}, synced={1}, suppressedBreaks={2}, mode={3}, durabilityCost={4}, player={5}, block={6}, source={7}, virtual={8}, score={9}.",
                vanillaHandled,
                synced,
                suppressedBreaks,
                toolMode,
                durabilityCost,
                byPlayer.PlayerName,
                block.Code,
                sourceCode,
                virtualTool.Collectible.Code,
                selectedScore);
        }
    }

    private static bool IsLikelyProspectableBlock(Block block)
    {
        if (block.BlockMaterial != EnumBlockMaterial.Stone && block.BlockMaterial != EnumBlockMaterial.Ore)
        {
            return false;
        }

        return block.Tool == null || block.Tool == EnumTool.Pickaxe;
    }
}
