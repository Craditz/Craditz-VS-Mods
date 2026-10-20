#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private long nextExpeditionPresentationCheckMs;
    private long lastExpeditionPresentationCheckMs;
    private readonly Dictionary<long,string> presentationLastAttempts=new();
    private sealed class ExpeditionPresentationAuthority
    {
        internal readonly Dictionary<string,FoxPackRecordV2> Records=new(StringComparer.Ordinal);
        internal readonly HashSet<string> Conflicts=new(StringComparer.Ordinal);
        internal readonly HashSet<long> NativeConflicts=new();
        internal readonly HashSet<string> Assigned=new(StringComparer.Ordinal);
        internal readonly HashSet<Entity> Originals=new(ReferenceEqualityComparer.Instance);
        internal readonly Dictionary<string,Entity> NativeBodies=new(StringComparer.Ordinal);
    }

    private string presentationAuthorityReason="not-captured";
    private ExpeditionPresentationAuthority? UnknownPresentationAuthority(string reason){presentationAuthorityReason=reason;return null;}
    // One bounded authority/index pass per sweep, not a ledger/world scan for
    // each animal. A malformed/oversized authority surface means UNKNOWN.
    private ExpeditionPresentationAuthority? CaptureExpeditionPresentationAuthority()
    {
        if(!RetainedNativeSuspension.Enabled || !companionContentReady || serverApi==null || packRepository?.Loaded!=true
            || RuntimeEnv.ServerMainThreadId<=0 || Environment.CurrentManagedThreadId!=RuntimeEnv.ServerMainThreadId)return UnknownPresentationAuthority("not-ready-or-not-server-main-thread");
        var records=packRepository.GetAllRecords();
        var trips=packRepository.GetActiveExpeditions().Take(1025).ToArray();
        var world=serverApi.World.LoadedEntities;
        if(records.Count>8192 || trips.Length>1024 || world==null || world.Count>131072
            || retainedAdmissionBodies.Count>8192 || loadedFoxes.Count>8192)return UnknownPresentationAuthority("authority-size-or-world-index-unavailable");
        var result=new ExpeditionPresentationAuthority();
        var nativeRecords=new HashSet<long>();
        foreach(FoxPackRecordV2 r in records) {
            if(r==null || string.IsNullOrWhiteSpace(r.FoxId))return UnknownPresentationAuthority("malformed-ledger-record");
            if(!result.Records.TryAdd(r.FoxId,r))result.Conflicts.Add(r.FoxId);
            if(r.EntityId>0 && !nativeRecords.Add(r.EntityId))result.NativeConflicts.Add(r.EntityId);
        }
        int budget=8192;
        foreach(var trip in trips) {
            if(trip.SelectedFoxIds==null || (budget-=trip.SelectedFoxIds.Count)<0)return UnknownPresentationAuthority("malformed-or-oversized-party");
            foreach(string id in trip.SelectedFoxIds) {
                if(string.IsNullOrWhiteSpace(id))return UnknownPresentationAuthority("empty-expedition-member");
                result.Assigned.Add(id); // A wrong-owner assignment is also not Present.
            }
        }
        var ids=new Dictionary<string,Entity>(StringComparer.Ordinal);
        var natives=new Dictionary<long,Entity>();
        foreach(Entity e in world.Values.Concat(retainedAdmissionBodies.Values).Concat(loadedFoxes.Values)) {
            if(e==null)return UnknownPresentationAuthority("null-native-body");
            if(IsRetiredRecoveryOriginal(e))continue; // Durable quarantine is not a competing active authority.
            string id=GetDomesticationStatus(e)?.GetString(FoxIdKey,string.Empty) ?? string.Empty;
            if(natives.TryGetValue(e.EntityId,out Entity? other) && !ReferenceEquals(other,e))result.NativeConflicts.Add(e.EntityId);
            else natives[e.EntityId]=e;
            if(RetainedNativeSuspension.Marker(e)?.GetString("phase")=="Complete")result.Originals.Add(e);
            if(result.Originals.Count>8192)return UnknownPresentationAuthority("too-many-retained-originals");
            if(string.IsNullOrEmpty(id))continue;
            if(ids.TryGetValue(id,out other) && !ReferenceEquals(other,e))result.Conflicts.Add(id);
            else {ids[id]=e;result.NativeBodies[id]=e;}
        }
        presentationAuthorityReason="ready";return result;
    }

    private string PresentationRepairBlocker(Entity e,ExpeditionPresentationAuthority? authority)
    {
        if(authority==null || serverApi==null || packRepository?.Loaded!=true)return "unknown-authority:"+presentationAuthorityReason;
        if(!ReferenceEquals(e.Api,serverApi) || !ReferenceEquals(e.World,serverApi.World))return "wrong-api-or-world";
        if(e is not EntityAgent agent || agent.MountedOn!=null || e.Pos.Dimension!=0 || !IsTamedFox(e))return "not-supported-unmounted-owned-companion";
        if(!e.Alive || e.ShouldDespawn || e.Teleporting || e.State==EnumEntityState.Despawned)return "body-not-living-ready";
        string id=GetDomesticationStatus(e)?.GetString(FoxIdKey,string.Empty) ?? string.Empty;
        if(string.IsNullOrEmpty(id))return "missing-companion-id";
        if(authority.Conflicts.Contains(id) || authority.NativeConflicts.Contains(e.EntityId))return "duplicate-identity";
        if(authority.Assigned.Contains(id))return "active-expedition-assignment";
        if(!authority.Records.TryGetValue(id,out FoxPackRecordV2? r) || !packRepository.TryGetRecord(id,out FoxPackRecordV2? current) || !ReferenceEquals(r,current))return "missing-or-ambiguous-record";
        if(r.EntityId!=e.EntityId || r.OwnerUid!=GetCompanionOwnerUid(e) || string.IsNullOrWhiteSpace(r.OwnerUid))return "owner-or-native-id-mismatch";
        if(r.Archived || r.MissingContentArchive?.Active==true || packRepository.IsPermanentlyDeleted(id))return "archived-deleted-or-missing-content";
        if(r.Status==null || r.Status.StartsWith("Away \u2014",StringComparison.OrdinalIgnoreCase) || r.Status.StartsWith("Leaving \u2014",StringComparison.OrdinalIgnoreCase) || r.Status is "Dead" or "MIA" or "Running late" or "Returning" or "Awaiting safe return")return "non-present-ledger-status";
        if(!string.IsNullOrEmpty(r.PendingDepartureStatus) || r.PendingDepartureAtUtcMs>0 || r.PendingDepartureDeadlineUtcMs>0)return "pending-departure";
        if(!string.IsNullOrEmpty(r.PendingReturnStatus) || r.PendingReturnAtUtcMs>0)return "pending-return";
        if(r.RetainedPhase!="Complete" || RetainedNativeSuspension.Marker(e)?.GetString("phase")!="Complete")return "not-completed-or-Hold";
        if(!RetainedNativeSuspension.SafeSnapshot(e) || !RetainedNativeSuspension.CompletionSettled(e,r))return "invalid-completion-identity-snapshot-or-outcome";
        var chunk=serverApi.WorldManager.GetChunk(e.InChunkIndex3d);
        if(!ReferenceEquals(serverApi.World.GetEntityById(e.EntityId),e) || chunk==null || chunk.Disposed)return "native-publication-unavailable";
        var matches=chunk.Entities.Take(chunk.EntitiesCount).Where(x=>x.EntityId==e.EntityId).ToArray();
        if(matches.Length!=1 || !ReferenceEquals(matches[0],e))return "native-chunk-membership-unavailable-or-duplicate";
        return "eligible";
    }
    private bool ReconcileCompletedExpeditionPresentation(Entity e,ExpeditionPresentationAuthority? authority)
    {
        string blocker=PresentationRepairBlocker(e,authority);
        if(presentationLastAttempts.Count<8192 || presentationLastAttempts.ContainsKey(e.EntityId))presentationLastAttempts[e.EntityId]=$"{e.World.ElapsedMilliseconds}:{blocker}";
        if(blocker!="eligible")return false;
        string id=GetDomesticationStatus(e)!.GetString(FoxIdKey);
        if(!RetainedCanonicalAdmission(e))return false;
        bool result=RetainedNativeSuspension.ReconcileCompletedPresentation(e,authority!.Records[id]);
        presentationLastAttempts[e.EntityId]=$"{e.World.ElapsedMilliseconds}:"+(result?"completed-state-published":"adapter-refused-completion");
        return result;
    }

    private void ReconcileCompletedExpeditionPresentationOnLoad(Entity e)
    {
        if(QuarantineRetiredRecoveryOriginal(e))return;
        if(RetainedNativeSuspension.Marker(e)?.GetString("phase")!="Complete")return;
        ReconcileCompletedExpeditionPresentation(e,CaptureExpeditionPresentationAuthority());
    }

    private void MaintainExpeditionPresentation()
    {
        if(serverApi==null)return;
        long now=serverApi.World.ElapsedMilliseconds;
        if(now>=lastExpeditionPresentationCheckMs && now<nextExpeditionPresentationCheckMs)return;
        lastExpeditionPresentationCheckMs=now;
        nextExpeditionPresentationCheckMs=now>long.MaxValue-60000?long.MaxValue:now+60000;
        presentationSweepCount++;
        var authority=CaptureExpeditionPresentationAuthority();
        presentationLastSweepAuthority=presentationAuthorityReason;
        if(authority==null)return;
        // Include originals that were observed before native publication. The
        // entity's blocked tick is never responsible for its own retry.
        var bodies=retainedAdmissionBodies.Values.Concat(loadedFoxes.Values).Concat(authority.Originals)
            .Distinct(ReferenceEqualityComparer.Instance).Cast<Entity>().ToArray();
        presentationLastAttempts.Clear();
        presentationLastSweepCandidates=bodies.Length;
        foreach(Entity e in bodies) {
            try {
                if(!ReconcileCompletedExpeditionPresentation(e,authority))continue;
                if(!loadedFoxes.TryGetValue(e.EntityId,out Entity? registered) || !ReferenceEquals(registered,e))RegisterLoadedFox(e);
            } catch(Exception ex) {RetainedNativeSuspension.Hold(e,"Completed presentation reconciliation failed: "+ex.GetType().Name);}
        }
    }
}
