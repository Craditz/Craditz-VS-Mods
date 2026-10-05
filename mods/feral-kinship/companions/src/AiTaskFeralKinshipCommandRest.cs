#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

public sealed class AiTaskFeralKinshipCommandRest : AiTaskBase
{
    private FeralKinshipCompanionSystem? system;

    public AiTaskFeralKinshipCommandRest(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
    }

    public override bool ShouldExecute()
    {
        if (!FeralKinshipCompanionSystem.CanRunCompanionCommandTask(entity)
            || FeralKinshipCompanionSystem.GetCompanionActivityMode(entity) != CompanionActivityMode.Rest)
        {
            return false;
        }

        system ??= entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (FeralKinshipCompanionSystem.HasCompanionRestArrival(entity))
        {
            return true;
        }
        if (system.TryResolveCompanionCommandHome(entity, out Vec3d? target)
            && target != null)
        {
            if (target.AsBlockPos.dimension != entity.Pos.Dimension) return false;
            float arrival = Math.Max(0.65f, FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity));
            if (!CompanionNavigation.IsClearArrival(
                    entity, target, arrival, allowTargetBlock: true)) return false;
        }
        return true;
    }

    public override void StartExecute()
    {
        entity.Controls.StopAllMovement();
        system ??= entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        system.MarkCompanionRestArrival(entity);
        base.StartExecute();
    }

    public override bool ContinueExecute(float dt)
    {
        entity.Controls.StopAllMovement();
        if (FeralKinshipCompanionSystem.GetCompanionActivityMode(entity) != CompanionActivityMode.Rest)
        {
            return false;
        }

        system ??= entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (system.IsCompanionRestComplete(entity))
        {
            system.CompleteCompanionActivity(entity, CompanionActivityMode.Rest);
            return false;
        }
        return true;
    }
}
