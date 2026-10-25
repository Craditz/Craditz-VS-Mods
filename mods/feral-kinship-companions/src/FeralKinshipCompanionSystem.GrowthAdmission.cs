#nullable enable
using System;
using System.Linq;
using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    // Authority is the actual prefix-owned source/replacement OBJECT pair,
    // never a matching name, species or owner alone. Keep it through deferred
    // publication; BecomeAdult returning is not proof of ledger admission.
    private bool VerifiedGrowthHandoff(Entity adult, out Entity child)
    {
        child = null!;
        if (!nativeAdultGrowthHandoffs.TryGetValue(adult, out var value) || value is not Entity source
            || ReferenceEquals(source, adult) || source.EntityId == adult.EntityId) return false;
        var status = GetDomesticationStatus(source);
        string id = status?.GetString(FoxIdKey) ?? string.Empty;
        string owner = GetCompanionOwnerUid(source);
        if (id.Length == 0 || owner.Length == 0 || GetDomesticationStatus(adult)?.GetString(FoxIdKey) != id
            || GetCompanionOwnerUid(adult) != owner || status?.GetBool(GrowthInProgressKey) != true
            || !ExactRecoveryCodeEquals(status.GetString(ChildAdultEntityCodeKey), adult.Code)
            || RetainedNativeSuspension.NativeAuthority(source) || RetainedNativeSuspension.NativeAuthority(adult)
            || status.GetBool(ExpeditionAwayKey) || GetDomesticationStatus(adult)?.GetBool(ExpeditionAwayKey) == true)
            return false;
        if (packRepository?.TryGetRecord(id, out var row) == true && row != null
            && (row.OwnerUid != owner || row.EntityId != source.EntityId && row.EntityId != adult.EntityId
                || NativeLinkPending(row) || EmergencyPending(LastEmergency(row))
                || row.PendingDepartureStatus.Length > 0 || row.PendingReturnStatus.Length > 0)) return false;
        child = source;
        return true;
    }

    private void PrepareVerifiedGrowthAdmission(Entity adult)
    {
        if (!VerifiedGrowthHandoff(adult, out var child) || serverApi == null || packRepository?.CanCheckpointRecovery != true) return;
        string id = GetDomesticationStatus(child)!.GetString(FoxIdKey);
        if (!packRepository.TryGetRecord(id, out var r) || r == null || !HasForceHistory(r) || !IsEmergencySelected(child)) return;
        // Never retire a still-published source or approve a third competing body.
        if (ReferenceEquals(serverApi.World.GetEntityById(child.EntityId), child)
            || !NativeRecoveryPublished(adult)
            || retainedAdmissionBodies.Values.Concat(loadedFoxes.Values).Any(e => !ReferenceEquals(e, child)
                && !ReferenceEquals(e, adult) && EmergencyIdentity(e, r) && !IsRetiredRecoveryOriginal(e))) return;
        var j = LastEmergency(r)!;
        var previous = r.EmergencyBody!;
        string oldCode = r.EntityCode;
        long oldId = r.EntityId;
        r.EntityCode = adult.Code.ToString(); r.EntityId = adult.EntityId;
        r.EmergencyBody = new() { Operation = j.Id, NativeId = adult.EntityId, BodyKey = ForceBodyKey(adult),
            Marker = adult.WatchedAttributes.GetString(EmergencyBodyKey) ?? string.Empty,
            Phase = "Ready", Reason = "verified native growth from selected recovery generation" };
        try { ForceCheckpoint(r, j); }
        catch { r.EntityCode = oldCode; r.EntityId = oldId; r.EmergencyBody = previous; throw; }
        adult.WatchedAttributes.SetString(ForceBodyIdentityKey, r.EmergencyBody.BodyKey);
    }

    private void FinishVerifiedGrowthAdmission(Entity adult)
    {
        if (!VerifiedGrowthHandoff(adult, out _) || !loadedFoxes.TryGetValue(adult.EntityId, out var registered)
            || !ReferenceEquals(registered, adult) || !NativeRecoveryPublished(adult)) return;
        string id = GetDomesticationStatus(adult)!.GetString(FoxIdKey);
        if (packRepository?.TryGetRecord(id, out var row) != true || row == null || row.EntityId != adult.EntityId
            || !ExactRecoveryCodeEquals(row.EntityCode, adult.Code)) return;
        packRepository.Save();
        CompleteCompanionAdultGrowthHandoff(adult);
    }
}
