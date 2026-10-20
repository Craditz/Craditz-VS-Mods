#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;

public sealed partial class FoxPackRecordV2
{
    // Emergency native escrow, not a second inventory authority. Never replayed
    // automatically. Kept on a failed rebuild so a failed rollback cannot erase it.
    [ProtoMember(152)] public byte[] DeveloperRecoveryNativeEscrow = Array.Empty<byte>();
}

public sealed partial class FeralKinshipCompanionSystem
{
    private readonly HashSet<string> pendingFullRebuilds = new(StringComparer.Ordinal);
    private readonly HashSet<string> pendingRecoveryBatches = new(StringComparer.Ordinal);
    private sealed record RecoveryResult(string Kind, string Reason);

    private string FullRecoveryBlocker(FoxPackRecordV2 r, Entity? original)
    {
        return FullRecoveryBlockerUsingAuthority(r, original, CaptureExpeditionPresentationAuthority());
    }
    private string FullRecoveryBlockerUsingAuthority(FoxPackRecordV2 r, Entity? original, ExpeditionPresentationAuthority? authority)
    {
        if (authority == null) return "Unknown server authority: " + presentationAuthorityReason;
        if (NativeLinkPending(r)) return "Native-ID relink/Undo is pending; inspect /companionlink status id:" + r.FoxId;
        if (EmergencyPending(LastEmergency(r))) return "Emergency rebuild/Undo journal is pending; normal Rebuild cannot overwrite it";
        if (r.MissingRecoveryState == "Prepared") return "Interrupted missing-body publication; native and ledger checkpoints retained. Inspect /companiondiag before another rebuild";
        if (r.DeveloperRecoveryNativeEscrow.Length > 0) return "Unresolved native reconstruction escrow; inspect /companiondiag before another rebuild";
        if (authority.Conflicts.Contains(r.FoxId) || authority.NativeConflicts.Contains(r.EntityId)) return "Duplicate identity; inspect /companiondiag before retrying";
        if (authority.Assigned.Contains(r.FoxId)) return "Active expedition; finish or cancel it first";
        if (packRepository!.IsPermanentlyDeleted(r.FoxId)) return "Permanently deleted record";
        if (pendingRecoveryChecks.Contains(r.FoxId) || pendingAdminCompanionTeleports.Contains(r.OwnerUid + ":" + r.FoxId)) return "Saved-location lookup is pending; wait for its result";
        if (!string.IsNullOrEmpty(r.PendingDepartureStatus) || r.PendingDepartureAtUtcMs > 0 || r.PendingDepartureDeadlineUtcMs > 0
            || !string.IsNullOrEmpty(r.PendingReturnStatus) || r.PendingReturnAtUtcMs > 0) return "Departure/return is pending; inspect /companiondiag";
        if (original == null) return string.IsNullOrEmpty(r.RetainedTrip)
            ? "Original is not loaded; use Teleport to check its saved location first. No inventory/body was fabricated"
            : "Retained original is unavailable; load its saved location first. The ledger is not a native inventory snapshot";
        if (!ReferenceEquals(original.Api, serverApi) || !ReferenceEquals(original.World, serverApi!.World)
            || original.Teleporting || original.State == EnumEntityState.Despawned || original is not EntityAgent agent || agent.MountedOn != null) return "Original native body is not ready/unmounted";
        if (r.EntityId != original.EntityId || GetCompanionOwnerUid(original) != r.OwnerUid || GetDomesticationStatus(original)?.GetString(FoxIdKey) != r.FoxId) return "Original native identity/owner differs from the saved record";
        if (!NativeRecoveryPublished(original)) return "Original native publication is incomplete: " + RecoveryPublicationReason(original);
        var marker = RetainedNativeSuspension.Marker(original);
        if (marker != null || !string.IsNullOrEmpty(r.RetainedTrip))
        {
            if(confirmedHoldRebuilds.Contains(r.FoxId)) {
                string manual=CompletedHoldRepairBlocker(r,original,authority);if(manual.Length>0)return manual;
            } else {
            if (marker?.GetString("phase") != "Complete" || r.RetainedPhase != "Complete") return "Retained trip is unfinished or on Hold; inspect /companiondiag; admin may inspect /companionrepair native:<ID>";
            if (!RetainedNativeSuspension.Matches(original, r) || !RetainedNativeSuspension.SafeSnapshot(original)
                || !RetainedNativeSuspension.CompletionSettled(original,r)) return "Completed-trip receipt/snapshot is inconsistent; no cargo or reward replay allowed";
            double resume = marker.GetDouble("resumeHours", double.NaN), now = serverApi.World.Calendar.TotalHours;
            if (!double.IsFinite(resume) || !double.IsFinite(now) || resume < marker.GetDouble("capturedHours") || resume > now)
                return "Completed-trip clock receipt is missing or invalid; no clock rebasing allowed";
            }
        }
        else if (GetDomesticationStatus(original)?.GetBool(ExpeditionAwayKey) == true) return "Away flag has no completed-trip authority; inspect /companiondiag";
        if (HasFoxPendingCartPickup(original) || r.BackpackDeliveryActive || backpackPreflights.ContainsKey(original.EntityId)
            || backpackStorageAreaLoads.Contains(r.FoxId) || backpackMissingEntityRecoveryChecks.Contains(r.FoxId)) return "Cargo/delivery work is pending; finish it first";
        return string.Empty;
    }

    private bool NativeRecoveryPublished(Entity e)
    {
        if (serverApi == null || !ReferenceEquals(serverApi.World.GetEntityById(e.EntityId), e)) return false;
        var chunk = serverApi.WorldManager.GetChunk(e.InChunkIndex3d);
        if (chunk == null || chunk.Disposed) return false;
        var matches = chunk.Entities.Take(chunk.EntitiesCount).Where(x => x != null && x.EntityId == e.EntityId).ToArray();
        return matches.Length == 1 && ReferenceEquals(matches[0], e);
    }

    private string RecoveryPublicationReason(Entity e)
    {
        var indexed = serverApi?.World.GetEntityById(e.EntityId);
        var chunk = serverApi?.WorldManager.GetChunk(e.InChunkIndex3d);
        var matches = chunk?.Entities.Take(chunk.EntitiesCount).Where(x => x != null && x.EntityId == e.EntityId).ToArray() ?? Array.Empty<Entity>();
        return $"world={(indexed == null ? "absent" : ReferenceEquals(indexed, e) ? "same" : "conflicting")}, chunk={(chunk == null ? "absent" : chunk.Disposed ? "disposed" : "loaded")}, native-id-count={matches.Length}, exact-body={matches.Any(x => ReferenceEquals(x, e))}, state={e.State}, teleporting={e.Teleporting}";
    }

    private byte[] SnapshotRecoveryNative(Entity e)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) e.ToBytes(writer, false);
        if (stream.Length == 0 || stream.Length > 8 * 1024 * 1024) throw new InvalidOperationException("Native snapshot exceeds the 8 MiB recovery limit");
        return stream.ToArray();
    }

    private Entity ReconstructRecoveryNative(EntityProperties type, byte[] bytes)
    {
        Entity e = serverApi!.World.ClassRegistry.CreateEntity(type) ?? throw new InvalidOperationException("Native factory returned null");
        using var stream = new MemoryStream(bytes, false);
        using var reader = new BinaryReader(stream);
        e.FromBytes(reader, false);
        if (stream.Position != stream.Length) throw new InvalidOperationException("Native snapshot was not fully consumed");
        return e;
    }

    private static void SanitizeCompletedRecovery(Entity e, FoxPackRecordV2 r)
    {
        // Full native deserialization preserves native inventories/provider state.
        // Discard only our completed expedition presentation receipt; fresh native
        // Initialize recreates boxes, behavior instances, renderer and listeners.
        e.WatchedAttributes.RemoveAttribute(RetainedNativeSuspension.Key);
        GetDomesticationStatus(e)!.SetBool(ExpeditionAwayKey, false);
        r.RetainedTrip = r.RetainedPhase = r.RetainedOutcome = r.RetainedLegacyAuthority = string.Empty;
        r.RetainedLegacyAdmissionHours = 0;
    }

    private void StartFullCompanionRecovery(IServerPlayer actor, string foxId, string ownerUid, Action<RecoveryResult>? completed = null)
    {
        LogRecoveryAttempt(foxId, "request", "Explicit rebuild; automatic NotNeeded classification is not consulted");
        try
        {
            if (TryLoadFullRecoveryOriginal(actor, foxId, ownerUid, completed)) return;
            if (packRepository?.TryGetRecord(foxId, out var missing) == true && missing != null && missing.OwnerUid == ownerUid
                && FindArchiveOriginal(missing, out string absence) == null && IsAbsentRecoveryOriginalReason(absence))
            { StartMissingCompanionRecovery(actor, missing, completed); return; }
            StartFullCompanionRecoveryCore(actor, foxId, ownerUid, completed);
        }
        catch (Exception error)
        {
            pendingFullRebuilds.Remove(foxId);
            serverApi?.Logger.Error("[FeralKinshipCompanions] Recovery preflight failed for {0}: {1}", foxId, error);
            var result = new RecoveryResult("failed", foxId + ": recovery preflight failed; inspect server log. " + error.Message);
            if (completed != null) completed(result); else SendCompanionRecoveryState(actor, result.Reason);
        }
    }

    private bool TryLoadFullRecoveryOriginal(IServerPlayer actor, string foxId, string ownerUid, Action<RecoveryResult>? completed)
    {
        if (serverApi == null || packRepository?.Loaded != true || !CanUseDeveloperTools(actor)
            || !DeveloperMayControlOwner(actor, ownerUid) || pendingFullRebuilds.Contains(foxId)
            || pendingRecoveryChecks.Contains(foxId) || completed == null && pendingRecoveryBatches.Contains(ownerUid)
            || !packRepository.TryGetRecord(foxId, out FoxPackRecordV2? r) || r == null || r.OwnerUid != ownerUid
            || !r.HasLastKnownPosition || !double.IsFinite(r.LastKnownX) || !double.IsFinite(r.LastKnownZ)
            || r.LastKnownX < int.MinValue || r.LastKnownX > int.MaxValue || r.LastKnownZ < int.MinValue || r.LastKnownZ > int.MaxValue
            || FindArchiveOriginal(r, out _) != null) return false;
        if (!CanLookUpFullRecoveryOriginal(r)) return false;
        pendingRecoveryChecks.Add(foxId);
        string lookupFingerprint = RecoveryUiFingerprint(r, null);
        string lookupForceEpoch = ForceEpoch(r);
        int checks = 0, loadAcknowledged = 0; bool finished = false;
        void Finish(string reason)
        {
            if (finished) return; finished = true; pendingRecoveryChecks.Remove(foxId);
            LogRecoveryAttempt(foxId, "lookup-result", reason);
            if (completed != null) completed(new RecoveryResult("skipped", GetFoxRecordLabel(r) + ": " + reason));
            else SendCompanionRecoveryState(actor, GetFoxRecordLabel(r) + ": " + reason);
        }
        void Check()
        {
            if (finished || serverApi == null || !pendingRecoveryChecks.Contains(foxId)) return;
            if (ForceSuperseded(r, lookupForceEpoch)) { Finish("Source lookup superseded by confirmed force reconstruction"); return; }
            try
            {
                if (actor.ConnectionState != EnumClientState.Playing || !CanUseDeveloperTools(actor) || !DeveloperMayControlOwner(actor, ownerUid)) { Finish("Selected-player authority expired or requesting session disconnected; select the cart again"); return; }
                if (!packRepository!.TryGetRecord(foxId, out var current) || !ReferenceEquals(current, r) || r.OwnerUid != ownerUid
                    || RecoveryUiFingerprint(r, null) != lookupFingerprint) { Finish("Identity, journey receipt or cargo changed during source lookup; request a new preview"); return; }
                Entity? original = FindArchiveOriginal(r, out string lookupReason);
                if (original == null && !IsAbsentRecoveryOriginalReason(lookupReason)) { Finish(lookupReason); return; }
                if (original != null)
                {
                    finished = true; pendingRecoveryChecks.Remove(foxId);
                    StartFullCompanionRecovery(actor, foxId, ownerUid, completed); return;
                }
                if (++checks >= 20) {
                    finished = true; pendingRecoveryChecks.Remove(foxId);
                    LogRecoveryAttempt(foxId, "source-lookup", "Absent after 20 single-chain polls; load-ack=" + System.Threading.Volatile.Read(ref loadAcknowledged));
                    if (r.LastKnownDimension == 0 && System.Threading.Volatile.Read(ref loadAcknowledged) == 0) {
                        if (completed != null) completed(new("skipped", "Source-column load has not acknowledged; absence is unknown. No reconstruction attempted"));
                        else SendCompanionRecoveryState(actor, "Source-column load has not acknowledged; absence is unknown. No reconstruction attempted");
                        return;
                    }
                    StartMissingCompanionRecovery(actor, r, completed); return;
                }
                serverApi.Event.RegisterCallback(_ => Check(), 500);
            }
            catch (Exception error) { Finish("Native original lookup failed; no replacement spawned. " + error.Message); }
        }
        int x = (int)Math.Floor(r.LastKnownX / (double)GlobalConstants.ChunkSize), z = (int)Math.Floor(r.LastKnownZ / (double)GlobalConstants.ChunkSize);
        try
        {
            if (completed == null) SendCompanionRecoveryState(actor, GetFoxRecordLabel(r) + ": recovery pending saved-location lookup");
            if (r.LastKnownDimension == 0)
                serverApi.WorldManager.LoadChunkColumnPriority(x, z, new ChunkLoadOptions { KeepLoaded = false,
                    OnLoaded = () => System.Threading.Interlocked.Exchange(ref loadAcknowledged, 1) });
            else { serverApi.WorldManager.LoadChunkColumnForDimension(x, z, r.LastKnownDimension); serverApi.Event.RegisterCallback(_ => Check(), 500); }
            // Also bound a provider that never invokes OnLoaded. OnLoaded still
            // inspects immediately while the native column is definitely resident.
            serverApi.Event.RegisterCallback(_ => Check(), 500);
        }
        catch (Exception error) { Finish("Saved chunk could not be loaded; no replacement spawned. " + error.Message); }
        return true;
    }

    private bool CanLookUpFullRecoveryOriginal(FoxPackRecordV2 r)
    {
        return CanLookUpFullRecoveryOriginalUsingAuthority(r, CaptureExpeditionPresentationAuthority());
    }
    private bool CanLookUpFullRecoveryOriginalUsingAuthority(FoxPackRecordV2 r, ExpeditionPresentationAuthority? authority, bool requireLocation = true)
    {
        return authority != null && (!requireLocation || r.HasLastKnownPosition) && double.IsFinite(r.LastKnownX) && double.IsFinite(r.LastKnownZ)
            && r.LastKnownX >= int.MinValue && r.LastKnownX <= int.MaxValue && r.LastKnownZ >= int.MinValue && r.LastKnownZ <= int.MaxValue
            && !authority.Assigned.Contains(r.FoxId) && !authority.Conflicts.Contains(r.FoxId) && !authority.NativeConflicts.Contains(r.EntityId)
            && !packRepository!.IsPermanentlyDeleted(r.FoxId) && (string.IsNullOrEmpty(r.RetainedTrip) || r.RetainedPhase == "Complete")
            && string.IsNullOrEmpty(r.PendingDepartureStatus) && r.PendingDepartureAtUtcMs <= 0 && r.PendingDepartureDeadlineUtcMs <= 0
            && string.IsNullOrEmpty(r.PendingReturnStatus) && r.PendingReturnAtUtcMs <= 0 && pendingRecoveryChecks.Count < 16
            && !pendingRecoveryChecks.Contains(r.FoxId) && !pendingFullRebuilds.Contains(r.FoxId)
            && !pendingAdminCompanionTeleports.Contains(r.OwnerUid + ":" + r.FoxId);
    }

    private void StartFullCompanionRecoveryCore(IServerPlayer actor, string foxId, string ownerUid, Action<RecoveryResult>? completed)
    {
        void Report(string kind, string reason)
        {
            LogRecoveryAttempt(foxId, "result-" + kind, reason);
            try
            {
                completed?.Invoke(new RecoveryResult(kind, reason));
                if (completed == null) SendCompanionRecoveryState(actor, reason);
            }
            catch (Exception error) { serverApi?.Logger.Warning("[FeralKinshipCompanions] Recovery result delivery failed for {0}: {1}", foxId, error.Message); }
        }
        if (serverApi == null || packRepository?.Loaded != true || !CanUseDeveloperTools(actor)
            || !DeveloperMayControlOwner(actor, ownerUid)
            || !packRepository.TryGetRecord(foxId, out FoxPackRecordV2? r) || r == null || r.OwnerUid != ownerUid)
        { Report("skipped", "Recovery refused: unauthorized or missing target-owner record"); return; }
        if (completed == null && pendingRecoveryBatches.Contains(ownerUid)) { Report("skipped", "This player's recovery batch is already pending; wait for its result"); return; }
        if (pendingRecoveryChecks.Contains(foxId)) { Report("skipped", "Saved-location lookup is already pending for this companion"); return; }
        if (!pendingFullRebuilds.Add(foxId)) { Report("skipped", "Recovery already pending for this companion"); return; }
        Entity? original = FindArchiveOriginal(r, out string lookupReason);
        bool completedHoldRecovery=confirmedHoldRebuilds.Contains(foxId);
        string blocker = string.IsNullOrEmpty(lookupReason) ? FullRecoveryBlocker(r, original) : lookupReason;
        if (!string.IsNullOrEmpty(blocker)) { pendingFullRebuilds.Remove(foxId); Report("skipped", GetFoxRecordLabel(r) + ": " + blocker); return; }
        FoxPackRecordV2 saved;
        try { saved = ClonePackRecordForRebuild(r); }
        catch (Exception error) { pendingFullRebuilds.Remove(foxId); Report("failed", "Record snapshot failed: " + error.Message); return; }
        bool manualRevival = !original!.Alive;
        float originalHealth = original.GetBehavior<Vintagestory.GameContent.EntityBehaviorHealth>()?.Health ?? 0;
        if (!manualRevival && (!float.IsFinite(originalHealth) || originalHealth <= 0))
        { pendingFullRebuilds.Remove(foxId); Report("skipped", "Original has invalid native health; no automatic healing"); return; }
        Entity? replacement = null;
        byte[] snapshot = Array.Empty<byte>();
        string forceEpoch = ForceEpoch(r);
        bool removed = false;
        bool removalAttempted = false, spawnAttempted = false;
        bool finishing = false;
        void Finish(string kind, string text)
        {
            if (finishing) return;
            finishing = true;
            pendingFullRebuilds.Remove(foxId);
            rebuildingEntityIds.Remove(saved.EntityId);
            Report(kind, GetFoxRecordLabel(saved) + ": " + text);
        }
        void Fail(Exception error)
        {
            if (ForceSuperseded(r, forceEpoch)) { if (replacement != null) GuardForceEmergency(replacement); Finish("skipped", "Old rebuild superseded by confirmed force reconstruction; no ledger rollback or inventory replay"); return; }
            try
            {
                if (removalAttempted && !removed)
                {
                    removed = !ReferenceEquals(serverApi!.World.GetEntityById(saved.EntityId), original);
                    if (!removed)
                    {
                        r.DeveloperRecoveryNativeEscrow = snapshot; packRepository!.Save();
                        throw new InvalidOperationException("Original native removal failed; its state cannot be assumed intact. Native escrow retained; no replacement spawned");
                    }
                }
                // An accepted SpawnEntity without a canonical index entry has
                // an uncertain outcome. Never guess that it failed and spawn a
                // second native inventory. Keep escrow and require inspection.
                if (removed && spawnAttempted && replacement != null && !ReferenceEquals(serverApi!.World.GetEntityById(replacement.EntityId), replacement))
                {
                    r.DeveloperRecoveryNativeEscrow = snapshot; packRepository!.Save();
                    throw new InvalidOperationException("Replacement publication/cancellation cannot be proven; no rollback body was spawned. Native escrow is retained");
                }
                if (replacement != null && ReferenceEquals(serverApi!.World.GetEntityById(replacement.EntityId), replacement))
                    serverApi.World.DespawnEntity(replacement, new EntityDespawnData { Reason = EnumDespawnReason.Removed });
                if (replacement != null) { UnregisterLoadedFox(replacement, "Failed full rebuild"); ForgetRetainedAdmissionBody(replacement); }
                packRepository!.RestoreRecord(saved);
                if (removed && snapshot.Length > 0)
                {
                    saved.DeveloperRecoveryNativeEscrow = snapshot;
                    packRepository.RestoreRecord(saved); packRepository.Save();
                    if (serverApi!.World.GetEntityById(saved.EntityId) != null) throw new InvalidOperationException("Native ID occupied; rollback snapshot retained without spawning another body");
                    Entity rollback = ReconstructRecoveryNative(original!.Properties, snapshot);
                    serverApi.World.SpawnEntity(rollback);
                    if (!NativeRecoveryPublished(rollback)) throw new InvalidOperationException("Rollback native publication is pending; escrow retained. Inspect before retrying");
                    saved.DeveloperRecoveryNativeEscrow = Array.Empty<byte>();
                    packRepository.RestoreRecord(saved);
                }
                packRepository.Save();
                Finish("failed", "Rebuild failed; original native state/ledger restored. " + error.Message);
            }
            catch (Exception rollbackError)
            {
                serverApi?.Logger.Error("[FeralKinshipCompanions] Recovery rollback unresolved for {0}: {1}", foxId, rollbackError);
                Finish("failed", "Rebuild/rollback unresolved; native escrow retained in the ledger. Do not repeat until /companiondiag is inspected. " + rollbackError.Message);
            }
            serverApi?.Logger.Error("[FeralKinshipCompanions] Full rebuild failed for {0}: {1}", foxId, error);
        }
        int checks = 0;
        void CheckPublication()
        {
            if (finishing || serverApi == null || replacement == null) return;
            if (ForceSuperseded(r, forceEpoch)) { GuardForceEmergency(replacement); Finish("skipped", "Old publication superseded by confirmed force reconstruction; original escrow retained"); return; }
            try
            {
                if(completedHoldRecovery) {
                    var authority=CaptureExpeditionPresentationAuthority();
                    if(authority==null||authority.Assigned.Contains(foxId)||authority.Conflicts.Contains(foxId)||authority.NativeConflicts.Contains(saved.EntityId)
                        ||!DeveloperMayControlOwner(actor,ownerUid)||(!actor.HasPrivilege(Privilege.controlserver)&&!actor.HasPrivilege(Privilege.root))
                        ||!packRepository!.TryGetRecord(foxId,out var current)||!ReferenceEquals(current,r)
                        ||!string.IsNullOrEmpty(r.PendingDepartureStatus)||r.PendingDepartureAtUtcMs>0||r.PendingDepartureDeadlineUtcMs>0
                        ||!string.IsNullOrEmpty(r.PendingReturnStatus)||r.PendingReturnAtUtcMs>0) {
                        // Do not roll an independently changed journey/ledger back to
                        // the pre-recovery record. Protect the one replacement and
                        // preserve the original escrow; never spawn another body.
                        RetainedNativeSuspension.Hold(replacement,"Manual recovery authority changed during publication; original escrow retained");
                        if(packRepository.TryGetRecord(foxId,out var retained)&&retained!=null) {
                            if(retained.DeveloperRecoveryNativeEscrow.Length==0)retained.DeveloperRecoveryNativeEscrow=snapshot;
                            try {packRepository.Save();}catch(Exception storeError){serverApi.Logger.Error("[CompanionRepair] Changed-authority escrow checkpoint unconfirmed: {0}",storeError);}
                        }
                        Finish("failed","Authority changed during native publication. Current journey/ledger preserved; replacement held and original native escrow retained. Do not repeat; collect /companiondiag.");return;
                    }
                }
                if (!NativeRecoveryPublished(replacement))
                {
                    if (++checks >= 20) throw new InvalidOperationException("Replacement did not enter both native indexes within 2 seconds");
                    serverApi.Event.RegisterCallback(_ => CheckPublication(), 100); return;
                }
                var health = replacement.GetBehavior<Vintagestory.GameContent.EntityBehaviorHealth>();
                if (health != null) { health.Health = manualRevival ? health.MaxHealth : Math.Clamp(originalHealth, 0, health.MaxHealth); health.MarkDirty(); }
                if (manualRevival)
                {
                    var status = GetDomesticationStatus(replacement)!;
                    if (!status.HasAttribute(FoxPointsKey) && r.DurableStateInitialized) RestoreArchivedProgress(replacement, r);
                    if (!r.IsJuvenile) { status.SetBool(AdulthoodExperienceGrantedKey, true); r.AdulthoodExperienceGranted = true; }
                    replacement.AnimManager?.StopAnimation("die");
                    replacement.AnimManager?.StopAnimation("wounded-idle"); replacement.AnimManager?.StopAnimation("wounded-resthead");
                }
                if (!replacement.Alive || replacement.ShouldDespawn || health?.Health is not > 0)
                    throw new InvalidOperationException("Replacement is not a living native animal");
                if (RetainedNativeSuspension.Marker(replacement) != null || GetDomesticationStatus(replacement)?.GetBool(ExpeditionAwayKey) == true)
                    throw new InvalidOperationException("Replacement retained stale expedition suppression");
                RetainedNativeSuspension.AdmitPresent(replacement);
                replacement.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.RefreshExpeditionSuspension();
                replacement.State = EnumEntityState.Active;
                replacement.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.TryTakeInitializedHealth(out _, out _);
                retainedLoadedHealthApplied.Add(replacement);
                ledgerHydratedEntities.Add(replacement.EntityId);
                RegisterLoadedFox(replacement);
                if (!loadedFoxes.TryGetValue(replacement.EntityId, out Entity? registered) || !ReferenceEquals(registered, replacement)
                    || RetainedNativeSuspension.Protected(replacement) || replacement.CollisionBox?.XSize is not > 0 || replacement.SelectionBox?.XSize is not > 0)
                    throw new InvalidOperationException("Replacement native/Companions registration or presentation is still suppressed");
                if (r.Archived && !packRepository!.UnarchiveRecord(r.OwnerUid, r.FoxId)) throw new InvalidOperationException("Archive authority changed during rebuild");
                r.EntityId = replacement.EntityId; r.Archived = false; r.ManualArchiveRecordRestored = true;
                r.Status = IsFoxMortallyWounded(replacement) ? "Mortally wounded" : replacement.WatchedAttributes.GetInt(EntityHealthStateKey) == RecoveringHealthState ? "Recovering" : "Present";
                r.StatusExpiresDay = 0; r.CurrentHealth = health!.Health; r.MaxHealth = health.MaxHealth; r.HealthState = replacement.WatchedAttributes.GetInt(EntityHealthStateKey);
                r.DeveloperRecoveryNativeEscrow = Array.Empty<byte>();
                packRepository!.Save();
                try { SendState(replacement, actor, "Server rebuild committed; use /companiondiag to verify client arrival/rendering."); }
                catch (Exception error) { serverApi.Logger.Warning("[FeralKinshipCompanions] Rebuild committed but client state delivery failed: {0}", error.Message); }
                Finish("recovered", "Server rebuild committed with a fresh native body. Identity, progression and native inventory retained; client arrival/rendering needs /companiondiag verification");
            }
            catch (Exception error) { Fail(error); }
        }
        try
        {
            snapshot = SnapshotRecoveryNative(original!);
            replacement = ReconstructRecoveryNative(original!.Properties, snapshot); // Prove factory/deserializer before removal.
            if(completedHoldRecovery) {
                PrepareCompletedHoldReplacement(replacement,original,r);
                r.CompletedHoldRecoveryNativeEscrow=snapshot;
                r.CompletedHoldRecoveryLedgerEscrow=HoldRecoveryRecordBytes(saved);
                r.CompletedHoldRecoveryAudit="Confirmed completed-Hold recovery by "+actor.PlayerUID+" at "+DateTime.UtcNow.ToString("O")
                    +"; native="+saved.EntityId+"; trip="+saved.RetainedTrip+"; outcomeApplied="+RetainedNativeSuspension.Marker(original)?.GetBool("outcomeApplied")
                    +"; outcomes/cargo not replayed; native SHA256="+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(snapshot));
            }
            SanitizeCompletedRecovery(replacement, r);
            if (manualRevival)
            {
                replacement.Alive = true;
                replacement.WatchedAttributes.SetInt("healthState", 0);
                replacement.WatchedAttributes.SetDouble("mortallyWoundedTotalHours", 0);
                replacement.WatchedAttributes.SetFloat("regenSpeed", 1);
                var status = GetDomesticationStatus(replacement)!;
                status.SetInt(StabilizedWindowAppliedRankKey, 0);
                status.SetDouble(StabilizedWindowAdjustedStartHoursKey, -1);
                status.SetDouble(DeathlessRecoveryAtHoursKey, -1);
                status.SetDouble(DeathlessFallbackAtHoursKey, -1);
                r.HealthState = 0; r.MortallyWoundedStartHours = 0;
                r.StabilizedWindowAppliedRank = 0; r.StabilizedWindowAdjustedStartHours = -1;
                r.DeathlessRecoveryAtHours = r.DeathlessFallbackAtHours = -1;
            }
            r.DeveloperRecoveryNativeEscrow = snapshot; packRepository.Save();
            rebuildingEntityIds.Add(original.EntityId);
            removalAttempted = true;
            serverApi.World.DespawnEntity(original, new EntityDespawnData { Reason = EnumDespawnReason.Removed });
            removed = !ReferenceEquals(serverApi.World.GetEntityById(original.EntityId), original);
            if (!removed || serverApi.World.GetEntityById(original.EntityId) != null) throw new InvalidOperationException("Original native removal was not confirmed; replacement not spawned");
            UnregisterLoadedFox(original, "Full native recovery handoff"); ForgetRetainedAdmissionBody(original);
            SanitizeCompletedRecovery(replacement, r);
            ledgerHydratedEntities.Add(replacement.EntityId); // Native snapshot is authoritative; never replay saved backpack/cargo at spawn.
            RetainedNativeSuspension.DeferAdmission(replacement);
            spawnAttempted = true;
            serverApi.World.SpawnEntity(replacement); // Preserve the native ID; no second inventory identity.
            CheckPublication();
            if (!finishing && completed == null) SendCompanionRecoveryState(actor, GetFoxRecordLabel(saved) + ": rebuild pending native publication; do not repeat");
        }
        catch (Exception error) { Fail(error); }
    }

    private void StartPlayerRecoveryBatch(IServerPlayer actor, string targetOwner)
    {
        if (!DeveloperMayControlOwner(actor, targetOwner)) return;
        if (!pendingRecoveryBatches.Add(targetOwner)) { SendCompanionRecoveryState(actor, "A recovery batch for this player is already pending"); return; }
        var ids = packRepository!.GetAllRecords().Where(r => r.OwnerUid == targetOwner).Select(r => r.FoxId).Distinct().Take(8193).ToArray();
        if (ids.Length > 8192) { pendingRecoveryBatches.Remove(targetOwner); SendCompanionRecoveryState(actor, "Roster exceeds recovery safety bound; select individual companions"); return; }
        int index = 0, recovered = 0, skipped = 0, failed = 0;
        var reasons = new List<string>();
        void Next()
        {
            if (serverApi == null) { pendingRecoveryBatches.Remove(targetOwner); return; }
            if (!DeveloperMayControlOwner(actor, targetOwner)) { skipped += ids.Length - index; reasons.Add("Target authorization expired; select the cart again"); index = ids.Length; }
            if (index == ids.Length)
            {
                pendingRecoveryBatches.Remove(targetOwner);
                string result = $"Player {targetOwner}: {recovered} rebuilt, {skipped} skipped, {failed} failed. " + string.Join("; ", reasons.Take(4));
                SendCompanionRecoveryState(actor, result);
                SendAdminCompanionMessage(actor, result);
                foreach (string reason in reasons.Take(12)) SendAdminCompanionMessage(actor, reason);
                if (reasons.Count > 12) SendAdminCompanionMessage(actor, $"{reasons.Count - 12} additional skipped/failed animals; inspect their recovery ledger rows for the current reasons");
                return;
            }
            string id = ids[index++];
            StartFullCompanionRecovery(actor, id, targetOwner, result =>
            {
                if (result.Kind == "recovered") recovered++; else if (result.Kind == "failed") failed++; else skipped++;
                if (result.Kind != "recovered") reasons.Add(result.Reason);
                serverApi?.Event.RegisterCallback(_ => Next(), 50); // One animal per callback; failures are isolated.
            });
        }
        SendCompanionRecoveryState(actor, $"Recovering {ids.Length} companions for player {targetOwner}; results pending");
        serverApi?.Event.RegisterCallback(_ => Next(), 50);
    }
}
