using System.Reflection.Emit;
using System.Text.Json;
using HarmonyLib;
using rustboundmagic.src.system.effects;
using rustboundmagic.src.system.enums;
using rustboundmagic.src.system.spells;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace RustboundExpansion;

internal static class BulwarkCatalog
{
    internal static readonly SpellDefinition[] All=Read();
    private static SpellDefinition[] Read()
    {
        using var stream=typeof(BulwarkCatalog).Assembly.GetManifestResourceStream("RustboundExpansion.bulwark-spell-definitions.json")!;
        return JsonSerializer.Deserialize<SpellDefinition[]>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    }
}

public sealed class BulwarkSpell : ExpansionSpell
{
    internal readonly SpellDefinition Definition;
    internal BulwarkSpell(SpellDefinition d):base(d.Id,d.School,d.Essence)
    {
        Definition=d;Tier=d.Tier;ManaCost=d.Mana;CastTime=d.Cast;Duration=d.Duration;
        Stats=d.Stats;Range=d.Range;BuffType=EnumBuffType.buff;
        SoundAssetLocationSpellCastingSuccessful=new("rustboundmagic:sounds/effects/castcomplete-buff.ogg");
    }
    public override void OnCastStop(float seconds,ItemSlot slot,EntityAgent caster,BlockSelection block,EntitySelection entity,
        bool scroll,bool magnification,bool duplication,float remaining,bool infinite=false)
    {
        if(caster is not EntityPlayer p || p.World.Side!=EnumAppSide.Server || remaining!=0 || !p.Alive || p.ShouldDespawn
            || ControlEffects.Banished(p) || StatusEffects.IsEffectActive(p,"stasis"))return;
        int cost=GetModifiedManaCost(p,Id);
        if(!Bulwark.Allows(p,Tier) || (!scroll && p.WatchedAttributes.GetInt("entitybehavior-resource-currentmana_rm")<cost))
        {NotifyFailure(p);return;}
        var selected=RayTraceDestinationSelectedEntityAgent(p,Range);
        Entity target=selected!=null && ExpansionRuntime.Ally(p,selected)?selected:p;
        if(StatusEffects.IsEffectActive(target,"stasis")){NotifyFailure(p);return;}
        float potency=GetModifiedStats(p,Id)/Definition.Stats;
        if(!Bulwark.Apply(this,target,potency,GetModifiedDuration(p,Id),infinite)){NotifyFailure(p);return;}
        Complete(p,scroll,false);
    }
}

internal static class Bulwark
{
    internal const string StateKey="rustboundexpansion-bulwark";
    private static readonly StatusEffectsRM Effects=new();
    internal static float Flat(float baseValue,float potency)=>baseValue*(1+Math.Clamp(potency-1,0,1)/3);
    internal static float Slow(float potency)=>-.25f/Math.Max(1,potency);
    internal static float ArmorFactor(float relative)=>1-.6f*Math.Clamp((relative-.5f)/.4f,0,1);
    internal static bool Allows(EntityPlayer player,int tier)
    {
        if(tier<=1)return true;
        var inventory=player.Player?.InventoryManager?.GetOwnInventory("character");
        var stack=inventory is {Count:>7}?inventory[7]?.Itemstack:null;
        if(stack?.StackSize!=1 || !stack.Attributes.GetBool("item-attribute-soulbound_rm")
            || stack.Attributes.GetString("item-attribute-soulbound-playerid_rm","")!=player.PlayerUID)return false;
        string? code=stack.Collectible?.Code?.ToString();
        return code=="rustboundmagic:attunementcore-advanced-alteration"
            || tier==2 && code=="rustboundmagic:attunementcore-basic-alteration";
    }
    internal static ITreeAttribute? State(Entity target)
    {
        var state=target.WatchedAttributes.GetTreeAttribute(StateKey);
        return state!=null && Effects.IsEffectActive(target,state.GetString("id",""))?state:null;
    }
    internal static bool Apply(BulwarkSpell spell,Entity target,float potency,float duration,bool infinite=false)
    {
        if(target.World.Side!=EnumAppSide.Server || !target.Alive || target.ShouldDespawn
            || target.GetBehavior<EntityBehaviorHealth>() is not {Health:>0} || !float.IsFinite(potency) || potency<=0
            || !float.IsFinite(duration) || duration<=0)return false;
        float flat=Flat(spell.Definition.Stats,potency),slow=Slow(potency);
        if(State(target) is {} old && (old.GetInt("rank")>spell.Tier || old.GetInt("rank")==spell.Tier && old.GetFloat("flat")>flat+.00001f))return false;
        Clear(target);
        var state=new TreeAttribute();state.SetString("id",spell.Id);state.SetInt("rank",spell.Tier);
        state.SetFloat("flat",flat);state.SetFloat("slow",slow);
        target.WatchedAttributes.SetAttribute(StateKey,state);target.WatchedAttributes.MarkPathDirty(StateKey);
        Effects.ApplyStatusEffect(target,new(){["walkspeed"]=slow},"rustboundmagic-"+spell.Id,duration,spell.Id,infinite);
        if(!Effects.IsEffectActive(target,spell.Id)){Clear(target);return false;}
        if(!infinite)ScheduleExpiry(target,state,spell.Id);
        if(target is EntityPlayer)Effects.RefreshBuffForOtherPlayers(target,spell.Id,duration);
        else if(target is rustboundmagic.src.common.entity.EntityCompanionRM)Effects.RefreshBuffForCompanion(target,spell.Id,duration);
        return true;
    }
    internal static void Removed(Entity target,string id)
    {
        if(target.WatchedAttributes.GetTreeAttribute(StateKey)?.GetString("id")==id)
            Forget(target);
    }
    private static void Forget(Entity target)
    {
        long callback=target.WatchedAttributes.GetTreeAttribute(StateKey)?.GetLong("cleanup")??0;
        if(callback>0)target.World.UnregisterCallback(callback);
        target.WatchedAttributes.RemoveAttribute(StateKey);target.WatchedAttributes.MarkPathDirty(StateKey);
    }
    private static void ScheduleExpiry(Entity target,ITreeAttribute state,string id)
    {
        if(!ReferenceEquals(target.WatchedAttributes.GetTreeAttribute(StateKey),state))return;
        long left=Effects.GetEffectRemainingMilliseconds(target,id);
        if(left<=0){Clear(target);return;}
        long callback=target.World.RegisterCallback(_=>ScheduleExpiry(target,state,id),(int)Math.Min(left,int.MaxValue));
        state.SetLong("cleanup",callback);
    }
    internal static void Clear(Entity target)
    {
        foreach(var d in BulwarkCatalog.All)
        {
            if(target.WatchedAttributes.HasAttribute("effectactive-"+d.Id))Effects.RemoveEffect(target,d.Id);
            target.Stats?.Remove("walkspeed","rustboundmagic-"+d.Id);
        }
        Forget(target);
    }
    internal static bool Covered(DamageSource source)=>CreatureSupport.Physical(source.Type)
        && source.Source is not (EnumDamageSource.Internal or EnumDamageSource.Suicide or EnumDamageSource.Fall);

    internal sealed class Frame(Entity target,DamageSource source,Frame? previous)
    {
        internal readonly Entity Target=target;
        internal readonly DamageSource Source=source;
        internal readonly Frame? Previous=previous;
        internal float Armor;
    }
    [ThreadStatic] private static Frame? current;
    internal static bool FrameActive=>current!=null;
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(EntityBehaviorHealth),"ApplyOnDamageDelegates"),
            prefix:new(typeof(Bulwark),nameof(Begin)),finalizer:new(typeof(Bulwark),nameof(End)));
        harmony.Patch(AccessTools.Method(typeof(ModSystemWearableStats),"handleDamaged"),
            transpiler:new(typeof(Bulwark),nameof(ArmorCapture)));
        harmony.Patch(AccessTools.Method(typeof(SpellcastingModifierSystemRM),"GetModifiedStats",
            [typeof(EntityPlayer),typeof(string),typeof(float),typeof(string),typeof(string),typeof(string)]),
            postfix:new(typeof(Bulwark),nameof(Tooltip)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(rustboundmagic.src.common.item.tool.spellcasting.ItemToolStaffRM),"OnHeldInteractStart"),
            prefix:new(typeof(Bulwark),nameof(StaffStart)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(rustboundmagic.src.common.item.tool.spellcasting.ItemToolSpellScrollRM),"OnHeldInteractStart"),
            prefix:new(typeof(Bulwark),nameof(ScrollStart)));
    }
    private static bool Start(EntityAgent caster,string id,ref EnumHandHandling handling)
    {
        if(caster is not EntityPlayer p || !rustboundmagic.src.RustboundMagic.FullSpellDictionary.TryGetValue(id,out var spell)
            || spell is not BulwarkSpell b || Allows(p,b.Tier))return true;
        handling=EnumHandHandling.NotHandled;
        if(p.World.Side==EnumAppSide.Server)ExpansionSpell.NotifyFailure(p);
        return false;
    }
    private static bool StaffStart(EntityAgent byEntity,ref EnumHandHandling handling)
        =>Start(byEntity,byEntity.WatchedAttributes.GetString("entitybehavior-castingtool-currentlyselectedspell-name_rm",""),ref handling);
    private static bool ScrollStart(ItemSlot itemslot,EntityAgent byEntity,ref EnumHandHandling handling)
        =>Start(byEntity,itemslot.Itemstack?.Collectible.Code.Path.Split('-').Last()??"",ref handling);
    private static void Tooltip(EntityPlayer __0,string __1,ref string __result)
    {
        if(!rustboundmagic.src.RustboundMagic.FullSpellDictionary.TryGetValue(__1,out var spell) || spell is not BulwarkSpell bulwark)return;
        float potency=new SpellcastingModifierSystemRM().CalculateModifiedSpellAttributeStats(__0,__1)/bulwark.Definition.Stats;
        if(!float.IsFinite(potency) || potency<=0)return;
        __result=Lang.Get("rustboundexpansion:bulwark-currentstats",
            Flat(bulwark.Definition.Stats,potency).ToString("0.##"),(-Slow(potency)*100).ToString("0.##"));
    }
    private static void Begin(EntityBehaviorHealth __instance,DamageSource __0,out Frame? __state)
    {
        __state=null;
        if(ExpansionRuntime.TransferDepth!=0 || __instance.entity.World.Side!=EnumAppSide.Server || !Covered(__0)
            || State(__instance.entity)==null)return;
        __state=new(__instance.entity,__0,current);current=__state;
    }
    private static void End(Frame? __state)
    {if(__state!=null)current=__state.Previous;}
    internal static float RelativeProtection(ProtectionModifiers protection,int damageTier)
    {
        float relative=protection.RelativeProtection;
        for(int tier=1;tier<=damageTier;tier++)
        {
            bool above=tier>protection.ProtectionTier;
            float loss=protection.PerTierRelativeProtectionLoss[above?1:0];
            if(above && protection.HighDamageTierResistant)loss/=2;
            relative*=1-loss;
        }
        return float.IsFinite(relative)?Math.Clamp(relative,0,1):0;
    }
    internal static ProtectionModifiers Capture(IWearableStatsSupplier supplier,ItemSlot slot,IPlayer player,DamageSource source)
    {
        // This is the same native call at the same selected ItemSlot. Capture
        // before durability can break it, without another roll or durability cost.
        var protection=supplier.GetProtectionModifiers(slot);
        if(current is {} frame && ReferenceEquals(frame.Target,player.Entity) && ReferenceEquals(frame.Source,source) && protection!=null)
            frame.Armor=RelativeProtection(protection,source.DamageTier);
        return protection!;
    }
    private static IEnumerable<CodeInstruction> ArmorCapture(IEnumerable<CodeInstruction> instructions)
    {
        var getter=AccessTools.Method(typeof(IWearableStatsSupplier),nameof(IWearableStatsSupplier.GetProtectionModifiers));
        int found=0;
        foreach(var instruction in instructions)
        {
            if(!instruction.Calls(getter)){yield return instruction;continue;}
            var player=new CodeInstruction(OpCodes.Ldarg_1);player.labels.AddRange(instruction.labels);player.blocks.AddRange(instruction.blocks);
            yield return player;yield return new(OpCodes.Ldarg_3);
            yield return new(OpCodes.Call,AccessTools.Method(typeof(Bulwark),nameof(Capture)));found++;
        }
        if(found!=1)throw new InvalidOperationException("Unsupported native armor selection layout: "+found);
    }
    internal static void Reduce(Entity target,DamageSource source,ref float damage)
    {
        if(ExpansionRuntime.TransferDepth!=0 || !float.IsFinite(damage) || damage<=0 || !target.Alive || !Covered(source)
            || State(target) is not {} state)return;
        float armor=current is {} frame && ReferenceEquals(frame.Target,target) && ReferenceEquals(frame.Source,source)?frame.Armor:0;
        float flat=state.GetFloat("flat");
        if(float.IsFinite(flat) && flat>0)damage=Math.Max(0,damage-flat*ArmorFactor(armor));
    }
}
