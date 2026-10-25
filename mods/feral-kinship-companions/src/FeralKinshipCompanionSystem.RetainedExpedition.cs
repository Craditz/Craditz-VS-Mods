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
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public partial class FoxPackRecordV2
{
    [ProtoMember(146)] public string RetainedTrip { get; set; } = string.Empty;
    [ProtoMember(147)] public string RetainedPhase { get; set; } = string.Empty;
    [ProtoMember(148)] public string RetainedOutcome { get; set; } = string.Empty;
    [ProtoMember(149)] public string RetainedLegacyAuthority { get; set; } = string.Empty;
    [ProtoMember(150)] public double RetainedLegacyAdmissionHours { get; set; }
}

public partial class FeralKinshipCompanionSystem
{
    // Returns true when the native body must not enter baseline registration or
    // hydration. A completed marker permanently keeps inventory on that body.
    private bool ReconcileRetainedBody(Entity e)
    {
        if (EmergencyBodyAdmission(e, out bool blocked)) return blocked;
        if(QuarantineRetiredRecoveryOriginal(e))return true;
        if(!RetainedNativeSuspension.Enabled)return false;
        if(!ObserveRetainedAdmission(e) || !RetainedCanonicalAdmission(e))return true;
        if(packRepository?.Loaded!=true)return true;
        string foxId=GetDomesticationStatus(e)?.GetString(FoxIdKey,string.Empty) ?? string.Empty;
        packRepository.TryGetRecord(foxId,out FoxPackRecordV2? r);
        if(GuardNativeLinkAdmission(e))return true;
        ITreeAttribute? m=RetainedNativeSuspension.Marker(e);
        if (r != null) ReconcileReturnJournal(e, r);
        if(r!=null&&RetainedNativeSuspension.InProtectedTransition(e,r))return true;
        if(m?.GetString("phase")=="Hold") {
            e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.RefreshExpeditionSuspension();return true;
        }
        if(m==null) {
            if(GetDomesticationStatus(e)?.GetBool(ExpeditionAwayKey)==true
                || r!=null && (!string.IsNullOrEmpty(r.RetainedTrip)
                    || r.Status.StartsWith("Away \u2014",StringComparison.OrdinalIgnoreCase)
                    || r.Status is "MIA" or "Running late" or "Returning" or "Awaiting safe return")) {
                if(!TryAdmitLegacyAway(e,r)) {
                    e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.RefreshExpeditionSuspension();return true;
                }
                m=RetainedNativeSuspension.Marker(e);
            } else {
                RetainedNativeSuspension.AdmitPresent(e);
                e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.RefreshExpeditionSuspension();return false;
            }
        }
        bool unique=!loadedFoxes.Values.Any(other=>!ReferenceEquals(e,other)
            && GetDomesticationStatus(other)?.GetString(FoxIdKey)==foxId);
        if(!unique) foreach(Entity other in loadedFoxes.Values.Where(other=>!ReferenceEquals(e,other)
            && GetDomesticationStatus(other)?.GetString(FoxIdKey)==foxId))
            RetainedNativeSuspension.Hold(other,"Duplicate loaded companion identity; preserve both bodies");
        if(r==null || m==null || !unique || !RetainedNativeSuspension.Matches(e,r)
            || m.GetString("phase")!=r.RetainedPhase || r.RetainedPhase is not ("Leaving" or "Suspended" or "Returning" or "Complete")) {
            RetainedNativeSuspension.Hold(e,"Native/ledger phase, owner, identity or trip mismatch; preserve original");
            e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.RefreshExpeditionSuspension();return true;
        }
        if((!string.IsNullOrEmpty(r.RetainedLegacyAuthority)||!string.IsNullOrEmpty(m.GetString("legacyAuthority")))
            && (string.IsNullOrEmpty(r.RetainedLegacyAuthority)||m.GetString("legacyAuthority")!=r.RetainedLegacyAuthority
                || !double.IsFinite(r.RetainedLegacyAdmissionHours)||r.RetainedLegacyAdmissionHours<0
                || m.GetDouble("legacyAdmissionHours",double.NaN)!=r.RetainedLegacyAdmissionHours
                || m.GetDouble("capturedHours",double.NaN)!=r.RetainedLegacyAdmissionHours)) {
            RetainedNativeSuspension.Hold(e,"Legacy admission receipt conflict; never reset admission clocks");return true;
        }
        if(r.RetainedPhase is "Suspended" or "Returning" && !RetainedNativeSuspension.SafeSnapshot(e)) {
            RetainedNativeSuspension.Hold(e,"Suspended native receipt lacks its original valid snapshot");return true;
        }
        if(r.RetainedPhase is "Complete" or "Leaving") {
            if(r.RetainedPhase=="Complete" && (!RetainedNativeSuspension.SafeSnapshot(e)||!m.GetBool("outcomeApplied")&&!RetainedNativeSuspension.CompletionSettled(e,r))) {
                RetainedNativeSuspension.Hold(e,"Completed native receipt lacks a valid original snapshot/outcome");return true;
            }
            if(GetDomesticationStatus(e)?.GetBool(ExpeditionAwayKey)==true) {
                RetainedNativeSuspension.Hold(e,"Present/Leaving native phase disagrees with saved Away flag");return true;
            }
            if(!RetainedNativeSuspension.HydrateProgressOnce(e,()=>HydrateRetainedProgress(e,r)))return true;
            if(r.RetainedPhase=="Complete")RetainedNativeSuspension.RestoreBoxes(e);
            RetainedNativeSuspension.AdmitPresent(e);
            e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.RefreshExpeditionSuspension();
            return false;
        }
        if(!RetainedNativeSuspension.HydrateProgressOnce(e,()=>HydrateRetainedProgress(e,r)))return true;
        if(!ApplyRetainedLoadedHealth(e))return true;
        loadedFoxes[e.EntityId]=e;
        RefreshRetainedSourceLocation(e);
        // A saved Returning marker is only dispatch intent. A new verified
        // placement callback is required; an in-memory completion is not assumed.
        e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.RefreshExpeditionSuspension();return true;
    }

    private bool PrepareRetainedProgressAward(Entity e)
    {
        string transitionId=GetDomesticationStatus(e)?.GetString(FoxIdKey,string.Empty)??string.Empty;
        if(packRepository?.TryGetRecord(transitionId,out var transitionRecord)==true&&transitionRecord!=null&&RetainedNativeSuspension.InProtectedTransition(e,transitionRecord))return false;
        if(!RetainedNativeSuspension.Enabled || !RetainedNativeSuspension.NativeAuthority(e)
            && GetDomesticationStatus(e)?.GetBool(ExpeditionAwayKey)!=true)return true;
        ReconcileRetainedBody(e);
        if(!RetainedCanonicalAdmission(e) || packRepository?.Loaded!=true)return false;
        return RetainedNativeSuspension.Marker(e)?.GetString("phase") is "Leaving" or "Suspended" or "Returning" or "Complete";
    }

    // Ledger authority is limited to these Companions progression fields. Absolute
    // projection cannot replay awards, pack points, health, inventory or timers.
    private bool CanHydrateRetainedProgress(Entity e,FoxPackRecordV2 r)
    {
        ITreeAttribute? s=GetDomesticationStatus(e);
        if(s==null || r.CurrentLevelExperience<0 || r.LifetimeExperience<r.CurrentLevelExperience
            || r.Points<0 || r.BankedTalentPoints<0 || r.LifetimePoints<(long)r.Points+r.BankedTalentPoints
            || r.Level<GetCompanionLevel(s) || r.LifetimeExperience<GetCompanionLifetimeExperience(s)
            || r.LifetimePoints<Math.Max(0,s.GetInt(FoxLifetimePointsKey,0))
            || r.LifetimeExperience==GetCompanionLifetimeExperience(s)
                && (r.Level!=GetCompanionLevel(s) || r.CurrentLevelExperience!=GetCompanionCurrentExperience(s))
            || r.RetainedPhase is "Suspended" or "Returning"
                && (r.Points<Math.Max(0,s.GetInt(FoxPointsKey,0))
                    || r.BankedTalentPoints<Math.Max(0,s.GetInt(BankedTalentPointsKey,0)))) {
            return false;
        }
        return true;
    }
    private bool HydrateRetainedProgress(Entity e,FoxPackRecordV2 r)
    {
        if(!CanHydrateRetainedProgress(e,r)) {
            RetainedNativeSuspension.Hold(e,"Progression ledger is behind or conflicts with native progress; preserve both authorities");return false;
        }
        ITreeAttribute s=GetDomesticationStatus(e)!;
        s.SetInt(CompanionLevelKey,Math.Max(CompanionProgressionRules.StartingLevel,r.Level));
        s.SetLong(CompanionCurrentExperienceKey,Math.Max(0,r.CurrentLevelExperience));
        s.SetLong(CompanionLifetimeExperienceKey,Math.Max(r.CurrentLevelExperience,r.LifetimeExperience));
        s.SetInt(FoxPointsKey,Math.Max(0,r.Points));s.SetInt(FoxLifetimePointsKey,Math.Max(0,r.LifetimePoints));
        s.SetInt(BankedTalentPointsKey,Math.Max(0,r.BankedTalentPoints));
        s.SetBool(AdulthoodExperienceGrantedKey,r.AdulthoodExperienceGranted);
        MarkSocialStateDirty(e);return true;
    }

    private void SyncRetainedProgressFromBody(Entity e)
    {
        if(!RetainedNativeSuspension.NativeAuthority(e) || packRepository?.Loaded!=true)return;
        string id=GetDomesticationStatus(e)?.GetString(FoxIdKey,string.Empty) ?? string.Empty;
        if(!packRepository.TryGetRecord(id,out FoxPackRecordV2? r) || r==null
            || !RetainedNativeSuspension.Matches(e,r) || RetainedNativeSuspension.Marker(e)?.GetString("phase")!=r.RetainedPhase
            || r.RetainedPhase is not ("Leaving" or "Suspended" or "Returning" or "Complete"))return;
        RetainedNativeSuspension.SynchronizeProgress(e,()=> {
            ITreeAttribute s=GetDomesticationStatus(e)!;
            r.Level=GetCompanionLevel(s);r.CurrentLevelExperience=GetCompanionCurrentExperience(s);
            r.LifetimeExperience=GetCompanionLifetimeExperience(s);
            r.Points=Math.Max(0,s.GetInt(FoxPointsKey,0));r.LifetimePoints=Math.Max(0,s.GetInt(FoxLifetimePointsKey,0));
            r.BankedTalentPoints=Math.Max(0,s.GetInt(BankedTalentPointsKey,0));
            r.AdulthoodExperienceGranted=s.GetBool(AdulthoodExperienceGrantedKey,false);
        });
    }

    private bool HandleRetainedExpeditionStatus(string ownerUid,string foxId,string label,FoxPackRecordV2 r)
    {
        if(!RetainedNativeSuspension.Enabled)return false;
        if(ReturnSupersededByRebuild(r)) { CancelSupersededReturn(r.FoxId);return true; }
        Entity? e=r.EntityId>0?serverApi?.World.GetEntityById(r.EntityId):null;
        if(e==null && label.StartsWith("Away \u2014",StringComparison.OrdinalIgnoreCase)) {
            r.PendingDepartureAtUtcMs=UtcNowMs()+ReturnRetryDelayMs;r.PendingDepartureDeadlineUtcMs=r.PendingDepartureAtUtcMs;
            if(r.HasLastKnownPosition && r.LastKnownDimension==0)
                RequestReturnDestinationColumn(new BlockPos(r.LastKnownX,r.LastKnownY,r.LastKnownZ,0));
            return true;
        }
        if(e==null && !string.IsNullOrEmpty(r.PendingReturnStatus)) {
            RequestRetainedOriginalColumn(r,ownerUid,foxId,label,UtcNowMs());
            WaitForReturn(r,null,"Original unavailable","Loading the recorded original; no replacement is created");
            if (ResolveReturnCart(ownerUid,out bool unknown)==null && !unknown) OfferMissingReturnCart(r);
            return true;
        }
        bool retained=!string.IsNullOrEmpty(r.RetainedTrip) || e!=null && RetainedNativeSuspension.NativeAuthority(e);
        // Legacy away is held rather than sent into the old dispatch/heal path.
        if(!retained) {
            if(e==null || !IsFoxAwayFromWorld(e))return false;
            ReconcileRetainedBody(e);
            if(!RetainedNativeSuspension.Matches(e,r))return true;
        }
        if(e==null || !IsTamedFox(e) || !IsOwner(e,ownerUid)) {
            long now=UtcNowMs();
            if(e==null)RequestRetainedOriginalColumn(r,ownerUid,foxId,label,now);
            if (!string.IsNullOrEmpty(r.PendingReturnStatus)) {
                WaitForReturn(r,null,"Original unavailable",e==null?"Loading the recorded original; no replacement is created":"Native body ownership or type is inconsistent");
                if (ResolveReturnCart(ownerUid,out bool unknown)==null && !unknown) OfferMissingReturnCart(r);
            }
            return true;
        }
        if(!RetainedNativeSuspension.Matches(e,r)) { RetainedNativeSuspension.Hold(e,"Expedition status authority mismatch");return true; }
        // Baseline Start/Queue emits U+2014. An ASCII '-' check misroutes an
        // ordinary departure into the return-only PendingReturnStatus guard.
        if(label.StartsWith("Away \u2014",StringComparison.OrdinalIgnoreCase)) {
            if(!RetainedNativeSuspension.Suspend(e,r))return true;
            RecordWellbeingDeparture(r);r.Status=label;r.StatusExpiresDay=0;
            r.PendingDepartureStatus=string.Empty;r.PendingDepartureAtUtcMs=0;r.PendingDepartureDeadlineUtcMs=0;r.PendingDepartureMoment=false;
            ITreeAttribute s=GetDomesticationStatus(e,true)!;s.SetString(ExpeditionStatusKey,label);
            SetFoxAwayFromWorld(e,true);packRepository?.Save();return true;
        }
        if(label is "MIA" or "Running late") { r.Status=label;SetFoxAwayFromWorld(e,true);return true; }
        // Return must have been authorized by QueueExpeditionReturns; arbitrary
        // status repair must never manufacture a return outcome.
        if(r.PendingReturnStatus!=label || string.IsNullOrEmpty(label)) {
            RetainedNativeSuspension.Hold(e,"No intended return outcome in ledger");return true;
        }
        // The original can unload between departure preflight and its walk's
        // suspension acknowledgement. Finish that exact native transition;
        // never invent historical clocks or reconstruct another animal.
        if(r.RetainedPhase=="Leaving" && RetainedNativeSuspension.Marker(e)?.GetString("phase")=="Leaving") {
            if(!RetainedNativeSuspension.Suspend(e,r))return true;
            SetFoxAwayFromWorld(e,true);
        }
        // A priority-loaded original may enter the world index before ordinary
        // companion registration. Restore authoritative offline progress/health
        // through the existing reconciler before return can admit that body.
        EnsureReturnTransit(r);
        ReconcileRetainedBody(e);
        if(!RetainedCanonicalAdmission(e) || !RetainedNativeSuspension.Matches(e,r)
            || r.RetainedPhase is not ("Suspended" or "Returning")
            || RetainedNativeSuspension.Marker(e)?.GetString("phase")!=r.RetainedPhase) {
            WaitForReturn(r,e,"Original not admitted",RetainedNativeSuspension.Marker(e)?.GetString("reason") ?? "Waiting for canonical body/chunk registration");return true;
        }
        TryReturnRetainedBody(e,r,label);return true;
    }

    private void TryReturnRetainedBody(Entity e,FoxPackRecordV2 r,string label)
    {
        try { TryReturnRetainedBodyCore(e,r,label); }
        catch(Exception ex) { WaitForReturn(r,e,"Return preparation failed",ex.GetType().Name+": "+ex.Message); }
    }

    private void TryReturnRetainedBodyCore(Entity e,FoxPackRecordV2 r,string label)
    {
        if(serverApi==null || packRepository==null)return;
        var journal=EnsureReturnTransit(r);
        r.PendingReturnAtUtcMs=UtcNowMs()+ReturnRetryDelayMs;
        if(returnAttempts.ContainsKey(r.FoxId))return;
        if(e.Teleporting) { WaitForReturn(r,null,"Native teleport pending","Waiting for an existing native teleport without overlapping it");return; }
        if(!TrySelectReturnDestination(e,r,out BlockPos cart,out Vec3d safe,out string kind))return;
        if(!RetainedNativeSuspension.BeginReturn(e,r,label))return;
        journal.LastAttemptUtcMs=UtcNowMs();journal.Attempts++;journal.Stage="Teleporting";journal.Failure=string.Empty;journal.Destination=kind;
        var dispatch=new ReturnAttempt {Body=e,Trip=r.RetainedTrip,Outcome=label,Kind=kind,Anchor=cart.Copy(),Safe=safe.Clone(),Started=UtcNowMs()};
        returnAttempts[r.FoxId]=dispatch;
        packRepository.Save(); // Engine-memory checkpoint, not a durable disk flush.
        string trip=r.RetainedTrip;
        try {
            e.TeleportToDouble(safe.X,safe.Y,safe.Z,()=>{
                // Catch inside the engine callback: throwing here prevents its
                // Teleporting/IsTeleport cleanup from running.
                try {
                    // Run after the native callback clears its teleport flags.
                    dispatch.CallbackSeen=true;
                    serverApi.World.RegisterCallback(_=>VerifyReturnAttempt(r,dispatch,0),0);
                } catch(Exception ex) { FailReturnAttempt(r,dispatch,"Teleport callback failed: "+ex.GetType().Name); }
            });
        } catch(Exception ex) { FailReturnAttempt(r,dispatch,"Teleport dispatch failed: "+ex.GetType().Name); }
    }

    private bool RetainedDestinationChunkReady(Entity e,Vec3d safe)
        => CheckRetainedPlacementAuthority(e,safe)==RetainedPlacementAuthority.Ready;

    private enum RetainedPlacementAuthority { Waiting, Ready, Held }
    private RetainedPlacementAuthority CheckRetainedPlacementAuthority(Entity e,Vec3d safe)
    {
        if(serverApi==null || e.Pos.Dimension!=0)return RetainedPlacementAuthority.Waiting;
        string id=GetDomesticationStatus(e)?.GetString(FoxIdKey,string.Empty) ?? string.Empty;
        if(string.IsNullOrEmpty(id)) {RetainedNativeSuspension.Hold(e,"Destination companion identity is missing");return RetainedPlacementAuthority.Held;}
        int size=serverApi.WorldManager.ChunkSize;if(size<=0)return RetainedPlacementAuthority.Waiting;
        long index=serverApi.WorldManager.ChunkIndex3D((int)Math.Floor(safe.X/size),(int)Math.Floor(safe.Y/size),(int)Math.Floor(safe.Z/size));
        var chunk=serverApi.WorldManager.GetChunk(index);
        Entity[] nativeMembers=Array.Empty<Entity>();
        Entity[]? backing=null;int count=0;
        if(chunk!=null&&!chunk.Disposed) {
            backing=chunk.Entities;count=chunk.EntitiesCount;
            if(backing==null||count<0||count>backing.Length) {RetainedNativeSuspension.Hold(e,"Malformed destination chunk identity surface");return RetainedPlacementAuthority.Held;}
            nativeMembers=backing.Take(count).ToArray();
            if(nativeMembers.Any(body=>body==null)) {RetainedNativeSuspension.Hold(e,"Missing destination chunk entity reference");return RetainedPlacementAuthority.Held;}
        }
        var loaded=serverApi.World.LoadedEntities;
        Entity? canonical=serverApi.World.GetEntityById(e.EntityId);
        var loadedSnapshot=loaded?.ToArray();
        Entity? loadedAtId=loadedSnapshot?.FirstOrDefault(pair=>pair.Key==e.EntityId).Value;
        var observedSnapshot=retainedAdmissionBodies.ToArray();
        var registeredSnapshot=loadedFoxes.ToArray();
        Entity? observedAtId=observedSnapshot.FirstOrDefault(pair=>pair.Key==e.EntityId).Value;
        Entity? registeredAtId=registeredSnapshot.FirstOrDefault(pair=>pair.Key==e.EntityId).Value;
        Entity[] contenders=nativeMembers.Concat(loadedSnapshot?.Select(pair=>pair.Value)??Enumerable.Empty<Entity>())
            .Concat(observedSnapshot.Select(pair=>pair.Value)).Concat(registeredSnapshot.Select(pair=>pair.Value))
            .Concat(canonical==null?Array.Empty<Entity>():new[]{canonical})
            .Where(body=>body!=null&&!ReferenceEquals(body,e)
                && (body.EntityId==e.EntityId||GetDomesticationStatus(body)?.GetString(FoxIdKey)==id
                    || RetainedNativeSuspension.Marker(body)?.GetString("companionId")==id
                    || RetainedNativeSuspension.Marker(body)?.GetLong("nativeId")==e.EntityId
                    || ReferenceEquals(body,canonical)||ReferenceEquals(body,loadedAtId)
                    || ReferenceEquals(body,observedAtId)||ReferenceEquals(body,registeredAtId)))
            .Distinct(ReferenceEqualityComparer.Instance).Cast<Entity>().ToArray();
        bool duplicateOriginal=nativeMembers.Count(body=>ReferenceEquals(body,e))>1
            || loadedSnapshot?.Count(pair=>ReferenceEquals(pair.Value,e))>1
            || (loadedSnapshot??Array.Empty<KeyValuePair<long,Entity>>()).Concat(observedSnapshot).Concat(registeredSnapshot)
                .Any(pair=>ReferenceEquals(pair.Value,e)&&pair.Key!=e.EntityId);
        if(contenders.Length>0 || duplicateOriginal) {
            // Publish protection to each original; never remove a native body,
            // overwrite the engine lists, choose a winner or project inventory.
            RetainedNativeSuspension.DeferAdmission(e);
            foreach(Entity other in contenders)RetainedNativeSuspension.DeferAdmission(other);
            foreach(Entity other in contenders)RetainedNativeSuspension.Hold(other,"Ambiguous return destination identity; preserve both original inventories");
            RetainedNativeSuspension.Hold(e,"Ambiguous return destination identity; preserve both original inventories");return RetainedPlacementAuthority.Held;
        }
        if(chunk==null||chunk.Disposed||e.InChunkIndex3d!=index||nativeMembers.Count(body=>ReferenceEquals(body,e))!=1
            || canonical==null||loaded==null||!loaded.TryGetValue(e.EntityId,out Entity? indexed)||!ReferenceEquals(indexed,e))
            return RetainedPlacementAuthority.Waiting;
        // Detect chunk publication changes during the read. Supported completion
        // runs on the engine server main thread, not the independent physics thread.
        if(!ReferenceEquals(serverApi.WorldManager.GetChunk(index),chunk)||chunk.Disposed
            || !ReferenceEquals(chunk.Entities,backing)||chunk.EntitiesCount!=count
            || !chunk.Entities.Take(count).SequenceEqual(nativeMembers,ReferenceEqualityComparer.Instance)
            || !ReferenceEquals(serverApi.World.GetEntityById(e.EntityId),e))
            return RetainedPlacementAuthority.Waiting;
        return RetainedPlacementAuthority.Ready;
    }

    private void VerifyRetainedPlacement(Entity e,FoxPackRecordV2 r,string label,BlockPos cart,Vec3d safe,string trip,int attempt)
    {
        if(RuntimeEnv.ServerMainThreadId<=0 || Environment.CurrentManagedThreadId!=RuntimeEnv.ServerMainThreadId) {
            return; // The main-thread watchdog retries verification safely.
        }
        RetainedNativeSuspension.SynchronizeProgress(e,()=>VerifyRetainedPlacementCore(e,r,label,cart,safe,trip,attempt));
    }
    private void VerifyRetainedPlacementCore(Entity e,FoxPackRecordV2 r,string label,BlockPos cart,Vec3d safe,string trip,int attempt)
    {
        try {
            if(serverApi==null || packRepository==null || r.RetainedTrip!=trip || r.RetainedPhase=="Complete")return;
            if(!returnAttempts.TryGetValue(r.FoxId,out var dispatch) || dispatch.Expired || !ReferenceEquals(dispatch.Body,e))return;
            RetainedPlacementAuthority authority=CheckRetainedPlacementAuthority(e,safe);
            if(authority==RetainedPlacementAuthority.Held) { FailReturnAttempt(r,dispatch,"Conflicting destination identity; original quarantined");return; }
            if(r.PendingReturnStatus!=label
                || !RetainedNativeSuspension.Matches(e,r) || r.RetainedPhase!="Returning"
                || e.Teleporting || e.Pos.Dimension!=0 || e.Pos.XYZ.SquareDistanceTo(safe)>0.01) {
                if(e.Teleporting) return;
                FailReturnAttempt(r,dispatch,"Placement authority or position changed");return;
            }
            if(!ReturnDestinationStillValid(r,dispatch)) {
                FailReturnAttempt(r,dispatch,"Destination changed or owner left allowed territory");return;
            }
            // Native UpdateEvery1000ms owns chunk membership independently of
            // Entity.OnGameTick. Never move chunk lists or recreate a body here.
            if(authority!=RetainedPlacementAuthority.Ready) {
                if(attempt>=100) { FailReturnAttempt(r,dispatch,"Destination chunk membership remains pending");return; }
                serverApi.World.RegisterCallback(_=>VerifyReturnAttempt(r,dispatch,attempt+1),100);return;
            }
            if(!IsSafeEntityPosition(e.Pos.AsBlockPos,safe,e.Properties,e)) {
                // Another party member may have completed at this point while
                // native teleport/chunk placement was still pending. No outcome
                // has begun: preserve the original and let the normal queue
                // choose a new safe destination after its existing retry delay.
                FailReturnAttempt(r,dispatch,"Safe placement became occupied");return;
            }
            e.PositionBeforeFalling.Set(safe.X,safe.Y,safe.Z);
            var journal=EnsureReturnTransit(r);
            if(!journal.OutcomePrepared) {
                journal.WasMortallyWounded=e.GetBehavior<EntityBehaviorMortallyWoundable>()?.HealthState==EnumEntityHealthState.MortallyWounded;
                journal.OutcomeHours=RetainedNativeSuspension.Marker(e)?.GetDouble("resumeHours",serverApi.World.Calendar.TotalHours)??serverApi.World.Calendar.TotalHours;
                journal.OutcomePrepared=true;packRepository.Save();
            }
            RetainedNativeSuspension.Marker(e)!.SetDouble("resumeHours",journal.OutcomeHours);
            bool completed=RetainedNativeSuspension.CompletePlaced(e,r,()=>ApplyRetainedOutcome(e,r,label),()=>{
                r.PendingReturnStatus=string.Empty;r.PendingReturnAtUtcMs=0;r.Status=label;
                r.StatusExpiresDay=serverApi.World.Calendar.TotalDays+TransientExpeditionStatusDisplayDays;
                GetDomesticationStatus(e,true)!.SetBool(ExpeditionAwayKey,false);
                GetDomesticationStatus(e,true)!.SetString(ExpeditionStatusKey,label=="Mortally wounded"?string.Empty:label);
                packRepository.Save();
            },()=> {
                if(CheckRetainedPlacementAuthority(e,safe)==RetainedPlacementAuthority.Ready
                    && RuntimeEnv.ServerMainThreadId==Environment.CurrentManagedThreadId
                    && e.Alive&&ReturnDestinationStillValid(r,dispatch)
                    && RetainedNativeSuspension.Matches(e,r)&&r.RetainedTrip==trip
                    && RetainedNativeSuspension.Marker(e)?.GetString("phase")==r.RetainedPhase
                    && r.RetainedPhase is "Returning" or "Resuming" or "Complete"
                    && r.RetainedOutcome==label&&RetainedNativeSuspension.Marker(e)?.GetString("outcome")==label
                    && (r.RetainedPhase=="Complete" ? r.Status==label&&r.PendingReturnStatus==string.Empty : r.PendingReturnStatus==label)
                    && !e.Teleporting&&e.Pos.Dimension==0&&e.Pos.XYZ.SquareDistanceTo(safe)<=0.01
                    && IsSafeEntityPosition(e.Pos.AsBlockPos,safe,e.Properties,e))return true;
                return false;
            });
            if(completed) { journal.Stage="Complete";journal.Failure=string.Empty;dispatch.Expired=true;returnAttempts.Remove(r.FoxId);MarkSocialStateDirty(e);RetainedNativeSuspension.RestoreBoxes(e);RegisterFoxInPack(e); }
            else { r.PendingReturnStatus=label;FailReturnAttempt(r,dispatch,"Completion interrupted; original and outcome receipt retained"); }
        } catch(Exception ex) {
            r.PendingReturnStatus=label;
            if(returnAttempts.TryGetValue(r.FoxId,out var failed))FailReturnAttempt(r,failed,"Return completion failed: "+ex.GetType().Name);
            else WaitForReturn(r,e,"Return completion failed",ex.GetType().Name);
        }
    }

    private void ApplyRetainedOutcome(Entity e,FoxPackRecordV2 r,string label)
    {
        EntityBehaviorHealth health=e.GetBehavior<EntityBehaviorHealth>() ?? throw new InvalidOperationException("Missing native health");
        if(!e.Alive || health.Health<=0 || health.MaxHealth<=0 || !float.IsFinite(health.Health)||!float.IsFinite(health.MaxHealth))throw new InvalidOperationException("Cannot resurrect or heal an invalid original");
        if(label=="Returned injured")health.Health=Math.Min(health.Health,Math.Max(1f,health.MaxHealth*.5f));
        if(label=="Mortally wounded") {
            health.Health=Math.Min(health.Health,Math.Min(1f,health.MaxHealth));
            EntityBehaviorMortallyWoundable mortal=e.GetBehavior<EntityBehaviorMortallyWoundable>() ?? throw new InvalidOperationException("Missing mortality behavior");
            if(r.ReturnTransit?.WasMortallyWounded!=true) {
                mortal.HealthState=EnumEntityHealthState.MortallyWounded;
                mortal.MortallyWoundedTotalHours=r.ReturnTransit?.OutcomeHours??e.World.Calendar.TotalHours;mortal.HealthHealed=0;mortal.HealthDamaged=0;
            }
        }
        // MarkDirty also calls native UpdateMaxHealth, which may refill a full
        // bar if modifiers changed. Health's setter already dirties the tree.
        e.WatchedAttributes.MarkPathDirty("health");r.CurrentHealth=health.Health;r.MaxHealth=health.MaxHealth;
        // No ReceiveDamage bypass token; outcome is a single protected transition.
    }
}
