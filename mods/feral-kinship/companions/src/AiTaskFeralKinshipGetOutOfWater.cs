#nullable enable

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// Keeps the vanilla land-animal instinct to leave water during ordinary pack
/// life, but does not let that instinct override an explicit Follow command.
/// </summary>
public sealed class AiTaskFeralKinshipGetOutOfWater : AiTaskGetOutOfWater
{
    public AiTaskFeralKinshipGetOutOfWater(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
    }

    public override bool ShouldExecute()
    {
        if (FeralKinshipCompanionSystem.GetCompanionActivityMode(entity) == CompanionActivityMode.Follow)
        {
            return false;
        }

        return base.ShouldExecute();
    }

    public override bool ContinueExecute(float dt)
    {
        if (FeralKinshipCompanionSystem.GetCompanionActivityMode(entity) == CompanionActivityMode.Follow)
        {
            return false;
        }

        return base.ContinueExecute(dt);
    }
}
