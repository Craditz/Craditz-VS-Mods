#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;

namespace FeralKinshipCompanions;

// Separate, exact control/data-flow profile for the reviewed Stratum release.
// Neither its version label nor a disk hash grants native compatibility.
internal static class NativeStratumCompatibility
{
    private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly;
    internal sealed class Supplement {
        public SortedDictionary<string,string> Native {get;set;}=new(StringComparer.Ordinal);
        public SortedDictionary<string,SortedDictionary<string,string>> Modules {get;set;}=new(StringComparer.Ordinal);
        public SortedDictionary<string,SortedDictionary<string,string>> ServerModules {get;set;}=new(StringComparer.Ordinal);
    }
    private static NativeChunkContractSnapshot? expected;
    private static Supplement? supplement;
    private static readonly Dictionary<Assembly,bool> moduleResults=new();
    private static readonly Dictionary<Assembly,bool> clientModuleResults=new();
    private static T Read<T>(string file) {
        using Stream s=typeof(NativeStratumCompatibility).Assembly.GetManifestResourceStream("FeralKinshipCompanions."+file)
            ??throw new InvalidOperationException("Stratum compatibility resource missing");
        return JsonSerializer.Deserialize<T>(s)??throw new InvalidOperationException("Invalid Stratum compatibility resource");
    }
    internal static NativeChunkContractSnapshot Expected=>expected??=Read<NativeChunkContractSnapshot>("NativeStratum1227_2.json");
    internal static Supplement ExpectedSupplement=>supplement??=Read<Supplement>("NativeStratumSupplement1227_2.json");
    internal static bool TryQualify(Assembly native,NativeChunkContractSnapshot actual,out string reason) {
        if(!NativeChunkContractReader.Compare(Expected,actual,out reason))return false;
        MethodInfo generation=native.GetType("Vintagestory.Server.ServerSystemUnloadChunks",true)!.GetMethods(All).Single(m=>m.Name=="UnloadGeneratingChunkColumns");
        if(!NativeOptimumCompatibility.TryRequestLocal(generation,out int local,out reason)||!DropSites(generation,local,out reason))return false;
        if(!Compare(ExpectedSupplement,Capture(native),out reason))return false;
        reason="";return true;
    }
    private static bool Local(NativeChunkContractReader.Instruction i,int slot) {
        if(i.Code==OpCodes.Ldloc||i.Code==OpCodes.Ldloc_S)return i.Operand is int n&&n==slot;
        return i.Code.Name=="ldloc."+slot;
    }
    internal static bool DropCall(MethodInfo method,bool claim) {
        var args=method.GetParameters();
        return method.DeclaringType==(claim?typeof(System.Threading.Interlocked):typeof(System.Threading.Volatile))
            &&method.Name==(claim?"Exchange":"Write")&&args.Length==2
            &&args[0].ParameterType==typeof(int).MakeByRefType()&&args[1].ParameterType==typeof(int)
            &&method.ReturnType==(claim?typeof(int):typeof(void));
    }
    internal static bool DropSites(MethodInfo method,int local,out string reason) {
        var il=NativeChunkContractReader.Instructions(method);
        var field=method.DeclaringType!.Assembly.GetType("Vintagestory.Server.ChunkColumnLoadRequest",true)!.GetField("stratumDropping",All);
        if(field==null||field.IsStatic||field.FieldType!=typeof(int)){reason="Stratum drop flag differs";return false;}
        int claims=0,clears=0;
        for(int i=0;i<il.Count;i++)if(il[i].Operand is MethodInfo call&&(DropCall(call,true)||DropCall(call,false))) {
            bool claim=DropCall(call,true);
            if(i<3||!Local(il[i-3],local)||il[i-2].Code!=OpCodes.Ldflda||!Equals(il[i-2].Operand,field)
                ||il[i-1].Code!=(claim?OpCodes.Ldc_I4_1:OpCodes.Ldc_I4_0)) {
                reason="Stratum drop flag does not use the qualified request";return false;
            }
            if(claim)claims++;else clears++;
        }
        if(claims!=1||clears!=3){reason="Stratum drop ownership sites differ";return false;}
        reason="";return true;
    }
    // Include new incremental saves, reusable buffer owners, request pinning,
    // locks and their private helpers. Do not rely only on the old root set.
    internal static Supplement Capture(Assembly native) {
        var result=new Supplement();var methods=new Dictionary<string,MethodBase>(StringComparer.Ordinal);
        string[] owners={"Vintagestory.Server.ServerSystemLoadAndSaveGame","Vintagestory.Server.ServerSystemUnloadChunks",
            "Vintagestory.Server.ChunkServerThread","Vintagestory.Server.ChunkColumnLoadRequest","Vintagestory.Common.FastRWLock"};
        bool Owned(Type t) => owners.Any(n=>t.FullName==n||t.FullName?.StartsWith(n+"+",StringComparison.Ordinal)==true)
            ||t.Assembly==native&&(t.FullName?.StartsWith("Vintagestory.Server.Stratum",StringComparison.Ordinal)==true
                ||t.FullName?.StartsWith("Vintagestory.Common.Stratum",StringComparison.Ordinal)==true);
        void Fields(Type type) {
            foreach(var f in type.GetFields(All))result.Native["field:"+NativeChunkContractReader.TypeId(type)+"::"+f.Name]
                =NativeChunkContractReader.TypeId(f.FieldType)+(f.IsStatic?":static":":instance")+(f.IsInitOnly?":readonly":"")
                    +(f.IsLiteral?":constant="+JsonSerializer.Serialize(f.GetRawConstantValue()):"");
        }
        void Add(MethodBase m) {
            if(m.IsAbstract||m.ContainsGenericParameters||m.GetMethodBody()==null||!methods.TryAdd(NativeChunkContractReader.MethodId(m),m))return;
            Fields(m.DeclaringType!);
            if(m.DeclaringType!.TypeInitializer is ConstructorInfo init&&m!=init)Add(init);
        }
        foreach(string name in owners) {
            Type type=native.GetType(name,true)!;Fields(type);
            foreach(var m in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))Add(m);
        }
        for(int i=0;i<methods.Count;i++)foreach(var called in NativeChunkContractReader.Instructions(methods.Values.ElementAt(i)).Select(x=>x.Operand).OfType<MethodBase>())
            if(called.DeclaringType!=null&&Owned(called.DeclaringType))Add(called);
        foreach(var p in methods)result.Native["method:"+p.Key]=NativeChunkContractReader.Shape(p.Value);
        foreach(string module in new[]{"VintagestoryAPI","VSEssentials","VSSurvivalMod"})
            result.ServerModules[module]=CaptureServerModule(Assembly.Load(module));
        return result;
    }
    // The shared server gates never patch OnRenderFrame. Reading that client
    // callback's body can load OpenGL even without invoking it. Audit the exact
    // server root/helper closure separately; missing server dependencies still fail.
    internal static SortedDictionary<string,string> CaptureServerModule(Assembly assembly) {
        var result=new SortedDictionary<string,string>(StringComparer.Ordinal);
        var methods=new Dictionary<string,MethodBase>(StringComparer.Ordinal);
        var names=RetainedNativeSuspension.NativePatchEntries.Where(n=>n!="OnRenderFrame").ToHashSet(StringComparer.Ordinal);
        void Add(MethodBase method) {
            if(method.IsAbstract||method.ContainsGenericParameters||method.GetMethodBody()==null
                ||!methods.TryAdd(NativeChunkContractReader.MethodId(method),method))return;
            if(method.DeclaringType?.TypeInitializer is ConstructorInfo init&&method!=init)Add(init);
        }
        foreach(Type type in RetainedNativeSuspension.GetLoadableTypes(assembly).Where(t=>!t.ContainsGenericParameters)) {
            bool relevant=typeof(Vintagestory.API.Common.Entities.Entity).IsAssignableFrom(type)
                ||typeof(Vintagestory.API.Common.Entities.EntityBehavior).IsAssignableFrom(type)
                ||typeof(Vintagestory.API.Common.IAiTask).IsAssignableFrom(type);
            if(!relevant)continue;
            foreach(MethodInfo method in type.GetMethods(All))
                if(names.Contains(method.Name)||method.Name.StartsWith("IsTargetableEntity",StringComparison.Ordinal)||method.Name=="CanSense")Add(method);
        }
        for(int i=0;i<methods.Count;i++)
            foreach(MethodBase called in NativeChunkContractReader.Instructions(methods.Values.ElementAt(i)).Select(x=>x.Operand).OfType<MethodBase>())
                if(called.DeclaringType?.Assembly==assembly)Add(called);
        foreach(var entry in methods) {
            result["method:"+entry.Key]=NativeChunkContractReader.Shape(entry.Value);
            Type type=entry.Value.DeclaringType!;
            foreach(FieldInfo field in type.GetFields(All))
                result["field:"+NativeChunkContractReader.TypeId(type)+"::"+field.Name]=NativeChunkContractReader.TypeId(field.FieldType)
                    +(field.IsStatic?":static":":instance")+(field.IsInitOnly?":readonly":"")
                    +(field.IsLiteral?":constant="+JsonSerializer.Serialize(field.GetRawConstantValue()):"");
        }
        return result;
    }
    // Called only while installing client rendering. Preserve preview.22's full
    // client profile rather than letting a server-only contract authorize render IL.
    internal static bool QualifiesClientModule(Assembly assembly) {
        lock(clientModuleResults) {
            if(clientModuleResults.TryGetValue(assembly,out bool prior))return prior;
            if(!ExpectedSupplement.Modules.TryGetValue(assembly.GetName().Name??"",out var expected))return false;
            var actual=NativeOptimumCompatibility.CaptureModule(assembly);
            return clientModuleResults[assembly]=expected.Count==actual.Count&&expected.All(p=>actual.TryGetValue(p.Key,out var v)&&v==p.Value);
        }
    }
    internal static bool Compare(Supplement expected,Supplement actual,out string reason) {
        static bool Equal(SortedDictionary<string,string> a,SortedDictionary<string,string> b)
            =>a.Count==b.Count&&a.All(p=>b.TryGetValue(p.Key,out var v)&&v==p.Value);
        if(!Equal(expected.Native,actual.Native)){reason="Unsupported Stratum save/pin/helper contract";return false;}
        if(expected.ServerModules.Count!=actual.ServerModules.Count||expected.ServerModules.Any(p=>!actual.ServerModules.TryGetValue(p.Key,out var m)||!Equal(p.Value,m))) {
            reason="Unsupported Stratum native module patch coverage";return false;
        }
        reason="";return true;
    }
    internal static bool QualifiesModule(Assembly assembly) {
        lock(moduleResults) {
            if(moduleResults.TryGetValue(assembly,out bool prior))return prior;
            if(!ExpectedSupplement.ServerModules.TryGetValue(assembly.GetName().Name??"",out var expected))return false;
            var actual=CaptureServerModule(assembly);
            return moduleResults[assembly]=expected.Count==actual.Count&&expected.All(p=>actual.TryGetValue(p.Key,out var v)&&v==p.Value);
        }
    }
}
