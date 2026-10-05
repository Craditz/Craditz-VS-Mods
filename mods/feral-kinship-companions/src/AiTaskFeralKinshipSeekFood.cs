#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>Walks to a stocked Dining Board and consumes one diet-valid item.</summary>
public sealed class AiTaskFeralKinshipSeekFood : AiTaskBase
{
    private readonly bool emergencyOnly;
    private readonly float moveSpeed;
    private BlockPos? diningPos;
    private Vec3d? target;
    private bool done;
    private bool stuck;
    private long nextCheckAtMs;
    private readonly CompanionNavigation navigation;

    public AiTaskFeralKinshipSeekFood(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        emergencyOnly = taskConfig["emergencyOnly"].AsBool(false);
        moveSpeed = taskConfig["movespeed"].AsFloat(emergencyOnly ? 0.035f : 0.02f);
        navigation = new CompanionNavigation(entity, pathTraverser);
    }

    public override bool ShouldExecute()
    {
        if (entity.World.ElapsedMilliseconds < nextCheckAtMs
            || FeralKinshipCompanionSystem.IsFoxAwayFromWorld(entity)
            || FeralKinshipCompanionSystem.IsFoxIncapacitated(entity))
        {
            return false;
        }
        nextCheckAtMs = entity.World.ElapsedMilliseconds + (emergencyOnly ? 1000 : 5000);

        FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (emergencyOnly != system.IsCompanionFoodRestricted(entity))
        {
            return false;
        }
        return system.TryResolveCompanionFoodTarget(entity, out diningPos, out target)
            && diningPos != null
            && target != null;
    }

    public override void StartExecute()
    {
        base.StartExecute();
        done = false;
        stuck = false;
        if (target == null || diningPos == null)
        {
            done = true;
            return;
        }

        float arrival = Math.Max(0.75f, FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity));
        bool queued = navigation.Start(
            target,
            moveSpeed,
            arrival,
            OnGoalReached,
            OnFailed,
            3000,
            CompanionNavigationTargetKind.Position,
            4);
        if (!queued && !stuck) OnFailed();
    }

    // A completed or failed route still needs one ContinueExecute call so the
    // manager can observe false, call FinishExecute, and release the slot.
    public override bool CanContinueExecute() => true;

    public override bool ContinueExecute(float dt)
    {
        if (done || stuck || target == null || diningPos == null
            || FeralKinshipCompanionSystem.IsFoxAwayFromWorld(entity)
            || FeralKinshipCompanionSystem.IsFoxIncapacitated(entity))
        {
            return false;
        }

        FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (!FeralKinshipCompanionSystem.ShouldCompanionSeekFood(entity)
            || (emergencyOnly && !system.IsCompanionFoodRestricted(entity)))
        {
            return false;
        }

        // A path callback can be deferred until after the task manager has
        // already asked us to continue. Consume on this tick when the
        // traverser has validated the destination, matching the reliable
        // handoff used by the dropped-item task.
        if (navigation.IsAtValidatedDestination)
        {
            OnGoalReached();
            return false;
        }

        navigation.Tick();
        if (navigation.IsAtValidatedDestination)
        {
            OnGoalReached();
            return false;
        }
        return navigation.IsRunning;
    }

    public override void FinishExecute(bool cancelled)
    {
        navigation.Cancel();
        diningPos = null;
        target = null;
        base.FinishExecute(cancelled);
    }

    private void OnGoalReached()
    {
        done = true;
        entity.Controls.StopAllMovement();
        if (diningPos != null)
        {
            entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()
                .TryConsumeCompanionFood(entity, diningPos);
        }
    }

    private void OnFailed()
    {
        stuck = true;
        nextCheckAtMs = entity.World.ElapsedMilliseconds + 3000;
    }
}
