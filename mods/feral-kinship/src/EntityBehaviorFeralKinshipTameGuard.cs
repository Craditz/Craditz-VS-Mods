#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace FeralKinship
{
/// <summary>
/// Universal safety rules for Kinship tames. This runs before mortality and
/// health so a downed animal cannot be killed by a follow-up hit and one
/// owner's tames cannot directly damage each other.
/// </summary>
public sealed class EntityBehaviorFeralKinshipTameGuard : EntityBehavior
{
    public const string BehaviorCode = "feralkinshiptameguard";

    public EntityBehaviorFeralKinshipTameGuard(Entity entity) : base(entity)
    {
    }

    public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
    {
        base.OnEntityReceiveDamage(damageSource, ref damage);
        if (entity.Api.Side != EnumAppSide.Server
            || damageSource.Type == EnumDamageType.Heal
            || damage <= 0f)
        {
            return;
        }

        // Companion juveniles are not valid targets for their owner. Keep the
        // check owner-specific so unrelated wild babies remain ordinary
        // animals and environmental or hostile-creature damage still works.
        Entity? attacker = damageSource.GetCauseEntity() ?? damageSource.SourceEntity;
        string ownerUid = GetOwnerUid(entity);
        if (entity.WatchedAttributes.GetTreeAttribute("domesticationstatus")?
                .GetBool("feralKinshipJuvenile", false) == true
            && attacker is EntityPlayer player
            && !string.IsNullOrWhiteSpace(ownerUid)
            && string.Equals(ownerUid, player.PlayerUID, StringComparison.Ordinal))
        {
            damage = 0f;
            damageSource.CauseEntity = null;
            damageSource.SourceEntity = null;
            return;
        }

        int healthState = entity.WatchedAttributes.GetInt("healthState", 0);
        if (healthState is 1 or 2)
        {
            damage = 0f;
            return;
        }

        if (attacker == null || !IsDomesticated(attacker)) return;

        string attackerOwnerUid = GetOwnerUid(attacker);
        if (!string.IsNullOrWhiteSpace(ownerUid)
            && string.Equals(ownerUid, attackerOwnerUid, StringComparison.Ordinal))
        {
            damage = 0f;
        }
    }

    public override string PropertyName() => BehaviorCode;

    private static bool IsDomesticated(Entity candidate)
    {
        string level = candidate.WatchedAttributes
            .GetTreeAttribute("domesticationstatus")?
            .GetString("domesticationLevel", string.Empty) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(level)
            && !string.Equals(level, "WILD", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetOwnerUid(Entity candidate)
    {
        return candidate.WatchedAttributes
            .GetTreeAttribute("domesticationstatus")?
            .GetString("owner", string.Empty) ?? string.Empty;
    }
}
}
