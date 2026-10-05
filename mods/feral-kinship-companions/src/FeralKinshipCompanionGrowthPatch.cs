#nullable enable

using HarmonyLib;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// Keeps vanilla's juvenile timer, collision guard, and growth event. Only a
/// child explicitly born by Companions is redirected to its persisted core
/// tame adult code; every ordinary and source-mod juvenile remains untouched.
/// </summary>
[HarmonyPatch(typeof(EntityBehaviorGrow), "BecomeAdult")]
internal static class FeralKinshipCompanionGrowthPatch
{
    [HarmonyPrefix]
    private static void Prefix(EntityBehaviorGrow __instance, ref Entity adult)
    {
        Entity? child = __instance?.entity;
        if (child == null || adult == null)
        {
            return;
        }

        child.Api.ModLoader
            .GetModSystem<FeralKinshipCompanionSystem>()?
            .TryPrepareCompanionAdult(child, ref adult);
    }

    [HarmonyPostfix]
    private static void Postfix(EntityBehaviorGrow __instance, Entity adult)
    {
        Entity? child = __instance?.entity;
        if (child == null || adult == null)
        {
            return;
        }

        child.Api.ModLoader
            .GetModSystem<FeralKinshipCompanionSystem>()?
            .FinalizeCompanionAdultGrowth(adult);
    }
}
