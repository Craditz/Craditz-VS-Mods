#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace FeralKinshipCompanions;

internal readonly record struct JourneyCell(int X, int Y, int Z);
internal readonly record struct JourneyDoor(JourneyCell Position, int NormalX, int NormalZ, bool Allowed);
internal readonly record struct JourneyEdge(JourneyCell From, JourneyCell To, double Cost, JourneyDoor? Door = null);

internal interface ICompanionJourneyGraph
{
    int DirectionCount { get; }
    bool TouchedUnloaded { get; }
    void BeginSlice();
    bool TryEdge(JourneyCell from, int direction, out JourneyEdge edge);
}

internal enum JourneySearchState { Searching, Tracing, Validating, Found, NoRoute, Unloaded, Limit, TimedOut, Cancelled, Invalidated, Error }

/// <summary>Owned A*: each unit probes at most one neighbour. No world or engine dependency.</summary>
internal sealed class CompanionJourneySearch
{
    private sealed record Node(double Cost, JourneyEdge? Parent);
    private readonly record struct Entry(JourneyCell Cell, double Cost);
    private readonly PriorityQueue<Entry, double> frontier = new();
    private readonly Dictionary<JourneyCell, Node> nodes = new();
    private readonly HashSet<JourneyCell> goals;
    private readonly int nodeLimit;
    private readonly int workLimit;
    private int minX, maxX, minZ, maxZ;
    private readonly JourneyCell start;
    private JourneyEdge[]? cachedRoute;
    private readonly Dictionary<JourneyCell, int> cachedJoins = new();
    private int cacheJoinIndex = -1;
    internal bool CacheUsed { get; private set; }
    internal bool CacheRejected { get; private set; }
    private Entry? expanding;
    private int direction;
    private int validationIndex;
    private int validationDirection;
    private JourneyCell tracing;
    internal ICompanionJourneyGraph Graph { get; }
    internal JourneySearchState State { get; private set; } = JourneySearchState.Searching;
    internal List<JourneyEdge> Route { get; } = new();
    internal JourneyCell Reached { get; private set; }
    internal int Work { get; private set; }
    internal int Expanded { get; private set; }
    internal int Slices { get; private set; }
    internal int PeakNodes { get; private set; } = 1;
    internal int RetainedNodes => nodes.Count + frontier.Count;
    internal double ProcessingMs { get; set; }
    internal string Error { get; private set; } = "none";
    internal bool Pending => State is JourneySearchState.Searching or JourneySearchState.Tracing or JourneySearchState.Validating;

    internal CompanionJourneySearch(ICompanionJourneyGraph graph, JourneyCell start,
        IEnumerable<JourneyCell> goals, int nodeLimit = 24000, int workLimit = 240000, JourneyEdge[]? cachedRoute = null)
    {
        Graph = graph;
        this.goals = new(goals);
        this.start = start;
        this.nodeLimit = Math.Max(1, nodeLimit);
        this.workLimit = Math.Max(1, workLimit);
        if (this.goals.Count == 0) { Finish(JourneySearchState.NoRoute); return; }
        if (cachedRoute is { Length: > 0 } && cachedRoute.Length <= CompanionJourneyCache.MaxRouteSteps
            && this.goals.Contains(cachedRoute[^1].To))
        {
            this.cachedRoute = (JourneyEdge[])cachedRoute.Clone();
            for (int i = 0; i < cachedRoute.Length; i++) cachedJoins[cachedRoute[i].From] = i;
        }
        SetEstimateBounds();
        nodes[start] = new Node(0, null);
        frontier.Enqueue(new Entry(start, 0), Estimate(start));
    }

    private void SetEstimateBounds()
    {
        IEnumerable<JourneyCell> targets = goals.Concat(cachedJoins.Keys);
        minX = targets.Min(p => p.X); maxX = targets.Max(p => p.X);
        minZ = targets.Min(p => p.Z); maxZ = targets.Max(p => p.Z);
    }

    private void RejectCachedRoute()
    {
        // Invalid hints spend their work from the SAME allowance and deadline.
        // They never turn a route edit into a permanently unreachable destination.
        CacheRejected = true; CacheUsed = false;
        cachedRoute = null; cachedJoins.Clear(); cacheJoinIndex = -1;
        Route.Clear(); ReleaseSearchMemory(); SetEstimateBounds();
        validationIndex = validationDirection = 0;
        nodes[start] = new Node(0, null);
        frontier.Enqueue(new Entry(start, 0), Estimate(start));
        State = JourneySearchState.Searching;
    }

    private double Estimate(JourneyCell cell)
    {
        // Horizontal distance is admissible even for a descent spanning several Y levels.
        double x = Math.Max(0, Math.Max((long)minX - cell.X, (long)cell.X - maxX));
        double z = Math.Max(0, Math.Max((long)minZ - cell.Z, (long)cell.Z - maxZ));
        return Math.Max(x, z) + (Math.Sqrt(2) - 1) * Math.Min(x, z);
    }

    internal void BeginSlice() { Slices++; Graph.BeginSlice(); }

    internal void Step()
    {
        if (!Pending) return;
        if (++Work > workLimit) { Finish(JourneySearchState.Limit); return; }
        if (State == JourneySearchState.Tracing)
        {
            if (nodes[tracing].Parent is JourneyEdge parent)
            {
                Route.Add(parent);
                tracing = parent.From;
            }
            else
            {
                Route.Reverse();
                if (cacheJoinIndex >= 0)
                {
                    Route.AddRange(cachedRoute!.Skip(cacheJoinIndex));
                    CacheUsed = true;
                }
                if (Route.Count == 0) Finish(JourneySearchState.Found);
                else State = JourneySearchState.Validating;
            }
            return;
        }
        if (State == JourneySearchState.Validating)
        {
            // Recheck accumulated terrain against current blocks, also spread over updates.
            JourneyEdge expected = Route[validationIndex];
            if (Graph.TryEdge(expected.From, validationDirection++, out JourneyEdge actual)
                && actual.To == expected.To && actual.Door == expected.Door)
            {
                validationIndex++;
                validationDirection = 0;
                if (validationIndex == Route.Count) Finish(JourneySearchState.Found);
            }
            else if (validationDirection >= Graph.DirectionCount)
            {
                if (CacheUsed) RejectCachedRoute();
                else Finish(JourneySearchState.Invalidated);
            }
            return;
        }

        if (expanding == null)
        {
            if (!frontier.TryDequeue(out Entry entry, out _))
            {
                Finish(Graph.TouchedUnloaded ? JourneySearchState.Unloaded : JourneySearchState.NoRoute);
                return;
            }
            if (!nodes.TryGetValue(entry.Cell, out Node? node) || entry.Cost != node.Cost) return;
            if (goals.Contains(entry.Cell))
            {
                Reached = entry.Cell;
                tracing = entry.Cell;
                State = JourneySearchState.Tracing;
                return;
            }
            if (cachedJoins.TryGetValue(entry.Cell, out cacheJoinIndex))
            {
                Reached = cachedRoute![^1].To;
                tracing = entry.Cell;
                State = JourneySearchState.Tracing;
                return;
            }
            cacheJoinIndex = -1;
            expanding = entry;
            direction = 0;
            Expanded++;
            return;
        }

        Entry current = expanding.Value;
        bool traversable = Graph.TryEdge(current.Cell, direction++, out JourneyEdge next);
        if (direction >= Graph.DirectionCount) expanding = null;
        if (!traversable || next.To == current.Cell || !double.IsFinite(next.Cost) || next.Cost <= 0) return;
        double cost = current.Cost + next.Cost;
        if (nodes.TryGetValue(next.To, out Node? previous) && previous.Cost <= cost) return;
        if (previous == null && nodes.Count >= nodeLimit) { Finish(JourneySearchState.Limit); return; }
        nodes[next.To] = new Node(cost, next);
        frontier.Enqueue(new Entry(next.To, cost), cost + Estimate(next.To));
        PeakNodes = Math.Max(PeakNodes, nodes.Count);
        // Lazy priority updates are bounded too, not just distinct coordinates.
        if (frontier.Count > nodeLimit * 4) Finish(JourneySearchState.Limit);
    }

    internal void Finish(JourneySearchState result, string? error = null)
    {
        State = result;
        if (error != null) Error = error;
        if (result != JourneySearchState.Found) Route.Clear();
        ReleaseSearchMemory();
        cachedRoute = null; cachedJoins.Clear();
    }

    private void ReleaseSearchMemory() { nodes.Clear(); frontier.Clear(); expanding = null; }
}

/// <summary>Round-robin planning. Time is checked between individual neighbour probes.</summary>
internal sealed class CompanionJourneyQueue
{
    internal sealed record Ticket(CompanionJourneySearch Search, long ExpiresAt);
    private readonly Queue<Ticket> queue = new();
    private readonly Queue<Ticket> cargoQueue = new();
    private int cargoVisits;
    internal int Count => queue.Count + cargoQueue.Count;
    internal int LastWork { get; private set; }
    internal double LastMs { get; private set; }
    internal int RetainedNodes => queue.Sum(ticket => ticket.Search.RetainedNodes)
        + cargoQueue.Sum(ticket => ticket.Search.RetainedNodes);

    internal bool Enqueue(CompanionJourneySearch search, long expiresAt, bool cargo = false)
    {
        // Completed/cancelled tickets must not deny admission to replacements.
        foreach (Queue<Ticket> lane in new[] { queue, cargoQueue })
        {
            int count = lane.Count;
            while (count-- > 0) { Ticket old = lane.Dequeue(); if (old.Search.Pending) lane.Enqueue(old); }
        }
        if (Count >= 32) return false;
        (cargo ? cargoQueue : queue).Enqueue(new Ticket(search, expiresAt));
        return true;
    }

    internal void Tick(long now, Func<double> milliseconds, double budgetMs = 3, int maxWork = 2048)
    {
        double start = milliseconds();
        LastWork = 0;
        int retained = RetainedNodes;
        while (Count > 0 && LastWork < maxWork && milliseconds() - start < budgetMs)
        {
            // Three short visits for pickup/delivery, then one for background
            // cleanup. Each lane stays round-robin; unused shares are borrowed.
            bool takeCargo = cargoQueue.Count > 0 && (queue.Count == 0 || cargoVisits < 3);
            Queue<Ticket> lane = takeCargo ? cargoQueue : queue;
            cargoVisits = takeCargo ? Math.Min(3, cargoVisits + 1) : 0;
            Ticket ticket = lane.Dequeue();
            CompanionJourneySearch search = ticket.Search;
            if (!search.Pending) continue;
            if (now >= ticket.ExpiresAt)
            {
                retained -= search.RetainedNodes;
                search.Finish(JourneySearchState.TimedOut);
                continue;
            }
            int before = search.RetainedNodes;
            double sliceStart = milliseconds();
            try
            {
                search.BeginSlice();
                for (int slice = 0; slice < 16 && search.Pending && LastWork < maxWork
                    && milliseconds() - start < budgetMs; slice++)
                {
                    search.Step();
                    LastWork++;
                }
            }
            catch (Exception error) { search.Finish(JourneySearchState.Error, error.GetType().Name); }
            search.ProcessingMs += milliseconds() - sliceStart;
            retained += search.RetainedNodes - before;
            if (retained > 192000)
            {
                retained -= search.RetainedNodes;
                search.Finish(JourneySearchState.Limit, "pack-memory-limit");
            }
            if (search.Pending) lane.Enqueue(ticket);
        }
        LastMs = milliseconds() - start;
    }

    internal void Clear()
    {
        while (queue.TryDequeue(out Ticket? ticket)) ticket.Search.Finish(JourneySearchState.Cancelled);
        while (cargoQueue.TryDequeue(out Ticket? ticket)) ticket.Search.Finish(JourneySearchState.Cancelled);
        cargoVisits = 0;
    }
}
