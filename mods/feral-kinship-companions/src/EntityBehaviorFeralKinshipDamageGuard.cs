#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;

/// <summary>
/// Runs before mortality and health so damage can be adjusted or rejected
/// before either behavior consumes it. Companion AI installation deliberately
/// remains in the later social behavior, after taskai has initialized.
/// </summary>
public sealed class EntityBehaviorFeralKinshipDamageGuard : EntityBehavior
{
    public EntityBehaviorFeralKinshipDamageGuard(Entity entity) : base(entity)
    {
    }

    public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
    {
        base.OnEntityReceiveDamage(damageSource, ref damage);
        if (entity.Api.Side == EnumAppSide.Server
            && FeralKinshipCompanionSystem.IsGuideFox(entity)
            && damageSource.Type != EnumDamageType.Heal
            && damage > 0f)
        {
            damage = 0f;
            damageSource.CauseEntity = null;
            damageSource.SourceEntity = null;
            return;
        }

        bool isCompanionJuvenile = FeralKinshipCompanionSystem.IsCompanionJuvenile(entity);
        if (entity.Api.Side != EnumAppSide.Server
            || (!FeralKinshipCompanionSystem.IsTamedFox(entity) && !isCompanionJuvenile)
            || damageSource.Type == EnumDamageType.Heal)
        {
            return;
        }

        if (damage <= 0f) return;

        Entity? attacker = damageSource.GetCauseEntity() ?? damageSource.SourceEntity;
        string ownerUid = FeralKinshipCompanionSystem.GetCompanionOwnerUid(entity);
        if (attacker is EntityPlayer player
            && !string.IsNullOrWhiteSpace(ownerUid)
            && string.Equals(ownerUid, player.PlayerUID, StringComparison.Ordinal))
        {
            // Companions, including newly born juveniles, are not valid
            // targets for their own owner. The vanilla baby entity does not
            // have the adult tame entity's built-in protection, so enforce the
            // ownership rule here at the shared companion damage boundary.
            damage = 0f;
            damageSource.CauseEntity = null;
            damageSource.SourceEntity = null;
            return;
        }

        FeralKinshipCompanionSystem system = entity.Api.ModLoader
            .GetModSystem<FeralKinshipCompanionSystem>();
        int unshakenRank = FeralKinshipCompanionSystem.GetFoxPerkRank(entity, "unshaken");
        if (unshakenRank > 0)
        {
            damageSource.KnockbackStrength *= Math.Max(0f, 1f - 0.75f * unshakenRank);
        }

        system.AdjustFoxIncomingDamage(entity, damageSource, ref damage);
        if (damage <= 0f) return;

        system.RecordCompanionAttacker(entity, attacker);
        system.TryTriggerAutomaticRetreat(entity, attacker, damage);
        system.NotifyFoxDamaged(entity, damage);
    }

    public override string PropertyName() => FeralKinshipCompanionSystem.DamageGuardBehaviorCode;
}
