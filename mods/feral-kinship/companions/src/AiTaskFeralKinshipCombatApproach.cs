#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// Shared pathfinding for Kinship combat styles. Flee uses the same exclusive
/// command slot but paths toward safety instead of toward the threat.
/// </summary>
public sealed class AiTaskFeralKinshipCombatApproach : AiTaskBase
{
    private const double HorizontalRouteReplanDistanceSquared = 2.5d * 2.5d;

    private readonly float moveSpeed;
    private FeralKinshipCompanionSystem? system;
    private Entity? targetEntity;
    private Vec3d? targetPosition;
    private float stopDistance;
    private bool fleeMode;
    private bool holdingCombatRange;
    private bool stopped;
    private long nextAttemptAtMs;
    private long nextRetargetAtMs;
    private long suppressedTargetEntityId;
    private long suppressTargetUntilMs;
    private Vec3d suppressedTargetPosition = new();
    private Vec3d plannedDestination = new();
    private readonly CompanionNavigation navigation;

    public AiTaskFeralKinshipCombatApproach(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        moveSpeed = taskConfig["movespeed"].AsFloat(0.045f);
        navigation = new CompanionNavigation(entity, pathTraverser);
    }

    public override bool ShouldExecute()
    {
        long now = entity.World.ElapsedMilliseconds;
        if (now < nextAttemptAtMs || !FeralKinshipCompanionSystem.CanRunCompanionCombatTask(entity))
        {
            return false;
        }

        system ??= entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        fleeMode = FeralKinshipCompanionSystem.GetCompanionCombatStyle(entity) == CompanionCombatStyle.Flee;
        if (fleeMode)
        {
            targetEntity = null;
            stopDistance = 1.5f;
            return system.TryResolveCompanionFleeDestination(entity, out targetPosition)
                && targetPosition != null
                && targetPosition.AsBlockPos.dimension == entity.Pos.Dimension
                && entity.Pos.SquareDistanceTo(targetPosition) > stopDistance * stopDistance;
        }

        targetPosition = null;
        if (!system.TryResolveCompanionCombatTarget(entity, out targetEntity) || targetEntity == null)
        {
            return false;
        }
        if (targetEntity.EntityId == suppressedTargetEntityId && now < suppressTargetUntilMs)
        {
            double verticalMove = Math.Abs(targetEntity.Pos.Y - suppressedTargetPosition.Y);
            double horizontalMove = targetEntity.Pos.XYZ.HorizontalSquareDistanceTo(suppressedTargetPosition);
            if (verticalMove < 0.45d && horizontalMove < 1.25d * 1.25d) return false;
        }
        stopDistance = Math.Max(
            0.8f,
            entity.SelectionBox.XSize * 0.5f + targetEntity.SelectionBox.XSize * 0.5f + 0.25f
        );
        // Staying beside a live target is still combat work. If this task
        // releases slot 0 between melee swings, Follow takes over for a moment
        // and produces a combat/follow circle.
        return true;
    }

    public override void StartExecute()
    {
        base.StartExecute();
        stopped = false;
        holdingCombatRange = false;
        nextRetargetAtMs = entity.World.ElapsedMilliseconds + 750;
        Vec3d? destination = fleeMode ? targetPosition : targetEntity?.Pos.XYZ;
        if (!fleeMode
            && targetEntity != null
            && IsAtCombatRange(targetEntity))
        {
            HoldCombatRange();
            return;
        }

        if (destination != null) plannedDestination.Set(destination.X, destination.Y, destination.Z);
        if (destination == null || !navigation.Start(
                destination,
                moveSpeed,
                stopDistance,
                OnDestinationReached,
                StopAndRetry,
                4500,
                fleeMode ? CompanionNavigationTargetKind.Position : CompanionNavigationTargetKind.Approach,
                5))
        {
            if (!stopped) StopAndRetry();
        }
    }

    // Vintage Story treats false here as "do not tick yet", not "finish the
    // task". Always enter ContinueExecute so an asynchronously completed or
    // failed route can release this slot and its movement animation.
    public override bool CanContinueExecute() => true;

    public override bool ContinueExecute(float dt)
    {
        if (stopped || !FeralKinshipCompanionSystem.CanRunCompanionCombatTask(entity)) return false;
        system ??= entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();

        Vec3d? destination;
        if (fleeMode)
        {
            if (FeralKinshipCompanionSystem.GetCompanionCombatStyle(entity) != CompanionCombatStyle.Flee
                || targetPosition == null)
            {
                return false;
            }
            destination = targetPosition;
        }
        else
        {
            if (!system.TryResolveCompanionCombatTarget(entity, out targetEntity) || targetEntity == null)
            {
                return false;
            }
            destination = targetEntity.Pos.XYZ;
            if (IsAtCombatRange(targetEntity))
            {
                navigation.Cancel();
                HoldCombatRange();
                return true;
            }

            if (holdingCombatRange)
            {
                // The target left melee range. Re-enter through ShouldExecute
                // so StartExecute can request a fresh asynchronous route.
                holdingCombatRange = false;
                return false;
            }
        }

        long now = entity.World.ElapsedMilliseconds;
        navigation.Tick();
        if (!holdingCombatRange && !navigation.IsRunning) return false;
        if (!fleeMode && now >= nextRetargetAtMs)
        {
            nextRetargetAtMs = now + 750;
            double verticalMove = Math.Abs(plannedDestination.Y - destination.Y);
            double horizontalMove = plannedDestination.HorizontalSquareDistanceTo(destination);
            if (verticalMove >= 0.45d || horizontalMove >= HorizontalRouteReplanDistanceSquared)
            {
                // Retarget() discards A* waypoints and resumes direct steering.
                // End this leg so the next pass computes a fresh routed approach.
                // Vertical falls stay sensitive, while an ordinary moving
                // target must shift far enough to justify an async rebuild.
                nextAttemptAtMs = now + 100;
                return false;
            }
        }
        return holdingCombatRange || navigation.IsRunning;
    }

    public override void FinishExecute(bool cancelled)
    {
        navigation.Cancel();
        targetEntity = null;
        targetPosition = null;
        holdingCombatRange = false;
        base.FinishExecute(cancelled);
    }

    private void Stop() => stopped = true;

    private void OnDestinationReached()
    {
        if (fleeMode)
        {
            Stop();
            return;
        }

        if (targetEntity?.Alive == true
            && IsAtCombatRange(targetEntity))
        {
            HoldCombatRange();
            return;
        }

        stopped = true;
        nextAttemptAtMs = entity.World.ElapsedMilliseconds + 100;
    }

    private void HoldCombatRange()
    {
        stopped = false;
        holdingCombatRange = true;
        entity.Controls.StopAllMovement();
    }

    private bool IsAtCombatRange(Entity target)
    {
        return Math.Abs(entity.Pos.Y - target.Pos.Y) <= 0.65d
            && CompanionNavigation.IsClearArrival(
                entity, target.Pos.XYZ, stopDistance, allowTargetBlock: false);
    }

    private void StopAndRetry()
    {
        stopped = true;
        entity.Controls.StopAllMovement();
        if (!fleeMode && targetEntity != null)
        {
            suppressedTargetEntityId = targetEntity.EntityId;
            suppressedTargetPosition.Set(targetEntity.Pos.X, targetEntity.Pos.Y, targetEntity.Pos.Z);
            suppressTargetUntilMs = entity.World.ElapsedMilliseconds + 3500;
        }
        nextAttemptAtMs = entity.World.ElapsedMilliseconds + 1250;
    }
}
