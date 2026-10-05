using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// Keeps companions that are away on an expedition out of the player's
/// selection and attachment interaction pipeline. Some supported animal mods
/// expose wearable attachment-point boxes as part of the parent entity, so
/// hiding the renderer alone is not sufficient.
/// </summary>
[HarmonyPatch(typeof(Entity), nameof(Entity.IntersectsRay))]
internal static class FeralKinshipExpeditionSelectionPatch
{
    private static bool Prefix(
        Entity __instance,
        ref double intersectionDistance,
        ref int selectionBoxIndex,
        ref bool __result)
    {
        if (__instance.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>() == null
            || !FeralKinshipCompanionSystem.IsFoxAwayFromWorld(__instance))
        {
            return true;
        }

        intersectionDistance = double.MaxValue;
        selectionBoxIndex = 0;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(EntityBehaviorAttachable), nameof(EntityBehaviorAttachable.OnInteract))]
internal static class FeralKinshipExpeditionAttachInteractionPatch
{
    private static bool Prefix(EntityBehaviorAttachable __instance, ref EnumHandling handled)
    {
        if (__instance.entity.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>() == null
            || !FeralKinshipCompanionSystem.IsFoxAwayFromWorld(__instance.entity))
        {
            return true;
        }

        handled = EnumHandling.PreventSubsequent;
        return false;
    }
}

[HarmonyPatch(typeof(EntityBehaviorSelectionBoxes), nameof(EntityBehaviorSelectionBoxes.OnRenderFrame))]
internal static class FeralKinshipExpeditionSelectionRendererPatch
{
    private static bool Prefix(EntityBehaviorSelectionBoxes __instance)
    {
        if (__instance.entity.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>() == null
            || !FeralKinshipCompanionSystem.IsFoxAwayFromWorld(__instance.entity))
        {
            return true;
        }

        return false;
    }
}
