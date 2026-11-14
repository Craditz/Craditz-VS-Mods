using rustboundmagic.src;
using rustboundmagic.src.common.entity;
using rustboundmagic.src.system.effects;
using rustboundmagic.src.system.enums;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace RustboundExpansion;

public sealed class BurstOfSpeedSpell : ExpansionSpell
{
    public BurstOfSpeedSpell() : base(ExpansionSystem.BurstId, "alteration", "light")
    {
        ManaCost = 35;
        CastTime = 2;
        Duration = 15;
        Stats = BurstSpeedEffect.SpeedBonus;
        Range = 16;
        BuffType = EnumBuffType.buff;
    }

    public override void OnCastStop(float seconds, ItemSlot slot, EntityAgent caster, BlockSelection block,
        EntitySelection entity, bool scroll, bool magnification, bool duplication, float remaining, bool infinite = false)
    {
        if (caster is not EntityPlayer player || player.World.Side != EnumAppSide.Server || remaining != 0) return;
        var selected = RayTraceDestinationSelectedEntityAgent(player, Range);
        var target = SelectRecipient(player, selected);
        if (!BurstSpeedEffect.TryApply(target, GetModifiedDuration(player, Id))) return;
        Complete(player, scroll, false);
    }

    internal static EntityAgent SelectRecipient(EntityPlayer caster, Entity? selected)
    {
        if (selected is EntityAgent agent && agent.Alive && !agent.ShouldDespawn && agent.DespawnReason == null && agent.Pos.Dimension == caster.Pos.Dimension && IsAlly(caster, agent)) return agent;
        return caster;
    }

    internal static bool IsAlly(EntityPlayer caster, EntityAgent target)
    {
        if (target == caster) return true;
        if (target is not EntityPlayer) return CreatureSupport.IsFriendly(caster, target);
        // Rustbound has no party/friend contract. Respect both PvP gates; players
        // who can legally damage each other are not implicitly treated as allies.
        return !RustboundMagic.config.MAGICSYSTEM_PVP_ENABLED || !target.ShouldReceiveDamage(ArcLightningSpell.Source(caster), 1);
    }
}

public sealed class BurstRecoverySpell : ExpansionSpell
{
    public BurstRecoverySpell() : base(ExpansionSystem.RecoveryId, "alteration", "light")
    {
        Tier = 0;
        Duration = BurstSpeedEffect.RecoverySeconds;
        Stats = -BurstSpeedEffect.SlowAmount;
        BuffType = EnumBuffType.debuff;
        ResearchableBySpellforge = false;
        CanUseAttributeModifiers = false;
    }
    public override void OnCastStop(float seconds, ItemSlot slot, EntityAgent caster, BlockSelection block,
        EntitySelection entity, bool scroll, bool magnification, bool duplication, float remaining, bool infinite = false)
    { }
}

internal static class BurstSpeedEffect
{
    internal const float SpeedBonus = 2;
    internal const float DamageMultiplier = 1.5f;
    internal const float SlowAmount = 0.4f;
    internal const float RecoverySeconds = 10;
    private const string OwedKey = "rustboundexpansion-burstrecoveryowed";

    internal static bool TryApply(EntityAgent entity, float duration)
    {
        var effects = new StatusEffectsRM();
        if (!entity.Alive || entity.World.Side != EnumAppSide.Server || !float.IsFinite(duration) || duration <= 0
            || effects.IsEffectActive(entity, "stasis") || HasPhase(entity)) return false;
        effects.ApplyStatusEffect(entity, new() { ["walkspeed"] = SpeedBonus },
            "rustboundmagic-" + ExpansionSystem.BurstId, duration, ExpansionSystem.BurstId);
        if (!effects.IsEffectActive(entity, ExpansionSystem.BurstId)) return false;
        entity.WatchedAttributes.SetBool(OwedKey, true);
        entity.WatchedAttributes.MarkPathDirty(OwedKey);
        RefreshObservers(entity, ExpansionSystem.BurstId, duration);
        return true;
    }

    private static bool HasPhase(Entity entity) => entity.WatchedAttributes.GetBool(OwedKey)
        || entity.WatchedAttributes.GetBool("effectactive-" + ExpansionSystem.BurstId)
        || entity.WatchedAttributes.GetBool("effectactive-" + ExpansionSystem.RecoveryId);

    internal static void OnBurstRemoved(Entity entity)
    {
        if (entity.World?.Side != EnumAppSide.Server || !entity.WatchedAttributes.GetBool(OwedKey)) return;
        entity.WatchedAttributes.RemoveAttribute(OwedKey);
        entity.WatchedAttributes.MarkPathDirty(OwedKey);
        if (!entity.Alive) return;
        new StatusEffectsRM().ApplyStatusEffect(entity, new() { ["walkspeed"] = -SlowAmount },
            "rustboundmagic-" + ExpansionSystem.RecoveryId, RecoverySeconds, ExpansionSystem.RecoveryId);
        RefreshObservers(entity, ExpansionSystem.RecoveryId, RecoverySeconds);
    }

    private static void RefreshObservers(Entity entity, string id, float duration)
    {
        var effects = new StatusEffectsRM();
        if (entity is EntityPlayer) effects.RefreshBuffForOtherPlayers(entity, id, duration);
        else if (entity is EntityCompanionRM) effects.RefreshBuffForCompanion(entity, id, duration);
    }

    internal static bool Amplifies(Entity entity, DamageSource source, float damage) =>
        float.IsFinite(damage) && damage > 0 && source.Type != EnumDamageType.Heal && source.Duration <= TimeSpan.Zero
        && new StatusEffectsRM().IsEffectActive(entity, ExpansionSystem.BurstId);

    internal static void Clear(Entity entity)
    {
        // Clear this FIRST: reset/logout/death must not start a new slow phase.
        entity.WatchedAttributes.RemoveAttribute(OwedKey);
        foreach (var id in new[] { ExpansionSystem.BurstId, ExpansionSystem.RecoveryId })
        {
            if (entity.WatchedAttributes.GetBool("effectactive-" + id)) new StatusEffectsRM().RemoveEffect(entity, id);
            entity.Stats?.Remove("walkspeed", "rustboundmagic-" + id);
        }
    }
}
