using HarmonyLib;
using System.Reflection.Emit;
using rustboundmagic.src;
using rustboundmagic.src.system.abstracts;
using rustboundmagic.src.system.effects;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace RustboundExpansion;

internal static class BloodCipher
{
    internal static void Install(Harmony h)
    {
        var chalk=typeof(RustboundMagic).Assembly.GetTypes().Single(t=>t.Name=="ItemMagicChalkRM");
        h.Patch(AccessTools.DeclaredMethod(chalk,"OnHeldInteractStart"),prefix:new(typeof(BloodCipher),nameof(Imprint)));
        h.Patch(AccessTools.Method(typeof(StatusEffectsRM),"ResetAllTempBuffs"),prefix:new(typeof(BloodCipher),nameof(BeforeReset)));
        var forge=typeof(RustboundMagic).Assembly.GetTypes().Single(t=>t.Name=="BlockEntityStationSpellforgeRM");
        h.Patch(AccessTools.DeclaredMethod(forge,"CalculateNewSpellToDiscover"),transpiler:new(typeof(BloodCipher),nameof(ResearchSchools)));
    }
    internal static string ResearchSchool(SpellBase s)=>s is BloodSpell {Tier:1}?"general":s.School;
    private static IEnumerable<CodeInstruction> ResearchSchools(IEnumerable<CodeInstruction> instructions)
    {
        int count=0;var getter=AccessTools.PropertyGetter(typeof(SpellBase),nameof(SpellBase.School));
        foreach(var instruction in instructions)
        {
            var result=new CodeInstruction(instruction);
            if(result.Calls(getter)){result.opcode=OpCodes.Call;result.operand=AccessTools.Method(typeof(BloodCipher),nameof(ResearchSchool));count++;}
            yield return result;
        }
        if(count!=2)throw new InvalidOperationException("Rustbound Spellforge school layout changed: "+count);
    }
    private static void BeforeReset(Entity __0)
    {if(__0 is EntityPlayer p && p.World?.Side==EnumAppSide.Server)ExpansionRuntime.For(p.World)?.ClearBloodOwner(p);}
    internal static bool Imprint(CollectibleObject __instance,ItemSlot itemslot,EntityAgent byEntity,BlockSelection blockSel,bool firstEvent,ref EnumHandHandling handling)
    {
        if(byEntity is not EntityPlayer p || BloodAttunement.Level(p)==0 || blockSel==null
            || p.World.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityGroundStorage ground
            || ground.Inventory[0].Itemstack is not {} blank || blank.Collectible.Code.Domain!="rustboundmagic"
            || !blank.Collectible.Code.Path.StartsWith("spellcipherempty-tier",StringComparison.Ordinal)
            || !blank.Collectible.Code.Path.EndsWith("-none",StringComparison.Ordinal))return true;
        int tier=blank.Collectible.Code.Path.Contains("-tier2-")?2:blank.Collectible.Code.Path.Contains("-tier3-")?3:0;
        if(tier==0)return true;
        handling=EnumHandHandling.PreventDefault;
        if(!firstEvent || p.World.Side!=EnumAppSide.Server)return false;
        var effects=new StatusEffectsRM();
        if(!p.Alive || p.ShouldDespawn || ControlEffects.Banished(p) || effects.IsEffectActive(p,"stasis") || effects.IsEffectActive(p,"silence")
            || !p.WatchedAttributes.GetBool("entitybehavior-player-ismagicunlocked_rm") || !BloodAttunement.Allows(p,tier)
            || blockSel.Position.dimension!=p.Pos.Dimension || blockSel.Position.ToVec3d().SquareDistanceTo(p.Pos.XYZ)>36
            || !p.World.Claims.TryAccess(p.Player,blockSel.Position,EnumBlockAccessFlags.BuildOrBreak))
        {ExpansionSpell.NotifyFailure(p);return false;}
        int cost=(int?)(AccessTools.Property(__instance.GetType(),"SpellCipherManaCostTier"+tier)?.GetValue(__instance))??(tier==2?50:200);
        var output=p.World.GetItem(blank.Collectible.CodeWithVariant("school","hemomancy"));
        if(output==null || cost<0 || p.WatchedAttributes.GetInt("entitybehavior-resource-currentmana_rm")<cost)
        {ExpansionSpell.NotifyFailure(p);return false;}
        ground.Inventory[0].Itemstack=new ItemStack(output);
        ground.Inventory[0].MarkDirty();ground.MarkDirty(true);ExpansionRuntime.Mana(p,cost);
        return false;
    }
}
