#nullable enable
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
namespace FeralKinshipCompanions;

[HarmonyPatch(typeof(BlockEntityTrough), nameof(BlockEntityTrough.Initialize))]
internal static class CompanionTroughInitializePatch
{
    [HarmonyPrefix] static void Before(BlockEntityTrough __instance, ICoreAPI api)
    {
        // Attach before native initialization, rather than change its behavior
        // list on the first live feed while chunk serialization may be running.
        if (api.Side == EnumAppSide.Server && CompanionTroughCare.Supported(__instance)) CompanionTroughCare.Attach(__instance);
    }
}
[HarmonyPatch]
internal static class CompanionTroughManualPatch
{
    static MethodBase TargetMethod() => AccessTools.DeclaredMethod(typeof(BlockEntityTrough), "OnInteract");
    [HarmonyPrefix] static void Before(BlockEntityTrough __instance, IPlayer byPlayer, out CompanionTroughCare.ManualFeed? __state)
        => __state = CompanionTroughCare.BeforeManual(__instance, byPlayer);
    [HarmonyPostfix] static void After(BlockEntityTrough __instance, bool __result, CompanionTroughCare.ManualFeed? __state)
        => CompanionTroughCare.AfterManual(__instance, __result, __state);
    [HarmonyFinalizer] static System.Exception? AfterFailure(BlockEntityTrough __instance, System.Exception? __exception, CompanionTroughCare.ManualFeed? __state)
    {
        // A native notification may throw after the actual insertion. The same
        // finite source/target delta checks prove whether the feed completed.
        if (__exception != null) CompanionTroughCare.AfterManual(__instance, true, __state);
        return __exception;
    }
}
[HarmonyPatch(typeof(BlockEntityTrough), nameof(BlockEntityTrough.FromTreeAttributes))]
internal static class CompanionTroughReadPatch
{
    [HarmonyPostfix] static void After(BlockEntityTrough __instance, ITreeAttribute tree, IWorldAccessor worldForResolving)
        => CompanionTroughCare.Read(__instance, tree, worldForResolving);
}
