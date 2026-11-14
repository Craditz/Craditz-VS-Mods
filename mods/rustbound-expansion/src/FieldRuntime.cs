using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace RustboundExpansion;

public sealed partial class ExpansionRuntime
{
    internal sealed class CinderState(EntityPlayer caster,float damage,long end)
    {
        internal readonly EntityPlayer Caster=caster;
        internal readonly float Damage=damage;
        internal readonly long End=end;
        internal readonly int Dimension=caster.Pos.Dimension;
        internal readonly TrailPath Path=new(60,6000);
        internal long Visual,DamageTick;
    }
    internal sealed record WallopState(EntityPlayer Caster,Entity Target,Vec3d Direction,float Damage,long End,int Dimension);
    internal readonly Dictionary<long,CinderState> Cinders=[];
    internal readonly Dictionary<long,WallopState> Wallops=[];
    internal static (double Horizontal,double Vertical) WallopMotion(float power)
    {
        if(!float.IsFinite(power))return (0,0);
        double p=Math.Clamp(power,0,8);
        // Cubic instead of linear scaling: +100% power gives eight times the horizontal launch.
        return (Math.Min(6,.4*p*p*p),.09);
    }
    internal bool CastField(FieldSpell spell,EntityPlayer caster,Entity? target,float damage,float duration)
    {
        var d=spell.Definition;
        if(!float.IsFinite(damage)||!float.IsFinite(duration)||duration<0)return false;
        if(d.Kind=="wallop")
        {
            if(target==null || !CombatRules.Enemy(caster,target) || Active(target,"stasis"))return false;
            var motion=WallopMotion(damage/d.Damage);if(motion.Horizontal<=0)return false;
            if(!CombatRules.Hit(caster,target,damage,"none",EnumDamageType.BluntAttack))return false;
            if(!target.Alive)return true;
            var direction=target.Pos.XYZ-caster.Pos.XYZ;direction.Y=0;
            if(direction.Length()<.001)direction.Set(-Math.Sin(caster.Pos.Yaw),0,-Math.Cos(caster.Pos.Yaw));
            direction.Normalize();
            // Use the native queue: ground movement runs before knockback and uses
            // dmgkb to suppress coyote-time drag. Clearing it loses horizontal distance.
            target.WatchedAttributes.SetDouble("kbdirX",direction.X*motion.Horizontal);
            target.WatchedAttributes.SetDouble("kbdirY",motion.Vertical);
            target.WatchedAttributes.SetDouble("kbdirZ",direction.Z*motion.Horizontal);
            target.Attributes.SetInt("dmgkb",1);
            target.CollidedHorizontally=false;
            target.WatchedAttributes.MarkPathDirty("kbdirX");target.WatchedAttributes.MarkPathDirty("kbdirY");target.WatchedAttributes.MarkPathDirty("kbdirZ");
            Wallops[target.EntityId]=new(caster,target,direction,damage*2,Now+2500,target.Pos.Dimension);
            Vfx.Mark(target,"wind");return true;
        }
        if(duration<=0)return false;
        string? bread=d.Kind=="breadcrumbs"?BreadcrumbBread.Choose(World):null;
        if(d.Kind=="breadcrumbs" && bread==null)return false;
        ClearField(caster,d.Id);Marker(caster,d.Id,duration);
        if(!Active(caster,d.Id))return false;
        if(bread!=null)BreadcrumbBread.Begin(caster,bread);
        if(d.Kind=="cinder")
        {
            var state=new CinderState(caster,damage,Now+(long)(duration*1000));Cinders[caster.EntityId]=state;
            state.Path.Sample(World,caster.Pos.XYZ,caster.Pos.Dimension,Now);
        }
        return true;
    }
    internal void TickField()
    {
        foreach(var pair in Wallops.ToArray())
        {
            var s=pair.Value;var target=s.Target;
            if(Now>=s.End || !s.Caster.Alive || s.Caster.ShouldDespawn || s.Caster.Pos.Dimension!=s.Dimension
                || !target.Alive || target.ShouldDespawn || target.Pos.Dimension!=s.Dimension || Active(target,"stasis")
                || !CombatRules.Enemy(s.Caster,target)) {Wallops.Remove(pair.Key);continue;}
            if(target.Attributes.GetInt("dmgkb")==1 || !target.CollidedHorizontally && !StoppedAtWall(s))continue;
            // One wall hit per launch; ground/ceiling collisions do not count.
            Wallops.Remove(pair.Key);
            if(CombatRules.Hit(s.Caster,target,s.Damage,"none",EnumDamageType.BluntAttack))Vfx.Mark(target,"earth");
        }
        foreach(var pair in Cinders.ToArray())
        {
            var s=pair.Value;var p=s.Caster;
            if(!p.Alive || p.ShouldDespawn || p.Pos.Dimension!=s.Dimension) {ClearField(p,"rbxcindertrail1");continue;}
            s.Path.Points.RemoveAll(t=>Now>=t.End);
            if(Now<s.End && Active(p,"rbxcindertrail1") && !ControlEffects.Banished(p) && !Active(p,"stasis"))
                s.Path.Sample(World,p.Pos.XYZ,s.Dimension,Now);
            else if(Now<s.End) {ClearField(p,"rbxcindertrail1");continue;}
            if(Now>=s.End && s.Path.Points.Count==0){ClearField(p,"rbxcindertrail1");continue;}
            if(Now>=s.Visual)
            {s.Visual=Now+500;foreach(var point in s.Path.Points)TrailVisual.Emit(World,point.Position,true);}
            if(Now<s.DamageTick)continue;s.DamageTick=Now+1000;
            var victims=new Dictionary<long,Entity>();
            foreach(var point in s.Path.Points)
                foreach(var enemy in CombatRules.Enemies(p,point.Position,.85f))
                    if(Math.Abs(enemy.Pos.InternalY-point.Position.Y)<.75 && !Active(enemy,"stasis"))victims.TryAdd(enemy.EntityId,enemy);
            foreach(var target in victims.Values)
            {
                if(!p.Alive || p.ShouldDespawn || !Cinders.ContainsKey(pair.Key))break;
                CombatRules.Hit(p,target,s.Damage,"fire");
            }
        }
    }
    private bool StoppedAtWall(WallopState state)
    {
        var target=state.Target;
        // Physics can set and clear CollidedHorizontally between our 100 ms ticks.
        // A stopped launch against a solid vertical face still counts, once.
        if(target.Pos.Motion.X*state.Direction.X+target.Pos.Motion.Z*state.Direction.Z>.03)return false;
        var from=SpellTargeting.BodyCenter(target);
        double reach=Math.Max(target.CollisionBox?.XSize??.6f,target.CollisionBox?.ZSize??.6f)*.5+.15;
        BlockSelection? block=null;EntitySelection? entity=null;
        World.RayTraceForSelection(from,from+state.Direction*reach,ref block,ref entity,
            (pos,candidate)=>candidate.GetCollisionBoxes(World.BlockAccessor,pos)?.Length>0,_=>false);
        return block?.Face?.IsHorizontal==true;
    }
    internal void FieldRemoved(Entity target,string id)
    {
        if(id=="rbxbreadcrumbs1")BreadcrumbBread.Forget(target);
        // Natural expiry leaves the last embers for their remaining six seconds.
        if(id=="rbxcindertrail1" && Cinders.TryGetValue(target.EntityId,out var s) && Now<s.End)Cinders.Remove(target.EntityId);
    }
    internal void ClearField(Entity target,string? id=null)
    {
        if(id==null || id=="rbxbreadcrumbs1")BreadcrumbBread.Forget(target);
        if(id==null || id=="rbxcindertrail1")Cinders.Remove(target.EntityId);
        foreach(var d in FieldCatalog.All.Where(d=>id==null || d.Id==id))if(Active(target,d.Id))Unmark(target,d.Id);
        if(id==null)
            foreach(var pair in Wallops.Where(w=>w.Value.Target==target || w.Value.Caster==target).ToArray())Wallops.Remove(pair.Key);
    }
    private void DisposeField()
    {
        foreach(var s in Cinders.Values.ToArray())ClearField(s.Caster);
        Cinders.Clear();Wallops.Clear();
    }
}
