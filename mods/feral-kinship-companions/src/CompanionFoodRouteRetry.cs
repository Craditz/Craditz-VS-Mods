using System.Collections.Generic;
using System.Linq;

namespace FeralKinshipCompanions;

internal readonly record struct CompanionFoodBoardRouteKey(
    long EntityId, int Dimension, int X, int Y, int Z);

/// <summary>Temporarily skips only a board whose route failed for this body.</summary>
internal sealed class CompanionFoodRouteRetry
{
    internal const long RetryDelayMs = 15_000;
    private readonly Dictionary<CompanionFoodBoardRouteKey, long> retryAtMs = new();

    internal bool IsCoolingDown(CompanionFoodBoardRouteKey key, long nowMs)
    {
        if (!retryAtMs.TryGetValue(key, out long retryAt)) return false;
        if (nowMs < retryAt) return true;
        retryAtMs.Remove(key);
        return false;
    }

    internal void RecordFailure(CompanionFoodBoardRouteKey key, long nowMs)
    {
        // Bound stale body/board entries without retaining unloaded entities.
        foreach (CompanionFoodBoardRouteKey expired in retryAtMs
                     .Where(pair => pair.Value <= nowMs).Select(pair => pair.Key).ToArray())
            retryAtMs.Remove(expired);
        retryAtMs[key] = nowMs + RetryDelayMs;
    }

    internal void Clear(CompanionFoodBoardRouteKey key) => retryAtMs.Remove(key);
}
