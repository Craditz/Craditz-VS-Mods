#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace FeralKinshipCompanions;

internal sealed class MiningProbeStation
{
    public QuarryPoint Point { get; set; }
    public bool AlongX { get; set; }
    public int Branch { get; set; }
    public bool Descending { get; set; }
}

internal sealed partial class WorkCartMiningState
{
    private const int MaximumProbeEntries = 4096;
    public List<MiningProbeStation> ProbePlan { get; set; } = new();
    public double ProbeRadius { get; set; }
    // Missing in old saves: their completed numeric prefix may include legacy
    // skipped out-of-range stations, so it must not suppress later expansion.
    public int ProbeHistoryCount { get; set; } = -1;

    internal static bool WithinWorkArea(QuarryPoint point, QuarryPoint cart, double radius)
    {
        // Full voxel corners, with a little room for the wall, use the same
        // cart-centered cylinder as player area notices and range upgrades.
        double limit = Math.Max(0, radius - .25);
        double dx = Math.Abs((double)point.X - cart.X) + .5;
        double dz = Math.Abs((double)point.Z - cart.Z) + .5;
        return dx * dx + dz * dz <= limit * limit;
    }

    internal List<MiningProbeStation> ProbeEntries() => ProbePlan?.Count > 0 ? ProbePlan
        : LegacyProbeEntries().Select((p, i) => new MiningProbeStation {
            Point = p.Point, AlongX = p.AlongX, Branch = p.Branch, Descending = i > 0 && i <= Depth }).ToList();

    private bool ValidProbePlan() => ProbePlan != null && ProbePlan.Count <= MaximumProbeEntries
        && double.IsFinite(ProbeRadius) && ProbeRadius >= 0
        && ProbeHistoryCount >= -1 && ProbeHistoryCount <= ProbePlan.Count
        && ProbePlan.All(p => p != null && p.Branch >= 0
            && Math.Abs((long)p.Point.X - Origin.X) <= 256 && Math.Abs((long)p.Point.Z - Origin.Z) <= 256
            && p.Point.Y >= Origin.Y - Depth && p.Point.Y <= Origin.Y);

    internal bool EnsureProbePlan(double radius)
    {
        if (!Started || UnreadablePayload != null || !double.IsFinite(radius) || radius < 4
            || ProbePlan.Count > 0 && ProbeHistoryCount >= 0 && radius <= ProbeRadius + .001) return false;
        bool migrating = ProbePlan.Count == 0 || ProbeHistoryCount < 0;
        if (migrating)
        {
            // Preserve the numeric completed prefix and all real terrain/cargo/
            // supports. New geometry rechecks actual old passages, not virtual air.
            var history = ProbeEntries().Take(Cursor).ToArray();
            ProbePlan.Clear(); ProbePlan.AddRange(history);
            ProbeHistoryCount = history.Length;
        }
        var known = ProbePlan.Skip(ProbeHistoryCount).Select(p => (p.Point, p.AlongX, p.Descending)).ToHashSet();
        int branchOffset = ProbePlan.Count == 0 ? 0 : ProbePlan.Max(p => p.Branch);
        foreach (var entry in BuildProbePlan(radius))
        {
            if (ProbePlan.Count >= MaximumProbeEntries) break;
            if (!known.Add((entry.Point, entry.AlongX, entry.Descending))) continue;
            if (entry.Branch > 0) entry.Branch += branchOffset;
            ProbePlan.Add(entry);
        }
        ProbeRadius = radius; Version = 3;
        if (Cursor < ProbePlan.Count && Mode is "complete" or "ready") Mode = "probing";
        Status = migrating ? "Probe route fitted to the Work Cart area; existing excavation and access retained."
            : "Cart range increased; additional reachable probe work added after the saved plan.";
        return true;
    }

    private List<MiningProbeStation> BuildProbePlan(double radius)
    {
        var result = new List<MiningProbeStation>();
        var cart = Origin.Add(0, 0, -2);
        // Reserve the whole two-by-two corner footprint, not just a probe
        // anchor. This includes both lanes and leaves the wall inside the area.
        bool Fits(QuarryPoint point)
        {
            if (point.Y < 7) return false;
            for (int x = 0; x <= 1; x++) for (int z = 0; z <= 1; z++)
            {
                if (!WithinWorkArea(point.Add(x, 0, z), cart, radius)) return false;
            }
            return true;
        }
        void Add(QuarryPoint point, bool alongX, int branch = 0, bool descending = false)
            => result.Add(new() { Point = point, AlongX = alongX, Branch = branch, Descending = descending });
        var current = Origin;
        if (!Fits(current)) return result;
        Add(current, false);
        (int X, int Z)[] headings = { (0, 1), (1, 0), (0, -1), (-1, 0) };
        int dx = 0, dz = 1;
        var stairs = new HashSet<QuarryPoint>();
        void ReserveStair(QuarryPoint point, bool alongX)
        {
            foreach (var cell in Passage(point, alongX)) stairs.Add(cell);
            for (int below = 1; below <= 3; below++) for (int lane = 0; lane < 2; lane++)
                stairs.Add(point.Add(alongX ? 0 : lane, -below, alongX ? lane : 0));
        }
        for (int i = 0; i < Depth; i++)
        {
            var heading = headings[i / 12 % 4];
            bool turning = i > 0 && i % 12 == 0;
            int stride = turning && (heading.X > 0 || heading.Z > 0) ? 2 : 1;
            var next = current.Add(heading.X * stride, -1, heading.Z * stride);
            if (!Fits(next)) break;
            current = next; dx = heading.X; dz = heading.Z;
            Add(current, dx != 0, descending: true); ReserveStair(current, dx != 0);
        }
        // The bottom landing is the shared connection, not an obstacle to its
        // first corridor. Earlier stairs' headroom/support columns stay reserved.
        foreach (var cell in Passage(current, dx != 0)) stairs.Remove(cell);
        var visited = new HashSet<QuarryPoint> { current };
        bool LandingClear(QuarryPoint point) => !Passage(point, false).Any(stairs.Contains)
            && !Passage(point.Add(0, 0, 1), false).Any(stairs.Contains);
        void AddLanding(QuarryPoint point, int branchId)
        {
            Add(point, false, branchId);
            Add(point.Add(0, 0, 1), false, branchId);
        }
        int branch = 0;
        for (int step = 1; step <= Length; step++)
        {
            (int X, int Z)? chosen = null;
            foreach (var heading in new[] { (X: dx, Z: dz), (X: dz, Z: -dx), (X: -dz, Z: dx), (X: -dx, Z: -dz) })
            {
                var next = current.Add(heading.X, 0, heading.Z);
                if (!Fits(next) || visited.Contains(next)
                    || Passage(next, heading.X != 0).Any(stairs.Contains)
                    || Passage(current, heading.X != 0).Any(stairs.Contains)
                    || (dx != 0) != (heading.X != 0) && !LandingClear(current)) continue;
                chosen = heading; break;
            }
            if (chosen == null) break; // Finite area exhausted: normal completion.
            if ((dx != 0) != (chosen.Value.X != 0)) AddLanding(current, 0);
            dx = chosen.Value.X; dz = chosen.Value.Z;
            current = current.Add(dx, 0, dz); visited.Add(current); Add(current, dx != 0);
            if (step % 8 != 0) continue;
            foreach (int sign in new[] { -1, 1 })
            {
                branch++;
                int bx = -dz * sign, bz = dx * sign;
                bool alongX = bx != 0;
                var first = current.Add(bx, 0, bz);
                if (!Fits(first) || !LandingClear(current)
                    || Passage(first, alongX).Any(stairs.Contains)) continue;
                AddLanding(current, branch); // A supported two-by-two junction.
                for (int j = 1; j <= BranchLength; j++)
                {
                    var point = current.Add(bx * j, 0, bz * j);
                    if (!Fits(point) || Passage(point, alongX).Any(stairs.Contains)) break;
                    Add(point, alongX, branch);
                }
            }
        }
        return result;
    }
}
