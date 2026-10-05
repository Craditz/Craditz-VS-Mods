#nullable enable

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace TamablesCritters;

/// <summary>
/// PetAI's food behavior calls EntityBehaviorTameable.OnInteract directly,
/// so the stage check must live in the tameable behavior itself.
/// </summary>
internal sealed class EntityBehaviorTamablesCrittersTameable : PetAI.EntityBehaviorTameable
{
    public const string BehaviorCode = "tamablescritterstameable";

    public EntityBehaviorTamablesCrittersTameable(Entity entity) : base(entity)
    {
    }

    public override void OnInteract(
        EntityAgent byEntity,
        ItemSlot itemslot,
        Vec3d hitPosition,
        EnumInteractMode mode,
        ref EnumHandling handled)
    {
        if (TamablesCrittersSystem.ShouldBlockTamingInteraction(entity, itemslot, mode))
        {
            handled = EnumHandling.PreventSubsequent;
            return;
        }

        base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
    }

    public override string PropertyName() => "tameable";
}
