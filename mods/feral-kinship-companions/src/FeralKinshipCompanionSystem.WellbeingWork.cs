#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;
using Vintagestory.API.MathTools;
namespace FeralKinshipCompanions;
public sealed partial class FeralKinshipCompanionSystem
{
    private readonly Dictionary<long, double> wellbeingWorkHours = new();
    internal static float OfflineDutyScanFactor(Entity entity) => CompanionWellbeingWork.ScanFactor(
        !entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>().IsCompanionOwnerOffline(entity));
    private void ObserveWellbeingWork(Entity entity, FoxPackRecordV2 record, double seconds)
    {
        var tasks = entity.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.ActiveTasksBySlot;
        bool working = tasks?.Any(task => task is AiTaskFeralKinshipFertilizer or AiTaskFeralKinshipTroughRefill or AiTaskFeralKinshipMowGrass or AiTaskFeralKinshipLogging or AiTaskFeralKinshipQuarry
            or AiTaskFeralKinshipFetchDroppedItem or AiTaskFeralKinshipBackpackDelivery or AiTaskFeralKinshipClearSnow) == true;
        bool resting = tasks?.Any(task => task is AiTaskFeralKinshipRestAtDen or AiTaskFeralKinshipCommandRest) == true;
        var calendar = entity.World.Calendar;
        double now = calendar.TotalHours;
        double hours = wellbeingWorkHours.TryGetValue(entity.EntityId, out double previous)
            ? CompanionDailyRoutine.ObservedHours(previous, now, seconds, calendar.SpeedOfTime, calendar.CalendarSpeedMul) : 0;
        wellbeingWorkHours[entity.EntityId] = now;
        bool indoorBed = resting && IsAtIndoorAssignedBed(entity, record);
        bool nourished = !FoodSystemEnabled || GetCompanionFoodLevel(entity) >= FoodLowThreshold;
        CompanionWellbeingWork.Observe(record.Wellbeing!, seconds, hours, calendar.HoursPerDay,
            working, resting, indoorBed, nourished);
        // A sleepy personality mood must not strand an otherwise rested animal
        // at its bed after dawn. Explicit mood overrides and commands still win.
        if (resting && indoorBed && nourished && record.Wellbeing!.WorkBurden <= .000001
            && !IsCompanionBedtime(entity) && GetCompanionActivityMode(entity) == CompanionActivityMode.AtEase
            && GetMood(entity) is "sleepy" or "resting")
        {
            var status = GetDomesticationStatus(entity, true)!;
            if (string.IsNullOrEmpty(status.GetString(MoodOverrideKey, ""))
                && string.IsNullOrEmpty(status.GetString(PackMoodOverrideKey, "")))
            {
                status.SetString(MoodKey, "rested");
                MarkSocialStateDirty(entity);
            }
        }
    }
    private bool TryGetAssignedBedPosition(Entity entity, out BlockPos? pos)
    {
        pos = null;
        if (packRepository?.Loaded != true || !IsTamedFox(entity)) return false;
        string id = GetDomesticationStatus(entity)?.GetString(FoxIdKey, "") ?? "";
        var bed = packRepository.GetBedForFox(id);
        if (bed == null || bed.OwnerUid != GetCompanionOwnerUid(entity)) return false;
        var candidate = new BlockPos(bed.X, bed.Y, bed.Z, bed.Dimension);
        var accessor = entity.World.BlockAccessor;
        if (accessor.GetChunkAtBlockPos(candidate) != null
            && accessor.GetBlock(candidate).Code?.ToShortString() != bed.BlockCode) return false;
        pos = candidate;
        return true;
    }
    private bool IsAtIndoorAssignedBed(Entity entity, FoxPackRecordV2 record)
    {
        var bed = packRepository?.GetBedForFox(record.FoxId);
        if (bed == null || bed.OwnerUid != record.OwnerUid || bed.Dimension != entity.Pos.Dimension) return false;
        var pos = new BlockPos(bed.X, bed.Y, bed.Z, bed.Dimension);
        var accessor = entity.World.BlockAccessor;
        if (accessor.GetChunkAtBlockPos(pos) == null || accessor.GetBlock(pos).Code?.ToShortString() != bed.BlockCode
            || entity.Pos.SquareDistanceTo(pos.X + .5, pos.InternalY + .35, pos.Z + .5) > 2.25) return false;
        // Use the same enclosed-room definition as the existing shelter checks.
        var room = roomRegistry?.GetRoomForPosition(pos);
        bool inside = room != null ? room.ExitCount == 0 || room.SkylightCount < room.NonSkylightCount
            : accessor.GetRainMapHeightAt(pos) > pos.Y;
        return inside && IsInside(entity);
    }
    private bool IsCompanionBedtime(Entity entity) => CompanionDailyRoutine.IsBedtime(
        entity.World.Calendar.HourOfDay, entity.World.Calendar.HoursPerDay,
        GetFoxPackTalentRank(entity, "earlier-morning"), GetFoxPackTalentRank(entity, "later-bedtime"));
    private void RecordWellbeingDuty(Entity entity, double units, bool selfProvisioning = false, string label = "Completed chore")
    {
        if (packRepository?.Loaded != true || IsCompanionOwnerOffline(entity)) return;
        string id = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (packRepository.TryGetRecord(id, out var record) && record != null)
        {
            record.Wellbeing = CompanionWellbeingRules.Normalize(record.Wellbeing);
            if (CompanionWellbeingWork.CompleteDuty(record.Wellbeing, units, true, selfProvisioning, label)) packRepository.Save();
        }
    }
    private void RecordWellbeingDeparture(FoxPackRecordV2 record)
    {
        if (packRepository?.Loaded != true || serverApi == null) return;
        var trip = packRepository.GetActiveExpeditions().FirstOrDefault(e => e.OwnerUid == record.OwnerUid && e.SelectedFoxIds.Contains(record.FoxId));
        if (trip == null) return;
        record.Wellbeing = CompanionWellbeingRules.Normalize(record.Wellbeing);
        if (CompanionWellbeingWork.Depart(record.Wellbeing, trip.ExpeditionId, trip.Type,
            IsOnlineServerPlayer(serverApi.World.PlayerByUid(record.OwnerUid))))
        {
            record.Wellbeing.ExpeditionLastWorldHour = serverApi.World.Calendar.TotalHours;
            wellbeingTripSamples[record.FoxId] = (trip.ExpeditionId, serverApi.World.ElapsedMilliseconds,
                IsOnlineServerPlayer(serverApi.World.PlayerByUid(record.OwnerUid)));
            packRepository.Save();
        }
    }
    private readonly Dictionary<string, (long Id, long Milliseconds, bool Online)> wellbeingTripSamples = new();
    private Dictionary<string, string>? wellbeingReturnOutcomes;
    private long nextWellbeingTripSaveMs;
    private void AccrueWellbeingExpedition(FoxExpeditionRecord trip)
    {
        if (serverApi == null || packRepository?.Loaded != true) return;
        long nowMs = serverApi.World.ElapsedMilliseconds;
        double now = serverApi.World.Calendar.TotalHours;
        bool online = IsOnlineServerPlayer(serverApi.World.PlayerByUid(trip.OwnerUid));
        bool changed = false;
        foreach (string id in trip.SelectedFoxIds)
        {
            if (!packRepository.TryGetRecord(id, out var record) || record == null || record.OwnerUid != trip.OwnerUid
                || !string.IsNullOrEmpty(record.PendingDepartureStatus)) continue;
            record.Wellbeing = CompanionWellbeingRules.Normalize(record.Wellbeing);
            var w = record.Wellbeing;
            if (w.Version != CompanionWellbeingRules.Version) continue;
            if (w.LastChargedExpeditionId != trip.ExpeditionId)
                CompanionWellbeingWork.Depart(w, trip.ExpeditionId, trip.Type, online);
            bool observed = wellbeingTripSamples.TryGetValue(id, out var previous) && previous.Id == trip.ExpeditionId
                && online && previous.Online && nowMs >= previous.Milliseconds && nowMs - previous.Milliseconds <= 5000;
            double hours = CompanionWellbeingWork.ChargeHours(w, w.ExpeditionLastWorldHour, now,
                trip.CompletesTotalHours > 0 ? trip.CompletesTotalHours : now, observed, trip.Type);
            wellbeingTripSamples[id] = (trip.ExpeditionId, nowMs, online);
            changed |= hours > 0;
        }
        if (changed && nowMs >= nextWellbeingTripSaveMs) {packRepository.Save(); nextWellbeingTripSaveMs = nowMs + 10000;}
    }
    private void CompleteWellbeingExpedition(FoxExpeditionRecord trip, bool objectiveSucceeded, Dictionary<string,string> outcomes)
    {
        if (serverApi == null || packRepository?.Loaded != true) return;
        bool carry = outcomes.Values.Any(status => status == "Mortally wounded");
        bool safe = objectiveSucceeded && outcomes.Count >= trip.SelectedFoxIds.Count
            && outcomes.Values.All(status => status == "Returned healthy");
        bool online = IsOnlineServerPlayer(serverApi.World.PlayerByUid(trip.OwnerUid));
        foreach (string id in trip.SelectedFoxIds)
        {
            wellbeingTripSamples.Remove(id);
            if (!outcomes.TryGetValue(id, out string? status) || status == "MIA"
                || !packRepository.TryGetRecord(id, out var record) || record == null || record.OwnerUid != trip.OwnerUid) continue;
            record.Wellbeing = CompanionWellbeingRules.Normalize(record.Wellbeing);
            CompanionWellbeingWork.Outcome(record.Wellbeing, trip.ExpeditionId, carry, status == "Returned healthy", safe, online);
        }
    }
}
