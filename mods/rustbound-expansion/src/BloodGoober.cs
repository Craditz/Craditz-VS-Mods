using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace RustboundExpansion;

public class BloodGoober : EntityAgent
{
    internal bool Settled=>WatchedAttributes.GetBool("rbx-settled");
    internal string OwnerUid=>WatchedAttributes.GetString("rbx-owner","");
    private long nextAttack;
    private int seenAttack;
    private BloodNavigation? navigation;
    internal long TargetId=>WatchedAttributes.GetLong("rbx-combat-target");
    internal int HostIndex=>Math.Clamp(WatchedAttributes.GetInt("rbx-index"),0,2);
    internal void Setup(EntityPlayer owner,int index,float damage,long end,float investment)
    {
        WatchedAttributes.SetString("rbx-owner",owner.PlayerUID);WatchedAttributes.SetInt("rbx-index",index);
        WatchedAttributes.SetFloat("rbx-damage",damage);WatchedAttributes.SetLong("rbx-end",end);
        WatchedAttributes.SetFloat("rbx-investment",investment);WatchedAttributes.SetFloat("rbx-initialmax",20);
        nextAttack=owner.World.ElapsedMilliseconds+index*170;
    }
    public override void OnGameTick(float dt)
    {
        base.OnGameTick(dt);
        if(World.Side==EnumAppSide.Client)
        {
            int attack=WatchedAttributes.GetInt("rbx-attack");
            if(attack!=seenAttack){seenAttack=attack;AnimManager?.StopAnimation("attack");AnimManager?.StartAnimation("attack");}
            return;
        }
        GooberStep(dt);
    }
    internal void GooberStep(float dt)
    {
        if(!float.IsFinite(dt) || dt<=0)return;
        Controls.WalkVector.Set(0,0,0);Controls.Forward=false;
        var owner=World.PlayerByUid(OwnerUid)?.Entity;
        if(Settled || !Alive || owner==null || !owner.Alive || owner.ShouldDespawn || owner.Pos.Dimension!=Pos.Dimension)
        {World.Logger?.Debug("[Crooked Codex] Crimson Host goober {0} retired: settled={1}, alive={2}, ownerFound={3}, ownerAlive={4}, sameDimension={5}",EntityId,Settled,Alive,owner!=null,owner?.Alive,owner?.Pos.Dimension==Pos.Dimension);Settle(false);Retire();return;}
        if(World.ElapsedMilliseconds>=WatchedAttributes.GetLong("rbx-end")){Settle(true);Retire();return;}
        if(ControlEffects.Banished(this)||ControlEffects.Banished(owner)){navigation?.Stop();return;}
        var runtime=ExpansionRuntime.For(World);
        if(runtime==null || !runtime.BloodHosts.TryGetValue(owner,out var host) || !host.Contains(this) || !BloodAttunement.Allows(owner,3))
        {World.Logger?.Debug("[Crooked Codex] Crimson Host goober {0} retired: runtime/host membership or attunement invalid",EntityId);Settle(false);Retire();return;}
        var candidates=CombatRules.Enemies(owner,owner.Pos.XYZ,12).Where(e=>e is not BloodGoober).ToArray();
        var target=ChooseTarget(candidates,host);
        if(TargetId!=(target?.EntityId??0))WatchedAttributes.SetLong("rbx-combat-target",target?.EntityId??0);
        (navigation??=new(this)).Tick(target??owner,target!=null,host);
        if(target==null || target.Pos.XYZ.DistanceTo(Pos.XYZ)>2 || World.ElapsedMilliseconds<nextAttack
            || !SpellTargeting.HasLineOfSight(World,BloodNavigation.BodyPoint(this),BloodNavigation.BodyPoint(target)))return;
        var look=target.Pos.XYZ-Pos.XYZ;Pos.Yaw=(float)Math.Atan2(look.Z,-look.X);
        nextAttack=World.ElapsedMilliseconds+1050+HostIndex*150;
        WatchedAttributes.SetInt("rbx-attack",WatchedAttributes.GetInt("rbx-attack")+1);
        CombatRules.Hit(owner,target,WatchedAttributes.GetFloat("rbx-damage"),"none",EnumDamageType.PiercingAttack);
        BloodVfx.Mark(World,target);
    }
    internal Entity? ChooseTarget(IEnumerable<Entity> candidates,IReadOnlyList<BloodGoober> host)
        =>candidates.OrderBy(e=>host.Count(g=>g!=this && g.Alive && !g.Settled && g.TargetId==e.EntityId))
            .ThenBy(e=>e.EntityId==TargetId?0:1)
            .ThenBy(e=>e.Pos.XYZ.SquareDistanceTo(Pos.XYZ)).ThenBy(e=>e.EntityId).FirstOrDefault();
    internal Vec3d Approach(Entity target,double radius)
    {
        double angle=HostIndex*Math.PI*2/3;
        return target.Pos.XYZ.AddCopy(Math.Cos(angle)*radius,0,Math.Sin(angle)*radius);
    }
    internal void Steer(Vec3d destination,double stop=.85,IReadOnlyList<BloodGoober>? siblings=null)
    {
        var delta=destination-Pos.XYZ;delta.Y=0;double distance=delta.Length();
        var direction=distance>stop?delta.Normalize():new Vec3d();
        if(siblings!=null)
            foreach(var other in siblings.Where(g=>g!=this && g.Alive && !g.Settled))
            {
                if(Math.Abs(Pos.Y-other.Pos.Y)>1)continue;
                var away=Pos.XYZ-other.Pos.XYZ;away.Y=0;double spacing=away.Length();
                if(spacing>=.9)continue;
                if(spacing<.001)away.Set(Math.Cos(HostIndex*Math.PI*2/3),0,Math.Sin(HostIndex*Math.PI*2/3));
                else away.Mul(1/spacing);
                direction.Add(away*(2*(.9-spacing)/.9));
            }
        Controls.WalkVector.Set(0,0,0);Controls.Forward=direction.LengthSq()>.001;
        if(distance>.001)Pos.Yaw=(float)Math.Atan2(delta.Z,-delta.X);
        if(Controls.Forward)Controls.WalkVector.Set(direction.Normalize()*(.025*GlobalConstants.OverallSpeedMultiplier*(.92+HostIndex*.08)));
        // Native controlledphysics owns movement, steps, collision and gravity.
        // In particular, never overwrite Motion.Y or translate through terrain.
    }
    internal void Settle(bool refund)
    {
        if(World?.Side!=EnumAppSide.Server || Settled)return;
        WatchedAttributes.SetBool("rbx-settled",true);
        var owner=World.PlayerByUid(OwnerUid)?.Entity;
        if(!refund || !Alive || ShouldDespawn || owner==null || !owner.Alive || owner.ShouldDespawn || owner.Pos.Dimension!=Pos.Dimension
            || GetBehavior<EntityBehaviorHealth>() is not {} h || owner.GetBehavior<EntityBehaviorHealth>() is not {} hp)return;
        float amount=ExpansionRuntime.HostRefund(WatchedAttributes.GetFloat("rbx-investment"),h.Health,WatchedAttributes.GetFloat("rbx-initialmax"));
        if(amount>0)hp.Health=Math.Min(hp.MaxHealth,hp.Health+amount);
    }
    internal void Retire()
    {
        navigation?.Stop();
        Settle(false);Die(EnumDespawnReason.Removed);
        DespawnReason=new(){Reason=EnumDespawnReason.Removed};
    }
    public override void Die(EnumDespawnReason reason=EnumDespawnReason.Death,DamageSource? source=null)
    {
        navigation?.Stop();
        Settle(false);base.Die(reason,source);
        DespawnReason=new(){Reason=EnumDespawnReason.Removed,DamageSourceForDeath=source};
    }
}
