#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
namespace FeralKinshipCompanions;

// Stratum reuses scratch buffers across calls. Keep each related buffer group
// serialized through the original DB acknowledgement and our recovery finalizer.
internal static class NativeStratumSaveAdapter
{
    private sealed class Gates {
        internal readonly object Save=new(),Generation=new(),Unload=new();
    }
    private sealed record Lease(object Gate);
    private static readonly ConditionalWeakTable<object,Gates> gates=new();
    [ThreadStatic] private static HashSet<object>? active;
    internal static IReadOnlyList<MethodBase> Install(Harmony owner,Assembly native) {
        var result=new List<MethodBase>();if(!NativeChunkCompatibility.UsesStratum(native))return result;
        Type type=native.GetType("Vintagestory.Server.ServerSystemUnloadChunks",true)!;
        foreach(string name in new[]{"SaveDirtyUnloadedChunks","SaveDirtyMapRegions","UnloadGeneratingChunkColumns","UnloadChunkColumns"}) {
            MethodInfo m=AccessTools.DeclaredMethod(type,name)??throw new MissingMethodException(type.FullName,name);
            owner.Patch(m,prefix:new HarmonyMethod(typeof(NativeStratumSaveAdapter),nameof(Enter)){priority=Priority.First},
                finalizer:new HarmonyMethod(typeof(NativeStratumSaveAdapter),nameof(Exit)){priority=Priority.Last});
            result.Add(m);
        }
        return result;
    }
    private static void Enter(object __instance,MethodBase __originalMethod,ref Lease? __state) {
        var g=gates.GetValue(__instance,_=>new Gates());
        object gate=__originalMethod.Name=="UnloadGeneratingChunkColumns"?g.Generation:__originalMethod.Name=="UnloadChunkColumns"?g.Unload:g.Save;
        bool taken=false;
        try {
            Monitor.Enter(gate,ref taken);active??=new HashSet<object>(ReferenceEqualityComparer.Instance);
            if(!active.Add(gate))RetainedNativeSuspension.RefuseNativeSaveWork("Reentrant shared Stratum unload buffers are not admitted");
            __state=new Lease(gate);
        }catch{if(taken)Monitor.Exit(gate);throw;}
    }
    private static Exception? Exit(Exception? __exception,Lease? __state) {
        if(__state==null)return __exception;
        try{active!.Remove(__state.Gate);}finally{Monitor.Exit(__state.Gate);}
        return __exception;
    }
}
