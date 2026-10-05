using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace AnimalicaBodyTools.HarmonyPatches;

internal sealed class WearableBlockBreakContext
{
    public WearableBlockBreakContext(IPlayer player, Block block, BlockPos position)
    {
        Player = player;
        Block = block;
        Position = position;
    }

    public IPlayer Player { get; }
    public Block Block { get; }
    public BlockPos Position { get; }
    public bool CompatibilityReported { get; set; }
    public bool DurabilityCharged { get; set; }
    public ItemSlot? SourceSlot { get; set; }
    public string SourceCode { get; set; } = string.Empty;
    public EnumTool? ReportedTool { get; set; }
}

internal static class WearableBlockBreakToolContext
{
    [ThreadStatic]
    private static WearableBlockBreakContext? current;

    public static WearableBlockBreakContext? Current => current;

    public static WearableBlockBreakContext? Enter(IBlockAccessor accessor, BlockPos position, IPlayer? player)
    {
        WearableBlockBreakContext? previous = current;
        current = player == null ? null : new WearableBlockBreakContext(player, accessor.GetBlock(position), position);
        return previous;
    }

    public static void Restore(WearableBlockBreakContext? previous)
    {
        current = previous;
    }

    public static void MarkDurabilityCharged(IPlayer player, BlockPos position)
    {
        if (current != null && ReferenceEquals(current.Player, player) && SamePosition(current.Position, position))
        {
            current.DurabilityCharged = true;
        }
    }

    public static void Complete(WearableBlockBreakContext? completed)
    {
        if (completed == null
            || completed.DurabilityCharged
            || !completed.CompatibilityReported
            || completed.SourceSlot == null
            || WearableTargetedActionPatches.IsBlockBreakDurabilitySuppressed
            || completed.Player.Entity.World.Side != EnumAppSide.Server)
        {
            return;
        }

        completed.DurabilityCharged = WearableTargetedActionPatches.TryDamageSourceTool(
            completed.Player.Entity,
            completed.SourceSlot,
            completed.SourceCode,
            $"compatibility-block-break:{completed.Block.Code}:{completed.ReportedTool}");
    }

    private static bool SamePosition(BlockPos left, BlockPos right)
    {
        return left.X == right.X
            && left.InternalY == right.InternalY
            && left.Z == right.Z
            && left.dimension == right.dimension;
    }
}

[HarmonyPatch]
internal static class WearableBlockBreakContextPatch
{
    private static readonly Type? BreakCommandType = AccessTools.TypeByName("Packet_ClientBlockPlaceOrBreak");
    private static readonly FieldInfo? ModeField = BreakCommandType == null ? null : AccessTools.Field(BreakCommandType, "Mode");
    private static readonly FieldInfo? XField = BreakCommandType == null ? null : AccessTools.Field(BreakCommandType, "X");
    private static readonly FieldInfo? YField = BreakCommandType == null ? null : AccessTools.Field(BreakCommandType, "Y");
    private static readonly FieldInfo? ZField = BreakCommandType == null ? null : AccessTools.Field(BreakCommandType, "Z");

    private sealed class ScopeState
    {
        public WearableBlockBreakContext? Previous { get; init; }
        public bool Entered { get; init; }
    }

    private static MethodBase? TargetMethod()
    {
        Type? simulationType = AccessTools.TypeByName("Vintagestory.Server.ServerSystemBlockSimulation");
        return simulationType?.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method => method.Name == "TryModifyBlockInWorld" && method.GetParameters().Length == 2);
    }

    private static void Prefix(object __0, object __1, out ScopeState __state)
    {
        __state = new ScopeState();
        if (__0 is not IPlayer player
            || ModeField?.GetValue(__1) is not int mode
            || mode != 0
            || XField?.GetValue(__1) is not int x
            || YField?.GetValue(__1) is not int y
            || ZField?.GetValue(__1) is not int z)
        {
            return;
        }

        BlockPos position = new(x, y, z);
        __state = new ScopeState
        {
            Previous = WearableBlockBreakToolContext.Enter(player.Entity.World.BlockAccessor, position, player),
            Entered = true
        };
    }

    private static Exception? Finalizer(Exception? __exception, ScopeState __state)
    {
        if (__state.Entered)
        {
            WearableBlockBreakToolContext.Complete(WearableBlockBreakToolContext.Current);
            WearableBlockBreakToolContext.Restore(__state.Previous);
        }

        return __exception;
    }
}

[HarmonyPatch]
internal static class WearableActiveToolCompatibilityPatch
{
    private static readonly FieldInfo? PlayerField = AccessTools.Field("Vintagestory.Common.PlayerInventoryManager:player");
    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);

    private static MethodBase? TargetMethod()
    {
        Type? managerType = AccessTools.TypeByName("Vintagestory.Common.PlayerInventoryManager");
        return managerType == null ? null : AccessTools.PropertyGetter(managerType, "ActiveTool");
    }

    private static void Postfix(object __instance, ref EnumTool? __result)
    {
        if (__result != null || PlayerField?.GetValue(__instance) is not IPlayer player)
        {
            return;
        }

        WearableBlockBreakContext? context = WearableBlockBreakToolContext.Current;
        if (context == null || !ReferenceEquals(context.Player, player))
        {
            return;
        }

        ItemSlot? activeSlot = player.InventoryManager?.ActiveHotbarSlot;
        if (activeSlot == null || !activeSlot.Empty)
        {
            return;
        }

        if (!TryResolveReportedTool(
                player,
                activeSlot,
                context,
                out EnumTool reportedTool,
                out ItemStack virtualTool,
                out string sourceCode,
                out ItemSlot dummySlot,
                out ItemSlot sourceSlot))
        {
            return;
        }

        __result = reportedTool;
        context.CompatibilityReported = true;
        context.SourceSlot = sourceSlot;
        context.SourceCode = sourceCode;
        context.ReportedTool = reportedTool;

        if (WearableTargetedActionPatches.DiagnosticsEnabled
            && WearableTargetedActionPatches.TryLogOnce(
                Logged,
                $"{player.PlayerUID}:{context.Block.Code}:{sourceCode}:{reportedTool}"))
        {
            player.Entity.World.Logger.VerboseDebug(
                "[animalicabodytools] Equipped break ActiveTool compatibility: player={0}, block={1}, material={2}, source={3}, virtual={4}, reportedTool={5}.",
                player.PlayerName,
                context.Block.Code,
                context.Block.BlockMaterial,
                sourceCode,
                WearableTargetedActionPatches.DescribeTool(virtualTool, dummySlot),
                reportedTool);
        }
    }

    private static bool TryResolveReportedTool(
        IPlayer player,
        ItemSlot activeSlot,
        WearableBlockBreakContext context,
        out EnumTool reportedTool,
        out ItemStack virtualTool,
        out string sourceCode,
        out ItemSlot dummySlot,
        out ItemSlot sourceSlot)
    {
        reportedTool = default;
        virtualTool = null!;
        sourceCode = string.Empty;
        dummySlot = null!;
        sourceSlot = null!;

        EnumTool? materialTool = PreferredToolFor(context.Block.BlockMaterial);
        List<EnumTool> dropTools = GetDropTools(context.Block);

        if (materialTool.HasValue
            && dropTools.Contains(materialTool.Value)
            && TryResolveSpecificTool(player, activeSlot, materialTool.Value, out virtualTool, out sourceCode, out dummySlot, out sourceSlot))
        {
            reportedTool = materialTool.Value;
            return true;
        }

        foreach (EnumTool dropTool in dropTools)
        {
            if (TryResolveSpecificTool(player, activeSlot, dropTool, out virtualTool, out sourceCode, out dummySlot, out sourceSlot))
            {
                reportedTool = dropTool;
                return true;
            }
        }

        if (materialTool.HasValue
            && TryResolveSpecificTool(player, activeSlot, materialTool.Value, out virtualTool, out sourceCode, out dummySlot, out sourceSlot))
        {
            reportedTool = materialTool.Value;
            return true;
        }

        BlockSelection selection = new(context.Position, BlockFacing.UP, context.Block)
        {
            HitPosition = new Vec3d(0.5, 0.5, 0.5)
        };

        if (!WearableTargetedActionPatches.TryCreateVirtualMiningDummySlot(
                player,
                activeSlot,
                context.Block,
                selection,
                out virtualTool,
                out sourceCode,
                out dummySlot,
                out sourceSlot,
                out _))
        {
            return false;
        }

        EnumTool? fallbackTool = virtualTool.Collectible?.GetTool(dummySlot);
        if (!fallbackTool.HasValue)
        {
            return false;
        }

        reportedTool = fallbackTool.Value;
        return true;
    }

    private static bool TryResolveSpecificTool(
        IPlayer player,
        ItemSlot activeSlot,
        EnumTool reportedTool,
        out ItemStack virtualTool,
        out string sourceCode,
        out ItemSlot dummySlot,
        out ItemSlot sourceSlot)
    {
        EnumTool sourceTool = reportedTool == EnumTool.Shovel ? EnumTool.Knife : reportedTool;

        if (!WearableTargetedActionPatches.TryCreateVirtualDummySlot(
                player,
                activeSlot,
                stack => stack.Collectible?.Tool == sourceTool ? 1f : 0f,
                out virtualTool,
                out sourceCode,
                out dummySlot,
                out sourceSlot,
                out _))
        {
            return false;
        }

        return virtualTool.Collectible?.GetTool(dummySlot) == sourceTool;
    }

    private static List<EnumTool> GetDropTools(Block block)
    {
        List<EnumTool> tools = new();
        if (block.Drops == null)
        {
            return tools;
        }

        foreach (BlockDropItemStack drop in block.Drops)
        {
            if (drop.Tool.HasValue && !tools.Contains(drop.Tool.Value))
            {
                tools.Add(drop.Tool.Value);
            }
        }

        return tools;
    }

    private static EnumTool? PreferredToolFor(EnumBlockMaterial material)
    {
        return material switch
        {
            EnumBlockMaterial.Wood => EnumTool.Axe,
            EnumBlockMaterial.Plant or EnumBlockMaterial.Leaves or EnumBlockMaterial.Cloth => EnumTool.Knife,
            EnumBlockMaterial.Stone or EnumBlockMaterial.Ore or EnumBlockMaterial.Metal
                or EnumBlockMaterial.Mantle or EnumBlockMaterial.Glass or EnumBlockMaterial.Ceramic
                or EnumBlockMaterial.Brick or EnumBlockMaterial.Ice => EnumTool.Pickaxe,
            EnumBlockMaterial.Soil or EnumBlockMaterial.Gravel or EnumBlockMaterial.Sand or EnumBlockMaterial.Snow => EnumTool.Shovel,
            _ => null
        };
    }
}
