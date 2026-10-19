#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

[ProtoContract] public sealed class RetainedSaveMigration
{
    [ProtoMember(1)] public int Version=1;
    [ProtoMember(2)] public string State="Queued";
    [ProtoMember(3)] public string Reason=string.Empty;
    [ProtoMember(4)] public string Trip=string.Empty;
    [ProtoMember(5)] public string Token=string.Empty;
    [ProtoMember(6)] public double ResumeHours;
    [ProtoMember(7)] public byte[] NativeEscrow=Array.Empty<byte>();
    [ProtoMember(8)] public byte[] LedgerEscrow=Array.Empty<byte>();
    [ProtoMember(9)] public int Attempts;
    [ProtoMember(10)] public long NextAttemptUtcMs;
    [ProtoMember(11)] public long NativeId;
    [ProtoMember(12)] public string Owner=string.Empty;
    [ProtoMember(13)] public string Code=string.Empty;
    [ProtoMember(14)] public string Outcome=string.Empty;
}
[ProtoContract] public sealed class RetainedCompletionProof
{
    [ProtoMember(1)] public int Version=1;
    [ProtoMember(2)] public string Trip=string.Empty;
    [ProtoMember(3)] public string Owner=string.Empty;
    [ProtoMember(4)] public string Companion=string.Empty;
    [ProtoMember(5)] public long NativeId;
    [ProtoMember(6)] public string Code=string.Empty;
    [ProtoMember(7)] public double CapturedHours;
    [ProtoMember(8)] public double ResumeHours;
    [ProtoMember(9)] public string Outcome=string.Empty;
}
public sealed partial class FoxPackRecordV2
{
    [ProtoMember(156)] public RetainedSaveMigration? SaveMigration;
    [ProtoMember(157)] public RetainedCompletionProof? CompletionProof;
}
public sealed partial class FeralKinshipCompanionSystem
{
    private long nextSaveMigrationMs,nextSaveMigrationSummaryMs;
    private int saveMigrationCursor;
    private readonly Queue<string> saveMigrationWake=new();
    private readonly HashSet<string> saveMigrationWoken=new(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<Entity,object> saveMigrationObserved=new();
    private string lastSaveMigrationSummary=string.Empty;

    private void WakeSaveMigration(Entity e)
    {
        if(serverApi==null||packRepository?.Loaded!=true||!ReferenceEquals(e.Api,serverApi))return;
        string id=GetDomesticationStatus(e)?.GetString(FoxIdKey,string.Empty)??string.Empty;
        if(string.IsNullOrEmpty(id)||!packRepository.TryGetRecord(id,out var r)||r==null)return;
        var q=r.SaveMigration;
        if(q!=null&&q.Version!=1)return;
        bool seen=saveMigrationObserved.TryGetValue(e,out _);if(!seen)saveMigrationObserved.Add(e,new());
        if(seen&&(q?.State=="ManualNeeded"||q?.State=="Deferred"&&UtcNowMs()<q.NextAttemptUtcMs))return;
        if(q?.State=="Done"&&!RetainedNativeSuspension.CompletionSettled(e,r)&&q.Trip==r.RetainedTrip)q.State=q.NativeEscrow.Length>0?"Prepared":"Queued";
        if((q?.State is "Done" or "NotNeeded" or "Excluded") && (string.IsNullOrEmpty(r.RetainedTrip)&&RetainedNativeSuspension.Marker(e)==null
            ||RetainedNativeSuspension.CompletionSettled(e,r))&&loadedFoxes.TryGetValue(e.EntityId,out var loaded)&&ReferenceEquals(loaded,e)&&!RetainedNativeSuspension.Protected(e))return;
        if(saveMigrationWoken.Count<8192&&saveMigrationWoken.Add(id))saveMigrationWake.Enqueue(id);
    }
    private string SaveMigrationSummary()
    {
        if(packRepository?.Loaded!=true)return "Migration unavailable: ledger not initialized";
        var rows=packRepository.GetAllRecords();
        return "version=1 "+string.Join(" ",new[]{"Done","Deferred","ManualNeeded","Prepared","Queued","NotNeeded","Excluded"}
            .Select(s=>s+"="+rows.Count(r=>r.SaveMigration?.Version==1&&r.SaveMigration.State==s)))
            +" undiscovered="+rows.Count(r=>r.SaveMigration==null)+" unsupported="+rows.Count(r=>r.SaveMigration!=null&&r.SaveMigration.Version!=1);
    }
    private void RegisterSaveMigrationDiagnostics(ICoreServerAPI api)
    {
        api.ChatCommands.Create("companionmigration").WithDescription("Read migration counts; native:<ID> for status; retry native:<ID> rechecks authority without clearing Hold.")
            .RequiresPlayer().RequiresPrivilege(Privilege.controlserver).WithArgs(api.ChatCommands.Parsers.OptionalAll("status-or-retry"))
            .HandleWith(args=>{
                if(args.Caller.Player is not IServerPlayer actor||(!actor.HasPrivilege(Privilege.controlserver)&&!actor.HasPrivilege(Privilege.root))
                    ||packRepository?.Loaded!=true)return TextCommandResult.Error("Initialized server/admin privilege required");
                string text=args.Parsers.Count>0?((string?)args[0]??string.Empty).Trim():string.Empty;
                if(text.Length==0||text=="status")return TextCommandResult.Success(SaveMigrationSummary());
                bool retry=text.StartsWith("retry ",StringComparison.OrdinalIgnoreCase);if(retry)text=text[6..].Trim();
                if(!text.StartsWith("native:",StringComparison.OrdinalIgnoreCase)||!long.TryParse(text[7..],out long native))return TextCommandResult.Error("Use /companionmigration [retry] native:<ID>");
                var found=packRepository.GetAllRecords().Where(r=>r.EntityId==native).Take(2).ToArray();
                if(found.Length!=1)return TextCommandResult.Error("No unique native ledger identity; inspect /companiondiag");
                var r=found[0];var q=r.SaveMigration;
                if(retry){
                    if(q!=null&&q.Version!=1||packRepository.CanCheckpointRecovery!=true)return TextCommandResult.Error("Unknown migration schema or checkpoint unavailable; unchanged");
                    q??=r.SaveMigration=new();q.NextAttemptUtcMs=0;q.State=q.NativeEscrow.Length>0?"Prepared":"Queued";
                    try{packRepository.Save();}catch(Exception ex){return TextCommandResult.Error("Retry checkpoint unavailable: "+ex.GetType().Name+"; no animal changed. Inspect server-main.");}
                    if(saveMigrationWoken.Add(r.FoxId))saveMigrationWake.Enqueue(r.FoxId);
                }
                return TextCommandResult.Success("native="+r.EntityId+" owner="+r.OwnerUid+" migration="+(q?.Version??0)+"/"+(q?.State??"undiscovered")
                    +" reason="+DiagnosticText(q?.Reason,400)+" attempts="+(q?.Attempts??0)+" escrow="+(q?.NativeEscrow.Length??0)+". No Hold or outcome was reset.");
            });
    }
    private void ResetSaveMigrationRuntime()
    {
        nextSaveMigrationMs=nextSaveMigrationSummaryMs=0;saveMigrationCursor=0;saveMigrationWake.Clear();saveMigrationWoken.Clear();saveMigrationObserved.Clear();lastSaveMigrationSummary=string.Empty;
    }
    internal static void ClearMigratedExpeditionStatus(Entity e)
    {
        var s=GetDomesticationStatus(e)!;s.SetBool(ExpeditionAwayKey,false);s.SetString(ExpeditionStatusKey,string.Empty);s.SetDouble(ExpeditionStatusExpiresDayKey,0);
    }
    private void MaintainRetainedSaveMigration()
    {
        if(serverApi==null||packRepository?.CanCheckpointRecovery!=true||!companionContentReady||!RetainedNativeSuspension.Enabled
            ||RuntimeEnv.ServerMainThreadId<=0||Environment.CurrentManagedThreadId!=RuntimeEnv.ServerMainThreadId)return;
        long elapsed=serverApi.World.ElapsedMilliseconds;if(elapsed<nextSaveMigrationMs)return;nextSaveMigrationMs=elapsed+2000;
        var rows=packRepository.GetAllRecords();if(rows.Count>8192)return;
        bool changed=false;
        // Discover every owner's records, including offline owners. Discovery is
        // bounded separately from expensive body processing; state lives per row.
        for(int i=0;i<Math.Min(64,rows.Count);i++) {
            if(saveMigrationCursor>=rows.Count)saveMigrationCursor=0;
            var r=rows[saveMigrationCursor++];
            if(r.SaveMigration==null){r.SaveMigration=new();changed=true;}
            if(saveMigrationWoken.Count<8192&&r.SaveMigration.Version==1&&r.SaveMigration.State is not ("NotNeeded" or "Excluded")
                &&UtcNowMs()>=r.SaveMigration.NextAttemptUtcMs&&saveMigrationWoken.Add(r.FoxId))saveMigrationWake.Enqueue(r.FoxId);
        }
        if(changed){try{packRepository.Save();}catch{nextSaveMigrationMs=elapsed+30000;return;}}
        var authority=CaptureExpeditionPresentationAuthority();if(authority==null)return;
        for(int work=0;work<8&&saveMigrationWake.Count>0;work++) {
            string id=saveMigrationWake.Dequeue();saveMigrationWoken.Remove(id);
            if(!packRepository.TryGetRecord(id,out var r)||r==null||r.SaveMigration!=null&&r.SaveMigration.Version!=1)continue;
            r.SaveMigration??=new();
            try{ProcessSaveMigration(r,authority);}catch(Exception ex){
                var q=r.SaveMigration;
                bool tooLarge=ex is InvalidOperationException&&(ex.Message=="Native snapshot exceeds the 8 MiB recovery limit"||ex.Message=="Ledger forensic checkpoint exceeds 1 MiB");
                q.State=tooLarge?"ManualNeeded":q.NativeEscrow.Length>0?"Prepared":"Deferred";
                q.Reason=tooLarge?"Checkpoint exceeds bounded recovery size; original preserved; admin/backup inspection required"
                    :"Interrupted: "+ex.GetType().Name+"; "+(q.NativeEscrow.Length>0?"in-memory checkpoint retained; disk persistence unconfirmed":"original retained; no migration checkpoint accepted")+"; inspect /companionmigration";
                q.NextAttemptUtcMs=tooLarge?long.MaxValue:UtcNowMs()+30000;
                try{packRepository.Save();}catch{ /* In-memory queue/escrow is retained; no claimed completion. */ }
            }
        }
        if(elapsed>=nextSaveMigrationSummaryMs){nextSaveMigrationSummaryMs=elapsed+60000;string summary=SaveMigrationSummary();if(summary!=lastSaveMigrationSummary){lastSaveMigrationSummary=summary;serverApi.Logger.Notification("[CompanionMigration] {0}",summary);}}
    }
    private void MigrationState(FoxPackRecordV2 r,string state,string reason,long wait=0)
    {
        var q=r.SaveMigration!;q.State=state;q.Reason=reason;q.NextAttemptUtcMs=state=="ManualNeeded"?long.MaxValue:UtcNowMs()+wait;
        try {packRepository!.Save();}catch {if(state=="Done")q.State="Prepared";throw;}
    }
    private string MigrationIdentityBlocker(FoxPackRecordV2 r,Entity e,ExpeditionPresentationAuthority? authority)
    {
        if(authority==null)return "Unknown initialized server authority";
        if(string.IsNullOrWhiteSpace(r.OwnerUid)||string.IsNullOrWhiteSpace(r.FoxId))return "Saved ownership/companion identity is missing; preserve original";
        if(authority.Conflicts.Contains(r.FoxId)||authority.NativeConflicts.Contains(r.EntityId))return "Duplicate native/companion identity";
        if(authority.Assigned.Contains(r.FoxId)||!string.IsNullOrEmpty(r.PendingDepartureStatus)||r.PendingDepartureAtUtcMs>0||r.PendingDepartureDeadlineUtcMs>0
            ||!string.IsNullOrEmpty(r.PendingReturnStatus)||r.PendingReturnAtUtcMs>0)return "Active or pending journey; deferred";
        if(!authority.Records.TryGetValue(r.FoxId,out var current)||!ReferenceEquals(current,r)||r.RetainedPhase!="Complete"
            ||!RetainedNativeSuspension.Matches(e,r)||!RetainedNativeSuspension.SafeSnapshot(e))return "Complete-trip native/ledger identity, trip or snapshot cannot be verified";
        var receipt=RetainedNativeSuspension.Marker(e)!;double capture=receipt.GetDouble("capturedHours",double.NaN),now=serverApi!.World.Calendar.TotalHours;
        double resume=receipt.HasAttribute("resumeHours")?receipt.GetDouble("resumeHours",double.NaN):now;
        if(!double.IsFinite(capture)||capture<0||!double.IsFinite(now)||!double.IsFinite(resume)||resume<capture||resume>now)return "Invalid pause-clock receipt; preserve original";
        if(receipt.GetString("legacyAuthority",string.Empty)!=r.RetainedLegacyAuthority||!string.IsNullOrEmpty(r.RetainedLegacyAuthority)
            &&(!double.IsFinite(r.RetainedLegacyAdmissionHours)||r.RetainedLegacyAdmissionHours<0||receipt.GetDouble("legacyAdmissionHours",double.NaN)!=r.RetainedLegacyAdmissionHours||capture!=r.RetainedLegacyAdmissionHours))return "Legacy admission receipts conflict; preserve original";
        if(r.Status==null||r.Status.StartsWith("Away",StringComparison.OrdinalIgnoreCase)||r.Status.StartsWith("Leaving",StringComparison.OrdinalIgnoreCase)
            ||r.Status is "MIA" or "Running late" or "Returning" or "Awaiting safe return")return "Non-present ledger status conflicts with completion; preserve original";
        if(!ReferenceEquals(e.Api,serverApi)||!ReferenceEquals(e.World,serverApi!.World)||!e.Alive||e.ShouldDespawn||e.Teleporting||e.Pos.Dimension!=0
            ||e is not EntityAgent a||a.MountedOn!=null||!IsTamedFox(e)||CompanionContentIdentity.ExactCode(r.EntityCode)?.ToString()!=e.Code?.ToString())return "Original body/owner/code is unavailable or unsupported";
        var health=e.GetBehavior<EntityBehaviorHealth>();if(health==null||!float.IsFinite(health.Health)||health.Health<=0||!float.IsFinite(health.MaxHealth)||health.MaxHealth<=0)return "Original native health is unavailable; no automatic resurrection/healing";
        if(!RetainedNativeSuspension.Qualified(e,out string qualification))return "Native adapter qualification unavailable: "+qualification;
        if(!NativeRecoveryPublished(e))return "Native world/chunk publication is not ready; deferred";
        if(pendingFullRebuilds.Contains(r.FoxId)||pendingRecoveryBatches.Contains(r.OwnerUid)||pendingRecoveryChecks.Contains(r.FoxId)
            ||pendingAdminCompanionTeleports.Contains(r.OwnerUid+":"+r.FoxId))return "Another recovery is pending; deferred";
        if(HasFoxPendingCartPickup(e)||r.BackpackDeliveryActive||backpackPreflights.ContainsKey(e.EntityId)||backpackStorageAreaLoads.Contains(r.FoxId)
            ||backpackMissingEntityRecoveryChecks.Contains(r.FoxId))return "Cargo/delivery work is pending; deferred";
        if(r.DeveloperRecoveryNativeEscrow.Length>0)return "Unresolved reconstruction escrow; admin inspection required";
        if(serverApi.World.LoadedEntities.Values.Concat(retainedAdmissionBodies.Values).Concat(loadedFoxes.Values).Any(other=>!ReferenceEquals(other,e)
            &&(RetainedNativeSuspension.Marker(other)?.GetString("companionId")==r.FoxId||RetainedNativeSuspension.Marker(other)?.GetLong("nativeId")==e.EntityId
                ||RetainedNativeSuspension.Marker(other)?.GetString("trip")==r.RetainedTrip)))return "Competing native marker claims this trip/identity";
        return string.Empty;
    }
    private void ProcessSaveMigration(FoxPackRecordV2 r,ExpeditionPresentationAuthority authority)
    {
        var q=r.SaveMigration!;if(q.Version!=1)return;
        if(r.Archived||r.MissingContentArchive?.Active==true||r.Status=="Dead"||packRepository!.IsPermanentlyDeleted(r.FoxId)) {MigrationState(r,"Excluded","Archived/dead/deleted/missing-content record; no automatic resurrection",300000);return;}
        Entity? e=serverApi!.World.GetEntityById(r.EntityId);
        if(r.EntityId<=0||CompanionContentIdentity.ExactCode(r.EntityCode)==null){MigrationState(r,"ManualNeeded","Native identity/entity definition is absent from this historical record; restore source assets or inspect a backup",300000);return;}
        if(e==null){MigrationState(r,"Deferred","Original unloaded or missing. Waiting for natural entity load; no chunk force-load or replacement. Missing source mods/true deletion require restored assets or a backup",300000);return;}
        if(!authority.Conflicts.Contains(r.FoxId)&&!authority.NativeConflicts.Contains(r.EntityId)&&!authority.Assigned.Contains(r.FoxId)&&!string.IsNullOrWhiteSpace(r.OwnerUid)
            &&string.IsNullOrEmpty(r.PendingDepartureStatus)&&r.PendingDepartureAtUtcMs<=0&&r.PendingDepartureDeadlineUtcMs<=0&&string.IsNullOrEmpty(r.PendingReturnStatus)&&r.PendingReturnAtUtcMs<=0
            &&RetainedNativeSuspension.CompletionSettled(e,r)&&loadedFoxes.TryGetValue(e.EntityId,out var loaded)&&ReferenceEquals(loaded,e)&&!RetainedNativeSuspension.Protected(e)
            &&e.CollisionBox?.XSize is >0&&e.SelectionBox?.XSize is >0&&e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.HasExpeditionPresentationSuppression!=true) {
            if(q.State is "Done" or "NotNeeded")return;
            MigrationState(r,"NotNeeded","Already verified present original; ordinary return labels/state preserved",300000);return;
        }
        q.Attempts=Math.Min(int.MaxValue-1,q.Attempts)+1;
        if(string.IsNullOrEmpty(r.RetainedTrip)&&RetainedNativeSuspension.Marker(e)==null){MigrationState(r,"NotNeeded","No retained journey receipt; no animal changed",300000);return;}
        if(RetainedNativeSuspension.InProtectedTransition(e,r)){MigrationState(r,"Deferred","Protected return/migration is in progress",10000);return;}
        string blocker=MigrationIdentityBlocker(r,e,authority);
        if(blocker.Length>0){MigrationState(r,blocker.Contains("deferred")||blocker.StartsWith("Active")?"Deferred":"ManualNeeded",blocker+"; /companiondiag and /companionrepair native:"+r.EntityId,300000);return;}
        var marker=RetainedNativeSuspension.Marker(e)!;
        float nativeHealth=e.GetBehavior<EntityBehaviorHealth>()!.Health;
        bool settled=RetainedNativeSuspension.CompletionSettled(e,r);
        if(r.CompletionProof!=null&&!RetainedNativeSuspension.CompletionProofMatches(e,r)){MigrationState(r,"ManualNeeded","Stored completion proof conflicts with original or is an unknown version; preserve both authorities",300000);return;}
        if(!settled&&q.Trip.Length>0&&q.Trip!=r.RetainedTrip&&q.NativeEscrow.Length>0){MigrationState(r,"ManualNeeded","A previous trip checkpoint is preserved; a different trip needs admin inspection",300000);return;}
        var s=GetDomesticationStatus(e)!;
        if(r.Points<s.GetInt(FoxPointsKey,0)||r.BankedTalentPoints<s.GetInt(BankedTalentPointsKey,0)){MigrationState(r,"ManualNeeded","Native spendable progression exceeds ledger; preserve both authorities",300000);return;}
        if(!CanHydrateRetainedProgress(e,r)){MigrationState(r,"ManualNeeded","Native/ledger progression conflict; original unchanged",300000);return;}
        // Native entity bytes and mod data can survive different save generations.
        // An applied native receipt can restore its lost journal without replaying
        // outcomes, clocks, health or cargo. A random token/Hold cannot do so.
        if(!settled&&RetainedNativeSuspension.NativeMigrationReceiptMatches(e,r)) {
            if(q.NativeEscrow.Length==0) {
                var record=ClonePackRecordForRebuild(r);record.SaveMigration=null;
                q.NativeEscrow=SnapshotRecoveryNative(e);q.LedgerEscrow=HoldRecoveryRecordBytes(record);
                q.Trip=r.RetainedTrip;q.Token=marker.GetString("migrationToken");q.NativeId=e.EntityId;q.Owner=r.OwnerUid;q.Code=e.Code.ToString();q.Outcome=r.RetainedOutcome;q.ResumeHours=marker.GetDouble("resumeHours",double.NaN);
                MigrationState(r,"Prepared","Adopted verified applied native receipt after older ledger checkpoint; no replay");
            }
            if(q.Token!=marker.GetString("migrationToken")||q.Trip!=r.RetainedTrip||q.NativeId!=e.EntityId||q.Owner!=r.OwnerUid||q.Code!=e.Code.ToString()||q.Outcome!=r.RetainedOutcome){MigrationState(r,"ManualNeeded","Native migration receipt conflicts with persisted journal; unchanged",300000);return;}
            if(q.State!="Prepared")MigrationState(r,"Prepared","Rolling forward verified native receipt");
            RetainedNativeSuspension.RollForwardMigrationReceipt(e,r,()=>MigrationIdentityBlocker(r,e,CaptureExpeditionPresentationAuthority()).Length==0);
            settled=true;
        }
        if(!settled) {
            string reason=marker.GetString("reason");bool proof=RetainedNativeSuspension.CompletionProofMatches(e,r);
            bool supported=reason==StaleAuthorityHold||proof&&(reason=="Completed native receipt lacks a valid original snapshot/outcome"
                ||reason=="Present/Leaving native phase disagrees with saved Away flag"||reason.StartsWith("Resume callback failed: ",StringComparison.Ordinal));
            if(marker.GetString("phase")!="Hold"||!supported){MigrationState(r,"ManualNeeded","Unsupported/ambiguous Hold or interrupted outcome; unchanged. /companiondiag; admin /companionrepair native:"+r.EntityId,300000);return;}
            if(!proof){confirmedHoldRebuilds.Add(r.FoxId);
                try{blocker=FullRecoveryBlocker(r,e);}finally{confirmedHoldRebuilds.Remove(r.FoxId);}}
            if(blocker.Length>0){MigrationState(r,"ManualNeeded",blocker,300000);return;}
            // Validate projection and clock restoration on detached watched data.
            var probe=new EntityAgent{EntityId=e.EntityId,Code=e.Code,Api=e.Api,World=e.World};
            using(var stream=new MemoryStream()){using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))e.WatchedAttributes.ToBytes(w);stream.Position=0;using var reader=new BinaryReader(stream);probe.WatchedAttributes.FromBytes(reader);}
            try{PrepareCompletedHoldReplacement(probe,e,r);}catch(Exception ex){MigrationState(r,"ManualNeeded","Detached pause/progression preflight failed: "+ex.GetType().Name+"; original unchanged",300000);return;}
            if(q.NativeEscrow.Length==0) {
                byte[] bytes=SnapshotRecoveryNative(e);if(MigrationIdentityBlocker(r,e,CaptureExpeditionPresentationAuthority()).Length>0)throw new InvalidOperationException("Authority changed during checkpoint");
                var record=ClonePackRecordForRebuild(r);record.SaveMigration=null;
                byte[] ledger=HoldRecoveryRecordBytes(record);
                q.Trip=r.RetainedTrip;q.Token=Guid.NewGuid().ToString("N");q.NativeId=e.EntityId;q.Owner=r.OwnerUid;q.Code=e.Code.ToString();q.Outcome=r.RetainedOutcome;
                q.ResumeHours=marker.HasAttribute("resumeHours")?marker.GetDouble("resumeHours"):proof?r.CompletionProof!.ResumeHours:serverApi.World.Calendar.TotalHours;
                q.NativeEscrow=bytes;q.LedgerEscrow=ledger;MigrationState(r,"Prepared","Original checkpoint accepted; native settlement pending");
            }
            else if(q.State!="Prepared")MigrationState(r,"Prepared","Rechecking persisted intent against an older native checkpoint");
            if(q.Trip!=r.RetainedTrip||q.NativeId!=e.EntityId||q.Owner!=r.OwnerUid||q.Code!=e.Code.ToString()||q.Outcome!=r.RetainedOutcome||q.Token.Length!=32){MigrationState(r,"ManualNeeded","Persisted intent conflicts with current authority; original checkpoint preserved",300000);return;}
            RetainedNativeSuspension.ApplyMigrationSettlement(e,r,q,()=>{if(!HydrateRetainedProgress(e,r))throw new InvalidOperationException("Progression conflict; preserved");},
                ()=>MigrationIdentityBlocker(r,e,CaptureExpeditionPresentationAuthority()).Length==0);
        }
        // A native receipt may survive a crash before its ledger Done checkpoint.
        // No outcome or clock operation is repeated on this roll-forward path.
        if(!RetainedNativeSuspension.CompletionSettled(e,r)||MigrationIdentityBlocker(r,e,CaptureExpeditionPresentationAuthority()).Length>0)throw new InvalidOperationException("Native settlement could not be verified");
        if(!HydrateRetainedProgress(e,r)){MigrationState(r,"ManualNeeded","Native progression conflicts with ledger; no rollback projection",300000);return;}
        ledgerHydratedEntities.Add(e.EntityId);retainedLoadedHealthApplied.Add(e);
        RetainedNativeSuspension.AdmitMigrationSettlement(e,r);
        RegisterLoadedFox(e);
        var finalHealth=e.GetBehavior<EntityBehaviorHealth>()!;
        // Native max-health recalculation can refill a full bar. Preserve the
        // measured original current health without MarkDirty's second refill.
        if(float.IsFinite(finalHealth.Health)&&finalHealth.Health>nativeHealth){finalHealth.Health=nativeHealth;e.WatchedAttributes.MarkPathDirty("health");}
        if(!loadedFoxes.TryGetValue(e.EntityId,out var registered)||!ReferenceEquals(registered,e)||RetainedNativeSuspension.Protected(e)
            ||e.CollisionBox?.XSize is not >0||e.SelectionBox?.XSize is not >0)throw new InvalidOperationException("Original registration/presentation not yet verified");
        if(finalHealth.Health!=nativeHealth){RetainedNativeSuspension.Hold(e,"Migration native health changed; escrow retained for inspection");MigrationState(r,"ManualNeeded","Native health decreased/invalid during registration; no automatic healing rollback",300000);return;}
        r.CurrentHealth=finalHealth.Health;r.MaxHealth=finalHealth.MaxHealth;r.HealthState=e.WatchedAttributes.GetInt(EntityHealthStateKey);
        if(!RetainedNativeSuspension.CompletionSettled(e,r)||MigrationIdentityBlocker(r,e,CaptureExpeditionPresentationAuthority()).Length>0){MigrationState(r,"Deferred","Authority changed after registration; no migration completion claimed",300000);return;}
        MigrationState(r,"Done",settled?"Verified completed original; no outcome/clock replay":"Original repaired in place; migration settlement, not an outcome replay",300000);
    }
}

internal static partial class RetainedNativeSuspension
{
    internal static bool InProtectedTransition(Entity e,FoxPackRecordV2 r)
        =>gates.TryGetValue(e,out var g)&&(g.Completing||g.Migrating)&&Matches(e,r);
    internal static bool CompletionSettled(Entity e,FoxPackRecordV2 r)
    {
        var m=Marker(e);if(!Matches(e,r)||r.RetainedPhase!="Complete"||m?.GetString("phase")!="Complete"||string.IsNullOrEmpty(r.RetainedOutcome))return false;
        if(m.GetBool("outcomeApplied")&&m.GetString("outcome")==r.RetainedOutcome)return true;
        var q=r.SaveMigration;
        return q?.Version==1&&(q.State is "Prepared" or "Done")&&q.Token.Length==32&&q.Trip==r.RetainedTrip&&q.NativeId==e.EntityId&&q.Owner==r.OwnerUid&&q.Code==e.Code.ToString()
            &&q.Outcome==r.RetainedOutcome&&q.NativeEscrow.Length>0&&q.LedgerEscrow.Length>0
            &&NativeMigrationReceiptMatches(e,r)&&m.GetString("migrationToken")==q.Token;
    }
    internal static bool NativeMigrationReceiptMatches(Entity e,FoxPackRecordV2 r)
    {
        var m=Marker(e);double now=e.World.Calendar.TotalHours,capture=m?.GetDouble("capturedHours",double.NaN)??double.NaN,resume=m?.GetDouble("resumeHours",double.NaN)??double.NaN;
        return Matches(e,r)&&r.RetainedPhase=="Complete"&&SafeSnapshot(e)&&m?.GetInt("migrationVersion")==1&&m.GetBool("migrationApplied")
            &&m.GetString("migrationToken").Length==32&&m.GetString("migrationTrip")==r.RetainedTrip&&!string.IsNullOrEmpty(r.RetainedOutcome)&&m.GetString("migrationOutcome")==r.RetainedOutcome
            &&double.IsFinite(capture)&&capture>=0&&double.IsFinite(resume)&&resume>=capture&&double.IsFinite(now)&&resume<=now;
    }
    internal static bool CompletionProofMatches(Entity e,FoxPackRecordV2 r)
    {
        var p=r.CompletionProof;var m=Marker(e);
        return p?.Version==1&&r.RetainedPhase=="Complete"&&Matches(e,r)&&SafeSnapshot(e)&&p.Trip==r.RetainedTrip&&p.Owner==r.OwnerUid&&p.Companion==r.FoxId
            &&p.NativeId==e.EntityId&&p.Code==e.Code.ToString()&&!string.IsNullOrEmpty(p.Outcome)&&p.Outcome==r.RetainedOutcome
            &&p.CapturedHours==m!.GetDouble("capturedHours",double.NaN)&&double.IsFinite(p.CapturedHours)&&p.CapturedHours>=0
            &&double.IsFinite(p.ResumeHours)&&p.ResumeHours>=p.CapturedHours&&p.ResumeHours<=e.World.Calendar.TotalHours
            &&(!m.HasAttribute("resumeHours")||m.GetDouble("resumeHours",double.NaN)==p.ResumeHours)
            &&(string.IsNullOrEmpty(m.GetString("outcome"))||m.GetString("outcome")==p.Outcome);
    }
    internal static void RollForwardMigrationReceipt(Entity e,FoxPackRecordV2 r,Func<bool> verify)
    {
        var g=gates.GetValue(e,_=>new());lock(g.Sync){
            if(!NativeMigrationReceiptMatches(e,r)||!verify())throw new InvalidOperationException("Applied native migration receipt cannot be verified");
            g.Migrating=true;g.Blocked=1;
            try {var m=Marker(e)!;if(m.GetString("phase")!="Complete")m.SetString("migrationInterruptedReason",m.GetString("reason"));
                m.SetString("phase","Complete");m.RemoveAttribute("reason");FeralKinshipCompanionSystem.ClearMigratedExpeditionStatus(e);Dirty(e);e.WatchedAttributes.MarkPathDirty("domesticationstatus");}
            finally{g.Migrating=false;}
        }
    }
    internal static void ApplyMigrationSettlement(Entity e,FoxPackRecordV2 r,RetainedSaveMigration q,Action project,Func<bool> verify)
    {
        var g=gates.GetValue(e,_=>new());lock(g.Sync){
            if(InProtectedTransition(e,r)||!Matches(e,r)||!SafeSnapshot(e)||!verify())throw new InvalidOperationException("Migration authority unavailable");
            g.Migrating=true;g.Blocked=1;
            try {
                var m=Marker(e)!;m.SetDouble("resumeHours",q.ResumeHours);RestoreClocks(e,m);project();
                if(!verify())throw new InvalidOperationException("Authority changed during migration");
                RestoreBoxes(e);m.SetString("migrationPriorPhase",m.GetString("phase"));m.SetString("migrationPriorReason",m.GetString("reason"));
                m.SetInt("migrationVersion",1);m.SetString("migrationToken",q.Token);m.SetString("migrationTrip",q.Trip);m.SetString("migrationOutcome",q.Outcome);
                m.SetString("phase","Complete");m.RemoveAttribute("reason");
                FeralKinshipCompanionSystem.ClearMigratedExpeditionStatus(e);
                m.SetBool("migrationApplied",true); // Last native write: all settlement surfaces now exist.
                Dirty(e);e.WatchedAttributes.MarkPathDirty("domesticationstatus");
            }finally{g.Migrating=false;}
        }
    }
    internal static void AdmitMigrationSettlement(Entity e,FoxPackRecordV2 r)
    {
        var g=gates.GetValue(e,_=>new());lock(g.Sync){
            if(!CompletionSettled(e,r)||!SafeSnapshot(e))throw new InvalidOperationException("No verified completed settlement");
            RestoreBoxes(e);
            if(NativeMigrationReceiptMatches(e,r))FeralKinshipCompanionSystem.ClearMigratedExpeditionStatus(e);
            else FeralKinshipCompanionSystem.GetDomesticationStatus(e)!.SetBool("feralKinshipExpeditionAway",false);
            e.State=EnumEntityState.Active;e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.EndRetainedSuspension();
            g.Admitted=true;g.ProgressHydrated=true;g.TeleportPending=false;g.Blocked=0;PublishCompletedPresentation(e);
        }
    }
}
