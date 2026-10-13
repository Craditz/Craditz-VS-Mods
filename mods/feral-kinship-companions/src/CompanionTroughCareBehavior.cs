#nullable enable
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
namespace FeralKinshipCompanions;

// State belongs to this native block entity. Native serialization continues
// calling behaviors after mod Dispose removes every Harmony hook, including
// the engine's final shutdown save. No static cache or surviving global hook.
internal sealed class CompanionTroughCareBehavior : BlockEntityBehavior
{
    internal TroughCare? State;
    internal CompanionTroughCareBehavior(BlockEntityTrough trough) : base(trough) { }
    public override void ToTreeAttributes(ITreeAttribute tree)
        => CompanionTroughCare.Write((BlockEntityTrough)Blockentity, tree);
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        => CompanionTroughCare.Read((BlockEntityTrough)Blockentity, tree, worldAccessForResolve);
    public override void OnBlockRemoved() => State = null;
    public override void OnBlockPlaced(ItemStack? byItemStack = null) => State = null;
}
