#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

// Bounded method/field contracts, never a version or whole-file identity waiver.
internal static class NativeOptimumCompatibility
{
    private const BindingFlags All = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    internal sealed class Supplement
    {
        public SortedDictionary<string,string> Native { get; set; } = new(StringComparer.Ordinal);
        public SortedDictionary<string,SortedDictionary<string,string>> Modules { get; set; } = new(StringComparer.Ordinal);
    }
    private static NativeChunkContractSnapshot? expectedNative;
    private static Supplement? expectedSupplement;
    private static readonly Dictionary<Assembly,bool> moduleResults=new();
    private static T Read<T>(string name)
    {
        using Stream stream=typeof(NativeOptimumCompatibility).Assembly.GetManifestResourceStream("FeralKinshipCompanions."+name) ?? throw new InvalidOperationException("Optimum contract resource missing");
        return JsonSerializer.Deserialize<T>(stream) ?? throw new InvalidOperationException("Optimum contract resource invalid");
    }
    internal static NativeChunkContractSnapshot ExpectedNative => expectedNative ??= Read<NativeChunkContractSnapshot>("NativeOptimum1227.json");
    internal static Supplement ExpectedSupplement => expectedSupplement ??= Read<Supplement>("NativeOptimumSupplement1227.json");

    internal static bool TryQualify(Assembly native,NativeChunkContractSnapshot actual,out string reason)
    {
        if(!NativeChunkContractReader.Compare(ExpectedNative,actual,out reason))return false;
        Type save=native.GetType("Vintagestory.Server.ServerSystemLoadAndSaveGame",true)!;
        foreach(string name in new[]{"SaveAllDirtyGeneratingChunks","SaveAllDirtyLoadedChunks","SaveAllDirtyMapChunks","SaveAllDirtyMapRegions"})
        {
            MethodInfo method=save.GetMethods(All).Single(m=>m.Name==name);
            if(!BatchProjectionMatchesVanilla(method,out reason))return false;
        }
        MethodInfo generation=native.GetType("Vintagestory.Server.ServerSystemUnloadChunks",true)!.GetMethods(All).Single(m=>m.Name=="UnloadGeneratingChunkColumns");
        if(!TryRequestLocal(generation,out _,out reason))return false;
        if(!Compare(ExpectedSupplement,Capture(native),out reason))return false;
        reason="";return true;
    }

    internal static bool BatchProjectionMatchesVanilla(MethodInfo method,out string reason)
    {
        var il=NativeChunkContractReader.Instructions(method);var field=method.DeclaringType!.GetField("optimumSaveBatch",All);
        if(field==null||field.IsStatic||field.FieldType.FullName?.StartsWith("System.Collections.Generic.List`1",StringComparison.Ordinal)!=true
            ||field.FieldType.GetGenericArguments()[0].FullName!="Vintagestory.Common.Database.DbChunk") {reason="Unsupported Optimum batch field";return false;}
        int start=il.FindIndex(i=>i.Code==OpCodes.Ldfld&&Equals(i.Operand,field))-1;
        OpCode[] code={OpCodes.Ldarg_0,OpCodes.Ldfld,OpCodes.Brtrue_S,OpCodes.Ldarg_0,OpCodes.Newobj,OpCodes.Stfld,OpCodes.Ldarg_0,OpCodes.Ldfld,OpCodes.Callvirt,OpCodes.Ldarg_0,OpCodes.Ldfld};
        if(start<0||start+11>il.Count) {reason="Optimum lazy batch prologue absent";return false;}
        for(int i=0;i<code.Length;i++)if(il[start+i].Code!=code[i] && !(i==2&&il[start+i].Code==OpCodes.Brtrue)) {reason="Optimum lazy batch opcode differs";return false;}
        if(new[]{1,5,7,10}.Any(i=>!Equals(il[start+i].Operand,field))
            ||il[start+4].Operand is not ConstructorInfo constructor||constructor.DeclaringType!=field.FieldType||constructor.GetParameters().Length!=0
            ||il[start+8].Operand is not MethodInfo clear||clear.Name!="Clear"||clear.DeclaringType!=field.FieldType||clear.GetParameters().Length!=0
            ||!Equals(il[start+2].Operand,il[start+6].Offset)) {reason="Optimum lazy batch data flow differs";return false;}
        var inside=il.Skip(start+1).Take(10).Select(i=>i.Offset).ToHashSet();
        if(il.Where((_,n)=>n<start||n>=start+11).Any(i=>i.Operand is int target&&i.Code.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch&&inside.Contains(target)
            ||i.Operand is int[] targets&&targets.Any(inside.Contains))) {reason="Branch enters Optimum batch prologue";return false;}
        var projected=il.Take(start).Append(il[start+4] with {Offset=il[start].Offset}).Concat(il.Skip(start+11)).ToArray();
        string id=NativeChunkContractReader.MethodId(method),shape=NativeChunkContractReader.Shape(method,projected);
        // Map-regions is outside the old root set, but shares the exact batch and
        // therefore has its own reviewed reference in the supplement.
        string? expected=NativeChunkCompatibility.Expected.Methods.TryGetValue(id,out string? old)?old:
            ExpectedSupplement.Native.GetValueOrDefault("vanilla-projection:"+id);
        if(expected==null||shape!=expected) {reason="Optimum batch remainder differs from vanilla: "+id;return false;}
        reason="";return true;
    }

    // Identify the request supplying each retirement key. A slot number is never
    // selected from an Optimum version string or guessed from its local count.
    internal static bool TryRequestLocal(MethodBase method,out int local,out string reason)
    {
        local=-1;var il=NativeChunkContractReader.Instructions(method);var body=method.GetMethodBody()!;
        Type request=method.DeclaringType!.Assembly.GetType("Vintagestory.Server.ChunkColumnLoadRequest",true)!;
        bool Removal(NativeChunkContractReader.Instruction i)=>i.Operand is MethodInfo m&&m.Name=="Remove"
            &&(m.DeclaringType?.FullName=="Vintagestory.API.Util.DictExtensions"||m.DeclaringType?.Name.StartsWith("ConcurrentIndexedFifoQueue",StringComparison.Ordinal)==true);
        int removals=0;
        for(int i=0;i<il.Count;i++)if(Removal(il[i]))
        {
            if(i<2||il[i-1].Code!=OpCodes.Ldfld||il[i-1].Operand is not FieldInfo key||key.Name!="mapIndex2d"||key.DeclaringType!=request
                ||!TryLocal(il[i-2],out int operand)||operand>=body.LocalVariables.Count||body.LocalVariables[operand].LocalType!=request
                ||local>=0&&local!=operand) {reason="Generation retirement key has no unique typed request source";return false;}
            local=operand;removals++;
        }
        if(removals!=3||local<0||!NativeChunkContractReader.EveryPathStoresLocal(il,local,Removal)) {reason="Generation request does not dominate all retirement sites";return false;}
        reason="";return true;
    }
    private static bool TryLocal(NativeChunkContractReader.Instruction instruction,out int local)
    {
        local=-1;if(instruction.Code==OpCodes.Ldloc||instruction.Code==OpCodes.Ldloc_S) {local=(int)instruction.Operand!;return true;}
        string name=instruction.Code.Name??"";return name.StartsWith("ldloc.",StringComparison.Ordinal)&&int.TryParse(name[6..],out local);
    }

    private static void AddFields(Type type,SortedDictionary<string,string> result)
    {
        foreach(FieldInfo field in type.GetFields(All))result["field:"+NativeChunkContractReader.TypeId(type)+"::"+field.Name]=NativeChunkContractReader.TypeId(field.FieldType)+(field.IsStatic?":static":":instance")+(field.IsInitOnly?":readonly":"")+(field.IsLiteral?":constant="+JsonSerializer.Serialize(field.GetRawConstantValue()):"");
    }
    private static void AddMethods(IEnumerable<MethodBase> roots,SortedDictionary<string,string> result,System.Func<Assembly,bool> follow)
    {
        var methods=new Dictionary<string,MethodBase>(StringComparer.Ordinal);
        void Add(MethodBase m) {
            if(!m.IsAbstract&&!m.ContainsGenericParameters&&m.GetMethodBody()!=null&&methods.TryAdd(NativeChunkContractReader.MethodId(m),m)
                &&m.DeclaringType?.TypeInitializer is ConstructorInfo initializer&&initializer!=m)Add(initializer);
        }
        foreach(MethodBase m in roots)Add(m);
        for(int at=0;at<methods.Count;at++)foreach(MethodBase called in NativeChunkContractReader.Instructions(methods.Values.ElementAt(at)).Select(i=>i.Operand).OfType<MethodBase>())
            if(called.DeclaringType!=null&&follow(called.DeclaringType.Assembly))Add(called);
        foreach(var pair in methods) {result["method:"+pair.Key]=NativeChunkContractReader.Shape(pair.Value);AddFields(pair.Value.DeclaringType!,result);}
    }
    internal static SortedDictionary<string,string> CaptureModule(Assembly assembly)
    {
        var result=new SortedDictionary<string,string>(StringComparer.Ordinal);var roots=new List<MethodBase>();
        var names=RetainedNativeSuspension.NativePatchEntries;
        foreach(Type type in RetainedNativeSuspension.GetLoadableTypes(assembly).Where(t=>!t.ContainsGenericParameters))
        {
            bool relevant=typeof(Entity).IsAssignableFrom(type)||typeof(EntityBehavior).IsAssignableFrom(type)||typeof(IAiTask).IsAssignableFrom(type);
            if(!relevant)continue;
            foreach(MethodInfo method in type.GetMethods(All))if(names.Contains(method.Name)||method.Name.StartsWith("IsTargetableEntity",StringComparison.Ordinal)||method.Name=="CanSense")roots.Add(method);
        }
        AddMethods(roots,result,a=>a==assembly||a.GetName().Name=="Optimum.Api.Contracts");return result;
    }
    internal static Supplement Capture(Assembly native)
    {
        var result=new Supplement();Type save=native.GetType("Vintagestory.Server.ServerSystemLoadAndSaveGame",true)!;
        Type thread=native.GetType("Vintagestory.Server.ChunkServerThread",true)!;Type unload=native.GetType("Vintagestory.Server.ServerSystemUnloadChunks",true)!;
        Type request=native.GetType("Vintagestory.Server.ChunkColumnLoadRequest",true)!;
        var roots=save.GetMethods(All).Where(m=>m.Name is "SaveAllDirtyMapRegions" or "SaveGameWorld" or "OnSeparateThreadTick" or "OnWorldBeingSaved").Cast<MethodBase>()
            .Concat(thread.GetMethods(All).Where(m=>m.Name=="TryAcquireOptimumWorldgenFootprint"))
            .Concat(request.GetMethods(All).Where(m=>m.Name is "TryClaimDispatch" or "ReleaseDispatch" or "FlagToDispose" or "FlagToRequeue" or "get_Disposed"));
        AddMethods(roots,result.Native,a=>a.GetName().Name=="Optimum.Api.Contracts");AddFields(save,result.Native);AddFields(thread,result.Native);AddFields(unload,result.Native);
        Type lease=thread.GetMethod("TryAcquireOptimumWorldgenFootprint",All)!.GetParameters().Last().ParameterType.GetElementType()!;
        if(lease.FullName!="Vintagestory.API.Config.OptimumWorldgenFootprintLease"||!typeof(IDisposable).IsAssignableFrom(lease))throw new InvalidOperationException("Optimum lease contract differs");
        Assembly contracts=lease.Assembly;
        foreach(Type type in contracts.GetTypes().Where(t=>t.FullName!.Contains("OptimumWorldgenFootprint",StringComparison.Ordinal)||t.FullName!.Contains("OptimumDispatchClaim",StringComparison.Ordinal)))
            AddMethods(type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)),result.Native,a=>a==contracts);
        foreach(string module in new[]{"VintagestoryAPI","VSEssentials","VSSurvivalMod"})result.Modules[module]=CaptureModule(Assembly.Load(module));
        return result;
    }
    internal static bool Compare(Supplement expected,Supplement actual,out string reason)
    {
        bool Equal(SortedDictionary<string,string> a,SortedDictionary<string,string> b)=>a.Count==b.Count&&a.All(p=>b.TryGetValue(p.Key,out string? value)&&value==p.Value);
        // The analysis-only map-region projection is not a native member.
        var native=new SortedDictionary<string,string>(expected.Native.Where(p=>!p.Key.StartsWith("vanilla-projection:",StringComparison.Ordinal)).ToDictionary(p=>p.Key,p=>p.Value),StringComparer.Ordinal);
        if(!Equal(native,actual.Native)) {reason="Unsupported Optimum native helper/lock/lease contract";return false;}
        if(expected.Modules.Count!=actual.Modules.Count||expected.Modules.Any(p=>!actual.Modules.TryGetValue(p.Key,out var module)||!Equal(p.Value,module))) {reason="Unsupported Optimum module patch-coverage contract";return false;}
        reason="";return true;
    }
    internal static bool QualifiesModule(Assembly assembly)
    {
        lock(moduleResults) {
            if(moduleResults.TryGetValue(assembly,out bool prior))return prior;
            if(!ExpectedSupplement.Modules.TryGetValue(assembly.GetName().Name??"",out var expected))return false;
            var actual=CaptureModule(assembly);return moduleResults[assembly]=expected.Count==actual.Count&&expected.All(p=>actual.TryGetValue(p.Key,out string? value)&&value==p.Value);
        }
    }
}
