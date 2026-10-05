#nullable enable

using System;

namespace FeralKinshipCompanions;

/// <summary>Shared-slot arbitration and stale-callback policy for source idle cues.</summary>
internal static class CompanionSourceIdleSoundPolicy
{
    internal const int Slot = 0;
    internal const float MaximumPriority = 1.20f;
    private const float FallbackPriorityGap = 0.001f;

    internal static float EffectivePriority(float? sourcePriority)
        => sourcePriority.HasValue && float.IsFinite(sourcePriority.Value)
            ? System.Math.Min(sourcePriority.Value, MaximumPriority)
            : MaximumPriority;

    internal static float FallbackPriorityForCancel(float lowestEffectivePriority)
        => lowestEffectivePriority - FallbackPriorityGap;

    internal static bool IsPlaybackCurrent(
        long callbackGeneration,
        long currentGeneration,
        bool isActiveSlot,
        bool entityAlive,
        bool entityActive,
        bool entityStillInWorld)
        => callbackGeneration == currentGeneration
            && isActiveSlot
            && entityAlive
            && entityActive
            && entityStillInWorld;
}

/// <summary>Shares one randomized re-entry delay across an entity's source idle sound clones.</summary>
internal sealed class CompanionSourceIdleEligibilityGate
{
    private bool initialized;
    private bool blocked;
    private long eligibleAtMs;

    internal void Update(bool currentlyBlocked, long nowMs, Func<int> chooseReentryDelayMs)
    {
        if (!initialized)
        {
            initialized = true;
            blocked = currentlyBlocked;
            eligibleAtMs = nowMs;
            return;
        }

        if (blocked && !currentlyBlocked)
        {
            eligibleAtMs = nowMs + Math.Max(0, chooseReentryDelayMs());
        }

        blocked = currentlyBlocked;
    }

    internal bool CanStart(bool currentlyBlocked, long nowMs)
        => initialized && !currentlyBlocked && !blocked && nowMs >= eligibleAtMs;

    internal bool CanStart(
        bool taskAllowed,
        bool sourceSoundsBlocked,
        long nowMs,
        Func<int> chooseReentryDelayMs)
    {
        Update(sourceSoundsBlocked, nowMs, chooseReentryDelayMs);
        return taskAllowed && CanStart(sourceSoundsBlocked, nowMs);
    }
}
