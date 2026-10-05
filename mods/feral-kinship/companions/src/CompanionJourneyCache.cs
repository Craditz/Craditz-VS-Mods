#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace FeralKinshipCompanions;

// Value-only identity: no entities, worlds, block accessors or search graphs retained.
internal readonly record struct JourneyMovementProfile(string Owner, int Dimension, int Creature,
    float X1, float Y1, float Z1, float X2, float Y2, float Z2, float StepHeight, int MaxFall);

internal sealed class CompanionJourneyCache
{
    internal const int MaxRoutes = 64, MaxSteps = 8192, MaxRouteSteps = 512;
    internal const long LifetimeMs = 120000;
    internal const long MaxLifetimeMs = 1800000;
    private sealed record Entry(long Id, JourneyMovementProfile Profile, JourneyEdge[] Route)
    {
        internal long LastUse { get; set; }
        internal long ExpiresAt { get; set; }
        internal long IdleLifetime { get; set; } = LifetimeMs;
    }
    internal sealed record Hint(long Id, JourneyEdge[] Route);
    private readonly List<Entry> entries = new();
    private long nextId, useSequence;
    internal int Count => entries.Count;
    internal int StepCount { get; private set; }

    internal void Expire(long now)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
            if (now >= entries[i].ExpiresAt) RemoveAt(i);
    }

    internal long Store(JourneyMovementProfile profile, IReadOnlyList<JourneyEdge> route, long now)
    {
        Expire(now);
        if (route.Count == 0 || route.Count > MaxRouteSteps) return 0;
        // Only keep continuous directed routes, never guess that a drop reverses.
        for (int i = 1; i < route.Count; i++) if (route[i - 1].To != route[i].From) return 0;
        for (int i = entries.Count - 1; i >= 0; i--)
            if (entries[i].Profile == profile && entries[i].Route[0].From == route[0].From
                && entries[i].Route[^1].To == route[^1].To) RemoveAt(i);
        while (entries.Count >= MaxRoutes || StepCount + route.Count > MaxSteps)
        {
            int oldest = 0;
            for (int i = 1; i < entries.Count; i++) if (entries[i].LastUse < entries[oldest].LastUse) oldest = i;
            RemoveAt(oldest);
        }
        Entry entry = new(++nextId, profile, route.ToArray()) { LastUse = ++useSequence, ExpiresAt = now + LifetimeMs };
        entries.Add(entry); StepCount += route.Count;
        return entry.Id;
    }

    internal Hint? Find(JourneyMovementProfile profile, JourneyCell start, IEnumerable<JourneyCell> goals, long now)
    {
        Expire(now);
        HashSet<JourneyCell> destinations = new(goals);
        Entry? best = null;
        double bestDistance = double.MaxValue;
        // Bounded metadata scan only; terrain checking belongs to the scheduler.
        foreach (Entry entry in entries)
        {
            if (entry.Profile != profile || !destinations.Contains(entry.Route[^1].To)) continue;
            foreach (JourneyEdge edge in entry.Route)
            {
                double distance = Math.Abs((double)start.X - edge.From.X) + Math.Abs((double)start.Y - edge.From.Y)
                    + Math.Abs((double)start.Z - edge.From.Z);
                if (distance < bestDistance) { bestDistance = distance; best = entry; }
            }
        }
        if (best == null) return null;
        return new Hint(best.Id, (JourneyEdge[])best.Route.Clone());
    }

    internal void ConfirmUse(long id, long now)
    {
        Expire(now);
        Entry? entry = entries.Find(candidate => candidate.Id == id);
        if (entry == null) return;
        // Refresh only after current-terrain validation, not a speculative lookup.
        entry.IdleLifetime = Math.Min(MaxLifetimeMs, entry.IdleLifetime + LifetimeMs);
        entry.ExpiresAt = now + entry.IdleLifetime;
        entry.LastUse = ++useSequence;
    }

    internal void Invalidate(long id)
    {
        for (int i = 0; i < entries.Count; i++) if (entries[i].Id == id) { RemoveAt(i); return; }
    }
    private void RemoveAt(int index) { StepCount -= entries[index].Route.Length; entries.RemoveAt(index); }
    internal void Clear() { entries.Clear(); StepCount = 0; }
}
