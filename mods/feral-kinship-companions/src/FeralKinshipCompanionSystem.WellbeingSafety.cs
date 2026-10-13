#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
namespace FeralKinshipCompanions;
public sealed partial class FeralKinshipCompanionSystem
{
    private readonly Dictionary<long, long> wellbeingSafetySamples = new();
    private void TickWellbeingSafety(Entity entity)
    {
        if (packRepository?.Loaded != true) return;
        string id = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (packRepository.TryGetRecord(id, out var record) && record != null) ObserveCompanionWellbeing(entity, record);
    }
    private void ObserveWellbeingSafety(Entity entity, FoxPackRecordV2 record, bool force = false)
    {
        long now = entity.World.ElapsedMilliseconds;
        if (IsCompanionOwnerOffline(entity) || !entity.Alive || IsFoxAwayFromWorld(entity))
        { wellbeingSafetySamples.Remove(entity.EntityId); return; }
        var state = record.Wellbeing;
        if (state == null || state.Version != CompanionWellbeingRules.Version) return;
        long previous = wellbeingSafetySamples.GetValueOrDefault(entity.EntityId, -1);
        if (!force && previous >= 0 && now >= previous && now - previous < 1000) return;
        wellbeingSafetySamples[entity.EntityId] = now;
        GetHealth(entity, out float current, out float maximum);
        float fraction = maximum > 0 && float.IsFinite(current) && float.IsFinite(maximum) ? current / maximum : float.NaN;
        bool threat = entity.World.GetEntitiesAround(entity.Pos.XYZ, 16, 8)
            .Any(other => other.EntityId != entity.EntityId && other.Pos.Dimension == entity.Pos.Dimension
                && IsPredator(other) && !IsTamedOrBeingTamed(other) && HasWellbeingLineOfSight(entity, other));
        CompanionWellbeingSafety.Observe(state, fraction, IsFoxMortallyWounded(entity),
            entity.WatchedAttributes.GetInt(EntityHealthStateKey, 0) == RecoveringHealthState,
            threat, IsPrecipitating(entity) && !IsInside(entity)
                && entity.World.BlockAccessor.GetRainMapHeightAt(entity.Pos.AsBlockPos) <= entity.Pos.Y, CompanionWellbeingSafety.SafeDelta(previous, now));
        ObserveWellbeingCompany(entity, record, CompanionWellbeingSafety.SafeDelta(previous, now));
        ObserveWellbeingWork(entity, record, CompanionWellbeingSafety.SafeDelta(previous, now));
        CompanionWellbeingEvents.Observe(state, CompanionWellbeingSafety.SafeDelta(previous, now));
        ObserveWellbeingCare(entity, record, previous, now);
        CompanionWellbeingRecovery.Update(state);
    }
    private static bool HasWellbeingLineOfSight(Entity entity, Entity other)
    {
        BlockSelection? block = null; EntitySelection? selected = null;
        var from = entity.Pos.XYZ.AddCopy(0, Math.Clamp(entity.CollisionBox.YSize * .6, .2, 1.5), 0);
        var to = other.Pos.XYZ.AddCopy(0, Math.Clamp(other.CollisionBox.YSize * .6, .2, 1.5), 0);
        entity.World.RayTraceForSelection(from, to, ref block, ref selected,
            (pos, b) => b.GetCollisionBoxes(entity.World.BlockAccessor, pos)?.Length > 0, _ => false);
        return block == null;
    }
}
