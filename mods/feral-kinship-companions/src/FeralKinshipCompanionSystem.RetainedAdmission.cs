#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
namespace FeralKinshipCompanions;

public partial class FeralKinshipCompanionSystem
{
    // Observation is NOT successful registration. It must not consume first-load
    // health/request handling or replace a competing native object.
    private readonly Dictionary<long,Entity> retainedAdmissionBodies=new();
    private readonly HashSet<Entity> retainedLoadedHealthApplied=new(ReferenceEqualityComparer.Instance);
    private bool ObserveRetainedAdmission(Entity e)
    {
        if (EmergencyBodyAdmission(e, out bool blocked)) return !blocked;
        if(QuarantineRetiredRecoveryOriginal(e))return false;
        string id=GetDomesticationStatus(e)?.GetString(FoxIdKey,string.Empty) ?? string.Empty;
        Entity[] competing=retainedAdmissionBodies.Values.Concat(loadedFoxes.Values).Where(other=>!ReferenceEquals(e,other)
            && (other.EntityId==e.EntityId || !string.IsNullOrEmpty(id)&&GetDomesticationStatus(other)?.GetString(FoxIdKey)==id)).Distinct().ToArray();
        if(competing.Length>0) {
            if(nativeAdultGrowthHandoffs.TryGetValue(e,out object? source) && source is Entity child
                && !RetainedNativeSuspension.NativeAuthority(e)
                && !string.IsNullOrEmpty(id)
                && competing.All(other=>ReferenceEquals(other,child)
                    && other.EntityId!=e.EntityId
                    && GetDomesticationStatus(other)?.GetString(FoxIdKey)==id
                    && GetCompanionOwnerUid(other)==GetCompanionOwnerUid(e)
                    && !string.IsNullOrWhiteSpace(GetCompanionOwnerUid(e))
                    && GetDomesticationStatus(other)?.GetBool(GrowthInProgressKey)==true
                    && string.Equals(GetDomesticationStatus(other)?.GetString(ChildAdultEntityCodeKey),e.Code?.ToString(),StringComparison.OrdinalIgnoreCase)
                    && !RetainedNativeSuspension.NativeAuthority(other)
                    && GetDomesticationStatus(other)?.GetBool(ExpeditionAwayKey)!=true
                    && GetDomesticationStatus(e)?.GetBool(ExpeditionAwayKey)!=true)) {
                RetainedNativeSuspension.DeferAdmission(e);return false; // Wait for native grow-source unload, never replace it here.
            }
            foreach(Entity other in competing.Where(other => !IsEmergencySelected(other)))RetainedNativeSuspension.Hold(other,"Competing native object/companion identity; retain both inventories");
            RetainedNativeSuspension.Hold(e,"Competing native object/companion identity; retain both inventories");return false;
        }
        retainedAdmissionBodies[e.EntityId]=e;WakeSaveMigration(e);WakeNativeLink(e);return true;
    }
    private bool RetainedCanonicalAdmission(Entity e)
    {
        if (EmergencyBodyAdmission(e, out bool blocked)) return !blocked;
        if(GuardEmergencyEntity(e))return false;
        if(QuarantineRetiredRecoveryOriginal(e))return false;
        if(serverApi==null){RetainedNativeSuspension.DeferAdmission(e);return false;}
        Entity? canonical=serverApi.World.GetEntityById(e.EntityId);
        if(canonical!=null&&!ReferenceEquals(canonical,e)) {
            if (!IsEmergencySelected(canonical)) RetainedNativeSuspension.Hold(canonical,"World index identifies a competing native object");
            RetainedNativeSuspension.Hold(e,"World index identifies a competing native object");return false;
        }
        var chunk=serverApi.WorldManager?.GetChunk(e.InChunkIndex3d);
        if(canonical==null || chunk==null || chunk.Disposed) { RetainedNativeSuspension.DeferAdmission(e);return false; }
        Entity[] matches=chunk.Entities.Take(chunk.EntitiesCount).Where(body=>body.EntityId==e.EntityId).ToArray();
        if(matches.Any(body=>!ReferenceEquals(body,e)) || matches.Length>1) {
            foreach(Entity body in matches)RetainedNativeSuspension.Hold(body,"Chunk contains competing native identity");
            RetainedNativeSuspension.Hold(e,"Chunk contains competing native identity");return false;
        }
        if(matches.Length!=1) { RetainedNativeSuspension.DeferAdmission(e);return false; }
        return true;
    }
    private void ForgetRetainedAdmissionBody(Entity e)
    {
        if(retainedAdmissionBodies.TryGetValue(e.EntityId,out Entity? observed)&&ReferenceEquals(e,observed))retainedAdmissionBodies.Remove(e.EntityId);
        retainedLoadedHealthApplied.Remove(e);
    }
    private bool ApplyRetainedLoadedHealth(Entity e)
    {
        if(!retainedLoadedHealthApplied.Add(e))return true;
        try { RetainedNativeSuspension.SynchronizeProgress(e,()=> {
            EntityBehaviorHealth? health=e.GetBehavior<EntityBehaviorHealth>();
            float original=health?.Health ?? 0;
            if(e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.TryPeekInitializedHealth(out float saved)==true)original=saved;
            bool away=GetDomesticationStatus(e)?.GetBool(ExpeditionAwayKey)==true
                || RetainedNativeSuspension.Marker(e)?.GetString("phase") is "Suspended" or "Returning";
            ApplyFoxDerivedStats(e,preserveCurrentHealth:!nativeAdultGrowthHandoffs.TryGetValue(e,out _));
            if(away&&health!=null&&float.IsFinite(original)&&health.Health>Math.Max(0,original))health.Health=Math.Max(0,original);
        }); }
        catch(Exception ex) {RetainedNativeSuspension.Hold(e,"First-load health reconciliation failed: "+ex.GetType().Name);return false;}
        ITreeAttribute? status=GetDomesticationStatus(e);
        if(status!=null&&FoxRequestCatalog.GetDurationSeconds(status.GetString(ActiveRequestKey,string.Empty))>0) {
            status.SetLong(RequestQualifiedSinceUtcMsKey,0);status.SetFloat(RequestProgressKey,0);
        }
        return true;
    }
    private bool TryAdmitLegacyAway(Entity e,FoxPackRecordV2? r)
    {
        ITreeAttribute? status=GetDomesticationStatus(e);
        if(r==null || r.EntityId!=e.EntityId || r.FoxId!=status?.GetString(FoxIdKey) || r.OwnerUid!=status?.GetString("owner")
            || !e.Alive || status?.GetBool(ExpeditionAwayKey)!=true || !string.IsNullOrEmpty(r.RetainedTrip)) {
            RetainedNativeSuspension.Hold(e,"Legacy Away native/ledger identity or living original cannot be verified");return false;
        }
        var trips=packRepository!.GetExpeditions(r.OwnerUid).Where(trip=>trip.SelectedFoxIds.Contains(r.FoxId,StringComparer.Ordinal)).ToArray();
        string authority;
        if(trips.Length==1&&trips[0].ExpeditionId>0)authority="expedition:"+trips[0].ExpeditionId;
        else if(trips.Length==0 && r.PendingReturnAtUtcMs>0 && r.PendingReturnStatus is "Returned healthy" or "Returned injured" or "Mortally wounded"
            && r.Status is "Returning" or "Awaiting safe return")
            authority="pending-return:"+r.PendingReturnAtUtcMs+":"+r.PendingReturnStatus;
        else {RetainedNativeSuspension.Hold(e,"Legacy Away has no unique active expedition or persisted intended return");return false;}
        if(!RetainedNativeSuspension.Qualified(e,out string reason)){RetainedNativeSuspension.Hold(e,reason);return false;}
        if(!RetainedNativeSuspension.LegacyClockSafe(e,out reason)){RetainedNativeSuspension.Hold(e,reason);return false;}
        try {
            if(!ApplyRetainedLoadedHealth(e))return false;
            if(!RetainedNativeSuspension.AdmitLegacy(e,r,authority))return false;
            packRepository.Save();return true; // Engine-memory checkpoint; surviving one-sided admission receipts Hold.
        } catch(Exception ex){RetainedNativeSuspension.Hold(e,"Legacy admission failed: "+ex.GetType().Name);return false;}
    }
    private bool TryPreflightRetainedParty(string ownerUid,IEnumerable<string> ids,out string refusal)
    {
        refusal=string.Empty;if(!RetainedNativeSuspension.Enabled)return true;
        var bodies=new List<Entity>();
        foreach(string id in ids) {
            if(packRepository?.TryGetRecord(id,out FoxPackRecordV2? r)!=true || r==null || r.OwnerUid!=ownerUid
                || serverApi?.World.GetEntityById(r.EntityId) is not Entity e || !IsTamedFox(e) || !IsOwner(e,ownerUid)
                || GetDomesticationStatus(e)?.GetString(FoxIdKey)!=id) {refusal="A selected companion's original identity is unavailable";return false;}
            if(r.BackpackDeliveryActive || backpackPreflights.ContainsKey(e.EntityId)
                || backpackStorageAreaLoads.Contains(id) || backpackMissingEntityRecoveryChecks.Contains(id)) {
                refusal="A selected companion has active or pending backpack delivery work";return false;
            }
            if(!RetainedNativeSuspension.Qualified(e,out refusal))return false;
            ITreeAttribute? marker=RetainedNativeSuspension.Marker(e);
            if(GetDomesticationStatus(e)?.GetBool(ExpeditionAwayKey)==true
                || marker!=null && (marker.GetString("phase")!="Complete" || r.RetainedPhase!="Complete"
                    || !RetainedNativeSuspension.Matches(e,r) || !RetainedNativeSuspension.SafeSnapshot(e)
                    || !marker.GetBool("outcomeApplied")&&!RetainedNativeSuspension.CompletionSettled(e,r))
                || marker==null&&!string.IsNullOrEmpty(r.RetainedTrip)) {
                refusal="A selected companion has unfinished or inconsistent retained authority";return false;
            }
            // Pure preflight: no marker/cache writes, point deductions or callbacks.
            var chunk=serverApi.WorldManager.GetChunk(e.InChunkIndex3d);
            if(chunk==null||chunk.Disposed||chunk.Entities.Take(chunk.EntitiesCount).Count(body=>body.EntityId==e.EntityId)!=1
                || !chunk.Entities.Take(chunk.EntitiesCount).Any(body=>ReferenceEquals(body,e))
                || bodies.Any(body=>body.EntityId==e.EntityId || GetDomesticationStatus(body)?.GetString(FoxIdKey)==id)
                || retainedAdmissionBodies.Values.Concat(loadedFoxes.Values).Any(body=>!ReferenceEquals(body,e)
                    && (body.EntityId==e.EntityId||GetDomesticationStatus(body)?.GetString(FoxIdKey)==id))) {
                refusal="A selected companion has unavailable or ambiguous native world/chunk authority";return false;
            }
            bodies.Add(e);
        }
        return true;
    }
}
