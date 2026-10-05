using System;
using System.Collections.Generic;
using AnimalicaBodyTools.HarmonyPatches;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace AnimalicaBodyTools.Systems;

internal sealed class WearableAxeBreakBridgeSystem
{
    private readonly ICoreServerAPI api;
    private readonly HashSet<string> logged = new(StringComparer.Ordinal);
    private int bridgeDepth;

    public WearableAxeBreakBridgeSystem(ICoreServerAPI api)
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
        if (!IsLikelyTreeLog(block))
        {
            return;
        }

        ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
        if (activeSlot == null)
        {
            return;
        }

        BlockSelection axeBlockSel = new(blockSel.Position, blockSel.Face ?? BlockFacing.UP, block)
        {
            HitPosition = blockSel.HitPosition ?? new Vec3d(0.5, 0.5, 0.5),
            SelectionBoxIndex = blockSel.SelectionBoxIndex,
            SelectionBoxId = blockSel.SelectionBoxId,
            DidOffset = blockSel.DidOffset
        };

        if (!WearableTargetedActionPatches.TryCreateVirtualDummySlot(
                byPlayer,
                activeSlot,
                stack => ScoreAxeForBlock(stack, axeBlockSel, block, byPlayer),
                out ItemStack virtualTool,
                out string sourceCode,
                out ItemSlot dummySlot,
                out ItemSlot sourceSlot,
                out float selectedScore))
        {
            return;
        }

        if (virtualTool.Collectible?.GetTool(dummySlot) != EnumTool.Axe)
        {
            return;
        }

        handling = EnumHandling.PreventDefault;

        bool handled = false;
        bool invoked = false;
        bool lateSoundThreadFailure = false;
        int logBreakCount = 0;
        bridgeDepth++;
        WearableTargetedActionPatches.BeginBlockBreakDurabilitySuppression();
        try
        {
            invoked = true;
            handled = virtualTool.Collectible.OnBlockBrokenWith(api.World, byPlayer.Entity, dummySlot, axeBlockSel, dropQuantityMultiplier);
        }
        catch (Exception ex)
        {
            lateSoundThreadFailure = ex is InvalidOperationException
                                      && ex.Message.Contains("PlaySound", StringComparison.OrdinalIgnoreCase)
                                      && ex.Message.Contains("thread", StringComparison.OrdinalIgnoreCase);

            api.World.Logger.Warning("[animalicabodytools] Wearable axe break bridge interrupted: player={0}, block={1}, source={2}, virtual={3}, lateSoundThreadFailure={4}, error={5}: {6}",
                byPlayer.PlayerName,
                block.Code,
                sourceCode,
                virtualTool.Collectible.Code,
                lateSoundThreadFailure,
                ex.GetType().Name,
                ex.Message);
        }
        finally
        {
            logBreakCount = WearableTargetedActionPatches.EndBlockBreakDurabilitySuppression();
            bridgeDepth--;
        }

        bool shouldChargeDurability = handled || (invoked && lateSoundThreadFailure && IsGrownLog(block));
        int durabilityDamage = shouldChargeDurability ? Math.Max(1, logBreakCount) : 0;
        if (shouldChargeDurability)
        {
            bool damaged = WearableTargetedActionPatches.TryDamageSourceTool(byPlayer.Entity, sourceSlot, sourceCode, "axe-log-break:" + (block.Code?.ToShortString() ?? "unknown") + ":logs=" + logBreakCount, durabilityDamage);
            if (damaged)
            {
                WearableBlockBreakToolContext.MarkDurabilityCharged(byPlayer, blockSel.Position);
            }
        }

        string logKey = $"{byPlayer.PlayerUID}:{sourceCode}:{block.Code?.ToShortString() ?? "unknown"}:{handled}:{shouldChargeDurability}:{durabilityDamage}";
        if (WearableTargetedActionPatches.DiagnosticsEnabled && WearableTargetedActionPatches.TryLogOnce(logged, logKey))
        {
            api.World.Logger.VerboseDebug("[animalicabodytools] Wearable axe break bridge handled log break: handled={0}, charged={1}, durabilityDamage={2}, logBreakCount={3}, lateSoundThreadFailure={4}, player={5}, block={6}, source={7}, virtual={8}, score={9}.",
                handled,
                shouldChargeDurability,
                durabilityDamage,
                logBreakCount,
                lateSoundThreadFailure,
                byPlayer.PlayerName,
                block.Code,
                sourceCode,
                virtualTool.Collectible.Code,
                selectedScore);
        }
    }

    private static float ScoreAxeForBlock(ItemStack stack, BlockSelection blockSel, Block block, IPlayer player)
    {
        if (stack.Collectible?.Tool != EnumTool.Axe)
        {
            return 0f;
        }

        return stack.Collectible.GetMiningSpeed(stack, blockSel, block, player);
    }

    private static bool IsLikelyTreeLog(Block block)
    {
        string path = block.Code?.Path ?? string.Empty;
        return WearableTargetedActionPatches.IsLogBlockCode(path);
    }

    private static bool IsGrownLog(Block block)
    {
        string path = block.Code?.Path ?? string.Empty;
        return path.StartsWith("log-grown-", StringComparison.Ordinal)
               || path.StartsWith("bamboo-grown-", StringComparison.Ordinal);
    }
}
