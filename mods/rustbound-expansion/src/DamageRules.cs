using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace RustboundExpansion;

public static class DamageRules
{
    public static bool IsDirectHit(DamageSource source, float damage, Entity target) =>
        source != null && float.IsFinite(damage) && damage > 0 &&
        source.Type != EnumDamageType.Heal && source.Duration == TimeSpan.Zero &&
        source.DamageOverTimeType == 0 &&
        source.Source is EnumDamageSource.Player or EnumDamageSource.Entity or EnumDamageSource.Explosion &&
        source.GetCauseEntity() is EntityAgent attacker && attacker != target;
}
