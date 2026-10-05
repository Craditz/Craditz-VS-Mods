using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// Preserves vanilla's mortal-wound recovery transition while recognizing
/// full health restores made by mods that set EntityBehaviorHealth.Health
/// directly instead of sending DamageType.Heal.
/// </summary>
internal static class FeralKinshipMortalWoundRecoveryPatch
{
    private const string WoundMaximumHealthKey = "feralKinshipMortalWoundMaximumHealth";
    private const string WoundGenerationKey = "feralKinshipMortalWoundGeneration";
    private static MethodInfo? recoverMethod;
    private static ILogger? logger;

    public static void Install(Harmony harmony, ILogger modLogger)
    {
        logger = modLogger;
        Type behaviorType = typeof(EntityBehaviorMortallyWoundable);
        MethodInfo? healthStateSetter = AccessTools.PropertySetter(behaviorType, nameof(EntityBehaviorMortallyWoundable.HealthState));
        MethodInfo? onGameTick = AccessTools.Method(behaviorType, nameof(EntityBehaviorMortallyWoundable.OnGameTick));
        recoverMethod = AccessTools.Method(behaviorType, "recover");

        if (healthStateSetter == null || onGameTick == null || recoverMethod == null
            || recoverMethod.ReturnType != typeof(void)
            || recoverMethod.GetParameters().Length != 0)
        {
            logger.Error(
                "[FeralKinshipCompanions] External mortal-wound recovery hook was not installed: " +
                "the supported vanilla recovery methods were not found. Companions will retain vanilla healing behavior."
            );
            recoverMethod = null;
            return;
        }

        harmony.Patch(
            healthStateSetter,
            prefix: new HarmonyMethod(typeof(FeralKinshipMortalWoundRecoveryPatch), nameof(BeforeHealthStateChange)),
            postfix: new HarmonyMethod(typeof(FeralKinshipMortalWoundRecoveryPatch), nameof(AfterHealthStateChange)));
        harmony.Patch(
            onGameTick,
            prefix: new HarmonyMethod(typeof(FeralKinshipMortalWoundRecoveryPatch), nameof(BeforeMortalWoundTick)));
        harmony.Patch(
            recoverMethod,
            postfix: new HarmonyMethod(typeof(FeralKinshipMortalWoundRecoveryPatch), nameof(AfterVanillaRecovery)));
    }

    public static void RememberWoundMaximumHealth(Entity entity, EntityBehaviorHealth health)
    {
        if (!IsCompanionServerEntity(entity)) return;
        float maximumHealth = health.MaxHealth;
        if (!float.IsFinite(maximumHealth) || maximumHealth <= 0f) return;

        entity.WatchedAttributes.SetFloat(WoundMaximumHealthKey, maximumHealth);
        entity.WatchedAttributes.MarkPathDirty(WoundMaximumHealthKey);
    }

    public static void StartNewWound(Entity entity, EntityBehaviorHealth health)
    {
        if (!IsCompanionServerEntity(entity)) return;
        RememberWoundMaximumHealth(entity, health);
        entity.WatchedAttributes.SetLong(
            WoundGenerationKey,
            entity.WatchedAttributes.GetLong(WoundGenerationKey, 0L) + 1L);
        entity.WatchedAttributes.MarkPathDirty(WoundGenerationKey);
    }

    public static long GetWoundGeneration(Entity entity)
    {
        return entity.WatchedAttributes.GetLong(WoundGenerationKey, 0L);
    }

    private static void BeforeHealthStateChange(EntityBehaviorMortallyWoundable __instance, out int __state)
    {
        __state = (int)__instance.HealthState;
    }

    private static void AfterHealthStateChange(EntityBehaviorMortallyWoundable __instance, int __state)
    {
        if (__state == (int)EnumEntityHealthState.MortallyWounded
            || __instance.HealthState != EnumEntityHealthState.MortallyWounded)
        {
            return;
        }

        EntityBehaviorHealth? health = __instance.entity.GetBehavior<EntityBehaviorHealth>();
        if (health != null) StartNewWound(__instance.entity, health);
    }

    private static bool BeforeMortalWoundTick(EntityBehaviorMortallyWoundable __instance)
    {
        Entity entity = __instance.entity;
        if (!IsCompanionServerEntity(entity)
            || !entity.Alive
            || entity.ShouldDespawn
            || __instance.HealthState != EnumEntityHealthState.MortallyWounded)
        {
            return true;
        }

        EntityBehaviorHealth? health = entity.GetBehavior<EntityBehaviorHealth>();
        if (health == null) return true;

        if (!HasRememberedWoundMaximum(entity))
        {
            // Seed old wounded saves or a wound created outside the vanilla
            // damage callback. The saved value is then stable across reloads.
            RememberWoundMaximumHealth(entity, health);
        }

        float woundMaximum = entity.WatchedAttributes.GetFloat(WoundMaximumHealthKey, 0f);
        if (!MortalWoundRecoveryPolicy.IsHealthSufficient(health.Health, woundMaximum, health.MaxHealth))
        {
            return true;
        }

        MethodInfo? method = recoverMethod;
        if (method == null) return true;
        try
        {
            // Use vanilla's own Recovering state, animation, regeneration,
            // attacker reset, and Recovering-to-Normal tick transition.
            method.Invoke(__instance, null);
            __instance.HealthHealed = 0d;
            entity.WatchedAttributes.MarkPathDirty("healthHealed");
        }
        catch (Exception exception)
        {
            logger?.Error(
                "[FeralKinshipCompanions] Could not resolve a fully healed mortal wound on {0}: {1}",
                entity.Code,
                exception.GetBaseException().Message
            );
        }

        return true;
    }

    private static void AfterVanillaRecovery(EntityBehaviorMortallyWoundable __instance)
    {
        Entity entity = __instance.entity;
        if (!IsCompanionServerEntity(entity)) return;

        // The threshold belongs to one wound only. Keep the generation so any
        // queued persistence callback from this wound can still be validated.
        entity.WatchedAttributes.RemoveAttribute(WoundMaximumHealthKey);
        entity.WatchedAttributes.MarkPathDirty(WoundMaximumHealthKey);

        entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()?
            .ClearMortalWoundRecoveryCountdown(entity);
    }

    private static bool HasRememberedWoundMaximum(Entity entity)
    {
        float maximum = entity.WatchedAttributes.GetFloat(WoundMaximumHealthKey, 0f);
        return float.IsFinite(maximum) && maximum > 0f;
    }

    private static bool IsCompanionServerEntity(Entity entity)
    {
        return entity.Api.Side == EnumAppSide.Server
            && entity.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>() != null;
    }
}
