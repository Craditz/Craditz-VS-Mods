#nullable enable

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed class AiTaskFeralKinshipCommandReturnHome : AiTaskBase
{
    private const int PathSearchDepth = 5000;
    private const int RetryIntervalMs = 1200;
    private readonly float moveSpeed;
    private FeralKinshipCompanionSystem? system;
    private Vec3d? target;
    private bool stopped;
    private long nextAttemptAtMs;
    private readonly CompanionNavigation navigation;

    public AiTaskFeralKinshipCommandReturnHome(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        moveSpeed = taskConfig["movespeed"].AsFloat(0.045f);
        navigation = new CompanionNavigation(entity, pathTraverser);
    }

    public override bool ShouldExecute()
    {
        if (entity.World.ElapsedMilliseconds < nextAttemptAtMs
            || !FeralKinshipCompanionSystem.CanRunCompanionCommandTask(entity)
            || FeralKinshipCompanionSystem.GetCompanionActivityMode(entity) != CompanionActivityMode.ReturnHome)
        {
            return false;
        }

        system ??= entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (system.TryResolveCompanionCommandHome(entity, out target) != true || target == null)
        {
            nextAttemptAtMs = entity.World.ElapsedMilliseconds + RetryIntervalMs;
            return false;
        }

        float arrival = FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity);
        int targetDimension = target.AsBlockPos.dimension;
        if (entity.Pos.Dimension == targetDimension
            && CompanionNavigation.IsClearArrival(entity, target, arrival, allowTargetBlock: true))
        {
            system.CompleteCompanionActivity(entity, CompanionActivityMode.ReturnHome);
            return false;
        }

        long elapsed = FeralKinshipCompanionSystem.GetCompanionActivityElapsedMs(entity);
        // Give the routed approach time to make visible progress even when
        // the home is beyond the old unload-distance heuristic.  The command
        // still has the bounded teleport fallback, but it should not look as
        // though it immediately failed.
        if (elapsed >= 12000)
        {
            if (system.TryTeleportCompanionToCommandHome(entity, target))
            {
                system.CompleteCompanionActivity(entity, CompanionActivityMode.ReturnHome);
            }
            else
            {
                nextAttemptAtMs = entity.World.ElapsedMilliseconds + RetryIntervalMs;
            }
            return false;
        }

        return entity.Pos.Dimension == targetDimension;
    }

    public override void StartExecute()
    {
        base.StartExecute();
        stopped = false;
        if (target == null || !navigation.Start(
                target,
                moveSpeed,
                FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity),
                OnArrived,
                OnFailed,
                PathSearchDepth,
                CompanionNavigationTargetKind.Position,
                5,
                adaptiveSearch: true))
        {
            if (!stopped) OnFailed();
        }
    }

    public override bool CanContinueExecute() => true;

    public override bool ContinueExecute(float dt)
    {
        if (stopped
            || FeralKinshipCompanionSystem.GetCompanionActivityMode(entity) != CompanionActivityMode.ReturnHome)
        {
            return false;
        }

        if (target == null) return false;

        float arrival = FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity);
        if (CompanionNavigation.IsClearArrival(entity, target, arrival, allowTargetBlock: true))
        {
            OnArrived();
            return false;
        }

        if (FeralKinshipCompanionSystem.GetCompanionActivityElapsedMs(entity) >= 12000
            && system != null
            && system.TryTeleportCompanionToCommandHome(entity, target))
        {
            system.CompleteCompanionActivity(entity, CompanionActivityMode.ReturnHome);
            return false;
        }

        navigation.Tick();
        return navigation.IsRunning;
    }

    public override void FinishExecute(bool cancelled)
    {
        navigation.Cancel();
        target = null;
        base.FinishExecute(cancelled);
    }

    private void OnArrived()
    {
        stopped = true;
        system?.CompleteCompanionActivity(entity, CompanionActivityMode.ReturnHome);
    }

    private void OnFailed()
    {
        stopped = true;
        nextAttemptAtMs = entity.World.ElapsedMilliseconds + RetryIntervalMs;
    }

}
