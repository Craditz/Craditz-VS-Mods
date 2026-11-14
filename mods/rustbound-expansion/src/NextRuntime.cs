using rustboundmagic.src.common.entity;
using rustboundmagic.src.system.effects;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace RustboundExpansion;

public sealed partial class ExpansionRuntime
{
    internal const string GripImmunity = "rbxrecentlylifegripped", FoulRecovery = "rbxpartyfoulcooldown";
    internal readonly Dictionary<long, WispState> Wisps = new();
    internal readonly Dictionary<long, long> WispLaunches = new();
    internal readonly Dictionary<long, PrayerState> Prayers = new(); // keyed by caster, not recipient
    internal readonly Dictionary<long, CircleState> Circles = new();
    internal readonly Dictionary<long, FoulState> Fouls = new();
    private readonly Dictionary<long, EntityPlayer> stanceOwners = new();
    private readonly Dictionary<long, long> celebratedBlasts = new();
    internal sealed record WispState(EntityPlayer Caster, float Damage, long End) { internal int Remaining = 3; internal long NextVisual; }
    internal sealed record PrayerState(EntityPlayer Caster, EntityAgent Target, float Heal, long End)
    { internal EntityAgent Recipient = Target; internal int Charges = 5; internal long Next; }
    internal sealed record CircleState(EntityPlayer Caster, Vec3d Center, int Dimension, float Heal, long End)
    { internal long Next, NextVisual; internal int Pulses; }
    internal sealed record FoulState(EntityAgent Target, bool Cosmetic, long End)
    { internal float Budget = 15; internal long NextVisual; internal float PendingVisual; }
    private static readonly StatusEffectsRM nextEffects = new();

    internal static bool Ally(EntityPlayer p, Entity e) => e is EntityAgent a && a.Alive && !a.ShouldDespawn && a.DespawnReason == null
        && a.Pos.Dimension == p.Pos.Dimension && !ControlEffects.Banished(a) && BurstOfSpeedSpell.IsAlly(p, a);
    internal static bool Active(Entity e, string id) => nextEffects.IsEffectActive(e, id);
    internal static void Marker(Entity e, string id, float seconds, bool infinite = false)
    {
        nextEffects.ApplyStatusEffect(e, new(), "rustboundmagic-" + id, seconds, id, infinite);
        if (e is EntityPlayer) nextEffects.RefreshBuffForOtherPlayers(e, id, seconds);
        else if (e is EntityCompanionRM) nextEffects.RefreshBuffForCompanion(e, id, seconds);
    }
    internal static void Unmark(Entity e, string id)
    { if (Active(e, id)) nextEffects.RemoveEffect(e, id); }

    internal bool CastNext(NextSpell spell, EntityPlayer p, Entity? selected, Vec3d? ground)
    {
        var d = spell.Definition;
        switch (d.Kind)
        {
            case "seeking":
                return selected != null && CombatRules.Enemy(p, selected) && SpellTargeting.HasLineOfSight(World, p.Pos.XYZ.AddCopy(p.LocalEyePos), SpellTargeting.BodyCenter(selected))
                    && NextProjectile.Launch(p, "seeking", p.Pos.XYZ.AddCopy(p.LocalEyePos), SpellTargeting.BodyCenter(selected), spell.ModifiedDamage(p), d.Range, "arcane", selected);
            case "ricochet": case "orb":
                return NextProjectile.Launch(p, d.Kind, p.Pos.XYZ.AddCopy(p.LocalEyePos), Aim(p, d.Range), spell.ModifiedDamage(p), d.Range, d.Element);
            case "buckshot":
                long blast = NextProjectile.NextBlast();
                var direction = p.Pos.GetViewVector().ToVec3d().Normalize();
                var side = new Vec3d(direction.Z, 0, -direction.X); if (side.Length() < .01) side.Set(1, 0, 0); side.Normalize();
                var up = new Vec3d(side.Y * direction.Z - side.Z * direction.Y, side.Z * direction.X - side.X * direction.Z, side.X * direction.Y - side.Y * direction.X);
                bool any = false;
                for (int i = 0; i < 8; i++)
                {
                    double angle = i * 2.399963229728653, radius = i == 0 ? 0 : .15 * Math.Sqrt(i / 7.0);
                    var ray = (direction + side * (Math.Cos(angle) * radius) + up * (Math.Sin(angle) * radius)).Normalize();
                    var origin = p.Pos.XYZ.AddCopy(p.LocalEyePos);
                    any |= NextProjectile.Launch(p, "pellet", origin, origin + ray, spell.ModifiedDamage(p), 9, "none", blast: blast);
                }
                return any;
            case "dry":
                var dry = selected != null && Ally(p, selected) ? selected : p;
                dry.WatchedAttributes.SetFloat("wetness", 0); dry.WatchedAttributes.MarkPathDirty("wetness"); Vfx.Mark(dry, "wind"); return true;
            case "wisps":
                Unmark(p, d.Id);
                Wisps[p.EntityId] = new(p, spell.ModifiedDamage(p), Now + 15000);
                Marker(p, d.Id, 15); return true;
            case "prayer":
                var patient = selected != null && Ally(p, selected) ? (EntityAgent)selected : p;
                if (patient.GetBehavior<EntityBehaviorHealth>() == null) return false;
                RemovePrayer(p.EntityId);
                var prayer = new PrayerState(p, patient, spell.ModifiedStats(p), Now + (long)(spell.ModifiedDuration(p) * 1000));
                Prayers[p.EntityId] = prayer; RefreshPrayerMarker(patient); Vfx.Mark(patient, "nature"); return true;
            case "grip":
                if (selected is not EntityAgent friend || friend == p || !Ally(p, friend) || Active(friend, GripImmunity)) return false;
                return Pull(p, friend);
            case "circle":
                if (ground == null) return false;
                Circles[p.EntityId] = new(p, ground, p.Pos.Dimension, spell.ModifiedStats(p), Now + 12000) { Next = Now + 1000, NextVisual = Now + 250 };
                NextVfx.Circle(World, ground, p.Pos.Dimension); return true;
            case "party":
                var target = selected is EntityAgent a && (Ally(p, a) || CombatRules.Enemy(p, a)) ? a : p;
                bool cosmetic = Ally(p, target);
                if (!cosmetic && Active(p, FoulRecovery)) return false;
                Unmark(target, d.Id);
                var foul = new FoulState(target, cosmetic, Now + (cosmetic ? 600000 : 4000)); Fouls[target.EntityId] = foul;
                Marker(target, d.Id, cosmetic ? 600 : 4);
                if (!cosmetic) Marker(p, FoulRecovery, 20);
                return true;
            case "snap": case "furtive": case "allin":
                RemoveStance(p);
                var values = StanceValues.For(d.Kind, spell.ModifiedStats(p));
                p.WatchedAttributes.SetString(NextModifiers.StanceKey, d.Id);
                p.WatchedAttributes.SetFloat(NextModifiers.SpeedKey, values.Speed);
                p.WatchedAttributes.SetFloat(NextModifiers.DamageKey, values.Damage);
                p.WatchedAttributes.SetFloat(NextModifiers.ManaKey, values.Mana);
                stanceOwners[p.EntityId] = p;
                p.WatchedAttributes.MarkAllDirty(); Marker(p, d.Id, 600, true); return true;
        }
        return false;
    }

    internal void RemoveStance(Entity p)
    {
        stanceOwners.Remove(p.EntityId);
        string id = p.WatchedAttributes.GetString(NextModifiers.StanceKey, "");
        foreach (var key in new[] { NextModifiers.StanceKey, NextModifiers.SpeedKey, NextModifiers.DamageKey, NextModifiers.ManaKey })
            p.WatchedAttributes.RemoveAttribute(key);
        p.WatchedAttributes.MarkAllDirty();
        if (id.Length > 0) Unmark(p, id);
    }
    internal void NextEffectRemoved(Entity target, string id)
    {
        if (id == target.WatchedAttributes.GetString(NextModifiers.StanceKey, "")) RemoveStance(target);
        if (id == NextCatalog.Kind("wisps").Id) Wisps.Remove(target.EntityId);
        if (id == NextCatalog.Kind("party").Id) Fouls.Remove(target.EntityId);
        if (id == NextCatalog.Kind("prayer").Id)
            foreach (var prayer in Prayers.Values.Where(s => s.Recipient == target).ToArray()) Prayers.Remove(prayer.Caster.EntityId);
    }
    internal void RemovePrayer(long caster)
    {
        if (!Prayers.Remove(caster, out var old)) return;
        RefreshPrayerMarker(old.Recipient);
    }
    private void RefreshPrayerMarker(Entity recipient)
    {
        long end = Prayers.Values.Where(s => s.Recipient == recipient).Select(s => s.End).DefaultIfEmpty(0).Max();
        string id = NextCatalog.Kind("prayer").Id;
        if (end <= Now) Unmark(recipient, id);
        else Marker(recipient, id, (end - Now) / 1000f);
    }
    internal void AfterActualDamage(Entity target, DamageSource source, float actual)
    {
        if (source.Type == EnumDamageType.Heal || actual <= 0 || !target.Alive) return;
        foreach (var prayer in Prayers.Values.Where(s => s.Recipient == target && s.Next <= Now && s.End > Now).ToArray())
        {
            prayer.Next = Now + 1000; prayer.Charges--;
            Heal(prayer.Caster, prayer.Recipient, prayer.Heal); Vfx.Mark(target, "nature");
            if (prayer.Charges <= 0) { RemovePrayer(prayer.Caster.EntityId); continue; }
            var next = Allies(prayer.Caster, target.Pos.XYZ, 8).Where(e => e != target)
                .OrderBy(e => e.Pos.XYZ.SquareDistanceTo(target.Pos.XYZ)).ThenBy(e => e.EntityId).FirstOrDefault();
            if (next == null) continue;
            Prayers.Remove(prayer.Caster.EntityId);
            RefreshPrayerMarker(prayer.Recipient);
            Vfx.Line(World, SpellTargeting.BodyCenter(target), SpellTargeting.BodyCenter(next), "nature", next.Pos.Dimension);
            prayer.Recipient = next; Prayers[prayer.Caster.EntityId] = prayer;
            RefreshPrayerMarker(next);
        }
    }
    private EntityAgent[] Allies(EntityPlayer owner, Vec3d center, float range) => World.GetEntitiesAround(center, range, range)
        .Append(owner).Where(e => Ally(owner, e) && e.Pos.XYZ.SquareDistanceTo(center) <= range * range && e.GetBehavior<EntityBehaviorHealth>() != null)
        .DistinctBy(e => e.EntityId).Cast<EntityAgent>().ToArray();
    internal static void Heal(EntityPlayer caster, Entity target, float amount)
    {
        if (amount <= 0 || !float.IsFinite(amount) || !target.Alive) return;
        target.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.Heal, CauseEntity = caster, SourceEntity = caster, IgnoreInvFrames = true, KnockbackStrength = 0 }, amount);
    }

    internal static Entity? DamageActor(IWorldAccessor world, DamageSource source)
    {
        var cause = source.GetCauseEntity() ?? source.SourceEntity;
        if (cause is SummonedSentry sentry) return world.PlayerByUid(sentry.OwnerUid)?.Entity ?? cause;
        if (cause is EntityCompanionRM pet) return world.PlayerByUid(pet.OwnerPlayerUID)?.Entity ?? cause;
        if (cause is MagicProjectile or NextProjectile) return world.PlayerByUid(cause.WatchedAttributes.GetString("rbx-owner", ""))?.Entity ?? cause;
        return cause;
    }
    internal void PartyMitigation(Entity target, DamageSource source, ref float damage)
    {
        if (TransferDepth > 0 || source.Type == EnumDamageType.Heal || damage <= 0 || !float.IsFinite(damage)) return;
        var actor = DamageActor(World, source);
        if (actor == null || !Fouls.TryGetValue(actor.EntityId, out var foul) || foul.End <= Now) return;
        float festive = foul.Cosmetic ? damage : Math.Min(damage, foul.Budget);
        if (!foul.Cosmetic) { damage -= festive; foul.Budget -= festive; }
        foul.PendingVisual += festive;
        if (Now >= foul.NextVisual || foul.Budget <= 0)
        {
            foul.NextVisual = Now + 150;
            NextVfx.Party(World, SpellTargeting.BodyCenter(target), foul.PendingVisual); foul.PendingVisual = 0;
        }
        if (!foul.Cosmetic && foul.Budget <= 0) { Fouls.Remove(actor.EntityId); Unmark(actor, NextCatalog.Kind("party").Id); }
    }
    internal void CelebrateBuckshot(long blast, Vec3d at)
    {
        if (celebratedBlasts.ContainsKey(blast)) return;
        celebratedBlasts[blast] = Now + 5000; NextVfx.Party(World, at, 2);
    }
    internal void TickNext()
    {
        foreach (var state in Wisps.Values.ToArray())
        {
            var p = state.Caster;
            if (!p.Alive || p.ShouldDespawn || Now >= state.End || state.Remaining <= 0) { Wisps.Remove(p.EntityId); Unmark(p, NextCatalog.Kind("wisps").Id); continue; }
            if (ControlEffects.Banished(p)) continue;
            if (Now >= state.NextVisual)
            {
                state.NextVisual = Now + 200;
                for (int i = 0; i < state.Remaining; i++) Vfx.Burst(World, WispPosition(p, i), "fire", .025f, 2);
            }
            if (WispLaunches.GetValueOrDefault(p.EntityId) > Now) continue;
            var target = CombatRules.Enemies(p, p.Pos.XYZ, 6).Where(e => SpellTargeting.HasLineOfSight(World, WispPosition(p, 0), SpellTargeting.BodyCenter(e)))
                .OrderBy(e => e.Pos.XYZ.SquareDistanceTo(p.Pos.XYZ)).ThenBy(e => e.EntityId).FirstOrDefault();
            if (target != null && NextProjectile.Launch(p, "wisp", WispPosition(p, state.Remaining - 1), SpellTargeting.BodyCenter(target), state.Damage, 12, "fire", target))
            { state.Remaining--; WispLaunches[p.EntityId] = Now + 750; }
        }
        foreach (var circle in Circles.Values.ToArray())
        {
            if (!circle.Caster.Alive || circle.Caster.ShouldDespawn || circle.Caster.Pos.Dimension != circle.Dimension || circle.Pulses >= 12 || Now > circle.End)
            { Circles.Remove(circle.Caster.EntityId); continue; }
            if (Now >= circle.NextVisual)
            { circle.NextVisual = Now + 250; NextVfx.Circle(World, circle.Center, circle.Dimension); }
            if (Now < circle.Next) continue;
            circle.Next += 1000; circle.Pulses++;
            var injured = Allies(circle.Caster, circle.Center, 4).Where(e => e.GetBehavior<EntityBehaviorHealth>() is { } h && h.Health < h.MaxHealth).ToArray();
            foreach (var ally in injured) Heal(circle.Caster, ally, circle.Heal / injured.Length);
        }
        foreach (var prayer in Prayers.Values.Where(s => !s.Caster.Alive || !s.Recipient.Alive || s.Caster.ShouldDespawn || s.Recipient.ShouldDespawn || s.End <= Now || s.Caster.Pos.Dimension != s.Recipient.Pos.Dimension).ToArray()) RemovePrayer(prayer.Caster.EntityId);
        foreach (var foul in Fouls.Values.Where(s => !s.Target.Alive || s.Target.ShouldDespawn || s.End <= Now).ToArray()) { Fouls.Remove(foul.Target.EntityId); Unmark(foul.Target, NextCatalog.Kind("party").Id); }
        foreach (var key in celebratedBlasts.Where(x => x.Value <= Now).Select(x => x.Key).ToArray()) celebratedBlasts.Remove(key);
        foreach (var key in WispLaunches.Where(x => x.Value < Now - 15000 && !Wisps.ContainsKey(x.Key)).Select(x => x.Key).ToArray()) WispLaunches.Remove(key);
    }
    private Vec3d WispPosition(EntityPlayer p, int index)
    { double a = Now / 500.0 + index * Math.PI * 2 / 3; return p.Pos.XYZ.AddCopy(Math.Cos(a) * .65, 1.25 + Math.Sin(a * 2) * .1, Math.Sin(a) * .65); }
    internal bool Pull(EntityPlayer caster, EntityAgent target)
    {
        if (Active(target, GripImmunity)) return false;
        var from = target.Pos.XYZ; var delta = caster.Pos.XYZ - from;
        double length = delta.Length(); if (length <= 1.5) return false;
        var to = from.Clone(); var direction = delta.Normalize();
        for (double distance = .2; distance < length - 1.2; distance += .2)
        { var point = from + direction * distance; if (!CanOccupy(target, point)) break; to = point; }
        if (to.DistanceTo(from) < .15) return false;
        Vfx.Line(World, SpellTargeting.BodyCenter(target), to.AddCopy(0, target.CollisionBox?.YSize * .5 ?? .8, 0), "arcane", target.Pos.Dimension);
        if (target is EntityPlayer player) player.TeleportToDouble(to.X, to.Y, to.Z);
        else { target.Pos.SetPos(to); target.Pos.Motion.Set(0, 0, 0); }
        Marker(target, GripImmunity, 10); return true;
    }
    private bool CanOccupy(EntityAgent target, Vec3d point)
    {
        var box = (target.CollisionBox ?? new Cuboidf(-.3f, 0, -.3f, .3f, 1.8f, .3f)).ToDouble().Translate(point.X, point.Y, point.Z);
        for (int x = (int)Math.Floor(box.X1); x <= Math.Floor(box.X2); x++)
        for (int y = (int)Math.Floor(box.Y1); y <= Math.Floor(box.Y2); y++)
        for (int z = (int)Math.Floor(box.Z1); z <= Math.Floor(box.Z2); z++)
        {
            var pos = new BlockPos(x, y, z, target.Pos.Dimension); var block = World.BlockAccessor.GetBlock(pos);
            foreach (var part in block.GetCollisionBoxes(World.BlockAccessor, pos) ?? [])
                if (box.X1 < x + part.X2 - .001 && box.X2 > x + part.X1 + .001 && box.Y1 < y + part.Y2 - .001 && box.Y2 > y + part.Y1 + .001 && box.Z1 < z + part.Z2 - .001 && box.Z2 > z + part.Z1 + .001) return false;
        }
        return true;
    }
    internal void ClearNextTarget(Entity target)
    {
        foreach (var prayer in Prayers.Values.Where(p => p.Recipient == target).ToArray()) RemovePrayer(prayer.Caster.EntityId);
        Fouls.Remove(target.EntityId); RemoveStance(target);
        foreach (var id in NextCatalog.All.Where(d => d.Kind is "party" or "wisps").Select(d => d.Id).Concat(NextCatalog.Hidden)) Unmark(target, id);
    }
    internal void ClearNextOwner(EntityPlayer p)
    { Wisps.Remove(p.EntityId); WispLaunches.Remove(p.EntityId); Circles.Remove(p.EntityId); RemovePrayer(p.EntityId); RemoveStance(p); ChargeHooks.Forget(p); }
    internal void DisposeNext()
    {
        foreach (var owner in stanceOwners.Values.ToArray()) RemoveStance(owner);
        foreach (var p in Prayers.Keys.ToArray()) RemovePrayer(p);
        foreach (var f in Fouls.Values.ToArray()) Unmark(f.Target, NextCatalog.Kind("party").Id);
        foreach (var w in Wisps.Values.ToArray()) Unmark(w.Caster, NextCatalog.Kind("wisps").Id);
        Fouls.Clear(); Wisps.Clear(); Circles.Clear(); WispLaunches.Clear(); celebratedBlasts.Clear();
    }
}

internal readonly record struct StanceValues(float Speed, float Damage, float Mana)
{
    internal static StanceValues For(string kind, float potency)
    {
        float s = Math.Min(1.25f, 1 + .25f * (Math.Max(0, float.IsFinite(potency) ? potency : 1) - 1));
        return kind switch { "snap" => new(1 + .8f * s, 1 - .2f / s, 1 + .1f / s), "furtive" => new(1 - .25f / s, 1, 1 - .25f * s), "allin" => new(1 - .25f / s, 1 + .3f * s, 1 + .2f / s), _ => new(1, 1, 1) };
    }
}
