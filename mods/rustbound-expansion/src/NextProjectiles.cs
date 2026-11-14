using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace RustboundExpansion;

public sealed class NextProjectile : Entity
{
    private static long nextBlast;
    internal static long NextBlast() => Interlocked.Increment(ref nextBlast);
    internal double Traveled, Outward;
    internal bool Returning, Bounced;
    internal int Ricochets;
    private readonly HashSet<long> unique = new(), outwardHits = new(), returnHits = new();
    private long nextVisual;
    public override bool IsInteractable => false;
    internal static bool Launch(EntityPlayer owner, string mode, Vec3d from, Vec3d toward, float damage, float range,
        string element, Entity? target = null, long blast = 0)
    {
        var shot=Prepare(owner,mode,from,toward,damage,range,element,target,blast);
        if(shot==null)return false;
        Spawn(owner,shot);return true;
    }
    internal static NextProjectile? Prepare(EntityPlayer owner, string mode, Vec3d from, Vec3d toward, float damage, float range,
        string element, Entity? target = null, long blast = 0)
    {
        if (from.DistanceTo(toward) < .001 || !float.IsFinite(damage) || damage <= 0) return null;
        string code = mode is "ricochet" or "pellet" ? "nextprojectile-rock" : "nextprojectile";
        var type=owner.World.GetEntityType(new("rustboundexpansion:"+code));
        if(type==null){owner.World.Logger?.Warning("[Crooked Codex] Projectile type unavailable: {0} ({1})",code,mode);return null;}
        var shot = owner.World.ClassRegistry.CreateEntity(type) as NextProjectile;
        if (shot == null){owner.World.Logger?.Warning("[Crooked Codex] Projectile factory returned wrong class: {0} ({1})",code,mode);return null;}
        shot.Pos.SetPos(from); shot.Pos.Dimension = owner.Pos.Dimension;
        shot.WatchedAttributes.SetString("rbx-owner", owner.PlayerUID);
        shot.WatchedAttributes.SetString("rbx-mode", mode); shot.WatchedAttributes.SetString("rbx-element", element);
        shot.WatchedAttributes.SetFloat("rbx-damage", damage); shot.WatchedAttributes.SetFloat("rbx-range", range);
        shot.WatchedAttributes.SetLong("rbx-target", target?.EntityId ?? 0); shot.WatchedAttributes.SetLong("rbx-blast", blast);
        float speed = mode switch { "pellet" => 35, "orb" => 10, "seeking" => 9, "blood" => 16, "star" => 24, "ricochet" => 22, _ => 16 };
        shot.Velocity((toward - from).Normalize() * speed);
        shot.WatchedAttributes.SetLong("rbx-end", owner.World.ElapsedMilliseconds + (mode == "ricochet" ? 10000 : mode == "orb" ? 8000 : (long)(range / speed * 1000) + 1000));
        return shot;
    }
    internal static void Spawn(EntityPlayer owner,NextProjectile shot)
    {
        ExpansionRuntime.For(owner.World)?.Transients.Add(shot);owner.World.SpawnEntity(shot);
        if(shot.WatchedAttributes.GetString("rbx-mode")=="blood")
            owner.World.Logger?.Debug("[Crooked Codex] Blood missile spawned id={0}, code={1}, despawning={2}, target={3}",shot.EntityId,shot.Code,shot.ShouldDespawn,shot.WatchedAttributes.GetLong("rbx-target"));
    }
    internal void LaunchBlood(EntityPlayer owner,Entity target)
    {
        Pos.SetPos(owner.Pos.XYZ.AddCopy(owner.LocalEyePos));Pos.Dimension=owner.Pos.Dimension;
        Velocity((SpellTargeting.BodyCenter(target)-Pos.XYZ).Normalize()*16);
        WatchedAttributes.SetLong("rbx-end",owner.World.ElapsedMilliseconds+(long)(WatchedAttributes.GetFloat("rbx-range")/16*1000)+1000);
        Spawn(owner,this);BloodVfx.Burst(owner.World,Pos.XYZ,.15f,5);
    }
    private Vec3d Velocity() => new(WatchedAttributes.GetDouble("rbx-vx"), WatchedAttributes.GetDouble("rbx-vy"), WatchedAttributes.GetDouble("rbx-vz"));
    private void Velocity(Vec3d v)
    { WatchedAttributes.SetDouble("rbx-vx", v.X); WatchedAttributes.SetDouble("rbx-vy", v.Y); WatchedAttributes.SetDouble("rbx-vz", v.Z); }
    public override void OnGameTick(float dt)
    {
        base.OnGameTick(dt);
        if (World.Side == EnumAppSide.Client)
        {
            bool rock = WatchedAttributes.GetString("rbx-mode") is "ricochet" or "pellet";
            RenderColor = rock ? unchecked((int)0xffffffff) : Vfx.EntityColor(WatchedAttributes.GetString("rbx-element"));
            if (!rock && World.ElapsedMilliseconds >= nextVisual)
            { nextVisual = World.ElapsedMilliseconds + 70; Vfx.Burst(World, Pos.XYZ, WatchedAttributes.GetString("rbx-element"), .03f, 2); }
            return;
        }
        FlightStep(dt);
    }
    internal void FlightStep(float dt)
    {
        var owner = World.PlayerByUid(WatchedAttributes.GetString("rbx-owner", ""))?.Entity;
        if (owner == null || !owner.Alive || owner.ShouldDespawn || owner.Pos.Dimension != Pos.Dimension || World.ElapsedMilliseconds >= WatchedAttributes.GetLong("rbx-end"))
        { Retire(); return; }
        string mode = WatchedAttributes.GetString("rbx-mode"); float range = WatchedAttributes.GetFloat("rbx-range");
        var velocity = Velocity(); double speed = velocity.Length(); if (speed < .001 || dt <= 0 || !float.IsFinite(dt)) { Retire(); return; }
        float elapsed = Math.Min(dt, .25f);
        if (mode == "ricochet") { velocity.Y -= 8 * elapsed; speed = velocity.Length(); }
        var from = Pos.XYZ;
        double ownerDistance = double.PositiveInfinity;
        if (mode == "orb" && Returning)
        {
            var towardOwner = SpellTargeting.BodyCenter(owner) - from;
            ownerDistance = towardOwner.Length();
            if (ownerDistance <= .1) { Retire(); return; }
            velocity = towardOwner.Normalize() * speed;
        }
        else if (mode is "seeking" or "wisp" or "blood" && World.GetEntityById(WatchedAttributes.GetLong("rbx-target")) is { } target && CombatRules.Enemy(owner, target))
        {
            var aim = SpellTargeting.BodyCenter(target) - from;
            if (aim.Length() > .001) velocity = (velocity.Normalize() * Math.Max(0, 1 - dt * 5) + aim.Normalize() * Math.Min(1, dt * 5)).Normalize() * speed;
        }
        Velocity(velocity);
        double distance = speed * elapsed;
        distance = Math.Min(distance, ownerDistance);
        distance = Math.Min(distance, mode == "orb" ? Returning ? 48 - Traveled : range - Outward : range - Traveled);
        if (distance <= .00001)
        { if (mode == "orb" && !Returning) { Returning = true; return; } Retire(); return; }
        // Vec3d.Normalize mutates its receiver. Preserve the velocity magnitude
        // for reflection; normalizing it here previously made bounces crawl.
        var to = from + velocity.Clone().Normalize() * distance;
        BlockSelection? block = null; EntitySelection? selected = null;
        World.RayTraceForSelection(from, to, ref block, ref selected, (p, b) => b.GetCollisionBoxes(World.BlockAccessor, p)?.Length > 0, _ => false);
        var wall = block?.HitPosition is { } wallHit ? block.Position.ToVec3d().Add(wallHit) : null;
        double wallFraction = wall == null ? 1 : Math.Clamp(from.DistanceTo(wall) / distance, 0, 1);
        var hits = new List<(Entity Target, double Fraction)>();
        foreach (var enemy in CombatRules.Enemies(owner, (from + to) * .5, (float)(distance * .5 + 4)))
            if (BeamGeometry.TryIntersect(from, to, SpellTargeting.BodyBox(enemy), mode == "pellet" ? .025 : .1, out double f) && (wall == null || f < wallFraction)) hits.Add((enemy, f));
        foreach (var hit in hits.OrderBy(h => h.Fraction).ThenBy(h => h.Target.EntityId))
        {
            if (mode == "orb")
            {
                var leg = Returning ? returnHits : outwardHits;
                if (leg.Contains(hit.Target.EntityId) || unique.Count >= 3 && !unique.Contains(hit.Target.EntityId)) continue;
                leg.Add(hit.Target.EntityId); unique.Add(hit.Target.EntityId);
            }
            var point = from + (to - from) * hit.Fraction;
            double traveled = Traveled + distance * hit.Fraction;
            if (mode == "pellet" && traveled >= 8)
                ExpansionRuntime.For(World)?.CelebrateBuckshot(WatchedAttributes.GetLong("rbx-blast"), point);
            else
            {
                float damage = WatchedAttributes.GetFloat("rbx-damage") * (mode == "pellet" ? Falloff(traveled) : 1);
                CombatRules.Hit(owner, hit.Target, damage, WatchedAttributes.GetString("rbx-element", "none"), mode is "ricochet" or "pellet" ? EnumDamageType.BluntAttack : mode=="blood"?EnumDamageType.PiercingAttack:EnumDamageType.Injury);
                Vfx.Burst(World, point, WatchedAttributes.GetString("rbx-element", "none"), .15f, 4);
            }
            if (mode != "orb") { Traveled = traveled; Pos.SetPos(point); Retire(); return; }
        }
        if (wall != null)
        {
            Traveled += distance * wallFraction; Outward += distance * wallFraction; Pos.SetPos(wall);
            if (mode == "ricochet" && Ricochets < 3)
            {
                Bounced = true; Ricochets++;
                var n = block!.Face?.Normalf?.ToVec3d() ?? velocity.Clone().Normalize() * -1;
                double dot = velocity.X * n.X + velocity.Y * n.Y + velocity.Z * n.Z;
                var reflected = (velocity - n * (2 * dot)) * .9; Velocity(reflected);
                Pos.SetPos(wall + n * .09); Vfx.Burst(World, wall, "earth", .25f, 10); return;
            }
            if (mode == "pellet" && Traveled >= 8) ExpansionRuntime.For(World)?.CelebrateBuckshot(WatchedAttributes.GetLong("rbx-blast"), wall);
            else Vfx.Burst(World, wall, WatchedAttributes.GetString("rbx-element", "none"), .1f, 3);
            Retire(); return;
        }
        Traveled += distance; Outward += distance; Pos.SetPos(to);
        if (mode == "orb" && Returning && distance >= ownerDistance - .0001) { Retire(); return; }
        if (mode == "orb" && !Returning && Outward >= range - .0001) Returning = true;
        else if (mode != "orb" && Traveled >= range - .0001) Retire();
    }
    internal void Retire() { Die(EnumDespawnReason.Removed); DespawnReason = new() { Reason = EnumDespawnReason.Removed }; }
    internal static float Falloff(double distance) => distance switch
    {
        <= 2 => 1,
        < 4 => (float)(1 - (distance - 2) * .35),
        < 6 => (float)(.3 - (distance - 4) * .125),
        < 8 => (float)(.05 - (distance - 6) * .025),
        _ => 0
    };
}
