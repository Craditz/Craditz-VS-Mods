#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Essentials;

namespace FeralKinshipCompanions;

internal sealed record CompanionSearchObservation(
    string Outcome, int NodesChecked, int Expanded, int Limit,
    double SearchMs, double QueueMs, bool MainThread);

internal sealed class CompanionSearchSession
{
    private readonly ConcurrentQueue<CompanionSearchObservation> completed = new();
    internal void Complete(CompanionSearchObservation observation) => completed.Enqueue(observation);

    internal CompanionSearchObservation[] Snapshot() => completed.ToArray();

    internal string Describe()
    {
        CompanionSearchObservation[] results = Snapshot();
        string samples = string.Join(";", results.Select(result =>
            FormattableString.Invariant($"{result.Outcome}:{result.NodesChecked}/{result.Limit}:{result.SearchMs:0.00}ms")));
        return FormattableString.Invariant(
            $"observed={results.Length} limits={results.Count(result => result.Outcome == "limit")} exhausted={results.Count(result => result.Outcome == "exhausted")} searchErrors={results.Count(result => result.Outcome == "exception")} expanded={results.Sum(result => (long)result.Expanded)} searchMs={results.Sum(result => result.SearchMs):0.00} queueMs={results.Sum(result => result.QueueMs):0.00} mainThreadSearches={results.Count(result => result.MainThread)} searches=[{samples}]");
    }
}

/// <summary>
/// Read-only measurements for explicitly tagged Companion requests. No world
/// access or logger calls occur on the pathfinding worker. Each navigation start
/// has its own session, so a late completion cannot contaminate its replacement.
/// </summary>
internal static class CompanionPathDiagnostics
{
    internal sealed class Request
    {
        internal required CompanionSearchSession Session;
        internal long QueuedAt = Stopwatch.GetTimestamp();
        internal int OwnerThread = Environment.CurrentManagedThreadId;
        internal long StartedAt;
    }

    private static readonly ConditionalWeakTable<WaypointsTraverser, CompanionSearchSession> Sessions = new();
    private static readonly ConditionalWeakTable<PathfinderTask, Request> Requests = new();
    [ThreadStatic] private static Request? active;

    internal static void Register(WaypointsTraverser traverser, CompanionSearchSession? session)
    {
        Sessions.Remove(traverser);
        if (session != null) Sessions.Add(traverser, session);
    }

    internal static void Prepared(WaypointsTraverser traverser, PathfinderTask? task)
    {
        if (task == null || !Sessions.TryGetValue(traverser, out CompanionSearchSession? session)) return;
        Requests.Remove(task);
        Requests.Add(task, new Request { Session = session });
    }

    internal static void Unregister(WaypointsTraverser traverser, CompanionSearchSession? session)
    {
        if (Sessions.TryGetValue(traverser, out CompanionSearchSession? current)
            && ReferenceEquals(current, session)) Sessions.Remove(traverser);
    }

    internal static void Activate(PathfinderTask? task)
    {
        active = task != null && Requests.TryGetValue(task, out Request? request) ? request : null;
    }

    internal static Request? Begin()
    {
        Request? request = active;
        active = null; // Never attribute later synchronous/unrelated A* calls to this request.
        if (request != null) request.StartedAt = Stopwatch.GetTimestamp();
        return request;
    }

    internal static string Classify(bool found, bool invalidInput, bool error, int nodes, int limit, int openNodes)
    {
        if (error) return "exception";
        if (invalidInput) return "invalid-input";
        if (found) return "found";
        // VS 1.22.6/1.22.7 uses `if (NodesChecked++ > searchDepth)`.
        // Exhaustion after exactly limit+1 expansions is NOT a budget exit.
        if (nodes > (long)limit + 1 && openNodes > 0) return "limit";
        return openNodes == 0 ? "exhausted" : "unknown";
    }

    internal static void End(Request request, AStar astar, List<PathNode>? result,
        Cuboidf collisionBox, int limit, Exception? error)
    {
        bool invalid = collisionBox == null || collisionBox.XSize > 100 || collisionBox.YSize > 100 || collisionBox.ZSize > 100;
        // The oversized-box early return does not reset A* counters.
        int nodes = invalid || error != null ? 0 : astar.NodesChecked;
        int expanded = invalid || error != null ? 0 : astar.closedSet.Count;
        string outcome = Classify(result != null, invalid, error != null, nodes, limit,
            invalid || error != null ? 0 : astar.openSet.Count);
        request.Session.Complete(new CompanionSearchObservation(outcome, nodes, expanded, limit,
            Stopwatch.GetElapsedTime(request.StartedAt).TotalMilliseconds,
            Stopwatch.GetElapsedTime(request.QueuedAt, request.StartedAt).TotalMilliseconds,
            Environment.CurrentManagedThreadId == request.OwnerThread));
    }
}

[HarmonyPatch(typeof(AStar), nameof(AStar.FindPathOrEscapePath))]
internal static class FeralKinshipCompanionSearchDiagnosticPatch
{
    [HarmonyPrefix]
    private static void Prefix(out CompanionPathDiagnostics.Request? __state)
    {
        __state = CompanionPathDiagnostics.Begin();
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(AStar __instance, List<PathNode>? __result,
        Cuboidf entityCollBox, int searchDepth, Exception? __exception,
        CompanionPathDiagnostics.Request? __state)
    {
        if (__state != null)
            CompanionPathDiagnostics.End(__state, __instance, __result, entityCollBox, searchDepth, __exception);
        return __exception; // Preserve engine failure behavior, including exceptions.
    }
}
