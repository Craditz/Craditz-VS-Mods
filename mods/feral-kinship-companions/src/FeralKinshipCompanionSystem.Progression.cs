using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    internal const string CompanionLevelKey = "feralKinshipCompanionLevel";
    internal const string CompanionCurrentExperienceKey = "feralKinshipCompanionCurrentExperience";
    internal const string CompanionLifetimeExperienceKey = "feralKinshipCompanionLifetimeExperience";
    internal const string AdulthoodExperienceGrantedKey = "feralKinshipAdulthoodExperienceGranted";
    internal const string CleanupDutyUnitsKey = "feralKinshipCleanupDutyUnits";

    private readonly Dictionary<long, ProgressionCombatEncounter> progressionCombatEncounters = new();
    private long progressionCombatCleanupListenerId;

    private static int GetCompanionLevel(ITreeAttribute? status) => Math.Max(
        CompanionProgressionRules.StartingLevel,
        status?.GetInt(CompanionLevelKey, CompanionProgressionRules.StartingLevel)
            ?? CompanionProgressionRules.StartingLevel);

    private static long GetCompanionCurrentExperience(ITreeAttribute? status) =>
        Math.Max(0L, status?.GetLong(CompanionCurrentExperienceKey, 0L) ?? 0L);

    private static long GetCompanionLifetimeExperience(ITreeAttribute? status) =>
        Math.Max(
            GetCompanionCurrentExperience(status),
            status?.GetLong(CompanionLifetimeExperienceKey, 0L) ?? 0L);

    internal int AwardCompanionExperience(Entity companion, int baseExperience, bool applyNearbyFamilyBonus = true)
    {
        if (serverApi == null || packRepository?.Loaded != true || baseExperience <= 0
            || !companion.Alive || !IsTamedFox(companion) || IsCompanionJuvenile(companion))
        {
            return 0;
        }

        bool familyBonus = applyNearbyFamilyBonus && HasNearbyProgressionFamily(companion);
        int awarded = CompanionProgressionRules.ApplyFamilyBonus(baseExperience, familyBonus);
        return AwardCompanionExperienceExact(companion, awarded);
    }

    private int AwardCompanionExperienceExact(Entity companion, int amount, bool announceLevelUp = true)
    {
        if (amount <= 0 || packRepository?.Loaded != true)
        {
            return 0;
        }

        ITreeAttribute? status = GetDomesticationStatus(companion, true);
        if (status == null)
        {
            return 0;
        }

        int oldLevel = GetCompanionLevel(status);
        long oldCurrent = GetCompanionCurrentExperience(status);
        ProgressionResult result = CompanionProgressionRules.AddExperience(oldLevel, oldCurrent, amount);
        status.SetInt(CompanionLevelKey, result.Level);
        status.SetLong(CompanionCurrentExperienceKey, result.CurrentExperience);
        status.SetLong(
            CompanionLifetimeExperienceKey,
            CompanionProgressionRules.SaturatingAdd(GetCompanionLifetimeExperience(status), amount));

        if (result.LevelsGained > 0)
        {
            AwardIndividualTalentPoints(companion, status, result.LevelsGained);
            string ownerUid = GetCompanionOwnerUid(companion);
            if (!string.IsNullOrWhiteSpace(ownerUid))
            {
                packRepository.AdjustPackPoints(ownerUid, result.LevelsGained);
            }

            if (announceLevelUp && TryGetOwnerPlayer(companion, out IServerPlayer? owner))
            {
                string fallback = result.LevelsGained == 1
                    ? "Something feels different. I think I am getting better at this."
                    : "That was a lot at once. I think I improved more than I realized.";
                EmitDialogueEvent(
                    companion,
                    owner!,
                    "exp.level_gain",
                    fallback,
                    CompanionDialoguePriority.High,
                    "exp.level_gain",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["oldLevel"] = oldLevel.ToString(),
                        ["newLevel"] = result.Level.ToString(),
                        ["levelsGained"] = result.LevelsGained.ToString(),
                        ["experienceAwarded"] = amount.ToString(),
                        ["levelGainKind"] = result.LevelsGained == 1 ? "single" : "multi"
                    });
            }
        }

        MarkSocialStateDirty(companion);
        RegisterFoxInPack(companion);
        SendStateToOwner(companion, string.Empty);
        SendPackStateToOwner(companion);
        return amount;
    }

    private int AwardRecordExperienceExact(FoxPackRecordV2 record, int amount)
    {
        if (amount <= 0 || packRepository?.Loaded != true)
        {
            return 0;
        }

        ProgressionResult result = CompanionProgressionRules.AddExperience(
            record.Level,
            record.CurrentLevelExperience,
            amount);
        record.Level = result.Level;
        record.CurrentLevelExperience = result.CurrentExperience;
        record.LifetimeExperience = CompanionProgressionRules.SaturatingAdd(record.LifetimeExperience, amount);
        if (result.LevelsGained > 0)
        {
            if (record.IsJuvenile)
            {
                record.BankedTalentPoints = SaturatingPointAdd(
                    record.BankedTalentPoints, result.LevelsGained);
            }
            else
            {
                record.Points = SaturatingPointAdd(record.Points, result.LevelsGained);
            }
            record.LifetimePoints = SaturatingPointAdd(record.LifetimePoints, result.LevelsGained);
            packRepository.AdjustPackPoints(record.OwnerUid, result.LevelsGained);
        }
        return amount;
    }

    private void AwardIndividualTalentPoints(Entity entity, ITreeAttribute status, int count)
    {
        int safeCount = Math.Max(0, count);
        if (safeCount == 0) return;
        if (IsCompanionJuvenile(entity))
        {
            status.SetInt(BankedTalentPointsKey, SaturatingPointAdd(
                status.GetInt(BankedTalentPointsKey, 0), safeCount));
        }
        else
        {
            status.SetInt(FoxPointsKey, SaturatingPointAdd(
                status.GetInt(FoxPointsKey, 0), safeCount));
        }
        status.SetInt(FoxLifetimePointsKey, SaturatingPointAdd(
            status.GetInt(FoxLifetimePointsKey, 0), safeCount));
        MarkSocialStateDirty(entity);
    }

    private static int SaturatingPointAdd(int current, int amount) =>
        (int)Math.Clamp((long)Math.Max(0, current) + Math.Max(0, amount), 0L, int.MaxValue);

    private void AwardIndividualAndPackPoint(Entity entity, ITreeAttribute status)
    {
        AwardIndividualTalentPoints(entity, status, 1);
        string ownerUid = GetCompanionOwnerUid(entity);
        if (!string.IsNullOrWhiteSpace(ownerUid))
        {
            packRepository?.AdjustPackPoints(ownerUid, 1);
        }
    }

    internal void RecordCleanupDutyUnit(Entity companion)
    {
        if (IsCompanionJuvenile(companion)) return;
        ITreeAttribute? status = GetDomesticationStatus(companion, true);
        if (status == null) return;
        int units = Math.Clamp(status.GetInt(CleanupDutyUnitsKey, 0), 0,
            CompanionProgressionRules.CleanupActionsPerExperience - 1) + 1;
        if (units >= CompanionProgressionRules.CleanupActionsPerExperience)
        {
            units = 0;
            AwardCompanionExperience(companion, 1);
        }
        status.SetInt(CleanupDutyUnitsKey, units);
        MarkSocialStateDirty(companion);
        RegisterFoxInPack(companion);
    }

    internal void AwardDutyExperience(Entity companion, int baseExperience) =>
        AwardCompanionExperience(companion, baseExperience);

    private void AddExperienceDeveloper(Entity companion, IServerPlayer owner, bool advanceLevel)
    {
        ITreeAttribute? status = GetDomesticationStatus(companion);
        if (status == null) return;
        int amount = advanceLevel
            ? (int)Math.Min(
                int.MaxValue,
                Math.Max(1L, CompanionProgressionRules.GetRequiredExperience(GetCompanionLevel(status))
                    - GetCompanionCurrentExperience(status)))
            : 100;
        AwardCompanionExperienceExact(companion, amount);
        packRepository?.Save();
        SendState(companion, owner, advanceLevel
            ? $"Developer progression: advanced to level {GetCompanionLevel(status)}."
            : $"Developer progression: awarded {amount} EXP.");
    }

    private bool HasNearbyProgressionFamily(Entity companion)
    {
        ITreeAttribute? status = GetDomesticationStatus(companion);
        string foxId = status?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(foxId)) return false;

        return loadedFoxes.Values.Any(other =>
            other.EntityId != companion.EntityId
            && other.Alive
            && IsTamedFox(other)
            && !IsFoxAwayFromWorld(other)
            && other.Pos.Dimension == companion.Pos.Dimension
            && other.Pos.SquareDistanceTo(companion.Pos.XYZ) <= 100d
            && AreProgressionFamily(status!, foxId, GetDomesticationStatus(other),
                GetDomesticationStatus(other)?.GetString(FoxIdKey, string.Empty) ?? string.Empty));
    }

    private static bool AreProgressionFamily(
        ITreeAttribute first,
        string firstId,
        ITreeAttribute? second,
        string secondId)
    {
        if (second == null || string.IsNullOrWhiteSpace(firstId) || string.IsNullOrWhiteSpace(secondId))
            return false;
        return IsDirectFamilyReference(first, secondId)
            || IsDirectFamilyReference(second, firstId);
    }

    private static bool IsDirectFamilyReference(ITreeAttribute status, string otherId) =>
        string.Equals(status.GetString(BondedPartnerIdKey, string.Empty), otherId, StringComparison.Ordinal)
        || string.Equals(status.GetString(ParentMotherIdKey, string.Empty), otherId, StringComparison.Ordinal)
        || string.Equals(status.GetString(ParentFatherIdKey, string.Empty), otherId, StringComparison.Ordinal);

    private static bool AreProgressionFamily(FoxPackRecordV2 first, FoxPackRecordV2 second) =>
        IsDirectFamilyReference(first, second.FoxId) || IsDirectFamilyReference(second, first.FoxId);

    private static bool IsDirectFamilyReference(FoxPackRecordV2 record, string otherId) =>
        string.Equals(record.BondedPartnerId, otherId, StringComparison.Ordinal)
        || string.Equals(record.ParentMotherId, otherId, StringComparison.Ordinal)
        || string.Equals(record.ParentFatherId, otherId, StringComparison.Ordinal);

    private void AwardExpeditionExperience(FoxExpeditionRecord expedition)
    {
        if (packRepository?.Loaded != true || expedition.SelectedFoxIds == null)
            return;
        List<FoxPackRecordV2> participants = expedition.SelectedFoxIds
            .Distinct(StringComparer.Ordinal)
            .Select(id => packRepository.TryGetRecord(id, out FoxPackRecordV2? record) ? record : null)
            .Where(record => record != null)
            .Cast<FoxPackRecordV2>()
            .ToList();
        float completion = expedition.Story?.Completion
            ?? Math.Clamp(ExpeditionCalculations.GetRawCompletion(
                expedition.ExpeditionStrength,
                expedition.TargetStrength), 0f, 1f);
        int baseExperience = CompanionProgressionRules.CalculateExpeditionExperience(
            expedition.TargetStrength,
            completion);
        foreach (FoxPackRecordV2 participant in participants)
        {
            bool familyBonus = participants.Any(other =>
                !ReferenceEquals(other, participant) && AreProgressionFamily(participant, other));
            Entity? loaded = loadedFoxes.Values.FirstOrDefault(entity =>
                string.Equals(GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty),
                    participant.FoxId, StringComparison.Ordinal));
            int amount = CompanionProgressionRules.ApplyFamilyBonus(baseExperience, familyBonus);
            if (loaded != null && loaded.Alive)
            {
                AwardCompanionExperienceExact(loaded, amount);
            }
            else
            {
                AwardRecordExperienceExact(participant, amount);
            }
        }
    }

    private void AwardAdulthoodExperienceIfNeeded(Entity adult, ITreeAttribute status)
    {
        if (packRepository?.Loaded != true
            || status.GetBool(AdulthoodExperienceGrantedKey, false)) return;
        status.SetBool(AdulthoodExperienceGrantedKey, true);
        AwardCompanionExperienceExact(
            adult,
            CompanionProgressionRules.AdulthoodExperience,
            announceLevelUp: false);
    }

    internal void RecordProgressionCombatDamage(Entity target, DamageSource source, float positiveDamage)
    {
        if (serverApi == null || positiveDamage <= 0f || target.EntityId <= 0)
            return;

        long now = UtcNowMs();
        if (progressionCombatEncounters.TryGetValue(target.EntityId, out ProgressionCombatEncounter? existing))
        {
            existing.LastPositiveDamageUtcMs = now;
        }

        Entity? attacker = source.GetCauseEntity() ?? source.SourceEntity;
        if (attacker == null || attacker.EntityId == target.EntityId
            || !attacker.Alive || !IsTamedFox(attacker) || IsCompanionJuvenile(attacker)
            || IsTamedFox(target))
        {
            return;
        }

        string foxId = GetDomesticationStatus(attacker)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(foxId))
        {
            EnsureFoxNumberIfNeeded(attacker);
            RegisterFoxInPack(attacker);
            foxId = GetDomesticationStatus(attacker)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        }
        if (string.IsNullOrWhiteSpace(foxId)) return;

        if (existing == null)
        {
            EntityBehaviorHealth? health = target.GetBehavior<EntityBehaviorHealth>();
            int baseExperience = CompanionProgressionRules.CalculateKillBaseExperience(
                health?.BaseMaxHealth ?? float.NaN);
            if (baseExperience <= 0) return;
            existing = new ProgressionCombatEncounter(baseExperience, now);
            progressionCombatEncounters[target.EntityId] = existing;
        }

        bool familyBonus = HasNearbyProgressionFamily(attacker);
        if (existing.Contributors.TryGetValue(foxId, out bool alreadyEligible))
        {
            existing.Contributors[foxId] = alreadyEligible || familyBonus;
        }
        else
        {
            existing.Contributors[foxId] = familyBonus;
        }
    }

    private void OnProgressionEntityDeath(Entity target, DamageSource _)
    {
        if (!progressionCombatEncounters.Remove(target.EntityId, out ProgressionCombatEncounter? encounter)
            || packRepository?.Loaded != true)
        {
            return;
        }

        foreach ((string foxId, bool familyBonus) in encounter.Contributors)
        {
            int amount = CompanionProgressionRules.ApplyFamilyBonus(encounter.BaseExperience, familyBonus);
            Entity? loaded = loadedFoxes.Values.FirstOrDefault(entity =>
                string.Equals(GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty), foxId,
                    StringComparison.Ordinal));
            if (loaded != null && loaded.Alive)
            {
                AwardCompanionExperienceExact(loaded, amount);
            }
            else if (packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record) && record != null)
            {
                AwardRecordExperienceExact(record, amount);
            }
        }

        packRepository.Save();
    }

    private void OnPredatorRequestTargetDeath(Entity target, DamageSource _)
    {
        if (!IsPredatorSpecies(target) || packRepository?.Loaded != true) return;

        bool changed = false;
        foreach (Entity companion in loadedFoxes.Values.ToArray())
        {
            ITreeAttribute? status = GetDomesticationStatus(companion);
            if (status == null
                || !string.Equals(
                    status.GetString(ActiveRequestKey, string.Empty),
                    FoxRequestType.Predator,
                    StringComparison.Ordinal)
                || status.GetLong(PredatorTargetKey, 0) != target.EntityId
                || status.GetBool(PredatorTargetKilledKey, false))
            {
                continue;
            }

            status.SetBool(PredatorTargetKilledKey, true);
            MarkSocialStateDirty(companion);
            RegisterFoxInPack(companion);
            changed = true;
        }

        if (changed) packRepository.Save();
    }

    private void OnProgressionEntityDespawn(Entity target, EntityDespawnData _)
    {
        // Every non-death disappearance is intentionally final for this
        // encounter. Nothing is serialized and a reload can never revive it.
        progressionCombatEncounters.Remove(target.EntityId);
    }

    private void PurgeInactiveProgressionCombatEncounters(float _)
    {
        long cutoff = UtcNowMs() - CompanionProgressionRules.CombatEncounterTimeoutMilliseconds;
        foreach (long entityId in progressionCombatEncounters
                     .Where(pair => pair.Value.LastPositiveDamageUtcMs <= cutoff)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            progressionCombatEncounters.Remove(entityId);
        }
    }

    private sealed class ProgressionCombatEncounter
    {
        internal ProgressionCombatEncounter(int baseExperience, long nowUtcMs)
        {
            BaseExperience = baseExperience;
            LastPositiveDamageUtcMs = nowUtcMs;
        }

        internal int BaseExperience { get; }
        internal long LastPositiveDamageUtcMs { get; set; }
        internal Dictionary<string, bool> Contributors { get; } = new(StringComparer.Ordinal);
    }
}
