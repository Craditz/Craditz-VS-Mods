using System;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VintageStoryConfigMigration;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private void SendPackState(IServerPlayer owner, string message = "")
    {
        pendingPackStateOwners.Remove(owner.PlayerUID);
        RefreshLoadedFoxPackRecords();
        TryResolvePendingSafeReturns(owner.PlayerUID);
        IReadOnlyList<FoxPackRecordV2> records = packRepository?.GetRecordsForOwner(owner.PlayerUID)
            ?? Array.Empty<FoxPackRecordV2>();
        IReadOnlyList<FoxPackRecordV2> archivedRecords = packRepository?.GetArchivedRecordsForOwner(owner.PlayerUID)
            ?? Array.Empty<FoxPackRecordV2>();
        double currentDay = owner.Entity?.World.Calendar.TotalDays ?? 0d;
        bool changedTransientStatuses = false;
        foreach (FoxPackRecordV2 record in records)
        {
            if (!IsTransientExpeditionStatus(record.Status))
            {
                continue;
            }

            if (record.StatusExpiresDay <= 0d)
            {
                // Give statuses from an older save a normal one-day display
                // window instead of letting them remain forever.
                record.StatusExpiresDay = currentDay + TransientExpeditionStatusDisplayDays;
                changedTransientStatuses = true;
            }
            else if (currentDay >= record.StatusExpiresDay)
            {
                record.Status = "Present";
                record.StatusExpiresDay = 0d;
                changedTransientStatuses = true;
            }
        }
        if (changedTransientStatuses)
        {
            packRepository?.Save();
        }

        List<FoxPackMemberPacket> members = records.Select(record => BuildPackMemberPacket(record, currentDay)).ToList();
        List<FoxPackMemberPacket> archivedMembers = archivedRecords
            .Select(record => BuildPackMemberPacket(record, currentDay))
            .ToList();

        IReadOnlyList<FoxExpeditionRecord> expeditions = packRepository?.GetExpeditions(owner.PlayerUID)
            ?? Array.Empty<FoxExpeditionRecord>();
        FoxExpeditionRecord? expedition = expeditions.FirstOrDefault();
        float expeditionRemainingSeconds = expedition == null
            ? 0f
            : expedition.CompletesTotalHours > 0d
                ? 0f
                : Math.Max(0f, (expedition.CompletesUtcMs - UtcNowMs()) / 1000f);
        float expeditionProgressPercent = expedition == null || expedition.TargetStrength <= 0f
            ? 0f
            : Math.Clamp(expedition.ExpeditionStrength / expedition.TargetStrength * 100f, 0f, 100f);

        FoxRecruitmentRewardRecord? pendingRecruitment = packRepository?.GetPendingRecruitment(owner.PlayerUID);
        int pendingRecruitmentCount = packRepository?.GetPendingRecruitmentCount(owner.PlayerUID) ?? 0;
        string recruitmentType = string.Empty;
        string recruitmentSpeciesId = string.Empty;
        if (pendingRecruitment != null)
        {
            recruitmentSpeciesId = string.IsNullOrWhiteSpace(pendingRecruitment.SpeciesId)
                ? "fox"
                : pendingRecruitment.SpeciesId;
            if (CompanionSpeciesCatalog.TryGetRecruitmentVariant(
                    pendingRecruitment.WildEntityCode,
                    out CompanionSpeciesProfile rewardSpecies,
                    out CompanionRecruitmentVariant rewardVariant))
            {
                recruitmentType = GetRecruitmentDisplayName(rewardSpecies, rewardVariant);
            }
            else
            {
                recruitmentType = "Companion";
            }
        }

        serverChannel?.SendPacket(new FoxPackStatePacket
        {
            PackSize = members.Count,
            PackPoints = GetPackPoints(owner.PlayerUID),
            Members = members,
            ArchivedMembers = archivedMembers,
            LootItems = GetPackLootItems(owner.PlayerUID),
            RecruitmentReady = pendingRecruitment != null,
            RecruitmentType = recruitmentType,
            RecruitmentSpeciesId = recruitmentSpeciesId,
            Message = message,
            ExpeditionType = expedition?.Type ?? string.Empty,
            ExpeditionRemainingSeconds = expeditionRemainingSeconds,
            ExpeditionSelectedFoxIds = expedition?.SelectedFoxIds?.ToList() ?? new List<string>(),
            ExpeditionStrength = expedition?.ExpeditionStrength ?? 0f,
            ExpeditionTargetStrength = expedition?.TargetStrength ?? 0f,
            ExpeditionProgressPercent = expeditionProgressPercent,
            ExpeditionRecruitmentChanceBonus = expedition?.RecruitmentChanceBonus ?? 0f,
            ExpeditionTargetFoxId = expedition?.TargetFoxId ?? string.Empty,
            UnlockedExpeditionTypes = packRepository?.GetUnlockedExpeditionTypes(owner.PlayerUID).ToList()
                ?? new List<string>(),
            UnlockedPackTalents = packRepository?.GetUnlockedPackTalents(owner.PlayerUID).ToList()
                ?? new List<string>(),
            PatrolPrepared = packRepository?.IsPatrolPrepared(owner.PlayerUID) == true,
            ExpeditionPrepared = expedition?.Prepared == true,
            ExpeditionPreparationRiskReduction = expedition?.PreparationRiskReduction ?? 0f,
            ExpeditionPatrolRiskReduction = expedition?.PatrolRiskReduction ?? 0f,
            ExpeditionCompletesTotalHours = expedition?.CompletesTotalHours ?? 0d,
            ExpeditionBaseDurationHours = expedition?.BaseDurationHours ?? 0f,
            ExpeditionLateDurationHours = expedition?.LateDurationHours ?? 0f,
            LastExpedition = packRepository?.GetLastExpeditionReport(owner.PlayerUID),
            CargoUnloadingActive = packRepository?.IsCargoUnloadingActive(owner.PlayerUID) == true,
            PermanentRangeRank = packRepository?.GetPackTalentRank(owner.PlayerUID, "far-reaching-pack") ?? 0,
            ActiveExpeditions = expeditions.Select(active => new FoxActiveExpeditionPacket
            {
                ExpeditionId = active.ExpeditionId,
                Type = active.Type,
                RemainingSeconds = active.CompletesTotalHours > 0d
                    ? 0f
                    : Math.Max(0f, (active.CompletesUtcMs - UtcNowMs()) / 1000f),
                SelectedFoxIds = active.SelectedFoxIds?.ToList() ?? new List<string>(),
                Strength = active.ExpeditionStrength,
                TargetStrength = active.TargetStrength,
                ProgressPercent = active.TargetStrength <= 0f
                    ? 0f
                    : Math.Clamp(active.ExpeditionStrength / active.TargetStrength * 100f, 0f, 100f),
                RecruitmentChanceBonus = active.RecruitmentChanceBonus,
                TargetFoxId = active.TargetFoxId,
                Prepared = active.Prepared,
                PreparationRiskReduction = active.PreparationRiskReduction,
                PatrolRiskReduction = active.PatrolRiskReduction,
                CompletesTotalHours = active.CompletesTotalHours,
                BaseDurationHours = active.BaseDurationHours,
                LateDurationHours = active.LateDurationHours,
                RunningLate = active.RunningLate,
                RawCompletion = ExpeditionCalculations.GetRawCompletion(active.ExpeditionStrength, active.TargetStrength),
                OvercapRewardFactor = active.Story?.OvercapRewardFactor
                    ?? ExpeditionCalculations.GetOvercapRewardFactor(
                        ExpeditionCalculations.GetRawCompletion(active.ExpeditionStrength, active.TargetStrength))
            }).ToList(),
            ExpeditionReports = packRepository?.GetExpeditionReports(owner.PlayerUID).ToList()
                ?? new List<FoxExpeditionSummaryPacket>(),
            PendingRecruitmentCount = pendingRecruitmentCount,
            ScavengeSites = BuildScavengeSitePackets(owner.PlayerUID),
            PermanentExpeditionCapacityRank = packRepository?.GetPackTalentRank(owner.PlayerUID, "many-trails") ?? 0,
            ExpeditionCapacity = packRepository?.GetExpeditionCapacity(owner.PlayerUID)
                ?? FoxPackRepository.BaseExpeditionCapacity
        }, owner);
    }

    private void TryResolvePendingSafeReturns(string ownerUid)
    {
        if (serverApi == null || packRepository == null)
        {
            return;
        }

        long nowUtcMs = UtcNowMs();
        if (TryProcessPendingExpeditionDepartures(nowUtcMs)
            | TryProcessPendingExpeditionReturns(nowUtcMs))
        {
            packRepository.Save();
        }
    }

    private FoxPackMemberPacket BuildPackMemberPacket(FoxPackRecordV2 record, double currentDay)
    {
        Entity? entity = record.EntityId > 0 ? serverApi?.World.GetEntityById(record.EntityId) : null;
        GetBackpackLedgerDisplay(
            entity,
            record,
            out bool backpackEquipped,
            out string backpackName,
            out int backpackOccupiedSlots,
            out int backpackTotalSlots);
        float conditionStrength = record.MaxHealth > 0f
            ? 0.50f + Math.Clamp(record.CurrentHealth / record.MaxHealth, 0f, 1f) * 0.50f
            : 0.50f;
        float healthStrength = Math.Clamp((record.MaxHealth - FoxBaseMaxHealth) * 0.02f, 0f, 0.50f);
        float movementStrength = 0f;
        float coreStrength = conditionStrength + healthStrength;
        if (entity != null)
        {
            coreStrength = GetFoxExpeditionCoreStrength(
                entity,
                record.CurrentHealth,
                record.MaxHealth,
                out conditionStrength,
                out healthStrength,
                out movementStrength
            );
        }
        return new FoxPackMemberPacket
        {
            FoxId = record.FoxId,
            EntityId = entity?.EntityId ?? 0,
            AtCart = entity != null && TryGetPackCartIdleTarget(entity, out Vec3d? cartTarget)
                && cartTarget != null && entity.Pos.SquareDistanceTo(cartTarget) <= 64d,
            Number = record.Number,
            Name = record.Name,
            Status = record.Status,
            CurrentHealth = record.CurrentHealth,
            MaxHealth = record.MaxHealth,
            Personality = GetPersonalityLabel(record.Personality),
            Mood = record.Mood,
            RequestsGenerated = record.RequestsGenerated,
            RequestsCompleted = record.RequestsCompleted,
            Points = record.Points,
            LifetimePoints = Math.Max(record.LifetimePoints, record.Points),
            ActiveRequest = record.ActiveRequest,
            LastCompleted = record.LastCompleted,
            HasLastKnownPosition = record.HasLastKnownPosition
                && (string.Equals(record.Status, "Present", StringComparison.OrdinalIgnoreCase)
                    || record.LastKnownPositionExpiresDay <= 0d
                    || currentDay <= record.LastKnownPositionExpiresDay),
            LastKnownX = record.LastKnownX,
            LastKnownY = record.LastKnownY,
            LastKnownZ = record.LastKnownZ,
            ExpeditionStrengthBonus = entity == null ? 0f : GetFoxExpeditionStrengthBonus(entity),
            SpeciesTraining = GetSpeciesTraining(entity, record.SpeciesId),
            ExpeditionSpeedBonus = entity == null ? 0f : GetFoxExpeditionSpeedBonus(entity),
            RecruitmentChanceBonus = entity == null ? 0f : GetFoxRecruitmentChanceBonus(entity),
            ExpeditionInjuryRiskReduction = entity == null ? 0f : GetFoxExpeditionInjuryRiskReduction(entity),
            ExpeditionCoreStrength = coreStrength,
            ExpeditionConditionStrength = conditionStrength,
            ExpeditionHealthStrength = healthStrength,
            ExpeditionMovementStrength = movementStrength,
            CarriedItem = entity == null ? string.Empty : GetFoxStorageCargoDisplay(entity),
            WaitingForCartCargo = entity != null && HasFoxPendingCartPickup(entity),
            SpeciesId = string.IsNullOrWhiteSpace(record.SpeciesId) ? "fox" : record.SpeciesId,
            SpeciesDisplayName = GetCompanionSpeciesDisplayName(record),
            AppearanceCode = entity?.Code?.ToShortString() ?? record.EntityCode,
            IsJuvenile = record.IsJuvenile,
            PregnancyActive = record.PregnancyActive,
            ExpeditionStrengthFactor = record.PregnancyActive
                ? entity == null
                    ? CompanionBreedingCatalog.PregnancyExpeditionMultiplier
                    : GetPregnancyExpeditionMultiplier(entity)
                : 1f,
            EntityLoaded = entity != null && entity.Alive && IsTamedFox(entity),
            RescueRecoverable = IsRescueTarget(record),
            Level = Math.Max(CompanionProgressionRules.StartingLevel, record.Level),
            CurrentLevelExperience = Math.Max(0L, record.CurrentLevelExperience),
            RequiredLevelExperience = CompanionProgressionRules.GetRequiredExperience(record.Level),
            LifetimeExperience = Math.Max(record.CurrentLevelExperience, record.LifetimeExperience),
            BackpackEquipped = backpackEquipped,
            BackpackName = backpackName,
            BackpackOccupiedSlots = backpackOccupiedSlots,
            BackpackTotalSlots = backpackTotalSlots,
            BackpackDeliveryActive = record.BackpackDeliveryActive,
            BackpackDeliveryPhase = record.BackpackDeliveryPhase,
            BackpackReturnScheduled = record.BackpackReturnDueUtcMs > 0,
            BackpackReturnRemainingSeconds = record.BackpackDeliveryActive
                && string.Equals(record.BackpackDeliveryPhase, "wait-return", StringComparison.Ordinal)
                && record.BackpackReturnDueUtcMs > 0
                    ? Math.Max(0f, (record.BackpackReturnDueUtcMs - UtcNowMs()) / 1000f)
                    : 0f,
            BackpackLastDeliverySummary = record.BackpackLastDeliverySummary
        };
    }

    private void ArchivePackFox(IServerPlayer owner, string foxId)
    {
        string ownerUid = owner.PlayerUID;
        if (packRepository == null || !packRepository.ArchiveRecord(ownerUid, foxId))
        {
            SendPackState(owner, "Only unavailable companions can be archived; expedition members must return first.");
            return;
        }

        packRepository.Save();
        SendPackState(owner, "Companion archived. Its history remains in Archived.");
    }

    private void UnarchivePackFox(IServerPlayer owner, string foxId)
    {
        if (serverApi == null || packRepository?.Loaded != true
            || !packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
            || record == null
            || !string.Equals(record.OwnerUid, owner.PlayerUID, StringComparison.Ordinal)
            || !record.Archived)
        {
            SendPackState(owner, "That companion is not available to restore.");
            return;
        }

        Entity? entity = record.EntityId > 0 ? serverApi.World.GetEntityById(record.EntityId) : null;
        if (entity == null || !entity.Alive || !IsTamedFox(entity) || !IsOwner(entity, owner.PlayerUID))
        {
            SendPackState(owner, "A companion can only be restored after it is found in the world.");
            return;
        }

        if (!packRepository.UnarchiveRecord(owner.PlayerUID, foxId))
        {
            SendPackState(owner, "That companion could not be restored.");
            return;
        }

        packRepository.Save();
        SendPackState(owner, $"Restored {GetFoxRecordLabel(record)} to the active pack.");
    }

    private void DeleteExpeditionReport(IServerPlayer owner, long expeditionId)
    {
        if (packRepository?.Loaded != true
            || !packRepository.RemoveExpeditionReport(owner.PlayerUID, expeditionId))
        {
            SendPackState(owner, "That expedition report no longer exists.");
            return;
        }

        packRepository.Save();
        SendPackState(owner, $"Deleted expedition report #{expeditionId}. Expedition cargo and active parties were not changed.");
    }

    private static string GetFoxDisplayName(Entity entity)
    {
        string customName = entity.WatchedAttributes.GetTreeAttribute("nametag")?.GetString("name") ?? string.Empty;
        return string.IsNullOrWhiteSpace(customName) ? entity.GetName() : customName;
    }

    private static void GetHealth(Entity entity, out float currentHealth, out float maxHealth)
    {
        ITreeAttribute? health = entity.WatchedAttributes.GetTreeAttribute("health");
        maxHealth = health?.GetFloat("maxhealth", 0f) ?? 0f;
        currentHealth = health?.GetFloat("currenthealth", maxHealth) ?? maxHealth;
    }

    private void SendStateToOwner(Entity entity, string message)
    {
        string ownerId = GetDomesticationStatus(entity)?.GetString("owner") ?? string.Empty;
        if (socialViewByOwner.TryGetValue(ownerId, out long viewedEntityId)
            && viewedEntityId == entity.EntityId
            && serverApi?.World.PlayerByUid(ownerId) is IServerPlayer owner)
        {
            SendState(entity, owner, message);
        }
    }

    private void SendPackStateToOwner(Entity entity)
    {
        string ownerId = GetDomesticationStatus(entity)?.GetString("owner") ?? string.Empty;
        if (packViewers.Contains(ownerId)
            && serverApi?.World.PlayerByUid(ownerId) is IServerPlayer owner)
        {
            pendingPackStateOwners.Add(owner.PlayerUID);
        }
    }

    private readonly HashSet<string> pendingPackStateOwners = new(StringComparer.Ordinal);

    private void FlushPendingPackStates()
    {
        string[] owners = pendingPackStateOwners.ToArray();
        pendingPackStateOwners.Clear();
        foreach (string ownerUid in owners)
        {
            if (packViewers.Contains(ownerUid) && serverApi?.World.PlayerByUid(ownerUid) is IServerPlayer owner)
                SendPackState(owner);
        }
    }

}
