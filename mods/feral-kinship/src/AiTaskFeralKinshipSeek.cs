using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace FeralKinship
{
public sealed class AiTaskFeralKinshipSeek : AiTaskSeekEntity
{
    private readonly KinshipTargetFilter kinship;

    public AiTaskFeralKinshipSeek(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        kinship = new KinshipTargetFilter(entity, taskConfig);
    }

    public override bool IsTargetableEntity(Entity candidate, float range)
    {
        return !kinship.ShouldIgnore(candidate) && base.IsTargetableEntity(candidate, range);
    }

    public override bool IsTargetableEntityNoTagsNoAll(Entity candidate, float range)
    {
        return !kinship.ShouldIgnore(candidate) && base.IsTargetableEntityNoTagsNoAll(candidate, range);
    }

    public override bool IsTargetableEntityNoTagsAll(Entity candidate, float range)
    {
        return !kinship.ShouldIgnore(candidate) && base.IsTargetableEntityNoTagsAll(candidate, range);
    }

    public override void OnEntityHurt(DamageSource source, float damage)
    {
        kinship.OnEntityHurt(source);
        base.OnEntityHurt(source, damage);
    }
}
}
