using HarmonyLib;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// A Companion that is returning home is no longer a valid target for the
/// ordinary target-acquisition paths used by vanilla hostile AI. The system
/// also clears active hostile tasks when Return Home begins; these guards keep
/// the same enemy from immediately selecting the Companion again next tick.
/// </summary>
internal static class FeralKinshipReturningTargetGuard
{
    private static readonly FieldInfo? TaskEntityField = AccessTools.Field(typeof(AiTaskBase), "entity");
    internal static bool BlocksTarget(Entity target)
    {
        return FeralKinshipCompanionSystem.IsTamedFox(target)
            && FeralKinshipCompanionSystem.GetCompanionActivityMode(target)
                == CompanionActivityMode.ReturnHome;
    }

    internal static bool BlocksOwnedJuvenileTarget(AiTaskBaseTargetable task, Entity target)
    {
        if (target is not EntityPlayer player) return false;
        EntityAgent? seeker = TaskEntityField?.GetValue(task) as EntityAgent;
        if (seeker == null) return false;
        if (!FeralKinshipCompanionSystem.IsCompanionJuvenile(seeker)) return false;
        string ownerUid = FeralKinshipCompanionSystem.GetCompanionOwnerUid(seeker);
        return !string.IsNullOrWhiteSpace(ownerUid)
            && string.Equals(ownerUid, player.PlayerUID, System.StringComparison.Ordinal);
    }
}

[HarmonyPatch(typeof(AiTaskBaseTargetable), nameof(AiTaskBaseTargetable.IsTargetableEntity))]
internal static class FeralKinshipReturningTargetPatch
{
    private static bool Prefix(AiTaskBaseTargetable __instance, Entity e, ref bool __result)
    {
        if ((!__instance.AggressiveTargeting || !FeralKinshipReturningTargetGuard.BlocksTarget(e))
            && !FeralKinshipReturningTargetGuard.BlocksOwnedJuvenileTarget(__instance, e))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(AiTaskBaseTargetable), nameof(AiTaskBaseTargetable.IsTargetableEntityNoTagsAll))]
internal static class FeralKinshipReturningTargetNoTagsAllPatch
{
    private static bool Prefix(AiTaskBaseTargetable __instance, Entity e, ref bool __result)
    {
        if ((!__instance.AggressiveTargeting || !FeralKinshipReturningTargetGuard.BlocksTarget(e))
            && !FeralKinshipReturningTargetGuard.BlocksOwnedJuvenileTarget(__instance, e))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(AiTaskBaseTargetable), nameof(AiTaskBaseTargetable.IsTargetableEntityNoTagsNoAll))]
internal static class FeralKinshipReturningTargetNoTagsNoAllPatch
{
    private static bool Prefix(AiTaskBaseTargetable __instance, Entity e, ref bool __result)
    {
        if ((!__instance.AggressiveTargeting || !FeralKinshipReturningTargetGuard.BlocksTarget(e))
            && !FeralKinshipReturningTargetGuard.BlocksOwnedJuvenileTarget(__instance, e))
        {
            return true;
        }

        __result = false;
        return false;
    }
}
