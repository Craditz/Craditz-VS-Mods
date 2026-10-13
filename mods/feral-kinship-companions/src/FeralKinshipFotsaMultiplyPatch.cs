#nullable enable
using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>Arbitrates only Companions-owned FOTSA native reproduction.</summary>
[HarmonyPatch(typeof(EntityBehaviorMultiply), "CheckMultiply")]
internal static class FeralKinshipFotsaMultiplyPatch
{
    private static readonly FieldInfo CallbackId = AccessTools.Field(typeof(EntityBehaviorMultiply), "callbackId");
    private static readonly MethodInfo CheckMultiply = AccessTools.Method(typeof(EntityBehaviorMultiply), "CheckMultiply");

    internal static bool ShouldRunNative(Entity entity)
    {
        if (!CompanionFotsaBreedingCatalog.IsFotsa(entity.Code)
            || entity.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>() == null
            || !FeralKinshipCompanionSystem.IsTamedFox(entity)
            || string.IsNullOrWhiteSpace(FeralKinshipCompanionSystem.GetCompanionOwnerUid(entity))) return true;

        var native = entity.WatchedAttributes.GetTreeAttribute("multiply");
        // Grandfather existing native pregnancies; a mirrored custom pregnancy
        // or legacy dual-pregnancy conflict cannot run a second birth callback.
        return native?.GetBool("isPregnant") == true
            && !FeralKinshipCompanionSystem.IsCompanionPregnant(entity);
    }

    [HarmonyPrefix]
    private static bool Prefix(EntityBehaviorMultiply __instance)
    {
        if (ShouldRunNative(__instance.entity)) return true;
        // The original callback reschedules itself. Skipping it must preserve
        // that lifecycle so ownership changes and grandfather pregnancies can
        // resume normally; death still stops scheduling just like vanilla.
        long id = __instance.entity.Alive
            ? __instance.entity.World.RegisterCallback(dt => CheckMultiply.Invoke(__instance, new object[] { dt }), 3000)
            : 0;
        CallbackId.SetValue(__instance, id);
        return false;
    }
}
