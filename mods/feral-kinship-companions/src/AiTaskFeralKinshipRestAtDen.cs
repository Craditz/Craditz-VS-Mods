#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// Keeps a fox settled at its assigned bed while the den is being used as
/// shelter. Higher-priority companion commands and danger responses can interrupt.
/// </summary>
public sealed class AiTaskFeralKinshipRestAtDen : AiTaskBase
{
    private Vec3d? den;
    private long nextCheckAtMs;

    public AiTaskFeralKinshipRestAtDen(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
    }

    public override bool ShouldExecute()
    {
        if (entity.World.ElapsedMilliseconds < nextCheckAtMs
            || !CanRest())
        {
            return false;
        }
        nextCheckAtMs = entity.World.ElapsedMilliseconds + 1000;

        FeralKinshipCompanionSystem? system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (system?.ShouldFoxSeekDenShelter(entity) != true
            || system.TryGetAssignedFoxDen(entity, out den) != true
            || den == null)
        {
            den = null;
            return false;
        }

        double restDistance = Math.Max(0.60f, FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity));
        return CompanionNavigation.IsClearArrival(
            entity, den, restDistance, allowTargetBlock: true);
    }

    public override void StartExecute()
    {
        entity.Controls.StopAllMovement();
        base.StartExecute();
    }

    public override bool ContinueExecute(float dt)
    {
        FeralKinshipCompanionSystem? system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        return den != null
            && CanRest()
            && system?.ShouldFoxSeekDenShelter(entity) == true
            && CompanionNavigation.IsClearArrival(
                entity,
                den,
                Math.Max(0.60f, FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity)),
                allowTargetBlock: true);
    }

    private bool CanRest()
    {
        return entity.Alive
            && entity.MountedOn == null
            && !entity.Swimming
            && !entity.FeetInLiquid
            && !FeralKinshipCompanionSystem.IsFoxAwayFromWorld(entity)
            && !FeralKinshipCompanionSystem.IsFoxIncapacitated(entity);
    }
}
