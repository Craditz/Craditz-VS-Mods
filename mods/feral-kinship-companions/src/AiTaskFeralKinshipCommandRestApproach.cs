#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed class AiTaskFeralKinshipCommandRestApproach : AiTaskBase
{
    private const double ReturnHomeFallbackDistanceSquared = 56d * 56d;
    private const long ReturnHomeFallbackDelayMs = 2000;
    private readonly float moveSpeed;
    private Vec3d? target;
    private bool stopped;
    private long nextAttemptAtMs;
    private int consecutiveFailures;
    private bool failureTeleportUsed;
    private readonly CompanionNavigation navigation;

    public AiTaskFeralKinshipCommandRestApproach(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        moveSpeed = taskConfig["movespeed"].AsFloat(0.02f);
        navigation = new CompanionNavigation(entity, pathTraverser);
    }

    public override bool ShouldExecute()
    {
        if (entity.World.ElapsedMilliseconds < nextAttemptAtMs
            || !FeralKinshipCompanionSystem.CanRunCompanionCommandTask(entity)
            || FeralKinshipCompanionSystem.GetCompanionActivityMode(entity) != CompanionActivityMode.Rest
            || FeralKinshipCompanionSystem.HasCompanionRestArrival(entity))
        {
            return false;
        }

        // A fresh Rest command is a fresh route attempt, even if an earlier
        // command on this same loaded entity exhausted its failure allowance.
        if (FeralKinshipCompanionSystem.GetCompanionActivityElapsedMs(entity) < 1000)
        {
            consecutiveFailures = 0;
            failureTeleportUsed = false;
        }
        FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (!system.TryResolveCompanionCommandHome(entity, out target) || target == null)
        {
            target = null;
            return false;
        }

        if (consecutiveFailures >= 3)
        {
            if (!failureTeleportUsed && system.TryTeleportCompanionToCommandHome(entity, target))
            {
                consecutiveFailures = 0;
                failureTeleportUsed = true;
                target = null;
                nextAttemptAtMs = entity.World.ElapsedMilliseconds + 250;
            }
            else
            {
                // Never leave Rest command-locked if even its safe fallback
                // cannot find a valid destination.
                system.MarkCompanionRestArrival(entity);
            }
            return false;
        }

        bool differentDimension = target.AsBlockPos.dimension != entity.Pos.Dimension;
        bool beyondWalkingRange = differentDimension
            || entity.Pos.SquareDistanceTo(target) > ReturnHomeFallbackDistanceSquared;
        if (beyondWalkingRange
            && differentDimension
            && FeralKinshipCompanionSystem.GetCompanionActivityElapsedMs(entity) >= ReturnHomeFallbackDelayMs)
        {
            if (system.TryTeleportCompanionToCommandHome(entity, target))
            {
                consecutiveFailures = 0;
                target = null;
                nextAttemptAtMs = entity.World.ElapsedMilliseconds + 250;
            }
            return false;
        }
        if (differentDimension) return false;

        float arrival = Math.Max(0.65f, FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity));
        return !CompanionNavigation.IsClearArrival(entity, target, arrival, allowTargetBlock: true);
    }

    public override void StartExecute()
    {
        base.StartExecute();
        stopped = false;
        float arrival = Math.Max(0.65f, FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity));
        if (target == null || !navigation.Start(
                target,
                moveSpeed,
                arrival,
                OnArrived,
                OnFailed,
                5000,
                CompanionNavigationTargetKind.Position,
                5))
        {
            if (!stopped) OnFailed();
        }
    }

    public override bool CanContinueExecute() => true;

    public override bool ContinueExecute(float dt)
    {
        if (stopped
            || FeralKinshipCompanionSystem.GetCompanionActivityMode(entity) != CompanionActivityMode.Rest
            || target == null)
        {
            return false;
        }

        if (entity.Pos.SquareDistanceTo(target) > ReturnHomeFallbackDistanceSquared
            && FeralKinshipCompanionSystem.GetCompanionActivityElapsedMs(entity) >= 12000)
        {
            FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
            if (system.TryTeleportCompanionToCommandHome(entity, target)) consecutiveFailures = 0;
            stopped = true;
            return false;
        }

        float arrival = Math.Max(0.65f, FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity));
        if (CompanionNavigation.IsClearArrival(entity, target, arrival, allowTargetBlock: true))
        {
            OnArrived();
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
        consecutiveFailures = 0;
        failureTeleportUsed = false;
        entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>().MarkCompanionRestArrival(entity);
    }

    private void OnFailed()
    {
        stopped = true;
        consecutiveFailures++;
        nextAttemptAtMs = entity.World.ElapsedMilliseconds + 1500;
    }

}
