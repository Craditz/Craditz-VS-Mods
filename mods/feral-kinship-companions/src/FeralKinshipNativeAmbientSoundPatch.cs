#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;

/// <summary>Only the native random-idle call site; never a sound-family-wide mute.</summary>
internal static class FeralKinshipNativeAmbientSoundPatch
{
    internal static void Install(Harmony harmony)
        => harmony.Patch(AccessTools.DeclaredMethod(typeof(Entity), nameof(Entity.OnGameTick)),
            transpiler: new HarmonyMethod(typeof(FeralKinshipNativeAmbientSoundPatch), nameof(Transpiler)));

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> code = instructions.ToList();
        MethodInfo native = AccessTools.DeclaredMethod(typeof(Entity), nameof(Entity.PlayEntitySound));
        MethodInfo replacement = AccessTools.DeclaredMethod(typeof(FeralKinshipNativeAmbientSoundPatch), nameof(PlayNativeIdle));
        int replacements = 0;
        for (int i = 0; i < code.Count; i++)
        {
            if (!code[i].Calls(native)) continue;
            // Entity.OnGameTick has exactly one sound call, with constant idle.
            // Reject a changed engine shape rather than intercept another cue.
            if (i < 2 || code[i - 2].opcode != OpCodes.Ldstr || !Equals(code[i - 2].operand, "idle"))
                throw new InvalidOperationException("Native random idle sound call shape changed.");
            code[i].opcode = OpCodes.Call;
            code[i].operand = replacement;
            replacements++;
        }
        if (replacements != 1) throw new InvalidOperationException("Expected one native random idle sound call.");
        return code;
    }

    private static void PlayNativeIdle(Entity entity, string type, IPlayer? dualCallByPlayer)
    {
        if (entity.Api.Side != EnumAppSide.Client || type != "idle"
            || !FeralKinshipCompanionSystem.IsTamedFox(entity)
            || string.IsNullOrWhiteSpace(FeralKinshipCompanionSystem.GetCompanionOwnerUid(entity)))
        {
            entity.PlayEntitySound(type, dualCallByPlayer);
            return;
        }
        if (entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>().TryPlayNativeAmbientSound(entity))
            entity.PlayEntitySound(type, dualCallByPlayer);
    }
}
