#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    // Native growth creates a fresh adult, not a load of the juvenile's health.
    // Registration can re-enter through numbering/naming before the growth
    // postfix. Keep this authority on the actual replacement reference only;
    // never serialize it or infer it from saved juvenile/points markers.
    private static readonly ConditionalWeakTable<Entity, object> nativeAdultGrowthHandoffs = new();
    private static readonly object nativeAdultGrowthHandoffMarker = new();

    internal static void CompleteCompanionAdultGrowthHandoff(Entity adult) =>
        nativeAdultGrowthHandoffs.Remove(adult);

    internal const string JuvenileKey = "feralKinshipJuvenile";
    internal const string JuvenileSpeciesKey = "feralKinshipJuvenileSpecies";
    internal const string ParentMotherIdKey = "feralKinshipMotherId";
    internal const string ParentMotherNameKey = "feralKinshipMotherName";
    internal const string ParentFatherIdKey = "feralKinshipFatherId";
    internal const string ParentFatherNameKey = "feralKinshipFatherName";
    internal const string BankedTalentPointsKey = "feralKinshipBankedTalentPoints";
    internal const string JuvenilePointsTransferredKey = "feralKinshipJuvenilePointsTransferred";
    internal const string ChildAdultEntityCodeKey = "feralKinshipChildAdultEntityCode";
    internal const string GrowthInProgressKey = "feralKinshipGrowthInProgress";
    internal const string BreedingEnabledKey = "feralKinshipBreedingEnabled";
    internal const string BondedPartnerIdKey = "feralKinshipBondedPartnerId";
    internal const string BondedPartnerNameKey = "feralKinshipBondedPartnerName";
    internal const string LastBreedingAttemptNightKey = "feralKinshipLastBreedingAttemptNight";
    internal const string BreedingFeedbackKey = "feralKinshipBreedingFeedback";
    internal const string PregnancyActiveKey = "feralKinshipPregnancyActive";
    internal const string PregnancyStartTotalHoursKey = "feralKinshipPregnancyStartTotalHours";
    internal const string PregnancyDueTotalHoursKey = "feralKinshipPregnancyDueTotalHours";
    internal const string PregnancyFatherIdKey = "feralKinshipPregnancyFatherId";
    internal const string PregnancyFatherNameKey = "feralKinshipPregnancyFatherName";
    internal const string PendingBirthKey = "feralKinshipPendingBirth";

    internal static bool IsCompanionJuvenile(Entity entity) =>
        GetDomesticationStatus(entity)?.GetBool(JuvenileKey, false) == true;

    internal static bool IsCompanionPregnant(Entity entity) =>
        GetDomesticationStatus(entity)?.GetBool(PregnancyActiveKey, false) == true;

    internal static float GetPregnancyMovementMultiplier(Entity entity)
    {
        float penalty = 1f - CompanionBreedingCatalog.PregnancyMovementMultiplier;
        if (GetFoxPackTalentRank(entity, "steady-mothers") > 0)
        {
            penalty *= 0.50f;
        }
        return 1f - penalty;
    }

    internal static float GetPregnancyExpeditionMultiplier(Entity entity)
    {
        float penalty = 1f - CompanionBreedingCatalog.PregnancyExpeditionMultiplier;
        if (GetFoxPackTalentRank(entity, "steady-mothers") > 0)
        {
            penalty *= 0.50f;
        }
        return 1f - penalty;
    }

    /// <summary>
    /// Repairs companions that reached an adult entity code through vanilla or
    /// a source-mod growth path without running our adult-growth handoff.
    /// Their persisted juvenile marker must not override the actual adult code.
    /// </summary>
    internal bool TryRepairStaleAdultCompanionState(Entity entity)
    {
        if (adultStateValidatedEntities.Contains(entity.EntityId))
        {
            return false;
        }

        ITreeAttribute? status = GetDomesticationStatus(entity);
        if (status == null
            || !CompanionBreedingCatalog.TryGetForAdult(entity, out _))
        {
            return false;
        }

        if (!status.GetBool(JuvenileKey, false))
        {
            adultStateValidatedEntities.Add(entity.EntityId);
            return false;
        }

        int banked = Math.Max(0, status.GetInt(BankedTalentPointsKey, 0));
        if (banked > 0)
        {
            status.SetInt(
                FoxPointsKey,
                Math.Max(0, status.GetInt(FoxPointsKey, 0)) + banked
            );
        }

        status.SetInt(BankedTalentPointsKey, 0);
        status.SetBool(JuvenileKey, false);
        status.SetBool(JuvenilePointsTransferredKey, true);
        status.SetBool(GrowthInProgressKey, false);
        status.SetString(BreedingFeedbackKey, "grown");
        adultStateValidatedEntities.Add(entity.EntityId);
        AwardAdulthoodExperienceIfNeeded(entity, status);
        MarkSocialStateDirty(entity);
        RegisterFoxInPack(entity);
        packRepository?.Save();
        ApplyFoxDerivedStats(entity);
        entity.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.EnsureAdultCompanionTasks();
        return true;
    }

    private void UpdateBreedingAndChildren(Entity entity)
    {
        if (RetainedNativeSuspension.Protected(entity)) return;
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        if (IsCompanionJuvenile(entity))
        {
            return;
        }

        if (!CompanionBreedingCatalog.TryGetForAdult(entity, out CompanionBreedingDefinition definition))
        {
            if (CompanionFotsaBreedingCatalog.IsFotsa(entity.Code))
                SetFotsaBreedingFeedback(entity, status, "source-unavailable");
            return;
        }

        if (definition.IsFotsa && ReconcileFotsaPregnancy(entity, definition)) return;

        if (status.GetBool(PregnancyActiveKey, false))
        {
            TryCompleteCompanionBirth(entity, status, definition);
            return;
        }

        if (!status.GetBool(BreedingEnabledKey, false)
            || (definition.IsFotsa && entity.WatchedAttributes.GetTreeAttribute("multiply")?.GetDouble("totalDaysCooldownUntil", 0) > entity.World.Calendar.TotalDays)
            || !CompanionBreedingCatalog.TryGetGender(entity, out string gender)
            || !string.Equals(gender, "female", StringComparison.Ordinal)
            || !string.Equals(GetCompanionActivityMode(entity), CompanionActivityMode.AtEase, StringComparison.Ordinal)
            || IsFoxAwayFromWorld(entity)
            || IsFoxIncapacitated(entity)
            || IsCompanionFoodRestricted(entity))
        {
            return;
        }

        double hour = entity.World.Calendar.HourOfDay;
        double hoursPerDay = entity.World.Calendar.HoursPerDay;
        bool nighttime = hour >= hoursPerDay * 0.75d || hour < hoursPerDay * 0.25d;
        if (!nighttime)
        {
            return;
        }

        long night = (long)Math.Floor(entity.World.Calendar.TotalHours / hoursPerDay
            - (hour < hoursPerDay * 0.25d ? 1d : 0d));
        if (status.GetLong(LastBreedingAttemptNightKey, long.MinValue) == night)
        {
            return;
        }

        long nowMs = entity.World.ElapsedMilliseconds;
        if (nextBreedingPartnerScanAtMs.TryGetValue(entity.EntityId, out long nextScanAtMs)
            && nowMs < nextScanAtMs)
        {
            return;
        }
        nextBreedingPartnerScanAtMs[entity.EntityId] = nowMs + BreedingPartnerScanIntervalMs;

        // Breeding is owner-present gameplay: do not start a pregnancy while
        // the owner is offline, even when this entity remains loaded nearby.
        if (!HasOnlineBreedingOwner(entity))
        {
            return;
        }

        Entity? partner = FindEligibleBreedingPartner(entity, definition);
        if (partner == null)
        {
            return;
        }

        ITreeAttribute partnerStatus = GetDomesticationStatus(partner, true)!;
        status.SetLong(LastBreedingAttemptNightKey, night);
        MarkSocialStateDirty(entity);
        double conceptionChance = CompanionBreedingCatalog.AttemptChance
            + GetFoxPackTalentRank(entity, "fertile-den") * 0.20d;
        bool conceived = entity.World.Rand.NextDouble() < Math.Min(1d, conceptionChance);
        if (!conceived)
        {
            status.SetString(BreedingFeedbackKey, "attempt-failed");
            partnerStatus.SetString(BreedingFeedbackKey, "attempt-failed");
            PersistBreedingEvent(entity, partner);
            return;
        }

        string motherId = status.GetString(FoxIdKey, string.Empty);
        string fatherId = partnerStatus.GetString(FoxIdKey, string.Empty);
        status.SetString(BondedPartnerIdKey, fatherId);
        status.SetString(BondedPartnerNameKey, GetFoxDisplayName(partner));
        partnerStatus.SetString(BondedPartnerIdKey, motherId);
        partnerStatus.SetString(BondedPartnerNameKey, GetFoxDisplayName(entity));
        status.SetBool(PregnancyActiveKey, true);
        status.SetDouble(PregnancyStartTotalHoursKey, entity.World.Calendar.TotalHours);
        double pregnancyDays = definition.PregnancyDays
            * (1d - GetFoxPackTalentRank(entity, "short-gestation") * 0.20d);
        status.SetDouble(PregnancyDueTotalHoursKey,
            entity.World.Calendar.TotalHours + pregnancyDays * hoursPerDay);
        status.SetString(PregnancyFatherIdKey, fatherId);
        status.SetString(PregnancyFatherNameKey, GetFoxDisplayName(partner));
        status.SetBool(PendingBirthKey, false);
        status.SetString(BreedingFeedbackKey, "pregnant");
        if (definition.IsFotsa) StartFotsaPregnancyMirror(entity, definition);
        partnerStatus.SetString(BreedingFeedbackKey, "partner-pregnant");
        MarkSocialStateDirty(entity);
        MarkSocialStateDirty(partner);
        PersistBreedingEvent(entity, partner);
        if (TryGetOwnerPlayer(entity, out IServerPlayer? pregnancyOwner))
            EmitDialogueEvent(entity, pregnancyOwner!, "family.pregnancy.start", string.Empty, CompanionDialoguePriority.High);
        SendStateToOwner(entity, Lang.Get("feralkinshipcompanions:breeding-conceived"));
    }

    private Entity? FindEligibleBreedingPartner(Entity mother, CompanionBreedingDefinition definition)
    {
        ITreeAttribute motherStatus = GetDomesticationStatus(mother, true)!;
        string ownerUid = motherStatus.GetString("owner", string.Empty);
        string motherId = motherStatus.GetString(FoxIdKey, string.Empty);
        string bondedId = motherStatus.GetString(BondedPartnerIdKey, string.Empty);
        if (string.IsNullOrWhiteSpace(ownerUid) || string.IsNullOrWhiteSpace(motherId))
        {
            return null;
        }

        foreach (Entity candidate in loadedFoxes.Values.OrderBy(value => value.EntityId))
        {
            if (candidate.EntityId == mother.EntityId
                || !candidate.Alive
                || IsCompanionJuvenile(candidate)
                || IsCompanionPregnant(candidate)
                || IsFoxAwayFromWorld(candidate)
                || IsFoxIncapacitated(candidate)
                || IsCompanionFoodRestricted(candidate)
                || !CompanionBreedingCatalog.TryGetForAdult(candidate, out CompanionBreedingDefinition candidateDefinition)
                || !definition.CanMate(mother, candidate, candidateDefinition)
                || !CompanionBreedingCatalog.TryGetGender(candidate, out string candidateGender)
                || !string.Equals(candidateGender, "male", StringComparison.Ordinal)
                || !string.Equals(GetCompanionActivityMode(candidate), CompanionActivityMode.AtEase, StringComparison.Ordinal))
            {
                continue;
            }

            ITreeAttribute? candidateStatus = GetDomesticationStatus(candidate);
            string candidateId = candidateStatus?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
            string candidateBondedId = candidateStatus?.GetString(BondedPartnerIdKey, string.Empty) ?? string.Empty;
            if (candidateStatus?.GetBool(BreedingEnabledKey, false) != true
                || !string.Equals(candidateStatus.GetString("owner", string.Empty), ownerUid, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(candidateId)
                || (!string.IsNullOrWhiteSpace(bondedId) && !string.Equals(bondedId, candidateId, StringComparison.Ordinal))
                || (!string.IsNullOrWhiteSpace(candidateBondedId) && !string.Equals(candidateBondedId, motherId, StringComparison.Ordinal))
                || !AssignedBedsShareEnclosedRoom(mother, candidate))
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private bool AssignedBedsShareEnclosedRoom(Entity first, Entity second)
    {
        if (packRepository?.Loaded != true || roomRegistry == null)
        {
            return false;
        }

        string firstId = GetDomesticationStatus(first)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        string secondId = GetDomesticationStatus(second)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        FoxBedRecord? firstBed = packRepository.GetBedForFox(firstId);
        FoxBedRecord? secondBed = packRepository.GetBedForFox(secondId);
        if (firstBed == null || secondBed == null || firstBed.Dimension != secondBed.Dimension)
        {
            return false;
        }

        BlockPos firstPos = new(firstBed.X, firstBed.Y, firstBed.Z, firstBed.Dimension);
        BlockPos secondPos = new(secondBed.X, secondBed.Y, secondBed.Z, secondBed.Dimension);
        Room? firstRoom = roomRegistry.GetRoomForPosition(firstPos);
        Room? secondRoom = roomRegistry.GetRoomForPosition(secondPos);
        return firstRoom != null
            && secondRoom != null
            && firstRoom.ExitCount == 0
            && secondRoom.ExitCount == 0
            && firstRoom.Contains(secondPos)
            && secondRoom.Contains(firstPos);
    }

    private void TryCompleteCompanionBirth(
        Entity mother,
        ITreeAttribute motherStatus,
        CompanionBreedingDefinition definition)
    {
        if (serverApi == null
            || mother.World.Calendar.TotalHours < motherStatus.GetDouble(PregnancyDueTotalHoursKey, double.MaxValue))
        {
            return;
        }

        // Keep an overdue pregnancy intact while its owner is away. Because
        // this update runs continuously for loaded companions, the birth can
        // complete on the first eligible update after the owner returns.
        if (!HasOnlineBreedingOwner(mother))
        {
            return;
        }

        if (IsFoxAwayFromWorld(mother) || IsFoxIncapacitated(mother))
        {
            motherStatus.SetBool(PendingBirthKey, true);
            motherStatus.SetString(BreedingFeedbackKey, "birth-waiting");
            MarkSocialStateDirty(mother);
            return;
        }

        long nowMs = mother.World.ElapsedMilliseconds;
        if (nextBirthRetryAtMsByEntity.TryGetValue(mother.EntityId, out long nextRetryAtMs)
            && nowMs < nextRetryAtMs)
        {
            return;
        }

        string typeId = string.Empty;
        if (!definition.IsFotsa && (!CompanionBreedingCatalog.TryGetType(mother, out typeId)
            || !CompanionBreedingCatalog.TryGetSpeciesProfile(definition.SpeciesId, out CompanionSpeciesProfile profile)
            || !CompanionBreedingCatalog.IsKnownType(profile, typeId)))
        {
            motherStatus.SetBool(PendingBirthKey, true);
            motherStatus.SetString(BreedingFeedbackKey, "birth-invalid-type");
            MarkSocialStateDirty(mother);
            return;
        }

        CompanionFotsaOffspring[] offspringOptions = definition.FotsaOffspring?.ToArray()
            ?? new[] { "female", "male" }.Select(gender => new CompanionFotsaOffspring(
                definition.BuildChildCode(typeId, gender), definition.BuildAdultCode(typeId, gender))).ToArray();
        foreach (CompanionFotsaOffspring offspring in offspringOptions)
        {
            if (serverApi.World.GetEntityType(new AssetLocation(offspring.ChildCode)) == null
                || serverApi.World.GetEntityType(new AssetLocation(offspring.AdultCode)) == null)
            {
                motherStatus.SetBool(PendingBirthKey, true);
                motherStatus.SetString(BreedingFeedbackKey, "birth-waiting");
                MarkSocialStateDirty(mother);
                return;
            }
        }

        int litterSize = definition.RollLitterSize(mother.World.Rand);
        motherStatus.SetBool(PendingBirthKey, true);
        List<Entity> stagedChildren = new(litterSize);
        List<Vec3d> reservedPositions = new(litterSize);
        int spawnedCount = 0;
        try
        {
            for (int litterIndex = 0; litterIndex < litterSize; litterIndex++)
            {
                CompanionFotsaOffspring offspring = offspringOptions[mother.World.Rand.Next(offspringOptions.Length)];
                string childCode = offspring.ChildCode;
                string adultCode = offspring.AdultCode;
                EntityProperties? childType = serverApi.World.GetEntityType(new AssetLocation(childCode));
                EntityProperties? adultType = serverApi.World.GetEntityType(new AssetLocation(adultCode));
                Vec3d? safe = childType == null
                    ? null
                    : FindSafeCompanionBirthPosition(mother, childType, reservedPositions);
                if (childType == null || adultType == null || safe == null)
                {
                    nextBirthRetryAtMsByEntity[mother.EntityId] = nowMs + BirthSafePositionRetryIntervalMs;
                    motherStatus.SetString(BreedingFeedbackKey, "birth-waiting");
                    MarkSocialStateDirty(mother);
                    return;
                }

                Entity? child = serverApi.World.ClassRegistry.CreateEntity(childType);
                if (child == null)
                {
                    nextBirthRetryAtMsByEntity[mother.EntityId] = nowMs + BirthSafePositionRetryIntervalMs;
                    motherStatus.SetString(BreedingFeedbackKey, "birth-waiting");
                    MarkSocialStateDirty(mother);
                    return;
                }

                child.Pos.SetPosWithDimension(safe);
                child.Pos.Yaw = mother.Pos.Yaw;
                child.PositionBeforeFalling.Set(safe.X, safe.Y, safe.Z);
                ITreeAttribute childStatus = GetDomesticationStatus(child, true)!;
                childStatus.SetString("owner", motherStatus.GetString("owner", string.Empty));
                childStatus.SetString("domesticationLevel", "DOMESTICATED");
                childStatus.SetFloat("obedience", 1f);
                childStatus.SetBool(JuvenileKey, true);
                childStatus.SetString(JuvenileSpeciesKey, definition.SpeciesId);
                childStatus.SetString(ParentMotherIdKey, motherStatus.GetString(FoxIdKey, string.Empty));
                childStatus.SetString(ParentMotherNameKey, GetFoxDisplayName(mother));
                childStatus.SetString(ParentFatherIdKey, motherStatus.GetString(PregnancyFatherIdKey, string.Empty));
                childStatus.SetString(ParentFatherNameKey, motherStatus.GetString(PregnancyFatherNameKey, string.Empty));
                childStatus.SetInt(BankedTalentPointsKey, 0);
                childStatus.SetBool(JuvenilePointsTransferredKey, false);
                childStatus.SetString(ChildAdultEntityCodeKey, adultCode);
                childStatus.SetString(ActivityModeKey, CompanionActivityMode.AtEase);
                childStatus.SetString(CombatStyleKey, CompanionCombatStyle.Passive);
                child.WatchedAttributes.SetLong("motherId", mother.EntityId);
                child.WatchedAttributes.SetInt("generation", mother.WatchedAttributes.GetInt("generation", 0) + 1);
                stagedChildren.Add(child);
                reservedPositions.Add(safe);
            }

            foreach (Entity child in stagedChildren)
            {
                try
                {
                    serverApi.World.SpawnEntity(child);
                }
                catch
                {
                    if (child.EntityId > 0 && serverApi.World.GetEntityById(child.EntityId) != null)
                    {
                        spawnedCount++;
                    }
                    throw;
                }

                spawnedCount++;
                RegisterFoxInPack(child);
            }

            nextBirthRetryAtMsByEntity.Remove(mother.EntityId);
            if (definition.IsFotsa) CompleteFotsaPregnancyMirror(mother, definition);
            motherStatus.SetBool(PregnancyActiveKey, false);
            motherStatus.SetBool(PendingBirthKey, false);
            motherStatus.SetDouble(PregnancyStartTotalHoursKey, 0d);
            motherStatus.SetDouble(PregnancyDueTotalHoursKey, 0d);
            motherStatus.SetString(BreedingFeedbackKey, "birth-success");
            MarkSocialStateDirty(mother);
            RegisterFoxInPack(mother);
            packRepository?.Save();
            if (TryGetOwnerPlayer(mother, out IServerPlayer? birthOwner))
                EmitDialogueEvent(mother, birthOwner!, "family.birth", string.Empty, CompanionDialoguePriority.High);
            SendStateToOwner(mother, Lang.Get("feralkinshipcompanions:breeding-birth-success"));
            SendPackStateToOwner(mother);
        }
        catch (Exception exception)
        {
            if (spawnedCount > 0)
            {
                // Do not retry the whole litter after a partial spawn: doing so
                // could duplicate already-created pups on every later update.
                nextBirthRetryAtMsByEntity.Remove(mother.EntityId);
                if (definition.IsFotsa) CompleteFotsaPregnancyMirror(mother, definition);
                motherStatus.SetBool(PregnancyActiveKey, false);
                motherStatus.SetBool(PendingBirthKey, false);
                motherStatus.SetDouble(PregnancyStartTotalHoursKey, 0d);
                motherStatus.SetDouble(PregnancyDueTotalHoursKey, 0d);
                motherStatus.SetString(BreedingFeedbackKey, "birth-interrupted");
                try
                {
                    PersistBreedingEvent(mother);
                }
                catch (Exception persistException)
                {
                    serverApi.Logger.Error(
                        "[FeralKinshipCompanions] Could not persist interrupted birth state for mother {0}: {1}",
                        mother.EntityId,
                        persistException);
                }
            }
            else
            {
                nextBirthRetryAtMsByEntity[mother.EntityId] = nowMs + BirthSafePositionRetryIntervalMs;
                motherStatus.SetString(BreedingFeedbackKey, "birth-waiting");
                MarkSocialStateDirty(mother);
            }

            serverApi.Logger.Error(
                "[FeralKinshipCompanions] Safe companion birth failed after {0} of {1} pups spawned: {2}",
                spawnedCount,
                litterSize,
                exception);
        }
    }

    private bool HasOnlineBreedingOwner(Entity entity)
    {
        if (serverApi == null)
        {
            return false;
        }

        string ownerUid = GetCompanionOwnerUid(entity);
        return !string.IsNullOrWhiteSpace(ownerUid)
            && IsOnlineServerPlayer(serverApi.World.PlayerByUid(ownerUid));
    }

    private void PersistBreedingEvent(params Entity[] entities)
    {
        foreach (Entity entity in entities)
        {
            MarkSocialStateDirty(entity);
            RegisterFoxInPack(entity);
        }
        packRepository?.Save();
    }

    private bool TryGetPregnancyDepartureRefusal(
        System.Collections.Generic.IEnumerable<string> party,
        double plannedReturnTotalHours,
        out string refusal)
    {
        refusal = string.Empty;
        if (serverApi == null || packRepository == null)
        {
            return false;
        }

        foreach (string foxId in party)
        {
            if (!packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
                || record?.PregnancyActive != true)
            {
                continue;
            }

            Entity? entity = record.EntityId > 0 ? serverApi.World.GetEntityById(record.EntityId) : null;
            double due = entity == null
                ? record.PregnancyDueTotalHours
                : GetDomesticationStatus(entity)?.GetDouble(PregnancyDueTotalHoursKey, record.PregnancyDueTotalHours)
                    ?? record.PregnancyDueTotalHours;
            if (due > 0d && due <= plannedReturnTotalHours)
            {
                refusal = Lang.Get(
                    "feralkinshipcompanions:pregnancy-departure-refusal",
                    GetFoxRecordLabel(record));
                return true;
            }
        }

        return false;
    }

    private void ToggleCompanionBreeding(Entity entity, Vintagestory.API.Server.IServerPlayer owner)
    {
        if (IsCompanionJuvenile(entity)
            || !CompanionBreedingCatalog.TryGetForAdult(entity, out _))
        {
            SendState(entity, owner, CompanionFotsaBreedingCatalog.IsFotsa(entity.Code)
                ? Lang.Get("feralkinshipcompanions:breeding-source-unavailable", CompanionFotsaBreedingCatalog.UnavailableReason(entity.Code))
                : Lang.Get("feralkinshipcompanions:breeding-unsupported"));
            return;
        }

        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        bool enabled = !status.GetBool(BreedingEnabledKey, false);
        status.SetBool(BreedingEnabledKey, enabled);
        status.SetString(BreedingFeedbackKey, enabled ? "enabled" : "disabled");
        PersistBreedingEvent(entity);
        EmitDialogueEvent(entity, owner, enabled ? "family.breeding.enable" : "family.breeding.disable",
            string.Empty, CompanionDialoguePriority.Medium);
        SendState(entity, owner, Lang.Get(enabled
            ? "feralkinshipcompanions:breeding-enabled"
            : "feralkinshipcompanions:breeding-disabled"));
    }

    internal void TryPrepareCompanionAdult(Entity child, ref Entity adult)
    {
        ITreeAttribute? childStatus = GetDomesticationStatus(child);
        if (serverApi == null
            || childStatus?.GetBool(JuvenileKey, false) != true
            || childStatus.GetBool(JuvenilePointsTransferredKey, false))
        {
            return;
        }

        string adultCode = childStatus.GetString(ChildAdultEntityCodeKey, string.Empty);
        EntityProperties? adultType = string.IsNullOrWhiteSpace(adultCode)
            ? null
            : serverApi.World.GetEntityType(new AssetLocation(adultCode));
        Entity? replacement = adultType == null ? null : serverApi.World.ClassRegistry.CreateEntity(adultType);
        if (replacement == null)
        {
            serverApi.Logger.Error("[FeralKinshipCompanions] Child {0} could not resolve persisted adult type {1}; vanilla growth was left unchanged.", child.EntityId, adultCode);
            return;
        }

        replacement.Pos.SetFrom(adult.Pos);
        replacement.PositionBeforeFalling.Set(replacement.Pos.X, replacement.Pos.Y, replacement.Pos.Z);
        replacement.WatchedAttributes[DomesticationStatusPath] = childStatus.Clone();
        ITreeAttribute replacementStatus = GetDomesticationStatus(replacement, true)!;
        int banked = Math.Max(0, replacementStatus.GetInt(BankedTalentPointsKey, 0));
        replacementStatus.SetInt(FoxPointsKey, Math.Max(0, replacementStatus.GetInt(FoxPointsKey, 0)) + banked);
        replacementStatus.SetInt(BankedTalentPointsKey, 0);
        replacementStatus.SetBool(JuvenileKey, false);
        replacementStatus.SetBool(JuvenilePointsTransferredKey, true);
        replacementStatus.SetBool(GrowthInProgressKey, false);
        replacementStatus.SetString(BreedingFeedbackKey, "grown");
        if (child.WatchedAttributes.GetTreeAttribute("nametag") is ITreeAttribute nameTag)
        {
            replacement.WatchedAttributes.SetAttribute("nametag", nameTag.Clone());
            replacement.WatchedAttributes.MarkPathDirty("nametag");
        }

        childStatus.SetInt(FoxPointsKey, replacementStatus.GetInt(FoxPointsKey, 0));
        childStatus.SetInt(BankedTalentPointsKey, 0);
        childStatus.SetBool(JuvenileKey, false);
        childStatus.SetBool(JuvenilePointsTransferredKey, true);
        childStatus.SetBool(GrowthInProgressKey, true);

        // EntityBehaviorGrow calls this Harmony prefix before the replacement
        // has been spawned/initialized.  GetBehavior (including the health
        // lookup inside ApplyFoxDerivedStats) is not safe at this point because
        // the new entity does not yet have initialized sided properties.
        // Finish behavior and stat setup in the postfix, after SpawnEntity.
        RegisterFoxInPack(child);
        packRepository?.Save();
        nativeAdultGrowthHandoffs.Add(replacement, nativeAdultGrowthHandoffMarker);
        adult = replacement;
    }

    internal void FinalizeCompanionAdultGrowth(Entity adult)
    {
        if (serverApi == null || !IsTamedFox(adult))
        {
            return;
        }

        ITreeAttribute? status = GetDomesticationStatus(adult);
        string expectedAdultCode = status?.GetString(ChildAdultEntityCodeKey, string.Empty) ?? string.Empty;
        if (status == null
            || status.GetBool(JuvenileKey, true)
            || !string.Equals(expectedAdultCode, adult.Code?.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        status.SetBool(GrowthInProgressKey, false);
        status.SetBool(JuvenilePointsTransferredKey, true);
        if (nativeAdultGrowthHandoffs.TryGetValue(adult, out _))
        {
            // If the repository was not loaded during spawn, a later first
            // registration must persist this living adult, not hydrate an old
            // child record. Normal unload/reload still resets this runtime set.
            ledgerHydratedEntities.Add(adult.EntityId);
        }
        adult.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.EnsureAdultCompanionTasks();
        string preservedName = adult.WatchedAttributes.GetTreeAttribute("nametag")?.GetString("name", string.Empty)
            ?? string.Empty;
        RegisterLoadedFox(adult);
        if (!string.IsNullOrWhiteSpace(preservedName))
        {
            SetCompanionName(adult, preservedName);
        }
        ApplyFoxDerivedStats(adult);
        AwardAdulthoodExperienceIfNeeded(adult, status);
        packRepository?.Save();
        MarkSocialStateDirty(adult);
        if (TryGetOwnerPlayer(adult, out IServerPlayer? growthOwner))
            EmitDialogueEvent(adult, growthOwner!, "family.child_adult", string.Empty, CompanionDialoguePriority.High);
    }

    private static string GetBreedingFeedbackText(string feedbackId) => feedbackId switch
    {
        "enabled" => Lang.Get("feralkinshipcompanions:breeding-feedback-enabled"),
        "disabled" => Lang.Get("feralkinshipcompanions:breeding-feedback-disabled"),
        "attempt-failed" => Lang.Get("feralkinshipcompanions:breeding-feedback-attempt-failed"),
        "pregnant" => Lang.Get("feralkinshipcompanions:breeding-feedback-pregnant"),
        "partner-pregnant" => Lang.Get("feralkinshipcompanions:breeding-feedback-partner-pregnant"),
        "birth-waiting" => Lang.Get("feralkinshipcompanions:breeding-feedback-birth-waiting"),
        "birth-invalid-type" => Lang.Get("feralkinshipcompanions:breeding-feedback-invalid-type"),
        "birth-interrupted" => Lang.Get("feralkinshipcompanions:breeding-feedback-birth-interrupted"),
        "birth-success" => Lang.Get("feralkinshipcompanions:breeding-feedback-birth-success"),
        "grown" => Lang.Get("feralkinshipcompanions:breeding-feedback-grown"),
        "source-unavailable" => Lang.Get("feralkinshipcompanions:breeding-feedback-source-unavailable"),
        "native-pregnant" => Lang.Get("feralkinshipcompanions:breeding-feedback-native-pregnant"),
        "pregnancy-conflict" => Lang.Get("feralkinshipcompanions:breeding-feedback-pregnancy-conflict"),
        _ => string.Empty
    };
}
