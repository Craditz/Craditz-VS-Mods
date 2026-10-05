#nullable enable

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

/// <summary>
/// A deliberately simple, extremely durable hostile humanoid used to verify
/// the damage that a companion actually deals.
/// </summary>
public sealed class EntityFeralKinshipTrainingDummy : EntityHumanoid
{
    internal const string LastAttackerKey = "feralKinshipDummyLastAttacker";
    internal const string LastDamageKey = "feralKinshipDummyLastDamage";
    internal const string LastDamageTierKey = "feralKinshipDummyLastDamageTier";
    internal const string LastDamageTypeKey = "feralKinshipDummyLastDamageType";
    internal const string HitCountKey = "feralKinshipDummyHitCount";

    public override bool AlwaysActive
    {
        get => true;
        set { }
    }

    public override bool IsCreature => true;

    public override void Initialize(EntityProperties properties, ICoreAPI api, long inChunkIndex3d)
    {
        base.Initialize(properties, api, inChunkIndex3d);

        // Keep the vanilla Seraph texture/shape, but tint the whole model
        // brown so the target is visually distinct from a player.
        RenderColor = ColorUtil.ColorFromRgba(112, 72, 42, 255);
    }

    public override void OnHurt(DamageSource damageSource, float damage)
    {
        base.OnHurt(damageSource, damage);

        if (World?.Side != EnumAppSide.Server
            || damageSource == null
            || damage <= 0f
            || damageSource.Type == EnumDamageType.Heal)
        {
            return;
        }

        Entity? attacker = damageSource.GetCauseEntity();
        string attackerName = attacker?.GetName() ?? damageSource.Source.ToString();
        if (string.IsNullOrWhiteSpace(attackerName))
        {
            attackerName = attacker?.Code?.ToShortString() ?? "Unknown source";
        }

        WatchedAttributes.SetString(LastAttackerKey, attackerName);
        WatchedAttributes.SetFloat(LastDamageKey, damage);
        WatchedAttributes.SetInt(LastDamageTierKey, damageSource.DamageTier);
        WatchedAttributes.SetString(LastDamageTypeKey, damageSource.Type.ToString());
        WatchedAttributes.SetInt(HitCountKey, WatchedAttributes.GetInt(HitCountKey) + 1);
    }
}
