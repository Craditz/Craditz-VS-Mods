using HarmonyLib;
using LTO.BlackSmithPlus.Core;
using LTO.BlackSmithPlus.Repair;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AnimalicaToolsmith;

/// <summary>Allow salvaging a humanoid tool at its native station, without allowing its use.</summary>
public static class StationDismantling
{
    private const string Lease="animalicatoolsmith:dismantling";
    public static void Start(Harmony harmony)
    {
        var type=AccessTools.TypeByName("TraitRestrictions.ItemPermissionsPatches")
            ?? throw new InvalidOperationException("Trait Restrictions item permission hooks are unavailable.");
        harmony.Patch(AccessTools.Method(type,"TryBlockInteract"),prefix:new HarmonyMethod(typeof(StationDismantling),nameof(AllowStart)));
        harmony.Patch(AccessTools.Method(type,"ShouldBlockUse"),prefix:new HarmonyMethod(typeof(StationDismantling),nameof(AllowContinuation)));
    }
    public static bool Eligible(EntityAgent actor,ItemStack? stack,BlockSelection? selection,EntitySelection? entitySelection=null)
    {
        if(stack?.Collectible.Code.Domain!="game" || actor.Controls.ShiftKey!=true || selection?.Position==null || entitySelection!=null ||
            !(bool)IntegrationSystem.RestrictCraft.Invoke(null,null)! || !(bool)IntegrationSystem.Holder.Invoke(null,new object[]{actor})! ||
            !LTOBlackSmithPlusSystem.Tools.TryGet(stack.Collectible.Code.ToString(),out _) ||
            actor.Pos.SquareDistanceTo(selection.Position.ToVec3d().Add(.5,.5,.5))>64) return false;
        var block=actor.World.BlockAccessor.GetBlock(selection.Position);
        if(block is BlockMultiblock { OffsetInv:not null } proxy)
            block=actor.World.BlockAccessor.GetBlock(selection.Position.AddCopy(proxy.OffsetInv));
        return block is RepairWorkbenchBlock && selection.SelectionBoxIndex==RepairWorkbenchBlock.ViseSelectionIndex;
    }
    private static bool AllowStart(ItemSlot slot,EntityAgent byEntity,BlockSelection? blockSel,EntitySelection? entitySel,ref bool __result)
    {
        if(!Eligible(byEntity,slot.Itemstack,blockSel,entitySel))return true;
        var lease=new TreeAttribute();lease.SetBlockPos("position",blockSel!.Position);lease.SetInt("box",blockSel.SelectionBoxIndex);
        lease.SetLong("until",byEntity.World.ElapsedMilliseconds+1000);slot.Itemstack!.TempAttributes[Lease]=lease;
        __result=true;return false;
    }
    private static bool AllowContinuation(EntityPlayer player,ItemStack? stack,EnumHandInteract useType,ref CollectibleObject? blockedColl,ref bool __result)
    {
        var lease=stack?.TempAttributes.GetTreeAttribute(Lease);
        if(lease==null)return true;
        var position=lease.GetBlockPos("position");
        var selected=player.BlockSelection;
        var selection=new BlockSelection { Position=position,SelectionBoxIndex=lease.GetInt("box") };
        if(useType!=EnumHandInteract.HeldItemInteract || player.World.ElapsedMilliseconds>lease.GetLong("until") ||
            selected?.Position!=null && (!selected.Position.Equals(position) || selected.SelectionBoxIndex!=selection.SelectionBoxIndex) ||
            !Eligible(player,stack,selection))
        {stack!.TempAttributes.RemoveAttribute(Lease);return true;}
        lease.SetLong("until",player.World.ElapsedMilliseconds+1000);
        blockedColl=null;__result=false;return false;
    }
}
