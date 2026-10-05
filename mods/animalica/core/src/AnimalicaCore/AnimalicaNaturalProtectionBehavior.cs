using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace AnimalicaCore;

public sealed class AnimalicaNaturalProtectionBehavior(Entity entity) : EntityBehavior(entity)
{
    public const string BehaviorCode = "animalicanaturalprotection";
    public const string StatCode = "animalicaNaturalProtection";
    public const string ReductionStatCode = "animalicaNaturalDamageReduction";

    public override string PropertyName()
    {
        return BehaviorCode;
    }

    public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
    {
        if (entity.World.Side != EnumAppSide.Server || damage <= 0 || !IsPhysicalDamage(damageSource.Type))
        {
            return;
        }

        // Weighted-sum stats have an implicit base value of 1. Trait data stores the bonuses themselves.
        float reduction = Math.Max(0, entity.Stats.GetBlended(ReductionStatCode) - 1);
        reduction = Math.Min(0.9f, reduction);
        damage *= 1 - reduction;

        float protection = Math.Max(0, entity.Stats.GetBlended(StatCode) - 1);
        damage = Math.Max(0, damage - protection);
    }

    private static bool IsPhysicalDamage(EnumDamageType damageType)
    {
        return damageType is EnumDamageType.BluntAttack
            or EnumDamageType.SlashingAttack
            or EnumDamageType.PiercingAttack;
    }
}
