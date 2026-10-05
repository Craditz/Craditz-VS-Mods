#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace TamablesFotsa;

internal sealed class EntityBehaviorTamablesFotsaTameGuard : EntityBehavior
{
    public const string BehaviorCode = "tamablesfotsatameguard";

    public EntityBehaviorTamablesFotsaTameGuard(Entity entity) : base(entity)
    {
    }

    public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
    {
        base.OnEntityReceiveDamage(damageSource, ref damage);
        if (entity.Api.Side != EnumAppSide.Server
            || damageSource.Type == EnumDamageType.Heal
            || damage <= 0f) return;

        Entity? attacker = damageSource.GetCauseEntity() ?? damageSource.SourceEntity;
        bool isTamedVariant = entity.Code.Domain.Equals("tamablesfotsa", StringComparison.OrdinalIgnoreCase);
        if (!isTamedVariant && !IsDomesticated(entity)) return;

        int healthState = entity.WatchedAttributes.GetInt("healthState", 0);
        if (healthState is 1 or 2)
        {
            damage = 0f;
            return;
        }

        if (attacker is EntityPlayer player
            && string.Equals(player.PlayerUID, GetOwnerUid(entity), StringComparison.Ordinal))
        {
            damage = 0f;
            damageSource.CauseEntity = null;
            damageSource.SourceEntity = null;
            return;
        }

        if (attacker == null || !IsDomesticated(attacker)) return;

        string ownerUid = GetOwnerUid(entity);
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
