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

[ProtoContract] public sealed class CompanionPresentationProbeRequest
{
    [ProtoMember(1)] public string Nonce=string.Empty;
    [ProtoMember(2)] public long NativeId;
}
[ProtoContract] public sealed class CompanionPresentationProbeReply
{
    [ProtoMember(1)] public string Nonce=string.Empty;
    [ProtoMember(2)] public long NativeId;
    [ProtoMember(3)] public string Detail=string.Empty;
}
public sealed partial class FeralKinshipCompanionSystem
{
    private sealed record PendingPresentationProbe(string Nonce,long NativeId,long Expires);
    private readonly Dictionary<string,PendingPresentationProbe> presentationProbes=new(StringComparer.Ordinal);
    private readonly Dictionary<string,long> presentationProbeNext=new(StringComparer.Ordinal);
    private long presentationSweepCount;
    private int presentationLastSweepCandidates;
    private string presentationLastSweepAuthority="not-run";
    private static string? presentationDllHash;
    private static string PresentationDllHash()
    {
        if(presentationDllHash!=null)return presentationDllHash;
        try {return presentationDllHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(FeralKinshipCompanionSystem).Assembly.Location))).ToLowerInvariant();}
        catch(Exception e){return "unavailable:"+e.GetType().Name;}
    }
    private void RegisterPresentationDiagnostics(ICoreServerAPI api)
    {
        RegisterCompletedHoldRecovery(api);
        RegisterSaveMigrationDiagnostics(api);
        api.ChatCommands.Create("companiondiag").WithDescription("Read-only: list [page], native ID, id:ledger-id, #roster-number, name:exact name. Prefix cart for a validated admin cart target.")
            .RequiresPlayer().RequiresPrivilege(Privilege.chat).WithArgs(api.ChatCommands.Parsers.OptionalAll("list-or-selector"))
            .HandleWith(OnCompanionPresentationDiagnostic);
        api.Logger.Notification("[CompanionDiag] Ready: /companiondiag list; /companiondiag native:<native-id>; server DLL SHA256={0}",PresentationDllHash());
    }
    private TextCommandResult OnCompanionPresentationDiagnostic(TextCommandCallingArgs args)
    {
        try { return OnCompanionPresentationDiagnosticCore(args); }
        catch(Exception error) {
            serverApi?.Logger.Warning("[CompanionDiag] Read-only diagnostic formatting stopped: {0}. No repair was attempted.",error.GetType().Name);
            return TextCommandResult.Error("Read-only diagnostic could not format the saved authority; collect server-main. No repair was attempted.");
        }
    }
    private TextCommandResult OnCompanionPresentationDiagnosticCore(TextCommandCallingArgs args)
    {
        if(serverApi==null || packRepository?.Loaded!=true || args.Caller.Player is not IServerPlayer owner)
            return TextCommandResult.Error("Diagnostic unavailable: server ledger is not initialized.");
        if(packRepository.GetAllRecords().Count>8192)return TextCommandResult.Error("Diagnostic refused: ledger exceeds the read-only bound.");
        string selector=args.Parsers.Count>0?((string?)args[0]??string.Empty).Trim():string.Empty;
        string targetOwner=owner.PlayerUID;
        bool cart=selector.Equals("cart",StringComparison.OrdinalIgnoreCase)||selector.StartsWith("cart ",StringComparison.OrdinalIgnoreCase);
        if(cart) {
            if(!CanUseDeveloperTools(owner)||!developerCartTargets.TryGetValue(owner.PlayerUID,out var target)
                ||!DeveloperMayControlOwner(owner,target.Owner))return TextCommandResult.Error("Cart diagnostic refused: select the nearby loaded cart with a held Developer Ledger and retain Creative/admin access.");
            targetOwner=target.Owner;selector=selector.Length>4?selector[5..].Trim():string.Empty;
        }
        if(string.IsNullOrEmpty(selector)||selector.Equals("list",StringComparison.OrdinalIgnoreCase)||selector.StartsWith("list ",StringComparison.OrdinalIgnoreCase)) {
            int page=1;
            if(selector.Length>4&&(!int.TryParse(selector[5..].Trim(),out page)||page<1))return TextCommandResult.Error("Use /companiondiag list [page], for example /companiondiag list 2.");
            return TextCommandResult.Success(ListPresentationRecords(targetOwner,page,cart));
        }
        FoxPackRecordV2[] matches=SelectPresentationRecords(targetOwner,selector);
        if(matches.Length!=1)return TextCommandResult.Error("Diagnostic refused: no unique owned record matches. Run /companiondiag "+(cart?"cart ":"")+"list; select native:<ID from list>, id:<ledger-id>, #<roster-number>, or name:<exact name>. Bare numbers are native IDs, not roster numbers.");
        long now=serverApi.World.ElapsedMilliseconds;
        if(presentationProbeNext.TryGetValue(owner.PlayerUID,out long next)&&now<next)
            return TextCommandResult.Error("Wait ten seconds before another presentation diagnostic.");
        presentationProbeNext[owner.PlayerUID]=now+10000;
        FoxPackRecordV2 r=matches[0];Entity? e=serverApi.World.GetEntityById(r.EntityId);
        string summary=DescribeServerPresentation(r,e);
        serverApi.Logger.Notification("[CompanionDiag] SERVER {0}",DiagnosticText(summary,7000));
        serverApi.Logger.Notification("[CompanionDiag] AUTHORITY {0}",DiagnosticText(DescribeRetainedAuthority(r,e),7000));
        serverApi.Logger.Notification("[CompanionDiag] PROGRESS {0}",DiagnosticText(DescribeRetainedProgress(r,e),3000));
        if(r.EntityId<=0)return TextCommandResult.Success("Server diagnostic logged for "+DiagnosticText(r.Name,60)+"; no saved native ID is available for a client probe. Collect server-main.");
        if(serverChannel==null)return TextCommandResult.Success("Server diagnostic logged; client channel unavailable.");
        var request=new CompanionPresentationProbeRequest{Nonce=Guid.NewGuid().ToString("N"),NativeId=r.EntityId};
        presentationProbes[owner.PlayerUID]=new(request.Nonce,r.EntityId,now+30000);
        serverChannel.SendPacket(request,owner);
        serverApi.World.RegisterCallback(_=>{
            if(presentationProbes.TryGetValue(owner.PlayerUID,out var pending)&&pending.Nonce==request.Nonce){
                presentationProbes.Remove(owner.PlayerUID);serverApi?.Logger.Notification("[CompanionDiag] CLIENT no reply for native={0}; client registration/sync remains UNKNOWN.",r.EntityId);
            }
        },30000);
        return TextCommandResult.Success("Read-only diagnostic logged for "+DiagnosticText(r.Name,60)+" (native "+r.EntityId+"). Wait 30 seconds; collect fresh server-main and client-main logs containing [CompanionDiag] SERVER, AUTHORITY, PROGRESS and CLIENT. No Hold, return state, animal or record was changed.");
    }
    private static string DiagnosticText(string? value,int limit) => new string((value??string.Empty).Where(c=>!char.IsControl(c)).Take(limit).ToArray());
    private FoxPackRecordV2[] SelectPresentationRecords(string ownerUid,string selector)
    {
        var owned=packRepository!.GetAllRecords().Where(r=>r.OwnerUid==ownerUid);
        if(selector.StartsWith("native:",StringComparison.OrdinalIgnoreCase)) {
            return long.TryParse(selector[7..].Trim(),out long native)&&native>0?owned.Where(r=>r.EntityId==native).Take(2).ToArray():Array.Empty<FoxPackRecordV2>();
        }
        if(selector.StartsWith("id:",StringComparison.OrdinalIgnoreCase))return owned.Where(r=>r.FoxId==selector[3..].Trim()).Take(2).ToArray();
        if(selector.StartsWith("#",StringComparison.Ordinal))return int.TryParse(selector[1..],out int number)&&number>0?owned.Where(r=>r.Number==number).Take(2).ToArray():Array.Empty<FoxPackRecordV2>();
        if(selector.StartsWith("name:",StringComparison.OrdinalIgnoreCase))return owned.Where(r=>string.Equals(r.Name?.Trim(),selector[5..].Trim(),StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if(long.TryParse(selector,out long id))return owned.Where(r=>id>0&&r.EntityId==id).Take(2).ToArray();
        return owned.Where(r=>r.FoxId==selector||string.Equals(r.Name?.Trim(),selector,StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
    }
    private string ListPresentationRecords(string ownerUid,int page,bool cart)
    {
        var records=packRepository!.GetAllRecords().Where(r=>r.OwnerUid==ownerUid).OrderBy(r=>r.Number).ThenBy(r=>r.FoxId,StringComparer.Ordinal).ToArray();
        int pages=Math.Max(1,(records.Length+7)/8);page=Math.Min(Math.Max(1,page),pages);
        string prefix="/companiondiag "+(cart?"cart ":string.Empty);
        var lines=new List<string>{"Read-only records for selected player "+ownerUid+"; page "+page+"/"+pages+". Bare numbers mean native IDs. # means roster number."};
        foreach(var r in records.Skip((page-1)*8).Take(8))lines.Add("#"+r.Number+" "+DiagnosticText(r.Name,40)+" | native:"+r.EntityId+" | id:"+DiagnosticText(r.FoxId,100)+" | "+DiagnosticText(r.Status,40)+" | ledger="+DiagnosticText(r.RetainedPhase,30)+(r.Archived?" | archived":""));
        if(records.Length==0)lines.Add("No owned records in this target.");
        lines.Add("Probe a listed entry: "+prefix+"native:<ID> or "+prefix+"name:<exact name>. Wait ten seconds between probes.");
        if(page<pages)lines.Add("Next page: "+prefix+"list "+(page+1));
        return string.Join("\n",lines);
    }
    private string DescribeRetainedAuthority(FoxPackRecordV2 r,Entity? e)
    {
        var m=e==null?null:RetainedNativeSuspension.Marker(e);var s=e==null?null:GetDomesticationStatus(e);
        var assigned=packRepository?.GetActiveExpeditions().Where(x=>x.SelectedFoxIds.Contains(r.FoxId,StringComparer.Ordinal)).Take(20).ToArray();
        bool identity=e!=null&&RetainedNativeSuspension.Matches(e,r);
        bool snapshot=e!=null&&RetainedNativeSuspension.SafeSnapshot(e);
        return $"native={r.EntityId} name={DiagnosticText(r.Name,60)} companion={r.FoxId} ledger-phase={r.RetainedPhase} marker-phase={m?.GetString("phase")??"none"} hold-reason={DiagnosticText(m?.GetString("reason"),300)} migration-prior-phase={m?.GetString("migrationPriorPhase")} migration-prior-reason={DiagnosticText(m?.GetString("migrationPriorReason"),300)} migration-interrupted-reason={DiagnosticText(m?.GetString("migrationInterruptedReason"),300)} "
            +$"ledger-trip={r.RetainedTrip} marker-trip={m?.GetString("trip")} identity-match={identity} phase-match={m?.GetString("phase")==r.RetainedPhase} snapshot-valid={snapshot} "
            +$"snapshot-captured={m?.GetBool("captured")} snapshot-state={m?.GetInt("state")} clock-tree-present={m?.GetTreeAttribute("clocks")!=null} boxes={DiagnosticBoxSummary(e)} "
            +$"ledger-outcome={r.RetainedOutcome} marker-outcome={m?.GetString("outcome")} outcome-applied={m?.GetBool("outcomeApplied")} captured-hours={m?.GetDouble("capturedHours",double.NaN)} resume-hours={m?.GetDouble("resumeHours",double.NaN)} now-hours={serverApi?.World.Calendar.TotalHours} completion-proof={r.CompletionProof?.Version}/{r.CompletionProof?.Trip} completion-proof-match={e!=null&&RetainedNativeSuspension.CompletionProofMatches(e,r)} "
            +$"native-id-match={e!=null&&r.EntityId==e.EntityId&&m?.GetLong("nativeId")==e.EntityId} owner-match={m?.GetString("owner")==r.OwnerUid&&s?.GetString("owner")==r.OwnerUid} companion-match={m?.GetString("companionId")==r.FoxId&&s?.GetString(FoxIdKey)==r.FoxId} code-match={m?.GetString("code")==e?.Code?.ToString()} "
            +$"legacy-authority-match={m?.GetString("legacyAuthority",string.Empty)==r.RetainedLegacyAuthority} legacy-ledger-hours={r.RetainedLegacyAdmissionHours} legacy-marker-hours={m?.GetDouble("legacyAdmissionHours",double.NaN)} "
            +$"party-ids={string.Join(",",assigned?.Select(x=>x.ExpeditionId)??Array.Empty<long>())} pending-departure={r.PendingDepartureStatus}/{r.PendingDepartureAtUtcMs}/{r.PendingDepartureDeadlineUtcMs} pending-return={r.PendingReturnStatus}/{r.PendingReturnAtUtcMs} "
            +$"rebuild-blocker={FullRecoveryBlocker(r,e)} manual-recovery-audit={DiagnosticText(r.CompletedHoldRecoveryAudit,500)} manual-native-escrow-bytes={r.CompletedHoldRecoveryNativeEscrow.Length} manual-ledger-escrow-bytes={r.CompletedHoldRecoveryLedgerEscrow.Length} migration={r.SaveMigration?.Version}/{r.SaveMigration?.State} migration-reason={DiagnosticText(r.SaveMigration?.Reason,300)} migration-escrow={r.SaveMigration?.NativeEscrow.Length} completion-settled={e!=null&&RetainedNativeSuspension.CompletionSettled(e,r)}";
    }
    private static string DiagnosticBoxSummary(Entity? e)
    {
        var marker=e==null?null:RetainedNativeSuspension.Marker(e);
        return string.Join("/",Enumerable.Range(0,4).Select(i=>{
            var b=(marker?["box"+i] as Vintagestory.API.Datastructures.FloatArrayAttribute)?.value;
            return b?.Length==6?($"{i}:"+string.Join(",",b)):i+":missing-or-malformed";
        }));
    }
    private string DescribeRetainedProgress(FoxPackRecordV2 r,Entity? e)
    {
        var s=e==null?null:GetDomesticationStatus(e);var health=e?.GetBehavior<Vintagestory.GameContent.EntityBehaviorHealth>();
        return $"native={r.EntityId} ledger-level-xp={r.Level}/{r.CurrentLevelExperience}/{r.LifetimeExperience} native-level-xp={s?.GetInt(CompanionLevelKey)}/{s?.GetLong(CompanionCurrentExperienceKey)}/{s?.GetLong(CompanionLifetimeExperienceKey)} "
            +$"ledger-points={r.Points}/{r.LifetimePoints}/{r.BankedTalentPoints} native-points={s?.GetInt(FoxPointsKey)}/{s?.GetInt(FoxLifetimePointsKey)}/{s?.GetInt(BankedTalentPointsKey)} "
            +$"ledger-health={r.CurrentHealth}/{r.MaxHealth}/state:{r.HealthState} native-health={health?.Health}/{health?.MaxHealth}/state:{e?.WatchedAttributes.GetInt(EntityHealthStateKey)} native-mortal-start={e?.WatchedAttributes.GetDouble("mortallyWoundedTotalHours")}";
    }
    private string DescribeServerPresentation(FoxPackRecordV2 r,Entity? e)
    {
        var authority=CaptureExpeditionPresentationAuthority();
        string blockers=e==null?"world-index-missing":PresentationRepairBlocker(e,authority);
        string chunk="unknown";
        if(e!=null && serverApi!=null){var c=serverApi.WorldManager.GetChunk(e.InChunkIndex3d);chunk=c==null?"missing":c.Disposed?"disposed":$"native-matches={c.Entities.Take(c.EntitiesCount).Count(x=>ReferenceEquals(x,e))}";}
        int active=packRepository?.GetActiveExpeditions().Count(x=>x.SelectedFoxIds.Contains(r.FoxId,StringComparer.Ordinal))??-1;
        return $"native={r.EntityId} companion={r.FoxId} world-index={e!=null} observed={retainedAdmissionBodies.ContainsKey(r.EntityId)} registered={loadedFoxes.ContainsKey(r.EntityId)} chunk={chunk} "
            +$"ready={companionContentReady}/{packRepository?.Loaded} main-thread={Environment.CurrentManagedThreadId==RuntimeEnv.ServerMainThreadId} "
            +$"full-rebuild-pending={pendingFullRebuilds.Contains(r.FoxId)} native-escrow-bytes={r.DeveloperRecoveryNativeEscrow?.Length??0} ledger-phase={r.RetainedPhase} status={r.Status} active={active} pending-departure={r.PendingDepartureStatus}/{r.PendingDepartureAtUtcMs} pending-return={r.PendingReturnStatus}/{r.PendingReturnAtUtcMs} archived={r.Archived} "
            +$"eligibility={blockers} sweeps={presentationSweepCount} candidates={presentationLastSweepCandidates} last-authority={presentationLastSweepAuthority} last-check={(presentationLastAttempts.TryGetValue(r.EntityId,out string? check)?check:"never-candidate-this-session")} "
            +(e==null?"entity=absent":RetainedNativeSuspension.DescribePresentation(e))+$" dll={PresentationDllHash()}";
    }
    private void OnPresentationProbeClient(CompanionPresentationProbeRequest request)
    {
        if(clientApi==null || request.Nonce.Length!=32 || request.NativeId<=0)return;
        clientApi.Event.EnqueueMainThreadTask(()=>{
            if(clientApi==null)return;
            Entity? e=clientApi.World.GetEntityById(request.NativeId);
            string detail=$"native={request.NativeId} level-finalized={clientLevelFinalized} world-index={e!=null} ";
            if(e!=null)detail+=RetainedNativeSuspension.DescribePresentation(e)+$" renderer={e.Properties?.Client?.Renderer?.GetType().FullName??"none"} renderer-code={e.Properties?.Client?.RendererName??"none"} "+(e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.DescribePresentationUpdates()??"social-behavior=missing");
            detail+=$" dll={PresentationDllHash()}";
            clientApi.Logger.Notification("[CompanionDiag] CLIENT {0}",detail);
            clientChannel?.SendPacket(new CompanionPresentationProbeReply{Nonce=request.Nonce,NativeId=request.NativeId,Detail=detail});
        },"companion-presentation-read-only-probe");
    }
    private void OnPresentationProbeServer(IServerPlayer owner,CompanionPresentationProbeReply reply)
    {
        if(serverApi==null || !presentationProbes.TryGetValue(owner.PlayerUID,out var pending)
            || pending.Nonce!=reply.Nonce || pending.NativeId!=reply.NativeId || serverApi.World.ElapsedMilliseconds>pending.Expires)return;
        presentationProbes.Remove(owner.PlayerUID);
        string detail=(reply.Detail??string.Empty).Replace('\r',' ').Replace('\n',' ');
        if(detail.Length>2400)detail=detail[..2400];
        serverApi.Logger.Notification("[CompanionDiag] CLIENT-REPLY {0}",detail);
    }
}
