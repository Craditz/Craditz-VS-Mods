#nullable enable

using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TamablesFotsa;

internal sealed class AiTaskTamablesFotsaAquaticFollow : AiTaskBase
{
    private const int RouteRefreshIntervalMs = 1000;
    private const int RetryIntervalMs = 1500;
    private const float RouteGoalRadius = 0.35f;
    private const double ExtremeCatchUpDistanceSquared = 64d * 64d;
    private const double FailedRouteCatchUpDistanceSquared = 16d * 16d;
    private const double OwnerReplanDistanceSquared = 2.5d * 2.5d;

    private readonly float moveSpeed;
    private readonly float maxDistance;
    private readonly bool allowTeleport;
    private Entity? owner;
    private bool stopped;
    private long nextRouteRefreshAtMs;
    private long nextAttemptAtMs;
    private double plannedOwnerX;
    private double plannedOwnerY;
    private double plannedOwnerZ;
    private int consecutiveFailures;

    public AiTaskTamablesFotsaAquaticFollow(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        moveSpeed = taskConfig["movespeed"].AsFloat(0.045f);
        maxDistance = taskConfig["maxDistance"].AsFloat(taskConfig["maxdistance"].AsFloat(8f));
        allowTeleport = taskConfig["allowTeleport"].AsBool(true);
    }

    public override bool ShouldExecute()
    {
        if (entity.World.ElapsedMilliseconds < nextAttemptAtMs
            || !IsFollowCommandActive()
            || !TamablesFotsaAquaticNavigation.IsInSwimmableWater(entity))
        {
            return false;
        }

        owner = ResolveOwner();
        if (owner?.Alive != true || owner.Pos.Dimension != entity.Pos.Dimension)
        {
            owner = null;
            return false;
        }

        double distanceSquared = entity.Pos.SquareDistanceTo(owner.Pos);
        if (allowTeleport
            && (distanceSquared > ExtremeCatchUpDistanceSquared
                || (consecutiveFailures >= 3 && distanceSquared > FailedRouteCatchUpDistanceSquared))
            && TamablesFotsaAquaticNavigation.TryTeleportToWaterNearOwner(entity, owner))
        {
            consecutiveFailures = 0;
            owner = null;
            return false;
        }

        return distanceSquared > maxDistance * maxDistance;
    }

    public override void StartExecute()
    {
        base.StartExecute();
        stopped = false;
        owner ??= ResolveOwner();
        nextRouteRefreshAtMs = entity.World.ElapsedMilliseconds + RouteRefreshIntervalMs;

        if (owner == null || !StartConnectedWaterRoute())
        {
            StopAndRetry();
        }
    }

    public override bool CanContinueExecute() => pathTraverser.Ready;

    public override bool ContinueExecute(float dt)
    {
        if (stopped
            || !IsFollowCommandActive()
            || !TamablesFotsaAquaticNavigation.IsInSwimmableWater(entity))
        {
            return false;
        }

        owner = ResolveOwner();
        if (owner?.Alive != true || owner.Pos.Dimension != entity.Pos.Dimension) return false;

        double distanceSquared = entity.Pos.SquareDistanceTo(owner.Pos);
        if (distanceSquared <= maxDistance * maxDistance)
        {
            consecutiveFailures = 0;
            return false;
        }

        if (allowTeleport
            && distanceSquared > ExtremeCatchUpDistanceSquared
            && TamablesFotsaAquaticNavigation.TryTeleportToWaterNearOwner(entity, owner))
        {
            consecutiveFailures = 0;
            return false;
        }

        long now = entity.World.ElapsedMilliseconds;
        if (now >= nextRouteRefreshAtMs)
        {
            nextRouteRefreshAtMs = now + RouteRefreshIntervalMs;
            double dx = owner.Pos.X - plannedOwnerX;
            double dy = owner.Pos.Y - plannedOwnerY;
            double dz = owner.Pos.Z - plannedOwnerZ;
            if (dx * dx + dy * dy + dz * dz > OwnerReplanDistanceSquared
                && !StartConnectedWaterRoute())
            {
                StopAndRetry();
                return false;
            }
        }

        return pathTraverser.Active;
    }

    public override void FinishExecute(bool cancelled)
    {
        pathTraverser.Stop();
        owner = null;
        base.FinishExecute(cancelled);
    }

    private bool StartConnectedWaterRoute()
    {
        if (owner == null
            || !TamablesFotsaAquaticNavigation.TryBuildConnectedWaterRoute(
                entity,
                owner,
                maxDistance,
                out List<Vec3d> route))
        {
            return false;
        }

        pathTraverser.FollowRoute(route, moveSpeed, RouteGoalRadius, StopSuccessfully, StopAndRetry);
        plannedOwnerX = owner.Pos.X;
        plannedOwnerY = owner.Pos.Y;
        plannedOwnerZ = owner.Pos.Z;
        return true;
    }

    private Entity? ResolveOwner()
    {
        string ownerUid = entity.WatchedAttributes.GetTreeAttribute("domesticationstatus")?
            .GetString("owner", string.Empty) ?? string.Empty;
        return string.IsNullOrWhiteSpace(ownerUid) ? null : entity.World.PlayerByUid(ownerUid)?.Entity;
    }

    private bool IsFollowCommandActive()
    {
        ITreeAttribute? status = entity.WatchedAttributes.GetTreeAttribute("domesticationstatus");
        return string.Equals(
                entity.WatchedAttributes.GetString("activeCommand", string.Empty),
                "followmaster",
                StringComparison.OrdinalIgnoreCase
            )
            && (status?.GetFloat("obedience", 0f) ?? 0f) >= 0.6f;
    }

    private void StopSuccessfully()
    {
        stopped = true;
        consecutiveFailures = 0;
        if (owner != null && entity.Pos.SquareDistanceTo(owner.Pos) > maxDistance * maxDistance)
        {
            nextAttemptAtMs = entity.World.ElapsedMilliseconds + RetryIntervalMs;
        }
    }

    private void StopAndRetry()
    {
        pathTraverser.Stop();
        stopped = true;
        consecutiveFailures++;
        int retryDelay = Math.Min(10000, RetryIntervalMs * Math.Max(1, consecutiveFailures));
        nextAttemptAtMs = entity.World.ElapsedMilliseconds + retryDelay;
    }
}

internal sealed class AiTaskTamablesFotsaAquaticWander : AiTaskWander
{
    public AiTaskTamablesFotsaAquaticWander(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
    }

    public override bool ShouldExecute()
    {
        return base.ShouldExecute()
            && TamablesFotsaAquaticNavigation.IsInSwimmableWater(entity)
            && TamablesFotsaAquaticNavigation.IsWaterLineClear(entity, MainTarget);
    }
}
