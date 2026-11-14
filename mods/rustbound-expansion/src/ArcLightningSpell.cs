using rustboundmagic.src;
using rustboundmagic.src.common.entity;
using rustboundmagic.src.system.effects;
using rustboundmagic.src.system.enums;
using rustboundmagic.src.system.enums.spells;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace RustboundExpansion;

public sealed class ArcLightningSpell : ExpansionSpell
{
    internal Action<IWorldAccessor, Vec3d, Vec3d, int> VisualEmitter { get; set; } = ArcVisualSystem.Emit;
    public ArcLightningSpell() : base(ExpansionSystem.ArcId, "evocation", "neutral")
    {
        ManaCost = 45;
        CastTime = 2;
        Damage = 10;
        Range = 20;
        Element = "lightning";
        BuffType = EnumBuffType.none;
        BuffDurationType = EnumBuffDurationType.none;
    }

    public override void OnCastStop(float seconds, ItemSlot slot, EntityAgent caster, BlockSelection block,
        EntitySelection entity, bool scroll, bool magnification, bool duplication, float remaining, bool infinite = false)
    {
        if (caster is not EntityPlayer player || player.World.Side != EnumAppSide.Server || remaining != 0) return;
        var first = RayTraceDestinationSelectedEntityAgent(player, Range);
        if (first == null || !Eligible(player, first)) return;
        float damage = GetModifiedDamage(player, Id);
        int hits = ArcChain.Cast(player, first, damage,
            center => player.World.GetEntitiesAround(center, ArcChain.JumpRange, ArcChain.JumpRange),
            target => Eligible(player, target),
            (from, target) => SpellTargeting.HasLineOfSight(player.World, from, SpellTargeting.Center(target)),
            strike =>
            {
                strike.Target.ReceiveDamage(Source(player), strike.Damage);
                VisualEmitter(player.World, strike.From, strike.To, player.Pos.Dimension);
            });
        if (hits > 0) Complete(player, scroll, false);
    }

    internal bool Eligible(EntityPlayer caster, Entity target)
    {
        if (target is not EntityAgent || ControlEffects.Banished(target) || target.EntityId == caster.EntityId || target.Pos.Dimension != caster.Pos.Dimension || !CanDamageTarget(caster, target)
            || target.ShouldDespawn || target.GetBehavior<EntityBehaviorHealth>() == null) return false;
        if (target is EntityCompanionRM companion && companion.OwnerPlayerUID == caster.PlayerUID || CombatRules.IsAlliedSentry(caster, target)) return false;
        if (target is not EntityPlayer && !caster.Player.HasPrivilege("attackcreatures")) return false;
        return target.ShouldReceiveDamage(Source(caster), Damage);
    }
    internal bool NativePermission(EntityPlayer caster, Entity target) => CanDamageTarget(caster, target);

    internal static ElementalDamageSource Source(EntityPlayer caster) => new()
    {
        Source = EnumDamageSource.Player,
        Type = EnumDamageType.Injury, // Rustbound's native elemental spell damage convention.
        ElementalType = EnumMagicElement.lightning,
        SourceEntity = caster,
        CauseEntity = caster,
        KnockbackStrength = 0.2f
    };
}

internal record ArcStrike(Entity Target, Vec3d From, Vec3d To, float Damage);

internal static class ArcChain
{
    internal const int MaxTargets = 5;
    internal const float JumpRange = 6;
    internal const float Falloff = 0.8f;

    internal static int Cast(Entity caster, Entity first, float baseDamage,
        System.Func<Vec3d, IEnumerable<Entity>> nearby, System.Func<Entity, bool> eligible,
        System.Func<Vec3d, Entity, bool> visible, Action<ArcStrike> strike)
    {
        if (!float.IsFinite(baseDamage) || baseDamage <= 0) return 0;
        // One set for the ENTIRE cast, updated before damage callbacks. This also
        // rejects duplicate references/IDs returned by overlapping spatial queries.
        var visited = new HashSet<long> { caster.EntityId };
        var from = caster.Pos.XYZ.AddCopy(caster.LocalEyePos);
        Entity? current = first;
        int count = 0;
        while (current != null && count < MaxTargets)
        {
            if (visited.Contains(current.EntityId) || !eligible(current) || !visible(from, current)) break;
            visited.Add(current.EntityId);
            var to = SpellTargeting.Center(current);
            strike(new(current, from.Clone(), to.Clone(), baseDamage * MathF.Pow(Falloff, count)));
            count++;
            from = to;
            current = nearby(to)
                .Where(e => e != null && !visited.Contains(e.EntityId) && eligible(e)
                    && SpellTargeting.Center(e).SquareDistanceTo(to) <= JumpRange * JumpRange && visible(to, e))
                .OrderBy(e => SpellTargeting.Center(e).SquareDistanceTo(to))
                .ThenBy(e => e.EntityId)
                .FirstOrDefault();
        }
        return count;
    }
}

internal static class SpellTargeting
{
    internal static Vec3d? Ground(EntityPlayer caster, float range)
    {
        BlockSelection? block = null; EntitySelection? entity = null;
        caster.World.RayTraceForSelection(caster.Pos.XYZ.AddCopy(caster.LocalEyePos), caster.Pos.Pitch, caster.Pos.Yaw, range,
            ref block, ref entity, (p, b) => b.GetCollisionBoxes(caster.World.BlockAccessor, p)?.Length > 0, _ => false);
        return block?.Face == BlockFacing.UP ? block.FullPosition : null;
    }
    internal static Vec3d Center(Entity entity) => entity.Pos.XYZ.AddCopy(0,
        Math.Clamp(entity.SelectionBox?.YSize ?? 1.8f, 0.3f, 8f) * 0.55, 0);

    // Target the body box itself, including offsets; never the eye/head point.
    internal static Cuboidd BodyBox(Entity entity) => (entity.CollisionBox ?? entity.SelectionBox ?? new Cuboidf(-.3f, 0, -.3f, .3f, 1.8f, .3f))
        .ToDouble().Translate(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
    internal static Vec3d BodyCenter(Entity entity)
    {
        var box = BodyBox(entity);
        return new((box.X1 + box.X2) * .5, (box.Y1 + box.Y2) * .5, (box.Z1 + box.Z2) * .5);
    }

    internal static bool HasLineOfSight(IWorldAccessor world, Vec3d from, Vec3d to)
    {
        BlockSelection? block = null;
        EntitySelection? entity = null;
        world.RayTraceForSelection(from, to, ref block, ref entity,
            (pos, candidate) => candidate.GetCollisionBoxes(world.BlockAccessor, pos)?.Length > 0,
            _ => false);
        return block == null;
    }
}
