using System;
using System.Collections.Generic;
using AnimalicaBodyTools.Systems;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace AnimalicaBodyTools.HarmonyPatches;

[HarmonyPatch(typeof(Entity), nameof(Entity.ReceiveDamage))]
internal static class WearableAttackDamagePatch
{
    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);
    private static readonly HashSet<string> DurabilityLogged = new(StringComparer.Ordinal);

    private static void Prefix(Entity __instance, DamageSource damageSource, ref float damage)
    {
        if (__instance?.World?.Side != EnumAppSide.Server || damageSource == null || damage <= 0)
        {
            return;
        }

        if (damageSource.Source != EnumDamageSource.Player
            || damageSource.CauseEntity != null
            || damageSource.Duration > TimeSpan.Zero
            || (damageSource.Type != EnumDamageType.BluntAttack
                && damageSource.Type != EnumDamageType.SlashingAttack
                && damageSource.Type != EnumDamageType.PiercingAttack))
        {
            return;
        }

        if (damageSource.SourceEntity is not EntityPlayer playerEntity || playerEntity.Player == null)
        {
            return;
        }

        if (ReferenceEquals(__instance, playerEntity))
        {
            return;
        }

        ItemSlot? activeSlot = playerEntity.Player.InventoryManager?.ActiveHotbarSlot;
        if (activeSlot == null)
        {
            return;
        }

        if (!WearableTargetedActionPatches.TryCreateVirtualDummySlot(
                playerEntity,
                activeSlot,
                ScoreAttackTool,
                out ItemStack virtualTool,
                out string sourceCode,
                out ItemSlot dummySlot,
                out ItemSlot sourceSlot,
                out float selectedScore))
        {
            return;
        }

        CollectibleObject? collectible = virtualTool.Collectible;
        if (collectible == null)
        {
            return;
        }

        float attackPower = collectible.GetAttackPower(virtualTool);
        bool shouldReceiveDamage = true;
        float virtualDamage = collectible.GetDamageToEntity(attackPower, __instance, virtualTool, ref shouldReceiveDamage);
        if (virtualDamage <= damage)
        {
            return;
        }

        float originalDamage = damage;
        damage = virtualDamage;
        damageSource.DamageTier = Math.Max(damageSource.DamageTier, collectible.GetToolTier(dummySlot));

        if (WearableTargetedActionPatches.DiagnosticsEnabled)
        {
            string key = $"{playerEntity.PlayerUID}:{sourceCode}:{virtualTool.Collectible.Code}:{__instance.Code}";
            if (WearableTargetedActionPatches.TryLogOnce(Logged, key))
            {
                __instance.World.Logger.VerboseDebug("[animalicabodytools] Wearable attack damage patch applied: player={0}, target={1}, source={2}, virtual={3}, originalDamage={4}, virtualDamage={5}, score={6}.",
                    playerEntity.Player.PlayerName,
                    __instance.Code,
                    sourceCode,
                    virtualTool.Collectible.Code,
                    originalDamage,
                    virtualDamage,
                    selectedScore);
            }
        }

        WearableTargetedActionPatches.TryDamageSourceTool(playerEntity, sourceSlot, sourceCode, "entity-attack:" + (virtualTool.Collectible.Code?.ToShortString() ?? "unknown"));

        if (WearableTargetedActionPatches.DiagnosticsEnabled)
        {
            string durabilityKey = $"{playerEntity.PlayerUID}:{sourceCode}:{virtualTool.Collectible.Code}";
            if (WearableTargetedActionPatches.TryLogOnce(DurabilityLogged, durabilityKey))
            {
                playerEntity.World.Logger.VerboseDebug("[animalicabodytools] Wearable attack durability charged in prefix: source={0}, virtual={1}, originalDamage={2}, virtualDamage={3}.",
                    sourceCode,
                    virtualTool.Collectible.Code,
                    originalDamage,
                    virtualDamage);
            }
        }
    }

    private static float ScoreAttackTool(ItemStack stack)
    {
        CollectibleObject? collectible = stack.Collectible;
        if (collectible == null)
        {
            return 0f;
        }

        // Buffable's damage hook expects a real target because it checks the
        // target world for critical hits. Selection only needs a target-free
        // score; the real damage calculation below supplies the target.
        return collectible.GetAttackPower(stack);
    }
}
