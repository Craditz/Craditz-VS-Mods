using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using AnimalicaBodyTools.Items;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace AnimalicaBodyTools.HarmonyPatches;

// Wearable plays an underflow sound before clamping damage, even though it
// suppresses destruction. Replace only that call, leaving ordinary wearables
// and calls outside our positive, destroy-enabled Body Tools damage untouched.
[HarmonyPatch(typeof(CollectibleBehaviorWearable), nameof(CollectibleBehaviorWearable.OnDamageItem))]
internal static class BodyToolWearableBreakSoundPatch
{
    private static readonly MethodInfo OriginalSound = AccessTools.Method(typeof(IWorldAccessor), "PlaySoundAt", new[]
    {
        typeof(AssetLocation), typeof(double), typeof(double), typeof(double), typeof(IPlayer), typeof(bool), typeof(float), typeof(float)
    });

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => ReplaceSoundCall(
        instructions, OpCodes.Ldarg_3, AccessTools.Method(typeof(BodyToolWearableBreakSoundPatch), nameof(PlayWearableSound)), "Wearable.OnDamageItem");

    internal static IEnumerable<CodeInstruction> ReplaceSoundCall(IEnumerable<CodeInstruction> instructions, OpCode extraArgument, MethodInfo wrapper, string source)
    {
        // Harmony chains a transpiler again when it is registered repeatedly.
        // Accept one original OR one already-wrapped call, without appending a
        // second argument load. Match equivalent wrappers across same-build mod
        // load contexts by module/token; do not relax missing/ambiguous sites.
        var code = instructions.Select(instruction => new CodeInstruction(instruction)).ToArray();
        bool IsWrapper(CodeInstruction instruction) => instruction.operand is MethodInfo method
            && (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
            && method.MetadataToken == wrapper.MetadataToken && method.Module.ModuleVersionId == wrapper.Module.ModuleVersionId;
        int original = code.Count(instruction => instruction.Calls(OriginalSound));
        int wrapped = code.Count(IsWrapper);
        if (original + wrapped != 1)
            throw new InvalidOperationException("Expected one original or already-wrapped " + source + " break-sound call; found " + (original + wrapped));
        if (wrapped == 1) return code;

        var result = new List<CodeInstruction>();
        foreach (CodeInstruction instruction in code)
        {
            if (instruction.Calls(OriginalSound))
            {
                var slot = new CodeInstruction(extraArgument);
                slot.labels.AddRange(instruction.labels);
                instruction.labels.Clear();
                slot.blocks.AddRange(instruction.blocks);
                instruction.blocks.Clear();
                result.Add(slot);
                instruction.opcode = OpCodes.Call;
                instruction.operand = wrapper;
            }
            result.Add(instruction);
        }
        return result;
    }

    private static void PlayWearableSound(IWorldAccessor world, AssetLocation location, double x, double y, double z,
        IPlayer? dualCallByPlayer, bool randomizePitch, float range, float volume, ItemSlot slot)
    {
        if (BodyToolDamagePatch.OwnsWearableSound(world, slot)) return;
        world.PlaySoundAt(location, x, y, z, dualCallByPlayer, randomizePitch, range, volume);
    }
}

[HarmonyPatch(typeof(ItemHoe), nameof(ItemHoe.DoTill))]
internal static class BodyToolHoeBreakSoundPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => BodyToolWearableBreakSoundPatch.ReplaceSoundCall(
        instructions, OpCodes.Ldarg_0, AccessTools.Method(typeof(BodyToolHoeBreakSoundPatch), nameof(PlayHoeBreakSound)), "ItemHoe.DoTill");

    private static void PlayHoeBreakSound(IWorldAccessor world, AssetLocation location, double x, double y, double z,
        IPlayer? dualCallByPlayer, bool randomizePitch, float range, float volume, ItemHoe hoe)
    {
        // ItemHoe adds its own empty-slot sound after DamageItem has returned.
        // Body Tools destruction already emitted the single server break event.
        if (AnimalicaBodyTools.Systems.BodyToolDurability.IsCultivatingClaws(hoe)) return;
        world.PlaySoundAt(location, x, y, z, dualCallByPlayer, randomizePitch, range, volume);
    }
}
