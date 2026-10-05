using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;

[HarmonyPatch(typeof(Entity), nameof(Entity.OnHurt), new[] { typeof(DamageSource), typeof(float) })]
internal static class FeralKinshipExperiencePatch
{
    [HarmonyPostfix]
    private static void AfterEntityHurt(Entity __instance, DamageSource? __0, float __1)
    {
        // Vanilla deliberately supplies a null source when a server health
        // change is mirrored through the client's synced onHurt attribute.
        // Reject client callbacks before examining the server-only source.
        if (__instance.Api?.Side != EnumAppSide.Server
            || __1 <= 0f
            || __0 == null
            || __0.Type == EnumDamageType.Heal)
        {
            return;
        }

        __instance.Api.ModLoader
            .GetModSystem<FeralKinshipCompanionSystem>()?
            .RecordProgressionCombatDamage(__instance, __0, __1);
    }
}
