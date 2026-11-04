#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace FeralKinshipCompanions;

// Optimum0.3.13 is reviewed only for a remote multiplayer client. Its older
// worldgen/save path is never admitted by NativeChunkCompatibility.TryQualify.
internal static class NativeOptimum013ClientCompatibility
{
    private const BindingFlags All=BindingFlags.DeclaredOnly|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    internal sealed class Profile {
        public SortedDictionary<string,SortedDictionary<string,string>> Modules {get;set;}=new(StringComparer.Ordinal);
    }
    private static Profile? expected;
    private static readonly Dictionary<Assembly,bool> modules=new();
    internal static Profile Expected {
        get {
            if(expected!=null)return expected;
            using Stream stream=typeof(NativeOptimum013ClientCompatibility).Assembly.GetManifestResourceStream("FeralKinshipCompanions.NativeOptimum013Client1227.json")
                ??throw new InvalidOperationException("Optimum0.3.13 client profile is missing");
            return expected=JsonSerializer.Deserialize<Profile>(stream)??throw new InvalidOperationException("Invalid Optimum0.3.13 client profile");
        }
    }
    internal static SortedDictionary<string,string> CaptureModule(Assembly assembly) {
        var result=NativeOptimumCompatibility.CaptureModule(assembly);
        var extra=new Dictionary<string,MethodBase>(StringComparer.Ordinal);
        void Add(MethodBase method) {
            if(method.IsAbstract||method.ContainsGenericParameters||method.GetMethodBody()==null||!extra.TryAdd(NativeChunkContractReader.MethodId(method),method))return;
            if(method.DeclaringType?.TypeInitializer is ConstructorInfo init&&method!=init)Add(init);
        }
        // Shared setup adds these explicit entries outside behavior/task roots.
        foreach(Type type in RetainedNativeSuspension.GetLoadableTypes(assembly)) {
            if(type.FullName=="Vintagestory.Common.ClassRegistry")
                foreach(MethodInfo method in type.GetMethods(All).Where(m=>m.Name is "CreateItem" or "RegisterItemClass" or "RegisterBlockClass" or "RegisterEntityType"))Add(method);
            if(type.FullName=="Vintagestory.Common.ItemTypeNet")
                foreach(MethodInfo method in type.GetMethods(All).Where(m=>m.Name=="ReadItemTypePacket"))Add(method);
            if(type.FullName=="Vintagestory.Client.NoObf.ClientSystemStartup")
                foreach(MethodInfo method in type.GetMethods(All).Where(m=>m.Name is "PopulateItems" or "LoadItemTypes" or "HandleServerAssets_Step1"))Add(method);
            if(type.FullName=="Vintagestory.GameContent.AiTaskManager")
                foreach(MethodInfo method in type.GetMethods(All))
                    if(!method.ContainsGenericParameters&&method.Name is "OnGameTick" or "ExecuteTask" or "Notify" or "OnEntityHurt" or "StartNewTasks" or "ProcessRunningTasks")Add(method);
            if(type.FullName=="Vintagestory.GameContent.EntityBehaviorMortallyWoundable")
                foreach(MethodInfo method in type.GetMethods(All).Where(m=>m.Name=="recover"))Add(method);
            if(type.FullName=="Vintagestory.API.Common.AiTaskBase")
                foreach(MethodInfo method in type.GetMethods(All).Where(m=>m.Name.StartsWith("<StartExecute>",StringComparison.Ordinal)))Add(method);
        }
        for(int i=0;i<extra.Count;i++)
            foreach(MethodBase called in NativeChunkContractReader.Instructions(extra.Values.ElementAt(i)).Select(x=>x.Operand).OfType<MethodBase>())
                if(called.DeclaringType?.Assembly==assembly||called.DeclaringType?.Assembly.GetName().Name=="Optimum.Api.Contracts")Add(called);
        foreach(var entry in extra) {
            result["method:"+entry.Key]=NativeChunkContractReader.Shape(entry.Value);
            Type type=entry.Value.DeclaringType!;
            foreach(FieldInfo field in type.GetFields(All))result["field:"+NativeChunkContractReader.TypeId(type)+"::"+field.Name]=NativeChunkContractReader.TypeId(field.FieldType)
                +(field.IsStatic?":static":":instance")+(field.IsInitOnly?":readonly":"")
                +(field.IsLiteral?":constant="+JsonSerializer.Serialize(field.GetRawConstantValue()):"");
        }
        return result;
    }
    internal static Profile Capture(Assembly native) {
        var profile=new Profile();
        foreach(string name in new[]{"VintagestoryLib","VintagestoryAPI","VSEssentials","VSSurvivalMod"}) {
            Assembly assembly=name=="VintagestoryLib"?native:Assembly.Load(name);
            profile.Modules[name]=CaptureModule(assembly);
        }
        return profile;
    }
    internal static bool Compare(Profile expected,Profile actual,out string reason) {
        if(expected.Modules.Count!=actual.Modules.Count||expected.Modules.Any(p=>!actual.Modules.TryGetValue(p.Key,out var module)
            ||p.Value.Count!=module.Count||p.Value.Any(k=>!module.TryGetValue(k.Key,out var value)||value!=k.Value))) {
            reason="Unsupported Optimum0.3.13 client entity/AI/render contract";return false;
        }
        reason="";return true;
    }
    internal static bool TryQualify(Assembly native,out string reason) {
        try {return Compare(Expected,Capture(native),out reason);}
        catch(Exception error) {reason="Optimum0.3.13 client inspection failed: "+error.GetType().Name+": "+error.Message;return false;}
    }
    internal static bool QualifiesModule(Assembly assembly) {
        lock(modules) {
            if(modules.TryGetValue(assembly,out bool prior))return prior;
            if(!Expected.Modules.TryGetValue(assembly.GetName().Name??"",out var expected))return false;
            var actual=CaptureModule(assembly);
            return modules[assembly]=expected.Count==actual.Count&&expected.All(p=>actual.TryGetValue(p.Key,out var value)&&value==p.Value);
        }
    }
}
