#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinship
{
/// <summary>
/// Core-only owner following for basic PetAI commands. Companions replaces the
/// entire task list when installed, so this deliberately remains a small
/// fallback rather than a second advanced companion AI implementation.
/// </summary>
public sealed class AiTaskFeralKinshipBasicFollow : AiTaskBase
{
    private const int PathSearchDepth = AnimalicaShared.BasicFollowTiming.SearchDepth;
    private const int RouteRefreshIntervalMs = AnimalicaShared.BasicFollowTiming.RefreshIntervalMs;
    private const float PathfinderGoalRadius = 0.6f;
    private const double ExtremeCatchUpDistanceSquared = 64d * 64d;
    private const double FailedRouteCatchUpDistanceSquared = 16d * 16d;
    private const double RouteReplanDistanceSquared = 4d * 4d;

    private readonly float moveSpeed;
    private readonly float maxDistance;
    private readonly bool allowTeleport;
    private Entity? owner;
    private bool stopped;
    private long nextRouteRefreshAtMs;
    private long nextAttemptAtMs;
    private long searchDeadlineMs;
    private double plannedOwnerX;
    private double plannedOwnerY;
    private double plannedOwnerZ;
    private int consecutiveFailures;

    public AiTaskFeralKinshipBasicFollow(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        moveSpeed = taskConfig["movespeed"].AsFloat(0.045f);
        maxDistance = taskConfig["maxDistance"].AsFloat(
            taskConfig["maxdistance"].AsFloat(8f)
        );
        allowTeleport = taskConfig["allowTeleport"].AsBool(true);
    }

    public override bool ShouldExecute()
    {
        if (entity.World.ElapsedMilliseconds < nextAttemptAtMs
            || !IsFollowCommandActive())
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
            && TryTeleportNearOwner())
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
        searchDeadlineMs = entity.World.ElapsedMilliseconds + AnimalicaShared.BasicFollowTiming.SearchTimeoutMs;
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
                StopSuccessfully,
                StopAndRetry,
                StopAndRetry,
                PathSearchDepth,
                1,
                EnumAICreatureType.LandCreature))
        {
            StopAndRetry();
        }
    }

    // Continue also runs while a route is pending so commands and timeouts can cancel it.
    public override bool CanContinueExecute() => true;

    public override bool ContinueExecute(float dt)
    {
        if (stopped
            || !IsFollowCommandActive())
        {
            return false;
        }

        owner = ResolveOwner();
        if (owner?.Alive != true || owner.Pos.Dimension != entity.Pos.Dimension)
        {
            return false;
        }

        if (entity.Pos.SquareDistanceTo(owner.Pos) <= maxDistance * maxDistance)
        {
            consecutiveFailures = 0;
            return false;
        }

        if (allowTeleport
            && entity.Pos.SquareDistanceTo(owner.Pos) > ExtremeCatchUpDistanceSquared
            && TryTeleportNearOwner())
        {
            consecutiveFailures = 0;
            return false;
        }

        long now = entity.World.ElapsedMilliseconds;
        if (!pathTraverser.Ready)
        {
            if (now < searchDeadlineMs) return true;
            StopAndRetry();
            return false;
        }
        if (now >= nextRouteRefreshAtMs)
        {
            nextRouteRefreshAtMs = now + RouteRefreshIntervalMs;
            pathTraverser.CurrentTarget.Set(owner.Pos.X, owner.Pos.Y, owner.Pos.Z);

            double dx = owner.Pos.X - plannedOwnerX;
            double dy = owner.Pos.Y - plannedOwnerY;
            double dz = owner.Pos.Z - plannedOwnerZ;
            if (dx * dx + dy * dy + dz * dz > RouteReplanDistanceSquared)
            {
                stopped = true;
                nextAttemptAtMs = now + 100;
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

    private Entity? ResolveOwner()
    {
        string ownerUid = entity.WatchedAttributes
            .GetTreeAttribute("domesticationstatus")?
            .GetString("owner", string.Empty) ?? string.Empty;
        return string.IsNullOrWhiteSpace(ownerUid)
            ? null
            : entity.World.PlayerByUid(ownerUid)?.Entity;
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

    private bool TryTeleportNearOwner()
    {
        if (owner == null) return false;

        BlockPos origin = owner.Pos.AsBlockPos;
        IBlockAccessor blocks = entity.World.BlockAccessor;
        for (int radius = 1; radius <= 4; radius++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius) continue;

                    for (int dy = 2; dy >= -2; dy--)
                    {
                        int x = origin.X + dx;
                        int y = origin.Y + dy;
                        int z = origin.Z + dz;
                        BlockPos feet = new BlockPos(x, y, z, origin.dimension);
                        if (!blocks.GetBlock(feet.DownCopy()).SideSolid[BlockFacing.UP.Index]) continue;

                        Vec3d candidate = new(
                            x + 0.5,
                            y + origin.dimension * BlockPos.DimensionBoundary,
                            z + 0.5
                        );
                        if (entity.World.CollisionTester.IsColliding(
                                blocks,
                                entity.Properties.SpawnCollisionBox,
                                candidate,
                                false))
                        {
                            continue;
                        }

                        Entity[] nearby = entity.World.GetEntitiesAround(
                            candidate,
                            0.9f,
                            1.5f,
                            other => other != entity && other.Alive
                        );
                        if (nearby.Length > 0) continue;

                        entity.TeleportTo(candidate);
                        entity.PositionBeforeFalling.Set(candidate.X, candidate.Y, candidate.Z);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private void StopSuccessfully()
    {
        stopped = true;
        consecutiveFailures = 0;
    }

    private void StopAndRetry()
    {
        stopped = true;
        consecutiveFailures++;
        nextAttemptAtMs = AnimalicaShared.BasicFollowTiming.RetryAt(entity.World.ElapsedMilliseconds, entity.EntityId);
    }
}
}
