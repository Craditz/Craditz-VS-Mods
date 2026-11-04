#nullable enable
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

/// <summary>Drop override scoped to one synchronous quarry pickaxe action.</summary>
public sealed class BlockBehaviorQuarryRecovery : BlockBehavior
{
    internal static IWorldAccessor? ActiveWorld;
    internal static BlockPos? ActivePosition;
    public BlockBehaviorQuarryRecovery(Block block) : base(block) { }
    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer,
        ref float dropQuantityMultiplier, ref EnumHandling handling)
    {
        if (!ReferenceEquals(world, ActiveWorld) || ActivePosition?.Equals(pos) != true) return null!;
        handling = EnumHandling.PreventSubsequent;
        return new[] { new ItemStack(block) };
    }
}
