#nullable enable

using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;

/// <summary>
/// Gives the ambient-life system a lightweight signal when an entity lands a
/// physical attack. The reaction itself remains server-side, capped, and
/// cooldown-gated by the Companion system.
/// </summary>
[HarmonyPatch(typeof(EntityAgent), nameof(EntityAgent.DidAttack), new[] { typeof(DamageSource), typeof(EntityAgent) })]
internal static class FeralKinshipAmbientStartlePatch
{
    private static void Postfix(EntityAgent __instance, DamageSource source, EntityAgent targetEntity)
    {
        if (__instance?.Api.Side != EnumAppSide.Server)
        {
            return;
        }

        FeralKinshipCompanionSystem system = __instance.Api.ModLoader
            .GetModSystem<FeralKinshipCompanionSystem>();
        system.NotifyAmbientStartleFromAttack(__instance, targetEntity, source);
    }
}
