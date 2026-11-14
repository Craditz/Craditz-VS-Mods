using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace RustboundExpansion;

public sealed partial class ExpansionRuntime : ModSystem
{
    private static readonly Dictionary<IWorldAccessor, ExpansionRuntime> worlds = new(ReferenceEqualityComparer.Instance);
    internal static ExpansionRuntime? For(IWorldAccessor world) => worlds.GetValueOrDefault(world);
    internal IWorldAccessor World = null!;
    private ICoreServerAPI? server;
    private long listener;
    internal long Now => World.ElapsedMilliseconds;
    internal readonly List<Dot> Dots = new();
    internal readonly List<Oath> Oaths = new();
    internal readonly Dictionary<long, ControlState> Controls = new();
    internal readonly Dictionary<long, ChannelState> Channels = new();
    internal readonly Dictionary<string, SummonedSentry> Sentries = new();
    internal readonly HashSet<Entity> Transients = new(ReferenceEqualityComparer.Instance);
    private readonly List<TravelGate> gates = new();
    private readonly Dictionary<string, long> transitCooldown = new();
    private readonly HashSet<string> insideGate = new();
    [ThreadStatic] internal static int ReactiveDepth;
    [ThreadStatic] internal static int TransferDepth;
    internal sealed record Oath(EntityPlayer Caster, Entity Target, bool Ally, long End);
    internal sealed record ControlState(Entity Target, Vec3d Anchor, int Dimension, long End, bool Banish);
    internal sealed class Dot
    {
        internal required EntityPlayer Caster;
        internal required Entity Target;
        internal required string Kind;
        internal float Damage, Increment, Seconds;
        internal long End, Next;
        internal float Explosion;
    }
    internal sealed class ChannelState
    {
        internal required EntityPlayer Caster;
        internal required BacklogSpell Spell;
        internal required Vec3d Center;
        internal required Vec3d Anchor;
        internal long Next, Started, End, LastStep, NextMana;
        internal float Damage;
    }
    public override void StartServerSide(ICoreServerAPI api)
    {
        server = api; Bind(api.World); LoadGems();
        listener = api.Event.RegisterGameTickListener(_ => Tick(), 100);
        api.Event.PlayerDisconnect += OnDisconnect;
        api.Event.OnEntityDespawn += OnDespawn;
    }
    internal void Bind(IWorldAccessor world) { World = world; worlds[world] = this; }
    private void OnDisconnect(IServerPlayer p) => ClearOwner(p.Entity);
    private void OnDespawn(Entity e, EntityDespawnData data)
    {
        if(e is BloodGoober blood)blood.Settle(false);
        Transients.Remove(e);
        foreach (var dot in Dots.Where(d => d.Target == e).ToArray()) Dots.Remove(dot);
        ClearTarget(e);
        if (e is EntityPlayer p) ClearOwner(p);
    }
    internal bool Cast(BacklogSpell spell, EntityPlayer caster, Entity? selected, Vec3d? ground)
    {
        var d = spell.Definition; float damage = spell.ModifiedDamage(caster), duration = spell.ModifiedDuration(caster);
        switch (d.Kind)
        {
            case "oath":
                if (selected is not EntityAgent agent || selected == caster || !selected.Alive || selected.Pos.Dimension != caster.Pos.Dimension ||
                    selected.GetBehavior<EntityBehaviorHealth>() == null || ControlEffects.Banished(selected)) return false;
                bool ally = BurstOfSpeedSpell.IsAlly(caster, agent);
                if (!ally && !CombatRules.Enemy(caster, selected)) return false;
                Oaths.RemoveAll(o => o.Caster == caster || ally && o.Ally && o.Target == selected);
                Oaths.Add(new(caster, selected, ally, Now + (long)(duration * 1000))); Vfx.Mark(selected, "nature"); return true;
            case "banish":
                if (selected == null || !CombatRules.Enemy(caster, selected)) return false;
                SetControl(selected, duration, true); return true;
            case "plague":
                if (selected == null || !CombatRules.Enemy(caster, selected)) return false;
                AddDot(caster, selected, "plague", damage, duration, damage); return true;
            case "beam": return CastBeam(caster, damage, d.Range);
            case "armageddon":
                foreach (var victim in CombatRules.Enemies(caster, caster.Pos.XYZ, d.Range))
                { CombatRules.Hit(caster, victim, damage, "fire"); if (victim.Alive) AddDot(caster, victim, "burn", 3 * damage / 40, duration); Vfx.Fall(victim, "fire"); }
                AddDot(caster, caster, "selfburn", 3 * damage / 40, duration); Vfx.Burst(World, caster.Pos.XYZ, "fire", 12); return true;
            case "gem": return ManaGemItem.Conjure(caster);
            case "bomb":
                if (selected == null || !CombatRules.Enemy(caster, selected)) return false;
                AddDot(caster, selected, "bomb", 2 * damage / 25, duration, 0, damage); return true;
            case "mark": return TravelMarkItem.Record(caster);
            case "teleport": return TravelMarkItem.Travel(caster);
            case "gate": case "twogate": return OpenGate(caster, duration, d.Kind == "twogate");
            case "cannonball":
            case "bullet":
                return MagicProjectile.Launch(caster, caster.Pos.XYZ.AddCopy(caster.LocalEyePos), Aim(caster, d.Range), damage,
                    "none", d.Kind == "cannonball" ? EnumDamageType.BluntAttack : EnumDamageType.PiercingAttack, d.Kind == "cannonball" ? 3 : 0,
                    d.Kind == "cannonball" ? 24 : 65, d.Range, d.Kind);
            case "sentry":
                if (ground == null || ground.DistanceTo(caster.Pos.XYZ) > 14) return false;
                return Summon(caster, ground, d.Element, damage, duration);
        }
        return false;
    }
    internal static Vec3d Aim(EntityPlayer caster, float range) => caster.Pos.XYZ.AddCopy(caster.LocalEyePos)
        .Add(caster.Pos.GetViewVector().ToVec3d() * range);
    private bool CastBeam(EntityPlayer caster, float damage, float range)
    {
        var from = caster.Pos.XYZ.AddCopy(caster.LocalEyePos); var to = Aim(caster, range);
        BlockSelection? block = null; EntitySelection? entity = null;
        World.RayTraceForSelection(from, to, ref block, ref entity, (p, b) => b.GetCollisionBoxes(World.BlockAccessor, p)?.Length > 0, _ => false);
        if (block?.HitPosition != null) to = block.Position.ToVec3d().Add(block.HitPosition);
        foreach (var target in CombatRules.Enemies(caster, (from + to) * 0.5f, range * 0.5f + 2))
            if (BeamGeometry.Intersects(from, to, target.SelectionBox?.ToDouble().Translate(target.Pos.X, target.Pos.Y, target.Pos.Z)
                ?? new Cuboidd(target.Pos.X - .3, target.Pos.Y, target.Pos.Z - .3, target.Pos.X + .3, target.Pos.Y + 1.8, target.Pos.Z + .3), 0.75))
                CombatRules.Hit(caster, target, damage, "lightning");
        // The dedicated beam supports longer segments than Arc's 24-block mesh.
        Vfx.Line(World, from, to, "lightning", caster.Pos.Dimension); return true;
    }
    internal void AddDot(EntityPlayer caster, Entity target, string kind, float damage, float seconds, float increment = 0, float explosion = 0)
    {
        Dots.RemoveAll(d => d.Caster == caster && d.Target == target && d.Kind == kind);
        Dots.Add(new Dot
        {
            Caster = caster,
            Target = target,
            Kind = kind,
            Damage = damage,
            Seconds = seconds,
            Increment = increment,
            End = Now + (long)(seconds * 1000),
            Next = Now + 1000,
            Explosion = explosion
        });
        Vfx.Mark(target, kind == "plague" ? "nature" : "fire");
    }
    internal void SetControl(Entity target, float seconds, bool banish)
    {
        if (Controls.TryGetValue(target.EntityId, out var existing) && existing.Banish && !banish) return;
        Controls[target.EntityId] = new(target, target.Pos.XYZ, target.Pos.Dimension, Now + (long)(seconds * 1000), banish);
        ControlEffects.Set(target, banish);
        Vfx.Mark(target, banish ? "arcane" : "frost");
    }
    internal void Tick()
    {
        Oaths.RemoveAll(o => Now >= o.End || !o.Caster.Alive || !o.Target.Alive || o.Target.Pos.Dimension != o.Caster.Pos.Dimension);
        foreach (var control in Controls.Values.ToArray())
        {
            if (Now >= control.End || !control.Target.Alive || control.Target.ShouldDespawn) { ClearTargetControl(control.Target); continue; }
            ControlEffects.Hold(control.Target, control.Anchor, control.Dimension);
        }
        foreach (var dot in Dots.ToArray())
        {
            if (!Dots.Contains(dot)) continue;
            if (!dot.Caster.Alive || dot.Caster.ShouldDespawn || dot.Target.Pos.Dimension != dot.Caster.Pos.Dimension) { Dots.Remove(dot); continue; }
            if (!dot.Target.Alive) { ResolveDeadDot(dot); continue; }
            if (Now >= dot.Next && Now <= dot.End && !ControlEffects.Banished(dot.Target))
            {
                dot.Next = Now + 1000;
                if (dot.Kind == "selfburn") dot.Target.ReceiveDamage(new rustboundmagic.src.system.effects.ElementalDamageSource
                {
                    Source = EnumDamageSource.Internal,
                    Type = EnumDamageType.Injury,
                    ElementalType = rustboundmagic.src.system.enums.spells.EnumMagicElement.fire,
                    IgnoreInvFrames = true,
                    KnockbackStrength = 0
                }, dot.Damage);
                else CombatRules.Hit(dot.Caster, dot.Target, dot.Damage, dot.Kind == "plague" ? "nature" : "fire");
                Vfx.Mark(dot.Target, dot.Kind == "plague" ? "nature" : "fire");
            }
            if (Now >= dot.End && Dots.Remove(dot) && dot.Kind == "bomb") Explode(dot);
        }
        TickChannels();
        TickNext();
        TickField();
        TickMemes();
        TickBlood();
        foreach (var gate in gates.ToArray())
        {
            if (!gate.Alive || gate.ShouldDespawn || Now >= gate.End) { gate.Retire(); gates.Remove(gate); continue; }
            foreach (var player in World.AllOnlinePlayers.OfType<IServerPlayer>())
            {
                var entity = player.Entity;
                if (entity.Pos.Dimension != gate.Pos.Dimension || !entity.Alive || ControlEffects.Frozen(entity) || entity.Pos.XYZ.DistanceTo(gate.Pos.XYZ) > 1.1 || insideGate.Contains(player.PlayerUID) || transitCooldown.GetValueOrDefault(player.PlayerUID) > Now) continue;
                transitCooldown[player.PlayerUID] = Now + 3000;
                if (TravelMarkItem.TravelTo(entity, gate.Destination)) insideGate.Add(player.PlayerUID);
            }
        }
        foreach (var key in transitCooldown.Where(p => p.Value < Now).Select(p => p.Key).ToArray()) transitCooldown.Remove(key);
        insideGate.RemoveWhere(uid => World.PlayerByUid(uid)?.Entity is not { } player ||
            !gates.Any(g => g.Pos.Dimension == player.Pos.Dimension && g.Pos.XYZ.DistanceTo(player.Pos.XYZ) <= 1.1));
    }
    internal void OnDeath(Entity entity)
    {
        BloodDeath(entity);
        foreach (var dot in Dots.Where(d => d.Target == entity).ToArray()) ResolveDeadDot(dot);
        if (entity is EntityPlayer p) ClearOwner(p);
        ClearTarget(entity);
    }
    private void ResolveDeadDot(Dot dot)
    {
        if (!Dots.Remove(dot)) return;
        if (dot.Kind == "bomb") { Explode(dot); return; }
        if (dot.Kind != "plague" || !dot.Caster.Alive) return;
        var next = CombatRules.Enemies(dot.Caster, dot.Target.Pos.XYZ, 8)
            .Where(e => e.EntityId != dot.Target.EntityId)
            .OrderBy(e => e.Pos.XYZ.SquareDistanceTo(dot.Target.Pos.XYZ)).ThenBy(e => e.EntityId).FirstOrDefault();
        if (next != null) AddDot(dot.Caster, next, "plague", dot.Damage + dot.Increment, dot.Seconds, dot.Increment);
    }
    private void Explode(Dot dot)
    {
        Vfx.Explosion(World, SpellTargeting.BodyCenter(dot.Target), 5);
        foreach (var victim in CombatRules.Enemies(dot.Caster, dot.Target.Pos.XYZ, 5))
        {
            CombatRules.Hit(dot.Caster, victim, dot.Explosion, "fire");
            if (victim.Alive && victim != dot.Target) AddDot(dot.Caster, victim, "burn", dot.Damage, 6);
        }
    }
    internal void AfterMitigation(Entity target, DamageSource source, ref float damage)
    {
        if (ReactiveDepth > 0 || !target.Alive || source.Type == EnumDamageType.Heal || source.Duration > TimeSpan.Zero || !float.IsFinite(damage) || damage <= 0) return;
        var oath = Oaths.FirstOrDefault(o => o.Ally && o.Target == target && o.End > Now && o.Caster.Alive && o.Caster != target);
        if (oath != null) { float share = damage / 2; damage = share; LinkedHit(oath.Caster, source, share, true); }
        foreach (var echo in Oaths.Where(o => !o.Ally && o.Caster == target && o.End > Now && o.Target.Alive).ToArray())
            if (CombatRules.Enemy(echo.Caster, echo.Target)) LinkedHit(echo.Target, CombatRules.Source(echo.Caster, ElementOf(source), source.Type), damage, false);
    }
    private static string ElementOf(DamageSource source) => source is rustboundmagic.src.system.effects.ElementalDamageSource elemental ? elemental.ElementalType.ToString() : "none";
    private static void LinkedHit(Entity target, DamageSource source, float damage, bool transfer)
    {
        ReactiveDepth++; if (transfer) TransferDepth++;
        try { target.ReceiveDamage(source, damage); }
        finally { if (transfer) TransferDepth--; ReactiveDepth--; }
    }
    internal void ClearTargetControl(Entity target) { Controls.Remove(target.EntityId); ControlEffects.ClearControl(target); }
    internal void ClearTarget(Entity target)
    {
        ClearField(target);
        Bulwark.Clear(target);
        ClearBloodTarget(target);
        ClearNextTarget(target);
        ClearMemeTarget(target);
        ClearTargetControl(target); Oaths.RemoveAll(o => o.Target == target);
        ControlEffects.Clear(target);
        Dots.RemoveAll(d => d.Target == target);
    }
    internal void ClearOwner(EntityPlayer player)
    {
        ClearField(player);
        ClearBloodOwner(player);
        ClearNextOwner(player);
        ClearMemeOwner(player);
        ChannelHooks.Forget(player);
        StopChannel(player); Oaths.RemoveAll(o => o.Caster == player); Dots.RemoveAll(d => d.Caster == player);
        if (Sentries.Remove(player.PlayerUID, out var sentry)) sentry.Retire();
        foreach (var gate in gates.Where(g => g.OwnerUid == player.PlayerUID).ToArray()) { gate.Retire(); gates.Remove(gate); }
    }
    private bool Summon(EntityPlayer caster, Vec3d point, string element, float damage, float duration)
    {
        var sentry = World.ClassRegistry.CreateEntity(World.GetEntityType(new("rustboundexpansion:" + SummonedSentry.EntityCode(element)))) as SummonedSentry;
        if (sentry == null) return false;
        if (Sentries.Remove(caster.PlayerUID, out var previous)) previous.Retire();
        // Ground selection already gives the exact top surface. The pedestal
        // starts at model Y=0; only the head has a hover gap/animation.
        sentry.Pos.SetPos(point); sentry.Pos.Dimension = caster.Pos.Dimension;
        sentry.Setup(caster, element, damage, Now + (long)(duration * 1000));
        Transients.Add(sentry); Sentries[caster.PlayerUID] = sentry; World.SpawnEntity(sentry); return true;
    }
    private bool OpenGate(EntityPlayer caster, float duration, bool twoWay)
    {
        if (!TravelMarkItem.Read(caster, out var destination) || !TravelMarkItem.Safe(World, destination)) return false;
        var source = new TravelPoint(caster.Pos.X - 2 * Math.Sin(caster.Pos.Yaw), caster.Pos.Y, caster.Pos.Z - 2 * Math.Cos(caster.Pos.Yaw), caster.Pos.Dimension, caster.Pos.Yaw);
        if (!TravelMarkItem.Safe(World, source)) return false;
        SpawnGate(caster, source, destination, duration);
        if (twoWay) SpawnGate(caster, destination, source, duration);
        return true;
    }
    private void SpawnGate(EntityPlayer owner, TravelPoint point, TravelPoint destination, float seconds)
    {
        var gate = (TravelGate)World.ClassRegistry.CreateEntity(World.GetEntityType(new("rustboundexpansion:travelgate")));
        gate.Pos.SetPos(point.X, point.Y, point.Z); gate.Pos.Dimension = point.Dimension;
        gate.Setup(owner.PlayerUID, destination, Now + (long)(seconds * 1000)); Transients.Add(gate); gates.Add(gate); World.SpawnEntity(gate);
    }
    public override void Dispose()
    {
        BloodVolleys.Clear();
        foreach(var owner in BloodHosts.Keys.ToArray())EndBloodHost(owner,false);
        foreach(var drain in BloodDrains.Keys.ToArray())StopBloodDrain(drain);
        foreach(var effect in BloodEffects.Values.ToArray())RemoveBloodEffect(effect);
        DisposeNext();
        DisposeField();
        DisposeMemes();
        foreach (var c in Controls.Values.ToArray()) ControlEffects.Clear(c.Target);
        foreach (var c in Channels.Values.ToArray()) StopChannel(c.Caster);
        foreach (var s in Sentries.Values) s.Retire(); foreach (var g in gates) g.Retire();
        Controls.Clear(); Oaths.Clear(); Dots.Clear(); Sentries.Clear(); gates.Clear(); transitCooldown.Clear(); Transients.Clear(); insideGate.Clear();
        if (server != null) { server.Event.UnregisterGameTickListener(listener); server.Event.PlayerDisconnect -= OnDisconnect; server.Event.OnEntityDespawn -= OnDespawn; }
        if (World != null) worlds.Remove(World);
    }
}

internal static class BeamGeometry
{
    internal static bool Intersects(Vec3d from, Vec3d to, Cuboidd box, double padding)
        => TryIntersect(from, to, box, padding, out _);
    internal static bool TryIntersect(Vec3d from, Vec3d to, Cuboidd box, double padding, out double fraction)
    {
        fraction = 0;
        double lo = 0, hi = 1;
        double[] a = [from.X, from.Y, from.Z], b = [to.X, to.Y, to.Z], min = [box.X1 - padding, box.Y1 - padding, box.Z1 - padding], max = [box.X2 + padding, box.Y2 + padding, box.Z2 + padding];
        for (int i = 0; i < 3; i++)
        {
            double d = b[i] - a[i]; if (Math.Abs(d) < 1e-9) { if (a[i] < min[i] || a[i] > max[i]) return false; continue; }
            double enter = (min[i] - a[i]) / d, exit = (max[i] - a[i]) / d;
            if (enter > exit) (enter, exit) = (exit, enter);
            lo = Math.Max(lo, enter); hi = Math.Min(hi, exit); if (lo > hi) return false;
        }
        fraction = lo;
        return true;
    }
}
