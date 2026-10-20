#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    internal bool CanUseEmergencyAdminClient() => clientApi?.World.Player != null
        && (clientApi.World.Player.HasPrivilege(Privilege.controlserver) || clientApi.World.Player.HasPrivilege(Privilege.root));

    private bool CanViewEmergency(IServerPlayer actor, string id) => MayRepairCompletedHold(actor)
        && packRepository?.TryGetRecord(id, out var r) == true && r != null && DeveloperMayControlOwner(actor, r.OwnerUid);
    private bool CanUndoEmergency(IServerPlayer actor, string id) => CanViewEmergency(actor, id)
        && packRepository!.TryGetRecord(id, out var r) && r != null && LastEmergency(r) is { } j && j.Version is 1 or 2 or 3 && j.Stage != "Undone";
    private string EmergencyUiReport(IServerPlayer actor, string id) => CanViewEmergency(actor, id)
        && packRepository!.TryGetRecord(id, out var r) && r != null ? EmergencyReport(r) : string.Empty;

    private void PreviewEmergencyUi(IServerPlayer actor, string owner, string foxId, bool undo)
    {
        recoveryUiConfirmations.Remove(actor.PlayerUID);
        if (!MayRepairCompletedHold(actor) || !DeveloperMayControlOwner(actor, owner)
            || packRepository?.TryGetRecord(foxId, out var r) != true || r == null || r.OwnerUid != owner) {
            SendCompanionRecoveryState(actor, "Emergency rebuild and Undo are server-admin-only and scoped to the explicitly selected owner"); return;
        }
        if (!undo || LastEmergency(r) is { Version: 2 or 3 }) { PreviewForceEmergency(actor, owner, r, undo); return; }
        var lookup = LookupRecoveryOriginal(r);
        var original = lookup.State=="Found"?lookup.Candidates[0].Body:null;
        string blocker = lookup.State is not ("Found" or "Absent") ? lookup.Reason : EmergencyAdminBlocker(actor, r, undo);
        if (!undo && original != null && (!NativeRecoveryPublished(original) || EmergencyBodyBlocker(original).Length > 0 || original.Pos.Dimension != 0))
            blocker = "Original native publication/dimension is not safe: " + RecoveryPublicationReason(original);
        var j = LastEmergency(r);
        var recovered = new List<string>(); var defaulted = new List<string>();
        if (j != null && j.Stage != "Undone") { recovered.AddRange(j.Recovered); defaulted.AddRange(j.Defaulted); }
        else DescribeEmergencyFields(r, recovered, defaulted);
        var entries = recovered.Select(x => new CompanionRecoveryPreviewEntry { Name = GetFoxRecordLabel(r), Decision = "Recovered fields", Reason = x }).ToList();
        InspectSavedRecoveryCode(r,out string registeredCode);
        entries.Insert(0,new() {Name="Saved entity code",Decision=registeredCode,Reason=r.EntityCode.Length==0?"unavailable":r.EntityCode});
        entries.Insert(1,new() {Name="Native lookup",Decision=lookup.State,Reason=lookup.Candidates.Length==0?"No candidate in the world index, loaded entities, observed/registered bodies or loaded chunks; identity checks are unavailable.":string.Join(" | ",lookup.Candidates.Select(x=>DescribeRecoveryIdentity(r,x.Body,x.Source)))});
        entries.AddRange(defaulted.Select(x => new CompanionRecoveryPreviewEntry { Name = GetFoxRecordLabel(r), Decision = "Defaulted fields", Reason = x }));
        entries.Add(new() { Name = "Original cargo", Decision = "Separate escrow", Reason = j == null
            ? $"Pre-mutation ledger/backpack bytes ({r.BackpackItem.Length}) and any available full native original are checkpointed before replacement. Unavailable native cargo remains UNKNOWN; late originals are quarantined."
            : $"Undo checkpoint retained; {j.Cargo.Count} separate native escrow entries, {j.Cargo.Sum(x => (long)x.Native.Length)} bytes. Uncertain original ledger backpack remains in the immutable checkpoint, even if native inventory differs. No uncertain cargo is merged into live slots or overwritten by Undo." });
        if (undo) entries.Insert(0, new() { Name = "Undo behavior", Decision = "Restore saved checkpoint", Reason =
            "Withdraw and separately escrow the replacement first. Restore the prior ledger and original native snapshot, or reuse an actual late original without overwriting its current cargo. An originally missing body returns to missing state if no native original is available." });
        if (j != null) entries.Add(new() { Name = "Persistent operation", Decision = j.Stage,
            Reason = $"Checkpoint {j.Id}; original native bytes {j.BeforeNative.Length}; ledger bytes {j.BeforeLedger.Length}. " + j.UndoResult + (j.Error.Length > 0 ? "Waiting: " + j.Error : string.Empty) });
        if (blocker.Length > 0) entries.Insert(0, new() { Name = "Action unavailable", Decision = "Skip", Reason = blocker });
        LogRecoveryAttempt(r.FoxId,undo?"emergency-undo-preview":"emergency-preview",$"lookup={lookup.State}; saved-code={IdentityValue(r.EntityCode)}; registration={registeredCode}; blocker={(blocker.Length==0?"none":blocker)}");
        string mode = undo ? "Emergency undo" : "Emergency rebuild";
        string token = string.Empty;
        if (blocker.Length == 0) {
            if (recoveryUiConfirmations.Count >= 128) { SendCompanionRecoveryState(actor, "Recovery confirmation limit reached"); return; }
            token = Guid.NewGuid().ToString("N");
            recoveryUiConfirmations[actor.PlayerUID] = new(actor, token, owner, serverApi!.World.ElapsedMilliseconds + 60000, false,
                new[] { new RecoveryUiTarget(r.FoxId, r.EntityId, original, RecoveryUiFingerprint(r, original), mode) }, 0);
        }
        serverChannel?.SendPacket(new CompanionRecoveryStatePacket { TargetOwnerUid = owner, PreviewTitle = mode,
            PreviewToken = token, PreviewEntries = entries, Message = blocker.Length > 0 ? blocker :
                undo ? "ADMIN UNDO. Replacement cargo is retained separately before restoring the checkpoint. Review every field/cargo page. Confirm once within 60 seconds. Interrupted execution resumes from the saved journal."
                : "ADMIN EMERGENCY REBUILD. Missing native fields use explicit defaults. Original cargo remains separate, never guessed or copied. A persistent undo checkpoint is saved before mutation. Review every field page and confirm once within 60 seconds." }, actor);
    }
}
