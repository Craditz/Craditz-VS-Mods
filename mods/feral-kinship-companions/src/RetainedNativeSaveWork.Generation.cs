#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;

namespace FeralKinshipCompanions;

internal static partial class RetainedNativeSaveWork
{
    private sealed class GenerationReceipt(object request,Array? array,object? map,object?[] version) {
        internal readonly object Request=request;
        internal readonly Array? OriginalArray=array,Chunks=array==null?null:(Array)array.Clone();
        internal readonly object? Map=map;
        internal readonly object?[] Version=version;
        internal bool RemoveRequested;
        internal bool DispatchClaimHeld;
        internal readonly List<(IDictionary Dictionary,long Key,bool WasPresent,object? Expected)> Dictionaries=new();
    }
    private sealed class GenerationLease(object owner,object queue,object sync) {
        internal readonly object Owner=owner,Queue=queue,Sync=sync;
        internal readonly Dictionary<object,GenerationReceipt> Requests=new();
        internal OperationToken? Operation;
        internal GenerationLease? Parent;
        internal readonly List<IDisposable> Footprints=new();
    }
    private sealed class OperationState { internal OperationToken? Active; }
    private sealed class OperationToken(object sync,OperationState state) { internal readonly object Sync=sync;internal readonly OperationState State=state; }
    [ThreadStatic] private static GenerationLease? activeGeneration;
    private static readonly ConditionalWeakTable<object,object> requestAdmission=new();
    private static readonly ConditionalWeakTable<object,OperationState> requestOperations=new();
    private static FieldInfo? supplyThread;
    private static FieldInfo? ownerThread,threadQueue,queueIndex,requestChunks,requestMap,chunkDirty,mapDirty;
    private static FieldInfo[] requestVersion=Array.Empty<FieldInfo>();
    private static MethodInfo? removeRequest;
    private static Action<object>? acquireGeneration,releaseGeneration;
    private static int generationRequestLocal=-1;
    private static MethodInfo? footprintDispose;
    private static Func<object,bool>? claimDispatch;
    private static Action<object>? releaseDispatch;
    private static IReadOnlyList<MethodBase> InstallGeneration(Harmony harmony,Assembly native) {
        Type unload=native.GetType("Vintagestory.Server.ServerSystemUnloadChunks",true)!,thread=native.GetType("Vintagestory.Server.ChunkServerThread",true)!,request=native.GetType("Vintagestory.Server.ChunkColumnLoadRequest",true)!;
        ownerThread=AccessTools.Field(unload,"chunkthread");threadQueue=AccessTools.Field(thread,"requestedChunkColumns");Type queue=threadQueue!.FieldType;
        queueIndex=AccessTools.Field(queue,"elementsByIndex");requestChunks=AccessTools.Field(request,"Chunks");requestMap=AccessTools.Field(request,"MapChunk");
        requestVersion=new[]{"untilPass","creationTime","blockingRequest","disposeOrRequeueFlags"}.Select(name=>AccessTools.Field(request,name)??throw new MissingFieldException(name)).ToArray();
        chunkDirty=AccessTools.Field(requestChunks!.FieldType.GetElementType()!,"DirtyForSaving");mapDirty=AccessTools.Field(requestMap!.FieldType,"DirtyForSaving");
        removeRequest=AccessTools.DeclaredMethod(queue,"Remove",new[]{typeof(long)});
        FieldInfo generating=AccessTools.Field(request,"generatingLock");
        acquireGeneration=LockAction(request,generating,"AcquireReadLock");releaseGeneration=LockAction(request,generating,"ReleaseReadLock");
        var methods=new List<MethodBase>();
        MethodInfo generation=AccessTools.DeclaredMethod(unload,"UnloadGeneratingChunkColumns");
        if(!NativeOptimumCompatibility.TryRequestLocal(generation,out generationRequestLocal,out string reason))throw new InvalidOperationException(reason);
        footprintDispose=NativeChunkCompatibility.UsesOptimum(native)
            ? native.GetType("Vintagestory.Server.ChunkServerThread",true)!.GetMethod("TryAcquireOptimumWorldgenFootprint",BindingFlags.NonPublic|BindingFlags.Instance)!.GetParameters().Last().ParameterType.GetElementType()!.GetMethod(nameof(IDisposable.Dispose)) : null;
        claimDispatch=null;releaseDispatch=null;
        if(footprintDispose!=null) {
            MethodInfo claim=AccessTools.DeclaredMethod(request,"TryClaimDispatch"),release=AccessTools.DeclaredMethod(request,"ReleaseDispatch");
            claimDispatch=value=>(bool)claim.Invoke(value,null)!;releaseDispatch=value=>release.Invoke(value,null);
        }
        harmony.Patch(generation,
            prefix:new HarmonyMethod(typeof(RetainedNativeSaveWork),nameof(GenerationEntry)){priority=Priority.First},
            transpiler:new HarmonyMethod(typeof(RetainedNativeSaveWork),nameof(GenerationTranspiler)),
            finalizer:new HarmonyMethod(typeof(RetainedNativeSaveWork),nameof(GenerationExit)));
        methods.Add(generation);
        // Native GetOrAdd/merge mutates the original request without its
        // generating lock. Coordinate only that short admission/removal work;
        // never retain this monitor during serialization, DB calls or disposal.
        MethodInfo merge=thread.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
            .Single(m=>m.Name=="addChunkColumnRequest"&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType==request);
        harmony.Patch(merge,prefix:new HarmonyMethod(typeof(RetainedNativeSaveWork),nameof(RequestMergeEntry)),finalizer:new HarmonyMethod(typeof(RetainedNativeSaveWork),nameof(RequestAdmissionExit)));methods.Add(merge);
        // This second pinned admission route mutates GetOrAdd's original
        // untilPass/creationTime directly, bypassing addChunkColumnRequest.
        MethodInfo minimum=AccessTools.DeclaredMethod(thread,"EnsureMinimumWorldgenPassAt")??throw new MissingMethodException("Native minimum worldgen admission");
        harmony.Patch(minimum,prefix:new HarmonyMethod(typeof(RetainedNativeSaveWork),nameof(RequestMergeEntry)),finalizer:new HarmonyMethod(typeof(RetainedNativeSaveWork),nameof(RequestAdmissionExit)));methods.Add(minimum);
        Type supply=native.GetType("Vintagestory.Server.ServerSystemSupplyChunks",true)!;supplyThread=AccessTools.Field(supply,"chunkthread");
        MethodInfo clear=AccessTools.DeclaredMethod(supply,"FullyClearGeneratingQueue");
        harmony.Patch(clear,prefix:new HarmonyMethod(typeof(RetainedNativeSaveWork),nameof(ClearEntry)){priority=Priority.First},finalizer:new HarmonyMethod(typeof(RetainedNativeSaveWork),nameof(ClearExit)));methods.Add(clear);
        foreach(MethodInfo mutation in queue.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)
            .Where(m=>m.Name is "Enqueue" or "EnqueueWithoutAddingToIndex" or "Dequeue" or "DequeueWithoutRemovingFromIndex" or "Requeue" or "Remove" or "Clear")) {
            harmony.Patch(mutation,prefix:new HarmonyMethod(typeof(RetainedNativeSaveWork),nameof(RequestAdmissionEntry)),finalizer:new HarmonyMethod(typeof(RetainedNativeSaveWork),nameof(RequestAdmissionExit)));methods.Add(mutation);
        }
        return methods;
    }
    private static Action<object> LockAction(Type request,FieldInfo field,string method) {
        var dynamic=new DynamicMethod("NativeRequest"+method,typeof(void),new[]{typeof(object)},typeof(RetainedNativeSaveWork).Module,true);
        ILGenerator il=dynamic.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Castclass,request);il.Emit(OpCodes.Ldflda,field);il.Emit(OpCodes.Call,AccessTools.DeclaredMethod(field.FieldType,method));il.Emit(OpCodes.Ret);
        return (Action<object>)dynamic.CreateDelegate(typeof(Action<object>));
    }
    private static void RequestMergeEntry(object __instance,ref object? __state) => RequestAdmissionEntry(threadQueue!.GetValue(__instance)!,ref __state);
    private static void RequestAdmissionEntry(object __instance,ref object? __state) {object sync=requestAdmission.GetValue(__instance,_=>new object());Monitor.Enter(sync);__state=sync;}
    private static Exception? RequestAdmissionExit(Exception? __exception,object? __state) {if(__state!=null)Monitor.Exit(__state);return __exception;}
    private static OperationToken BeginOperation(object queue) {
        object sync=requestAdmission.GetValue(queue,_=>new object());OperationState state=requestOperations.GetValue(queue,_=>new());
        lock(sync) {
            if(state.Active!=null)RetainedNativeSuspension.RefuseNativeSaveWork("Concurrent or nested native generation retirement is not admitted");
            return state.Active=new OperationToken(sync,state);
        }
    }
    private static void EndOperation(OperationToken token) {lock(token.Sync)if(ReferenceEquals(token.State.Active,token))token.State.Active=null;}
    private static void ClearEntry(object __instance,ref OperationToken? __state) => __state=BeginOperation(threadQueue!.GetValue(supplyThread!.GetValue(__instance)!)!);
    private static Exception? ClearExit(Exception? __exception,OperationToken? __state) {if(__state!=null)EndOperation(__state);return __exception;}
    private static void GenerationEntry(object __instance,ref GenerationLease? __state) {
        object queue=threadQueue!.GetValue(ownerThread!.GetValue(__instance)!)!;
        OperationToken operation=BeginOperation(queue);
        __state=new GenerationLease(__instance,queue,operation.Sync){Parent=activeGeneration,Operation=operation};activeGeneration=__state;
    }
    private static GenerationReceipt CaptureRequest(object request) {
        GenerationLease lease=activeGeneration??throw new InvalidOperationException("Native generation work lease missing");
        lock(lease.Sync) {
            if(lease.Requests.TryGetValue(request,out GenerationReceipt? receipt))return receipt;
            receipt=new GenerationReceipt(request,requestChunks!.GetValue(request) as Array,requestMap!.GetValue(request),requestVersion.Select(field=>Version(field,request)).ToArray());
            lease.Requests.Add(request,receipt);return receipt;
        }
    }
    private static object? Version(FieldInfo field,object request) => field.Name=="disposeOrRequeueFlags"?((int)field.GetValue(request)!&1):field.GetValue(request);
    private static T CapturedChunks<T>(object request) => (T)(object?)CaptureRequest(request).Chunks!;
    private static T CapturedMap<T>(object request) => (T)CaptureRequest(request).Map!;
    private static bool DeferRequestRemove<T>(T queue,long key,object request) {
        GenerationReceipt receipt=CaptureRequest(request);
        if(claimDispatch!=null&&!receipt.DispatchClaimHeld)receipt.DispatchClaimHeld=claimDispatch(request);
        // An already dispatched worker may be paused before footprint admission.
        // Keep its request/dictionaries alive rather than retiring underneath it.
        receipt.RemoveRequested=claimDispatch==null||receipt.DispatchClaimHeld;return true;
    }
    private static void DeferDictionaryRemove<T>(T dictionary,long key,object request) {
        var dict=(IDictionary)(object)dictionary!;GenerationReceipt receipt=CaptureRequest(request);
        bool present=dict.Contains(key);object? expected=present?dict[key]:null;
        // Map removal belongs to the original captured map, even when a newer
        // same-index map has already replaced the dictionary value.
        if(present&&typeof(T).GetGenericArguments()[1]==requestMap!.FieldType)expected=receipt.Map;
        receipt.Dictionaries.Add((dict,key,present,expected));
    }
    private static IEnumerable<CodeInstruction> GenerationTranspiler(IEnumerable<CodeInstruction> input) {
        int chunks=0,maps=0,removals=0,footprints=0;
        foreach(CodeInstruction instruction in input) {
            if(instruction.opcode==OpCodes.Ldfld&&Equals(instruction.operand,requestChunks)) {instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.DeclaredMethod(typeof(RetainedNativeSaveWork),nameof(CapturedChunks)).MakeGenericMethod(requestChunks!.FieldType);chunks++;}
            else if(instruction.opcode==OpCodes.Ldfld&&Equals(instruction.operand,requestMap)) {instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.DeclaredMethod(typeof(RetainedNativeSaveWork),nameof(CapturedMap)).MakeGenericMethod(requestMap!.FieldType);maps++;}
            else if(instruction.operand is MethodInfo method && (Equals(method,removeRequest)
                || method.Name=="Remove"&&method.DeclaringType?.FullName=="Vintagestory.API.Util.DictExtensions")) {
                // The qualified removal keys select one typed, dominating request
                // local. Never infer an Optimum slot from its version/local count.
                if(generationRequestLocal<0)throw new InvalidOperationException("Qualified generation request local missing");
                var loadRequest=generationRequestLocal<=byte.MaxValue?new CodeInstruction(OpCodes.Ldloc_S,(byte)generationRequestLocal):new CodeInstruction(OpCodes.Ldloc,(short)generationRequestLocal);loadRequest.labels.AddRange(instruction.labels);instruction.labels.Clear();loadRequest.blocks.AddRange(instruction.blocks);instruction.blocks.Clear();yield return loadRequest;
                Type target=Equals(method,removeRequest)?method.DeclaringType!:method.GetParameters()[0].ParameterType;
                instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.DeclaredMethod(typeof(RetainedNativeSaveWork),Equals(method,removeRequest)?nameof(DeferRequestRemove):nameof(DeferDictionaryRemove)).MakeGenericMethod(target);removals++;
            }
            if(footprintDispose!=null&&Equals(instruction.operand,footprintDispose)) {
                instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.DeclaredMethod(typeof(RetainedNativeSaveWork),nameof(DeferFootprintDispose));footprints++;
            }
            yield return instruction;
        }
        if(footprints!=(footprintDispose==null?0:2))throw new InvalidOperationException("Qualified footprint disposal sites differ");
        if(chunks!=7||maps!=1||removals!=3)throw new InvalidOperationException("Pinned native generation unload shape differs: "+chunks+"/"+maps+"/"+removals);
    }
    private static void DeferFootprintDispose(object footprint) {
        GenerationLease lease=activeGeneration??throw new InvalidOperationException("Footprint disposal outside qualified generation operation");
        if(footprint is not IDisposable disposable||footprintDispose?.DeclaringType!=footprint.GetType())throw new InvalidOperationException("Unexpected footprint lease type");
        if(!lease.Footprints.Any(p=>ReferenceEquals(p,disposable)))lease.Footprints.Add(disposable);
    }
    private static void DirtyGeneration(GenerationReceipt receipt) {
        if(receipt.Chunks!=null)foreach(object? chunk in receipt.Chunks)if(chunk!=null)chunkDirty!.SetValue(chunk,true);
        if(receipt.Map!=null)mapDirty!.SetValue(receipt.Map,true);
    }
    private static bool Unchanged(GenerationReceipt receipt) {
        if(!ReferenceEquals(requestChunks!.GetValue(receipt.Request),receipt.OriginalArray)||!ReferenceEquals(requestMap!.GetValue(receipt.Request),receipt.Map))return false;
        for(int i=0;i<requestVersion.Length;i++)if(!Equals(Version(requestVersion[i],receipt.Request),receipt.Version[i]))return false;
        if(receipt.OriginalArray!=null&&receipt.Chunks!=null)for(int i=0;i<receipt.Chunks.Length;i++)if(!ReferenceEquals(receipt.OriginalArray.GetValue(i),receipt.Chunks.GetValue(i)))return false;
        // Native generation may resume after its serialization read lock. Do
        // not retire newly dirty work that was produced during the DB calls.
        if(receipt.Chunks!=null)foreach(object? chunk in receipt.Chunks)if(chunk!=null&&(bool)chunkDirty!.GetValue(chunk)!)return false;
        if(receipt.Map!=null&&(bool)mapDirty!.GetValue(receipt.Map)!)return false;
        foreach(var move in receipt.Dictionaries)if(move.Dictionary.Contains(move.Key)!=move.WasPresent||move.WasPresent&&!Equals(move.Dictionary[move.Key],move.Expected))return false;
        return true;
    }
    private static Exception? GenerationExit(Exception? __exception,GenerationLease? __state) {
        if(__state==null)return __exception;activeGeneration=__state.Parent;
        Exception? result=__exception;
        try {result=FinishGeneration(__exception,__state);}
        catch(Exception error) {result??=error;}
        finally {
            // Native finally has requested disposal, but the footprint remains
            // held through DB acknowledgement and our deferred identity checks.
            try {
                foreach(GenerationReceipt receipt in __state.Requests.Values.Where(r=>r.DispatchClaimHeld))try {
                    var index=(IDictionary)queueIndex!.GetValue(__state.Queue)!;long key=(long)AccessTools.Field(receipt.Request.GetType(),"mapIndex2d").GetValue(receipt.Request)!;
                    // A retired/superseded request keeps its one-bit claim as a
                    // tombstone until GC. Releasing it would let a worker whose
                    // disposed check preceded retirement claim a stale object.
                    if(((int)requestVersion.Last().GetValue(receipt.Request)!&1)==0&&index.Contains(key)&&ReferenceEquals(index[key],receipt.Request))releaseDispatch!(receipt.Request);
                }catch(Exception error){result??=error;}
                foreach(IDisposable footprint in __state.Footprints)try {footprint.Dispose();}catch(Exception error) {result??=error;}
            }
            finally {EndOperation(__state.Operation!);}
        }
        return result;
    }
    private static Exception? FinishGeneration(Exception? __exception,GenerationLease __state) {
        if(__exception!=null) {foreach(GenerationReceipt receipt in __state.Requests.Values)DirtyGeneration(receipt);return __exception;}
        Exception? cleanupError=null;
        foreach(GenerationReceipt receipt in __state.Requests.Values.Where(r=>r.RemoveRequested)) {
            // Native order: generating read lock BEFORE the short admission
            // monitor. The monitor is never held while waiting for that lock.
            bool acquired=false;
            try {acquireGeneration!(receipt.Request);acquired=true;lock(__state.Sync) {
                var index=(IDictionary)queueIndex!.GetValue(__state.Queue)!;long key=(long)AccessTools.Field(receipt.Request.GetType(),"mapIndex2d").GetValue(receipt.Request)!;
                if(!index.Contains(key)||!ReferenceEquals(index[key],receipt.Request)||!Unchanged(receipt)) {DirtyGeneration(receipt);continue;}
                // Exact original Remove performs the native FlagToDispose. A
                // changed/merged/newer authority is retained, never overwritten.
                if(!(bool)removeRequest!.Invoke(__state.Queue,new object[]{key})!) {DirtyGeneration(receipt);continue;}
                foreach(var move in receipt.Dictionaries) {
                    if(!move.WasPresent)continue; // Preserve the native absent-key no-op.
                    Type value=move.Dictionary.GetType().GetGenericArguments()[1];
                    AccessTools.DeclaredMethod(typeof(RetainedNativeSaveWork),nameof(RemoveExpectedDictionaryValue)).MakeGenericMethod(value).Invoke(null,new object?[]{move.Dictionary,move.Key,move.Expected});
                }
            }}catch(Exception error) {cleanupError??=error is TargetInvocationException invocation?invocation.InnerException??invocation:error;}
            finally{if(acquired)releaseGeneration!(receipt.Request);}
        }
        // The native DB calls already acknowledged these bytes. Report cleanup
        // failure and attempt later originals; do not requeue committed work.
        return cleanupError;
    }
    private static bool RemoveExpectedDictionaryValue<T>(object dictionary,long key,object? expected) {
        // Concurrent map/tracking replacement outside queue admission must not
        // be removed by a stale key-only delete after the identity check.
        return expected is T value && ((ICollection<KeyValuePair<long,T>>)dictionary).Remove(new KeyValuePair<long,T>(key,value));
    }
}
