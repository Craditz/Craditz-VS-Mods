namespace AnimalicaShared;

// Source-linked by the three basic land-follow implementations. Movement,
// teleport eligibility and aquatic/air routing stay owned by each mod.
internal static class BasicFollowTiming
{
    internal const int SearchDepth = 3000;
    internal const int RefreshIntervalMs = 750;
    internal const int SearchTimeoutMs = 5000;
    internal const int RetryIntervalMs = 1500;
    internal static long RetryAt(long now, long entityId) => now + RetryIntervalMs + (entityId & 255);
}
