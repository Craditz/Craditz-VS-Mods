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
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;

public sealed partial class FoxPackRecordV2
{
    // One bounded forensic checkpoint. Never an automatic inventory/outcome source.
    [ProtoMember(153)] public byte[] CompletedHoldRecoveryNativeEscrow = Array.Empty<byte>();
    [ProtoMember(154)] public byte[] CompletedHoldRecoveryLedgerEscrow = Array.Empty<byte>();
    [ProtoMember(155)] public string CompletedHoldRecoveryAudit = string.Empty;
}

public sealed partial class FeralKinshipCompanionSystem
{
    private const string StaleAuthorityHold = "Native/ledger phase, owner, identity or trip mismatch; preserve original";
    private sealed record HoldRecoveryConfirmation(string Token, string FoxId, string Owner, long NativeId,
        Entity Original, string Fingerprint, long Until);
    private readonly Dictionary<string,HoldRecoveryConfirmation> holdRecoveryConfirmations = new(StringComparer.Ordinal);
    private readonly HashSet<string> confirmedHoldRebuilds = new(StringComparer.Ordinal);

    private void RegisterCompletedHoldRecovery(ICoreServerAPI api)
    {
        api.ChatCommands.Create("companionrepair").WithDescription("Attempt confirmed recovery of a verified completed original on Hold: [cart] native:<ID>, then confirm <token>.")
            .RequiresPlayer().RequiresPrivilege(Privilege.controlserver).WithArgs(api.ChatCommands.Parsers.OptionalAll("selector-or-confirm"))
            .HandleWith(args => {
                if(args.Caller.Player is not IServerPlayer actor)return TextCommandResult.Error("A server admin is required.");
                try { return HandleCompletedHoldRecovery(actor,args.Parsers.Count>0?((string?)args[0]??string.Empty).Trim():string.Empty); }
                catch(Exception ex) {
                    serverApi?.Logger.Warning("[CompanionRepair] Preflight stopped: {0}. No unverified retry is allowed.",ex.GetType().Name);
                    return TextCommandResult.Error("Recovery stopped: "+ex.GetType().Name+". Inspect server-main and /companiondiag before retrying.");
                }
            });
    }
    private static byte[] HoldRecoveryRecordBytes(FoxPackRecordV2 r)
    {
        using var stream=new MemoryStream();Serializer.Serialize(stream,r);
        if(stream.Length==0||stream.Length>1024*1024)throw new InvalidOperationException("Ledger forensic checkpoint exceeds 1 MiB");
        return stream.ToArray();
    }
    private static string HoldRecoveryFingerprint(FoxPackRecordV2 r,Entity e)
    {
        return RecoveryUiFingerprint(r,e);
    }
    private string CompletedHoldRepairBlocker(FoxPackRecordV2 r,Entity e,ExpeditionPresentationAuthority authority)
    {
        if(!e.Alive||e.ShouldDespawn||e.Pos.Dimension!=0||r.Archived||r.MissingContentArchive?.Active==true)return "Original must be living, unarchived and in dimension 0";
        if(r.DeveloperRecoveryNativeEscrow.Length>0)return "Unresolved prior recovery escrow; inspect before retrying";
        if(r.CompletedHoldRecoveryNativeEscrow.Length>0||r.CompletedHoldRecoveryLedgerEscrow.Length>0||!string.IsNullOrEmpty(r.CompletedHoldRecoveryAudit))return "An earlier manual recovery checkpoint already exists; inspect before another recovery";
        var m=RetainedNativeSuspension.Marker(e);
        if(r.RetainedPhase!="Complete"||m?.GetString("phase")!="Hold"||m.GetString("reason")!=StaleAuthorityHold)return "Only the observed completed-ledger authority-mismatch Hold is supported";
        if(!RetainedNativeSuspension.Matches(e,r))return "Native/marker/ledger owner, companion, native ID, code or nonempty trip differs; preserve original";
        if(CompanionContentIdentity.ExactCode(r.EntityCode)?.ToString()!=e.Code?.ToString())return "Saved ledger entity code is missing or differs from the native original";
        if(serverApi!.World.LoadedEntities.Values.Concat(retainedAdmissionBodies.Values).Concat(loadedFoxes.Values).Any(other=>!ReferenceEquals(other,e)
            &&(RetainedNativeSuspension.Marker(other)?.GetString("companionId")==r.FoxId
                ||RetainedNativeSuspension.Marker(other)?.GetLong("nativeId")==e.EntityId
                ||RetainedNativeSuspension.Marker(other)?.GetString("trip")==r.RetainedTrip)))return "Another native marker claims this identity/trip; preserve both originals";
        if(!authority.Records.TryGetValue(r.FoxId,out var current)||!ReferenceEquals(current,r))return "Canonical ledger record changed";
        if(r.Status==null||r.Status.StartsWith("Away",StringComparison.OrdinalIgnoreCase)||r.Status.StartsWith("Leaving",StringComparison.OrdinalIgnoreCase)
            ||r.Status is "Dead" or "MIA" or "Running late" or "Returning" or "Awaiting safe return")return "Ledger is not independently present/completed";
        if(string.IsNullOrEmpty(r.RetainedOutcome)||!string.IsNullOrEmpty(m.GetString("outcome"))&&m.GetString("outcome")!=r.RetainedOutcome)return "Return outcome is missing or conflicts; no outcome replay allowed";
        if(!RetainedNativeSuspension.SafeSnapshot(e))return "Original native snapshot is invalid";
        double capture=m.GetDouble("capturedHours",double.NaN),now=serverApi!.World.Calendar.TotalHours;
        double resume=m.HasAttribute("resumeHours")?m.GetDouble("resumeHours",double.NaN):now;
        if(!double.IsFinite(capture)||capture<0||!double.IsFinite(now)||!double.IsFinite(resume)||resume<capture||resume>now)return "Native pause-clock receipt is invalid";
        if(m.GetString("legacyAuthority",string.Empty)!=r.RetainedLegacyAuthority
            ||!string.IsNullOrEmpty(r.RetainedLegacyAuthority)&&(!double.IsFinite(r.RetainedLegacyAdmissionHours)||r.RetainedLegacyAdmissionHours<0
                ||m.GetDouble("legacyAdmissionHours",double.NaN)!=r.RetainedLegacyAdmissionHours||capture!=r.RetainedLegacyAdmissionHours))return "Legacy admission receipts conflict";
        var s=GetDomesticationStatus(e)!;
        if(r.Points<s.GetInt(FoxPointsKey,0)||r.BankedTalentPoints<s.GetInt(BankedTalentPointsKey,0))return "Native spendable progression exceeds ledger; preserve both authorities";
        return string.Empty;
    }
    private void PrepareCompletedHoldReplacement(Entity replacement,Entity original,FoxPackRecordV2 r)
    {
        replacement.Api=original.Api;replacement.World=original.World;
        if(!CanHydrateRetainedProgress(replacement,r))throw new InvalidOperationException("Ledger/native progression cannot be reconciled without rollback");
        if(!HydrateRetainedProgress(replacement,r))throw new InvalidOperationException("Ledger/native progression cannot be reconciled without rollback");
        RetainedNativeSuspension.PrepareManualCompletedRecovery(replacement,r);
        // A stale native Away/Leaving label can otherwise be projected back into
        // the ledger by ordinary registration even after the receipt is retired.
        var status=GetDomesticationStatus(replacement)!;
        status.SetString(ExpeditionStatusKey,string.Empty);status.SetDouble(ExpeditionStatusExpiresDayKey,0);
    }
    private TextCommandResult HandleCompletedHoldRecovery(IServerPlayer actor,string input)
    {
        if(serverApi==null||packRepository?.Loaded!=true||!companionContentReady
            ||(!actor.HasPrivilege(Privilege.controlserver)&&!actor.HasPrivilege(Privilege.root)))return TextCommandResult.Error("Initialized server and admin privilege required.");
        long now=serverApi.World.ElapsedMilliseconds;
        if(input.StartsWith("confirm ",StringComparison.OrdinalIgnoreCase)) {
            if(!holdRecoveryConfirmations.Remove(actor.PlayerUID,out var c)||c.Token!=input[8..].Trim()||now>c.Until)return TextCommandResult.Error("Confirmation missing, incorrect or expired. Inspect the target again.");
            if(!DeveloperMayControlOwner(actor,c.Owner)||!packRepository.TryGetRecord(c.FoxId,out var r)||r==null||r.OwnerUid!=c.Owner||r.EntityId!=c.NativeId
                ||!ReferenceEquals(serverApi.World.GetEntityById(c.NativeId),c.Original))return TextCommandResult.Error("Target ownership/original changed; no recovery attempted.");
            string blocker=ConfirmedHoldRecoveryPreflight(r,c.Original);
            if(blocker.Length>0)return TextCommandResult.Error("Recovery refused: "+blocker);
            if(HoldRecoveryFingerprint(r,c.Original)!=c.Fingerprint)return TextCommandResult.Error("Native or ledger receipt/progression changed; inspect and confirm again.");
            DispatchConfirmedCompletedHoldRecovery(actor,r);
            return TextCommandResult.Success("Confirmed recovery dispatched once. Inspect recovery result and /companiondiag; server commit is distinct from client rendering.");
        }
        foreach(string id in holdRecoveryConfirmations.Where(x=>now>x.Value.Until).Select(x=>x.Key).ToArray())holdRecoveryConfirmations.Remove(id);
        holdRecoveryConfirmations.Remove(actor.PlayerUID);
        string owner=actor.PlayerUID;
        if(input.StartsWith("cart ",StringComparison.OrdinalIgnoreCase)) {
            if(!developerCartTargets.TryGetValue(actor.PlayerUID,out var target)||!DeveloperMayControlOwner(actor,target.Owner))return TextCommandResult.Error("Select the nearby owner's cart with a held Developer Ledger first.");
            owner=target.Owner;input=input[5..].Trim();
        }
        if(!input.StartsWith("native:",StringComparison.OrdinalIgnoreCase)||!long.TryParse(input[7..],out long native)||native<=0)return TextCommandResult.Error("Use /companionrepair [cart] native:<ID from /companiondiag list>. Then confirm the displayed one-use token.");
        var matches=packRepository.GetAllRecords().Where(r=>r.OwnerUid==owner&&r.EntityId==native).Take(2).ToArray();
        if(matches.Length!=1)return TextCommandResult.Error("No unique record in the authorized owner scope.");
        var record=matches[0];Entity? original=serverApi.World.GetEntityById(native);
        if(original==null)return TextCommandResult.Error("Native original is not loaded; no body or inventory was fabricated.");
        string reason=ConfirmedHoldRecoveryPreflight(record,original);
        if(reason.Length>0)return TextCommandResult.Error("Recovery refused: "+reason+". Collect /companiondiag.");
        if(holdRecoveryConfirmations.Count>=128)return TextCommandResult.Error("Confirmation limit reached; wait for existing confirmations to expire.");
        string token=Guid.NewGuid().ToString("N");
        holdRecoveryConfirmations[actor.PlayerUID]=new(token,record.FoxId,owner,native,original,HoldRecoveryFingerprint(record,original),now+60000);
        return TextCommandResult.Success("ATTEMPT only: "+DiagnosticText(record.Name,60)+" native "+native+" has a completed ledger but a held original. A fresh native body will replace that original with the same identity and inventory; current health is preserved and compatible ledger progression projected. Expedition outcomes, loot and rewards are NOT replayed; missing historical rewards cannot be inferred. Native and ledger checkpoints will be retained. To accept these terms within 60 seconds: /companionrepair confirm "+token);
    }
    private string ConfirmedHoldRecoveryPreflight(FoxPackRecordV2 r,Entity e) => ConfirmedHoldRecoveryPreflightForBatch(r,e,false);
    private string ConfirmedHoldRecoveryPreflightForBatch(FoxPackRecordV2 r,Entity e,bool inBatch)
    {
        if(r.SaveMigration!=null&&r.SaveMigration.Version!=1)return "Unknown migration schema; preserve original and inspect /companiondiag";
        if(r.SaveMigration?.NativeEscrow.Length>0&&r.SaveMigration.Trip==r.RetainedTrip&&r.SaveMigration.State!="Done")return "Migration has a partial checkpoint; inspect /companionmigration native:"+r.EntityId+" and retry its guarded reconciliation before reconstruction";
        if(packRepository?.CanCheckpointRecovery!=true)return "Ledger checkpoint is unavailable or a save batch is open";
        if(pendingFullRebuilds.Contains(r.FoxId)||!inBatch&&pendingRecoveryBatches.Contains(r.OwnerUid))return "Recovery already pending";
        if(r.RetainedPhase!="Complete"||RetainedNativeSuspension.Marker(e)?.GetString("phase")!="Hold")return "Only a completed ledger with an original on Hold is supported";
        confirmedHoldRebuilds.Add(r.FoxId);
        try {
            string blocker=FullRecoveryBlocker(r,e);if(blocker.Length>0)return blocker;
            // Native Entity.ToBytes invokes provider serialization hooks and can flush
            // state. Preview must not invoke those hooks. Inspect a detached watched
            // tree; exact factory/native deserialization is proved after confirmation,
            // before removing the original, by the existing escrow rebuild path.
            var clone=new EntityAgent {EntityId=e.EntityId,Code=e.Code,Api=e.Api,World=e.World};
            using(var bytes=new MemoryStream()) {
                using(var writer=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true))e.WatchedAttributes.ToBytes(writer);
                bytes.Position=0;using var reader=new BinaryReader(bytes);clone.WatchedAttributes.FromBytes(reader);
            }
            PrepareCompletedHoldReplacement(clone,e,r);return string.Empty;
        } catch(Exception ex) {return "Detached native/progression preflight failed: "+ex.Message;}
        finally {confirmedHoldRebuilds.Remove(r.FoxId);}
    }
}

internal static partial class RetainedNativeSuspension
{
    internal static void PrepareManualCompletedRecovery(Entity detached,FoxPackRecordV2 r)
    {
        if(!Matches(detached,r)||!SafeSnapshot(detached)||r.RetainedPhase!="Complete"||Marker(detached)?.GetString("phase")!="Hold")throw new InvalidOperationException("Manual completed authority changed");
        // Restore original clock snapshots absolutely, once. No outcome callback or reward path is invoked.
        RestoreClocks(detached,Marker(detached)!);
    }
}
