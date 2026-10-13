#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;
namespace FeralKinshipCompanions;
public sealed partial class FeralKinshipCompanionSystem
{
    internal static float OfflineDutyScanFactor(Entity entity) => CompanionWellbeingWork.ScanFactor(
        !entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>().IsCompanionOwnerOffline(entity));
    private void ObserveWellbeingWork(Entity entity, FoxPackRecordV2 record, double seconds)
    {
        var tasks = entity.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.ActiveTasksBySlot;
        bool working = tasks?.Any(task => task is AiTaskFeralKinshipFertilizer or AiTaskFeralKinshipTroughRefill or AiTaskFeralKinshipMowGrass or AiTaskFeralKinshipLogging
            or AiTaskFeralKinshipFetchDroppedItem or AiTaskFeralKinshipBackpackDelivery or AiTaskFeralKinshipClearSnow) == true;
        bool resting = GetCompanionActivityMode(entity) == CompanionActivityMode.Rest && HasCompanionRestArrival(entity);
        CompanionWellbeingWork.Observe(record.Wellbeing!, seconds, working, resting);
    }
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
