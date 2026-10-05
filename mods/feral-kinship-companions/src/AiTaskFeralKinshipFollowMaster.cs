#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// Kinship-owned owner following. The selected distance is a ring with
/// hysteresis, so the companion does not constantly start and stop at one
/// exact number.
/// </summary>
public sealed class AiTaskFeralKinshipFollowMaster : AiTaskBase
{
    private const int PathSearchDepth = 4000;
    private const int RouteRefreshIntervalMs = 750;
    private const int RouteReplanDelayMs = 100;
    private const int RetryIntervalMs = 1500;
    private const int PathSearchTimeoutMs = 10000;
    private const float PathfinderGoalRadius = 0.6f;
    private const double ArrivalVerticalTolerance = 1.5d;
    private const double RouteReplanDistanceSquared = 4d * 4d;
    private const double ExtremeCatchUpDistanceSquared = 64d * 64d;
    private const double FailedRouteCatchUpDistanceSquared = 16d * 16d;

    private readonly float moveSpeed;
    private Entity? owner;
    private float innerRadius;
    private float outerRadius;
    private bool stopped;
    private long nextRouteRefreshAtMs;
    private long nextAttemptAtMs;
    private double plannedOwnerX;
    private double plannedOwnerY;
    private double plannedOwnerZ;
    private int consecutiveFailures;
    private long pathSearchStartedAtMs;
    private int pathAttemptGeneration;
    private FeralKinshipCompanionSystem? system;

    public AiTaskFeralKinshipFollowMaster(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        moveSpeed = taskConfig["movespeed"].AsFloat(0.045f);
    }

    public override bool ShouldExecute()
    {
        if (entity.World.ElapsedMilliseconds < nextAttemptAtMs
            || !FeralKinshipCompanionSystem.CanRunCompanionCommandTask(entity)
            || FeralKinshipCompanionSystem.GetCompanionActivityMode(entity) != CompanionActivityMode.Follow)
        {
            return false;
        }

        owner = ResolveOwner();
        if (owner?.Alive != true || owner.Pos.Dimension != entity.Pos.Dimension)
        {
            owner = null;
            return false;
        }

        CompanionFollowDistance.GetRing(
            FeralKinshipCompanionSystem.GetCompanionFollowDistance(entity),
            out innerRadius,
            out outerRadius
        );
        double distanceSquared = entity.Pos.SquareDistanceTo(owner.Pos);
        if (distanceSquared > ExtremeCatchUpDistanceSquared
            || (consecutiveFailures >= 3 && distanceSquared > FailedRouteCatchUpDistanceSquared))
        {
            system ??= entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
            if (system.TryTeleportCompanionToCommandHome(entity, owner.Pos.XYZ))
            {
                consecutiveFailures = 0;
                owner = null;
                return false;
            }
        }
        return !IsInsideFollowRing(owner, outerRadius);
    }

    public override void StartExecute()
    {
        base.StartExecute();
        stopped = false;
        int generation = ++pathAttemptGeneration;
        pathSearchStartedAtMs = entity.World.ElapsedMilliseconds;
        nextRouteRefreshAtMs = entity.World.ElapsedMilliseconds + RouteRefreshIntervalMs;
        if (owner != null)
        {
            plannedOwnerX = owner.Pos.X;
            plannedOwnerY = owner.Pos.Y;
            plannedOwnerZ = owner.Pos.Z;
        }
        if (owner == null || !pathTraverser.NavigateTo_Async(
                owner.Pos.XYZ,
                moveSpeed,
                PathfinderGoalRadius,
                () => Stop(generation),
                () => StopAndRetry(generation),
                () => StopAndRetry(generation),
                PathSearchDepth,
                1,
                EnumAICreatureType.LandCreature))
        {
            StopAndRetry(generation);
        }
    }

    // The task manager skips ContinueExecute while CanContinueExecute is false
    // without releasing the running-task slot. Keep this task updateable while
    // an asynchronous path search is pending so a failed/hung search can time out.
    public override bool CanContinueExecute() => true;

    public override bool ContinueExecute(float dt)
    {
        if (stopped
            || FeralKinshipCompanionSystem.GetCompanionActivityMode(entity) != CompanionActivityMode.Follow)
        {
            return false;
        }

        long now = entity.World.ElapsedMilliseconds;
        if (!pathTraverser.Ready)
        {
            if (now - pathSearchStartedAtMs >= PathSearchTimeoutMs)
            {
                StopAndRetry(pathAttemptGeneration);
                return false;
            }

            return true;
        }

        if (!pathTraverser.Active)
        {
            StopAndRetry(pathAttemptGeneration);
            return false;
        }

        owner = ResolveOwner();
        if (owner?.Alive != true || owner.Pos.Dimension != entity.Pos.Dimension)
        {
            return false;
        }

        if (IsInsideFollowRing(owner, innerRadius))
        {
            consecutiveFailures = 0;
            return false;
        }

        if (entity.Pos.SquareDistanceTo(owner.Pos) > ExtremeCatchUpDistanceSquared)
        {
            system ??= entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
            if (system.TryTeleportCompanionToCommandHome(entity, owner.Pos.XYZ))
            {
                consecutiveFailures = 0;
                return false;
            }
        }

        if (now >= nextRouteRefreshAtMs)
        {
            nextRouteRefreshAtMs = now + RouteRefreshIntervalMs;
            pathTraverser.CurrentTarget.Set(owner.Pos.X, owner.Pos.Y, owner.Pos.Z);

            double ownerMoveX = owner.Pos.X - plannedOwnerX;
            double ownerMoveY = owner.Pos.Y - plannedOwnerY;
            double ownerMoveZ = owner.Pos.Z - plannedOwnerZ;
            if (ownerMoveX * ownerMoveX + ownerMoveY * ownerMoveY + ownerMoveZ * ownerMoveZ
                > RouteReplanDistanceSquared)
            {
                // Retarget() skips every remaining A* waypoint and walks directly at
                // the owner. End this task instead so the next pass computes a real
                // route from the companion's current position.
                stopped = true;
                nextAttemptAtMs = now + RouteReplanDelayMs;
                return false;
            }
        }
        return pathTraverser.Active;
    }

    public override void FinishExecute(bool cancelled)
    {
        pathAttemptGeneration++;
        pathTraverser.Stop();
        owner = null;
        base.FinishExecute(cancelled);
    }

    private Entity? ResolveOwner()
    {
        string ownerUid = FeralKinshipCompanionSystem.GetCompanionOwnerUid(entity);
        return string.IsNullOrWhiteSpace(ownerUid) ? null : entity.World.PlayerByUid(ownerUid)?.Entity;
    }

    private bool IsInsideFollowRing(Entity target, float radius)
    {
        if (Math.Abs(entity.Pos.Y - target.Pos.Y) > ArrivalVerticalTolerance)
        {
            return false;
        }

        double deltaX = entity.Pos.X - target.Pos.X;
        double deltaZ = entity.Pos.Z - target.Pos.Z;
        return deltaX * deltaX + deltaZ * deltaZ <= radius * radius;
    }

    private void Stop(int generation)
    {
        if (generation != pathAttemptGeneration) return;
        stopped = true;
        consecutiveFailures = 0;
        entity.Controls.StopAllMovement();
    }

    private void StopAndRetry(int generation)
    {
        if (generation != pathAttemptGeneration) return;
        stopped = true;
        consecutiveFailures++;
        entity.Controls.StopAllMovement();
        nextAttemptAtMs = entity.World.ElapsedMilliseconds + RetryIntervalMs;
    }

}
