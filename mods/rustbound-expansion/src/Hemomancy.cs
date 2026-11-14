using System.Text;
using System.Text.Json;
using HarmonyLib;
using rustboundmagic.src;
using rustboundmagic.src.system.effects;
using rustboundmagic.src.system.enums;
using rustboundmagic.src.system.spells;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace RustboundExpansion;

internal static class BloodCatalog
{
    internal const string School = "hemomancy";
    internal static readonly SpellDefinition[] All = Read();
    private static SpellDefinition[] Read()
    {
        using var stream = typeof(BloodCatalog).Assembly.GetManifestResourceStream("RustboundExpansion.hemomancy-spell-definitions.json")!;
        return JsonSerializer.Deserialize<SpellDefinition[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
    internal static SpellDefinition Kind(string kind) => All.Single(d => d.Kind == kind);
}

public sealed class BloodCoreItem : Item { }

public sealed class BloodCoreTooltipBehavior(CollectibleObject collectible) : CollectibleBehavior(collectible)
{
    public override void GetHeldItemInfo(ItemSlot slot, StringBuilder text, IWorldAccessor world, bool debug)
    {
        text.AppendLine(Lang.Get("rustboundmagic:msg-collectiblebehavior-equipmentstatsdisplay-specialize"));
        if(BloodAttunement.Advanced(slot.Itemstack))
            foreach(string stat in new[]{"manacost","casttime","duration","damage","stats"})
                text.AppendLine(Lang.Get("rustboundexpansion:bloodcore-specialize-"+stat));
        text.AppendLine(Lang.Get("rustboundexpansion:bloodcore-specialize-healthcost",BloodAttunement.Advanced(slot.Itemstack)?25:10));
        text.AppendLine();
    }
}

internal static class BloodAttunement
{
    internal const string BasicCode = "rustboundexpansion:attunementcore-basic-hemomancy";
    internal const string AdvancedCode = "rustboundexpansion:attunementcore-advanced-hemomancy";
    internal static bool Advanced(ItemStack? stack) => stack?.Collectible?.Code?.ToString() == AdvancedCode;
    internal static int Level(ItemStack? stack, string uid)
    {
        if (stack?.StackSize != 1 || stack.Collectible is not BloodCoreItem ||
            !stack.Attributes.GetBool("item-attribute-soulbound_rm") ||
            stack.Attributes.GetString("item-attribute-soulbound-playerid_rm", "") != uid) return 0;
        return Advanced(stack) ? 2 : stack.Collectible.Code?.ToString() == BasicCode ? 1 : 0;
    }
    internal static int Level(EntityPlayer p)
    {
        var character = p.Player?.InventoryManager?.GetOwnInventory("character");
        return character is { Count: > 7 } ? Level(character[7]?.Itemstack,p.PlayerUID) : 0;
    }
    internal static bool Allows(EntityPlayer p,int tier) => tier <= 1 || Level(p) >= tier-1;
    internal static float HealthFactor(EntityPlayer p) => Level(p) switch { 2 => .75f, 1 => .9f, _ => 1 };
    internal static float Bonus(EntityPlayer p,string attribute) => Level(p) < 2 ? 0 : attribute is "manacost" or "casttime" ? -.25f : .5f;

    internal static void Install(Harmony h)
    {
        var t = typeof(SpellcastingModifierSystemRM);
        h.Patch(AccessTools.Method(t,"CalculateModifiedSpellAttributeManaCost"),prefix:new(typeof(BloodAttunement),nameof(Mana)));
        h.Patch(AccessTools.Method(t,"CalculateModifiedSpellAttributeCastTime",[typeof(EntityPlayer),typeof(string)]),prefix:new(typeof(BloodAttunement),nameof(Cast)));
        h.Patch(AccessTools.Method(t,"CalculateModifiedSpellAttributeDuration"),prefix:new(typeof(BloodAttunement),nameof(Duration)));
        h.Patch(AccessTools.Method(t,"CalculateModifiedSpellAttributeDamage"),prefix:new(typeof(BloodAttunement),nameof(Damage)));
        h.Patch(AccessTools.Method(t,"CalculateModifiedSpellAttributeStats"),prefix:new(typeof(BloodAttunement),nameof(Stats)));
        var staff=typeof(rustboundmagic.src.common.item.tool.spellcasting.ItemToolStaffRM);
        h.Patch(AccessTools.DeclaredMethod(staff,"OnHeldInteractStart"),prefix:new(typeof(BloodAttunement),nameof(StaffStart)));
        var scroll=typeof(rustboundmagic.src.common.item.tool.spellcasting.ItemToolSpellScrollRM);
        h.Patch(AccessTools.DeclaredMethod(scroll,"OnHeldInteractStart"),prefix:new(typeof(BloodAttunement),nameof(ScrollStart)));
        BloodChannelHooks.Install(h);
        BloodCipher.Install(h);
    }
    private static BloodSpell? Spell(string id) => RustboundMagic.FullSpellDictionary.TryGetValue(id,out var s)?s as BloodSpell:null;
    private static float Modified(EntityPlayer p,BloodSpell s,string stat,float value,bool essence=false)
    {
        if (!s.CanUseAttributeModifiers || value==0) return value;
        float factor=p.Stats.GetBlended("genericmodifier"+stat)+Bonus(p,stat);
        if (essence) factor+=p.Stats.GetBlended(s.EssenceType+(stat=="damage"?"attackdamagemodifier":"buffdurationmodifier"))-1;
        return Math.Max(0,value*factor);
    }
    private static bool Mana(EntityPlayer __0,string __1,ref int __result)
    { if (Spell(__1) is not {} s) return true; __result=Math.Max(1,(int)Modified(__0,s,"manacost",s.ManaCost));return false; }
    private static bool Cast(EntityPlayer __0,string __1,ref float __result)
    { if (Spell(__1) is not {} s) return true; __result=Modified(__0,s,"casttime",s.CastTime);return false; }
    private static bool Duration(EntityPlayer __0,string __1,ref float __result)
    { if (Spell(__1) is not {} s) return true; __result=Modified(__0,s,"duration",s.Duration,true);return false; }
    private static bool Damage(EntityPlayer __0,string __1,ref float __result)
    { if (Spell(__1) is not {} s) return true; __result=s.Damage==0?0:Math.Max(1,Modified(__0,s,"damage",s.Damage,true));return false; }
    private static bool Stats(EntityPlayer __0,string __1,ref float __result)
    { if (Spell(__1) is not {} s) return true; __result=Modified(__0,s,"stats",s.Stats);return false; }
    private static bool StaffStart(EntityAgent byEntity,ref EnumHandHandling handling)
    {
        if (byEntity is not EntityPlayer p || Spell(p.WatchedAttributes.GetString("entitybehavior-castingtool-currentlyselectedspell-name_rm","")) is not {} s) return true;
        bool inscription = p.LeftHandItemSlot?.Itemstack?.Collectible.Code.Path=="toolscrollofbinding";
        if (!(inscription && s.Definition.Kind=="exsanguinate") && Allows(p,s.Tier)) return true;
        handling=EnumHandHandling.NotHandled;
        MissingCoreFailure(p);
        return false;
    }
    private static bool ScrollStart(ItemSlot itemslot,EntityAgent byEntity,ref EnumHandHandling handling)
    {
        if(byEntity is not EntityPlayer p || Spell(itemslot.Itemstack?.Collectible.Code.Path.Split('-').Last()??"") is not {} s) return true;
        if(s.Definition.Kind!="exsanguinate" && Allows(p,s.Tier))return true;
        handling=EnumHandHandling.NotHandled;MissingCoreFailure(p);return false;
    }
    internal static void MissingCoreFailure(EntityPlayer p)
    {
        // A rejected client-side start never reaches the server's chat helper.
        if(p.World.Side==EnumAppSide.Client && (p.Api??p.World.Api) is ICoreClientAPI api)
            api.TriggerIngameError(p,"rbx-spellfailed",Lang.Get("rustboundmagic:msg-world-spellfailed"));
        else ExpansionSpell.NotifyFailure(p);
    }
}

internal static class BloodCosts
{
    internal const float GraceShortfall=.5f, GraceHealth=.1f;
    internal static float Cost(EntityPlayer p,string kind) => BaseCost(kind,p.GetBehavior<EntityBehaviorHealth>()?.Health??0)*BloodAttunement.HealthFactor(p);
    internal static float BaseCost(string kind,float health) => kind switch
    { "bloodletting" or "transfusion" or "crimsonaegis" => 4, "bloodmissiles"=>3, "heartburst"=>health*.35f, "crimsonhost"=>12, _=>0 };
    internal static bool Affordable(EntityPlayer p,float amount) => p.World.Side==EnumAppSide.Server && p.Alive && !p.ShouldDespawn
        && float.IsFinite(amount) && amount>=0 && p.GetBehavior<EntityBehaviorHealth>() is {} hp && float.IsFinite(hp.Health) && hp.Health>0;
    internal static float Remaining(float health,float amount)
    {
        if(amount==0)return health;
        double remaining=(double)health-amount;
        if(remaining>0)return (float)remaining;
        return remaining>=-GraceShortfall?Math.Min(GraceHealth,health):0;
    }
    // A sacrifice is a resource transaction, not a damage event. Shields,
    // armor, damage echoes and on-hit effects cannot change its amount.
    internal static bool Pay(EntityPlayer p,float amount)
    {using var payment=new Payment(p);return payment.TryPay(amount);}

    // Finish a fatal cast before native death cleanup removes its buffs,
    // summons and equipped cost modifiers. No shared/deferred global state.
    internal sealed class Payment(EntityPlayer player):IDisposable
    {
        private bool paid,finished,lethal,grace;
        internal float ActualPaid {get;private set;}
        internal bool TryPay(float amount)
        {
            if(paid||finished||!Affordable(player,amount))return false;
            paid=true;
            var hp=player.GetBehavior<EntityBehaviorHealth>()!;float before=hp.Health;
            float after=Remaining(before,amount);ActualPaid=before-after;
            if(amount>0){hp.PreviousHealth=before;hp.Health=after;}
            grace=amount>0 && (double)before-amount<=0 && after>0;
            lethal=after<=0;return true;
        }
        public void Dispose()
        {
            if(finished)return;finished=true;
            if(grace)BloodMessages.Grace(player);
            if(!lethal||player.World.Side!=EnumAppSide.Server||!player.Alive)return;
            if(player.GetBehavior<EntityBehaviorHealth>() is {} hp)hp.Health=0;
            player.Die(EnumDespawnReason.Death,new BloodCostDamageSource(player));
        }
    }
}

public sealed class BloodSpell : ExpansionSpell
{
    internal readonly SpellDefinition Definition;
    internal BloodSpell(SpellDefinition d):base(d.Id,BloodCatalog.School,d.Essence)
    {
        Definition=d;Tier=d.Tier;ManaCost=d.Mana;CastTime=d.Cast;Duration=d.Duration;Range=d.Range;Damage=d.Damage;Stats=d.Stats;
        BuffType=d.Kind is "clot" or "bloodscent" or "crimsonaegis" or "crimsonhost"?EnumBuffType.buff
            :d.Kind is "openwound" or "redharvest"?EnumBuffType.debuff:EnumBuffType.none;
        SoundAssetLocationSpellCastingSuccessful=new("rustboundmagic:sounds/effects/castcomplete-"+(d.Damage>0?"evocation":"buff")+".ogg");
    }
    internal float ModifiedDamage(EntityPlayer p)=>GetModifiedDamage(p,Id);
    internal float ModifiedStats(EntityPlayer p)=>GetModifiedStats(p,Id);
    internal float ModifiedDuration(EntityPlayer p)=>GetModifiedDuration(p,Id);
    internal Entity? Target(EntityPlayer p)=>Range>0?RayTraceDestinationSelectedEntityAgent(p,Range):null;
    public override void OnCastStop(float seconds,ItemSlot slot,EntityAgent caster,BlockSelection block,EntitySelection entity,
        bool scroll,bool magnification,bool duplication,float remaining,bool infinite=false)
    {
        bool trace=Definition.Kind is "bloodmissiles" or "crimsonhost";
        if(trace && caster is EntityPlayer diagnostic && diagnostic.World.Side==EnumAppSide.Server)
            diagnostic.World.Logger?.Debug("[Crooked Codex] {0} cast stop: restored={1}, alive={2}, core={3}, runtime={4}",Id,remaining,diagnostic.Alive,BloodAttunement.Allows(diagnostic,Tier),ExpansionRuntime.For(diagnostic.World)!=null);
        if(caster is not EntityPlayer p || p.World.Side!=EnumAppSide.Server || remaining!=0 || !p.Alive || p.ShouldDespawn
            || ControlEffects.Banished(p) || StatusEffects.IsEffectActive(p,"stasis"))return;
        if(Definition.Kind=="exsanguinate")return;
        using var payment=new BloodCosts.Payment(p);
        if(!BloodAttunement.Allows(p,Tier) || (!scroll&&p.WatchedAttributes.GetInt("entitybehavior-resource-currentmana_rm")<GetModifiedManaCost(p,Id)))
        {if(trace)p.World.Logger?.Warning("[Crooked Codex] {0} rejected before execution: attunement/mana",Id);NotifyFailure(p);return;}
        var selected=Target(p);
        if(ExpansionRuntime.For(p.World)?.CastBlood(this,p,selected,payment)!=true)
        {if(trace)p.World.Logger?.Warning("[Crooked Codex] {0} execution rejected, selected={1}",Id,selected?.Code?.ToString()??"none");NotifyFailure(p);return;}
        Complete(p,scroll,false);
    }
}
