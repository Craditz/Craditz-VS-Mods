namespace FeralKinshipCompanions;

/// <summary>Pure decisions for companion-only mortal wound recovery.</summary>
internal static class MortalWoundRecoveryPolicy
{
    public static bool IsHealthSufficient(float health, float woundMaximum, float currentMaximum)
    {
        if (!float.IsFinite(health)
            || !float.IsFinite(woundMaximum)
            || !float.IsFinite(currentMaximum)
            || health < 0f
            || woundMaximum <= 0f
            || currentMaximum <= 0f)
        {
            return false;
        }

        return health + 0.001f >= System.Math.Max(woundMaximum, currentMaximum);
    }

    public static bool CanPersistCallback(
        bool isSameWorldEntity,
        bool isAlive,
        bool shouldDespawn,
        bool isSameCompanion,
        bool isSameFoxAssignment,
        bool isSameRecordEntity,
        bool isSameWoundGeneration,
        bool isStillMortallyWounded)
    {
        return isSameWorldEntity
            && isAlive
            && !shouldDespawn
            && isSameCompanion
            && isSameFoxAssignment
            && isSameRecordEntity
            && isSameWoundGeneration
            && !isStillMortallyWounded;
    }
}
