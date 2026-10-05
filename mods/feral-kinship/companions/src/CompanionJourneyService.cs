#nullable enable
using System;
using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;

/// <summary>All terrain queries and door decisions run on the server thread.</summary>
public sealed class CompanionJourneyService : ModSystem
{
    private readonly CompanionJourneyQueue queue = new();
    private ICoreServerAPI? api;
    private long listener;
    private long nextCacheSweep;
    internal CompanionJourneyCache Routes { get; } = new();
    internal CompanionTerrainProbe Probe { get; private set; } = null!;
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;
    public override void StartServerSide(ICoreServerAPI api)
    {
        this.api = api;
        Probe = new CompanionTerrainProbe(api);
        listener = api.Event.RegisterGameTickListener(_ =>
        {
            long now = api.World.ElapsedMilliseconds;
            if (now >= nextCacheSweep) { Routes.Expire(now); nextCacheSweep = now + 1000; }
            queue.Tick(now, ClockMs);
        }, 50);
    }
    private static double ClockMs() => Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;
    internal bool Submit(CompanionJourneySearch search, long expiresAt, bool cargo = false) => queue.Enqueue(search, expiresAt, cargo);
    public override void Dispose()
    {
        if (api != null) api.Event.UnregisterGameTickListener(listener);
        queue.Clear();
        Routes.Clear();
        base.Dispose();
    }
}
