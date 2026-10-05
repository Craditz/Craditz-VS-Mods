#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Essentials;

namespace FeralKinshipCompanions;

internal readonly record struct CompanionBlockedPathCell(int Dimension, int X, int Y, int Z);

/// <summary>
/// Carries companion-only closed-door exclusions through vanilla's asynchronous
/// pathfinder without changing A* behavior for wild animals or unrelated mods.
/// A returned route is inspected on the main thread first; only door cells
/// actually crossed by that route are excluded from its bounded replan.
/// </summary>
internal static class FeralKinshipCompanionPathPolicy
{
    private sealed class TraverserPolicy
    {
        internal TraverserPolicy(HashSet<CompanionBlockedPathCell> blockedCells, bool retainFirstStep)
        {
            BlockedCells = blockedCells;
            RetainFirstStep = retainFirstStep;
        }

        internal HashSet<CompanionBlockedPathCell> BlockedCells { get; }
        internal bool RetainFirstStep { get; }
    }

    internal sealed class TaskPolicy
    {
        internal TaskPolicy(HashSet<CompanionBlockedPathCell> blockedCells, float collisionHeight, bool retainFirstStep)
        {
            BlockedCells = blockedCells;
            CollisionHeight = collisionHeight;
            RetainFirstStep = retainFirstStep;
        }

        internal HashSet<CompanionBlockedPathCell> BlockedCells { get; }
        internal float CollisionHeight { get; }
        internal bool RetainFirstStep { get; }
        internal Vec3d? FirstStep { get; set; }

        internal bool Blocks(PathNode node, Cardinal fromDirection)
        {
            if (BlocksColumn(node.dimension, node.X, node.Y, node.Z)) return true;
            return BlocksColumn(
                node.dimension,
                node.X - fromDirection.Normali.X,
                node.Y - fromDirection.Normali.Y,
                node.Z - fromDirection.Normali.Z);
        }

        private bool BlocksColumn(int dimension, int x, int feetY, int z)
        {
            int topY = feetY + Math.Max(0, (int)Math.Ceiling(CollisionHeight) - 1);
            for (int y = feetY; y <= topY; y++)
            {
                if (BlockedCells.Contains(new CompanionBlockedPathCell(dimension, x, y, z)))
                {
                    return true;
                }
            }
            return false;
        }
    }

    private static readonly object PolicyLock = new();
    private static readonly ConditionalWeakTable<WaypointsTraverser, TraverserPolicy> TraverserPolicies = new();
    private static readonly ConditionalWeakTable<PathfinderTask, TaskPolicy> TaskPolicies = new();

    [ThreadStatic]
    private static TaskPolicy? activeTaskPolicy;
    [ThreadStatic]
    private static TaskPolicy? scopedTaskPolicy;

    internal readonly record struct SearchScope(TaskPolicy? Previous, TaskPolicy? Current);

    internal static SearchScope BeginSearch()
    {
        SearchScope scope = new(scopedTaskPolicy, activeTaskPolicy);
        scopedTaskPolicy = activeTaskPolicy;
        activeTaskPolicy = null;
        if (scopedTaskPolicy != null) scopedTaskPolicy.FirstStep = null;
        return scope;
    }

    internal static void RememberFirstStep(AStar astar, List<PathNode>? path)
    {
        if (scopedTaskPolicy?.RetainFirstStep == true && path?.Count > 0)
            scopedTaskPolicy.FirstStep = path[0].ToWaypoint().Add(astar.centerOffsetX, 0, astar.centerOffsetZ);
    }

    internal static void EndSearch(SearchScope scope, List<Vec3d>? result, Exception? error)
    {
        try
        {
            // 1.22.6 retracePath excludes the start node, but ToWaypoints starts
            // at index one. Keep that first actual step on opted-in journeys.
            Vec3d? first = scope.Current?.FirstStep;
            if (error == null && result != null && first != null
                && (result.Count == 0 || result[0].SquareDistanceTo(first) > 0.000001))
                result.Insert(0, first);
        }
        finally { scopedTaskPolicy = scope.Previous; }
    }

    internal static void SetBlockedDoorCells(
        WaypointsTraverser traverser,
        IEnumerable<CompanionBlockedPathCell> blockedCells,
        bool retainFirstStep = false)
    {
        HashSet<CompanionBlockedPathCell> snapshot = new(blockedCells);
        lock (PolicyLock)
        {
            TraverserPolicies.Remove(traverser);
            TraverserPolicies.Add(traverser, new TraverserPolicy(snapshot, retainFirstStep));
        }
    }

    internal static void ClearPreparedPolicy(WaypointsTraverser traverser)
    {
        lock (PolicyLock) TraverserPolicies.Remove(traverser);
    }

    internal static void CapturePreparedTask(WaypointsTraverser traverser, PathfinderTask? task)
    {
        CompanionPathDiagnostics.Prepared(traverser, task);
        if (task == null) return;
        lock (PolicyLock)
        {
            if (!TraverserPolicies.TryGetValue(traverser, out TraverserPolicy? traverserPolicy)) return;
            TraverserPolicies.Remove(traverser);
            if (traverserPolicy.BlockedCells.Count == 0 && !traverserPolicy.RetainFirstStep) return;
            TaskPolicies.Remove(task);
            TaskPolicies.Add(task, new TaskPolicy(
                new HashSet<CompanionBlockedPathCell>(traverserPolicy.BlockedCells),
                task.collisionBox.YSize, traverserPolicy.RetainFirstStep));
        }
    }

    internal static void ActivateTask(PathfinderTask? task)
    {
        CompanionPathDiagnostics.Activate(task);
        activeTaskPolicy = task != null && TaskPolicies.TryGetValue(task, out TaskPolicy? policy)
            ? policy
            : null;
    }

    internal static bool Blocks(PathNode node, Cardinal fromDirection)
    {
        return scopedTaskPolicy?.Blocks(node, fromDirection) == true;
    }
}

[HarmonyPatch(typeof(AStar), nameof(AStar.FindPathAsWaypoints))]
internal static class FeralKinshipCompanionWaypointScopePatch
{
    [HarmonyPrefix]
    private static void Prefix(out FeralKinshipCompanionPathPolicy.SearchScope __state) =>
        __state = FeralKinshipCompanionPathPolicy.BeginSearch();

    [HarmonyFinalizer]
    private static Exception? Finalizer(List<Vec3d>? __result, Exception? __exception,
        FeralKinshipCompanionPathPolicy.SearchScope __state)
    {
        FeralKinshipCompanionPathPolicy.EndSearch(__state, __result, __exception);
        return __exception;
    }
}

[HarmonyPatch(typeof(AStar), nameof(AStar.FindPathOrEscapePath))]
internal static class FeralKinshipCompanionFirstStepPatch
{
    [HarmonyPostfix]
    private static void Postfix(AStar __instance, List<PathNode>? __result) =>
        FeralKinshipCompanionPathPolicy.RememberFirstStep(__instance, __result);
}

[HarmonyPatch(typeof(WaypointsTraverser), nameof(WaypointsTraverser.PreparePathfinderTask))]
internal static class FeralKinshipCompanionPreparePathPatch
{
    [HarmonyPostfix]
    private static void Postfix(WaypointsTraverser __instance, PathfinderTask? __result)
    {
        FeralKinshipCompanionPathPolicy.CapturePreparedTask(__instance, __result);
    }
}

[HarmonyPatch(typeof(PathfindingAsync), "Next")]
internal static class FeralKinshipCompanionActivatePathPolicyPatch
{
    [HarmonyPostfix]
    private static void Postfix(PathfinderTask? __result)
    {
        FeralKinshipCompanionPathPolicy.ActivateTask(__result);
    }
}

[HarmonyPatch(typeof(AStar), "traversable")]
internal static class FeralKinshipCompanionClosedDoorPathPatch
{
    [HarmonyPrefix]
    private static bool Prefix(PathNode node, Cardinal fromDir, ref bool __result)
    {
        if (!FeralKinshipCompanionPathPolicy.Blocks(node, fromDir)) return true;
        __result = false;
        return false;
    }
}
