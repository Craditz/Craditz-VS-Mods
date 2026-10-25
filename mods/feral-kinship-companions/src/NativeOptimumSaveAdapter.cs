#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HarmonyLib;

namespace FeralKinshipCompanions;

internal static class NativeOptimumSaveAdapter
{
    private sealed record Lease(object Owner,object Sync);
    private static FieldInfo? savingLock;
    [ThreadStatic] private static HashSet<object>? active;
    internal static IReadOnlyList<MethodBase> Install(Harmony harmony,Assembly native)
    {
        var targets=new List<MethodBase>();if(!NativeChunkCompatibility.UsesOptimum(native))return targets;
        Type type=native.GetType("Vintagestory.Server.ServerSystemLoadAndSaveGame",true)!;
        savingLock=AccessTools.Field(type,"savingLock");
        if(savingLock==null||savingLock.IsStatic||savingLock.FieldType!=typeof(object))throw new InvalidOperationException("Qualified native saving lock differs");
        foreach(string name in new[]{"SaveAllDirtyGeneratingChunks","SaveAllDirtyLoadedChunks","SaveAllDirtyMapChunks","SaveAllDirtyMapRegions"})
        {
            MethodInfo method=AccessTools.DeclaredMethod(type,name)??throw new MissingMethodException(type.FullName,name);
            harmony.Patch(method,prefix:new HarmonyMethod(typeof(NativeOptimumSaveAdapter),nameof(Enter)){priority=Priority.First},finalizer:new HarmonyMethod(typeof(NativeOptimumSaveAdapter),nameof(Exit)));targets.Add(method);
        }
        return targets;
    }
    private static void Enter(object __instance,ref Lease? __state)
    {
        object sync=savingLock!.GetValue(__instance)??throw new InvalidOperationException("Native saving lock unavailable");
        bool taken=false;
        try {
            Monitor.Enter(sync,ref taken);
            active??=new HashSet<object>(ReferenceEqualityComparer.Instance);
            if(!active.Add(__instance))RetainedNativeSuspension.RefuseNativeSaveWork("Reentrant shared Optimum save batch is not admitted");
            __state=new Lease(__instance,sync);
        } catch {if(taken)Monitor.Exit(sync);throw;}
    }
    private static Exception? Exit(Exception? __exception,Lease? __state)
    {
        if(__state==null)return __exception;
        try {active!.Remove(__state.Owner);}finally {Monitor.Exit(__state.Sync);}
        return __exception;
    }
}
