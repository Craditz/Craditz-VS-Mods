using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace RustboundExpansion;

public sealed class SummonedSentry : EntityAgent
{
    public string OwnerUid => WatchedAttributes.GetString("rbx-owner", "");
    private long nextShot;
    private int visualShot;
    public override bool IsCreature => true;
    internal static string EntityCode(string element) => "sentry-" + (element switch
    {
        "rust" or "fire" or "water" or "wind" or "earth" or "lightning" or "frost" or "nature" or "arcane" => element,
        _ => "rock"
    });
    internal void Setup(EntityPlayer owner, string element, float damage, long end)
    {
        WatchedAttributes.SetString("rbx-owner", owner.PlayerUID); WatchedAttributes.SetString("rbx-element", element);
        WatchedAttributes.SetFloat("rbx-damage", damage); WatchedAttributes.SetLong("rbx-end", end);
    }
    public override void OnGameTick(float dt)
    {
        base.OnGameTick(dt);
        if (World.Side != EnumAppSide.Server)
        {
            // Stone remains stone; element colors live in eye/core materials.
            RenderColor = unchecked((int)0xffffffff);
            int shot = WatchedAttributes.GetInt("rbx-shot");
            if (shot != visualShot && AnimManager?.Animator != null)
            {
                visualShot = shot;
                AnimManager.StopAnimation("shoot");
                AnimManager.StartAnimation("shoot");
            }
            return;
        }
        var owner = World.PlayerByUid(OwnerUid)?.Entity;
        if (owner == null || !owner.Alive || owner.Pos.Dimension != Pos.Dimension || World.ElapsedMilliseconds >= WatchedAttributes.GetLong("rbx-end")) { Retire(); return; }
        Pos.Motion.Set(0, 0, 0);
        if (!Alive || ControlEffects.Banished(this) || World.ElapsedMilliseconds < nextShot) return;
        nextShot = World.ElapsedMilliseconds + 1500;
        Shoot(owner);
    }
    internal void Shoot(EntityPlayer owner)
    {
        var target = CombatRules.Enemies(owner, Pos.XYZ, 20).Where(e => SpellTargeting.HasLineOfSight(World, SpellTargeting.BodyCenter(this), SpellTargeting.BodyCenter(e)))
            .OrderBy(e => e.Pos.XYZ.SquareDistanceTo(Pos.XYZ)).FirstOrDefault();
        if (target == null) return;
        var element = WatchedAttributes.GetString("rbx-element", "none");
        // Model front is -Z; native entity rendering adds 90 degrees of yaw.
        Pos.Yaw = (float)Math.Atan2(target.Pos.Z - Pos.Z, Pos.X - target.Pos.X);
        if (MagicProjectile.Launch(owner, SpellTargeting.BodyCenter(this), SpellTargeting.BodyCenter(target), WatchedAttributes.GetFloat("rbx-damage"), element,
            element == "none" ? EnumDamageType.BluntAttack : EnumDamageType.Injury, 0, 15, 24, element == "none" ? "rock" : "bolt"))
            WatchedAttributes.SetInt("rbx-shot", WatchedAttributes.GetInt("rbx-shot") + 1);
    }
    internal void Retire() => Die(EnumDespawnReason.Removed);
    public override void Die(EnumDespawnReason reason = EnumDespawnReason.Death, DamageSource? source = null)
    {
        base.Die(reason, source);
        DespawnReason = new EntityDespawnData { Reason = EnumDespawnReason.Removed, DamageSourceForDeath = source };
    }
}

public sealed class MagicProjectile : Entity
{
    private long nextVisual;
    private double remaining;
    public override bool IsInteractable => false;
    internal static bool Launch(EntityPlayer caster, Vec3d from, Vec3d toward, float damage, string element, EnumDamageType type, float splash, float speed, float maxDistance, string appearance = "bolt")
    {
        if (from.DistanceTo(toward) < 0.001) return false;
        string code = appearance is "cannonball" or "bullet" or "rock" ? "magicprojectile-" + appearance : "magicprojectile";
        var projectile = caster.World.ClassRegistry.CreateEntity(caster.World.GetEntityType(new("rustboundexpansion:" + code))) as MagicProjectile;
        if (projectile == null) return false;
        projectile.Pos.SetPos(from); projectile.Pos.Dimension = caster.Pos.Dimension;
        var velocity = (toward - from).Normalize() * speed;
        projectile.WatchedAttributes.SetString("rbx-owner", caster.PlayerUID); projectile.WatchedAttributes.SetString("rbx-element", element);
        projectile.WatchedAttributes.SetFloat("rbx-damage", damage); projectile.WatchedAttributes.SetInt("rbx-type", (int)type);
        projectile.WatchedAttributes.SetFloat("rbx-splash", splash);
        projectile.WatchedAttributes.SetString("rbx-appearance", appearance);
        projectile.remaining = maxDistance;
        projectile.WatchedAttributes.SetDouble("rbx-vx", velocity.X); projectile.WatchedAttributes.SetDouble("rbx-vy", velocity.Y); projectile.WatchedAttributes.SetDouble("rbx-vz", velocity.Z);
        projectile.WatchedAttributes.SetLong("rbx-end", caster.World.ElapsedMilliseconds + (long)(maxDistance / speed * 1000));
        ExpansionRuntime.For(caster.World)?.Transients.Add(projectile); caster.World.SpawnEntity(projectile); return true;
    }
    public override void OnGameTick(float dt)
    {
        base.OnGameTick(dt);
        if (World.Side == EnumAppSide.Client)
        {
            RenderColor = WatchedAttributes.GetString("rbx-appearance") == "rock" ? unchecked((int)0xffffffff)
                : Vfx.EntityColor(WatchedAttributes.GetString("rbx-element", "none"));
            if (World.ElapsedMilliseconds >= nextVisual) { nextVisual = World.ElapsedMilliseconds + 100; Vfx.Burst(World, Pos.XYZ, WatchedAttributes.GetString("rbx-element", "none"), .05f, 2); }
            return;
        }
        FlightStep(dt);
    }
    internal void FlightStep(float dt)
    {
        var owner = World.PlayerByUid(WatchedAttributes.GetString("rbx-owner", ""))?.Entity;
        if (owner == null || !owner.Alive || owner.Pos.Dimension != Pos.Dimension || remaining <= 0) { Die(EnumDespawnReason.Removed); return; }
        var velocity = new Vec3d(WatchedAttributes.GetDouble("rbx-vx"), WatchedAttributes.GetDouble("rbx-vy"), WatchedAttributes.GetDouble("rbx-vz"));
        double step = Math.Min(remaining, velocity.Length() * dt); remaining -= step;
        var from = Pos.XYZ; var to = from + velocity.Normalize() * step;
        BlockSelection? block = null; EntitySelection? entity = null;
        World.RayTraceForSelection(from, to, ref block, ref entity, (p, b) => b.GetCollisionBoxes(World.BlockAccessor, p)?.Length > 0, _ => false);
        var wall = block?.HitPosition is { } hit ? block.Position.ToVec3d().Add(hit) : null;
        double first = wall == null ? 1 : Math.Clamp(from.DistanceTo(wall) / Math.Max(step, .00001), 0, 1);
        Entity? victim = null;
        float padding = WatchedAttributes.GetString("rbx-appearance") switch { "cannonball" => .18f, "bullet" => .035f, _ => .1f };
        // Sweep body hitboxes over the whole tick so fast bullets and bolts
        // cannot tunnel through a dummy/creature between discrete positions.
        foreach (var candidate in CombatRules.Enemies(owner, (from + to) * .5, (float)(step * .5 + 4)))
            if (BeamGeometry.TryIntersect(from, to, SpellTargeting.BodyBox(candidate), padding, out double fraction) &&
                (wall == null && victim == null || fraction < first))
            { victim = candidate; first = fraction; }
        if (wall != null || victim != null)
        {
            string element = WatchedAttributes.GetString("rbx-element", "none"); float damage = WatchedAttributes.GetFloat("rbx-damage");
            var type = (EnumDamageType)WatchedAttributes.GetInt("rbx-type");
            var impact = from + (to - from) * first;
            if (victim != null) CombatRules.Hit(owner, victim, damage, element, type);
            float radius = WatchedAttributes.GetFloat("rbx-splash");
            if (radius > 0) foreach (var other in CombatRules.Enemies(owner, impact, radius).Where(e => e != victim)) CombatRules.Hit(owner, other, damage * .5f, element, type);
            if (radius > 0) Vfx.Explosion(World, impact, radius);
            else Vfx.Burst(World, impact, element, .5f);
            Pos.SetPos(impact); Die(EnumDespawnReason.Removed); return;
        }
        Pos.SetPos(to);
    }
}

public sealed class TravelGate : Entity
{
    public string OwnerUid => WatchedAttributes.GetString("rbx-owner", "");
    internal long End => WatchedAttributes.GetLong("rbx-end");
    internal TravelPoint Destination => new(WatchedAttributes.GetDouble("rbx-x"), WatchedAttributes.GetDouble("rbx-y"), WatchedAttributes.GetDouble("rbx-z"),
        WatchedAttributes.GetInt("rbx-dim"), WatchedAttributes.GetFloat("rbx-yaw"));
    private long nextVisual;
    public override bool IsInteractable => false;
    internal void Setup(string owner, TravelPoint destination, long end)
    {
        WatchedAttributes.SetString("rbx-owner", owner); WatchedAttributes.SetLong("rbx-end", end);
        WatchedAttributes.SetDouble("rbx-x", destination.X); WatchedAttributes.SetDouble("rbx-y", destination.Y); WatchedAttributes.SetDouble("rbx-z", destination.Z);
        WatchedAttributes.SetInt("rbx-dim", destination.Dimension); WatchedAttributes.SetFloat("rbx-yaw", destination.Yaw);
    }
    public override void OnGameTick(float dt)
    {
        base.OnGameTick(dt);
        if (World.Side == EnumAppSide.Client && World.ElapsedMilliseconds >= nextVisual)
        { nextVisual = World.ElapsedMilliseconds + 100; Vfx.Burst(World, Pos.XYZ.AddCopy(0, 1, 0), "arcane", 1, 8); }
    }
    internal void Retire() => Die(EnumDespawnReason.Removed);
}
