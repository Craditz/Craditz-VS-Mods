using System;

namespace FeralKinshipCompanions;

/// <summary>
/// Pure, deterministic companion progression math. Keeping this independent of
/// world state makes every threshold testable without starting Vintage Story.
/// </summary>
public static class CompanionProgressionRules
{
    public const int StartingLevel = 1;
    public const int LastLinearLevel = 25;
    public const int AdulthoodExperience = 100;
    public const int CleanupActionsPerExperience = 8;
    public const int FamilyBonusPercent = 25;
    public const int MaximumBossBaseExperience = 1000;
    public const long CombatEncounterTimeoutMilliseconds = 30L * 60L * 1000L;

    public static long GetRequiredExperience(int currentLevel)
    {
        int level = Math.Max(StartingLevel, currentLevel);
        if (level < LastLinearLevel)
        {
            return 40L + 10L * (level - StartingLevel);
        }

        double required = 300d * Math.Pow(1.35d, level - LastLinearLevel);
        if (!double.IsFinite(required) || required >= long.MaxValue)
        {
            return long.MaxValue;
        }

        return Math.Max(1L, (long)Math.Ceiling(required));
    }

    public static int CalculateKillBaseExperience(float baseMaximumHealth)
    {
        if (!float.IsFinite(baseMaximumHealth) || baseMaximumHealth < 8f)
        {
            return 0;
        }

        if (baseMaximumHealth < 50f)
        {
            return Math.Max(1, (int)Math.Ceiling(baseMaximumHealth / 2d));
        }

        return Math.Min(
            MaximumBossBaseExperience,
            Math.Max(1, (int)Math.Ceiling(baseMaximumHealth * 2d)));
    }

    public static int ApplyFamilyBonus(int baseExperience, bool eligible)
    {
        int safeBase = Math.Max(0, baseExperience);
        if (!eligible || safeBase == 0)
        {
            return safeBase;
        }

        long result = safeBase + (long)safeBase * FamilyBonusPercent / 100L;
        return result >= int.MaxValue ? int.MaxValue : (int)result;
    }

    public static int CalculateExpeditionExperience(float targetStrength, float completion)
    {
        if (!float.IsFinite(targetStrength) || targetStrength <= 0f)
        {
            return 0;
        }

        double unscaled = Math.Round(targetStrength * 5d, MidpointRounding.AwayFromZero);
        int baseExperience = unscaled >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)unscaled);
        double safeCompletion = float.IsFinite(completion) ? Math.Clamp(completion, 0f, 1f) : 0d;
        double scaled = Math.Round(baseExperience * safeCompletion, MidpointRounding.AwayFromZero);
        return Math.Max(1, scaled >= int.MaxValue ? int.MaxValue : (int)scaled);
    }

    public static ProgressionResult AddExperience(int level, long currentExperience, int amount)
    {
        int nextLevel = Math.Max(StartingLevel, level);
        long remaining = Math.Max(0L, currentExperience);
        long lifetimeAdded = Math.Max(0, amount);
        if (lifetimeAdded == 0)
        {
            return new ProgressionResult(nextLevel, remaining, 0, 0L);
        }

        remaining = SaturatingAdd(remaining, lifetimeAdded);
        int levelsGained = 0;
        while (nextLevel < int.MaxValue)
        {
            long required = GetRequiredExperience(nextLevel);
            if (remaining < required)
            {
                break;
            }

            remaining -= required;
            nextLevel++;
            levelsGained++;
        }

        return new ProgressionResult(nextLevel, remaining, levelsGained, lifetimeAdded);
    }

    public static long SaturatingAdd(long left, long right)
    {
        if (right <= 0L) return Math.Max(0L, left);
        long safeLeft = Math.Max(0L, left);
        return safeLeft > long.MaxValue - right ? long.MaxValue : safeLeft + right;
    }
}

public readonly record struct ProgressionResult(
    int Level,
    long CurrentExperience,
    int LevelsGained,
    long LifetimeExperienceAdded);
