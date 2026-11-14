using HarmonyLib;
using rustboundmagic.src;
using rustboundmagic.src.common.item.tool.spellcasting;
using rustboundmagic.src.system.effects;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace RustboundExpansion;

internal static class BloodChannelHooks
{
    private sealed record Session(BloodSpell Spell,float Warmup,Vec3d Anchor);
    private static readonly Dictionary<EntityPlayer,Session> sessions=new(ReferenceEqualityComparer.Instance);
    internal static void Install(Harmony h)
    {
        var t=typeof(ItemToolStaffRM);
        h.Patch(AccessTools.DeclaredMethod(t,"OnHeldInteractStart"),postfix:new(typeof(BloodChannelHooks),nameof(Start)));
        h.Patch(AccessTools.DeclaredMethod(t,"OnHeldInteractStep"),prefix:new(typeof(BloodChannelHooks),nameof(Step)));
        foreach(var method in new[]{"OnHeldInteractCancel","OnHeldInteractStop"})h.Patch(AccessTools.DeclaredMethod(t,method),prefix:new(typeof(BloodChannelHooks),nameof(StopHook)));
    }
    internal static BloodSpell? Selected(EntityPlayer p)=>RustboundMagic.FullSpellDictionary.TryGetValue(
        p.WatchedAttributes.GetString("entitybehavior-castingtool-currentlyselectedspell-name_rm",""),out var value)
        && value is BloodSpell {Definition.Kind:"exsanguinate"} s?s:null;
    private static void Start(EntityAgent byEntity)
    {
        if(byEntity is EntityPlayer p && Selected(p) is {} s && BloodAttunement.Allows(p,s.Tier)
            && p.WatchedAttributes.GetBool("entitybehavior-iscastingspell_rm"))
            sessions[p]=new(s,p.WatchedAttributes.GetFloat("entitybehavior-currentspellcastduration_rm",s.CastTime),p.Pos.XYZ);
    }
    internal static bool Interrupted(EntityPlayer p,Vec3d anchor)
    {
        var effects=new StatusEffectsRM();
        return !p.Alive || p.ShouldDespawn || !BloodAttunement.Allows(p,3) || ControlEffects.Frozen(p) || p.Controls.TriesToMove
            || p.Controls.Jump || p.Controls.FloorSitting || p.Pos.XYZ.DistanceTo(anchor)>.4
            || effects.IsEffectActive(p,"silence") || effects.IsEffectActive(p,"stasis") || effects.IsEffectActive(p,"globalcooldown");
    }
    private static bool Step(float secondsUsed,EntityAgent byEntity,ref bool __result)
    {
        if(byEntity is not EntityPlayer p || !sessions.TryGetValue(p,out var session))return true;
        if(Selected(p)!=session.Spell || Interrupted(p,session.Anchor) || !p.WatchedAttributes.GetBool("entitybehavior-iscastingspell_rm"))
        {Stop(p);__result=false;return false;}
        if(secondsUsed<session.Warmup){__result=true;return false;}
        p.WatchedAttributes.SetFloat("entitybehavior-currentspellcasttimer_rm",secondsUsed);
        p.WatchedAttributes.SetFloat("entitybehavior-currentspellcastduration_rm",session.Warmup+session.Spell.Duration);
        if(secondsUsed>=session.Warmup+session.Spell.Duration){Stop(p);__result=false;return false;}
        __result=p.World.Side!=EnumAppSide.Server || ExpansionRuntime.For(p.World)?.BloodDrainStep(p,session.Spell)==true;
        if(!__result)Stop(p);return false;
    }
    private static void StopHook(EntityAgent byEntity){if(byEntity is EntityPlayer p)Stop(p);}
    internal static void Stop(EntityPlayer p)
    {
        if(ExpansionRuntime.For(p.World) is {} runtime)runtime.StopBloodDrain(p);
        else if(Forget(p))p.WatchedAttributes.SetBool("entitybehavior-iscastingspell_rm",false);
    }
    internal static bool Forget(EntityPlayer p)=>sessions.Remove(p);
}

public sealed partial class ExpansionRuntime
{
    internal bool BloodDrainStep(EntityPlayer p,BloodSpell spell)
    {
        if(World.Side!=EnumAppSide.Server || BloodChannelHooks.Interrupted(p,p.Pos.XYZ))return false;
        if(BloodDrains.TryGetValue(p,out var current)){current.LastStep=Now;return true;}
        var target=spell.Target(p);
        if(target==null || !BloodEnemy(p,target,spell.Range))return false;
        int cost=spell.GetModifiedManaCost(p,spell.Id);
        if(p.WatchedAttributes.GetInt("entitybehavior-resource-currentmana_rm")<cost)return false;
        spell.ConsumeManaForSpell(p,spell.Id,cost);
        BloodDrains[p]=new(p,spell,target,p.Pos.XYZ,Now,Now+(long)(spell.Duration*1000)){LastStep=Now,Next=Now+1000};
        MemeSpellCast(p);return true;
    }
    private void TickBloodDrains()
    {
        foreach(var drain in BloodDrains.Values.ToArray())
        {
            var p=drain.Caster;var s=drain.Spell;
            if(Now>=drain.End || Now-drain.LastStep>600 || BloodChannelHooks.Selected(p)?.Id!=s.Id
                || BloodChannelHooks.Interrupted(p,drain.Anchor) || !BloodEnemy(p,drain.Target,s.Range))
            {StopBloodDrain(p);continue;}
            if(Now<drain.Next)continue;drain.Next=Now+1000;
            int mana=Math.Max(1,(int)Math.Ceiling(8f*s.GetModifiedManaCost(p,s.Id)/s.ManaCost));
            if(p.WatchedAttributes.GetInt("entitybehavior-resource-currentmana_rm")<mana){StopBloodDrain(p);continue;}
            s.ConsumeManaForSpell(p,s.Id,mana);
            var hp=drain.Target.GetBehavior<EntityBehaviorHealth>()!;float before=hp.Health;
            CombatRules.Hit(p,drain.Target,s.ModifiedDamage(p));
            float removed=Math.Max(0,before-Math.Max(0,hp.Health));
            if(removed>0)Heal(p,p,removed*.5f);
            BloodVfx.Line(World,SpellTargeting.BodyCenter(drain.Target),p.Pos.XYZ.AddCopy(p.LocalEyePos));
            BloodVfx.Mark(World,drain.Target);
        }
    }
    internal void StopBloodDrain(EntityPlayer p)
    {
        bool active=BloodDrains.Remove(p);bool warming=BloodChannelHooks.Forget(p);
        if(!active&&!warming)return;
        p.WatchedAttributes.SetBool("entitybehavior-iscastingspell_rm",false);
    }
}
