#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

// Bounded adapter for the inspected 1.22.7 native/PetAI/TamablesFotsa paths.
// The entity's watched marker is the body authority. No inventory is copied.
internal static partial class RetainedNativeSuspension
{
    internal const string Key = "feralKinshipRetainedNativeExpeditionV1";
    private sealed class Gate { internal readonly object Sync = new(); internal int Blocked; internal volatile bool Admitted; internal bool TeleportPending; internal bool ProgressHydrated; internal bool Completing,Migrating; internal NativeStorageLocation? StorageLocation; }
    private static readonly ConditionalWeakTable<Entity, Gate> gates = new();
    private static readonly HashSet<MethodBase> installed = new();
    internal static bool Enabled { get; private set; }
    internal static bool HealthInitializationAccountingEnabled { get; private set; }
    internal static bool ChunkSerializationOrderingEnabled { get; private set; }
    private static Harmony? harmony;
    internal static ITreeAttribute? Marker(Entity e) => e.WatchedAttributes.GetTreeAttribute(Key);
    internal static bool NativeAuthority(Entity e) => Marker(e) != null;
    internal static bool HydrateProgressOnce(Entity e, Func<bool> hydrate) {
        Gate g=gates.GetValue(e,_=>new());lock(g.Sync) {
            if(g.Admitted||g.ProgressHydrated)return true;
            try {
                if(!hydrate())return false;
                g.ProgressHydrated=true;return true;
            } catch(Exception ex) { Hold(e,"Progression hydration failed: "+ex.GetType().Name);return false; }
        }
    }
    internal static void SynchronizeProgress(Entity e, Action synchronize) {
        Gate g=gates.GetValue(e,_=>new());lock(g.Sync)synchronize();
    }
    internal static void DeferAdmission(Entity e) { Gate g=gates.GetValue(e,_=>new());Volatile.Write(ref g.Blocked,1); }
    internal static bool Protected(Entity e)
    {
        Gate g = gates.GetValue(e, _ => new());
        if (Volatile.Read(ref g.Blocked) != 0) return true;
        if (Enabled && g.Admitted && e.Api?.Side==EnumAppSide.Server) return false;
        ITreeAttribute? m = Marker(e);
        if (m != null && m.GetString("phase") is not ("Leaving" or "Complete")) return true;
        ITreeAttribute? s = FeralKinshipCompanionSystem.GetDomesticationStatus(e);
        if (s?.GetBool("feralKinshipExpeditionAway") == true) return true;
        // Admission precedes ledger hydration. On clients the native marker is
        // authoritative; no server ledger is guessed from packet order.
        return Enabled && e.Api?.Side != EnumAppSide.Client && !g.Admitted
            && !string.IsNullOrWhiteSpace(s?.GetString("owner")) && FeralKinshipCompanionSystem.IsTamedFox(e);
    }
    internal static string DescribePresentation(Entity e)
    {
        gates.TryGetValue(e,out Gate? g);var m=Marker(e);var status=FeralKinshipCompanionSystem.GetDomesticationStatus(e);
        return $"code={e.Code} alive={e.Alive} state={e.State} pos={e.Pos.X:0.##},{e.Pos.Y:0.##},{e.Pos.Z:0.##}/{e.Pos.Dimension} "
            +$"marker={m?.GetString("phase")??"none"} hold={m?.GetString("reason")??"none"} captured={m?.GetBool("captured")} outcome-applied={m?.GetBool("outcomeApplied")} "
            +$"away-flag={status?.GetBool("feralKinshipExpeditionAway")} blocked={g?.Blocked??0} admitted={g?.Admitted??false} protected={Protected(e)} our-hidden={FeralKinshipCompanionSystem.IsFoxAwayFromWorld(e)} "
            +$"boxes={e.CollisionBox?.XSize:0.###}/{e.SelectionBox?.XSize:0.###} social-latch={e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.HasExpeditionPresentationSuppression}";
    }
    internal static void AdmitPresent(Entity e) {
        Gate g=gates.GetValue(e,_=>new()); lock(g.Sync) {
            if(FeralKinshipCompanionSystem.GetDomesticationStatus(e)?.GetBool("feralKinshipExpeditionAway")==true) { Volatile.Write(ref g.Blocked,1);return; }
            g.Admitted=true; Volatile.Write(ref g.Blocked,0);
        }
    }
    internal static void ObserveAway(Entity e) {
        Gate g=gates.GetValue(e,_=>new());Volatile.Write(ref g.Blocked,1);
        if(Marker(e)?.GetString("phase") is not ("Suspended" or "Returning" or "Resuming" or "Hold"))
            Hold(e,"Away transition has no suspended native snapshot");
    }
    internal static void Hold(Entity e, string reason)
    {
        Gate g=gates.GetValue(e,_=>new()); Volatile.Write(ref g.Blocked,1);
        lock(g.Sync) {
            ITreeAttribute m=Marker(e) ?? new TreeAttribute();
            bool report=m.GetString("phase")!="Hold" || m.GetString("reason")!=reason;
            m.SetString("phase","Hold"); m.SetString("reason",reason);
            e.WatchedAttributes.SetAttribute(Key,m); Dirty(e);
            // A failure can occur after restoring boxes but before admission.
            // Keep the original snapshot intact and close world surfaces again.
            e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.RefreshExpeditionSuspension();
            e.SetCollisionBox(0,0); e.SetSelectionBox(0,0); e.State=EnumEntityState.Inactive;
            e.Pos.Motion.Set(0,0,0);
            if(report) try { e.World?.Logger?.Warning("[FeralKinshipCompanions] Retained suspension held native entity {0}: {1}",e.EntityId,reason); } catch { /* Protection must not depend on logging. */ }
        }
    }
    internal static bool Matches(Entity e, FoxPackRecordV2 r) => Marker(e) is ITreeAttribute m
        && m.GetLong("nativeId")==e.EntityId && r.EntityId==e.EntityId
        && m.GetString("owner")==r.OwnerUid && m.GetString("companionId")==r.FoxId
        && FeralKinshipCompanionSystem.GetDomesticationStatus(e)?.GetString("owner")==r.OwnerUid
        && FeralKinshipCompanionSystem.GetDomesticationStatus(e)?.GetString("feralKinshipFoxId")==r.FoxId
        && m.GetString("trip")==r.RetainedTrip && !string.IsNullOrEmpty(r.RetainedTrip)
        && m.GetString("code")==e.Code?.ToString();
    internal static bool Bind(Entity e, FoxPackRecordV2 r)
    {
        if (!Qualified(e,out string reason)) { Hold(e,reason); return false; }
        Gate g=gates.GetValue(e,_=>new()); lock(g.Sync) {
            if (Marker(e) is ITreeAttribute prior && prior.GetString("phase")!="Complete") return false;
            var m=new TreeAttribute(); string trip=Guid.NewGuid().ToString("N");
            m.SetLong("nativeId",e.EntityId); m.SetString("owner",r.OwnerUid);
            m.SetString("companionId",r.FoxId); m.SetString("trip",trip);
            m.SetString("code",e.Code.ToString()); m.SetString("phase","Leaving");
            e.WatchedAttributes.SetAttribute(Key,m); r.RetainedTrip=trip; r.RetainedPhase="Leaving";
            r.RetainedOutcome=string.Empty;r.RetainedLegacyAuthority=string.Empty;r.RetainedLegacyAdmissionHours=0;r.CompletionProof=null;
            g.StorageLocation=null;g.Admitted=true; Dirty(e); return true;
        }
    }
    internal static bool LegacyClockSafe(Entity e,out string reason)
    {
        double hours=e.World.Calendar.TotalHours;
        if(!double.IsFinite(hours)||hours<0){reason="Invalid legacy admission calendar";return false;}
        EntityBehaviorHealth? health=e.GetBehavior<EntityBehaviorHealth>();
        if(health==null||!float.IsFinite(health.Health)||!float.IsFinite(health.MaxHealth)||health.Health<=0||health.MaxHealth<=0){reason="Legacy original has invalid or nonliving health; no resurrection";return false;}
        if(e.WatchedAttributes.GetInt("healthState")== (int)EnumEntityHealthState.MortallyWounded) {
            EntityBehaviorMortallyWoundable? mortal=e.GetBehavior<EntityBehaviorMortallyWoundable>();
            double start=e.WatchedAttributes.GetDouble("mortallyWoundedTotalHours",double.NaN);
            float limit=mortal==null?0:(float)(AccessTools.Field(typeof(EntityBehaviorMortallyWoundable),"remainAliveHours").GetValue(mortal) ?? 0f);
            if(!double.IsFinite(start)||!float.IsFinite(limit)||limit<=0||hours<start||hours-start>=limit) {
                reason="Legacy mortal rescue window is expired or unverifiable; no healing or reset";return false;
            }
        }
        reason="";return true;
    }
    internal static bool AdmitLegacy(Entity e,FoxPackRecordV2 r,string authority)
    {
        Gate g=gates.GetValue(e,_=>new());Volatile.Write(ref g.Blocked,1);
        lock(g.Sync) {
            if(Marker(e)!=null || !string.IsNullOrEmpty(r.RetainedTrip))return false;
            if(!Qualified(e,out string reason)){Hold(e,reason);return false;}
            if(!LegacyClockSafe(e,out reason)){Hold(e,reason);return false;}
            double hours=e.World.Calendar.TotalHours;
            var m=new TreeAttribute();m.SetLong("nativeId",e.EntityId);m.SetString("owner",r.OwnerUid);
            m.SetString("companionId",r.FoxId);m.SetString("trip",Guid.NewGuid().ToString("N"));m.SetString("code",e.Code.ToString());
            m.SetString("phase","Leaving");m.SetString("legacyAuthority",authority);m.SetDouble("legacyAdmissionHours",hours);
            e.WatchedAttributes.SetAttribute(Key,m);r.RetainedTrip=m.GetString("trip");r.RetainedPhase="Leaving";
            r.RetainedLegacyAuthority=authority;r.RetainedLegacyAdmissionHours=hours;
            // A single persistent capture starts NOW, never at an invented
            // historical departure time. Surviving one-sided receipts Hold.
            return Suspend(e,r);
        }
    }
    internal static bool Suspend(Entity e, FoxPackRecordV2 r)
    {
        Gate g=gates.GetValue(e,_=>new()); Volatile.Write(ref g.Blocked,1);
        lock(g.Sync) {
            if (!Matches(e,r) || !e.Alive) { Hold(e,"Suspension identity or living body mismatch"); return false; }
            ITreeAttribute m=Marker(e)!;
            if (m.GetString("phase")=="Suspended") return true;
            if (m.GetString("phase")!="Leaving") { Hold(e,"Unexpected suspension phase"); return false; }
            // Publish protection before stopping tasks or invoking any provider.
            m.SetString("phase","Suspended"); r.RetainedPhase="Suspended"; Dirty(e);
            try {
                double hours=e.World.Calendar.TotalHours;
                if(!double.IsFinite(hours)||hours<0)throw new InvalidOperationException("Invalid capture calendar");
                m.SetDouble("capturedHours",hours);
                CaptureBoxes(e,m); CaptureClocks(e,m); m.SetBool("captured",true); Dirty(e);
                e.SetCollisionBox(0,0); e.SetSelectionBox(0,0); e.State=EnumEntityState.Inactive;
                if(e is EntityAgent a) { a.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.StopTasks(); a.Controls.StopAllMovement(); }
                e.Pos.Motion.Set(0,0,0); return true;
            } catch(Exception ex) { Hold(e,"Suspend callback failed: "+ex.GetType().Name); return false; }
        }
    }
    internal static bool BeginReturn(Entity e,FoxPackRecordV2 r,string outcome)
    {
        Gate g=gates.GetValue(e,_=>new()); lock(g.Sync) {
            if (!Matches(e,r) || !e.Alive || !Qualified(e,out _)) { Hold(e,"Unsafe return authority/provider"); return false; }
            ITreeAttribute m=Marker(e)!; string phase=m.GetString("phase");
            if (phase is not ("Suspended" or "Returning")) return false;
            if (phase=="Returning" && m.GetString("outcome")!=outcome) { Hold(e,"Conflicting return outcome"); return false; }
            m.SetString("phase","Returning"); m.SetString("outcome",outcome);
            r.RetainedPhase="Returning"; r.RetainedOutcome=outcome; Dirty(e);
            if(g.TeleportPending) return false; g.TeleportPending=true; return true;
        }
    }
    internal static void Wait(Entity e) { Gate g=gates.GetValue(e,_=>new()); lock(g.Sync) g.TeleportPending=false; }
    internal static bool CompletePlaced(Entity e,FoxPackRecordV2 r, Action applyOutcome, Action persist, Func<bool> verifyPlacement)
    {
        Gate g=gates.GetValue(e,_=>new()); lock(g.Sync) {
            ITreeAttribute? m=Marker(e);
            if(Matches(e,r)&&m?.GetString("phase")=="Complete"&&r.RetainedPhase=="Complete"
                &&(m.GetBool("outcomeApplied")||CompletionSettled(e,r))) return false; // Duplicate/settled completion is a no-op.
            if (!Matches(e,r) || !e.Alive || !Qualified(e,out _) || e.Teleporting || !g.TeleportPending || m?.GetString("phase")!="Returning"
                || r.RetainedOutcome!=m.GetString("outcome") || !m.GetBool("captured")) {
                Hold(e,"Placement receipt disagrees with native/ledger authority"); return false;
            }
            g.Completing=true;
            try {
                // The production verifier runs inside this same body monitor.
                // Recheck at callback boundaries; no engine-wide chunk lock is invented.
                if(!verifyPlacement()) {PauseReturnCompletion(e,r,"Placement authority changed during completion");return false;}
                m.SetString("phase","Resuming"); r.RetainedPhase="Resuming"; Dirty(e);
                // An interrupted outcome is held on load, never automatically replayed.
                RestoreClocks(e,m);
                if(!verifyPlacement()) {PauseReturnCompletion(e,r,"Placement authority changed during completion");return false;}
                applyOutcome(); m.SetBool("outcomeApplied",true); Dirty(e);
                if(!verifyPlacement()) {PauseReturnCompletion(e,r,"Placement authority changed during completion");return false;}
                RestoreBoxes(e); e.Pos.Motion.Set(0,0,0);
                if(!verifyPlacement()) {PauseReturnCompletion(e,r,"Placement authority changed during completion");return false;}
                m.SetString("phase","Complete"); r.RetainedPhase="Complete";
                r.CompletionProof=new RetainedCompletionProof {Trip=r.RetainedTrip,Owner=r.OwnerUid,Companion=r.FoxId,NativeId=e.EntityId,Code=e.Code.ToString(),
                    CapturedHours=m.GetDouble("capturedHours"),ResumeHours=m.GetDouble("resumeHours"),Outcome=r.RetainedOutcome};
                persist(); Dirty(e);
                if(!verifyPlacement()) {PauseReturnCompletion(e,r,"Placement authority changed during completion");return false;}
                g.Admitted=true; g.TeleportPending=false; Volatile.Write(ref g.Blocked,0);
                e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.EndRetainedSuspension();
                PublishCompletedPresentation(e); return true;
            } catch(Exception ex) { PauseReturnCompletion(e,r,"Resume callback failed: "+ex.GetType().Name); return false; }
            finally {g.Completing=false;}
        }
    }
    private static void PauseReturnCompletion(Entity e,FoxPackRecordV2 r,string reason)
    {
        var j=r.ReturnTransit;
        if(j==null || j.Trip!=r.RetainedTrip || j.NativeId!=e.EntityId || string.IsNullOrEmpty(j.Outcome)
            || !Matches(e,r) || !SafeSnapshot(e) || Marker(e)?.GetString("phase")=="Hold") {Hold(e,reason);return;}
        Gate g=gates.GetValue(e,_=>new());
        var m=Marker(e)!;m.SetString("phase","Resuming");m.SetString("reason",reason);
        r.RetainedPhase="Resuming";r.PendingReturnStatus=j.Outcome;
        FeralKinshipCompanionSystem.GetDomesticationStatus(e)!.SetBool("feralKinshipExpeditionAway",true);
        g.TeleportPending=false;g.Admitted=false;Volatile.Write(ref g.Blocked,1);
        e.SetCollisionBox(0,0);e.SetSelectionBox(0,0);e.State=EnumEntityState.Inactive;Dirty(e);
    }
    // Only a caller with initialized, canonical completed ledger authority may
    // use this. Never manufactures a completion, replays an outcome or clocks,
    // removes a marker, or opens Hold/Suspended/Returning bodies.
    internal static bool ReconcileCompletedPresentation(Entity e,FoxPackRecordV2 r)
    {
        Gate g=gates.GetValue(e,_=>new());lock(g.Sync) {
            ITreeAttribute? m=Marker(e);
            if(!Enabled || e.Api?.Side!=EnumAppSide.Server || !e.Alive || e.Teleporting
                || !Matches(e,r) || r.RetainedPhase!="Complete" || m?.GetString("phase")!="Complete"
                || !SafeSnapshot(e) || !CompletionSettled(e,r))return false;
            var status=FeralKinshipCompanionSystem.GetDomesticationStatus(e)!;
            var social=e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>();
            bool suppressed=Volatile.Read(ref g.Blocked)!=0 || !g.Admitted
                || status.GetBool("feralKinshipExpeditionAway") || social?.HasExpeditionPresentationSuppression==true;
            if(suppressed) {
                RestoreBoxes(e);
                status.SetBool("feralKinshipExpeditionAway",false);
                social?.EndRetainedSuspension();
                g.Admitted=true;g.TeleportPending=false;Volatile.Write(ref g.Blocked,0);
            }
            // Re-send the current full owned surfaces even when the server is
            // already correct: a client may have missed the original return.
            PublishCompletedPresentation(e);return true;
        }
    }
    private static void PublishCompletedPresentation(Entity e)
    {
        Dirty(e);
        e.WatchedAttributes.MarkPathDirty("domesticationstatus");
    }
    private static void Dirty(Entity e) => e.WatchedAttributes.MarkPathDirty(Key);
    private static void CaptureBoxes(Entity e,ITreeAttribute m)
    {
        if(e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.CopyRetainedOriginalBoxes(m)==true)return;
        m.SetInt("state",(int)e.State);
        Cuboidf?[] boxes={e.CollisionBox,e.OriginCollisionBox,e.SelectionBox,e.OriginSelectionBox};
        for(int i=0;i<boxes.Length;i++) if(boxes[i] is Cuboidf b)
            m["box"+i]=new FloatArrayAttribute(new[]{b.X1,b.Y1,b.Z1,b.X2,b.Y2,b.Z2});
    }
    internal static void RestoreBoxes(Entity e)
    {
        ITreeAttribute? m=Marker(e); if(m?.GetBool("captured")!=true) return;
        if(!SafeSnapshot(e))throw new InvalidOperationException("Invalid original native snapshot");
        Cuboidf? Box(int i) { float[]? v=(m["box"+i] as FloatArrayAttribute)?.value; return v?.Length==6?new Cuboidf(v[0],v[1],v[2],v[3],v[4],v[5]):null; }
        if(Box(0) is Cuboidf a)e.CollisionBox=a; if(Box(1) is Cuboidf b)e.OriginCollisionBox=b;
        if(Box(2) is Cuboidf c)e.SelectionBox=c; if(Box(3) is Cuboidf d)e.OriginSelectionBox=d;
        e.State=(EnumEntityState)m.GetInt("state");
    }
    internal static bool SafeSnapshot(Entity e)
    {
        ITreeAttribute? m=Marker(e);
        if(m?.GetBool("captured")!=true || !double.IsFinite(m.GetDouble("capturedHours")) || m.GetTreeAttribute("clocks")==null
            || (EnumEntityState)m.GetInt("state") is not (EnumEntityState.Active or EnumEntityState.Inactive))return false;
        for(int i=0;i<4;i++) {
            float[]? b=(m["box"+i] as FloatArrayAttribute)?.value;
            if(b?.Length!=6 || b.Any(v=>!float.IsFinite(v)) || b[3]<=b[0] || b[4]<=b[1] || b[5]<=b[2])return false;
        }
        return true;
    }
    // Only inspected calendar fields, stored once with their native numeric type.
    // Opaque provider trees, inventory and non-clock fields stay on the original.
    private static readonly (string Tree,string Field,bool Days)[] clocks={
        ("multiply","totalDaysPregnancyStart",true),("multiply","totalDaysLastBirth",true),
        ("multiply","totalDaysCooldownUntil",true),("multiply","feralKinshipPregnancyDueDays",true),
        ("grow","timeSpawned",false),("","mortallyWoundedTotalHours",false),
        ("domesticationstatus","disobedienceTime",false),("domesticationstatus","cooldown",false),
        ("domesticationstatus","feralKinshipPregnancyStartTotalHours",false),
        ("domesticationstatus","feralKinshipPregnancyDueTotalHours",false),
        ("domesticationstatus","feralKinshipFoodLastUpdateTotalHours",false),
        ("domesticationstatus","feralKinshipStabilizedWindowAdjustedStartHours",false),
        ("domesticationstatus","feralKinshipDeathlessRecoveryAtHours",false),
        ("domesticationstatus","feralKinshipDeathlessFallbackAtHours",false)};
    private static ITreeAttribute? ClockTree(Entity e,string tree)=>tree==""?e.WatchedAttributes:tree=="@attributes"?e.Attributes:e.WatchedAttributes.GetTreeAttribute(tree);
    private static void CaptureClocks(Entity e,ITreeAttribute m)
    {
        var saved=new TreeAttribute();
        for(int i=0;i<clocks.Length;i++) {
            IAttribute? value=ClockTree(e,clocks[i].Tree)?[clocks[i].Field];
            if(value is DoubleAttribute invalidDouble && !double.IsFinite(invalidDouble.value)
                || value is FloatAttribute invalidFloat && !float.IsFinite(invalidFloat.value))throw new InvalidOperationException("Invalid native clock");
            if(value is DoubleAttribute d && d.value>=0) saved.SetDouble(i.ToString(),d.value);
            else if(value is FloatAttribute f && f.value>=0) saved.SetFloat(i.ToString(),f.value);
        }
        m["clocks"]=saved;
    }
    private static void RestoreClocks(Entity e,ITreeAttribute m)
    {
        double now=e.World.Calendar.TotalHours, start=m.GetDouble("capturedHours");
        if(!double.IsFinite(now)||!double.IsFinite(start)||now<start||!float.IsFinite(e.World.Calendar.HoursPerDay)||e.World.Calendar.HoursPerDay<=0) throw new InvalidOperationException("Invalid or backwards calendar");
        double resume=m.HasAttribute("resumeHours")?m.GetDouble("resumeHours"):now;
        if(!double.IsFinite(resume)||resume<start||resume>now)throw new InvalidOperationException("Invalid resume receipt");
        m.SetDouble("resumeHours",resume); Dirty(e);
        ITreeAttribute saved=m.GetTreeAttribute("clocks") ?? throw new InvalidOperationException("Missing clocks");
        for(int i=0;i<clocks.Length;i++) {
            IAttribute? value=saved[i.ToString()]; if(value==null) continue;
            var (tree,field,days)=clocks[i]; ITreeAttribute target=ClockTree(e,tree) ?? throw new InvalidOperationException("Missing native clock tree");
            double shift=(resume-start)/(days?e.World.Calendar.HoursPerDay:1d);
            if(!double.IsFinite(shift))throw new InvalidOperationException("Invalid elapsed calendar");
            if(value is DoubleAttribute d) {
                double restored=d.value+shift;if(!double.IsFinite(restored))throw new InvalidOperationException("Invalid restored native clock");target.SetDouble(field,restored);
            }
            else if(value is FloatAttribute f) {
                float restored=(float)(f.value+shift);if(!float.IsFinite(restored))throw new InvalidOperationException("Invalid restored native clock");target.SetFloat(field,restored);
            }
            e.WatchedAttributes.MarkPathDirty(tree==""?field:tree);
        }
    }
    internal static bool Qualified(Entity e,out string reason)
    {
        reason="Retained suspension hooks are unavailable"; if(!Enabled)return false;
        if(e is not EntityAgent agent || e.EntityId<=0 || !e.Alive || e.ShouldDespawn || e.Pos.Dimension!=0 || agent.MountedOn!=null) {
            reason="Retained suspension requires a living unmounted native agent in dimension 0";return false;
        }
        if(FeralKinshipCompanionSystem.IsBackpackDeliveryActive(e)) {
            reason="A backpack delivery job is active; wait for native cargo work to finish";return false;
        }
        if(!CompanionSpeciesCatalog.TryGetByTameEntityCode(e.Code,out _)) {
            reason="Body is not an established supported companion family";return false;
        }
        if(e.CollisionBox==null || e.SelectionBox==null) { reason="Native boxes are unavailable";return false; }
        // Pin the engine contracts used by native suspension/save ordering.
        // Add-on admission is compatibility-first; patch auditing is separate.
        foreach(string engine in new[]{"VintagestoryAPI","VintagestoryLib"}) {
            Assembly? a=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name==engine);
            if(a==null || !Verified(a,hashes[engine])) { reason="Unqualified engine binary: "+engine;return false; }
        }
        if(!CoveredType(e.GetType(),entityEntries,out reason))return false;
        foreach(EntityBehavior b in e.SidedProperties.Behaviors) {
            if(!CoveredType(b.GetType(),e.Api.Side==EnumAppSide.Client?behaviorEntries:serverBehaviorEntries,out reason))return false;
            for(Type? provider=b.GetType();provider!=null;provider=provider.BaseType) {
                if(!AuditedImplementation(provider)||!provider.Name.Contains("Tameable"))continue;
                foreach(string field in new[]{"replacementCallbackId","callbackId"}) {
                    if(AccessTools.Field(provider,field)?.GetValue(b) is long id && id!=0) {
                        reason="A provider replacement callback is pending";return false;
                    }
                }
            }
            // Keep audited clock/rearm contracts. Unknown add-on timers are not
            // admission blockers and are not guessed, cancelled or rebased.
            if(!TimerCapabilitiesCovered(b.GetType(),out reason))return false;
            if(b is EntityBehaviorTaskAI ai) {
                if(ai.TaskManager==null){reason="Native AI task manager is unavailable";return false;}
                foreach(IAiTask task in ai.TaskManager.AllTasks) {
                    for(Type? t=task.GetType();NativeAdmissionContract(task.GetType())&&t!=null;t=t.BaseType)if(t.IsGenericType) {
                        reason="Unqualified generic native AI sound/callback path: "+task.GetType().FullName;return false;
                    }
                    if(!CoveredType(task.GetType(),aiEntries,out reason)||!TimerCapabilitiesCovered(task.GetType(),out reason))return false;
                }
            }
        }
        reason="";return true;
    }
    private static bool CoveredType(Type t,string[] entries,out string reason)
    {
        for(Type? current=t;current!=null && current!=typeof(object);current=current.BaseType) {
            Assembly a=current.Assembly;string name=a.GetName().Name ?? "";
            if(NativeEngineAssembly(name)&&(!hashes.TryGetValue(name,out string? hash)||!Verified(a,hash))) {
                reason="Unqualified engine binary: "+name;return false;
            }
        }
        // An add-on may override or inherit unfamiliar native entry shapes.
        // Admission must not turn those metadata shapes into another allowlist.
        if(!NativeAdmissionContract(t)) {reason="";return true;}
        foreach(string entry in entries) {
            MethodInfo? method=AccessTools.Method(t,entry);
            if(method?.DeclaringType is Type declaring && declaring!=typeof(object) && NativeAdmissionContract(declaring)
                && !installed.Any(gate=>gate.Module==method.Module && gate.MetadataToken==method.MetadataToken)) {
                reason="Missing suspension gate: "+method.DeclaringType?.FullName+"."+entry;return false;
            }
        }
        reason="";return true;
    }
    private static bool TimerCapabilitiesCovered(Type type,out string reason)
    {
        if(!NativeAdmissionContract(type)) {reason="";return true;}
        // Native and owned clock/rearm contracts only. Add-on IL is not an
        // admission allowlist, even when an optional patch-audited DLL is loaded.
        for(Type? t=type;t!=null && t!=typeof(object);t=t.BaseType) {
            if(!NativeAdmissionContract(t))continue;
            foreach(MethodBase method in t.GetMethods(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly).Cast<MethodBase>()
                .Concat(t.GetConstructors(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
                .Concat(t.TypeInitializer==null?Array.Empty<MethodBase>():new MethodBase[]{t.TypeInitializer})) {
                bool registers;
                try {registers=RegistersTimer(method);}
                catch(Exception ex) {reason="Unreadable independent timer method: "+t.FullName+"."+method.Name+"/"+ex.GetType().Name;return false;}
                if(!registers)continue;
                string name=t.FullName ?? "";
                if(t==typeof(EntityBehaviorFeralKinshipFoxSocial)
                    && method is MethodInfo refresh && !refresh.IsStatic
                    && refresh.Name=="QueueCompanionBackpackRefresh"
                    && refresh.ReturnType==typeof(void)
                    && refresh.GetParameters().Select(p=>p.ParameterType).SequenceEqual(new[]{typeof(Vintagestory.API.Client.ICoreClientAPI)}))continue;
                if(t==typeof(EntityBehaviorGrow) || t==typeof(EntityBehaviorMultiply)
                    || name=="Vintagestory.GameContent.EntityBehaviorHunger"
                    || name=="Vintagestory.GameContent.EntityBehaviorHealth"
                    || t==typeof(AiTaskBase)
                    || t==typeof(AiTaskFeralKinshipSourceIdleSound))continue;
                reason="Unaudited independent timer registration: "+name+"."+method.Name;return false;
            }
        }
        // Nested closure methods are outside GetMethods on their owning class.
        // No additional registration in those classes is part of the audited
        // pinned entity families. Refuse rather than hide deferred registration.
        for(Type? t=type;t!=null && t!=typeof(object);t=t.BaseType) {
            if(!NativeAdmissionContract(t))continue;
            var nested=new Stack<Type>(t.GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic));
            while(nested.Count>0) {
                Type closure=nested.Pop();
                foreach(Type child in closure.GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic))nested.Push(child);
                foreach(MethodBase method in closure.GetMethods(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly).Cast<MethodBase>()
                    .Concat(closure.GetConstructors(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
                    .Concat(closure.TypeInitializer==null?Array.Empty<MethodBase>():new MethodBase[]{closure.TypeInitializer})) {
                    try {
                        if(RegistersTimer(method)) {reason="Unaudited nested timer registration: "+closure.FullName+"."+method.Name;return false;}
                    } catch(Exception ex) {reason="Unreadable nested timer method: "+closure.FullName+"."+method.Name+"/"+ex.GetType().Name;return false;}
                }
            }
        }
        reason="";return true;
    }
    private static bool RegistersTimer(MethodBase method)
    {
        byte[]? il=method.GetMethodBody()?.GetILAsByteArray();if(il==null)return false;
        // Parse instructions, never search arbitrary bytes as metadata tokens.
        int at=0;while(at<il.Length) {
            ushort code=il[at++];if(code==0xfe)code=(ushort)(0xfe00|il[at++]);
            if(!opcodes.TryGetValue(code,out System.Reflection.Emit.OpCode op))return true;
            int size=op.OperandType switch {
                System.Reflection.Emit.OperandType.InlineNone=>0,
                System.Reflection.Emit.OperandType.ShortInlineBrTarget or System.Reflection.Emit.OperandType.ShortInlineI or System.Reflection.Emit.OperandType.ShortInlineVar=>1,
                System.Reflection.Emit.OperandType.InlineVar=>2,
                System.Reflection.Emit.OperandType.InlineI8 or System.Reflection.Emit.OperandType.InlineR=>8,
                System.Reflection.Emit.OperandType.InlineSwitch=>4+4*BitConverter.ToInt32(il,at),_=>4 };
            if(op.OperandType==System.Reflection.Emit.OperandType.InlineMethod) {
                MethodBase? called=method.Module.ResolveMethod(BitConverter.ToInt32(il,at),method.DeclaringType?.GetGenericArguments(),method.IsGenericMethod?method.GetGenericArguments():null);
                if(called?.Name is "RegisterCallback" or "RegisterGameTickListener" or "EnqueueMainThreadTask")return true;
            }
            at+=size;
        }
        return false;
    }
    private static readonly Dictionary<ushort,System.Reflection.Emit.OpCode> opcodes=typeof(System.Reflection.Emit.OpCodes)
        .GetFields().Where(f=>f.FieldType==typeof(System.Reflection.Emit.OpCode)).Select(f=>(System.Reflection.Emit.OpCode)f.GetValue(null)!)
        .ToDictionary(op=>(ushort)op.Value);
    private static readonly string[] entityEntries={"ReceiveDamage","Die","Revive","OnGameTick","OnInteract","PlayEntitySound","OnReceivedClientPacket","IntersectsRay","ToBytes"};
    private static readonly string[] behaviorEntries={"OnGameTick","OnPhysicsTick","AfterPhysicsTick","HandleRemotePhysics","OnInteract","OnReceivedClientPacket","OnEntityReceiveDamage","OnEntityDeath","OnRenderFrame","SlowTick","CheckMultiply","CheckGrowth","Disobey","PurgeIfOwnerless","SpawnTameVariant","TryRestoreTamedVariant","ScheduleTamedVariantReplacement","QueueCompanionBackpackRefresh"};
    // Render callbacks cannot execute on a server, and even reading their IL
    // can resolve client-only OpenGL dependencies. Keep their client coverage
    // separate from the shared entity/physics/save/AI gates.
    private static readonly string[] serverBehaviorEntries=behaviorEntries.Where(n=>n!="OnRenderFrame").ToArray();
    private static readonly string[] aiEntries={"StartExecute","ShouldExecute","ContinueExecute","FinishExecute","Notify","OnEntityHurt","OnStateChanged"};
    private static bool NativeEngineAssembly(string name) => name is "VintagestoryAPI" or "VintagestoryLib" or "VSEssentials" or "VSSurvivalMod";
    private static bool NativeAdmissionContract(Type type) => NativeEngineAssembly(type.Assembly.GetName().Name ?? "")
        || type.Assembly.GetName().Name=="FeralKinshipCompanions";
    private static bool AuditedImplementation(Type type) {
        Assembly assembly=type.Assembly;string name=assembly.GetName().Name ?? "";
        return name=="FeralKinshipCompanions" || hashes.TryGetValue(name,out string? hash)&&Verified(assembly,hash);
    }
    // Patch-coverage pins, not an add-on admission allowlist. A changed/unlisted
    // mod is admitted without extending this table or patching arbitrary code.
    private static readonly Dictionary<string,string> hashes=new() {
        ["VintagestoryAPI"]="034283e7e9d98eae45ee63005576fd89badc3c995b531cc4c3fe46f3eb2d3296",
        ["VintagestoryLib"]="e08f22b493b92feaf0aaeb79d22437ea0f7efc38aa7f72a04a47f98bc0e40df0",
        ["VSEssentials"]="a28565c5c9181f8cc84b98a2b7457ab824b8ecb7763714448da1f7c245aecd6e",
        ["VSSurvivalMod"]="d67b48a321403b2052b33c7d0caa99611f92350ffac73ea72672901dd87ccd7a",
        ["FeralKinship"]="2c38e889f6c7b6f487960bc4653e227811ce03d4d799173cc1088fae8356bb8e",
        ["TamablesCritters"]="b2220c3ed865f180f77c865c3c596509b3dfd27d85dabc6bd3c1c0301e289086",
        ["Cats"]="f9802562c9f262a0ca30816b712ccb427d4ea199f5dde2e32eac1dbf04548c3b",
        ["WolfTaming"]="2fc21870add28e17cabd23e15157d8215e48018f092a8dd6f232997a4cb56d89",
        ["PetAI"]="6fe98340c1db266c8f554c47dd69d1ed8c5d787040ef95650679018de816e1f2",
        ["TamablesFotsa"]="1eaba584a81046e957b49036858b59ccdaf9bfe53c4069af8d887139e282e4ee" };
    private static readonly Dictionary<Assembly,bool> verified=new();
    internal static string[] NativePatchEntries => entityEntries.Concat(behaviorEntries).Concat(serverBehaviorEntries).Concat(aiEntries).Append("UpdateMaxHealth").Distinct().ToArray();
    private static bool Verified(Assembly a,string hash) {
        if(a.GetName().Name=="VintagestoryLib")return NativeChunkCompatibility.TryQualify(a,out _);
        if(NativeEngineAssembly(a.GetName().Name??"")&&NativeChunkCompatibility.UsesOptimum(typeof(Vintagestory.Common.WorldChunk).Assembly))return NativeOptimumCompatibility.QualifiesModule(a);
        lock(verified) {
            if(verified.TryGetValue(a,out bool result))return result;
            try { result=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location))).Equals(hash,StringComparison.OrdinalIgnoreCase); }
            catch { result=false; } verified[a]=result;return result;
        }
    }
    // A dedicated server may load the engine assembly without its client-only
    // audio/render dependencies. Keep all successfully loaded types; the caller
    // still restricts patch coverage to hash-qualified behaviors and AI tasks.
    internal static Type[] GetLoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>().ToArray();
        }
    }
    internal static IReadOnlyList<MethodBase> Install(Harmony owner)
    {
        NativeChunkCompatibility.Require(typeof(Vintagestory.Common.WorldChunk).Assembly);
        var targets=new HashSet<MethodBase>();
        var targetPredicates=new HashSet<MethodBase>();
        void Add(Type t,string name) { MethodInfo? m=AccessTools.DeclaredMethod(t,name); if(m!=null&&!m.IsAbstract&&!m.ContainsGenericParameters)targets.Add(m); }
        foreach(Type t in new[]{typeof(Entity),typeof(EntityAgent)}) foreach(string n in entityEntries) Add(t,n);
        Add(typeof(Entity),"get_IsInteractable");
        string[] entries=serverBehaviorEntries;
        foreach(Assembly a in AppDomain.CurrentDomain.GetAssemblies()) {
            string name=a.GetName().Name ?? "";
            if(name!="FeralKinshipCompanions" && (!hashes.TryGetValue(name,out string? expected)||!Verified(a,expected)))continue;
            Type[] types = GetLoadableTypes(a);
            foreach(Type t in types.Where(t=>typeof(EntityBehavior).IsAssignableFrom(t)&&!t.ContainsGenericParameters)) foreach(string n in entries) Add(t,n);
            foreach(Type t in types.Where(t=>typeof(IAiTask).IsAssignableFrom(t)&&!t.ContainsGenericParameters)) {
                foreach(string n in aiEntries)Add(t,n);
                // Inspected native and provider target predicates accept an
                // Entity first. Check the target, not the attacker's admission.
                foreach(MethodInfo predicate in t.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)
                    .Where(m=>!m.IsAbstract&&!m.ContainsGenericParameters&&m.ReturnType==typeof(bool)
                        && m.GetParameters().FirstOrDefault()?.ParameterType==typeof(Entity)
                        && (m.Name.StartsWith("IsTargetableEntity",StringComparison.Ordinal)||m.Name=="CanSense")))
                    targetPredicates.Add(predicate);
            }
        }
        foreach(MethodInfo callback in typeof(AiTaskBase).GetMethods(BindingFlags.Instance|BindingFlags.NonPublic).Where(m=>m.Name.StartsWith("<StartExecute>",StringComparison.Ordinal)))targets.Add(callback);
        foreach(MethodInfo method in typeof(AiTaskManager).GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)
            .Where(m=>!m.ContainsGenericParameters && m.Name is "OnGameTick" or "ExecuteTask" or "Notify" or "OnEntityHurt" or "StartNewTasks" or "ProcessRunningTasks"))targets.Add(method);
        Add(typeof(EntityBehaviorMortallyWoundable),"Die");
        Add(typeof(EntityBehaviorMortallyWoundable),"recover");
        foreach(MethodBase m in targets) {
            string prefix=m.Name=="ToBytes"?nameof(SerializeEntry):m.Name=="get_IsInteractable"?nameof(Interaction):((MethodInfo)m).ReturnType==typeof(bool)?nameof(BooleanEntry):nameof(Entry);
            if(((MethodInfo)m).ReturnType!=typeof(bool)&&((MethodInfo)m).ReturnType!=typeof(void))continue;
            owner.Patch(m,new HarmonyMethod(typeof(RetainedNativeSuspension),prefix){priority=Priority.First},
                finalizer:new HarmonyMethod(typeof(RetainedNativeSuspension),nameof(Exit)));
            installed.Add(m);
        }
        foreach(MethodBase predicate in targetPredicates) {
            owner.Patch(predicate,new HarmonyMethod(typeof(RetainedNativeSuspension),nameof(TargetEntry)){priority=Priority.First});
            installed.Add(predicate);
        }
        // This observer never blocks stat recalculation. It distinguishes the
        // inspected native full-bar refill from real HP changes before admission.
        if (Verified(typeof(EntityBehaviorHealth).Assembly, hashes["VSEssentials"])) {
            MethodInfo nativeHealth = AccessTools.DeclaredMethod(typeof(EntityBehaviorHealth), "UpdateMaxHealth")
                ?? throw new InvalidOperationException("Pinned native health recalculation is unavailable");
            owner.Patch(nativeHealth,
                prefix:new HarmonyMethod(typeof(RetainedNativeSuspension),nameof(HealthUpdateEntry)){priority=Priority.First},
                postfix:new HarmonyMethod(typeof(RetainedNativeSuspension),nameof(HealthUpdateCompleted)){priority=Priority.First},
                finalizer:new HarmonyMethod(typeof(RetainedNativeSuspension),nameof(HealthUpdateExit)));
            installed.Add(nativeHealth);
            HealthInitializationAccountingEnabled = true;
        }
        // Pinned native chunk serialization owns packUnpackLock before calling
        // Entity.ToBytes. Actor/physics/return work owns the body gate before
        // reading a chunk. Never wait for a body while retaining that chunk.
        Assembly nativeChunks = typeof(Vintagestory.Common.WorldChunk).Assembly;
        NativeChunkCompatibility.Require(nativeChunks);
        Type serverChunk = nativeChunks.GetType("Vintagestory.Server.ServerChunk",true)!;
        MethodInfo chunkBytes = AccessTools.DeclaredMethod(serverChunk,"ToBytes",new[]{typeof(FastMemoryStream)})
            ?? throw new MissingMethodException("Pinned native chunk serializer");
        chunkPackLock = AccessTools.Field(typeof(Vintagestory.Common.WorldChunk),"packUnpackLock")
            ?? throw new MissingFieldException("Pinned native chunk monitor");
        chunkEntities = AccessTools.Field(typeof(Vintagestory.Common.WorldChunk),"Entities")
            ?? throw new MissingFieldException("Pinned native chunk membership");
        chunkDirtyForSaving = AccessTools.Field(serverChunk,"DirtyForSaving")
            ?? throw new MissingFieldException("Pinned native chunk save retry flag");
        owner.Patch(chunkBytes,
            prefix:new HarmonyMethod(typeof(RetainedNativeSuspension),nameof(ChunkSerializationEntry)){priority=Priority.First},
            finalizer:new HarmonyMethod(typeof(RetainedNativeSuspension),nameof(ChunkSerializationExit)));
        installed.Add(chunkBytes); ChunkSerializationOrderingEnabled=true;
        MethodInfo commitChunks=AccessTools.DeclaredMethod(typeof(Vintagestory.Common.GameDatabase),"SetChunks")
            ?? throw new MissingMethodException("Pinned native chunk batch commit");
        owner.Patch(commitChunks,
            prefix:new HarmonyMethod(typeof(RetainedNativeSuspension),nameof(ChunkCommitEntry)){priority=Priority.First},
            finalizer:new HarmonyMethod(typeof(RetainedNativeSuspension),nameof(ChunkCommitExit)));
        installed.Add(commitChunks);
        foreach(MethodBase recovery in RetainedNativeSaveWork.Install(owner,nativeChunks))installed.Add(recovery);
        foreach(MethodBase batch in NativeOptimumSaveAdapter.Install(owner,nativeChunks))installed.Add(batch);
        foreach(MethodBase storageBoundary in InstallStorageLocation(owner,nativeChunks))installed.Add(storageBoundary);
        Enabled=true; return installed.ToArray();
    }
    internal static IReadOnlyList<MethodBase> InstallClientRendering(Harmony owner)
    {
        var added=new List<MethodBase>();
        foreach(Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            string name=assembly.GetName().Name ?? "";
            if(name!="FeralKinshipCompanions" && (!hashes.TryGetValue(name,out string? expected)||!Verified(assembly,expected)))continue;
            foreach(Type type in GetLoadableTypes(assembly).Where(t=>typeof(EntityBehavior).IsAssignableFrom(t)&&!t.ContainsGenericParameters)) {
                MethodInfo? method=AccessTools.DeclaredMethod(type,"OnRenderFrame");
                if(method==null||method.IsAbstract||method.ContainsGenericParameters||method.ReturnType!=typeof(void)||installed.Contains(method))continue;
                owner.Patch(method,new HarmonyMethod(typeof(RetainedNativeSuspension),nameof(Entry)){priority=Priority.First},
                    finalizer:new HarmonyMethod(typeof(RetainedNativeSuspension),nameof(Exit)));
                installed.Add(method);added.Add(method);
            }
        }
        return added;
    }
    internal static void StartClientRendering()
    {
        // A local client can share these statics with its integrated server.
        // Extend existing common gates once instead of reinstalling them.
        Start();
        try { InstallClientRendering(harmony!); }
        catch { Stop();throw; }
    }
    internal static void Start() { if(harmony!=null)return; harmony=new Harmony("feralkinshipcompanions.retained-native-r3"); try { Install(harmony); } catch { harmony.UnpatchAll(harmony.Id); harmony=null; Enabled=false;HealthInitializationAccountingEnabled=false;ChunkSerializationOrderingEnabled=false;RetainedNativeSaveWork.Stop();installed.Clear(); throw; } }
    internal static void Stop() { harmony?.UnpatchAll(harmony.Id); harmony=null;Enabled=false;HealthInitializationAccountingEnabled=false;ChunkSerializationOrderingEnabled=false;RetainedNativeSaveWork.Stop();installed.Clear(); }
    private sealed class HealthUpdateLease(object sync, EntityBehaviorFeralKinshipFoxSocial social) {
        internal readonly object Sync = sync;
        internal readonly EntityBehaviorFeralKinshipFoxSocial Social = social;
        internal bool Completed;
    }
    private static void HealthUpdateEntry(EntityBehaviorHealth __instance, ref HealthUpdateLease? __state) {
        Entity e = __instance.entity;
        EntityBehaviorFeralKinshipFoxSocial? social = e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>();
        if (social?.NeedsInitializedHealthAccounting != true) return;
        Gate g = gates.GetValue(e, _ => new()); Monitor.Enter(g.Sync);
        __state = new HealthUpdateLease(g.Sync, social);
        social.BeginInitializedNativeHealthUpdate(__instance.Health);
    }
    private static void HealthUpdateCompleted(EntityBehaviorHealth __instance, HealthUpdateLease? __state) {
        if (__state == null) return;
        __state.Social.EndInitializedNativeHealthUpdate(__instance.Health);
        __state.Completed = true;
    }
    private static Exception? HealthUpdateExit(Exception? __exception, HealthUpdateLease? __state) {
        if (__state == null) return __exception;
        try {
            if (__exception != null || !__state.Completed) __state.Social.InvalidateInitializedHealth();
        } finally { Monitor.Exit(__state.Sync); }
        return __exception;
    }
    private static Entity Body(object instance)=>instance is Entity e?e:instance is EntityBehavior behavior?behavior.entity
        :(Entity)(AccessTools.Field(instance.GetType(),"entity")?.GetValue(instance) ?? throw new InvalidOperationException("Missing native AI body"));
    private sealed record SoundCleanupLease(object Sync,FieldInfo Field,object Task,object? Sound);
    private static FieldInfo? chunkPackLock,chunkEntities,chunkDirtyForSaving;
    private sealed class ChunkSerializationLease(object chunk,object pack,List<object> bodies) {
        internal readonly object Chunk=chunk,Pack=pack;
        internal readonly List<object> Bodies=bodies;
        internal Exception? Failure;
    }
    [ThreadStatic] private static ChunkSerializationLease? activeChunkSerialization;
    // Native save batches clear dirty flags and buffer bytes before SetChunks.
    // Retain original references only until their exact byte arrays are written;
    // a later serialization failure must requeue earlier uncommitted originals.
    [ThreadStatic] private static Dictionary<byte[],object>? pendingChunkBytes;
    private static void RequeuePendingChunks() {
        if(pendingChunkBytes==null)return;
        try { foreach(object chunk in pendingChunkBytes.Values)chunkDirtyForSaving!.SetValue(chunk,true); }
        finally { pendingChunkBytes.Clear(); }
    }
    private static void ChunkCommitEntry(IEnumerable<Vintagestory.Common.Database.DbChunk> __0,ref byte[][]? __state) {
        // Inspected callers provide List/array. Do not enumerate an arbitrary
        // single-use provider sequence a second time or change its DB input.
        if(__0 is IList<Vintagestory.Common.Database.DbChunk> batch) {
            var bytes=new byte[batch.Count][];
            for(int i=0;i<bytes.Length;i++)bytes[i]=batch[i].Data;
            __state=bytes;
        }
    }
    private static Exception? ChunkCommitExit(Exception? __exception,byte[][]? __state) {
        if(__exception!=null || __state==null)RequeuePendingChunks();
        else if(pendingChunkBytes!=null)foreach(byte[] bytes in __state)if(bytes!=null)pendingChunkBytes.Remove(bytes);
        return __exception;
    }
    private static void RefuseChunkSerialization(object chunk,string reason) {
        var failure=new InvalidOperationException(reason);
        chunkDirtyForSaving!.SetValue(chunk,true);
        if(activeChunkSerialization is { } outer) {
            // Native FastSerializeEntities catches an entity's exception and
            // omits its bytes. Poison the OUTER chunk result as well so that a
            // swallowed nested refusal cannot produce a save missing that body.
            outer.Failure=failure;chunkDirtyForSaving.SetValue(outer.Chunk,true);
        }
        throw failure;
    }
    internal static void RefuseNativeSaveWork(string reason) {
        // A refusal raised inside an entity byte callback can be swallowed by
        // native per-body serialization. Invalidate that thread's whole chunk.
        if(activeChunkSerialization is { } outer)RefuseChunkSerialization(outer.Chunk,reason);
        throw new InvalidOperationException(reason);
    }
    private static void ChunkSerializationEntry(object __instance,ref ChunkSerializationLease? __state) {
        // Native scratch entity lists/streams are ThreadStatic. A reentrant
        // provider on this SAME thread must fail the entire
        // outer chunk rather than overwrite that scratch state or lose a body.
        if(activeChunkSerialization!=null)RefuseChunkSerialization(__instance,"Reentrant native chunk serialization is not qualified");
        object pack=chunkPackLock!.GetValue(__instance) ?? throw new InvalidOperationException("Missing native chunk monitor");
        bool inherited=Monitor.IsEntered(pack);
        while(true) {
            var bodies=new List<object>(); bool packTaken=false,leased=false;
            try {
                Monitor.Enter(pack,ref packTaken);
                // Native AddEntity/RemoveEntity use this SAME pack monitor, so
                // membership cannot change between leasing and native bytes.
                // Null/duplicate slots retain native behavior; never rewrite it.
                Entity[]? entities=chunkEntities!.GetValue(__instance) as Entity[];
                bool busy=false;
                if(entities!=null)foreach(Entity? entity in entities) {
                    if(entity==null)continue;
                    object sync=gates.GetValue(entity,_=>new()).Sync;
                    if(bodies.Contains(sync))continue;
                    if(!Monitor.TryEnter(sync)) { busy=true;break; }
                    bodies.Add(sync);
                }
                if(!busy) { __state=new ChunkSerializationLease(__instance,pack,bodies);activeChunkSerialization=__state;leased=true;return; }
                // A caller-owned recursion level cannot be released here. Abort
                // the WHOLE chunk before its native per-entity catch can omit a
                // body, rather than retrying while still owning an outer pack.
                if(inherited)RefuseChunkSerialization(__instance,"Caller-owned native chunk monitor has a busy body gate");
            } finally {
                if(!leased) {
                    for(int i=bodies.Count-1;i>=0;i--)Monitor.Exit(bodies[i]);
                    if(packTaken)Monitor.Exit(pack);
                }
            }
            // A caller may own another body's gate before entering native save.
            // We have released every prefix-owned gate and pack level here;
            // retaining that outer gate during retry could livelock against a
            // second serializer with the inverse caller-owned gate.
            foreach(var existing in gates)if(Monitor.IsEntered(existing.Value.Sync))
                RefuseChunkSerialization(__instance,"Caller-owned body gate has a busy native chunk member");
            // Actor and protected return work can now finish their native chunk
            // read. Acquire the complete current membership again on each try.
            Thread.Sleep(1);
        }
    }
    private static Exception? ChunkSerializationExit(object __instance,Exception? __exception,byte[]? __result,ChunkSerializationLease? __state) {
        try {
            if(__state?.Failure!=null)__exception=__state.Failure;
            if(__exception!=null) {
                // The native save worker clears DirtyForSaving BEFORE ToBytes.
                // Requeue the current AND earlier buffered originals on failure.
                chunkDirtyForSaving!.SetValue(__instance,true);RequeuePendingChunks();
            } else if(__result!=null) {
                try { (pendingChunkBytes??=new())[__result]=__instance; }
                catch { chunkDirtyForSaving!.SetValue(__instance,true);RequeuePendingChunks();throw; }
            }
        } finally {
            if(__state!=null) {
                activeChunkSerialization=null;
                try { for(int i=__state.Bodies.Count-1;i>=0;i--)Monitor.Exit(__state.Bodies[i]); }
                finally { Monitor.Exit(__state.Pack); }
            }
        }
        return __exception;
    }
    private static void SerializeEntry(Entity __instance,ref object? __state) {
        Gate g=gates.GetValue(__instance,_=>new());Monitor.Enter(g.Sync);__state=g.Sync;
        // Native saving and packet snapshots continue while away. This lock
        // only orders this entity's bytes against its in-memory transitions.
    }
    private static bool TargetEntry(Entity __0,ref bool __result) {
        if(__0==null || !Protected(__0))return true;
        __result=false;return false;
    }
    private static bool Interaction(Entity __instance,ref bool __result) { if(!Protected(__instance))return true;__result=false;return false; }
    private static bool BooleanEntry(object __instance,MethodBase __originalMethod,ref bool __result,ref object? __state) {
        bool run=Entry(__instance,__originalMethod,ref __state); if(!run)__result=false;return run;
    }
    private static bool Entry(object __instance,MethodBase __originalMethod,ref object? __state)
    {
        Entity e=Body(__instance); Gate g=gates.GetValue(e,_=>new()); Monitor.Enter(g.Sync); __state=g.Sync;
        bool leaving=Marker(e)?.GetString("phase")=="Leaving";
        bool leavingProtection=leaving && (__originalMethod.Name is "ReceiveDamage" or "Die" or "Revive" or "OnEntityReceiveDamage"
            or "ScheduleTamedVariantReplacement" or "SpawnTameVariant" or "TryRestoreTamedVariant" or "QueueCompanionBackpackRefresh"
            || __instance is EntityBehaviorMortallyWoundable && __originalMethod.Name=="OnGameTick");
        if(!Protected(e)&&!leavingProtection)return true;
        // A newly synced Away marker can precede the next social tick. The
        // entity gate skips that tick, so reconcile client boxes/light first.
        // Physics, provider work and sound remain blocked underneath it.
        if(e.Api?.Side==EnumAppSide.Client && __instance is Entity && __originalMethod.Name=="OnGameTick")
            e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.RefreshExpeditionSuspension();
        if(__instance is IAiTask && __originalMethod.Name=="FinishExecute") {
            // StopTasks must still clean paths/animations/tags. Its native base
            // finish sound uses World.PlaySoundAt, so mute only that field while
            // cleanup runs under the same entity monitor, restoring on failure.
            FieldInfo field=AccessTools.Field(__instance.GetType(),"finishSound") ?? throw new MissingFieldException("Native AI finish sound");
            __state=new SoundCleanupLease(g.Sync,field,__instance,field.GetValue(__instance));field.SetValue(__instance,null);return true;
        }
        if(__instance is EntityBehavior behavior && __originalMethod.Name is "CheckMultiply" or "CheckGrowth" or "PurgeIfOwnerless") {
            // Consume/re-arm ONE native chain. Do not register a second chain on
            // resume. The original method owns its chain when admitted again.
            try {
                FieldInfo f=AccessTools.Field(behavior.GetType(),"callbackId") ?? throw new MissingFieldException();
                if(f.GetValue(behavior) is long old && old!=0)e.World.UnregisterCallback(old);
                MethodInfo callback=(MethodInfo)__originalMethod;
                var action=(Action<float>)callback.CreateDelegate(typeof(Action<float>),behavior);
                f.SetValue(behavior,e.World.RegisterCallback(action,__originalMethod.Name=="PurgeIfOwnerless"?5000:3000));
            } catch(Exception ex) { Hold(e,"Native callback chain failed: "+ex.GetType().Name); }
        }
        return false;
    }
    private static Exception? Exit(Exception? __exception,object? __state) {
        if(__state is SoundCleanupLease lease) {try{lease.Field.SetValue(lease.Task,lease.Sound);}finally{Monitor.Exit(lease.Sync);}}
        else if(__state!=null)Monitor.Exit(__state);return __exception;
    }
}
