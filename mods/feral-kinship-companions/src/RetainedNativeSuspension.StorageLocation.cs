#nullable enable
using System;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.Common.Database;
using Vintagestory.Server;

namespace FeralKinshipCompanions;

internal static partial class RetainedNativeSuspension
{
    // One immutable receipt per native object. No world, ledger, callback, or
    // save operation occurs at this boundary; no worker-to-main wait is added.
    internal sealed record NativeStorageLocation(long NativeId,string Owner,string CompanionId,string Trip,
        int X,int Y,int Z,int Dimension);
    private static MethodBase[] InstallStorageLocation(Harmony owner,Assembly native) {
        MethodInfo unload=AccessTools.DeclaredMethod(native.GetType("Vintagestory.Server.ServerSystemUnloadChunks",true)!,"TryUnloadChunk")
            ?? throw new MissingMethodException("Pinned native unload location boundary");
        owner.Patch(unload,prefix:new HarmonyMethod(typeof(RetainedNativeSuspension),nameof(CaptureNativeUnloadLocation)){priority=Priority.First});
        MethodInfo columns=AccessTools.DeclaredMethod(unload.DeclaringType,"UnloadChunkColumns")
            ?? throw new MissingMethodException("Pinned native main-thread column retirement boundary");
        owner.Patch(columns,prefix:new HarmonyMethod(typeof(RetainedNativeSuspension),nameof(RequireMainThreadColumnRetirement)){priority=Priority.First});
        return new MethodBase[]{unload,columns};
    }
    private static void RequireMainThreadColumnRetirement() {
        if(Environment.CurrentManagedThreadId!=RuntimeEnv.ServerMainThreadId)
            RefuseNativeSaveWork("Native loaded-column retirement requires its main-thread boundary");
    }
    private static void CaptureNativeUnloadLocation(ChunkPos __1,ServerChunk __2) {
        // Pinned OnServerTick/UnloadChunkColumns calls this before native queue,
        // index removal and despawn, and before the later dirty worker receives
        // this chunk. Clean chunks take the same boundary without serialization.
        Entity[]? bodies=__2.Entities;
        if(bodies==null)return;
        foreach(Entity? e in bodies) {
            if(e==null || Marker(e)?.GetString("phase") is not ("Suspended" or "Returning"))continue;
            if(Environment.CurrentManagedThreadId!=RuntimeEnv.ServerMainThreadId)
                RefuseNativeSaveWork("Retained original unload requires the native main-thread boundary");
            try {
                Gate g=gates.GetValue(e,_=>new());Volatile.Write(ref g.StorageLocation,null);
                var m=Marker(e);
                if(m?.GetString("phase") is not ("Suspended" or "Returning") || e.Pos.Dimension!=0)continue;
                // The native ChunkPos, not a departure or teleport coordinate,
                // identifies the column that will store this exact original.
                int x=checked(__1.X*GlobalConstants.ChunkSize),y=checked(__1.Y*GlobalConstants.ChunkSize),z=checked(__1.Z*GlobalConstants.ChunkSize);
                // Identity/trip publication and retirement both belong to this
                // main thread. Worker serializers only read them under existing
                // gates. Publish a new immutable hint without acquiring any
                // body/pack monitor or touching watched attributes.
                Volatile.Write(ref g.StorageLocation,new(e.EntityId,m.GetString("owner"),m.GetString("companionId"),m.GetString("trip"),x,y,z,0));
            } catch(Exception) {
                // UnloadChunkColumns has not yet published earlier retired chunks
                // from its local list. A hint failure must never abort that list.
            }
        }
    }
    internal static NativeStorageLocation? TakeNativeStorageLocation(Entity e) {
        Gate g=gates.GetValue(e,_=>new());return Interlocked.Exchange(ref g.StorageLocation,null);
    }
    internal static bool CanCheckpointStorageHint() {
        if(activeChunkSerialization!=null)return false;
        foreach(var gate in gates)if(Monitor.IsEntered(gate.Value.Sync))return false;
        return true;
    }
}
