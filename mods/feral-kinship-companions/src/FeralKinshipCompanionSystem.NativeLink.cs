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
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;
public sealed partial class FeralKinshipCompanionSystem
{
    private sealed record NativeLinkEvidence(Entity? Body, string Blocker, string Sources);
    private sealed record NativeLinkConfirmation(IServerPlayer Actor, string Token, string Companion, string Owner, Entity Body, string Fingerprint, bool Undo, long Until);
    private readonly Dictionary<string, NativeLinkConfirmation> nativeLinkConfirmations = new(StringComparer.Ordinal);
    private readonly Queue<string> nativeLinkWake = new();
    private readonly HashSet<string> nativeLinkQueued = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> nativeLinkRetry = new(StringComparer.Ordinal);
    private readonly HashSet<(string Companion, long Saved, long Actual)> nativeLinkObserved = new();
    private readonly Dictionary<string, (int X, int Z)> nativeLinkSourceColumns = new(StringComparer.Ordinal);
    private long nextNativeLinkScan;
    private int nativeLinkCursor;
    private static NativeLinkRepair? LastNativeLink(FoxPackRecordV2 r) => r.NativeLinks.LastOrDefault();
    private static bool NativeLinkPending(FoxPackRecordV2 r) => LastNativeLink(r)?.Stage is "Prepared" or "UndoPrepared";
    private static bool NativeLinkPresentationReady(Entity e)
    {
        var social = e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>();
        return e.State == EnumEntityState.Active && e.CollisionBox?.XSize > 0 && e.SelectionBox?.XSize > 0 && social?.HasExpeditionPresentationSuppression != true
            || (e.State is EnumEntityState.Active or EnumEntityState.Inactive) && social?.CanResumeNativeLinkAdmission == true;
    }

    private static byte[] NativeLinkLedger(FoxPackRecordV2 r)
    {
        var copy = Serializer.DeepClone(r); copy.NativeLinks.Clear();
        using var stream = new MemoryStream(); Serializer.Serialize(stream, copy);
        if (stream.Length == 0 || stream.Length > 1024 * 1024) throw new InvalidOperationException("Relink ledger snapshot exceeds 1 MiB; unchanged");
        return stream.ToArray();
    }
    private static FoxPackRecordV2 ReadNativeLinkLedger(NativeLinkRepair j)
    {
        if (j.Version != 1 || j.Id.Length == 0 || j.OldId <= 0 || j.NewId <= 0 || j.OldId == j.NewId
            || j.BeforeLedger.Length == 0 || j.BeforeLedger.Length > 1024 * 1024 || j.BeforeNative.Length == 0 || j.BeforeNative.Length > 8 * 1024 * 1024
            || EmergencyHash(j.BeforeLedger) != j.LedgerHash || EmergencyHash(j.BeforeNative) != j.NativeHash)
            throw new InvalidOperationException("Relink snapshot/journal is invalid; preserve both identities");
        using var stream = new MemoryStream(j.BeforeLedger, false);
        var before = Serializer.Deserialize<FoxPackRecordV2>(stream);
        if (stream.Position != stream.Length || before.NativeLinks.Count != 0 || before.EntityId != j.OldId)
            throw new InvalidOperationException("Relink ledger snapshot identity is invalid");
        return before;
    }
    // No world-wide absence claim: every recorded source must be loaded. The
    // superseded reference is quarantined durably in case its body loads later.
    private NativeLinkEvidence InspectNativeLink(FoxPackRecordV2 r, NativeLinkRepair? operation = null, bool allowOwnPreview = false)
    {
        NativeLinkEvidence Stop(string reason, Entity? body = null, string sources = "") => new(body, reason, sources);
        if (serverApi == null || packRepository?.CanCheckpointRecovery != true || !companionContentReady
            || RuntimeEnv.ServerMainThreadId <= 0 || Environment.CurrentManagedThreadId != RuntimeEnv.ServerMainThreadId)
            return Stop("Initialized server main thread and immediate checkpoint required");
        if (string.IsNullOrWhiteSpace(r.OwnerUid) || string.IsNullOrWhiteSpace(r.FoxId) || r.EntityId <= 0
            || !packRepository.TryGetRecord(r.FoxId, out var canonical) || !ReferenceEquals(r, canonical)) return Stop("Canonical saved identity is unavailable");
        long old = operation?.OldId ?? r.EntityId;
        if (operation != null) {
            var before = ReadNativeLinkLedger(operation);
            if (before.FoxId != r.FoxId || before.OwnerUid != r.OwnerUid || before.EntityCode != r.EntityCode
                || r.EntityId != old && r.EntityId != operation.NewId) return Stop("Journal and current ledger authority differ");
        }
        if (r.Archived || r.MissingContentArchive?.Active == true || packRepository.IsPermanentlyDeleted(r.FoxId)
            || r.Status is "Dead" or "Unowned" or "MIA" or "Running late" or "Returning" or "Awaiting safe return"
            || r.Status.StartsWith("Away", StringComparison.OrdinalIgnoreCase) || r.Status.StartsWith("Leaving", StringComparison.OrdinalIgnoreCase)) return Stop("Archived, dead, missing or journey record; manual investigation required");
        if (InspectSavedRecoveryCode(r, out string registration) == null) return Stop("Exact saved code registration is " + registration);
        if (r.RetainedTrip.Length > 0 || r.RetainedPhase.Length > 0 || r.CompletionProof != null
            || r.MissingRecoveryState.Length > 0 || r.MissingRecoveryNativeCheckpoint.Length > 0 || r.MissingRecoveryLedgerCheckpoint.Length > 0
            || r.DeveloperRecoveryNativeEscrow.Length > 0 || r.CompletedHoldRecoveryNativeEscrow.Length > 0 || r.CompletedHoldRecoveryLedgerEscrow.Length > 0
            || r.CompletedHoldRecoveryAudit.Length > 0 || r.EmergencyRebuilds.Count > 0
            || r.SaveMigration is { } migration && (migration.Version != 1 || migration.NativeEscrow.Length > 0 || migration.LedgerEscrow.Length > 0
                || migration.Trip.Length > 0 || migration.Token.Length > 0 || migration.NativeId > 0 || migration.State == "Prepared"))
            return Stop("Existing recovery/Undo, retained receipt or checkpoint authority requires manual investigation; no receipt IDs are rewritten");
        if (r.NativeLinks.Any(j => j.Version != 1 || j != operation && j.Stage is not ("Linked" or "Undone"))) return Stop("Another or unknown relink journal is pending");
        var authority = CaptureExpeditionPresentationAuthority();
        if (authority == null || authority.Conflicts.Contains(r.FoxId) || authority.NativeConflicts.Contains(old) || authority.Assigned.Contains(r.FoxId)) return Stop("Unknown, duplicate or assigned expedition authority");
        if (pendingFullRebuilds.Contains(r.FoxId) || pendingRecoveryBatches.Contains(r.OwnerUid) || pendingRecoveryChecks.Contains(r.FoxId)
            || pendingAdminCompanionTeleports.Contains(r.OwnerUid + ":" + r.FoxId)
            || holdRecoveryConfirmations.Values.Any(c => c.FoxId == r.FoxId)
            || recoveryUiConfirmations.Values.Any(c => c.Targets.Any(t => t.FoxId == r.FoxId))
            || !allowOwnPreview && nativeLinkConfirmations.Values.Any(c => c.Companion == r.FoxId)
            || r.PendingDepartureStatus.Length > 0 || r.PendingDepartureAtUtcMs > 0 || r.PendingDepartureDeadlineUtcMs > 0 || r.PendingReturnStatus.Length > 0 || r.PendingReturnAtUtcMs > 0)
            return Stop("Pending recovery, confirmation, teleport or journey; wait for its outcome");
        if (!r.HasLastKnownPosition || r.LastKnownDimension != 0 || !double.IsFinite(r.LastKnownX) || !double.IsFinite(r.LastKnownY) || !double.IsFinite(r.LastKnownZ)
            || Math.Abs(r.LastKnownX) > int.MaxValue - 1 || Math.Abs(r.LastKnownZ) > int.MaxValue - 1 || r.LastKnownY < 0 || r.LastKnownY >= serverApi.WorldManager.MapSizeY)
            return Stop("Saved source location is unavailable/unverified; no absence is inferred");
        int x = (int)Math.Floor(r.LastKnownX / (double)GlobalConstants.ChunkSize), z = (int)Math.Floor(r.LastKnownZ / (double)GlobalConstants.ChunkSize);
        if ((companionConfig.EnableAutomaticNativeIdRelink || NativeLinkPending(r)) && nativeLinkSourceColumns.Count < 8192) nativeLinkSourceColumns[r.FoxId] = (x, z);
        int height = serverApi.WorldManager.MapSizeY;
        if (height <= 0 || height > 8192) return Stop("Saved source column bounds are unknown");
        for (int y = 0; y < (height + GlobalConstants.ChunkSize - 1) / GlobalConstants.ChunkSize; y++) {
            var chunk = serverApi.WorldManager.GetChunk(x, y, z);
            if (chunk == null || chunk.Disposed) return Stop("Saved source column is unloaded/unverified; unknown old-ID evidence, no force-load attempted");
        }
        if (serverApi.World.LoadedEntities.Count > 16384 || serverApi.WorldManager.AllLoadedChunks.Count > 4096)
            return Stop("Loaded identity evidence exceeds the bounded relink scan; manual investigation required");
        var view = new FoxPackRecordV2 { FoxId = r.FoxId, OwnerUid = r.OwnerUid, EntityId = old, EntityCode = r.EntityCode };
        var lookup = LookupRecoveryIdentity(view, true);
        if (lookup.State == "Unavailable" || lookup.Candidates.Length != 1) return Stop("Unique loaded body not verified: " + lookup.State + "; " + lookup.Reason);
        Entity e = lookup.Candidates[0].Body; string sources = lookup.Candidates[0].Source;
        if (e.EntityId == old && operation == null) return Stop("Healthy native reference; no repair required", e, sources);
        if (serverApi.World.GetEntityById(old) != null || serverApi.World.LoadedEntities.ContainsKey(old) || retainedAdmissionBodies.ContainsKey(old) || loadedFoxes.ContainsKey(old))
            return Stop("Old native ID is still occupied or aliased in a lookup index; preserve both references", e, sources);
        if (e.EntityId <= 0 || e.EntityId == old || operation != null && e.EntityId != operation.NewId) return Stop("Candidate native ID differs from the approved relink", e, sources);
        if (GetCompanionOwnerUid(e) != r.OwnerUid || GetDomesticationStatus(e)?.GetString(FoxIdKey) != r.FoxId || !ExactRecoveryCodeEquals(r.EntityCode, e.Code))
            return Stop("Owner, stable companion ID or exact code mismatch; no relink", e, sources);
        var social = e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>();
        if (!IsTamedFox(e) || !e.Alive || e.ShouldDespawn || e.Teleporting || !NativeLinkPresentationReady(e) || e is not EntityAgent agent || agent.MountedOn != null
            || !ReferenceEquals(e.Api, serverApi) || !ReferenceEquals(e.World, serverApi.World) || e.Pos.Dimension != 0 || !NativeRecoveryPublished(e))
            return Stop("Candidate native publication/living/unmounted state is unavailable: " + RecoveryPublicationReason(e), e, sources);
        var health = e.GetBehavior<EntityBehaviorHealth>();
        if (health == null || !float.IsFinite(health.Health) || health.Health <= 0 || !float.IsFinite(health.MaxHealth) || health.MaxHealth <= 0)
            return Stop("Native health is unavailable or invalid; no inferred resurrection/healing", e, sources);
        if (RetainedNativeSuspension.Marker(e) != null || GetDomesticationStatus(e)?.GetBool(ExpeditionAwayKey) == true || e.WatchedAttributes.HasAttribute(EmergencyBodyKey))
            return Stop("Candidate has retained/Emergency native authority; preserve it for manual investigation", e, sources);
        if (EmergencyBodyBlocker(e).Length > 0 || r.BackpackDeliveryActive || backpackPreflights.ContainsKey(old) || backpackPreflights.ContainsKey(e.EntityId)
            || backpackStorageAreaLoads.Contains(r.FoxId) || backpackMissingEntityRecoveryChecks.Contains(r.FoxId)) return Stop("Native cargo, mount or delivery work is pending", e, sources);
        var rows = packRepository.GetAllRecords(); if (rows.Count > 8192) return Stop("Ledger exceeds bounded identity scan", e, sources);
        if (authority.NativeConflicts.Contains(e.EntityId)) return Stop("Candidate has conflicting ledger native authority", e, sources);
        long checkpointBudget = 4 * 1024 * 1024;
        foreach (var other in rows) {
            bool self = ReferenceEquals(other, r);
            if (!self && (other.FoxId == r.FoxId || other.EntityId == old || other.EntityId == e.EntityId)) return Stop("Another record occupies the companion/old/new native identity", e, sources);
            if (other.RecoveryRetiredNativeIds.Contains(e.EntityId) || other.RecoveryRetiredNativeIds.Contains(old) && !(self && operation != null))
                return Stop("Old or candidate ID belongs to a retired generation", e, sources);
            if (!self && (other.CompletionProof?.NativeId == old || other.CompletionProof?.NativeId == e.EntityId || other.SaveMigration?.NativeId == old || other.SaveMigration?.NativeId == e.EntityId
                || other.EmergencyRebuilds.Any(j => new[] { j.OriginalId, j.TargetId, j.UndoTargetId, j.UndoDisplacedId }.Any(id => id == old || id == e.EntityId) || j.GenerationIds.Contains(old) || j.GenerationIds.Contains(e.EntityId) || j.RetiredIds.Contains(old) || j.RetiredIds.Contains(e.EntityId))
                || other.NativeLinks.Any(j => j.OldId == old || j.NewId == old || j.OldId == e.EntityId || j.NewId == e.EntityId))) return Stop("Another journal/checkpoint claims old or candidate native ID", e, sources);
            // Opaque native snapshots cannot safely be searched or reinterpreted.
            if (!self && (other.MissingRecoveryState == "Prepared" || EmergencyPending(LastEmergency(other)) || NativeLinkPending(other))) return Stop("Another interrupted publication has unverified generation authority", e, sources);
            if (!self && (other.SaveMigration is { Version: not 1 } || other.EmergencyRebuilds.Any(j => j.Version != 1)
                || other.NativeLinks.Any(j => j.Version != 1 || j.Stage is not ("Prepared" or "Linked" or "UndoPrepared" or "Undone")))) return Stop("Another record has unknown checkpoint/journal authority", e, sources);
            if (!self) {
                string checkpoint = NativeLinkCheckpointConflict(other, old, e.EntityId, ref checkpointBudget);
                if (checkpoint.Length > 0) return Stop(checkpoint, e, sources);
            }
        }
        return new(e, string.Empty, sources + $"; admission-presentation={(social?.CanResumeNativeLinkAdmission == true ? "verified-local-load-latch" : "active-native")}; saved-source-column={x},{z}/all-loaded; old-id={old}:not-observed-in-loaded-sources; unloaded-world=unknown; policy=retain-and-quarantine-late-old");
    }

    private static string NativeLinkCheckpointConflict(FoxPackRecordV2 other, long old, long current, ref long budget)
    {
        var bytes = new[] { other.MissingRecoveryLedgerCheckpoint, other.CompletedHoldRecoveryLedgerEscrow, other.SaveMigration?.LedgerEscrow ?? Array.Empty<byte>() }
            .Concat(other.EmergencyRebuilds.SelectMany(j => new[] { j.BeforeLedger, j.UndoDisplacedLedger }))
            .Concat(other.NativeLinks.SelectMany(j => new[] { j.BeforeLedger, j.BeforeUndo })).Where(b => b.Length > 0).ToArray();
        long size = bytes.Sum(b => (long)b.Length);
        if (size > budget) return "Other checkpoint evidence exceeds bounded inspection; unknown identity authority";
        budget -= size;
        if (bytes.Length == 0 && (other.DeveloperRecoveryNativeEscrow.Length > 0 || other.SaveMigration?.NativeEscrow.Length > 0 || other.CompletedHoldRecoveryNativeEscrow.Length > 0 || other.MissingRecoveryNativeCheckpoint.Length > 0))
            return "Unpaired native checkpoint on another record has unverified identity authority";
        foreach (var data in bytes) {
            if (data.Length > 1024 * 1024) return "Other ledger checkpoint exceeds bounded inspection; identity unknown";
            try {
                using var stream = new MemoryStream(data, false); var saved = Serializer.Deserialize<FoxPackRecordV2>(stream);
                if (stream.Position != stream.Length || saved.EntityId <= 0) return "Other checkpoint identity cannot be verified";
                if (saved.EntityId == old || saved.EntityId == current || saved.RecoveryRetiredNativeIds.Contains(old) || saved.RecoveryRetiredNativeIds.Contains(current)) return "Other record's checkpoint claims old/new identity";
            } catch { return "Other checkpoint is unreadable; identity authority remains unknown"; }
        }
        return string.Empty;
    }

    private void CheckpointNativeLink(FoxPackRecordV2 r, NativeLinkRepair j)
    {
        if (packRepository?.CanCheckpointRecovery != true) throw new InvalidOperationException("Relink checkpoint unavailable");
        ReadNativeLinkLedger(j);
        if (r.NativeLinks.Count > 4 || r.NativeLinks.Sum(q => (long)q.BeforeLedger.Length + q.BeforeNative.Length + q.BeforeUndo.Length) > 32 * 1024 * 1024)
            throw new InvalidOperationException("Relink history is full; snapshots are never overwritten");
        packRepository.Save();
    }
    private string BeginNativeLink(FoxPackRecordV2 r, string initiator)
    {
        var evidence = InspectNativeLink(r); if (evidence.Blocker.Length > 0) return evidence.Blocker;
        if (r.NativeLinks.Count >= 4) return "Four relink snapshots already retained; no snapshot will be overwritten";
        var e = evidence.Body!;
        var ledger = NativeLinkLedger(r); var native = SnapshotRecoveryNative(e);
        var j = new NativeLinkRepair { Id = Guid.NewGuid().ToString("N"), OldId = r.EntityId, NewId = e.EntityId,
            BeforeLedger = ledger, BeforeNative = native, LedgerHash = EmergencyHash(ledger), NativeHash = EmergencyHash(native), Evidence = evidence.Sources, Initiator = initiator };
        if (r.NativeLinks.Sum(q => (long)q.BeforeLedger.Length + q.BeforeNative.Length + q.BeforeUndo.Length) + ledger.Length + native.Length > 32 * 1024 * 1024)
            return "Relink snapshot capacity reached; existing history preserved, no operation added";
        r.NativeLinks.Add(j);
        // Even a write-after-error leaves Prepared: no reference changes until
        // a checkpoint is acknowledged. Retries never replace the first snapshot.
        try { CheckpointNativeLink(r, j); return AdvanceNativeLink(r); }
        catch (Exception ex) { j.Error = "Prepared checkpoint unconfirmed: " + ex.GetType().Name; LogNativeLink(r, j, j.Error); return j.Error; }
    }
    private string AdvanceNativeLink(FoxPackRecordV2 r)
    {
        var j = LastNativeLink(r); if (j == null) return "No relink journal";
        if (j.Stage is "Linked" or "Undone") return "Already " + j.Stage + "; no write or native mutation";
        if (j.Stage is not ("Prepared" or "UndoPrepared")) return "Unknown relink phase; manual investigation required";
        try {
            var before = ReadNativeLinkLedger(j);
            var evidence = InspectNativeLink(r, j); if (evidence.Blocker.Length > 0) return j.Error = evidence.Blocker;
            if (j.Stage == "UndoPrepared" && (j.BeforeUndo.Length == 0 || EmergencyHash(j.BeforeUndo) != j.UndoHash)) return j.Error = "Undo snapshot is invalid; unchanged";
            CheckpointNativeLink(r, j); // Persist intent before either reference change.
            bool undo = j.Stage == "UndoPrepared";
            r.EntityId = undo ? j.OldId : j.NewId;
            r.RecoveryRetiredNativeIds = undo ? before.RecoveryRetiredNativeIds : r.RecoveryRetiredNativeIds.Append(j.OldId).Distinct().ToArray();
            string pending = j.Stage; j.Stage = undo ? "Undone" : "Linked"; j.Error = string.Empty;
            try { CheckpointNativeLink(r, j); }
            catch { j.Stage = pending; throw; }
            if (!undo) PublishNativeLinkCache(evidence.Body!);
            nativeLinkSourceColumns.Remove(r.FoxId);
            nativeLinkRetry.Remove(r.FoxId);
            LogNativeLink(r, j, "committed; existing native body/inventory/health/progression unchanged");
            return j.Stage + $": ledger {j.OldId} -> {j.NewId}; " + (undo ? "ledger reference restored; current body/state retained, automatic retry disabled" : "same native body retained; no respawn or inventory replay");
        } catch (Exception ex) { j.Error = "Resumable " + j.Stage + ": " + ex.GetType().Name; LogNativeLink(r, j, j.Error); return j.Error; }
    }
    private string BeginNativeLinkUndo(FoxPackRecordV2 r)
    {
        var j = LastNativeLink(r); if (j?.Stage != "Linked") return "No completed relink to undo";
        var evidence = InspectNativeLink(r, j); if (evidence.Blocker.Length > 0) return evidence.Blocker;
        byte[] undo = NativeLinkLedger(r);
        if (r.NativeLinks.Sum(q => (long)q.BeforeLedger.Length + q.BeforeNative.Length + q.BeforeUndo.Length) - j.BeforeUndo.Length + undo.Length > 32 * 1024 * 1024)
            return "Relink Undo snapshot capacity reached; existing history preserved";
        j.BeforeUndo = undo; j.UndoHash = EmergencyHash(j.BeforeUndo); j.Stage = "UndoPrepared";
        try { CheckpointNativeLink(r, j); return AdvanceNativeLink(r); }
        catch (Exception ex) { j.Error = "Undo intent checkpoint unconfirmed: " + ex.GetType().Name; LogNativeLink(r, j, j.Error); return j.Error; }
    }
    private void LogNativeLink(FoxPackRecordV2 r, NativeLinkRepair j, string outcome)
        => serverApi?.Logger.Notification("[CompanionLink] companion={0} owner={1} operation={2} old-native={3} new-native={4} phase={5} initiator={6} evidence={7} outcome={8}", r.FoxId, r.OwnerUid, j.Id, j.OldId, j.NewId, j.Stage, j.Initiator, j.Evidence, outcome);

    private void WakeNativeLink(Entity e)
    {
        string id = GetDomesticationStatus(e)?.GetString(FoxIdKey) ?? string.Empty;
        if (packRepository?.TryGetRecord(id, out var row) == true && row != null && row.EntityId == e.EntityId && !NativeLinkPending(row)) return;
        if (id.Length > 0 && nativeLinkQueued.Count < 8192 && nativeLinkQueued.Add(id)) nativeLinkWake.Enqueue(id);
    }
    private void PublishNativeLinkCache(Entity e)
    {
        RetainedNativeSuspension.AdmitPresent(e);
        e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.RefreshExpeditionSuspension();
        ledgerHydratedEntities.Add(e.EntityId); retainedLoadedHealthApplied.Add(e);
        loadedFoxes[e.EntityId] = e; retainedAdmissionBodies[e.EntityId] = e;
        ForgetPendingCompanionAdmission(e);
        if (!refreshingRosterPresence) RefreshVerifiedCompanionPresence(e, CaptureExpeditionPresentationAuthority());
    }
    private bool AdmitNativeLinkedLoad(Entity e)
    {
        string id = GetDomesticationStatus(e)?.GetString(FoxIdKey) ?? string.Empty;
        if (packRepository?.TryGetRecord(id, out var r) != true || r == null || LastNativeLink(r) is not { Stage: "Linked" } j || j.NewId != e.EntityId || r.EntityId != e.EntityId) return false;
        if (r.RetainedTrip.Length > 0 || RetainedNativeSuspension.Marker(e) != null || GetDomesticationStatus(e)?.GetBool(ExpeditionAwayKey) == true || EmergencyPending(LastEmergency(r))) return false;
        // Restart registers an existing body as-is, without running first-load
        // naming/number allocation, derived stats or stale ledger hydration.
        if (GetCompanionOwnerUid(e) == r.OwnerUid && ExactRecoveryCodeEquals(r.EntityCode, e.Code) && NativeRecoveryPublished(e) && NativeLinkPresentationReady(e) && e.Alive && !e.Teleporting && !e.ShouldDespawn) {
            try { ReadNativeLinkLedger(j); PublishNativeLinkCache(e); }
            catch { return true; }
        }
        return true;
    }
    private bool GuardNativeLinkAdmission(Entity e)
    {
        if (IsEmergencySelected(e)) return false;
        if (IsRetiredRecoveryOriginal(e)) return QuarantineRetiredRecoveryOriginal(e);
        string id = GetDomesticationStatus(e)?.GetString(FoxIdKey) ?? string.Empty;
        if (packRepository?.TryGetRecord(id, out var r) != true || r == null) return false;
        var j = LastNativeLink(r);
        if (j?.Stage == "Undone" && (j.NewId == e.EntityId || j.OldId == e.EntityId)) return true;
        if (j != null && j.NewId == e.EntityId) {
            if (j.Stage == "Linked" && r.EntityId == e.EntityId && GetCompanionOwnerUid(e) == r.OwnerUid && ExactRecoveryCodeEquals(r.EntityCode, e.Code)) {
                // The ledger snapshot is forensic, never an authority to hydrate
                // this already existing body on registration or after restart.
                ledgerHydratedEntities.Add(e.EntityId); retainedLoadedHealthApplied.Add(e);
            } else { WakeNativeLink(e); return true; }
        }
        if (r.EntityId > 0 && r.EntityId != e.EntityId && !VerifiedGrowthHandoff(e, out _)) {
            // Keep the existing competing-body admission guard. Observation is
            // not ledger registration and never hydrates this candidate.
            ObserveRetainedAdmission(e);
            if (nativeLinkObserved.Count < 8192 && nativeLinkObserved.Add((r.FoxId, r.EntityId, e.EntityId)))
                serverApi?.Logger.Notification("[CompanionLink] identity-drift-observed companion={0} saved-native={1} actual-native={2} owner-check={3} code-check={4} origin-of-divergence=unknown; no native state changed", r.FoxId, r.EntityId, e.EntityId, IdentityCheck(r.OwnerUid, GetCompanionOwnerUid(e)), IdentityCodeCheck(r, e));
            WakeNativeLink(e); return true;
        }
        return false;
    }
    private void MaintainNativeLinks()
    {
        if (serverApi == null || packRepository?.CanCheckpointRecovery != true || !companionContentReady
            || RuntimeEnv.ServerMainThreadId != Environment.CurrentManagedThreadId) return;
        long now = serverApi.World.ElapsedMilliseconds; if (now < nextNativeLinkScan) return; nextNativeLinkScan = now + 2000;
        var rows = packRepository.GetAllRecords(); if (rows.Count > 8192) return;
        if (nativeLinkRetry.Count >= 8192) {
            var known = rows.Select(r => r.FoxId).ToHashSet(StringComparer.Ordinal);
            foreach (string id in nativeLinkRetry.Keys.Where(id => !known.Contains(id)).ToArray()) nativeLinkRetry.Remove(id);
        }
        for (int i = 0; i < Math.Min(16, rows.Count); i++) {
            if (nativeLinkCursor >= rows.Count) nativeLinkCursor = 0;
            var r = rows[nativeLinkCursor++];
            // No persistent classifier/retry writes for healthy animals.
            if (!NativeLinkPending(r) && (!companionConfig.EnableAutomaticNativeIdRelink || LastNativeLink(r) != null
                || serverApi.World.GetEntityById(r.EntityId) is { } e && GetCompanionOwnerUid(e) == r.OwnerUid && GetDomesticationStatus(e)?.GetString(FoxIdKey) == r.FoxId && ExactRecoveryCodeEquals(r.EntityCode, e.Code))) continue;
            if (nativeLinkQueued.Count < 8192 && nativeLinkQueued.Add(r.FoxId)) nativeLinkWake.Enqueue(r.FoxId);
        }
        for (int i = 0; i < 2 && nativeLinkWake.Count > 0; i++) {
            string id = nativeLinkWake.Dequeue(); nativeLinkQueued.Remove(id);
            if (!packRepository.TryGetRecord(id, out var r) || r == null || nativeLinkRetry.GetValueOrDefault(id) > now) continue;
            bool pending = NativeLinkPending(r);
            if (!pending && (!companionConfig.EnableAutomaticNativeIdRelink || LastNativeLink(r) != null)) continue;
            if (!pending && serverApi.World.GetEntityById(r.EntityId) is { } healthy && GetCompanionOwnerUid(healthy) == r.OwnerUid && GetDomesticationStatus(healthy)?.GetString(FoxIdKey) == r.FoxId && ExactRecoveryCodeEquals(r.EntityCode, healthy.Code)) continue;
            if (nativeLinkRetry.Count >= 8192 && !nativeLinkRetry.ContainsKey(id)) continue;
            nativeLinkRetry[id] = now + 30000;
            try {
                string result = pending ? AdvanceNativeLink(r) : BeginNativeLink(r, "automatic/default-off-test-switch");
                if (!pending && r.NativeLinks.Count == 0) serverApi.Logger.Notification("[CompanionLink] companion={0} old-native={1} outcome=deferred/manual reason={2}", id, r.EntityId, result);
            } catch (Exception ex) { serverApi.Logger.Warning("[CompanionLink] companion={0} outcome=unknown/manual exception={1}; no unverified relink permitted", id, ex.GetType().Name); }
        }
    }
    private void RegisterNativeLinkCommands(ICoreServerAPI api)
    {
        api.ChatCommands.Create("companionadult").WithDescription("Admin: [cart] id:<companion-id>; confirm <token>. Recover one verified retained adult, preserving current points and separate cargo.")
            .RequiresPlayer().RequiresPrivilege(Privilege.controlserver).WithArgs(api.ChatCommands.Parsers.OptionalAll("selection"))
            .HandleWith(args => args.Caller.Player is IServerPlayer actor
                ? HandleAdultRecoveryCommand(actor, args.Parsers.Count > 0 ? ((string?)args[0] ?? "") : "")
                : TextCommandResult.Error("Connected admin required"));
        api.ChatCommands.Create("companionbody").WithDescription("Admin: [cart] id:<companion-id> [select <actual-native-id>]. Bind one existing Emergency generation without reconstruction.")
            .RequiresPlayer().RequiresPrivilege(Privilege.controlserver).WithArgs(api.ChatCommands.Parsers.OptionalAll("selection"))
            .HandleWith(args => args.Caller.Player is IServerPlayer actor ? HandleEmergencyBodyCommand(actor, args.Parsers.Count > 0 ? ((string?)args[0] ?? "") : "") : TextCommandResult.Error("Connected admin required"));
        api.Event.ChunkColumnLoaded += (position, _) => { int x = position.X, z = position.Y; api.Event.EnqueueMainThreadTask(() => WakeNativeLinkColumn(x, z), "companion-native-link-wake"); };
        api.ChatCommands.Create("companionlink").WithDescription("Admin: [cart] [status|undo] id:<stable-id>; confirm <token>. Relinks existing verified bodies only.")
            .RequiresPlayer().RequiresPrivilege(Privilege.controlserver).WithArgs(api.ChatCommands.Parsers.OptionalAll("selector-or-confirm"))
            .HandleWith(args => {
                if (args.Caller.Player is not IServerPlayer actor) return TextCommandResult.Error("Connected server admin required");
                try { return HandleNativeLinkCommand(actor, args.Parsers.Count > 0 ? ((string?)args[0] ?? "").Trim() : ""); }
                catch (Exception ex) { api.Logger.Warning("[CompanionLink] Command stopped: {0}; snapshots retained", ex.GetType().Name); return TextCommandResult.Error("Relink stopped: " + ex.GetType().Name + "; collect /companiondiag"); }
            });
    }
    private void WakeNativeLinkColumn(int x, int z)
    {
        foreach (string id in nativeLinkSourceColumns.Where(pair => pair.Value == (x, z)).Select(pair => pair.Key).Take(64).ToArray()) {
            nativeLinkRetry.Remove(id);
            if (nativeLinkQueued.Count < 8192 && nativeLinkQueued.Add(id)) nativeLinkWake.Enqueue(id);
        }
        nextNativeLinkScan = 0;
    }
    private TextCommandResult HandleNativeLinkCommand(IServerPlayer actor, string text)
    {
        if (serverApi == null || packRepository?.Loaded != true || actor.ConnectionState != EnumClientState.Playing || !MayRepairCompletedHold(actor)) return TextCommandResult.Error("Connected server admin required");
        long now = serverApi.World.ElapsedMilliseconds;
        if (text.StartsWith("confirm ", StringComparison.Ordinal)) {
            if (!nativeLinkConfirmations.Remove(actor.PlayerUID, out var c) || c.Token != text[8..].Trim() || !ReferenceEquals(c.Actor, actor) || now > c.Until
                || !DeveloperMayControlOwner(actor, c.Owner) || !packRepository.TryGetRecord(c.Companion, out var r) || r == null || r.OwnerUid != c.Owner)
                return TextCommandResult.Error("Relink confirmation expired, consumed or unauthorized; unchanged");
            var evidence = InspectNativeLink(r, c.Undo ? LastNativeLink(r) : null);
            if (evidence.Blocker.Length > 0 || !ReferenceEquals(evidence.Body, c.Body) || RecoveryUiFingerprint(r, c.Body) != c.Fingerprint)
                return TextCommandResult.Error("Relink evidence changed: " + evidence.Blocker + "; request a new preview");
            return TextCommandResult.Success(c.Undo ? BeginNativeLinkUndo(r) : BeginNativeLink(r, "admin:" + actor.PlayerUID));
        }
        nativeLinkConfirmations.Remove(actor.PlayerUID);
        foreach (string uid in nativeLinkConfirmations.Where(p => now > p.Value.Until).Select(p => p.Key).ToArray()) nativeLinkConfirmations.Remove(uid);
        bool cart = text.StartsWith("cart ", StringComparison.Ordinal); if (cart) text = text[5..].Trim();
        bool undo = text.StartsWith("undo ", StringComparison.Ordinal), status = text.StartsWith("status ", StringComparison.Ordinal);
        if (undo || status) text = text[(undo ? 5 : 7)..].Trim();
        string owner = cart ? DeveloperTargetOwnerUid(actor) : actor.PlayerUID;
        if (!text.StartsWith("id:", StringComparison.Ordinal) || !packRepository.TryGetRecord(text[3..], out var row) || row == null || row.OwnerUid != owner || !DeveloperMayControlOwner(actor, owner))
            return TextCommandResult.Error("Use /companionlink [cart] [status|undo] id:<stable-id> in your authorized owner scope");
        var last = LastNativeLink(row);
        if (status) return TextCommandResult.Success(DescribeNativeLink(row));
        if (NativeLinkPending(row)) return TextCommandResult.Success("Approved relink/Undo pending; bounded worker will recheck and resume. " + DescribeNativeLink(row));
        if (undo && last?.Stage != "Linked") return TextCommandResult.Error("No completed relink to undo");
        var proof = InspectNativeLink(row, undo ? last : null);
        if (proof.Blocker.Length > 0) return TextCommandResult.Error("Relink refused: " + proof.Blocker);
        if (nativeLinkConfirmations.Count >= 128) return TextCommandResult.Error("Confirmation limit reached");
        string token = Guid.NewGuid().ToString("N");
        nativeLinkConfirmations[actor.PlayerUID] = new(actor, token, row.FoxId, owner, proof.Body!, RecoveryUiFingerprint(row, proof.Body!), undo, now + 60000);
        return TextCommandResult.Success((undo ? "UNDO ledger reference only; current native body/cargo/state stays intact, leaving the previous mismatch. " : $"Relink ledger {row.EntityId} -> {proof.Body!.EntityId}; keep the same body and current inventory/health/progression. ")
            + "Pre-change snapshots retained; late old IDs quarantined. Evidence: " + proof.Sources + ". Confirm within 60 seconds: /companionlink confirm " + token);
    }
    private string DescribeNativeLink(FoxPackRecordV2 r)
    {
        var j = LastNativeLink(r); var evidence = InspectNativeLink(r, NativeLinkPending(r) || j?.Stage == "Linked" ? j : null, true);
        return $"saved-native={r.EntityId} relink-phase={j?.Stage ?? "none"} old-native={j?.OldId ?? r.EntityId} new-native={j?.NewId.ToString() ?? "unavailable"} automatic-enabled={companionConfig.EnableAutomaticNativeIdRelink} blocker={IdentityValue(evidence.Blocker)} journal-error={IdentityValue(j?.Error)} evidence={IdentityValue(evidence.Sources)}";
    }
}
