#nullable enable
using System;

namespace FeralKinshipCompanions;

internal readonly record struct AmbientIdleSoundSettings(float ChanceMultiplier, float MinSeconds, float MaxSeconds)
{
    internal static AmbientIdleSoundSettings Defaults => new(0.25f, 60f, 120f);

    internal static AmbientIdleSoundSettings Normalize(float chance, float minSeconds, float maxSeconds)
    {
        if (!float.IsFinite(chance) || chance < 0 || chance > 1) chance = Defaults.ChanceMultiplier;
        if (!float.IsFinite(minSeconds) || minSeconds < 0 || minSeconds > 3600) minSeconds = Defaults.MinSeconds;
        if (!float.IsFinite(maxSeconds) || maxSeconds < 0 || maxSeconds > 3600) maxSeconds = Defaults.MaxSeconds;
        return new(chance, minSeconds, Math.Max(minSeconds, maxSeconds));
    }

    internal int ChooseDelayMs(Random random)
    {
        int min = (int)(MinSeconds * 1000);
        int max = (int)(MaxSeconds * 1000);
        return min + random.Next(max - min + 1);
    }
}

/// <summary>One local-clock budget per animal; first cue is staggered as well.</summary>
internal sealed class AmbientIdleSoundBudget
{
    private bool initialized;
    private long eligibleAtMs;

    internal void Prepare(long nowMs, Func<int> chooseDelayMs)
    {
        if (initialized) return;
        initialized = true;
        eligibleAtMs = nowMs + Math.Max(0, chooseDelayMs());
    }

    internal bool TryConsume(long nowMs, Func<int> chooseDelayMs)
    {
        Prepare(nowMs, chooseDelayMs);
        if (nowMs < eligibleAtMs) return false;
        eligibleAtMs = nowMs + Math.Max(0, chooseDelayMs());
        return true;
    }

    internal bool IsEligible(long nowMs) => initialized && nowMs >= eligibleAtMs;
}
