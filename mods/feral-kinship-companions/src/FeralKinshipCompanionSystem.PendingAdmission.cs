#nullable enable
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public partial class FeralKinshipCompanionSystem
{
    // PetAI publishes a replacement before copying its ownership. Its gated
    // entity tick cannot be responsible for retrying first admission afterward.
    private const int PendingAdmissionLimit = 1024;
    private const int PendingAdmissionWorkPerTick = 32;
    private const long PendingAdmissionLifetimeMs = 120000;
    private sealed record PendingCompanionAdmission(long ObservedAt, LinkedListNode<Entity> Node);
    private readonly object pendingCompanionAdmissionSync = new();
    private readonly Dictionary<Entity, PendingCompanionAdmission> pendingCompanionAdmissions = new(ReferenceEqualityComparer.Instance);
    private readonly LinkedList<Entity> pendingCompanionAdmissionQueue = new();

    private void ForgetPendingCompanionAdmission(Entity entity)
    {
        lock (pendingCompanionAdmissionSync)
        {
            if (pendingCompanionAdmissions.Remove(entity, out PendingCompanionAdmission? pending)
                && ReferenceEquals(pending.Node.List, pendingCompanionAdmissionQueue))
                pendingCompanionAdmissionQueue.Remove(pending.Node);
        }
    }

    private void ClearPendingCompanionAdmissions()
    {
        lock (pendingCompanionAdmissionSync)
        {
            pendingCompanionAdmissions.Clear();
            pendingCompanionAdmissionQueue.Clear();
        }
    }

    private void TrackPendingCompanionAdmission(Entity entity)
    {
        if (!RetainedNativeSuspension.Enabled || entity is not EntityAgent
            || entity.EntityId <= 0 || IsGuideFox(entity)
            || entity.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>() == null
            || !CompanionSpeciesCatalog.TryGetByTameEntityCode(entity.Code, out _)
            || RetainedNativeSuspension.NativeAuthority(entity)
            || GetDomesticationStatus(entity)?.GetBool(ExpeditionAwayKey) == true) return;

        long observedAt = entity.World.ElapsedMilliseconds;
        lock (pendingCompanionAdmissionSync)
        {
            if (pendingCompanionAdmissions.ContainsKey(entity)
                || pendingCompanionAdmissions.Count >= PendingAdmissionLimit) return;
            LinkedListNode<Entity> node = pendingCompanionAdmissionQueue.AddLast(entity);
            pendingCompanionAdmissions.Add(entity, new(observedAt, node));
        }
    }

    private bool HasPendingAdmissionAuthority(Entity entity)
    {
        string id = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (packRepository!.IsPermanentlyDeleted(id)) return true;
        if (!packRepository.TryGetPendingAdmissionRecord(entity.EntityId, id, out FoxPackRecordV2? record)) return true;
        string ownerUid = record?.OwnerUid ?? GetCompanionOwnerUid(entity);
        string foxId = record?.FoxId ?? id;
        if (!packRepository.TryHasPendingAdmissionExpedition(ownerUid, foxId, out bool assigned) || assigned) return true;
        if (record == null) return false;
        return record.OwnerUid != GetCompanionOwnerUid(entity)
            || record.FoxId != id || packRepository.IsPermanentlyDeleted(record.FoxId)
            || record.Archived || record.MissingContentArchive?.Active == true
            || !string.IsNullOrEmpty(record.RetainedTrip)
            || !string.IsNullOrEmpty(record.RetainedPhase)
            || !string.IsNullOrEmpty(record.RetainedOutcome)
            || !string.IsNullOrEmpty(record.PendingDepartureStatus)
            || !string.IsNullOrEmpty(record.PendingReturnStatus)
            || record.PendingDepartureAtUtcMs > 0 || record.PendingReturnAtUtcMs > 0
            || record.Status?.StartsWith("Away \u2014", StringComparison.OrdinalIgnoreCase) == true
            || record.Status?.StartsWith("Leaving \u2014", StringComparison.OrdinalIgnoreCase) == true
            || record.Status is "Dead" or "MIA" or "Running late" or "Returning" or "Awaiting safe return";
    }

    private void MaintainPendingCompanionAdmissions()
    {
        if (!RetainedNativeSuspension.Enabled || !companionContentReady || serverApi == null
            || packRepository?.Loaded != true || RuntimeEnv.ServerMainThreadId <= 0
            || Environment.CurrentManagedThreadId != RuntimeEnv.ServerMainThreadId) return;

        int work;
        lock (pendingCompanionAdmissionSync) work = Math.Min(PendingAdmissionWorkPerTick, pendingCompanionAdmissionQueue.Count);
        for (int i = 0; i < work; i++)
        {
            Entity entity;
            PendingCompanionAdmission pending;
            lock (pendingCompanionAdmissionSync)
            {
                if (pendingCompanionAdmissionQueue.First is not LinkedListNode<Entity> node) break;
                entity = node.Value;
                pendingCompanionAdmissionQueue.Remove(node);
                if (!pendingCompanionAdmissions.TryGetValue(entity, out PendingCompanionAdmission? entry)) continue;
                pending = entry;
            }
            bool retry = false;
            try
            {
                long now = serverApi.World.ElapsedMilliseconds;
                if (!ReferenceEquals(entity.Api, serverApi) || !ReferenceEquals(entity.World, serverApi.World)
                    || !entity.Alive || entity.State == EnumEntityState.Despawned
                    || now < pending.ObservedAt || now - pending.ObservedAt >= PendingAdmissionLifetimeMs
                    || RetainedNativeSuspension.NativeAuthority(entity)
                    || GetDomesticationStatus(entity)?.GetBool(ExpeditionAwayKey) == true) continue;

                retry = true;
                if (!IsTamedFox(entity) || string.IsNullOrWhiteSpace(GetCompanionOwnerUid(entity))) continue;
                if (!RetainedNativeSuspension.Qualified(entity, out _)) continue;
                if (HasPendingAdmissionAuthority(entity)) { retry = false; continue; }
                // Missing publication is retried. Competing publication enters
                // the existing reconciler's Hold path; it is never replaced.
                Entity? canonical = serverApi.World.GetEntityById(entity.EntityId);
                if (canonical == null) continue;
                if (!ReferenceEquals(canonical, entity))
                {
                    ReconcileRetainedBody(entity);
                    retry = false;
                    continue;
                }
                RegisterLoadedFox(entity);
                retry = !loadedFoxes.TryGetValue(entity.EntityId, out Entity? registered)
                    || !ReferenceEquals(registered, entity);
            }
            catch (Exception ex)
            {
                RetainedNativeSuspension.Hold(entity, "Deferred admission failed: " + ex.GetType().Name);
                retry = false;
            }
            finally
            {
                // Never hold our queue monitor across world/ledger/body work.
                // A concurrent exact-object removal/retrack owns its new node.
                lock (pendingCompanionAdmissionSync)
                {
                    if (pendingCompanionAdmissions.TryGetValue(entity, out PendingCompanionAdmission? current)
                        && ReferenceEquals(current, pending))
                    {
                        if (retry) pendingCompanionAdmissionQueue.AddLast(pending.Node);
                        else pendingCompanionAdmissions.Remove(entity);
                    }
                }
            }
        }
    }
}
