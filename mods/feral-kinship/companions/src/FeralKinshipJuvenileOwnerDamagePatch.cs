#nullable enable

using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;

/// <summary>
/// Final owner-safety boundary for player-owned juveniles. Runtime-added
/// behaviors can be ordered after health on source-mod entities, so reject the
/// hit before any behavior receives it. This does not alter juvenile tasks,
/// menus, commands, following, growth, or interactions.
/// </summary>
[HarmonyPatch(typeof(Entity), nameof(Entity.ReceiveDamage))]
internal static class FeralKinshipJuvenileOwnerDamagePatch
{
    private static bool Prefix(Entity __instance, DamageSource damageSource, ref bool __result)
    {
        if (__instance.Api.Side != EnumAppSide.Server
            || damageSource.Type == EnumDamageType.Heal
            || !FeralKinshipCompanionSystem.IsCompanionJuvenile(__instance)) return true;

        Entity? attacker = damageSource.GetCauseEntity() ?? damageSource.SourceEntity;
        string ownerUid = FeralKinshipCompanionSystem.GetCompanionOwnerUid(__instance);
        if (attacker is not EntityPlayer player
            || string.IsNullOrWhiteSpace(ownerUid)
            || !string.Equals(ownerUid, player.PlayerUID, StringComparison.Ordinal)) return true;

        __result = false;
        return false;
    }
}
