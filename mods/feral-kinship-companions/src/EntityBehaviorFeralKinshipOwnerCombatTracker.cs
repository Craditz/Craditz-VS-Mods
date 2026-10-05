#nullable enable

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;

/// <summary>
/// A server-only, session-local observer attached to an online owner. It gives
/// Protect and Assist an authoritative source without making player combat
/// state part of the companion save ledger.
/// </summary>
internal sealed class EntityBehaviorFeralKinshipOwnerCombatTracker : EntityBehavior
{
    private readonly FeralKinshipCompanionSystem system;

    public EntityBehaviorFeralKinshipOwnerCombatTracker(Entity entity, FeralKinshipCompanionSystem system)
        : base(entity)
    {
        this.system = system;
    }

    public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
    {
        base.OnEntityReceiveDamage(damageSource, ref damage);
        if (damage > 0f && damageSource.Type != EnumDamageType.Heal)
        {
            system.RecordOwnerAttacker(entity, damageSource.GetCauseEntity());
        }
    }

    public override void DidAttack(DamageSource source, EntityAgent targetEntity, ref EnumHandling handled)
    {
        base.DidAttack(source, targetEntity, ref handled);
        system.RecordOwnerAttackTarget(entity, targetEntity);
    }

    public override string PropertyName() => "feralkinshipownercombattracker";
}
