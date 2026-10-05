using System;
using System.Collections.Generic;
using AnimalicaBodyTools.HarmonyPatches;
using AnimalicaBodyTools.Items;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace AnimalicaBodyTools.Systems;

/// <summary>
/// Bridges a wearable harvesting claw into a bounded, server-authoritative
/// scythe harvest while preserving each target block's own break routine.
/// </summary>
internal sealed class WearableScytheBreakBridgeSystem
{
    private readonly ICoreServerAPI api;
    private readonly HashSet<string> logged = new(StringComparer.Ordinal);
    private int bridgeDepth;

    public WearableScytheBreakBridgeSystem(ICoreServerAPI api)
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
        if (block == null || block.Id == 0)
        {
            return;
        }

        ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
        if (activeSlot == null)
        {
            return;
        }

        BlockSelection scytheBlockSel = new(blockSel.Position, blockSel.Face ?? BlockFacing.UP, block)
        {
            HitPosition = blockSel.HitPosition ?? new Vec3d(0.5, 0.5, 0.5),
            SelectionBoxIndex = blockSel.SelectionBoxIndex,
            SelectionBoxId = blockSel.SelectionBoxId,
            DidOffset = blockSel.DidOffset
        };

        if (!WearableTargetedActionPatches.TryCreateVirtualDummySlot(
                byPlayer,
                activeSlot,
                stack => WearableTargetedActionPatches.ScoreScytheTool(stack, block, scytheBlockSel, byPlayer),
                out ItemStack virtualTool,
                out string sourceCode,
                out ItemSlot dummySlot,
                out ItemSlot sourceSlot,
                out float selectedScore))
        {
            return;
        }

        if (virtualTool.Collectible is not ItemSpeciesLockedScythe scythe || virtualTool.Collectible.GetTool(dummySlot) != EnumTool.Scythe)
        {
            return;
        }

        if (!scythe.CanMultiBreak(block))
        {
            return;
        }

        // The initiating block must be authorized before preventing its normal
        // break. Every neighboring target is checked separately below.
        if (!api.World.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
        {
            handling = EnumHandling.PreventDefault;
            return;
        }

        if (!scythe.TryPrepareWearableMode(virtualTool, out bool previousTrimMode))
        {
            return;
        }

        handling = EnumHandling.PreventDefault;
        ItemStack? originalActiveStack = activeSlot.Itemstack;
        bool handled = false;
        bool synced = false;
        bool sourceDurabilityCharged = false;
        bool suppressionStarted = false;
        int processed = 0;
        int suppressedBreaks = 0;
        bridgeDepth++;
        try
        {
            activeSlot.Itemstack = virtualTool;
            WearableTargetedActionPatches.BeginBlockBreakDurabilitySuppression();
            suppressionStarted = true;

            int toolMode = virtualTool.Attributes?.GetInt("toolMode", 0) ?? 0;
            HarvestScytheArea(scythe, scytheBlockSel, byPlayer, dropQuantityMultiplier, trimMode: toolMode == 0, ref processed);
            handled = processed > 0;

            if (WearableTargetedActionPatches.DiagnosticsEnabled)
            {
                api.World.Logger.VerboseDebug("[animalicabodytools] Wearable scythe bridge used safe block routines: player={0}, block={1}, source={2}, virtual={3}, toolMode={4}, processed={5}.",
                    byPlayer.PlayerName,
                    block.Code,
                    sourceCode,
                    virtualTool.Collectible.Code,
                    toolMode,
                    processed);
            }
        }
        catch (Exception ex)
        {
            // Keep the event suppressed after a partial harvest so the engine
            // cannot break the center a second time and duplicate its drops.
            api.World.Logger.Warning("[animalicabodytools] Wearable scythe bridge failed: player={0}, block={1}, source={2}, virtual={3}, processed={4}, error={5}: {6}",
                byPlayer.PlayerName,
                block.Code,
                sourceCode,
                virtualTool.Collectible.Code,
                processed,
                ex.GetType().Name,
                ex.Message);
        }
        finally
        {
            try
            {
                handled = processed > 0;
                if (handled)
                {
                    try
                    {
                        synced = WearableToolResolver.SyncVirtualToolStateToSource(virtualTool, sourceSlot, preserveDurability: true);
                    }
                    catch (Exception ex)
                    {
                        api.World.Logger.Warning("[animalicabodytools] Wearable scythe mode sync failed: player={0}, source={1}, error={2}: {3}",
                            byPlayer.PlayerName,
                            sourceCode,
                            ex.GetType().Name,
                            ex.Message);
                    }

                    try
                    {
                        sourceDurabilityCharged = WearableTargetedActionPatches.TryDamageSourceTool(
                            byPlayer.Entity,
                            sourceSlot,
                            sourceCode,
                            "scythe-cut:" + (block.Code?.ToShortString() ?? "unknown"),
                            amount: processed);

                        if (sourceDurabilityCharged)
                        {
                            WearableBlockBreakToolContext.MarkDurabilityCharged(byPlayer, blockSel.Position);
                        }
                    }
                    catch (Exception ex)
                    {
                        api.World.Logger.Warning("[animalicabodytools] Wearable scythe durability charge failed: player={0}, source={1}, amount={2}, error={3}: {4}",
                            byPlayer.PlayerName,
                            sourceCode,
                            processed,
                            ex.GetType().Name,
                            ex.Message);
                    }
                }
                else
                {
                    // No target was processed. Let the ordinary center break
                    // continue after the active slot is restored.
                    handling = EnumHandling.PassThrough;
                }

                activeSlot.Itemstack = originalActiveStack;
                activeSlot.MarkDirty();
            }
            finally
            {
                try
                {
                    if (suppressionStarted)
                    {
                        suppressedBreaks = WearableTargetedActionPatches.EndBlockBreakDurabilitySuppression();
                    }
                }
                finally
                {
                    try
                    {
                        scythe.RestoreWearableMode(previousTrimMode);
                    }
                    finally
                    {
                        bridgeDepth--;
                    }
                }
            }
        }

        string logKey = $"{byPlayer.PlayerUID}:{sourceCode}:{block.Code?.ToShortString() ?? "unknown"}:{handled}:{synced}:{processed}";
        if (WearableTargetedActionPatches.DiagnosticsEnabled && WearableTargetedActionPatches.TryLogOnce(logged, logKey))
        {
            api.World.Logger.VerboseDebug("[animalicabodytools] Wearable scythe bridge finished: handled={0}, synced={1}, processed={2}, durabilityCharged={3}, suppressedBreaks={4}, player={5}, block={6}, source={7}, virtual={8}, score={9}.",
                handled,
                synced,
                processed,
                sourceDurabilityCharged,
                suppressedBreaks,
                byPlayer.PlayerName,
                block.Code,
                sourceCode,
                virtualTool.Collectible.Code,
                selectedScore);
        }
    }

    private void HarvestScytheArea(ItemSpeciesLockedScythe scythe, BlockSelection blockSel, IServerPlayer byPlayer, float dropQuantityMultiplier, bool trimMode, ref int processed)
    {
        Block? eatenGrass = api.World.GetBlock(new AssetLocation("tallgrass-eaten-free"));
        Item? dryGrass = api.World.GetItem(new AssetLocation("drygrass"));
        int maxBreaks = Math.Max(1, scythe.MultiBreakQuantity + 1);
        Vec3d hitPosition = blockSel.Position.ToVec3d().Add(blockSel.HitPosition ?? new Vec3d(0.5, 0.5, 0.5));

        foreach (BlockPos pos in EnumerateScytheArea(blockSel.Position, hitPosition))
        {
            if (processed >= maxBreaks)
            {
                break;
            }

            Block target = api.World.BlockAccessor.GetBlock(pos);
            if (target == null || target.Id == 0 || !scythe.CanMultiBreak(target))
            {
                continue;
            }

            if (!api.World.Claims.TryAccess(byPlayer, pos, EnumBlockAccessFlags.BuildOrBreak))
            {
                continue;
            }

            string blockCode = target.Code?.ToShortString() ?? string.Empty;
            bool isVanillaTallGrass = string.Equals(target.Code?.Domain, "game", StringComparison.Ordinal)
                                      && WearableTargetedActionPatches.IsCuttableTallGrass(blockCode);

            // Match vanilla trim mode: already-eaten grass remains in place,
            // but still consumes a target slot and the corresponding tool wear.
            if (trimMode && blockCode.StartsWith("tallgrass-eaten-", StringComparison.Ordinal))
            {
                processed++;
                continue;
            }

            if (isVanillaTallGrass && dryGrass != null && (!trimMode || eatenGrass != null))
            {
                processed++;
                int quantity = Math.Max(1, (int)MathF.Round(dropQuantityMultiplier));
                Vec3d dropPosition = new(pos.X + 0.5, pos.InternalY + 0.5, pos.Z + 0.5);
                api.World.SpawnItemEntity(new ItemStack(dryGrass, quantity), dropPosition, new Vec3d(0, 0.05, 0));

                api.World.BlockAccessor.SetBlock(trimMode ? eatenGrass!.BlockId : 0, pos);
                api.World.BlockAccessor.MarkBlockDirty(pos);
                if (trimMode)
                {
                    BlockEntityTransient? transient = api.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityTransient;
                    if (transient != null && target.Code != null)
                    {
                        transient.ConvertToOverride = target.Code.ToShortString();
                    }
                }

                continue;
            }

            // The current BlockAccessor.BreakBlock implementation calls this
            // block's OnBlockBroken once and updates its neighbors. It does not
            // raise the initiating server BreakBlock event, so it cannot recurse
            // into this area bridge; bridgeDepth remains a defensive guard.
            processed++;
            api.World.BlockAccessor.BreakBlock(pos, byPlayer, dropQuantityMultiplier);
            api.World.BlockAccessor.MarkBlockDirty(pos);
        }
    }

    private static IEnumerable<BlockPos> EnumerateScytheArea(BlockPos center, Vec3d hitPosition)
    {
        yield return center.Copy();

        List<(BlockPos Position, double Distance, int Order)> nearby = new(26);
        int order = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dy == 0 && dz == 0)
                    {
                        continue;
                    }

                    BlockPos position = center.AddCopy(dx, dy, dz);
                    nearby.Add((position, hitPosition.SquareDistanceTo(position.X + 0.5, position.InternalY + 0.5, position.Z + 0.5), order++));
                }
            }
        }

        nearby.Sort(static (left, right) =>
        {
            int distanceOrder = left.Distance.CompareTo(right.Distance);
            return distanceOrder != 0 ? distanceOrder : left.Order.CompareTo(right.Order);
        });
        foreach ((BlockPos position, _, _) in nearby)
        {
            yield return position;
        }
    }
}
