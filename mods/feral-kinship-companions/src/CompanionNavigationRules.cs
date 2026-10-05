#nullable enable

using System;

namespace FeralKinshipCompanions;

/// <summary>Progress along one returned route, independent of distance to its final goal.</summary>
internal sealed class CompanionRouteProgress
{
    private const double MinimumSquaredDistanceImprovement = 0.12d;
    private const int StalledSamples = 3;
    private int furthestWaypoint = -1;
    private double bestSquaredDistance = double.PositiveInfinity;
    private int stalledSamples;

    internal void Reset(int waypoint, double squaredDistance)
    {
        furthestWaypoint = waypoint;
        bestSquaredDistance = squaredDistance;
        stalledSamples = 0;
    }

    internal bool IsStalled(int waypoint, double squaredDistance)
    {
        // Missing route metadata must not bring back the straight-line test.
        // Vanilla's collision and stuck checks still operate independently.
        if (waypoint < 0 || !double.IsFinite(squaredDistance)) return false;
        if (waypoint > furthestWaypoint
            || (waypoint == furthestWaypoint
                && bestSquaredDistance - squaredDistance >= MinimumSquaredDistanceImprovement))
        {
            Reset(waypoint, squaredDistance);
            return false;
        }

        return ++stalledSamples >= StalledSamples;
    }
}

/// <summary>
/// Redistributes the old per-journey search allowance after no-route results.
/// Depth is a node limit, not an estimate of CPU time or actual nodes visited.
/// </summary>
internal sealed class CompanionSearchBudget
{
    private readonly int baseDepth;
    private readonly bool adaptive;
    private int requestedDepth;
    private bool routeAccepted;
    internal long RecoveryReserve { get; }
    internal long Allowance { get; }
    internal long CommittedDepth { get; private set; }
    internal int Requests { get; private set; }
    internal int LastDepth { get; private set; }
    internal int NextDepth => adaptive
        ? (int)Math.Min(requestedDepth, PlanningRemaining)
        : baseDepth;
    private long PlanningRemaining => Math.Max(0L,
        Allowance - CommittedDepth - (routeAccepted ? 0 : RecoveryReserve));

    internal CompanionSearchBudget(int baseDepth, int attempts, bool adaptive)
    {
        this.baseDepth = Math.Max(256, baseDepth);
        this.adaptive = adaptive;
        requestedDepth = this.baseDepth;
        Allowance = (long)this.baseDepth * Math.Max(1, attempts);
        RecoveryReserve = adaptive ? Math.Min(this.baseDepth, Allowance - this.baseDepth) : 0;
    }

    internal void Commit(int depth)
    {
        if (depth <= 0 || (adaptive && depth > Allowance - CommittedDepth))
            throw new ArgumentOutOfRangeException(nameof(depth));
        CommittedDepth += depth;
        Requests++;
        LastDepth = depth;
    }

    internal void NoRoute()
    {
        if (adaptive)
            requestedDepth = (int)Math.Min(int.MaxValue, PlanningRemaining);
    }

    internal void RouteFound()
    {
        routeAccepted = true;
        requestedDepth = baseDepth;
    }

    // Deterministic spread avoids using world RNG and does not delay the first request.
    internal static int RetryDelayMs(long entityId, bool adaptive) =>
        adaptive ? 150 + (int)(unchecked((ulong)entityId * 73UL) % 201) : 100;
}
