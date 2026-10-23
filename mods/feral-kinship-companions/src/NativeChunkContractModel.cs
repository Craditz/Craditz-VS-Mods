#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

// Compatibility describes the adapter's actual native surfaces. Metadata tokens,
// branch encodings, nops, assembly timestamps, MVIDs, resources and file access do
// not determine acceptance. Critical control flow, locals and exception regions do.
internal sealed class NativeChunkContractSnapshot
{
    public int Format { get; set; } = 1;
    public string GameVersion { get; set; } = "1.22.7";
    public SortedDictionary<string, string> Fields { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, string> Methods { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, string> Witnesses { get; set; } = new(StringComparer.Ordinal);
    public List<string> Coverage { get; set; } = new();
}

internal static class NativeChunkContractReader
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private static readonly Dictionary<ushort, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(c => unchecked((ushort)c.Value));
    internal sealed record Instruction(int Offset, OpCode Code, object? Operand);

    internal static string TypeId(Type type)
    {
        if (type.IsByRef) return TypeId(type.GetElementType()!) + "&";
        if (type.IsPointer) return TypeId(type.GetElementType()!) + "*";
        if (type.IsArray) return TypeId(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        if (type.IsGenericParameter) return (type.DeclaringMethod == null ? "!" : "!!") + type.GenericParameterPosition;
        string kind = type.IsValueType ? "value:" : type.IsInterface ? "interface:" : "class:";
        if (type.IsGenericType) return kind + "[" + type.Assembly.GetName().Name + "]" + type.GetGenericTypeDefinition().FullName + "<" + string.Join(",", type.GetGenericArguments().Select(TypeId)) + ">";
        return kind + "[" + type.Assembly.GetName().Name + "]" + (type.FullName ?? type.Name);
    }

    internal static string MethodId(MethodBase method) => TypeId(method.DeclaringType!) + "::" + method.Name
        + (method.IsGenericMethod ? "<" + string.Join(",", method.GetGenericArguments().Select(TypeId)) + ">" : "")
        + "(" + string.Join(",", method.GetParameters().Select(p => TypeId(p.ParameterType))) + ")->"
        + (method is MethodInfo info ? TypeId(info.ReturnType) : "ctor")
        + (method.IsStatic ? ":static" : ":instance") + (method.IsVirtual ? ":virtual" : "") + ":" + method.CallingConvention;

    private static string MemberId(MemberInfo member) => member switch
    {
        MethodBase method => MethodId(method),
        FieldInfo field => TypeId(field.DeclaringType!) + "::" + field.Name + ":" + TypeId(field.FieldType),
        Type type => TypeId(type),
        _ => throw new InvalidOperationException("Unsupported native IL member: " + member.MemberType)
    };

    internal static List<Instruction> Instructions(MethodBase method)
    {
        byte[] bytes = method.GetMethodBody()?.GetILAsByteArray() ?? throw new InvalidOperationException("Native method has no inspectable body: " + MethodId(method));
        Type[]? typeArguments = method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null;
        Type[]? methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        var result = new List<Instruction>();
        int at = 0;
        int I4() { int value = BitConverter.ToInt32(bytes, at); at += 4; return value; }
        while (at < bytes.Length)
        {
            int offset = at;
            ushort value = bytes[at++];
            if (value == 0xfe) value = (ushort)(0xfe00 | bytes[at++]);
            if (!Codes.TryGetValue(value, out OpCode code)) throw new InvalidOperationException("Unknown native IL opcode");
            object? operand;
            switch (code.OperandType)
            {
                case OperandType.InlineNone: operand = null; break;
                case OperandType.ShortInlineI: operand = (int)(sbyte)bytes[at++]; break;
                case OperandType.InlineI: operand = I4(); break;
                case OperandType.InlineI8: operand = BitConverter.ToInt64(bytes, at); at += 8; break;
                case OperandType.ShortInlineR: operand = BitConverter.ToInt32(bytes, at).ToString("x8", CultureInfo.InvariantCulture); at += 4; break;
                case OperandType.InlineR: operand = BitConverter.ToInt64(bytes, at).ToString("x16", CultureInfo.InvariantCulture); at += 8; break;
                case OperandType.ShortInlineVar: operand = (int)bytes[at++]; break;
                case OperandType.InlineVar: operand = (int)BitConverter.ToUInt16(bytes, at); at += 2; break;
                case OperandType.ShortInlineBrTarget: int delta = (sbyte)bytes[at++]; operand = at + delta; break;
                case OperandType.InlineBrTarget: int distance = I4(); operand = at + distance; break;
                case OperandType.InlineSwitch:
                    int count = I4(); int end = checked(at + count * 4); var targets = new int[count];
                    for (int i = 0; i < count; i++) targets[i] = end + I4(); operand = targets; break;
                case OperandType.InlineString: operand = method.Module.ResolveString(I4()); break;
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    operand = method.Module.ResolveMember(I4(), typeArguments, methodArguments) ?? throw new InvalidOperationException("Unresolved native IL member"); break;
                default: throw new InvalidOperationException("Unsupported native IL operand: " + code.OperandType);
            }
            result.Add(new Instruction(offset, code, operand));
        }
        return result;
    }

    // Normalize harmless compiler encodings, never discard branches, EH regions,
    // local identities/types, constants, member identities or meaningful opcodes.
    internal static string Normalize(MethodBase method, IReadOnlyList<Instruction>? supplied = null)
    {
        var instructions = (supplied ?? Instructions(method)).Where(i => i.Code != OpCodes.Nop).ToArray();
        var body = method.GetMethodBody()!;
        int Index(int offset) { for (int i = 0; i < instructions.Length; i++) if (instructions[i].Offset >= offset) return i; return instructions.Length; }
        var text = new StringBuilder(MethodId(method)).Append('\n').Append("init=").Append(body.InitLocals).Append('\n');
        foreach (LocalVariableInfo local in body.LocalVariables) text.Append("local ").Append(local.LocalIndex).Append(' ').Append(TypeId(local.LocalType)).Append(' ').Append(local.IsPinned).Append('\n');
        foreach (Instruction item in instructions)
        {
            string name = item.Code.Name!;
            string operand;
            if (item.Code.OperandType is OperandType.ShortInlineBrTarget or OperandType.InlineBrTarget) { name = name.EndsWith(".s", StringComparison.Ordinal) ? name[..^2] : name; operand = Index((int)item.Operand!).ToString(CultureInfo.InvariantCulture); }
            else if (item.Code.OperandType == OperandType.InlineSwitch) operand = string.Join(",", ((int[])item.Operand!).Select(Index));
            else if (item.Operand is MemberInfo member) operand = MemberId(member);
            else if (item.Code.OperandType == OperandType.InlineString) operand = Convert.ToBase64String(Encoding.UTF8.GetBytes((string)item.Operand!));
            else operand = Convert.ToString(item.Operand, CultureInfo.InvariantCulture) ?? "";
            if (name.EndsWith(".s", StringComparison.Ordinal) && item.Code.OperandType is OperandType.ShortInlineVar or OperandType.ShortInlineI) name = name[..^2];
            foreach (string macro in new[] { "ldloc", "stloc", "ldarg", "ldc.i4" })
                if (name.StartsWith(macro + ".", StringComparison.Ordinal) && item.Operand == null)
                { string number = name[(macro.Length + 1)..]; operand = number == "m1" ? "-1" : number; name = macro; break; }
            text.Append(name).Append(' ').Append(operand).Append('\n');
        }
        foreach (ExceptionHandlingClause clause in body.ExceptionHandlingClauses)
            text.Append("eh ").Append(clause.Flags).Append(' ').Append(Index(clause.TryOffset)).Append(':').Append(Index(clause.TryOffset + clause.TryLength))
                .Append(' ').Append(Index(clause.HandlerOffset)).Append(':').Append(Index(clause.HandlerOffset + clause.HandlerLength))
                .Append(' ').Append(clause.Flags == ExceptionHandlingClauseOptions.Clause ? TypeId(clause.CatchType!) : clause.Flags == ExceptionHandlingClauseOptions.Filter ? Index(clause.FilterOffset).ToString(CultureInfo.InvariantCulture) : "-").Append('\n');
        return text.ToString();
    }

    internal static string Shape(MethodBase method, IReadOnlyList<Instruction>? supplied = null) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(method, supplied)))).ToLowerInvariant();
    private static FieldInfo Field(Type type, string name)
    {
        for (Type? current = type; current != null; current = current.BaseType) if (current.GetField(name, All) is FieldInfo field) return field;
        throw new MissingFieldException(type.FullName, name);
    }

    internal static NativeChunkContractSnapshot Capture(Assembly native)
    {
        if (native.GetName().Name != "VintagestoryLib") throw new InvalidOperationException("Native contract assembly role differs");
        var snapshot = new NativeChunkContractSnapshot
        {
            // Reflect the loaded API field: a const reference in our own IL
            // would be inlined at build time and falsely accept later games.
            GameVersion = typeof(GameVersion).GetField("ShortGameVersion", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string ?? "unavailable"
        };
        Type T(string name) => native.GetType(name, true)!;
        var methods = new Dictionary<string, MethodBase>(StringComparer.Ordinal);
        var roots = new Dictionary<string, string[]>
        {
            ["Vintagestory.Common.WorldChunk"] = new[] { "AddEntity", "RemoveEntity" },
            ["Vintagestory.Server.ServerChunk"] = new[] { "ToBytes", "FromBytes", "GatherEntitiesToSerialize", "FastSerializeEntities", "FastSerializeEntitiesCount", "AddEntity", "RemoveEntity" },
            ["Vintagestory.Common.GameDatabase"] = new[] { "SetChunks", "SetMapChunks" },
            ["Vintagestory.Server.ServerSystemUnloadChunks"] = new[] { "SaveDirtyUnloadedChunks", "UnloadGeneratingChunkColumns", "TryUnloadChunk", "UnloadChunkColumns" },
            ["Vintagestory.Server.ChunkServerThread"] = new[] { "addChunkColumnRequest", "EnsureMinimumWorldgenPassAt" },
            ["Vintagestory.Server.ServerSystemSupplyChunks"] = new[] { "FullyClearGeneratingQueue" },
            ["Vintagestory.Server.ServerSystemLoadAndSaveGame"] = new[] { "SaveAllDirtyLoadedChunks", "SaveAllDirtyGeneratingChunks", "SaveAllDirtyMapChunks" }
        };
        void Add(MethodBase method) { if (!method.IsAbstract && !method.ContainsGenericParameters && method.GetMethodBody() != null) methods.TryAdd(MethodId(method), method); }
        foreach (var root in roots)
        {
            Type type = T(root.Key);
            foreach (string name in root.Value)
            {
                var found = type.GetMethods(All).Where(m => m.Name == name).ToArray();
                if (found.Length == 0) throw new MissingMethodException(root.Key, name);
                foreach (MethodInfo method in found) Add(method);
            }
        }
        Type world = T("Vintagestory.Common.WorldChunk"), serverChunk = T("Vintagestory.Server.ServerChunk"), unload = T("Vintagestory.Server.ServerSystemUnloadChunks"), thread = T("Vintagestory.Server.ChunkServerThread"), request = T("Vintagestory.Server.ChunkColumnLoadRequest"), server = T("Vintagestory.Server.ServerMain");
        Type queue = Field(thread, "requestedChunkColumns").FieldType;
        var fieldGroups = new (Type Type, string[] Names)[]
        {
            (world, new[] { "packUnpackLock", "Entities" }), (serverChunk, new[] { "DirtyForSaving", "EntitiesSerialized" }),
            (unload, new[] { "dirtyChunksLock", "server", "chunkthread", "dirtyUnloadedChunks", "dirtyUnloadedMapChunks" }),
            (server, new[] { "readyToAutoSave" }), (thread, new[] { "requestedChunkColumns" }),
            (T("Vintagestory.Server.ServerSystemSupplyChunks"), new[] { "chunkthread" }),
            (queue, new[] { "elementsByIndex" }),
            (request, new[] { "Chunks", "MapChunk", "untilPass", "creationTime", "blockingRequest", "disposeOrRequeueFlags", "mapIndex2d", "generatingLock" }),
            (Field(request, "MapChunk").FieldType, new[] { "DirtyForSaving" })
        };
        foreach (var group in fieldGroups) foreach (string name in group.Names)
        {
            FieldInfo field = Field(group.Type, name);
            snapshot.Fields[TypeId(group.Type) + "::" + name] = TypeId(field.FieldType) + (field.IsStatic ? ":static" : ":instance") + (field.IsInitOnly ? ":readonly" : "");
        }
        Type generatingLock = Field(request, "generatingLock").FieldType;
        foreach (string name in new[] { "AcquireReadLock", "ReleaseReadLock" }) Add(generatingLock.GetMethod(name, All, null, Type.EmptyTypes, null) ?? throw new MissingMethodException(generatingLock.FullName, name));
        foreach (MethodInfo method in queue.GetMethods(All).Where(m => m.Name is "Enqueue" or "EnqueueWithoutAddingToIndex" or "Dequeue" or "DequeueWithoutRemovingFromIndex" or "Requeue" or "Remove" or "Clear")) Add(method);

        // Follow native private helpers and compiler-generated delegate bodies
        // within these owners. A changed SQL/serialization callback cannot hide
        // behind an unchanged caller's member token.
        var allowed = roots.Keys.Append(queue.GetGenericTypeDefinitionOrSelf().FullName!).ToHashSet(StringComparer.Ordinal);
        for (int cursor = 0; cursor < methods.Count; cursor++)
        {
            MethodBase current = methods.Values.ElementAt(cursor);
            foreach (MethodBase called in Instructions(current).Select(i => i.Operand).OfType<MethodBase>())
            {
                Type owner = called.DeclaringType!;
                string ownerName = owner.GetGenericTypeDefinitionOrSelf().FullName ?? "";
                bool owned = allowed.Any(name => ownerName == name || ownerName.StartsWith(name + "+", StringComparison.Ordinal));
                if (owned && (called.IsPrivate || called.Name.StartsWith("<", StringComparison.Ordinal))) Add(called);
            }
        }
        foreach (MethodBase method in methods.Values) snapshot.Methods[MethodId(method)] = Shape(method);

        MethodInfo generation = unload.GetMethods(All).Single(m => m.Name == "UnloadGeneratingChunkColumns");
        var gil = Instructions(generation);
        FieldInfo chunks = Field(request, "Chunks"), map = Field(request, "MapChunk");
        bool Removal(Instruction item) => item.Operand is MethodInfo m && m.Name == "Remove" && (m.DeclaringType == queue || m.DeclaringType?.FullName == "Vintagestory.API.Util.DictExtensions");
        snapshot.Witnesses["generation.Chunks.reads"] = gil.Count(i => i.Code == OpCodes.Ldfld && Equals(i.Operand, chunks)).ToString(CultureInfo.InvariantCulture);
        snapshot.Witnesses["generation.MapChunk.reads"] = gil.Count(i => i.Code == OpCodes.Ldfld && Equals(i.Operand, map)).ToString(CultureInfo.InvariantCulture);
        snapshot.Witnesses["generation.removal.sites"] = gil.Count(Removal).ToString(CultureInfo.InvariantCulture);
        snapshot.Witnesses["generation.local11.type"] = generation.GetMethodBody()!.LocalVariables.Count > 11 ? TypeId(generation.GetMethodBody()!.LocalVariables[11].LocalType) : "missing";
        snapshot.Witnesses["generation.local11.dominates.removals"] = EveryPathStoresLocal(gil, 11, Removal).ToString();
        MethodInfo saving = unload.GetMethods(All).Single(m => m.Name == "SaveDirtyUnloadedChunks");
        var sil = Instructions(saving);
        snapshot.Witnesses["unload.queue.clear.sites"] = sil.Count(i => i.Operand is MethodInfo m && m.Name == "Clear" && m.DeclaringType?.IsGenericType == true && m.DeclaringType.GetGenericTypeDefinition() == typeof(List<>) && m.DeclaringType.GetGenericArguments()[0].FullName is "Vintagestory.Server.ServerChunkWithCoord" or "Vintagestory.Server.ServerMapChunkWithCoord").ToString(CultureInfo.InvariantCulture);
        snapshot.Witnesses["unload.chunk.dispose.sites"] = sil.Count(i => i.Operand is MethodInfo m && m.Name == "Dispose" && m.DeclaringType == world).ToString(CultureInfo.InvariantCulture);
        foreach (MethodInfo method in world.GetMethods(All).Where(m => m.Name is "AddEntity" or "RemoveEntity"))
        {
            var il = Instructions(method);
            snapshot.Witnesses["membership." + method.Name + ".native.monitor"] = (il.Any(i => i.Code == OpCodes.Ldfld && Equals(i.Operand, Field(world, "packUnpackLock"))) && il.Any(i => i.Operand is MethodInfo call && call.DeclaringType == typeof(System.Threading.Monitor) && call.Name == "Enter")).ToString();
        }

        string[] behavior = { "OnGameTick", "OnPhysicsTick", "AfterPhysicsTick", "HandleRemotePhysics", "OnInteract", "OnReceivedClientPacket", "OnEntityReceiveDamage", "OnEntityDeath", "OnRenderFrame", "SlowTick", "CheckMultiply", "CheckGrowth", "Disobey", "PurgeIfOwnerless", "SpawnTameVariant", "TryRestoreTamedVariant", "ScheduleTamedVariantReplacement", "QueueCompanionBackpackRefresh" };
        string[] ai = { "StartExecute", "ShouldExecute", "ContinueExecute", "FinishExecute", "Notify", "OnEntityHurt", "OnStateChanged" };
        Type[] types;
        try { types = native.GetTypes(); } catch (ReflectionTypeLoadException failure) { types = failure.Types.OfType<Type>().ToArray(); }
        foreach (Type type in types.Where(t => !t.ContainsGenericParameters))
        {
            bool isBehavior = typeof(EntityBehavior).IsAssignableFrom(type), isAi = typeof(IAiTask).IsAssignableFrom(type);
            if (!isBehavior && !isAi) continue;
            foreach (MethodInfo method in type.GetMethods(All).Where(m => !m.IsAbstract && !m.ContainsGenericParameters))
                if (isBehavior && behavior.Contains(method.Name) || isAi && (ai.Contains(method.Name) || method.ReturnType == typeof(bool) && method.GetParameters().FirstOrDefault()?.ParameterType == typeof(Entity) && (method.Name.StartsWith("IsTargetableEntity", StringComparison.Ordinal) || method.Name == "CanSense"))) snapshot.Coverage.Add(MethodId(method));
        }
        snapshot.Coverage = snapshot.Coverage.Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        return snapshot;
    }

    private static Type GetGenericTypeDefinitionOrSelf(this Type type) => type.IsGenericType ? type.GetGenericTypeDefinition() : type;

    // A path reaching a transpiler removal before any stloc for its injected
    // request local is unsupported. Normal branch/switch edges are retained;
    // normalized method shapes additionally qualify complete EH control flow.
    internal static bool EveryPathStoresLocal(IReadOnlyList<Instruction> il, int local, System.Func<Instruction, bool> target)
    {
        var byOffset = il.Select((i, n) => (i.Offset, n)).ToDictionary(x => x.Offset, x => x.n);
        var seen = new HashSet<int>(); var pending = new Stack<int>(); pending.Push(0); bool found = false;
        while (pending.Count != 0)
        {
            int at = pending.Pop(); if (at >= il.Count || !seen.Add(at)) continue;
            Instruction item = il[at];
            bool store = (item.Code == OpCodes.Stloc || item.Code == OpCodes.Stloc_S) && Equals(item.Operand, local) || local <= 3 && item.Code.Name == "stloc." + local;
            if (store) continue;
            if (target(item)) return false;
            if (item.Code.FlowControl == FlowControl.Branch) { if (item.Operand is int b && byOffset.TryGetValue(b, out int next)) pending.Push(next); continue; }
            if (item.Code.FlowControl == FlowControl.Cond_Branch)
            {
                if (item.Operand is int b && byOffset.TryGetValue(b, out int next)) pending.Push(next);
                if (item.Operand is int[] choices) foreach (int b2 in choices) if (byOffset.TryGetValue(b2, out int n)) pending.Push(n);
            }
            if (item.Code.FlowControl is not (FlowControl.Return or FlowControl.Throw)) pending.Push(at + 1);
        }
        found = il.Any(target); return found;
    }

    internal static bool Compare(NativeChunkContractSnapshot expected, NativeChunkContractSnapshot actual, out string reason)
    {
        if (expected.Format != 1 || actual.Format != 1 || expected.GameVersion != "1.22.7" || actual.GameVersion != "1.22.7") { reason = "Unsupported native compatibility contract version"; return false; }
        foreach (var group in new[] { ("field signature", expected.Fields, actual.Fields), ("native control/data-flow shape", expected.Methods, actual.Methods), ("adapter witness", expected.Witnesses, actual.Witnesses) })
        {
            foreach (var row in group.Item2) if (!group.Item3.TryGetValue(row.Key, out string? value) || value != row.Value) { reason = "Changed or missing " + group.Item1 + ": " + row.Key; return false; }
            if (group.Item3.Keys.Any(key => !group.Item2.ContainsKey(key))) { reason = "Unrecognized " + group.Item1 + " surface"; return false; }
        }
        var allowed = expected.Coverage.ToHashSet(StringComparer.Ordinal);
        if (actual.Coverage.Any(id => !allowed.Contains(id))) { reason = "Unrecognized native behavior/AI patch surface"; return false; }
        reason = ""; return true;
    }
}
