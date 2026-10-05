using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common.Entities;

namespace AnimalicaCore;

internal static class AnimalicaSwimmingDepthPatch
{
    private const double DefaultSwimmingThreshold = 1.0;
    private static Func<Entity, bool>? shouldUseDefaultSwimmingDepth;

    public static void Start(Harmony harmony, Func<Entity, bool> predicate)
    {
        shouldUseDefaultSwimmingDepth = predicate;

        MethodInfo? getter = AccessTools.PropertyGetter(typeof(Entity), nameof(Entity.SwimmingOffsetY));
        if (getter == null)
        {
            return;
        }

        harmony.Patch(
            getter,
            postfix: new HarmonyMethod(typeof(AnimalicaSwimmingDepthPatch), nameof(EnsureDefaultSwimmingDepth))
        );
    }

    public static void Stop()
    {
        shouldUseDefaultSwimmingDepth = null;
    }

    private static void EnsureDefaultSwimmingDepth(Entity __instance, ref double __result)
    {
        if (shouldUseDefaultSwimmingDepth?.Invoke(__instance) == true)
        {
            __result = Math.Max(__result, DefaultSwimmingThreshold);
        }
    }
}
