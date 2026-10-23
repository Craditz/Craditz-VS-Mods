#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;
public sealed partial class FeralKinshipCompanionSystem
{
    private sealed record RecoveryBodyCandidate(Entity Body,string Source);
    private sealed record RecoveryOriginalLookup(string State,string Reason,RecoveryBodyCandidate[] Candidates);
    private static string IdentityValue(string? value)=>JsonConvert.SerializeObject(value??string.Empty);
    private static bool ExactRecoveryCodeEquals(string? saved,AssetLocation? actual)
        =>CompanionSpeciesPresentation.ExactCode(saved) is { } code && actual!=null && code.Equals(actual);
    private static string IdentityCheck(string? saved,string? actual,bool available=true)
        =>!available || string.IsNullOrEmpty(saved)?"unavailable":saved==actual?"match":"mismatch";
    private static string IdentityCodeCheck(FoxPackRecordV2 r,Entity? e)
        =>e==null||string.IsNullOrEmpty(r.EntityCode)?"unavailable":ExactRecoveryCodeEquals(r.EntityCode,e.Code)?"match":"mismatch";

    private RecoveryOriginalLookup LookupRecoveryOriginal(FoxPackRecordV2 r) => LookupRecoveryIdentity(r, false);
    private RecoveryOriginalLookup LookupRecoveryIdentity(FoxPackRecordV2 r, bool includeRetired)
    {
        RecoveryOriginalLookup Stop(string state,string reason,RecoveryBodyCandidate[]? candidates=null)=>new(state,reason,candidates??Array.Empty<RecoveryBodyCandidate>());
        if(serverApi==null)return Stop("Unavailable","Server world is not ready.");
        if(serverApi.World.LoadedEntities.Count>131072||retainedAdmissionBodies.Count>8192||loadedFoxes.Count>8192)
            return Stop("Unavailable","Native identity lookup exceeds its safety bound.");
        var chunks=serverApi.WorldManager.AllLoadedChunks;
        if(chunks.Count>32768)return Stop("Unavailable","Loaded chunk lookup exceeds its safety bound.");
        var bodies=new Dictionary<Entity,List<string>>(ReferenceEqualityComparer.Instance);
        void Observe(Entity? e,string kind,long? key=null,int slot=-1) {
            if(e==null||!includeRetired&&IsRetiredRecoveryOriginal(e))return;
            if(!(r.EntityId>0&&(e.EntityId==r.EntityId||kind!="chunk"&&key==r.EntityId))&&!(r.FoxId.Length>0&&(GetDomesticationStatus(e)?.GetString(FoxIdKey)==r.FoxId||RetainedNativeSuspension.Marker(e)?.GetString("companionId")==r.FoxId)))return;
            if(!bodies.TryGetValue(e,out var sources)) { if(bodies.Count>=4)return;bodies[e]=sources=new(); }
            if(sources.Count<16)sources.Add(!key.HasValue?kind:kind=="chunk"?$"chunk:index={key.Value},slot={slot}":$"{kind}:key={key.Value}");
        }
        Observe(r.EntityId>0?serverApi.World.GetEntityById(r.EntityId):null,"world-index:GetEntityById");
        foreach(var pair in serverApi.World.LoadedEntities)Observe(pair.Value,"world-loaded",pair.Key);
        foreach(var pair in retainedAdmissionBodies)Observe(pair.Value,"observed",pair.Key);
        foreach(var pair in loadedFoxes)Observe(pair.Value,"registered",pair.Key);
        int slots=0;
        foreach(var pair in chunks) {
            var c=pair.Value;
            if(includeRetired&&c.Disposed)return Stop("Unavailable","Loaded chunk evidence is disposed, inconsistent or exceeds the slot budget.");
            if(c.Disposed)continue;
            var entities=c.Entities;
            int count=c.EntitiesCount;
            // Fresh native chunks allocate entity storage lazily, including empty loaded chunks.
            if(entities==null) {
                if(count==0)continue;
                return Stop("Unavailable","Loaded chunk entity storage is unavailable for a nonempty or inconsistent chunk.");
            }
            if(includeRetired&&(count<0||count>entities.Length||(slots+=count)>65536))return Stop("Unavailable","Loaded chunk evidence is disposed, inconsistent or exceeds the slot budget.");
            for(int i=0;i<Math.Min(count,entities.Length);i++)Observe(entities[i],"chunk",pair.Key,i);
        }
        var candidates=bodies.Select(p=>new RecoveryBodyCandidate(p.Key,string.Join(";",p.Value))).ToArray();
        if(candidates.Length==0)return Stop("Absent","Original is not currently registered in the native world.");
        if(candidates.Length>1)return Stop("Duplicate","Multiple native originals/identities are present; all cargo is preserved and no winner is chosen.",candidates);
        var e=candidates[0].Body;
        string owner=IdentityCheck(r.OwnerUid,GetCompanionOwnerUid(e)),id=IdentityCheck(r.FoxId,GetDomesticationStatus(e)?.GetString(FoxIdKey));
        string native=r.EntityId>0?(r.EntityId==e.EntityId?"match":"mismatch"):"unavailable",code=IdentityCodeCheck(r,e);
        if(owner!="match"||id!="match"||native=="mismatch"||code=="mismatch")
            return Stop("Conflict",$"Native identity conflict: owner={owner}, companion={id}, native={native}, code={code}; lookup={candidates[0].Source}. Run /companiondiag for saved/actual values; admin /companionlink id:{r.FoxId} inspects guarded relinking.",candidates);
        if(!IsTamedFox(e))return Stop("Unsupported",$"Native identity checks pass, but the body is not a recognized domesticated companion; lookup={candidates[0].Source}. No replacement or cargo mutation is authorized.",candidates);
        return Stop("Found",string.Empty,candidates);
    }

    private EntityProperties? InspectSavedRecoveryCode(FoxPackRecordV2 r,out string status)
    {
        status=string.IsNullOrWhiteSpace(r.EntityCode)?"unavailable":"invalid";
        var code=CompanionSpeciesPresentation.ExactCode(r.EntityCode);
        if(code==null)return null;
        if(serverApi==null){status="unavailable:server-not-ready";return null;}
        var type=serverApi.World.GetEntityType(code);
        if(type?.Code==null||!code.Equals(type.Code)){status="missing";return null;}
        status="registered";return type;
    }
    private static string DescribeRecoveryIdentity(FoxPackRecordV2 r,Entity? e,string source)
    {
        string owner=e==null?"unavailable":IdentityValue(GetCompanionOwnerUid(e));
        string id=e==null?"unavailable":IdentityValue(GetDomesticationStatus(e)?.GetString(FoxIdKey));
        return $"lookup-source={source} saved-owner={IdentityValue(r.OwnerUid)} actual-owner={owner} owner-check={IdentityCheck(r.OwnerUid,e==null?null:GetCompanionOwnerUid(e),e!=null)} "
            +$"saved-companion={IdentityValue(r.FoxId)} actual-companion={id} companion-check={IdentityCheck(r.FoxId,e==null?null:GetDomesticationStatus(e)?.GetString(FoxIdKey),e!=null)} "
            +$"saved-native={r.EntityId} actual-native={(e==null?"unavailable":e.EntityId.ToString())} native-check={(e==null||r.EntityId<=0?"unavailable":r.EntityId==e.EntityId?"match":"mismatch")} "
            +$"saved-code={IdentityValue(r.EntityCode)} actual-code={(e==null?"unavailable":IdentityValue(e.Code?.ToString()))} code-check={IdentityCodeCheck(r,e)} "
            +$"recognized-domesticated={(e==null?"unavailable":IsTamedFox(e).ToString())}";
    }
    private string DescribeRecoveryLookup(FoxPackRecordV2 r,IServerPlayer actor)
    {
        var lookup=LookupRecoveryOriginal(r);
        string blocker=lookup.State is "Found" or "Absent"?EmergencyAdminBlocker(actor,r,false):lookup.Reason;
        if(blocker.Length==0&&lookup.State=="Found") {
            var e=lookup.Candidates[0].Body;
            blocker=!NativeRecoveryPublished(e)?"Native publication is not canonical: "+RecoveryPublicationReason(e):e.Pos.Dimension!=0?"Original native dimension is not supported":EmergencyBodyBlocker(e);
        }
        string normal=lookup.State is "Found" or "Absent"?FullRecoveryBlocker(r,lookup.State=="Found"?lookup.Candidates[0].Body:null):lookup.Reason;
        return $"saved-native={r.EntityId} companion={r.FoxId} lookup={lookup.State} candidates={lookup.Candidates.Length} lookup-reason={IdentityValue(lookup.Reason)} normal-rebuild-blocker={IdentityValue(normal)} emergency-blocker={IdentityValue(blocker.Length==0?"none:confirmation-and-placement-preflight-still-required":blocker)}";
    }
    private void LogRecoveryIdentityDiagnostic(FoxPackRecordV2 r,IServerPlayer actor)
    {
        var type=InspectSavedRecoveryCode(r,out string registered);
        string source=CompanionSpeciesPresentation.SourceCode(type,r.EntityCode);
        serverApi!.Logger.Notification("[CompanionDiag] SAVED-CODE saved-code={0} registration={1} registered-code={2} species={3} species-source-code={4}; mapping=not-applied",IdentityValue(r.EntityCode),registered,type?.Code?.ToString()??"unavailable",CompanionSpeciesPresentation.Display(r.SpeciesId,r.EntityCode,source),source.Length==0?"unavailable":source);
        var lookup=LookupRecoveryOriginal(r);
        serverApi.Logger.Notification("[CompanionDiag] RELINK {0}",DescribeNativeLink(r));
        serverApi.Logger.Notification("[CompanionDiag] RECOVERY {0}",DescribeRecoveryLookup(r,actor));
        if(lookup.Candidates.Length==0)serverApi.Logger.Notification("[CompanionDiag] IDENTITY {0}",DescribeRecoveryIdentity(r,null,"none:world-index/world-loaded/observed/registered/loaded-chunks"));
        foreach(var candidate in lookup.Candidates)serverApi.Logger.Notification("[CompanionDiag] IDENTITY {0}",DescribeRecoveryIdentity(r,candidate.Body,candidate.Source));
    }
}
