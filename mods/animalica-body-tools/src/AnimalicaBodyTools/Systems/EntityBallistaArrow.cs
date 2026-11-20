using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace AnimalicaBodyTools.Systems;

public sealed class EntityBallistaArrow : EntityProjectile
{
    internal const string SlowUntil = "animalicaBallistaSlowUntil";
    private const string FirstHit = "animalicaBallistaFirstTarget";

    protected override bool CanHitTarget(Entity target) =>
        target.EntityId != FiredBy?.EntityId && target.EntityId != Attributes.GetLong(FirstHit, -1)
        && base.CanHitTarget(target);

    protected override void ImpactOnEntity(Entity target)
    {
        if (!Alive || World.Side != EnumAppSide.Server || !CanHitTarget(target)) return;
        if (Attributes.HasAttribute(FirstHit) || !CanDealDamage(target))
        {
            base.ImpactOnEntity(target);
            return;
        }
        // Keep flying after the first target, even when this arrow cannot be recovered.
        Attributes.SetLong(FirstHit, target.EntityId);
        Collectible = World.Rand.NextDouble() < DropOnImpactChance;
        DealDamage(target);
        if (!entitiesHit.Contains(target.EntityId)) entitiesHit.Add(target.EntityId);
        EntityHit = true;
        World.PlaySoundAt(new AssetLocation("game:sounds/arrow-impact"), this);
    }

    protected override void DamageProjectile(Entity target)
    {
        if (!Collectible) Die();
    }

    protected override bool DealDamage(Entity target)
    {
        bool damaged = base.DealDamage(target);
        if (damaged && target.Alive && target is EntityAgent)
        {
            var clock = World.Calendar;
            target.WatchedAttributes.SetDouble(SlowUntil,
                clock.TotalHours + 2.0 * clock.SpeedOfTime * clock.CalendarSpeedMul / 3600.0);
        }
        return damaged;
    }
}

[HarmonyPatch(typeof(EntityAgent), nameof(EntityAgent.GetWalkSpeedMultiplier))]
internal static class CrossbowMovementPatch
{
    private static void Postfix(EntityAgent __instance, ref double __result)
    {
        var attrs = __instance.WatchedAttributes;
        if (__instance is EntityPlayer) __result *= attrs.GetFloat(RangedCrossbowSystem.SpeedFactor, 1f);
        __result *= 1 - System.Math.Max(attrs.GetFloat(HuntingEffects.Slow), attrs.GetFloat(HuntingEffects.DartSlow));
        double until = attrs.GetDouble(EntityBallistaArrow.SlowUntil);
        if (until > 0 && until > __instance.World.Calendar.TotalHours) __result *= 0.85;
    }
}

