#nullable enable

using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace TamablesCritters;

internal sealed class EntityBehaviorTamablesCrittersGrow : EntityBehaviorGrow
{
    public const string BehaviorCode = "tamablescrittersgrow";

    public EntityBehaviorTamablesCrittersGrow(Entity entity) : base(entity) { }

    protected override void BecomeAdult(Entity adult, bool keepTextureIndex)
    {
        EntityBehaviorTamablesCrittersMultiply.CopyOwnership(entity, adult);
        base.BecomeAdult(adult, keepTextureIndex);
    }

    public override string PropertyName() => BehaviorCode;
}
