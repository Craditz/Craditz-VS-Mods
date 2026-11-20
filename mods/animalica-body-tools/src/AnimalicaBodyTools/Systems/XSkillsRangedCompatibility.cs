using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace AnimalicaBodyTools.Systems;

public sealed class XSkillsRangedCompatibility : ModSystem
{
    private const string PatchId = "animalicabodytools.xskills.ranged";
    private const string OwnerKey = PatchId + ".oreowner";
    private readonly List<MethodInfo> patched = new();
    private ICoreServerAPI? server;
    private Harmony? harmony;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        var provider = api.ModLoader.GetModSystem("XSkills.XSkills");
        if (provider == null) return;

        server = api;
        harmony = new Harmony(PatchId);
        var assembly = provider.GetType().Assembly;
        Install(assembly, "XSkills.XSkillsEntityBehavior", "OnDamage",
            [typeof(float), typeof(DamageSource)], nameof(ClassifyProjectile));
        if (Install(assembly, "XSkills.XSkillsExplodableBehavior", "OnBlockExploded",
            [typeof(IWorldAccessor), typeof(BlockPos), typeof(BlockPos), typeof(EnumBlastType), typeof(EnumHandling).MakeByRefType()],
            nameof(CreditOreBolt)))
        {
            // The server owns this callback, including in single-player with separate mod load contexts.
            api.ObjectCache[OwnerKey] = (System.Func<BlockPos, IPlayer?>)ActiveOreBoltOwner;
        }
    }

    private bool Install(Assembly provider, string type, string name, Type[] signature, string transpiler)
    {
        var method = provider.GetType(type)?.GetMethod(name, signature);
        try
        {
            if (method == null) throw new MissingMethodException(type, name);
            harmony!.Patch(method, transpiler: new HarmonyMethod(typeof(XSkillsRangedCompatibility), transpiler));
            patched.Add(method);
            return true;
        }
        catch (Exception ex)
        {
            if (method != null) harmony!.Unpatch(method, HarmonyPatchType.Transpiler, PatchId);
            server!.Logger.Warning("[animalicabodytools] Optional XSkills hook {0}.{1} unavailable: {2}", type, name, ex.Message);
            return false;
        }
    }

    public override void Dispose()
    {
        server?.ObjectCache.Remove(OwnerKey);
        foreach (var method in patched) harmony!.Unpatch(method, HarmonyPatchType.Transpiler, PatchId);
        patched.Clear();
    }

    private static IEnumerable<CodeInstruction> ClassifyProjectile(IEnumerable<CodeInstruction> instructions)
    {
        var tool = AccessTools.Field(typeof(CollectibleObject), nameof(CollectibleObject.Tool));
        int matches = 0;
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (!instruction.LoadsField(tool)) continue;
            matches++;
            yield return new CodeInstruction(OpCodes.Ldarg_2); // OnDamage's DamageSource
            yield return CodeInstruction.Call(typeof(XSkillsRangedCompatibility), nameof(ProjectileTool));
        }
        if (matches != 1) throw new InvalidOperationException("Expected one projectile tool lookup.");
    }

    private static EnumTool? ProjectileTool(EnumTool? original, DamageSource source) =>
        source.SourceEntity?.Code is { Domain: "animalicabodytools", Path: "shot-dart-blank"
            or "shot-bolt-ore" or "shot-bolt-stone" or "shot-bolt-scrap" } ? EnumTool.Bow : original;

    private static IEnumerable<CodeInstruction> CreditOreBolt(IEnumerable<CodeInstruction> instructions)
    {
        int matches = 0;
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.operand is not MethodInfo { Name: "GetPlayerCausingExplosion",
                DeclaringType.FullName: "XSkills.Mining", ReturnType: var result } || result != typeof(IPlayer)) continue;
            matches++;
            yield return new CodeInstruction(OpCodes.Ldarg_1); // world
            yield return new CodeInstruction(OpCodes.Ldarg_3); // explosionCenter
            yield return CodeInstruction.Call(typeof(XSkillsRangedCompatibility), nameof(BlockDropOwner));
        }
        if (matches != 1) throw new InvalidOperationException("Expected one explosion owner lookup.");
    }

    private static IPlayer? BlockDropOwner(IPlayer? original, IWorldAccessor world, BlockPos center) =>
        world.Api.ObjectCache.TryGetValue(OwnerKey, out var query) && query is System.Func<BlockPos, IPlayer?> owner
            ? owner(center) ?? original : original;

    private static IPlayer? ActiveOreBoltOwner(BlockPos center) =>
        BoltBlast.Current is { PreserveTerrain: false } blast && blast.Center == center
            && blast.Bolt.FiredBy is EntityPlayer player ? player.Player : null;
}
