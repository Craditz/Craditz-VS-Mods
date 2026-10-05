#nullable enable

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace TamablesFotsa;

/// <summary>
/// Runs before PetAI's tameable behavior so a disallowed starting age cannot
/// consume a treat or create a partial taming state.
/// </summary>
internal sealed class EntityBehaviorTamablesFotsaTameStageGuard : EntityBehavior
{
    public const string BehaviorCode = "tamablesfotsatamestageguard";

    public EntityBehaviorTamablesFotsaTameStageGuard(Entity entity) : base(entity)
    {
    }

    public override void OnInteract(
        EntityAgent byEntity,
        ItemSlot itemslot,
        Vec3d hitPosition,
        EnumInteractMode mode,
        ref EnumHandling handled)
    {
        if (TamablesFotsaSystem.ShouldBlockTamingInteraction(entity, itemslot, mode))
        {
            handled = EnumHandling.PreventSubsequent;
        }
    }

    public override string PropertyName() => BehaviorCode;
}
