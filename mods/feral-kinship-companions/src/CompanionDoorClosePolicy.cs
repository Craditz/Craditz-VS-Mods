namespace FeralKinshipCompanions;

internal static class CompanionDoorClosePolicy
{
    // Zero means close now. Occupancy never expires into permission to close.
    // A crowded door gets one slower callback until clear or unloaded.
    internal static int NextPollDelay(long elapsedMs, bool crossedAndClear, bool occupied)
    {
        bool ready = elapsedMs >= 10000 || (crossedAndClear && elapsedMs >= 1500);
        return ready && !occupied ? 0 : elapsedMs >= 10000 ? 1000 : 250;
    }
}
