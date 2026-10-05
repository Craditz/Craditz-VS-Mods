#nullable enable

using System;

namespace FeralKinshipCompanions;

internal static class CompanionProtectionPolicy
{
    public const float ResponseRange = 20f;
    public const double ResponseRangeSquared = ResponseRange * ResponseRange;

    public static bool ShouldProtectNearbyOwnedCompanions(string? combatStyle) =>
        string.Equals(combatStyle, CompanionCombatStyle.Protect, StringComparison.Ordinal);

    public static bool ShouldAggressiveRespondToJuvenileThreats(string? combatStyle) =>
        string.Equals(combatStyle, CompanionCombatStyle.Aggressive, StringComparison.Ordinal);

    public static bool IsWithinOwnedCompanionScope(
        string protectorOwnerUid,
        int protectorDimension,
        string targetOwnerUid,
        int targetDimension,
        double distanceSquared)
    {
        return !string.IsNullOrWhiteSpace(protectorOwnerUid)
            && string.Equals(protectorOwnerUid, targetOwnerUid, StringComparison.Ordinal)
            && protectorDimension == targetDimension
            && double.IsFinite(distanceSquared)
            && distanceSquared >= 0d
            && distanceSquared <= ResponseRangeSquared;
    }

    public static bool IsUsableJuvenileThreatSnapshot(
        string guardianOwnerUid,
        int guardianDimension,
        string threatOwnerUid,
        int threatDimension,
        double distanceSquared,
        long nowMs,
        long expiresAtMs,
        bool attackerAlive,
        bool attackerActive,
        bool attackerIsAgent,
        bool attackerIsPlayer,
        bool attackerIsTamedCompanion)
    {
        return expiresAtMs > nowMs
            && attackerAlive
            && attackerActive
            && attackerIsAgent
            && !attackerIsPlayer
            && !attackerIsTamedCompanion
            && IsWithinOwnedCompanionScope(
                guardianOwnerUid,
                guardianDimension,
                threatOwnerUid,
                threatDimension,
                distanceSquared);
    }
}
