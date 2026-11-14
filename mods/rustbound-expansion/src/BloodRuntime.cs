using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace RustboundExpansion;

public sealed partial class ExpansionRuntime
{
    internal sealed record BloodEffect(BloodSpell Spell,EntityPlayer Caster,Entity Target,long End,float Power)
    { internal long Next; internal Vec3d Last=Target.Pos.XYZ; internal float Distance, Budget=2; internal long BudgetAt; }
    internal sealed record BloodVolley(EntityPlayer Caster,Entity Target,List<NextProjectile> Shots)
    { internal int Index=1; internal long Next; }
    internal readonly List<BloodVolley> BloodVolleys=new();
    internal readonly Dictionary<(Entity Target,string Id),BloodEffect> BloodEffects=new();
    internal readonly Dictionary<EntityPlayer,List<BloodGoober>> BloodHosts=new(ReferenceEqualityComparer.Instance);
    internal sealed record BloodDrain(EntityPlayer Caster,BloodSpell Spell,Entity Target,Vec3d Anchor,long Started,long End)
    { internal long LastStep,Next; }
    internal readonly Dictionary<EntityPlayer,BloodDrain> BloodDrains=new(ReferenceEqualityComparer.Instance);

    internal static bool BloodAlly(EntityPlayer caster,Entity target)=>Ally(caster,target) && !Active(target,"stasis") && target.GetBehavior<EntityBehaviorHealth>() is {} h && h.Health>0;
    internal static float MissingHealth(Entity target)=>target.GetBehavior<EntityBehaviorHealth>() is {} h?Math.Max(0,h.MaxHealth-h.Health):0;
    private Entity BloodRecipient(EntityPlayer caster,Entity? selected)=>selected!=null && BloodAlly(caster,selected)?selected:caster;
    internal bool CastBlood(BloodSpell spell,EntityPlayer caster,Entity? selected,BloodCosts.Payment? providedPayment=null)
    {
        if(World.Side!=EnumAppSide.Server || !BloodAttunement.Allows(caster,spell.Tier))return false;
        var d=spell.Definition;float cost=BloodCosts.Cost(caster,d.Kind);
        if(!BloodCosts.Affordable(caster,cost))return false;
        using var ownedPayment=providedPayment==null?new BloodCosts.Payment(caster):null;
        var payment=providedPayment??ownedPayment!;
        float power=spell.ModifiedStats(caster),damage=spell.ModifiedDamage(caster),duration=spell.ModifiedDuration(caster);
        var recipient=BloodRecipient(caster,selected);
        switch(d.Kind)
        {
            case "bloodletting":
                int current=caster.WatchedAttributes.GetInt("entitybehavior-resource-currentmana_rm");
                int maximum=caster.WatchedAttributes.GetInt("entitybehavior-resource-totalmaxmana_rm");
                if(maximum<=current || power<=0 || !payment.TryPay(cost))return false;
                caster.WatchedAttributes.SetInt("entitybehavior-resource-currentmana_rm",(int)Math.Min(maximum,(long)current+(int)Math.Floor(power)));
                caster.WatchedAttributes.MarkPathDirty("entitybehavior-resource-currentmana_rm");return true;
            case "transfusion":
                if(selected==null || selected==caster || !BloodAlly(caster,selected) || MissingHealth(selected)<=0 || !payment.TryPay(cost))return false;
                Heal(caster,selected,power);BloodVfx.Mark(World,selected);return true;
            case "clot": return SetBloodEffect(spell,caster,recipient,duration,power,0,payment);
            case "bloodscent": return SetBloodEffect(spell,caster,caster,duration,power,0,payment);
            case "crimsonaegis": return SetBloodEffect(spell,caster,recipient,duration,power,cost,payment);
            case "redharvest": case "openwound":
                if(selected==null || !BloodEnemy(caster,selected,d.Range))return false;
                return SetBloodEffect(spell,caster,selected,duration,d.Kind=="openwound"?damage:power,0,payment);
            case "bloodmissiles":
                if(selected==null || !BloodEnemy(caster,selected,d.Range))return false;
                var from=caster.Pos.XYZ.AddCopy(caster.LocalEyePos);
                var shots=new List<NextProjectile>();
                for(int i=0;i<3;i++)
                {
                    var shot=NextProjectile.Prepare(caster,"blood",from.AddCopy((i-1)*.12,0,0),SpellTargeting.BodyCenter(selected),damage,d.Range,"blood",selected);
                    if(shot==null)return false;
                    shots.Add(shot);
                }
                if(!payment.TryPay(cost))return false;
                shots[0].LaunchBlood(caster,selected);
                BloodVolleys.Add(new(caster,selected,shots){Next=Now+200});return true;
            case "heartburst":
                var victims=CombatRules.Enemies(caster,caster.Pos.XYZ,d.Range).Where(e=>SpellTargeting.HasLineOfSight(World,SpellTargeting.BodyCenter(caster),SpellTargeting.BodyCenter(e))).ToArray();
                if(victims.Length==0 || cost<=0 || !payment.TryPay(cost))return false;
                foreach(var target in victims)CombatRules.Hit(caster,target,damage*payment.ActualPaid);
                BloodVfx.Burst(World,SpellTargeting.BodyCenter(caster),d.Range);return true;
            case "crimsonhost": return SummonBloodHost(spell,caster,cost,duration,damage,payment);
        }
        return false;
    }
    internal static bool BloodEnemy(EntityPlayer caster,Entity target,float range)=>CombatRules.Enemy(caster,target)
        && target.Pos.XYZ.SquareDistanceTo(caster.Pos.XYZ)<=range*range
        && SpellTargeting.HasLineOfSight(caster.World,caster.Pos.XYZ.AddCopy(caster.LocalEyePos),SpellTargeting.BodyCenter(target));

    internal bool SetBloodEffect(BloodSpell spell,EntityPlayer caster,Entity target,float duration,float power,float cost,BloodCosts.Payment payment)
    {
        if(!float.IsFinite(duration)||duration<=0||!float.IsFinite(power)||!BloodCosts.Affordable(caster,cost))return false;
        if(spell.Definition.Kind=="crimsonaegis" && BloodEffects.TryGetValue((target,spell.Id),out var old) && old.Power>power)return false;
        Marker(target,spell.Id,duration);
        if(!Active(target,spell.Id))return false;
        if(!payment.TryPay(cost)){Unmark(target,spell.Id);return false;}
        BloodEffects[(target,spell.Id)]=new(spell,caster,target,Now+(long)(duration*1000),power){Next=Now+1000,BudgetAt=Now};
        return true;
    }
    internal void BloodEffectRemoved(Entity target,string id)
    {
        BloodEffects.Remove((target,id));
        if(id==BloodCatalog.Kind("crimsonhost").Id && target is EntityPlayer owner)EndBloodHost(owner,true);
    }
    private void RemoveBloodEffect(BloodEffect effect)
    {BloodEffects.Remove((effect.Target,effect.Spell.Id));Unmark(effect.Target,effect.Spell.Id);}

    internal void TickBlood()
    {
        foreach(var volley in BloodVolleys.ToArray())
        {
            var p=volley.Caster;
            if(!p.Alive || p.ShouldDespawn || !BloodAttunement.Allows(p,2) || ControlEffects.Banished(p)
                || !BloodEnemy(p,volley.Target,BloodCatalog.Kind("bloodmissiles").Range))
            {BloodVolleys.Remove(volley);continue;}
            if(Now<volley.Next)continue;
            volley.Shots[volley.Index++].LaunchBlood(p,volley.Target);volley.Next=Now+200;
            if(volley.Index==volley.Shots.Count)BloodVolleys.Remove(volley);
        }
        foreach(var effect in BloodEffects.Values.ToArray())
        {
            if(!BloodEffects.ContainsKey((effect.Target,effect.Spell.Id)))continue;
            var p=effect.Caster;var target=effect.Target;string kind=effect.Spell.Definition.Kind;
            if(Now>=effect.End || !p.Alive || p.ShouldDespawn || !target.Alive || target.ShouldDespawn || target.Pos.Dimension!=p.Pos.Dimension
                || !Active(target,effect.Spell.Id) || !BloodAttunement.Allows(p,effect.Spell.Tier))
            {RemoveBloodEffect(effect);continue;}
            if(ControlEffects.Banished(target)||ControlEffects.Banished(p))continue;
            if(kind=="openwound")
            {
                if(!CombatRules.Enemy(p,target)){RemoveBloodEffect(effect);continue;}
                // Token bucket limits damage from sprints and teleports without
                // turning a normal movement tick into an uncapped burst.
                effect.Budget=Math.Min(2,effect.Budget+(Now-effect.BudgetAt)*.002f);effect.BudgetAt=Now;
                double movement=target.Pos.XYZ.DistanceTo(effect.Last);effect.Last=target.Pos.XYZ;
                float paid=(float)Math.Min(effect.Budget,movement);effect.Budget-=paid;effect.Distance+=paid;
                if(effect.Distance>=.25f){float distance=effect.Distance;effect.Distance=0;CombatRules.Hit(p,target,effect.Power*distance);BloodVfx.Mark(World,target);}
            }
            if(Now<effect.Next)continue;effect.Next=Now+1000;
            if(kind=="clot")Heal(p,target,effect.Power);
            if(kind=="bloodscent")BloodScentSystem.Emit(p,ScentTargets(p,ScentRange(effect.Spell.Range,effect.Power)));
        }
        foreach(var (owner,host) in BloodHosts.ToArray())
        {
            if(!owner.Alive || owner.ShouldDespawn || !BloodAttunement.Allows(owner,3)){EndBloodHost(owner,false);continue;}
            if(!Active(owner,BloodCatalog.Kind("crimsonhost").Id))EndBloodHost(owner,true);
            else if(host.All(g=>g.Settled)){BloodHosts.Remove(owner);Unmark(owner,BloodCatalog.Kind("crimsonhost").Id);}
        }
        TickBloodDrains();
    }
    internal static float ScentRange(float baseRange,float potency)
        =>float.IsFinite(potency)?Math.Clamp(baseRange*(1+4*Math.Max(0,potency/baseRange-1)),baseRange,512):baseRange;
    internal Entity[] ScentTargets(EntityPlayer p,float range)=>World.GetEntitiesAround(p.Pos.XYZ,range,range)
        .Where(e=>e!=p && e is EntityAgent && e.Alive && !e.ShouldDespawn && e.Pos.Dimension==p.Pos.Dimension && !ControlEffects.Banished(e)
            && e.GetBehavior<EntityBehaviorHealth>() is { MaxHealth:>0 } h && h.Health>0 && h.Health<h.MaxHealth
            && e.Pos.XYZ.SquareDistanceTo(p.Pos.XYZ)<=Math.Pow(range*(.25+.75*(1-Math.Clamp(h.Health/h.MaxHealth,0,1))),2))
        .DistinctBy(e=>e.EntityId).OrderBy(e=>e.Pos.XYZ.SquareDistanceTo(p.Pos.XYZ)).Take(64).ToArray();

    internal void AbsorbBloodDamage(Entity target,DamageSource source,ref float damage)
    {
        if(source.Type==EnumDamageType.Heal || !float.IsFinite(damage)||damage<=0)return;
        var id=BloodCatalog.Kind("crimsonaegis").Id;
        if(!BloodEffects.TryGetValue((target,id),out var effect) || Now>=effect.End || !Active(target,id))return;
        float absorbed=Math.Min(damage,effect.Power);damage-=absorbed;
        BloodEffects[(target,id)]=effect with {Power=effect.Power-absorbed};
        if(effect.Power-absorbed<=.0001f)RemoveBloodEffect(effect);
    }
    internal void BloodActualDamage(Entity target,float actual)
    {
        if(actual>0 && BloodEffects.TryGetValue((target,BloodCatalog.Kind("clot").Id),out var clot))RemoveBloodEffect(clot);
    }
    internal void BloodDeath(Entity dead)
    {
        var marks=BloodEffects.Values.Where(e=>e.Target==dead && e.Spell.Definition.Kind=="redharvest").ToArray();
        foreach(var mark in marks)
        {
            RemoveBloodEffect(mark);
            if(!mark.Caster.Alive || mark.Caster.ShouldDespawn || mark.Caster.Pos.Dimension!=dead.Pos.Dimension || !BloodAttunement.Allows(mark.Caster,2))continue;
            var allies=Allies(mark.Caster,dead.Pos.XYZ,8).Where(a=>MissingHealth(a)>0).ToArray();
            foreach(var ally in allies){Heal(mark.Caster,ally,mark.Power/allies.Length);BloodVfx.Mark(World,ally);}
        }
        if(dead is BloodGoober goober)goober.Settle(false);
    }
    internal void ClearBloodTarget(Entity target)
    {foreach(var effect in BloodEffects.Values.Where(e=>e.Target==target).ToArray())RemoveBloodEffect(effect);}
    internal void ClearBloodOwner(EntityPlayer p)
    {
        BloodVolleys.RemoveAll(v=>v.Caster==p);
        EndBloodHost(p,false);StopBloodDrain(p);
        foreach(var effect in BloodEffects.Values.Where(e=>e.Caster==p).ToArray())RemoveBloodEffect(effect);
    }
    private bool SummonBloodHost(BloodSpell spell,EntityPlayer p,float cost,float duration,float damage,BloodCosts.Payment payment)
    {
        if(BloodHosts.ContainsKey(p))return false;
        var type=World.GetEntityType(new("rustboundexpansion:bloodgoober"));
        if(type==null){World.Logger?.Warning("[Crooked Codex] Crimson Host entity type unavailable");return false;}
        var positions=BloodHostPositions(p);
        if(positions.Count<3){World.Logger?.Warning("[Crooked Codex] Crimson Host could not find three clear nearby spawn positions");return false;}
        var goobers=new List<BloodGoober>();
        for(int i=0;i<3;i++)
        {
            var goober=World.ClassRegistry.CreateEntity(type) as BloodGoober;
            if(goober==null){World.Logger?.Warning("[Crooked Codex] Crimson Host factory returned wrong class");return false;}
            goober.Pos.SetPos(positions[i]);goober.Pos.Dimension=p.Pos.Dimension;goobers.Add(goober);
        }
        Marker(p,spell.Id,duration);if(!Active(p,spell.Id))return false;
        if(!payment.TryPay(cost)){Unmark(p,spell.Id);return false;}
        BloodHosts[p]=goobers;
        for(int i=0;i<goobers.Count;i++)
        {
            var goober=goobers[i];goober.Setup(p,i,damage,Now+(long)(duration*1000),payment.ActualPaid/3);
            Transients.Add(goober);World.SpawnEntity(goober);
            BloodVfx.Burst(World,goober.Pos.XYZ,.3f,12);
            World.Logger?.Debug("[Crooked Codex] Crimson Host spawned id={0}, code={1}, despawning={2}, health={3}",goober.EntityId,goober.Code,goober.ShouldDespawn,goober.GetBehavior<EntityBehaviorHealth>()?.Health??-1);
            if(goober.GetBehavior<EntityBehaviorHealth>() is {MaxHealth:>0} h)goober.WatchedAttributes.SetFloat("rbx-initialmax",h.MaxHealth);
        }
        return true;
    }
    internal List<Vec3d> BloodHostPositions(EntityPlayer caster)
    {
        var result=new List<Vec3d>();var eye=caster.Pos.XYZ.AddCopy(caster.LocalEyePos);
        var box=new Cuboidf(-.35f,0,-.35f,.35f,.95f,.35f);
        // Do not require a clear full circle behind a caster standing by a wall.
        foreach(double height in new[]{.02,.5})
        foreach(double radius in new[]{1.2,.8,1.7})
        for(int step=0;step<12;step++)
        {
            double angle=caster.Pos.Yaw+step*Math.PI/6;
            var at=caster.Pos.XYZ.AddCopy(-Math.Sin(angle)*radius,height,-Math.Cos(angle)*radius);
            if(result.Any(p=>p.SquareDistanceTo(at)<.65*.65) || !SpellTargeting.HasLineOfSight(World,eye,at))continue;
            if(World.CollisionTester?.IsColliding(World.BlockAccessor,box,at,false)==true)continue;
            result.Add(at);if(result.Count==3)return result;
        }
        return result;
    }
    internal void EndBloodHost(EntityPlayer p,bool refund)
    {
        if(!BloodHosts.Remove(p,out var host))return;
        foreach(var goober in host){goober.Settle(refund);goober.Retire();}
        Unmark(p,BloodCatalog.Kind("crimsonhost").Id);
    }
    internal static float HostRefund(float investment,float current,float initialMaximum)
        =>float.IsFinite(investment)&&float.IsFinite(current)&&float.IsFinite(initialMaximum)&&investment>0&&initialMaximum>0
            ?.75f*investment*Math.Clamp(current/initialMaximum,0,1):0;
}

internal static class BloodVfx
{
    internal static void Line(IWorldAccessor world,Vec3d from,Vec3d to)
    {int points=Math.Clamp((int)Math.Ceiling(from.DistanceTo(to)*2),2,48);for(int i=0;i<=points;i++)Burst(world,from+(to-from)*(i/(double)points),.02f,1);}
    internal static void Mark(IWorldAccessor world,Entity target)=>Burst(world,SpellTargeting.BodyCenter(target),.35f,5);
    internal static void Burst(IWorldAccessor world,Vec3d center,float radius,int quantity=30)
    {
        try{world.SpawnParticles(new SimpleParticleProperties(quantity,quantity,ColorUtil.ToRgba(235,190,20,45),
            center.AddCopy(-.15,-.15,-.15),center.AddCopy(.15,.15,.15),
            new Vec3f(-radius,.2f,-radius),new Vec3f(radius,Math.Max(.5f,radius*.4f),radius),.6f,.1f,.15f,.4f,EnumParticleModel.Quad)
            {WithTerrainCollision=false,OpacityEvolve=EvolvingNatFloat.create(EnumTransformFunction.LINEAR,-1.5f)});}
        catch(Exception e)when(e is not OutOfMemoryException){world.Logger?.Warning("[CrookedCodex] Blood particles failed: {0}",e.Message);}
    }
}
