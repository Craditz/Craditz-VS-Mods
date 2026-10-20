#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private const string EmergencyBodyKey = "feralKinshipEmergencyOperation";
    private const string EmergencyQuarantineReason = "Emergency recovery quarantine; original cargo retained separately, never replayed";
    private const long EmergencyMaximumBytes = 64L * 1024 * 1024;
    private readonly HashSet<string> emergencyProcessing = new(StringComparer.Ordinal);
    private readonly HashSet<Entity> emergencyAdmissionAllowed = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Entity> emergencyQuarantining = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, long> emergencyCheckpointedRevisions = new(StringComparer.Ordinal);
    private sealed class EmergencyColumnProbe { internal int Acknowledged; }
    private readonly Dictionary<string, EmergencyColumnProbe> emergencyColumnLoads = new(StringComparer.Ordinal);
    private long nextEmergencyTick;
    private int emergencyTickCursor;

    private EntityProperties? ResolveEmergencyEntityType(FoxPackRecordV2 r, out string reason)
    {
        var type=InspectSavedRecoveryCode(r,out string status);
        reason=type==null?$"Exact saved entity code {IdentityValue(r.EntityCode)} is {status}; no rename or different species is substituted. Run /companiondiag for the saved-code report.":string.Empty;
        return type;
    }

    private string EmergencyBodyBlocker(Entity e)
    {
        if (e.Teleporting || e.State == EnumEntityState.Despawned || e is not EntityAgent agent || agent.MountedOn != null)
            return "Native body must be published, stationary and unmounted";
        if (HasFoxPendingCartPickup(e) || backpackPreflights.ContainsKey(e.EntityId)) return "Finish pending native cargo/pickup work first";
        return string.Empty;
    }

    private static EmergencyCompanionJournal? LastEmergency(FoxPackRecordV2 r) => r.EmergencyRebuilds.LastOrDefault();
    private static bool EmergencyPending(EmergencyCompanionJournal? j) => j != null && j.Stage is not ("Rebuilt" or "Undone" or "ForceFailed");
    private static string EmergencyHash(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    private static long EmergencyBytes(EmergencyCompanionJournal j) => (long)j.BeforeLedger.Length + j.BeforeNative.Length
        + j.UndoDisplacedLedger.Length + j.UndoSourceNative.Length + j.Cargo.Sum(x => (long)x.Native.Length + x.LedgerBackpack.Length);

    private static byte[] EmergencyLedgerBytes(FoxPackRecordV2 r)
    {
        var copy = ClonePackRecordForRebuild(r);
        copy.EmergencyRebuilds.Clear();
        using var stream = new MemoryStream(); Serializer.Serialize(stream, copy);
        if (stream.Length == 0 || stream.Length > 32 * 1024 * 1024) throw new InvalidOperationException("Emergency ledger checkpoint exceeds 32 MiB");
        return stream.ToArray();
    }
    private static FoxPackRecordV2 ReadEmergencyLedger(byte[] bytes, string hash)
    {
        if (bytes.Length == 0 || EmergencyHash(bytes) != hash) throw new InvalidOperationException("Emergency ledger checkpoint hash mismatch");
        using var stream = new MemoryStream(bytes, false); var r = Serializer.Deserialize<FoxPackRecordV2>(stream);
        if (stream.Position != stream.Length || r.EmergencyRebuilds.Count != 0) throw new InvalidOperationException("Invalid or recursive emergency ledger checkpoint");
        return r;
    }
    private void CheckpointEmergency(FoxPackRecordV2 r, EmergencyCompanionJournal j, string? stage = null)
    {
        if (packRepository?.CanCheckpointRecovery != true) throw new InvalidOperationException("Emergency checkpoint cannot enter an open save batch");
        if (r.EmergencyRebuilds.Count > 8 || r.EmergencyRebuilds.Sum(EmergencyBytes) > EmergencyMaximumBytes)
            throw new InvalidOperationException("Emergency history/escrow bound reached; all existing cargo/checkpoints retained");
        if (j.Version != 1 || j.BeforeLedgerHash != EmergencyHash(j.BeforeLedger)
            || j.BeforeNativeHash != EmergencyHash(j.BeforeNative)) throw new InvalidOperationException("Emergency pre-mutation checkpoint is invalid");
        if (j.Cargo.Any(x => x.Sha256 != EmergencyHash(x.Native))) throw new InvalidOperationException("Cargo escrow checksum differs; preserve all bodies");
        string previousStage = j.Stage;
        bool changed = stage != null && stage != j.Stage;
        if (stage != null) { j.Stage = stage; j.Error = string.Empty; }
        j.Revision++;
        try { packRepository.Save(); } // Always before a native removal/spawn boundary.
        catch {
            // A failed terminal write must not make the in-memory operation
            // look complete and disappear from the resume loop. Even when
            // storage wrote before throwing, replaying this phase is safe.
            j.Stage = previousStage;
            throw;
        }
        emergencyCheckpointedRevisions[j.Id] = j.Revision;
        if (j.Stage is "Rebuilt" or "Undone") {
            emergencyColumnLoads.Remove(j.Id + ":rebuild"); emergencyColumnLoads.Remove(j.Id + ":undo");
        }
        if (changed || j.Revision == 1) LogRecoveryAttempt(r.FoxId, "emergency-" + j.Stage, "operation=" + j.Id);
    }

    private string EmergencyAuthorityBlocker(FoxPackRecordV2 r)
    {
        if (NativeLinkPending(r)) return "Native-ID relink/Undo is pending; preserve its checkpoint";
        if (serverApi == null || !companionContentReady || packRepository?.Loaded != true
            || RuntimeEnv.ServerMainThreadId != Environment.CurrentManagedThreadId) return "Initialized server main thread required";
        var a = CaptureExpeditionPresentationAuthority();
        if (a == null) return "Unknown expedition authority: " + presentationAuthorityReason;
        if (a.Assigned.Contains(r.FoxId) || a.Conflicts.Contains(r.FoxId) || a.NativeConflicts.Contains(r.EntityId)) return "Active journey or conflicting identity is protected";
        if (packRepository.IsPermanentlyDeleted(r.FoxId)) return "Permanently deleted companion identity";
        if (!string.IsNullOrEmpty(r.PendingDepartureStatus) || r.PendingDepartureAtUtcMs > 0 || r.PendingDepartureDeadlineUtcMs > 0
            || !string.IsNullOrEmpty(r.PendingReturnStatus) || r.PendingReturnAtUtcMs > 0) return "Pending departure/return is protected";
        if (!string.IsNullOrEmpty(r.RetainedTrip) && r.RetainedPhase != "Complete") return "Unfinished retained journey is protected";
        if (pendingFullRebuilds.Contains(r.FoxId) || pendingRecoveryChecks.Contains(r.FoxId) || pendingRecoveryBatches.Contains(r.OwnerUid)
            || pendingAdminCompanionTeleports.Contains(r.OwnerUid + ":" + r.FoxId)) return "Normal rebuild, lookup or owner batch is pending";
        if (r.BackpackDeliveryActive || backpackPreflights.ContainsKey(r.EntityId) || backpackStorageAreaLoads.Contains(r.FoxId) || backpackMissingEntityRecoveryChecks.Contains(r.FoxId)) return "Finish pending cargo work first";
        if (r.EmergencyRebuilds.Any(x => x.Version != 1)) return "Unknown emergency journal version; preserve checkpoints";
        return string.Empty;
    }
    private string EmergencyAdminBlocker(IServerPlayer actor, FoxPackRecordV2 r, bool undo)
    {
        if (actor.ConnectionState != EnumClientState.Playing || !MayRepairCompletedHold(actor) || !DeveloperMayControlOwner(actor, r.OwnerUid))
            return "Emergency rebuild and Undo require a connected server admin authorized for the selected owner";
        string blocked = EmergencyAuthorityBlocker(r); if (blocked.Length > 0) return blocked;
        var j = LastEmergency(r);
        if (undo) return j == null ? "No emergency undo checkpoint exists" : j.Stage == "Undone" ? "This operation was already undone; all escrow remains retained" : string.Empty;
        if (j?.Stage == "Rebuilt") return "This emergency rebuild is complete. Review its field report or Undo before starting another";
        if (j != null && j.Stage.StartsWith("Undo", StringComparison.Ordinal) && j.Stage != "Undone") return "Undo is pending; resume that operation";
        if (j == null || j.Stage == "Undone") {
            if (r.EmergencyRebuilds.Count >= 8) return "Eight emergency checkpoints are already retained; no checkpoint will be overwritten";
            if (ResolveEmergencyEntityType(r, out string typeReason) == null) return "Required entity type is unavailable: " + typeReason;
        }
        return string.Empty;
    }

    private Entity? EmergencyNative(long id, out string reason)
    {
        reason = string.Empty;
        if (serverApi == null || id <= 0) return null;
        var chunks = serverApi.WorldManager.AllLoadedChunks;
        if (chunks.Count > 32768 || serverApi.World.LoadedEntities.Count > 131072) { reason = "Native lookup bound reached"; return null; }
        var found = serverApi.World.LoadedEntities.Values.Concat(retainedAdmissionBodies.Values).Concat(loadedFoxes.Values)
            .Concat(chunks.Values.Where(c => !c.Disposed).SelectMany(c => c.Entities.Take(c.EntitiesCount)))
            .Where(e => e != null && e.EntityId == id).Distinct(ReferenceEqualityComparer.Instance).Cast<Entity>().Take(2).ToArray();
        if (found.Length > 1) { reason = "Competing native objects claim the emergency identity"; return null; }
        return found.FirstOrDefault();
    }
    private static bool EmergencyIdentity(Entity e, FoxPackRecordV2 r) => GetCompanionOwnerUid(e) == r.OwnerUid
        && GetDomesticationStatus(e)?.GetString(FoxIdKey) == r.FoxId;

    private EmergencyCompanionEscrow DescribeEmergencyNative(Entity e, string kind, byte[]? backpack = null)
    {
        var bytes = SnapshotRecoveryNative(e);
        using var marker = new MemoryStream();
        using (var w = new BinaryWriter(marker, System.Text.Encoding.UTF8, true)) RetainedNativeSuspension.Marker(e)?.ToBytes(w);
        var boxes = new[] { e.CollisionBox, e.OriginCollisionBox, e.SelectionBox, e.OriginSelectionBox };
        return new() { NativeId = e.EntityId, Kind = kind, Native = bytes, Sha256 = EmergencyHash(bytes),
            LedgerBackpack = backpack?.ToArray() ?? Array.Empty<byte>(), PriorMarker = marker.ToArray(),
            PriorAway = GetDomesticationStatus(e)?.GetBool(ExpeditionAwayKey) == true, PriorState = (int)e.State,
            PriorBoxes = boxes.SelectMany(b => b == null ? new float[6] : new[] { b.X1, b.Y1, b.Z1, b.X2, b.Y2, b.Z2 }).ToArray() };
    }
    private EmergencyCompanionEscrow CaptureEmergencyCargo(FoxPackRecordV2 r, EmergencyCompanionJournal j, Entity e, string kind, bool refresh = false)
    {
        var prior = j.Cargo.FirstOrDefault(x => x.NativeId == e.EntityId && x.Kind == kind);
        if (prior != null && !refresh) return prior; // Never overwrite an earlier uncertain inventory.
        var entry = DescribeEmergencyNative(e, kind, r.BackpackItem);
        if (j.Cargo.Any(x => x.NativeId == e.EntityId && x.Kind == kind && x.Sha256 == entry.Sha256)) return j.Cargo.First(x => x.NativeId == e.EntityId && x.Kind == kind && x.Sha256 == entry.Sha256);
        if (r.EmergencyRebuilds.Sum(EmergencyBytes) + entry.Native.Length + entry.LedgerBackpack.Length > EmergencyMaximumBytes)
            throw new InvalidOperationException("Cargo escrow bound reached; native body retained without removal");
        j.Cargo.Add(entry); CheckpointEmergency(r, j); return entry;
    }
    private bool GuardEmergencyEntity(Entity e)
    {
        if (serverApi == null || packRepository?.Loaded != true || e.Api?.Side == EnumAppSide.Client) return false;
        string id = GetDomesticationStatus(e)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (!packRepository.TryGetRecord(id, out var r) || r == null || r.EmergencyRebuilds.Count == 0) return false;
        if (LastEmergency(r)?.Version is 2 or 3) return GuardForceEmergency(e);
        if (emergencyQuarantining.Contains(e)) return true;
        var last = LastEmergency(r);
        if (last?.Stage == "Undone" && last.OriginalId == e.EntityId && r.EntityId == e.EntityId
            && !r.RecoveryRetiredNativeIds.Contains(e.EntityId) && RetainedNativeSuspension.Marker(e)?.GetString("reason") == EmergencyQuarantineReason) {
            RetainedNativeSuspension.DeferAdmission(e);
            if (RuntimeEnv.ServerMainThreadId != Environment.CurrentManagedThreadId || !packRepository.CanCheckpointRecovery) return true;
            emergencyQuarantining.Add(e);
            try {
                var presentation = last.Cargo.FirstOrDefault(x => x.NativeId == e.EntityId && x.Kind == "late original or retired generation") ?? last.OriginalPresentation;
                if (presentation == null) throw new InvalidOperationException("Undo lacks the original's pre-quarantine presentation");
                CaptureEmergencyCargo(r, last, e, "original arriving after undo", true);
                RestoreEmergencyPresentation(e, presentation); return false;
            } catch (Exception ex) { SetEmergencyError(r, last, "Late original Undo presentation pending: " + ex.Message); return true; }
            finally { emergencyQuarantining.Remove(e); }
        }
        if (r.RecoveryRetiredNativeIds.Contains(e.EntityId)) {
            RetainedNativeSuspension.DeferAdmission(e); // No native state mutation until escrow is checkpointed.
            var j = r.EmergencyRebuilds.LastOrDefault(x => x.OriginalId == e.EntityId || x.GenerationIds.Contains(e.EntityId) || x.RetiredIds.Contains(e.EntityId));
            if (j == null) return false;
            emergencyQuarantining.Add(e);
            try {
                if (RuntimeEnv.ServerMainThreadId == Environment.CurrentManagedThreadId && packRepository.CanCheckpointRecovery) {
                    CaptureEmergencyCargo(r, j, e, "late original or retired generation");
                    RetainedNativeSuspension.Hold(e, EmergencyQuarantineReason);
                }
            } catch (Exception ex) { SetEmergencyError(r, j, "Late original escrow pending: " + ex.Message); }
            finally { emergencyQuarantining.Remove(e); }
            return true;
        }
        if (EmergencyPending(LastEmergency(r)) && r.EntityId == e.EntityId && !emergencyAdmissionAllowed.Contains(e)) {
            RetainedNativeSuspension.DeferAdmission(e); return true;
        }
        return false;
    }
    private void SetEmergencyError(FoxPackRecordV2 r, EmergencyCompanionJournal j, string error)
    {
        if (j.Error == error) return;
        j.Error = DiagnosticText(error, 400);
        LogRecoveryAttempt(r.FoxId, "emergency-wait", j.Stage + ": " + j.Error);
        try { if (packRepository?.CanCheckpointRecovery == true) packRepository.Save(); } catch { /* No native mutation is authorized by a failed checkpoint. */ }
    }

    private void RestoreEmergencyPresentation(Entity e, EmergencyCompanionEscrow presentation)
    {
        if (presentation.PriorMarker.Length == 0) e.WatchedAttributes.RemoveAttribute(RetainedNativeSuspension.Key);
        else {
            var marker = new TreeAttribute(); using var bytes = new MemoryStream(presentation.PriorMarker, false);
            using var reader = new BinaryReader(bytes); marker.FromBytes(reader);
            e.WatchedAttributes.SetAttribute(RetainedNativeSuspension.Key, marker);
        }
        GetDomesticationStatus(e)!.SetBool(ExpeditionAwayKey, presentation.PriorAway);
        if (presentation.PriorBoxes.Length == 24) {
            Cuboidf Box(int i) { var b = presentation.PriorBoxes; int k = i * 6; return new(b[k], b[k+1], b[k+2], b[k+3], b[k+4], b[k+5]); }
            e.CollisionBox = Box(0); e.OriginCollisionBox = Box(1); e.SelectionBox = Box(2); e.OriginSelectionBox = Box(3);
        }
        e.State = (EnumEntityState)presentation.PriorState;
        if (!presentation.PriorAway && RetainedNativeSuspension.Marker(e) == null) RetainedNativeSuspension.AdmitPresent(e);
        e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.RefreshExpeditionSuspension();
    }

    private static string EmergencyReport(FoxPackRecordV2 r)
    {
        var j = LastEmergency(r); if (j == null) return "No emergency operation or undo snapshot yet";
        if (j.Version is 2 or 3) return $"Force Emergency {j.Stage}; operation {j.Id}; old native {j.OriginalId}; replacement {j.TargetId}; species {j.EntityCode}. Restored: {string.Join(", ", j.Recovered)}. Defaulted: {string.Join(", ", j.Defaulted)}. Unresolved: {string.Join("; ",j.Unresolved)}. Active body {r.EmergencyBody?.NativeId} ({r.EmergencyBody?.Phase ?? "unselected"}); saved/native binding {r.EntityId}. Separate cargo: {j.Cargo.Count} native/presentation snapshots; prior ledger {j.BeforeLedger.Length} bytes retained. {j.UndoResult}" + (j.Error.Length > 0 ? " Current operation status: " + j.Error : string.Empty);
        return $"Emergency {j.Stage}; checkpoint {j.Id[..Math.Min(8,j.Id.Length)]}. Recovered: {string.Join(", ", j.Recovered)}. "
            + $"Defaulted: {string.Join(", ", j.Defaulted)}. Separate cargo escrow: {j.Cargo.Count} native snapshots, {j.Cargo.Sum(x => (long)x.Native.Length)} bytes; original ledger backpack remains in the undo checkpoint. "
            + j.UndoResult + (j.Error.Length > 0 ? " Waiting: " + j.Error : string.Empty);
    }
}
