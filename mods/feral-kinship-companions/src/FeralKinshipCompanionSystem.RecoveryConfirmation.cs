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
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;

[ProtoContract]
public sealed class CompanionRecoveryPreviewEntry
{
    [ProtoMember(1)] public string Name = string.Empty;
    [ProtoMember(2)] public string Decision = string.Empty;
    [ProtoMember(3)] public string Reason = string.Empty;
}

public sealed partial class FeralKinshipCompanionSystem
{
    private sealed record RecoveryUiTarget(string FoxId, long NativeId, Entity? Original, string Fingerprint, string Mode);
    private sealed record RecoveryUiConfirmation(IServerPlayer Actor, string Token, string Owner, long Until, bool Bulk,
        RecoveryUiTarget[] Targets, int PreviewSkipped);
    private readonly Dictionary<string, RecoveryUiConfirmation> recoveryUiConfirmations = new(StringComparer.Ordinal);
    private GuiDialogFeralKinshipRecoveryConfirmation? recoveryConfirmationDialog;

    private static bool MayRepairCompletedHold(IServerPlayer actor) => actor.HasPrivilege(Privilege.controlserver) || actor.HasPrivilege(Privilege.root);

    private static string RecoveryUiFingerprint(FoxPackRecordV2 record, Entity? original)
    {
        // Confirmation binds authority and cargo, not a frame-perfect animal.
        // Health, movement, mood, request timers and earned XP continue ticking;
        // the rebuild takes their current native values after confirmation.
        // Never call provider ToBytes hooks from this read-only preview.
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        WriteRecoveryAuthority(writer, record);
        if (original != null) WriteRecoveryNativeAuthority(writer, original);
        if (stream.Length > 16 * 1024 * 1024) throw new InvalidOperationException("Preview authority exceeds 16 MiB");
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private string RecoveryUiBlocker(IServerPlayer actor, FoxPackRecordV2 record, Entity? original, string mode, bool inBatch = false)
    {
        if (actor.ConnectionState != EnumClientState.Playing || !CanUseDeveloperTools(actor) || !DeveloperMayControlOwner(actor, record.OwnerUid)) return "Selected-player permission expired or requesting session disconnected";
        if (NativeLinkPending(record)) return "Native-ID relink/Undo journal is pending; wait for its guarded resume";
        if (mode is "Emergency rebuild" or "Emergency undo") return EmergencyAdminBlocker(actor, record, mode == "Emergency undo");
        if (EmergencyPending(LastEmergency(record))) return "Emergency rebuild/Undo journal is pending; resume or Undo that operation first";
        if (record.MissingRecoveryState == "Prepared") return "Interrupted missing-body publication; inspect /companiondiag. Paired checkpoints and retired identity are preserved";
        if (record.SaveMigration != null && record.SaveMigration.Version != 1) return "Unknown migration schema; preserve original and inspect /companiondiag";
        if (mode == "Completed Hold") {
            if (!MayRepairCompletedHold(actor)) return "Completed-Hold repair requires server admin privilege";
            if (original == null) return "Native original is not loaded; load it and request a new preview";
            return ConfirmedHoldRecoveryPreflightForBatch(record, original, inBatch);
        }
        if (mode == "Saved location" && original == null)
            return CanLookUpFullRecoveryOriginalUsingAuthority(record, CaptureExpeditionPresentationAuthority(), false) ? string.Empty : "Missing-original recovery is blocked by identity, journey or pending work; inspect /companiondiag";
        return FullRecoveryBlocker(record, original);
    }

    private void PreviewRecoveryUi(IServerPlayer actor, string owner, string foxId, bool bulk)
    {
        recoveryUiConfirmations.Remove(actor.PlayerUID);
        long now = serverApi!.World.ElapsedMilliseconds;
        foreach (string id in recoveryUiConfirmations.Where(x => now > x.Value.Until).Select(x => x.Key).ToArray()) recoveryUiConfirmations.Remove(id);
        if (!CanUseDeveloperTools(actor) || !DeveloperMayControlOwner(actor, owner)) { SendCompanionRecoveryState(actor, "Recovery preview refused: selected-player permission expired"); return; }
        if (pendingRecoveryBatches.Contains(owner)) { SendCompanionRecoveryState(actor, "A recovery batch for this player is already pending"); return; }
        var rows = packRepository!.GetAllRecords().Where(r => r.OwnerUid == owner && (bulk || r.FoxId == foxId)).Take(8193).ToArray();
        if (rows.Length == 0 || rows.Length > 8192) { SendCompanionRecoveryState(actor, "No matching record, or roster exceeds the 8192-animal safety bound"); return; }
        var entries = new List<CompanionRecoveryPreviewEntry>();
        var targets = new List<RecoveryUiTarget>();
        foreach (var r in rows) {
            Entity? e = FindArchiveOriginal(r, out string lookupReason);
            string mode = e != null && r.RetainedPhase == "Complete" && RetainedNativeSuspension.Marker(e)?.GetString("phase") == "Hold"
                ? "Completed Hold" : e == null ? "Saved location" : "Native rebuild";
            string reason;
            try {
                reason = e == null && !IsAbsentRecoveryOriginalReason(lookupReason) ? lookupReason : RecoveryUiBlocker(actor, r, e, mode);
                LogRecoveryAttempt(r.FoxId, "preview", reason.Length == 0 ? mode : reason);
                if (reason.Length == 0) targets.Add(new(r.FoxId, r.EntityId, e, RecoveryUiFingerprint(r, e), mode));
            } catch (Exception ex) { reason = "Read-only preview stopped: " + ex.GetType().Name + "; collect /companiondiag"; }
            entries.Add(new() { Name = DiagnosticText(GetFoxRecordLabel(r), 60), Decision = reason.Length == 0 ? mode : "Skip", Reason = reason.Length == 0
                ? mode == "Completed Hold" ? "Confirmed repair of the held original; same identity/native inventory, compatible progression and current health; no outcome or reward replay."
                : mode == "Saved location" ? "Check saved source locations; rebuild a verified original or a matching complete native checkpoint. Missing native inventory data is reported. Late originals remain quarantined."
                : "Fresh body from current native state. A dead original is deliberately revived; current native inventory/progression are retained."
                : DiagnosticText(reason, 2048) });
        }
        if (targets.Count == 0) {
            SendCompanionRecoveryState(actor, "No eligible recovery. " + string.Join("; ", entries.Take(3).Select(x => x.Name + ": " + x.Reason)));
            serverChannel?.SendPacket(new CompanionRecoveryStatePacket { TargetOwnerUid = owner, PreviewEntries = entries,
                Message = $"Player {owner}: no eligible recovery; all {entries.Count} animals skipped. Review each reason below. No animal was changed." }, actor);
            return;
        }
        if (recoveryUiConfirmations.Count >= 128) { SendCompanionRecoveryState(actor, "Recovery confirmation limit reached; wait for previews to expire"); return; }
        string token = Guid.NewGuid().ToString("N");
        recoveryUiConfirmations[actor.PlayerUID] = new(actor, token, owner, now + 60000, bulk, targets.ToArray(), rows.Length - targets.Count);
        serverChannel?.SendPacket(new CompanionRecoveryStatePacket {
            TargetOwnerUid = owner, PreviewToken = token, PreviewEntries = entries,
            Message = $"Player {owner}: {targets.Count} eligible, {rows.Length - targets.Count} skipped. Confirm within 60 seconds. Native/ledger checkpoints are retained. Outcomes, loot and rewards are not replayed; missing rewards cannot be inferred. Only these previewed animals are included."
        }, actor);
    }

    private string RevalidateRecoveryUi(IServerPlayer actor, RecoveryUiConfirmation confirmation, RecoveryUiTarget target, bool inBatch,
        out FoxPackRecordV2? record, out Entity? original)
    {
        record = null; original = null;
        if (!ReferenceEquals(actor, confirmation.Actor) || actor.ConnectionState != EnumClientState.Playing
            || !CanUseDeveloperTools(actor) || !DeveloperMayControlOwner(actor, confirmation.Owner)) return "Selected-player permission expired or requesting session changed";
        if (!packRepository!.TryGetRecord(target.FoxId, out record) || record == null || record.OwnerUid != confirmation.Owner) return "Previewed companion/owner changed; request a new preview";
        if (ForceMode(target.Mode)) return MayRepairCompletedHold(actor) ? string.Empty : "Force reconstruction and Undo require server admin privilege";
        if (record.EntityId != target.NativeId) return "Previewed ledger identity changed; request a new preview";
        original = FindArchiveOriginal(record, out string lookupReason);
        if (original == null && !IsAbsentRecoveryOriginalReason(lookupReason)) return lookupReason;
        if (!ReferenceEquals(original, target.Original)) return "Previewed native original changed; request a new preview";
        string blocker = RecoveryUiBlocker(actor, record, original, target.Mode, inBatch);
        if (blocker.Length > 0) return blocker;
        if (RecoveryUiFingerprint(record, original) != target.Fingerprint) return "Identity, journey receipt or cargo changed; request a new preview";
        return string.Empty;
    }

    private void HandleRecoveryUiResponse(IServerPlayer actor, CompanionRecoveryRequestPacket packet)
    {
        // Consume before any validation: a stale, denied or repeated response can
        // never turn into a later valid execution. Old modal cancellation must not
        // cancel a newer preview issued to the same actor.
        if (!recoveryUiConfirmations.TryGetValue(actor.PlayerUID, out var c) || c.Token != packet.ConfirmationToken) {
            if (packet.Action != CompanionRecoveryRequestPacket.CancelPreview) SendCompanionRecoveryState(actor, "Confirmation missing, incorrect or already used; request a new preview");
            return;
        }
        recoveryUiConfirmations.Remove(actor.PlayerUID);
        if (packet.Action == CompanionRecoveryRequestPacket.CancelPreview) { SendCompanionRecoveryState(actor, "Recovery cancelled; no animal changed"); return; }
        if (serverApi == null || packRepository?.Loaded != true || !companionContentReady || !CanUseDeveloperTools(actor)
            || !ReferenceEquals(actor, c.Actor) || actor.ConnectionState != EnumClientState.Playing
            || packet.TargetOwnerUid != c.Owner || !DeveloperMayControlOwner(actor, c.Owner) || serverApi.World.ElapsedMilliseconds > c.Until) {
            SendCompanionRecoveryState(actor, "Recovery confirmation expired or selected-player permission changed; no animal changed"); return;
        }
        if (!c.Bulk) {
            var target = c.Targets[0];
            string blocker;
            FoxPackRecordV2? record; Entity? original;
            try { blocker = RevalidateRecoveryUi(actor, c, target, false, out record, out original); }
            catch (Exception ex) { SendCompanionRecoveryState(actor, "Confirmation preflight stopped: " + ex.GetType().Name + "; collect /companiondiag"); return; }
            if (blocker.Length > 0) { LogRecoveryAttempt(target.FoxId, "confirmation-refused", blocker); SendCompanionRecoveryState(actor, "Recovery refused: " + blocker); return; }
            DispatchRecoveryUi(actor, c.Owner, target, record!, original, result => SendCompanionRecoveryState(actor, result.Kind + ": " + result.Reason));
            return;
        }
        RunConfirmedRecoveryUiBatch(actor, c);
    }

    private void DispatchRecoveryUi(IServerPlayer actor, string owner, RecoveryUiTarget target, FoxPackRecordV2 record, Entity? original, Action<RecoveryResult> completed)
    {
        LogRecoveryAttempt(target.FoxId, "dispatch", target.Mode);
        if (target.Mode == ForceEmergencyMode) { StartForceEmergency(actor, record); return; }
        if (target.Mode == ForceUndoMode) { StartForceUndo(actor, record); return; }
        if (target.Mode == "Emergency rebuild") { StartEmergencyRebuild(actor, record); return; }
        if (target.Mode == "Emergency undo") { StartEmergencyUndo(actor, record); return; }
        if (target.Mode == "Completed Hold") DispatchConfirmedCompletedHoldRecovery(actor, record, completed);
        else StartFullCompanionRecovery(actor, target.FoxId, owner, completed);
    }

    private void RunConfirmedRecoveryUiBatch(IServerPlayer actor, RecoveryUiConfirmation c)
    {
        if (!pendingRecoveryBatches.Add(c.Owner)) { SendCompanionRecoveryState(actor, "A recovery batch for this selected player is already pending"); return; }
        int index = 0, recovered = 0, skipped = c.PreviewSkipped, failed = 0;
        void Next() {
            if (serverApi == null) { pendingRecoveryBatches.Remove(c.Owner); return; }
            if (index == c.Targets.Length) {
                pendingRecoveryBatches.Remove(c.Owner);
                SendCompanionRecoveryState(actor, $"Player {c.Owner}: {recovered} rebuilt, {skipped} skipped (including preview skips), {failed} failed. Only the confirmed preview manifest was processed."); return;
            }
            var target = c.Targets[index++];
            bool reported = false;
            void Result(RecoveryResult result) {
                if (reported) return;
                reported = true;
                LogRecoveryAttempt(target.FoxId, "batch-result-" + result.Kind, result.Reason);
                if (result.Kind == "recovered") recovered++; else if (result.Kind == "failed") failed++; else skipped++;
                // One honest result for each attempted animal, including failures.
                try { SendAdminCompanionMessage(actor, target.FoxId + ": " + result.Kind + ": " + result.Reason); }
                finally { serverApi?.Event.RegisterCallback(_ => Next(), 50); }
            }
            try {
                string blocker = RevalidateRecoveryUi(actor, c, target, true, out var record, out var original);
                if (blocker.Length > 0) { Result(new("skipped", blocker)); return; }
                DispatchRecoveryUi(actor, c.Owner, target, record!, original, Result);
            } catch (Exception ex) { Result(new("failed", "Recovery preflight stopped: " + ex.GetType().Name + "; inspect /companiondiag")); }
        }
        SendCompanionRecoveryState(actor, $"Player {c.Owner}: confirmed {c.Targets.Length} previewed animals; results pending. No new roster entries will be added.");
        serverApi!.Event.RegisterCallback(_ => Next(), 50);
    }

    private void DispatchConfirmedCompletedHoldRecovery(IServerPlayer actor, FoxPackRecordV2 record, Action<RecoveryResult>? completed = null)
    {
        confirmedHoldRebuilds.Add(record.FoxId);
        try { StartFullCompanionRecovery(actor, record.FoxId, record.OwnerUid, completed); }
        finally { confirmedHoldRebuilds.Remove(record.FoxId); }
    }

    internal void RespondToRecoveryPreview(CompanionRecoveryStatePacket preview, bool confirm)
    {
        SendCompanionRecoveryAction(confirm ? CompanionRecoveryRequestPacket.ConfirmPreview : CompanionRecoveryRequestPacket.CancelPreview,
            confirmationToken: preview.PreviewToken, targetOwnerUid: preview.TargetOwnerUid);
    }
}
