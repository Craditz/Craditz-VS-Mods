#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private sealed record EmergencySpawn(FoxPackRecordV2 Record, EmergencyCompanionJournal Journal);
    private readonly Dictionary<Entity, EmergencySpawn> emergencySpawns = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> emergencySelecting = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> emergencyBodyProblems = new(StringComparer.Ordinal);
    private int emergencyBodyCursor;
    private static bool HasForceHistory(FoxPackRecordV2 r) => r.EmergencyRebuilds.Any(j => j.Version is 2 or 3);
    private static bool RecoveryHold(Entity e) {
        string reason = RetainedNativeSuspension.Marker(e)?.GetString("reason") ?? string.Empty;
        return reason.StartsWith("Emergency ", StringComparison.Ordinal) || reason.StartsWith("Competing native", StringComparison.Ordinal)
            || reason.StartsWith("Retired recovery", StringComparison.Ordinal) || reason.StartsWith("Duplicate loaded companion", StringComparison.Ordinal)
            || reason.StartsWith("World index identifies a competing", StringComparison.Ordinal) || reason.StartsWith("Chunk contains competing", StringComparison.Ordinal);
    }
    private bool AuthorityMatches(FoxPackRecordV2 r, EmergencyBodyAuthority a, Entity e) => e is not EntityPlayer && EmergencyIdentity(e, r)
        && ExactRecoveryCodeEquals(r.EntityCode, e.Code) && (a.BodyKey.Length > 0 ? ForceBodyKey(e) == a.BodyKey
            || string.IsNullOrEmpty(e.WatchedAttributes.GetString(ForceBodyIdentityKey)) && e.EntityId == a.NativeId
                && (a.Marker.Length == 0 || e.WatchedAttributes.GetString(EmergencyBodyKey) == a.Marker)
            : a.Marker.Length > 0 ? e.WatchedAttributes.GetString(EmergencyBodyKey) == a.Marker : e.EntityId == a.NativeId);
    private bool IsEmergencySelected(Entity e) {
        string id = GetDomesticationStatus(e)?.GetString(FoxIdKey) ?? string.Empty;
        return packRepository?.TryGetRecord(id, out var r) == true && r?.EmergencyBody is { Phase: "Ready" } a && AuthorityMatches(r, a, e);
    }
    private void BindEmergencySpawn(Entity e, EmergencySpawn spawn)
    {
        var r = spawn.Record; var j = spawn.Journal;
        if (j.ReplacementKey.Length == 0) j.ReplacementKey = ForceBodyKey(e); // Resume a version-2 published generation.
        var status = GetDomesticationStatus(e, true)!;
        status.SetString("owner", j.Owner); status.SetString(FoxIdKey, j.FoxId);
        e.WatchedAttributes.SetString(EmergencyBodyKey, j.Id + ":force");
        // The approved OBJECT and generation own this callback. SpawnEntity
        // assigns an entity ID before Initialize and can assign it again later.
        long previous = j.TargetId;
        j.TargetId = e.EntityId;
        if (e.EntityId > 0 && !j.GenerationIds.Contains(e.EntityId)) j.GenerationIds.Add(e.EntityId);
        r.EntityId = e.EntityId;
        r.EmergencyBody = new() { Operation = j.Id, NativeId = e.EntityId, BodyKey = j.ReplacementKey,
            Marker = j.Id + ":force", Phase = "Spawning", Reason = "approved force object/generation" };
        e.WatchedAttributes.SetString(ForceBodyIdentityKey, j.ReplacementKey);
        RetainedNativeSuspension.AdmitPresent(e);
        if (previous != e.EntityId) LogRecoveryAttempt(r.FoxId, "force-native-bound", $"operation={j.Id}; provisional={string.Join(",",j.ProvisionalIds)}; previous={previous}; actual={e.EntityId}; source=approved-spawn-object");
    }
    // Used BEFORE ordinary retirement, mismatch and competing-cache admission.
    // A true return supplies the recovery-specific admission decision.
    private bool EmergencyBodyAdmission(Entity e, out bool blocked)
    {
        blocked = false;
        if (e is EntityPlayer || serverApi == null || packRepository?.Loaded != true) return false;
        if (emergencySpawns.TryGetValue(e, out var spawn)) { BindEmergencySpawn(e, spawn); blocked = true; return true; }
        string id = GetDomesticationStatus(e)?.GetString(FoxIdKey) ?? string.Empty;
        if (!packRepository.TryGetRecord(id, out var r) || r == null || !HasForceHistory(r) || !EmergencyIdentity(e, r)) return false;
        var a = r.EmergencyBody;
        if (a != null && AuthorityMatches(r, a, e)) {
            if (a.Phase != "Ready") { blocked = true; return true; }
            if (a.NativeId != e.EntityId && serverApi.World.GetEntityById(a.NativeId) is { } selected
                && !ReferenceEquals(selected, e) && AuthorityMatches(r, a, selected)) {
                blocked = true;
                if (packRepository.CanCheckpointRecovery && LastEmergency(r) is { } journal) ForceHold(r, journal, e, "duplicate generation token");
                return true;
            }
            var canonical = serverApi.World.GetEntityById(e.EntityId);
            if (canonical != null && !ReferenceEquals(canonical, e)) { blocked = true; return true; }
            if (a.NativeId != e.EntityId || r.EntityId != e.EntityId) { a.Phase = "Selecting"; blocked = true; return true; }
            forceBodyIdentities.GetValue(e, _ => new()).Key = a.BodyKey;
            if (a.BodyKey.Length > 0) e.WatchedAttributes.SetString(ForceBodyIdentityKey, a.BodyKey);
            // A later real expedition keeps its own normal suspension semantics.
            if (r.RetainedTrip.Length > 0 || GetDomesticationStatus(e)?.GetBool(ExpeditionAwayKey) == true && !RecoveryHold(e)) return false;
            if (RecoveryHold(e)) ActivateEmergencyBody(e);
            else { RetainedNativeSuspension.AdmitPresent(e); e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.RefreshExpeditionSuspension(); }
            ledgerHydratedEntities.Add(e.EntityId); retainedLoadedHealthApplied.Add(e);
            loadedFoxes[e.EntityId] = e; retainedAdmissionBodies[e.EntityId] = e;
            return true;
        }
        // Legacy repair is deferred until outside repository save batches. A
        // visible existing body is never hidden just because repair is queued.
        if (a == null) { blocked = true; return true; }
        blocked = true;
        var j = LastEmergency(r);
        if (j?.Version is 2 or 3 && a.Phase == "Ready" && packRepository.CanCheckpointRecovery) {
            try { if (!emergencySelecting.Contains(r.FoxId)) ForceHold(r, j, e, "inactive recovery generation"); }
            catch (Exception ex) { emergencyBodyProblems[r.FoxId] = "Separate body retention pending: " + ex.Message; }
        }
        return true;
    }
    private void ActivateEmergencyBody(Entity e)
    {
        e.WatchedAttributes.RemoveAttribute(RetainedNativeSuspension.Key);
        GetDomesticationStatus(e, true)!.SetBool(ExpeditionAwayKey, false);
        RetainedNativeSuspension.AdmitPresent(e);
        e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.RefreshExpeditionSuspension();
        // Old 0.5.89 snapshots can contain already-zero boxes and an Inactive
        // social latch. Restore fresh geometry from the same exact native type.
        if (e.CollisionBox?.XSize is not > 0 && e.Properties?.CollisionBoxSize is { } size) e.SetCollisionBox(size.X, size.Y);
        if (e.SelectionBox?.XSize is not > 0 && e.Properties?.SelectionBoxSize is { } selection) e.SetSelectionBox(selection.X, selection.Y);
        if (e.SelectionBox?.XSize is not > 0 && e.CollisionBox != null) e.SelectionBox = e.CollisionBox.Clone();
        e.State = EnumEntityState.Active;
        e.WatchedAttributes.MarkPathDirty(RetainedNativeSuspension.Key);
        e.WatchedAttributes.MarkPathDirty("domesticationstatus");
    }
    private static void ClearEmergencyPresentationLedger(FoxPackRecordV2 r)
    {
        r.RetainedTrip = r.RetainedPhase = r.RetainedOutcome = r.RetainedLegacyAuthority = string.Empty;
        r.SaveMigration = null; r.CompletionProof = null;
        r.PendingDepartureStatus = r.PendingReturnStatus = string.Empty;
        r.PendingDepartureAtUtcMs = r.PendingDepartureDeadlineUtcMs = r.PendingReturnAtUtcMs = 0;
        r.PendingDepartureMoment = false;
    }
    private Entity[] RecoveryBodies(FoxPackRecordV2 r, EmergencyCompanionJournal j) => ForceBodies(r, j)
        .Where(e => e is not EntityPlayer && e.State != EnumEntityState.Despawned).Distinct(ReferenceEqualityComparer.Instance).Cast<Entity>().ToArray();
    private Entity? FindAuthorityBody(FoxPackRecordV2 r, EmergencyBodyAuthority a, EmergencyCompanionJournal j)
    {
        var bodies = RecoveryBodies(r, j).Where(e => AuthorityMatches(r, a, e) && ReferenceEquals(serverApi!.World.GetEntityById(e.EntityId), e)).ToArray();
        return bodies.FirstOrDefault(e => e.EntityId == a.NativeId) ?? (bodies.Length == 1 ? bodies[0] : null);
    }
    private Entity? LegacyEmergencyChoice(FoxPackRecordV2 r, EmergencyCompanionJournal j)
    {
        var bodies = RecoveryBodies(r, j).Where(e => e.Alive && ExactRecoveryCodeEquals(r.EntityCode, e.Code)).ToArray();
        var visible = bodies.Where(e => NativeLinkPresentationReady(e)
            && RetainedNativeSuspension.Marker(e) == null && GetDomesticationStatus(e)?.GetBool(ExpeditionAwayKey) != true).ToArray();
        if (visible.Length == 1) return visible[0]; // Includes an already-visible Casio; no respawn or native hydration.
        long intended = r.EntityId;
        var history = r.EmergencyRebuilds.LastOrDefault(x => x.TargetId == intended);
        string marker = history == null ? string.Empty : history.Id + ":force";
        if (j.Stage != "Undone" && j.Stage != "ForceFailed") marker = j.Id + ":force";
        var exact = bodies.Where(e => marker.Length > 0 && e.WatchedAttributes.GetString(EmergencyBodyKey) == marker).ToArray();
        if (exact.Length == 1) return exact[0];
        exact = bodies.Where(e => e.EntityId == intended).ToArray();
        var indexed = exact.Where(e => ReferenceEquals(serverApi!.World.GetEntityById(intended), e)).ToArray();
        if (indexed.Length == 1) return indexed[0];
        if (exact.Length == 1) return exact[0];
        // An unloaded intended generation is unknown, even if another held
        // object happens to be the only body loaded so far.
        return marker.Length == 0 && bodies.Length == 1 ? bodies[0] : null;
    }
    private void PrepareEmergencyChoice(FoxPackRecordV2 r, EmergencyCompanionJournal j, Entity e, string reason)
    {
        var snapshot = ForceLedgerBytes(r);
        if (r.EmergencyBody is { } old && old.NativeId != e.EntityId && old.BodyKey == ForceBodyKey(e))
            forceBodyIdentities.GetValue(e, _ => new()).Key = Guid.NewGuid().ToString("N");
        ForceCapture(r, j, e, "before active-body binding");
        r.EmergencyBody = new() { Operation = j.Id, NativeId = e.EntityId, BodyKey = ForceBodyKey(e), Marker = e.WatchedAttributes.GetString(EmergencyBodyKey) ?? string.Empty,
            Reason = reason, BeforeRepairLedger = snapshot, BeforeRepairHash = EmergencyHash(snapshot) };
        ForceCheckpoint(r, j); // Intent and pre-change snapshot before changing the reference/holds.
    }
    private void CompleteEmergencyChoice(FoxPackRecordV2 r, EmergencyCompanionJournal j)
    {
        var a = r.EmergencyBody!;
        var selected = FindAuthorityBody(r, a, j);
        if (selected == null) { emergencyBodyProblems[r.FoxId] = "Selected body is unloaded; active binding retained, awaiting its natural load"; return; }
        if (!emergencySelecting.Add(r.FoxId)) return;
        try {
            // Even a failed/uncertain earlier write must be retried BEFORE
            // releasing a body or holding another body's native cargo.
            ForceCheckpoint(r, j);
            a.NativeId = selected.EntityId; r.EntityId = selected.EntityId;
            r.RecoveryRetiredNativeIds = r.RecoveryRetiredNativeIds.Where(id => id != selected.EntityId).ToArray();
            ClearEmergencyPresentationLedger(r);
            a.Phase = "Ready"; r.Status = "Present";
            try { ForceCheckpoint(r, j); } catch { a.Phase = "Selecting"; throw; }
            forceBodyIdentities.GetValue(selected, _ => new()).Key = a.BodyKey;
            selected.WatchedAttributes.SetString(ForceBodyIdentityKey, a.BodyKey);
            ActivateEmergencyBody(selected);
            ledgerHydratedEntities.Add(selected.EntityId); retainedLoadedHealthApplied.Add(selected);
            // Purge only this animal's stale cache references; leave native bodies and inventories intact.
            foreach (var pair in loadedFoxes.Where(p => EmergencyIdentity(p.Value, r) && !ReferenceEquals(p.Value, selected)).ToArray()) loadedFoxes.Remove(pair.Key);
            foreach (var pair in retainedAdmissionBodies.Where(p => EmergencyIdentity(p.Value, r) && !ReferenceEquals(p.Value, selected)).ToArray()) retainedAdmissionBodies.Remove(pair.Key);
            loadedFoxes[selected.EntityId] = selected; retainedAdmissionBodies[selected.EntityId] = selected;
            foreach (var other in RecoveryBodies(r, j).Where(e => !ReferenceEquals(e, selected))) ForceHold(r, j, other, "inactive recovery generation");
            emergencyBodyProblems.Remove(r.FoxId);
            ResolveEmergencyTransientNotes(j);
            ForceCheckpoint(r, j);
            LogRecoveryAttempt(r.FoxId, "emergency-active-body", $"operation={j.Id}; actual-native={r.EntityId}; body-key={a.BodyKey}; selection={a.Reason}; alive={selected.Alive}; state={selected.State}; boxes={selected.CollisionBox?.XSize}/{selected.SelectionBox?.XSize}; ledger-live-agree={r.EntityId==selected.EntityId}");
        } finally { emergencySelecting.Remove(r.FoxId); }
    }
    private static void ResolveEmergencyTransientNotes(EmergencyCompanionJournal j)
    {
        foreach (string text in j.Unresolved.Where(s => s.Contains("open save batch", StringComparison.Ordinal) || s.StartsWith("Old body hold/cargo pending:", StringComparison.Ordinal)).ToArray()) {
            j.Unresolved.Remove(text); if (!j.ResolvedNotes.Contains(text)) j.ResolvedNotes.Add(text);
        }
        if (j.Stage != "ForceFailed" && j.Error.Length > 0) {
            string resolved = "Resolved operation status: " + j.Error;
            if (!j.ResolvedNotes.Contains(resolved)) j.ResolvedNotes.Add(resolved);
            j.Error = string.Empty;
        }
    }
    private void MaintainEmergencyBodies()
    {
        if (serverApi == null || packRepository?.CanCheckpointRecovery != true) return;
        var rows = packRepository.GetAllRecords().Where(HasForceHistory).ToArray();
        for (int i = 0; i < Math.Min(8, rows.Length); i++) {
            if (emergencyBodyCursor >= rows.Length) emergencyBodyCursor = 0;
            var r = rows[emergencyBodyCursor++];var j = LastEmergency(r)!;
            if (emergencyProcessing.Contains(r.FoxId) || emergencySelecting.Contains(r.FoxId)) continue;
            try {
                if (j.Stage == "ForceFailed" && r.EmergencyBody?.Phase != "Ready" && ChooseForceUndoBody(r, j) is { } priorBody) {
                    var prior = ReadEmergencyLedger(j.BeforeLedger, j.BeforeLedgerHash);
                    prior.EmergencyRebuilds = r.EmergencyRebuilds;
                    packRepository.RestoreRecord(prior);
                    PrepareEmergencyChoice(prior, j, priorBody, "creation failed: retain the prior existing body");
                    CompleteEmergencyChoice(prior, j);
                    continue;
                }
                if (r.EmergencyBody == null && j.Version == 2 && !EmergencyPending(j)) {
                    var body = LegacyEmergencyChoice(r, j);
                    if (body == null) { emergencyBodyProblems[r.FoxId] = "Legacy recovery needs one body selection; use /companionbody [cart] id:<id> and select <actual-native-id>. No body or cargo changed"; continue; }
                    PrepareEmergencyChoice(r, j, body, "legacy recovery: unique visible body or saved operation generation");
                }
                if (r.EmergencyBody is { Phase: "Selecting" }) CompleteEmergencyChoice(r, j);
                else if (r.EmergencyBody is { Phase: "Ready" } a && FindAuthorityBody(r, a, j) is { } current) {
                    if (r.EntityId != current.EntityId || a.NativeId != current.EntityId || RecoveryHold(current)) { a.Phase = "Selecting"; CompleteEmergencyChoice(r, j); }
                }
            } catch (Exception ex) { emergencyBodyProblems[r.FoxId] = "Active-body persistence/presentation pending: " + ex.Message; }
        }
    }
    private bool GuardEmergencyLifecycle(Entity e) => EmergencyBodyAdmission(e, out bool blocked) && blocked;
    private Entity? ChooseForceUndoBody(FoxPackRecordV2 r, EmergencyCompanionJournal j)
    {
        var bodies = RecoveryBodies(r, j);
        if (j.UndoTargetId > 0) return bodies.FirstOrDefault(e => e.EntityId == j.UndoTargetId);
        if (j.BeforeBodyKey.Length > 0) return bodies.FirstOrDefault(e => ForceBodyKey(e) == j.BeforeBodyKey)
            ?? bodies.FirstOrDefault(e => e.EntityId == j.BeforeActiveId && string.IsNullOrEmpty(e.WatchedAttributes.GetString(ForceBodyIdentityKey)));
        if (j.BeforeBodyMarker.Length > 0) return bodies.FirstOrDefault(e => e.WatchedAttributes.GetString(EmergencyBodyKey) == j.BeforeBodyMarker);
        // Version-2 damaged data: resolve the old provisional reference through
        // its operation marker; never activate every captured original.
        var old = r.EmergencyRebuilds.LastOrDefault(x => x != j && x.TargetId == j.OriginalId);
        var candidates = bodies.Where(e => old != null ? e.WatchedAttributes.GetString(EmergencyBodyKey) == old.Id + ":force"
            : e.EntityId == j.OriginalId || j.Cargo.Any(c => c.Kind == "original before force" && c.BodyKey == ForceBodyKey(e))).ToArray();
        if (candidates.Length == 1) return candidates[0];
        var late = bodies.Where(e => !IsForceBody(e, r, j) && !j.GenerationIds.Contains(e.EntityId)).ToArray();
        return candidates.Length == 0 && late.Length == 1 ? late[0] : null;
    }
    private void CompleteForceUndoBody(FoxPackRecordV2 r, EmergencyCompanionJournal j)
    {
        var original = ChooseForceUndoBody(r, j);
        if (original == null) {
            j.Error = "Undo is pending: intended original generation is unloaded or needs explicit body selection; current replacement stays active";
            ForceCheckpoint(r, j, "UndoWaitingBody"); return;
        }
        var before = ReadEmergencyLedger(j.BeforeLedger, j.BeforeLedgerHash);
        before.EmergencyRebuilds = r.EmergencyRebuilds;
        before.RecoveryRetiredNativeIds = before.RecoveryRetiredNativeIds.Concat(r.RecoveryRetiredNativeIds).Distinct().Where(id => id != original.EntityId).ToArray();
        before.EntityId = original.EntityId;
        before.EmergencyBody = new() { Operation = j.Id, NativeId = original.EntityId, BodyKey = ForceBodyKey(original), Marker = original.WatchedAttributes.GetString(EmergencyBodyKey) ?? string.Empty,
            Reason = "confirmed Undo: one prior active native generation", BeforeRepairLedger = j.UndoDisplacedLedger, BeforeRepairHash = j.UndoDisplacedLedgerHash };
        packRepository!.RestoreRecord(before);
        ForceCheckpoint(before, j); // UndoPrepared + intended body, before native holds change.
        CompleteEmergencyChoice(before, j);
        if (before.EmergencyBody.Phase != "Ready") return;
        j.UndoTargetId = original.EntityId;
        j.UndoResult = $"Undo selected actual native {original.EntityId}; ledger/native identity agrees. Other generations and their cargo remain separately held. Native inventory, health, progression and nametag settings were not replayed.";
        ResolveEmergencyTransientNotes(j);
        ForceCheckpoint(before, j, "Undone"); NotifyForceResult(before, j);
    }
    private TextCommandResult HandleEmergencyBodyCommand(IServerPlayer actor, string text)
    {
        if (!MayRepairCompletedHold(actor)) return TextCommandResult.Error("Server admin required");
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool cart = words.Contains("cart", StringComparer.OrdinalIgnoreCase);
        string owner = cart ? DeveloperTargetOwnerUid(actor) : actor.PlayerUID;
        string id = words.FirstOrDefault(s => s.StartsWith("id:", StringComparison.Ordinal))?[3..] ?? string.Empty;
        if (!DeveloperMayControlOwner(actor, owner) || packRepository?.TryGetRecord(id, out var r) != true || r == null || r.OwnerUid != owner || !HasForceHistory(r))
            return TextCommandResult.Error("Select the authorized owner's animal with id:<companion-id>");
        var j = LastEmergency(r)!;
        int select = Array.FindIndex(words, s => s.Equals("select", StringComparison.OrdinalIgnoreCase));
        if (select >= 0 && select + 1 < words.Length && long.TryParse(words[select + 1], out long native)) {
            var body = RecoveryBodies(r, j).FirstOrDefault(e => e.EntityId == native && e.Alive && ExactRecoveryCodeEquals(r.EntityCode, e.Code));
            if (body == null) return TextCommandResult.Error("That actual body is not loaded in this owner/companion/species scope; force Emergency remains available");
            try {
                PrepareEmergencyChoice(r, j, body, "explicit admin generation selection"); CompleteEmergencyChoice(r, j);
                if (j.Stage is "UndoPrepared" or "UndoWaitingBody") { j.UndoTargetId = body.EntityId; ForceCheckpoint(r, j); CompleteForceUndoBody(r, j); }
            }
            catch (Exception ex) { return TextCommandResult.Error("Selection pending; persistence not yet confirmed: " + ex.Message); }
        }
        string bodies = string.Join(" | ", RecoveryBodies(r, j).Select(e => $"native={e.EntityId} operation={e.WatchedAttributes.GetString(EmergencyBodyKey)} state={e.State} boxes={e.CollisionBox?.XSize}/{e.SelectionBox?.XSize} hold={RetainedNativeSuspension.Marker(e)?.GetString("reason")??"none"}"));
        return TextCommandResult.Success($"Saved reference={r.EntityId}; selected={r.EmergencyBody?.NativeId}; phase={r.EmergencyBody?.Phase??"not selected"}; {emergencyBodyProblems.GetValueOrDefault(r.FoxId,"no current persistence error")}. {bodies}. To preserve one existing body: /companionbody {(cart?"cart ":"")}id:{id} select <actual-native-id>");
    }
}
