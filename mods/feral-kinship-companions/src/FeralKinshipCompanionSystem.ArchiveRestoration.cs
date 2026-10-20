#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;
public sealed partial class FoxPackRecordV2
{
    [ProtoMember(151)] public bool ManualArchiveRecordRestored;
}
public sealed partial class FeralKinshipCompanionSystem
{
    private readonly Dictionary<string,long> archiveAnimalConfirmations=new(StringComparer.Ordinal);
    private readonly HashSet<string> archiveOriginalLookups=new(StringComparer.Ordinal);
    private bool HasArchiveAnimalConfirmation(FoxPackRecordV2 r)=>serverApi!=null
        && archiveAnimalConfirmations.TryGetValue(r.OwnerUid+":"+r.FoxId,out long until)&&serverApi.World.ElapsedMilliseconds<=until;

    // Ledger restoration and restoring a usable native animal are deliberately
    // separate. Historical records need no health, location or spawn snapshot.
    private string ArchiveAnimalRestorationInfo(FoxPackRecordV2 r,out bool canAttempt)
    {
        canAttempt=false;
        if(serverApi==null || packRepository?.Loaded!=true || !companionContentReady)return "World/content/ledger initialization is not ready.";
        if(packRepository.IsPermanentlyDeleted(r.FoxId))return "Permanently deleted identity; record history can still be restored.";
        if(packRepository.GetExpeditionForMember(r.OwnerUid,r.FoxId)!=null || !string.IsNullOrEmpty(r.PendingReturnStatus)
            || !string.IsNullOrEmpty(r.PendingDepartureStatus) || r.PendingReturnAtUtcMs>0 || r.PendingDepartureAtUtcMs>0)return "Active expedition or pending departure/return must resolve first.";
        if(!string.IsNullOrEmpty(r.RetainedTrip)&&r.RetainedPhase!="Complete")return "Unfinished retained authority ("+r.RetainedPhase+"); original/Hold protection is preserved.";
        Entity? body=r.EntityId>0?serverApi.World.GetEntityById(r.EntityId):null;
        if(body!=null){
            if(GetCompanionOwnerUid(body)!=r.OwnerUid || GetDomesticationStatus(body)?.GetString(FoxIdKey)!=r.FoxId)return "Native ID maps to a conflicting owner/companion; no animal mutation is allowed.";
            if(RetainedNativeSuspension.Marker(body)?.GetString("phase")=="Hold")return "Original is held: "+RetainedNativeSuspension.Marker(body)?.GetString("reason");
            canAttempt=true;return body.Alive?"Bring the existing original to safe space near you; no healing or replacement.":"Confirm manual revival and bring the existing original near you; inventory stays on that body.";}
        if(r.HasLastKnownPosition){canAttempt=true;return "Find the original at its saved location and bring it near you. No replacement will be spawned.";}
        AssetLocation? code=CompanionContentIdentity.ExactCode(r.EntityCode);
        if(code!=null&&serverApi.World.GetEntityType(code)==null)return "Missing entity definition: "+code+". Ledger restoration is still available.";
        return string.IsNullOrEmpty(r.EntityCode)
            ?"Historical record has no entity code, original body or saved location; native inventory/state cannot be reconstructed. Record restoration is available."
            :"Original body and saved location are unavailable; ledger fields are not a native entity/inventory snapshot. Record restoration is available.";
    }

    private Entity? FindArchiveOriginal(FoxPackRecordV2 r,out string reason)
    {
        var lookup=LookupRecoveryOriginal(r);reason=lookup.Reason;
        return lookup.State=="Found"?lookup.Candidates[0].Body:null;
    }

    private bool TryRestoreArchiveOriginal(Entity e,FoxPackRecordV2 r,out string reason)
    {
        reason=string.Empty;
        if(serverApi==null || packRepository?.Loaded!=true || !companionContentReady
            || RuntimeEnv.ServerMainThreadId<=0 || Environment.CurrentManagedThreadId!=RuntimeEnv.ServerMainThreadId){reason="Restoration must run on the initialized server main thread.";return false;}
        if(pendingFullRebuilds.Contains(r.FoxId)||pendingRecoveryBatches.Contains(r.OwnerUid)){reason="A native rebuild/recovery batch is pending; wait for its result.";return false;}
        if(!ReferenceEquals(FindArchiveOriginal(r,out reason),e))return false;
        if(packRepository.IsPermanentlyDeleted(r.FoxId) || packRepository.GetExpeditionForMember(r.OwnerUid,r.FoxId)!=null
            || !string.IsNullOrEmpty(r.PendingDepartureStatus) || !string.IsNullOrEmpty(r.PendingReturnStatus)
            || r.PendingDepartureAtUtcMs>0 || r.PendingReturnAtUtcMs>0){reason="Deleted identity or active/pending expedition authority blocks animal restoration.";return false;}
        var marker=RetainedNativeSuspension.Marker(e);
        if(marker!=null && (marker.GetString("phase")!="Complete" || r.RetainedPhase!="Complete"
                || !RetainedNativeSuspension.Matches(e,r) || !RetainedNativeSuspension.SafeSnapshot(e) || !marker.GetBool("outcomeApplied")&&!RetainedNativeSuspension.CompletionSettled(e,r))
            || marker==null&&!string.IsNullOrEmpty(r.RetainedTrip)){
            reason="Unfinished, mismatched or Hold retained authority cannot be cleared by archive restoration.";return false;
        }
        if(GetDomesticationStatus(e)?.GetBool(ExpeditionAwayKey)==true){reason="The original still has Away authority; diagnose/complete its return before revival.";return false;}
        var chunk=serverApi.WorldManager.GetChunk(e.InChunkIndex3d);
        if(!ReferenceEquals(serverApi.World.GetEntityById(e.EntityId),e) || chunk==null || chunk.Disposed
            || chunk.Entities.Take(chunk.EntitiesCount).Count(x=>x.EntityId==e.EntityId)!=1
            || !chunk.Entities.Take(chunk.EntitiesCount).Any(x=>ReferenceEquals(x,e)) || e.State==EnumEntityState.Despawned || e.Alive&&e.ShouldDespawn || e.Teleporting){reason="Original world/chunk registration is not ready or the body is already despawning/teleporting.";return false;}
        EntityBehaviorHealth? health=e.GetBehavior<EntityBehaviorHealth>();
        if(health==null || !float.IsFinite(health.MaxHealth) || health.MaxHealth<=0){reason="The original has no valid native health behavior; its record/cargo are preserved.";return false;}
        bool revived=!e.Alive;
        try {
            RetainedNativeSuspension.SynchronizeProgress(e,()=>{
                RetainedNativeSuspension.AdmitPresent(e);
                if(revived){
                    e.Revive(); // Explicit confirmed manual resurrection, never a periodic repair.
                    e.WatchedAttributes.SetInt(EntityHealthStateKey,(int)EnumEntityHealthState.Normal);
                    e.WatchedAttributes.SetFloat("regenSpeed",1f);
                    e.WatchedAttributes.SetDouble("mortallyWoundedTotalHours",0);
                    var revivedStatus=GetDomesticationStatus(e,true)!;
                    revivedStatus.SetInt(StabilizedWindowAppliedRankKey,0);
                    revivedStatus.SetDouble(StabilizedWindowAdjustedStartHoursKey,-1);
                    revivedStatus.SetDouble(DeathlessRecoveryAtHoursKey,-1);
                    revivedStatus.SetDouble(DeathlessFallbackAtHoursKey,-1);
                    e.AnimManager?.StopAnimation("wounded-idle");e.AnimManager?.StopAnimation("wounded-resthead");
                }
                if(!e.Alive || !float.IsFinite(health.Health) || health.Health<=0)throw new InvalidOperationException("Native revival did not produce a living original");
                if(revived)RestoreArchivedProgress(e,r);
                if(r.Archived&&!packRepository.UnarchiveRecord(r.OwnerUid,r.FoxId))throw new InvalidOperationException("Archived record authority changed");
                r.ManualArchiveRecordRestored=true;r.EntityId=e.EntityId;r.EntityCode=e.Code.ToShortString();
                r.Status=IsFoxMortallyWounded(e)?"Mortally wounded":e.WatchedAttributes.GetInt(EntityHealthStateKey)==RecoveringHealthState?"Recovering":"Present";
                r.StatusExpiresDay=0;r.CurrentHealth=health.Health;r.MaxHealth=health.MaxHealth;
                r.HealthState=e.WatchedAttributes.GetInt(EntityHealthStateKey);
                if(revived){r.MortallyWoundedStartHours=0;r.StabilizedWindowAppliedRank=0;r.StabilizedWindowAdjustedStartHours=-1;r.DeathlessRecoveryAtHours=-1;r.DeathlessFallbackAtHours=-1;}
                e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.TryTakeInitializedHealth(out _,out _);
                retainedLoadedHealthApplied.Add(e);ledgerHydratedEntities.Add(e.EntityId);loadedFoxes[e.EntityId]=e;
                if(revived)e.State=EnumEntityState.Active;
                MarkSocialStateDirty(e);health.MarkDirty();packRepository.Save();
            });
            reason=revived?"Original manually revived. Identity, native inventory and saved progression retained; no expedition reward replay.":"Existing living original reused; no healing or replacement.";
            return true;
        } catch(Exception ex){RetainedNativeSuspension.Hold(e,"Manual archive restoration failed: "+ex.GetType().Name);reason="Native original restoration failed ("+ex.GetType().Name+"); record/cargo preserved and original held.";return false;}
    }

    private static void RestoreArchivedProgress(Entity e,FoxPackRecordV2 r)
    {
        var s=GetDomesticationStatus(e,true)!;
        if(!IsCompanionJuvenile(e)){s.SetBool(AdulthoodExperienceGrantedKey,true);r.AdulthoodExperienceGranted=true;}
        // No backpack/native inventory hydration, clock rebasing, XP award or
        // request completion. Sparse historical values remain recoverable too.
        s.SetInt(FoxPointsKey,r.DurableStateInitialized?Math.Max(0,r.Points):Math.Max(s.GetInt(FoxPointsKey),r.Points));
        s.SetInt(FoxLifetimePointsKey,Math.Max(s.GetInt(FoxLifetimePointsKey),Math.Max(r.LifetimePoints,r.Points)));
        s.SetInt(RequestsGeneratedKey,Math.Max(s.GetInt(RequestsGeneratedKey),r.RequestsGenerated));
        s.SetInt(RequestsCompletedKey,Math.Max(s.GetInt(RequestsCompletedKey),r.RequestsCompleted));
        if(r.DurableStateInitialized){
            s.SetInt(CompanionLevelKey,Math.Max(1,r.Level));s.SetLong(CompanionCurrentExperienceKey,Math.Max(0,r.CurrentLevelExperience));
            s.SetLong(CompanionLifetimeExperienceKey,Math.Max(r.CurrentLevelExperience,r.LifetimeExperience));s.SetInt(BankedTalentPointsKey,Math.Max(0,r.BankedTalentPoints));
            var perks=GetFoxPerkTree(e,true)!;
            foreach(var definition in FoxPerkCatalog.All){
                int rank=r.TalentRanks?.FirstOrDefault(x=>x.Id==definition.Id)?.Rank??0;
                perks.SetInt(definition.Id,Math.Clamp(rank,0,definition.MaxRank));
            }
            s.SetInt(DamagePerkRankKey,FoxPerkCatalog.GetRank(perks,"damage-training"));
        }
    }

    private void RestoreArchivedAnimal(IServerPlayer owner,string foxId)
    {
        if(serverApi==null || packRepository?.Loaded!=true || !packRepository.TryGetRecord(foxId,out FoxPackRecordV2? r)
            || r==null || r.OwnerUid!=owner.PlayerUID){SendPackState(owner,"Animal restoration refused: no matching owned record.");return;}
        string info=ArchiveAnimalRestorationInfo(r,out bool canAttempt);
        if(!canAttempt){SendPackState(owner,info);return;}
        string key=r.OwnerUid+":"+r.FoxId;long now=serverApi.World.ElapsedMilliseconds;
        if(archiveOriginalLookups.Contains(key)){SendPackState(owner,"Animal restoration is still checking arrival; wait for its result.");return;}
        if(!HasArchiveAnimalConfirmation(r)){archiveAnimalConfirmations[key]=now+30000;SendPackState(owner,"Confirm manual animal restoration within 30 seconds. The original will be brought to safe space near you in the same dimension. A dead original may be revived; no replacement or invented cargo will be created.");return;}
        archiveAnimalConfirmations.Remove(key);
        Entity? existing=FindArchiveOriginal(r,out string problem);
        if(archiveOriginalLookups.Count>=16 || !archiveOriginalLookups.Add(key)){SendPackState(owner,"An original lookup is already running or the lookup limit is reached.");return;}
        if(existing!=null){BringRestoredOriginalToOwner(owner,r,existing,key);return;}
        if(!r.HasLastKnownPosition){archiveOriginalLookups.Remove(key);SendPackState(owner,problem+" No saved location/native snapshot is available. Restore record remains available.");return;}
        int checks=0;bool lookupEnded=false;
        void Check(){
            if(lookupEnded || serverApi==null || !archiveOriginalLookups.Contains(key))return;
            if(packRepository?.TryGetRecord(foxId,out var current)!=true || !ReferenceEquals(current,r) || r.OwnerUid!=owner.PlayerUID){lookupEnded=true;archiveOriginalLookups.Remove(key);return;}
            Entity? original=FindArchiveOriginal(r,out string why);
            if(original!=null){lookupEnded=true;BringRestoredOriginalToOwner(owner,r,original,key);return;}
            if(++checks>=20){lookupEnded=true;archiveOriginalLookups.Remove(key);SendPackState(owner,why+" Original lookup ended; no replacement/cargo was fabricated. Ledger history can still be restored.");return;}
            serverApi.World.RegisterCallback(_=>Check(),500);
        }
        try {
            int x=(int)Math.Floor(r.LastKnownX/(double)GlobalConstants.ChunkSize),z=(int)Math.Floor(r.LastKnownZ/(double)GlobalConstants.ChunkSize);
            if(r.LastKnownDimension==0)serverApi.WorldManager.LoadChunkColumnPriority(x,z,new ChunkLoadOptions{KeepLoaded=false,OnLoaded=()=>serverApi.Event.EnqueueMainThreadTask(Check,"manual-archived-original-lookup")});
            else {serverApi.WorldManager.LoadChunkColumnForDimension(x,z,r.LastKnownDimension);serverApi.World.RegisterCallback(_=>Check(),1000);}
            SendPackState(owner,"Checking the original's saved location. No replacement will be spawned.");
            // The loader callback is not guaranteed after a failed load. One
            // watchdog releases the operation even when OnLoaded never arrives.
            serverApi.World.RegisterCallback(_=>{
                if(!lookupEnded && archiveOriginalLookups.Remove(key)){lookupEnded=true;SendPackState(owner,"Animal restoration could not verify arrival in time. The original and cargo are preserved; refresh before retrying.");}
            },20000);
        } catch(Exception ex){archiveOriginalLookups.Remove(key);SendPackState(owner,"Original location could not be loaded ("+ex.GetType().Name+"). Record/cargo preserved.");}
    }
}
