#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;

public partial class FeralKinshipCompanionSystem
{
    private sealed record OriginalLoadKey(string Owner,string CompanionId,long NativeId,string Trip,int X,int Z);
    private sealed class OriginalLoadAttempt { internal int Requests;internal long RetryAfter; }
    private sealed class OriginalColumnLoad(long started) { internal readonly long Started=started;internal int Completed;internal bool Reported; }
    private readonly Dictionary<OriginalLoadKey,OriginalLoadAttempt> retainedOriginalLoads=new();
    private readonly Dictionary<(int X,int Z),OriginalColumnLoad> retainedOriginalColumns=new();
    private readonly HashSet<string> retainedOriginalLocationWarnings=new(StringComparer.Ordinal);
    private long retainedOriginalLoadWindow;
    private int retainedOriginalLoadsInWindow;
    private bool retainedSourceCheckpointPending;
    private int retainedSourceCheckpointAttempts;
    private long retainedSourceCheckpointRetryAt;
    private const int MaximumOriginalColumnsInFlight=4;
    private const long OriginalLoadWaitMs=15000,OriginalLoadCooldownMs=60000;

    private void ResetRetainedOriginalLoading() {
        retainedOriginalLoads.Clear();retainedOriginalColumns.Clear();retainedOriginalLocationWarnings.Clear();retainedOriginalLoadWindow=0;retainedOriginalLoadsInWindow=0;
        returnDestinationRequests.Clear();
        retainedSourceCheckpointPending=false;retainedSourceCheckpointAttempts=0;retainedSourceCheckpointRetryAt=0;
    }
    private OriginalLoadKey? OriginalLoadIdentity(FoxPackRecordV2 r) {
        if(r.EntityId<=0 || string.IsNullOrEmpty(r.OwnerUid) || string.IsNullOrEmpty(r.FoxId)
            || !string.IsNullOrEmpty(r.RetainedTrip) && r.RetainedPhase is not ("Leaving" or "Suspended" or "Returning" or "Resuming") || r.Archived || r.MissingContentArchive?.Active==true
            || r.PendingReturnStatus is not ("Returned healthy" or "Returned injured" or "Mortally wounded")
            || !string.IsNullOrEmpty(r.PendingDepartureStatus) || !r.HasLastKnownPosition || r.LastKnownDimension!=0)return null;
        return new(r.OwnerUid,r.FoxId,r.EntityId,r.RetainedTrip,
            (int)Math.Floor(r.LastKnownX/(double)GlobalConstants.ChunkSize),(int)Math.Floor(r.LastKnownZ/(double)GlobalConstants.ChunkSize));
    }
    private void MaintainRetainedOriginalLoading(long now) {
        var pending=packRepository?.GetPendingReturnRecords() ?? Array.Empty<FoxPackRecordV2>();
        var valid=pending.Select(OriginalLoadIdentity).Where(k=>k!=null).ToHashSet();
        var warningKeys=pending.Select(r=>r.OwnerUid+":"+r.FoxId+":"+r.RetainedTrip).ToHashSet(StringComparer.Ordinal);
        retainedOriginalLocationWarnings.RemoveWhere(key=>!warningKeys.Contains(key));
        foreach(var key in retainedOriginalLoads.Keys.ToArray())
            if(valid?.Contains(key)!=true || serverApi?.World.GetEntityById(key.NativeId)!=null)retainedOriginalLoads.Remove(key);
        foreach(var column in retainedOriginalColumns.Keys.ToArray()) {
            var load=retainedOriginalColumns[column];
            // Observe native publication even if OnLoaded was lost. Never queue
            // a second native load while the first is still unacknowledged.
            if(serverApi?.WorldManager.GetChunk(column.X,0,column.Z)!=null)Volatile.Write(ref load.Completed,1);
            if(Volatile.Read(ref load.Completed)!=0 && now>=load.Started+OriginalLoadCooldownMs)retainedOriginalColumns.Remove(column);
        }
        long window=now/1000;if(window!=retainedOriginalLoadWindow){retainedOriginalLoadWindow=window;retainedOriginalLoadsInWindow=0;}
    }
    private void RequestRetainedOriginalColumn(FoxPackRecordV2 r,string owner,string id,string label,long now) {
        if(serverApi==null || packRepository?.Loaded!=true || Environment.CurrentManagedThreadId!=RuntimeEnv.ServerMainThreadId
            || r.OwnerUid!=owner || r.FoxId!=id || r.PendingReturnStatus!=label || r.Archived)return;
        MaintainRetainedOriginalLoading(now);
        OriginalLoadKey? key=OriginalLoadIdentity(r);
        if(key==null) {
            if(retainedOriginalLocationWarnings.Add(r.OwnerUid+":"+r.FoxId+":"+r.RetainedTrip))ReportOriginalLoadWait(r,"missing or unsupported native source location/return authority");return;
        }
        if(!retainedOriginalLoads.TryGetValue(key,out var attempt))retainedOriginalLoads[key]=attempt=new();
        if(now<attempt.RetryAfter)return;
        var column=(key.X,key.Z);
        if(retainedOriginalColumns.TryGetValue(column,out var existing)) {
            if(Volatile.Read(ref existing.Completed)==0 && now>=existing.Started+OriginalLoadWaitMs && !existing.Reported) {
                existing.Reported=true;ReportOriginalLoadWait(r,"source-column load has not acknowledged within 15 seconds; retained pending without adding another native request");
            }
            return;
        }
        if(retainedOriginalColumns.Values.Count(c=>Volatile.Read(ref c.Completed)==0)>=MaximumOriginalColumnsInFlight
            || retainedOriginalLoadsInWindow>=MaximumOriginalColumnsInFlight)return;
        // Admit before the engine call. No callback owns reconciliation or a
        // return outcome, and no forced-loaded ownership needs to be released.
        attempt.Requests++;attempt.RetryAfter=now+OriginalLoadCooldownMs;
        var load=new OriginalColumnLoad(now);retainedOriginalColumns[column]=load;retainedOriginalLoadsInWindow++;
        // The callback may run inline or on an engine thread. It publishes only
        // a flag on this session's request object. Timeout/cancellation cannot
        // free an unacknowledged native request slot and flood the engine queue.
        try {serverApi.WorldManager.LoadChunkColumnPriority(key.X,key.Z,new ChunkLoadOptions{KeepLoaded=false,OnLoaded=()=>Volatile.Write(ref load.Completed,1)});}
        catch(Exception ex) {
            // The call failed rather than accepting a live request. Do not let
            // the failed submission permanently consume one of the four slots.
            retainedOriginalColumns.Remove(column);
            ReportOriginalLoadWait(r,"source-column request failed: "+ex.GetType().Name);
        }
    }
    private readonly Dictionary<(int X,int Z),long> returnDestinationRequests=new();
    private void RequestReturnDestinationColumn(Vintagestory.API.MathTools.BlockPos pos)
    {
        if(serverApi==null || pos.dimension!=0)return;
        long now=UtcNowMs();MaintainRetainedOriginalLoading(now);
        var column=((int)Math.Floor(pos.X/(double)GlobalConstants.ChunkSize),(int)Math.Floor(pos.Z/(double)GlobalConstants.ChunkSize));
        if(retainedOriginalColumns.ContainsKey(column) || returnDestinationRequests.TryGetValue(column,out long next)&&now<next
            || retainedOriginalColumns.Values.Count(c=>Volatile.Read(ref c.Completed)==0)>=MaximumOriginalColumnsInFlight
            || retainedOriginalLoadsInWindow>=MaximumOriginalColumnsInFlight)return;
        returnDestinationRequests[column]=now+OriginalLoadCooldownMs;
        foreach(var key in returnDestinationRequests.Where(x=>x.Value<now-OriginalLoadCooldownMs).Select(x=>x.Key).ToArray())returnDestinationRequests.Remove(key);
        var load=new OriginalColumnLoad(now);retainedOriginalColumns[column]=load;retainedOriginalLoadsInWindow++;
        try {serverApi.WorldManager.LoadChunkColumnPriority(column.Item1,column.Item2,new ChunkLoadOptions{KeepLoaded=false,OnLoaded=()=>Volatile.Write(ref load.Completed,1)});}
        catch(Exception ex) {retainedOriginalColumns.Remove(column);serverApi.Logger.Warning("[FeralKinshipCompanions] Return destination load failed: {0}; retry after cooldown.",ex.GetType().Name);}
    }
    private void ReportOriginalLoadWait(FoxPackRecordV2 r,string reason) {
        serverApi?.Logger.Warning("[FeralKinshipCompanions] Retained return for {0} (native {1}) remains pending: {2}. Original/cargo preserved; no replacement.",r.FoxId,r.EntityId,reason);
    }
    private bool RefreshRetainedSourceLocation(Entity e,bool unloading=false) {
        if(serverApi==null || packRepository?.Loaded!=true || Environment.CurrentManagedThreadId!=RuntimeEnv.ServerMainThreadId)return false;
        string id=GetDomesticationStatus(e)?.GetString(FoxIdKey,string.Empty) ?? string.Empty;
        if(!packRepository.TryGetRecord(id,out var r) || r==null || !RetainedNativeSuspension.Matches(e,r)
            || r.RetainedPhase is not ("Suspended" or "Returning")
            || RetainedNativeSuspension.Marker(e)?.GetString("phase")!=r.RetainedPhase)return false;
        if(unloading) {
            var location=RetainedNativeSuspension.TakeNativeStorageLocation(e);
            if(location==null || location.NativeId!=r.EntityId || location.Owner!=r.OwnerUid || location.CompanionId!=r.FoxId || location.Trip!=r.RetainedTrip)return false;
            bool changed=!r.HasLastKnownPosition || r.LastKnownX!=location.X || r.LastKnownY!=location.Y || r.LastKnownZ!=location.Z || r.LastKnownDimension!=location.Dimension;
            r.LastKnownX=location.X;r.LastKnownY=location.Y;r.LastKnownZ=location.Z;r.LastKnownDimension=location.Dimension;r.HasLastKnownPosition=true;return changed;
        }
        // Main-thread save/registration refresh only accepts the canonical body
        // in its current published chunk. In-flight teleport coordinates do not
        // replace an actual storage hint before native membership moves.
        if(e.Teleporting || !RetainedCanonicalAdmission(e))return false;
        var p=e.Pos.AsBlockPos;
        long index=serverApi.WorldManager.ChunkIndex3D((int)Math.Floor(p.X/(double)GlobalConstants.ChunkSize),
            (int)Math.Floor(p.InternalY/(double)GlobalConstants.ChunkSize),(int)Math.Floor(p.Z/(double)GlobalConstants.ChunkSize));
        if(index!=e.InChunkIndex3d)return false;
        r.LastKnownX=p.X;r.LastKnownY=p.Y;r.LastKnownZ=p.Z;r.LastKnownDimension=p.dimension;r.HasLastKnownPosition=true;
        return true;
    }
    private void RefreshRetainedSourceLocations() {
        foreach(Entity e in loadedFoxes.Values.ToArray())RefreshRetainedSourceLocation(e);
    }
    private void CheckpointRetainedSourceHint(Entity e) {
        if(Environment.CurrentManagedThreadId!=RuntimeEnv.ServerMainThreadId)return;
        try {
            if(RefreshRetainedSourceLocation(e,unloading:true)) {
                retainedSourceCheckpointPending=true;retainedSourceCheckpointAttempts=0;retainedSourceCheckpointRetryAt=0;
            }
            TryCheckpointRetainedSourceHints(UtcNowMs());
        } catch(Exception ex) {
            try {serverApi?.Logger.Warning("[FeralKinshipCompanions] Native source hint update failed for {0}: {1}; original retained.",e.EntityId,ex.GetType().Name);} catch { }
        }
    }
    private void TryCheckpointRetainedSourceHints(long now) {
        if(!retainedSourceCheckpointPending || packRepository?.Loaded!=true || retainedSourceCheckpointAttempts>=3 || now<retainedSourceCheckpointRetryAt
            || Environment.CurrentManagedThreadId!=RuntimeEnv.ServerMainThreadId || !RetainedNativeSuspension.CanCheckpointStorageHint())return;
        retainedSourceCheckpointAttempts++;retainedSourceCheckpointRetryAt=now+1000;
        try {
            // Native normal retirement is main-thread and has no body/pack or
            // dirty-list monitor here. StoreData checkpoints engine memory; the
            // engine's ordinary world save still owns physical durability.
            packRepository.Save();retainedSourceCheckpointPending=false;
        } catch(Exception ex) {
            try {serverApi?.Logger.Warning("[FeralKinshipCompanions] Native source hint checkpoint pending after {0}; attempt {1}/3. Original retained; next ordinary world save also retries.",ex.GetType().Name,retainedSourceCheckpointAttempts);} catch { }
        }
    }
}
