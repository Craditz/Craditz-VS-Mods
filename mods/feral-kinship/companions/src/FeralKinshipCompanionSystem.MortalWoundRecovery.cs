using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    internal void ClearMortalWoundRecoveryCountdown(Entity entity)
    {
        if (serverApi == null
            || entity.Api.Side != EnumAppSide.Server
            || entity.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>() == null)
        {
            return;
        }

        ITreeAttribute? status = GetDomesticationStatus(entity);
        if (status == null) return;

        status.SetInt(StabilizedWindowAppliedRankKey, 0);
        status.SetDouble(StabilizedWindowAdjustedStartHoursKey, -1d);
        status.SetDouble(DeathlessRecoveryAtHoursKey, -1d);
        status.SetDouble(DeathlessFallbackAtHoursKey, -1d);
        MarkSocialStateDirty(entity);

        string foxId = status.GetString(FoxIdKey, string.Empty);
        if (packRepository?.Loaded != true
            || string.IsNullOrWhiteSpace(foxId)
            || !packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
            || record == null)
        {
            return;
        }

        int healthState = entity.WatchedAttributes.GetInt(EntityHealthStateKey, 0);
        record.HealthState = healthState;
        record.MortallyWoundedStartHours = healthState == RecoveringHealthState
            ? entity.WatchedAttributes.GetDouble("mortallyWoundedTotalHours", 0d)
            : 0d;
        record.StabilizedWindowAppliedRank = 0;
        record.StabilizedWindowAdjustedStartHours = -1d;
        record.DeathlessRecoveryAtHours = -1d;
        record.DeathlessFallbackAtHours = -1d;
        if (string.Equals(record.Status, "Mortally wounded", System.StringComparison.OrdinalIgnoreCase))
        {
            record.Status = healthState == RecoveringHealthState ? "Recovering" : "Present";
        }

        long entityId = entity.EntityId;
        long woundGeneration = FeralKinshipMortalWoundRecoveryPatch.GetWoundGeneration(entity);
        serverApi.Event.RegisterCallback(
            _ => PersistMortalRecoveryAfterHealthUpdate(entity, entityId, foxId, record, woundGeneration),
            50);
    }

    private void PersistMortalRecoveryAfterHealthUpdate(
        Entity entity,
        long entityId,
        string foxId,
        FoxPackRecordV2 expectedRecord,
        long woundGeneration)
    {
        if (serverApi == null || entity.EntityId != entityId || entity.Api.Side != EnumAppSide.Server)
        {
            return;
        }

        EntityBehaviorFeralKinshipFoxSocial? social = entity.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>();
        ITreeAttribute? status = GetDomesticationStatus(entity);
        bool sameFoxAssignment = string.Equals(status?.GetString(FoxIdKey, string.Empty), foxId, System.StringComparison.Ordinal);
        bool sameGeneration = FeralKinshipMortalWoundRecoveryPatch.GetWoundGeneration(entity) == woundGeneration;
        bool stillMortallyWounded = entity.GetBehavior<EntityBehaviorMortallyWoundable>()?.HealthState
            == EnumEntityHealthState.MortallyWounded;
        FoxPackRecordV2? record = null;
        bool hasRecord = packRepository?.Loaded == true
            && packRepository.TryGetRecord(foxId, out record)
            && record != null;
        bool sameRecord = hasRecord && ReferenceEquals(record, expectedRecord);
        bool canPersist = MortalWoundRecoveryPolicy.CanPersistCallback(
            isSameWorldEntity: ReferenceEquals(serverApi.World.GetEntityById(entityId), entity),
            isAlive: entity.Alive,
            shouldDespawn: entity.ShouldDespawn,
            isSameCompanion: social != null,
            isSameFoxAssignment: sameFoxAssignment,
            isSameRecordEntity: sameRecord && record!.EntityId == entityId,
            isSameWoundGeneration: sameGeneration,
            isStillMortallyWounded: stillMortallyWounded);
        if (!canPersist || !hasRecord || record == null) return;

        int healthState = entity.WatchedAttributes.GetInt(EntityHealthStateKey, 0);
        record.HealthState = healthState;
        record.MortallyWoundedStartHours = healthState == RecoveringHealthState
            ? entity.WatchedAttributes.GetDouble("mortallyWoundedTotalHours", 0d)
            : 0d;
        EntityBehaviorHealth? health = entity.GetBehavior<EntityBehaviorHealth>();
        if (health != null)
        {
            record.CurrentHealth = health.Health;
            record.MaxHealth = health.MaxHealth;
        }
        if (string.Equals(record.Status, "Mortally wounded", System.StringComparison.OrdinalIgnoreCase))
        {
            record.Status = healthState == RecoveringHealthState ? "Recovering" : "Present";
        }

        packRepository!.Save();
    }
}
