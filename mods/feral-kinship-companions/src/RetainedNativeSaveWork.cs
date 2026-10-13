#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.Common;

namespace FeralKinshipCompanions;

// Preserve the exact native unload work until its original DB calls acknowledge
// it. No entity/cargo reconstruction, byte replay, new DB call or scheduler.
internal static partial class RetainedNativeSaveWork
{
    private sealed class QueueReceipt(IList queue,object[] originals) {
        internal readonly IList Queue=queue;
        internal readonly object[] Originals=originals;
    }
    private sealed class UnloadLease(object owner,object dirtySync,object server,bool ready) {
        internal readonly object Owner=owner,DirtySync=dirtySync,Server=server;
        internal readonly bool Ready=ready;
        internal readonly List<QueueReceipt> Queues=new();
        internal readonly List<WorldChunk> Disposals=new();
        internal UnloadLease? Parent;
    }
    [ThreadStatic] private static UnloadLease? activeUnload;
    private static FieldInfo? dirtyLock,ownerServer,readyToSave;
    internal static bool Enabled { get; private set; }
    internal static IReadOnlyList<MethodBase> Install(Harmony harmony,Assembly native) {
        Type unload=native.GetType("Vintagestory.Server.ServerSystemUnloadChunks",true)!;
        dirtyLock=AccessTools.Field(unload,"dirtyChunksLock") ?? throw new MissingFieldException("Native unload queue monitor");
        ownerServer=AccessTools.Field(unload,"server") ?? throw new MissingFieldException("Native unload server");
        readyToSave=AccessTools.Field(ownerServer.FieldType,"readyToAutoSave") ?? throw new MissingFieldException("Native autosave admission");
        MethodInfo method=AccessTools.DeclaredMethod(unload,"SaveDirtyUnloadedChunks") ?? throw new MissingMethodException("Native unload save");
        harmony.Patch(method,
            prefix:new HarmonyMethod(typeof(RetainedNativeSaveWork),nameof(UnloadEntry)){priority=Priority.First},
            transpiler:new HarmonyMethod(typeof(RetainedNativeSaveWork),nameof(UnloadTranspiler)),
            finalizer:new HarmonyMethod(typeof(RetainedNativeSaveWork),nameof(UnloadExit)));
        var methods=new List<MethodBase>{method};methods.AddRange(InstallGeneration(harmony,native));
        Enabled=true;return methods;
    }
    private static void UnloadEntry(object __instance,ref UnloadLease? __state) {
        object server=ownerServer!.GetValue(__instance)!;
        __state=new UnloadLease(__instance,dirtyLock!.GetValue(__instance)!,server,(bool)readyToSave!.GetValue(server)!){Parent=activeUnload};
        activeUnload=__state;
    }
    private static void ConsumeQueue<T>(List<T> queue) {
        UnloadLease lease=activeUnload ?? throw new InvalidOperationException("Native unload work lease missing");
        var originals=new object[queue.Count];for(int i=0;i<originals.Length;i++)originals[i]=queue[i]!;
        // Executes at the original Clear site UNDER its native queue monitor.
        lease.Queues.Add(new QueueReceipt((IList)queue,originals));queue.Clear();
    }
    private static void DeferDispose(WorldChunk chunk) {
        UnloadLease lease=activeUnload ?? throw new InvalidOperationException("Native unload disposal lease missing");
        if(!lease.Disposals.Contains(chunk))lease.Disposals.Add(chunk);
    }
    private static IEnumerable<CodeInstruction> UnloadTranspiler(IEnumerable<CodeInstruction> input) {
        int clears=0,disposals=0;
        foreach(CodeInstruction instruction in input) {
            if(instruction.operand is MethodInfo called && called.Name=="Clear"
                && called.DeclaringType?.IsGenericType==true
                && called.DeclaringType.GetGenericTypeDefinition()==typeof(List<>)) {
                Type item=called.DeclaringType.GetGenericArguments()[0];
                if(item.FullName is "Vintagestory.Server.ServerChunkWithCoord" or "Vintagestory.Server.ServerMapChunkWithCoord") {
                    instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.DeclaredMethod(typeof(RetainedNativeSaveWork),nameof(ConsumeQueue)).MakeGenericMethod(item);clears++;
                }
            } else if(instruction.operand is MethodInfo dispose && dispose.DeclaringType==typeof(WorldChunk)&&dispose.Name=="Dispose") {
                instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.DeclaredMethod(typeof(RetainedNativeSaveWork),nameof(DeferDispose));disposals++;
            }
            yield return instruction;
        }
        if(clears!=2||disposals!=1)throw new InvalidOperationException("Pinned native unload save shape differs");
    }
    private static Exception? UnloadExit(Exception? __exception,UnloadLease? __state) {
        if(__state==null)return __exception;
        activeUnload=__state.Parent;
        if(__exception!=null) {
            // Restore original value records BEFORE newer appended work, without
            // replacing a queue or duplicating an already queued original record.
            lock(__state.DirtySync)foreach(QueueReceipt receipt in __state.Queues) {
                int at=0;foreach(object original in receipt.Originals)if(!receipt.Queue.Contains(original))receipt.Queue.Insert(at++,original);
            }
            readyToSave!.SetValue(__state.Server,__state.Ready);
            return __exception;
        }
        // Both original SetChunks and SetMapChunks returned successfully. Native
        // Dispose now runs exactly once on each original, after acknowledgement.
        Exception? cleanupError=null;
        foreach(WorldChunk original in __state.Disposals)try { original.Dispose(); } catch(Exception error) { cleanupError??=error; }
        if(cleanupError!=null)readyToSave!.SetValue(__state.Server,__state.Ready);
        return cleanupError;
    }
    internal static void Stop() { Enabled=false; }
}
