#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Essentials;
using Vintagestory.GameContent;
using Vintagestory.Systems;

namespace FeralKinshipCompanions;

internal enum CompanionNavigationTargetKind
{
    Position,
    NaturalCleanup,
    Approach,
    StorageApproach
}

/// <summary>
/// Companion-only adapter around the shared vanilla waypoint traverser. It
/// validates final positions, tries a bounded set of nearby approaches, and
/// prevents a late asynchronous result from reviving an obsolete task.
/// </summary>
internal sealed partial class CompanionNavigation
{
    private const int QueueRetryMs = 100;
    private const int ProgressCheckMs = 1400;
    private const float ApproachWaypointRadius = 0.28f;
    private const int SnowRecoveryWindowMs = 12000;
    private const int DoorTransitPollMs = 250;

    private static readonly ConcurrentDictionary<long, long> ActiveLeases = new();
    private static readonly ConcurrentDictionary<long, SnowRecoveryIntent> SnowRecoveryIntents = new();
    private static readonly ConcurrentDictionary<(IWorldAccessor World, string Door), PendingDoorTransit> PendingDoorTransits = new();
    private static readonly FieldInfo? WaypointsField = typeof(WaypointsTraverser).GetField(
        "waypoints",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? WaypointIndexField = typeof(WaypointsTraverser).GetField(
        "waypointToReachIndex",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private static long nextDoorTransitToken;

    private sealed class SnowRecoveryIntent
    {
        internal SnowRecoveryIntent(Vec3d goal, int failures, long lastFailureAtMs, long expiresAtMs)
        {
            Goal = goal;
            Failures = failures;
            LastFailureAtMs = lastFailureAtMs;
            ExpiresAtMs = expiresAtMs;
        }

        internal Vec3d Goal { get; }
        internal int Failures { get; }
        internal long LastFailureAtMs { get; }
        internal long ExpiresAtMs { get; }
    }

    private sealed class DoorCandidate
    {
        internal required BlockPos Position { get; init; }
        internal required Block Block { get; init; }
        internal required BlockFacing Normal { get; init; }
        internal required AssetLocation OpenSound { get; init; }
        internal required AssetLocation CloseSound { get; init; }
        internal required float OpenPitch { get; init; }
        internal required float ClosePitch { get; init; }
    }

    private sealed class PendingDoorTransit
    {
        internal required long Token { get; init; }
        internal required long EntityId { get; init; }
        internal required string OwnerUid { get; init; }
        internal required ICoreAPI Api { get; init; }
        internal required FeralKinshipCompanionSystem System { get; init; }
        internal required IWorldAccessor World { get; init; }
        internal required BlockPos Position { get; init; }
        internal required BlockFacing Normal { get; init; }
        internal required double StartSide { get; init; }
        internal required long OpenedAtMs { get; init; }
        internal bool ReportedOccupied { get; set; }
        internal required AssetLocation CloseSound { get; init; }
        internal required float ClosePitch { get; init; }
    }

    private readonly EntityAgent entity;
    private readonly WaypointsTraverser traverser;
    private readonly string taskName;
    private CompanionSearchSession? searchDiagnostics;
    private readonly List<Vec3d> candidates = new();
    private readonly HashSet<CompanionBlockedPathCell> blockedDoorCells = new();

    private Action? onReached;
    private Action? onFailed;
    private float moveSpeed;
    private float positionWaypointRadius;
    private float desiredArrivalRadius;
    private CompanionNavigationTargetKind targetKind;
    private Vec3d desiredTarget = new();
    private CompanionSearchBudget searchBudget = new(256, 1, false);
    private readonly CompanionRouteProgress routeProgress = new();
    private bool adaptiveSearch;
    private int retryDelayMs;
    private long startedAtMs;
    private int noRouteFailures;
    private int blockedStartFailures;
    private int traverserStuckFailures;
    private int progressFailures;
    private int arrivalFailures;
    private string lastMovementFailure = "none";
    private int maxAttempts;
    private int candidateIndex;
    private int attempts;
    private long lease;
    private long queuedPathLease;
    private long nextQueueAtMs;
    private long nextProgressCheckAtMs;
    private Vec3d? activeCandidate;
    private bool running;
    private bool queuePending;
    private bool foundPathHooked;
    private bool openedDoorForCurrentCandidate;
    private bool exactCandidateAdded;
    private bool activeCandidateIsExact;
    private int doorRouteReplans;

    internal CompanionNavigation(EntityAgent entity, WaypointsTraverser traverser,
        [CallerFilePath] string taskSource = "")
    {
        this.entity = entity;
        this.traverser = traverser;
        taskName = System.IO.Path.GetFileNameWithoutExtension(taskSource);
    }

    internal bool IsRunning => running;
    internal bool HasActiveRoute => running && traverser.Active;
    internal bool IsAtValidatedDestination =>
        targetKind == CompanionNavigationTargetKind.StorageApproach
            ? IsAtStorageApproachDestination()
            : IsClearArrival(
                entity,
                desiredTarget,
                desiredArrivalRadius,
                targetKind is CompanionNavigationTargetKind.Position or CompanionNavigationTargetKind.NaturalCleanup);

    internal static bool TryGetEmergencySnowGoal(Entity entity, out Vec3d? goal, out int failures)
    {
        goal = null;
        failures = 0;
        if (!SnowRecoveryIntents.TryGetValue(entity.EntityId, out SnowRecoveryIntent? intent)) return false;

        long now = entity.World.ElapsedMilliseconds;
        if (intent.ExpiresAtMs < now || intent.Goal.AsBlockPos.dimension != entity.Pos.Dimension)
        {
            SnowRecoveryIntents.TryRemove(entity.EntityId, out _);
            return false;
        }

        if (intent.Failures < 2) return false;
        goal = intent.Goal.Clone();
        failures = intent.Failures;
        return true;
    }

    internal static void ClearEmergencySnowGoal(Entity entity)
    {
        SnowRecoveryIntents.TryRemove(entity.EntityId, out _);
    }

    internal static bool IsClearArrival(
        EntityAgent entity,
        Vec3d desiredTarget,
        double arrivalRadius,
        bool allowTargetBlock)
    {
        double allowed = Math.Max(0.35d, arrivalRadius) + 0.18d;
        if (entity.Pos.SquareDistanceTo(desiredTarget) > allowed * allowed) return false;

        double verticalOffset = Math.Clamp(entity.CollisionBox.YSize * 0.45d, 0.18d, 0.55d);
        Vec3d from = entity.Pos.XYZ.AddCopy(0, verticalOffset, 0);
        Vec3d to = desiredTarget.AddCopy(0, verticalOffset, 0);
        BlockSelection? blockSelection = null;
        EntitySelection? entitySelection = null;
        entity.World.RayTraceForSelection(
            from,
            to,
            ref blockSelection,
            ref entitySelection,
            (pos, block) => block.GetCollisionBoxes(entity.World.BlockAccessor, pos)?.Length > 0,
            _ => false);
        if (blockSelection == null) return true;

        return allowTargetBlock && blockSelection.Position.Equals(desiredTarget.AsBlockPos);
    }

    internal static bool CanReachSnowLayer(EntityAgent entity, BlockPos snowPosition, double reach)
    {
        if (snowPosition.dimension != entity.Pos.Dimension) return false;
        double verticalOffset = Math.Clamp(entity.CollisionBox.YSize * 0.5d, 0.2d, 0.65d);
        Vec3d from = entity.Pos.XYZ.AddCopy(0, verticalOffset, 0);
        double nearestX = Math.Clamp(entity.Pos.X, snowPosition.X + 0.08d, snowPosition.X + 0.92d);
        double nearestZ = Math.Clamp(entity.Pos.Z, snowPosition.Z + 0.08d, snowPosition.Z + 0.92d);
        double targetY = snowPosition.InternalY + 0.28d;
        Vec3d[] strikePoints =
        {
            new(nearestX, targetY, nearestZ),
            new(snowPosition.X + 0.5d, targetY, snowPosition.Z + 0.5d),
            new(snowPosition.X + 0.12d, targetY, snowPosition.Z + 0.12d),
            new(snowPosition.X + 0.88d, targetY, snowPosition.Z + 0.12d),
            new(snowPosition.X + 0.12d, targetY, snowPosition.Z + 0.88d),
            new(snowPosition.X + 0.88d, targetY, snowPosition.Z + 0.88d)
        };
        BlockPos support = snowPosition.DownCopy();
        foreach (Vec3d target in strikePoints)
        {
            if (entity.Pos.SquareDistanceTo(target) > reach * reach) continue;

            BlockSelection? blockSelection = null;
            EntitySelection? entitySelection = null;
            entity.World.RayTraceForSelection(
                from,
                target,
                ref blockSelection,
                ref entitySelection,
                (pos, block) => block.GetCollisionBoxes(entity.World.BlockAccessor, pos)?.Length > 0,
                _ => false);
            if (blockSelection == null
                || blockSelection.Position.Equals(snowPosition)
                || blockSelection.Position.Equals(support)) return true;
        }

        return false;
    }

    internal bool Start(
        Vec3d desiredTarget,
        float speed,
        float arrivalRadius,
        Action reached,
        Action failed,
        int pathSearchDepth,
        CompanionNavigationTargetKind targetKind,
        int boundedAttempts = 4,
        bool adaptiveSearch = false,
        bool resumableSearch = false,
        bool exactTargetOnly = false)
    {
        Cancel();
        if (!IsFinite(desiredTarget) || desiredTarget.AsBlockPos.dimension != entity.Pos.Dimension)
        {
            failed();
            return false;
        }

        lease = ActiveLeases.AddOrUpdate(entity.EntityId, 1, (_, previous) => previous + 1);
        onReached = reached;
        onFailed = failed;
        moveSpeed = speed;
        maxAttempts = Math.Max(1, boundedAttempts);
        this.adaptiveSearch = adaptiveSearch;
        useJourneyPlanner = resumableSearch;
        ResetOwnedJourney();
        searchBudget = new CompanionSearchBudget(pathSearchDepth, maxAttempts, adaptiveSearch);
        retryDelayMs = CompanionSearchBudget.RetryDelayMs(entity.EntityId, adaptiveSearch);
        startedAtMs = entity.World.ElapsedMilliseconds;
        noRouteFailures = blockedStartFailures = traverserStuckFailures = progressFailures = arrivalFailures = 0;
        lastMovementFailure = "none";
        searchDiagnostics = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>().DiagnosticLoggingEnabled
            ? new CompanionSearchSession() : null;
        CompanionPathDiagnostics.Register(traverser, searchDiagnostics);
        this.targetKind = targetKind;
        this.desiredTarget.Set(desiredTarget.X, desiredTarget.Y, desiredTarget.Z);
        desiredArrivalRadius = Math.Max(0.35f, arrivalRadius);
        positionWaypointRadius = Math.Max(0.35f, arrivalRadius);
        candidateIndex = 0;
        attempts = 0;
        doorRouteReplans = 0;
        running = true;
        queuePending = false;
        openedDoorForCurrentCandidate = false;
        activeCandidate = null;
        blockedDoorCells.Clear();
        if (traverser.Ready) queuedPathLease = 0;
        candidates.Clear();
        BuildCandidates(desiredTarget, desiredArrivalRadius, targetKind, exactTargetOnly);

        if (candidates.Count == 0)
        {
            Fail(reportNavigationFailure: true);
            return false;
        }

        HookFoundPath();
        QueueNextCandidate();
        return running;
    }

    internal void Tick()
    {
        if (!running) return;
        if (!OwnsLease())
        {
            StopStaleCallback(lease);
            return;
        }

        long now = entity.World.ElapsedMilliseconds;
        if (useJourneyPlanner && TickOwnedJourney(now)) return;
        if (queuePending && now >= nextQueueAtMs)
        {
            QueueNextCandidate();
            return;
        }

        if (!traverser.Active || activeCandidate == null || now < nextProgressCheckAtMs) return;

        nextProgressCheckAtMs = now + ProgressCheckMs;
        if (TryReadRouteProgress(out int waypoint, out double distance)
            && routeProgress.IsStalled(waypoint, distance))
        {
            progressFailures++;
            CaptureMovementFailure("route-stalled");
            traverser.Stop();
            RetryCurrentFailure(retrySameCandidate: adaptiveSearch);
        }
    }

    internal void Cancel()
    {
        CancelOwnedSearch();
        if (running && OwnsLease())
        {
            // Tasks may finish an interaction directly after validating arrival,
            // before the traverser's callback. Do not label every Cancel a failure.
            LogNavigationSummary("task-ended");
            ActiveLeases.AddOrUpdate(entity.EntityId, 1, (_, previous) => previous + 1);
        }

        running = false;
        queuePending = false;
        CompanionPathDiagnostics.Unregister(traverser, searchDiagnostics);
        traverser.Stop();
        if (traverser.Ready)
        {
            queuedPathLease = 0;
            UnhookFoundPath();
        }
    }

    private void BuildCandidates(Vec3d desiredTarget, float arrivalRadius, CompanionNavigationTargetKind targetKind, bool exactTargetOnly)
    {
        exactCandidateAdded = false;
        bool storageApproach = targetKind == CompanionNavigationTargetKind.StorageApproach;
        bool naturalCleanup = targetKind == CompanionNavigationTargetKind.NaturalCleanup;
        if ((targetKind is CompanionNavigationTargetKind.Position or CompanionNavigationTargetKind.NaturalCleanup
                || storageApproach)
            && IsSafeStandingPosition(desiredTarget, naturalCleanup))
        {
            candidates.Add(desiredTarget.Clone());
            exactCandidateAdded = true;
        }

        // Local Follow walks must not substitute a destination outside their permitted circle.
        if (exactTargetOnly) return;

        double ring = storageApproach
            ? Math.Max(0.38d, Math.Min(0.65d, arrivalRadius - ApproachWaypointRadius))
            : Math.Max(0.38d, arrivalRadius - ApproachWaypointRadius);
        int[] yOffsets = storageApproach ? new[] { 0 } : new[] { 0, 1, -1, 2, -2 };
        int ringCount = storageApproach ? 1 : 3;
        for (int ringIndex = 0; ringIndex < ringCount; ringIndex++)
        {
            double radius = ring + ringIndex * 0.75d;
            for (int angleIndex = 0; angleIndex < 16; angleIndex++)
            {
                double angle = angleIndex * GameMath.TWOPI / 16d;
                double x = desiredTarget.X + Math.Sin(angle) * radius;
                double z = desiredTarget.Z + Math.Cos(angle) * radius;
                foreach (int yOffset in yOffsets)
                {
                    Vec3d candidate = new(x, desiredTarget.Y + yOffset, z);
                    if (IsSafeStandingPosition(candidate, naturalCleanup) && !ContainsNearDuplicate(candidate))
                    {
                        candidates.Add(candidate);
                        break;
                    }
                }
            }
        }

        int sortStart = exactCandidateAdded ? 1 : 0;
        if (candidates.Count - sortStart > 1)
        {
            List<Vec3d> remaining = candidates.Skip(sortStart).ToList();
            List<Vec3d> ordered = new();
            double towardEntity = Math.Atan2(
                entity.Pos.X - desiredTarget.X,
                entity.Pos.Z - desiredTarget.Z);
            double[] angleOffsets =
            {
                0d,
                GameMath.PIHALF,
                -GameMath.PIHALF,
                Math.PI,
                Math.PI / 4d,
                -Math.PI / 4d,
                Math.PI * 3d / 4d,
                -Math.PI * 3d / 4d
            };
            foreach (double angleOffset in angleOffsets)
            {
                if (remaining.Count == 0) break;
                double preferred = towardEntity + angleOffset;
                Vec3d selected = remaining.MinBy(candidate =>
                {
                    double angle = Math.Atan2(candidate.X - desiredTarget.X, candidate.Z - desiredTarget.Z);
                    double angularError = Math.Abs(GameMath.AngleRadDistance((float)angle, (float)preferred));
                    double radiusError = Math.Abs(
                        Math.Sqrt(candidate.HorizontalSquareDistanceTo(desiredTarget)) - ring);
                    return angularError * 4d + radiusError;
                })!;
                ordered.Add(selected);
                remaining.Remove(selected);
            }

            ordered.AddRange(remaining.OrderBy(candidate => entity.Pos.SquareDistanceTo(candidate)));
            candidates.RemoveRange(sortStart, candidates.Count - sortStart);
            candidates.AddRange(ordered);
        }
    }

    private bool IsAtStorageApproachDestination()
    {
        if (activeCandidate == null) return false;

        // StorageTarget is already a walkable tile beside the container.
        // Keep fallback waypoints close to that tile and validate the fox's
        // actual position, not just the waypoint it was sent toward.
        double maxHorizontalDistance = Math.Max(0.9d, desiredArrivalRadius + 0.35d);
        if (entity.Pos.XYZ.HorizontalSquareDistanceTo(desiredTarget)
            > maxHorizontalDistance * maxHorizontalDistance)
        {
            return false;
        }

        return Math.Abs(entity.Pos.Y - desiredTarget.Y) <= 0.5d
            && Math.Abs(activeCandidate.Y - desiredTarget.Y) <= 0.35d
            && IsClearArrival(entity, activeCandidate, ApproachWaypointRadius, false);
    }

    private bool ContainsNearDuplicate(Vec3d candidate)
    {
        return candidates.Any(existing => existing.SquareDistanceTo(candidate) < 0.2d * 0.2d);
    }

    internal bool IsSafeStandingPosition(Vec3d candidate, bool allowNaturalCleanupWater = false)
    {
        IBlockAccessor blocks = entity.World.BlockAccessor;
        if (entity.World.CollisionTester.IsColliding(blocks, entity.CollisionBox, candidate, false))
        {
            return false;
        }

        BlockPos feet = candidate.AsBlockPos;
        Block feetFluid = blocks.GetBlock(feet, BlockLayersAccess.Fluid);
        Block headFluid = blocks.GetBlock(feet.UpCopy(), BlockLayersAccess.Fluid);
        Block aboveHeadFluid = blocks.GetBlock(feet.UpCopy(2), BlockLayersAccess.Fluid);
        if (allowNaturalCleanupWater
            ? IsUnsafeLiquid(feetFluid) || IsUnsafeLiquid(headFluid) || IsUnsafeLiquid(aboveHeadFluid)
                || aboveHeadFluid.IsLiquid()
            : feetFluid.IsLiquid() || headFluid.IsLiquid())
        {
            return false;
        }

        double localY = candidate.Y - feet.dimension * BlockPos.DimensionBoundary;
        Vec3d supportProbe = new(candidate.X, candidate.Y - 0.08d, candidate.Z);
        BlockPos supportPos = supportProbe.AsBlockPos;
        Block support = blocks.GetBlock(supportPos);
        string supportPath = support.Code?.Path ?? string.Empty;
        if (support is BlockDamageOnTouch
            || supportPath.Contains("lava", StringComparison.OrdinalIgnoreCase)
            || supportPath.Contains("fire", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        double localX = candidate.X - supportPos.X;
        double localZ = candidate.Z - supportPos.Z;
        double supportBlockY = supportPos.Y;
        Cuboidf[]? boxes = support.GetCollisionBoxes(blocks, supportPos);
        if (boxes == null) return false;

        foreach (Cuboidf box in boxes)
        {
            double top = supportBlockY + box.MaxY;
            if (localX >= box.MinX - 0.01d && localX <= box.MaxX + 0.01d
                && localZ >= box.MinZ - 0.01d && localZ <= box.MaxZ + 0.01d
                && localY >= top - 0.12d && localY <= top + 0.24d)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnsafeLiquid(Block block)
    {
        return block.IsLiquid() && string.Equals(block.LiquidCode, "lava", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsInNaturalCleanupWater(Vec3d candidate)
    {
        BlockPos feet = candidate.AsBlockPos;
        IBlockAccessor blocks = entity.World.BlockAccessor;
        Block feetFluid = blocks.GetBlock(feet, BlockLayersAccess.Fluid);
        Block headFluid = blocks.GetBlock(feet.UpCopy(), BlockLayersAccess.Fluid);
        return (feetFluid.IsLiquid() && !IsUnsafeLiquid(feetFluid))
            || (headFluid.IsLiquid() && !IsUnsafeLiquid(headFluid));
    }

    private void QueueNextCandidate()
    {
        if (!running || !OwnsLease()) return;
        if (useJourneyPlanner) { QueueOwnedPlan(); return; }
        queuePending = false;
        if (candidateIndex >= candidates.Count || attempts >= maxAttempts || searchBudget.NextDepth <= 0)
        {
            Fail(reportNavigationFailure: true);
            return;
        }

        activeCandidateIsExact = exactCandidateAdded && candidateIndex == 0;
        activeCandidate = candidates[candidateIndex++];
        attempts++;
        openedDoorForCurrentCandidate = false;
        ResetProgressTracking();

        bool callbackRan = false;
        bool submitting = true;
        int requestDepth = searchBudget.NextDepth;
        long requestLease = lease;
        EnumAICreatureType creatureType = targetKind == CompanionNavigationTargetKind.NaturalCleanup
            && IsInNaturalCleanupWater(activeCandidate)
            ? EnumAICreatureType.SeaCreature
            : EnumAICreatureType.LandCreature;
        FeralKinshipCompanionPathPolicy.SetBlockedDoorCells(traverser, blockedDoorCells, adaptiveSearch);
        bool queued = traverser.NavigateTo_Async(
            activeCandidate,
            moveSpeed,
            activeCandidateIsExact && targetKind != CompanionNavigationTargetKind.StorageApproach
                ? positionWaypointRadius : ApproachWaypointRadius,
            () =>
            {
                callbackRan = true;
                if (!OwnsLease(requestLease))
                {
                    StopStaleCallback(requestLease);
                    return;
                }
                if (!IsAtValidatedDestination)
                {
                    arrivalFailures++;
                    CaptureMovementFailure("arrival-rejected");
                    RetryCurrentFailure(retrySameCandidate: adaptiveSearch);
                    return;
                }
                SnowRecoveryIntents.TryRemove(entity.EntityId, out _);
                LogNavigationSummary("arrived");
                running = false;
                CompanionPathDiagnostics.Unregister(traverser, searchDiagnostics);
                UnhookFoundPath();
                onReached?.Invoke();
            },
            () =>
            {
                callbackRan = true;
                if (OwnsLease(requestLease))
                {
                    traverserStuckFailures++;
                    CaptureMovementFailure("traverser-stuck");
                    RetryCurrentFailure(retrySameCandidate: adaptiveSearch);
                }
                else StopStaleCallback(requestLease);
            },
            () =>
            {
                callbackRan = true;
                if (OwnsLease(requestLease))
                {
                    // A synchronous callback means the starting cell was rejected,
                    // before a search was queued. A larger search cannot help that.
                    if (submitting) blockedStartFailures++;
                    else
                    {
                        noRouteFailures++;
                        searchBudget.NoRoute();
                    }
                    RetryCurrentFailure(retrySameCandidate: !submitting && adaptiveSearch
                        && candidateIndex >= candidates.Count && searchBudget.NextDepth > searchBudget.LastDepth);
                }
                else StopStaleCallback(requestLease);
            },
            requestDepth,
            targetKind == CompanionNavigationTargetKind.StorageApproach ? 0 : 1,
            creatureType);
        FeralKinshipCompanionPathPolicy.ClearPreparedPolicy(traverser);
        submitting = false;

        if (queued)
        {
            searchBudget.Commit(requestDepth);
            queuedPathLease = requestLease;
            return;
        }
        if (callbackRan) return;

        // Stop() does not cancel a vanilla asynchronous search. If a previous
        // task still owns one, retain this task slot briefly and queue after
        // that result has been neutralized by the lease-aware found-path hook.
        if (!traverser.Ready)
        {
            candidateIndex--;
            attempts--;
            activeCandidate = null;
            queuePending = true;
            nextQueueAtMs = entity.World.ElapsedMilliseconds + QueueRetryMs;
            return;
        }

        RetryCurrentFailure();
    }

    private void RetryCurrentFailure(bool retrySameCandidate = false)
    {
        if (!running || !OwnsLease()) return;
        if (useJourneyPlanner) { RetryOwnedJourney("movement-interrupted"); return; }
        FeralKinshipCompanionSystem system =
            entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        bool blockingSnowAvailable = system.TryFindSnowRescueTarget(entity, out _, out bool emergencySnow)
            && emergencySnow;
        if (!blockingSnowAvailable
            && !openedDoorForCurrentCandidate
            && attempts >= 2
            && system.DoorUseEnabled
            && TryOpenRouteDoor(system))
        {
            openedDoorForCurrentCandidate = true;
            retrySameCandidate = true;
            attempts = Math.Max(0, attempts - 1);
        }

        if (retrySameCandidate) candidateIndex = Math.Max(0, candidateIndex - 1);

        traverser.Stop();
        queuePending = true;
        nextQueueAtMs = entity.World.ElapsedMilliseconds + retryDelayMs;
    }

    private void ReportNavigationFailure()
    {
        Vec3d goal = activeCandidate?.Clone() ?? desiredTarget.Clone();
        long now = entity.World.ElapsedMilliseconds;
        SnowRecoveryIntent updated = SnowRecoveryIntents.AddOrUpdate(
            entity.EntityId,
            _ => new SnowRecoveryIntent(goal, 1, now, now + SnowRecoveryWindowMs),
            (_, previous) =>
            {
                bool sameProblem = now - previous.LastFailureAtMs <= SnowRecoveryWindowMs
                    && previous.Goal.SquareDistanceTo(goal) <= 4d * 4d;
                return new SnowRecoveryIntent(
                    goal,
                    sameProblem ? previous.Failures + 1 : 1,
                    now,
                    now + SnowRecoveryWindowMs);
            });
        entity.World.RegisterCallback(_elapsed =>
        {
            if (SnowRecoveryIntents.TryGetValue(entity.EntityId, out SnowRecoveryIntent? current)
                && ReferenceEquals(current, updated)
                && current.ExpiresAtMs <= entity.World.ElapsedMilliseconds)
            {
                SnowRecoveryIntents.TryRemove(entity.EntityId, out SnowRecoveryIntent? _removed);
            }
        }, SnowRecoveryWindowMs + 100);
    }

    private bool TryOpenRouteDoor(FeralKinshipCompanionSystem system)
    {
        string ownerUid = FeralKinshipCompanionSystem.GetCompanionOwnerUid(entity);
        IPlayer? owner = string.IsNullOrWhiteSpace(ownerUid) ? null : entity.World.PlayerByUid(ownerUid);
        if (owner == null || activeCandidate == null) return false;

        BlockPos center = entity.Pos.AsBlockPos;
        List<DoorCandidate> nearby = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int dx = -2; dx <= 2; dx++)
        for (int dz = -2; dz <= 2; dz++)
        for (int dy = -1; dy <= 2; dy++)
        {
            BlockPos pos = center.AddCopy(dx, dy, dz);
            if (center.DistanceTo(pos) > 2.8d || !TryDescribeClosedDoor(pos, out DoorCandidate? door)
                || door == null || !seen.Add(DoorKey(door.Position))) continue;
            if (DoorMatchesRouteIntent(door, activeCandidate)) nearby.Add(door);
        }

        foreach (DoorCandidate door in nearby.OrderBy(candidate => center.DistanceTo(candidate.Position)))
        {
            if (!IsClaimTraverseable(door.Block)) continue;
            if (entity.World.Claims.TestAccess(owner, door.Position, EnumBlockAccessFlags.Use)
                != EnumWorldAccessResponse.Granted) continue;

            ModSystemBlockReinforcement? reinforcement =
                entity.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
            if (reinforcement?.IsLockedForInteract(door.Position, owner) == true) continue;

            BlockSelection selection = new()
            {
                Position = door.Position,
                Face = BlockFacing.UP
            };
            door.Block.OnBlockInteractStart(entity.World, owner, selection);

            if (!IsDoorOpen(entity.World, door.Position)) continue;

            // Vanilla correctly broadcasts the interaction sound to everyone
            // except the supplied player because ordinary player interactions
            // also play it client-side. A fox interaction is server-only, so
            // explicitly send the missing spatial sound to its owner.
            system.SendOwnerSpatialSound(owner, door.OpenSound, door.Position, door.OpenPitch);
            TrackDoorTransit(ownerUid, door);
            return true;
        }

        return false;
    }

    private bool TryDescribeClosedDoor(BlockPos scannedPosition, out DoorCandidate? door)
    {
        door = null;
        BlockPos interactionPos = scannedPosition.Copy();
        Block block = entity.World.BlockAccessor.GetBlock(interactionPos);

        BEBehaviorDoor? animated = BlockBehaviorDoor.getDoorAt(entity.World, interactionPos);
        if (animated != null)
        {
            if (animated.Opened || !animated.doorBh.handopenable
                || !string.IsNullOrWhiteSpace(animated.StoryLockedCode)) return false;
            interactionPos = animated.Pos.Copy();
            block = entity.World.BlockAccessor.GetBlock(interactionPos);
            door = new DoorCandidate
            {
                Position = interactionPos,
                Block = block,
                Normal = animated.facingWhenClosed,
                OpenSound = animated.doorBh.OpenSound ?? new AssetLocation("game:sounds/block/door"),
                CloseSound = animated.doorBh.CloseSound ?? new AssetLocation("game:sounds/block/door"),
                OpenPitch = 1.1f,
                ClosePitch = 0.9f
            };
            return true;
        }

        if (block is BlockDoor legacyDoor && legacyDoor.IsUpperHalf())
        {
            interactionPos = interactionPos.DownCopy();
            block = entity.World.BlockAccessor.GetBlock(interactionPos);
        }

        if (block is not BlockBaseDoor baseDoor || baseDoor.IsOpened()) return false;
        AssetLocation triggerSound = AssetLocation.Create(
            block.Attributes?["triggerSound"].AsString("sounds/block/door") ?? "sounds/block/door",
            block.Code.Domain);
        door = new DoorCandidate
        {
            Position = interactionPos,
            Block = block,
            Normal = baseDoor.GetDirection(),
            OpenSound = triggerSound,
            CloseSound = triggerSound,
            OpenPitch = 1f,
            ClosePitch = 1f
        };
        return true;
    }

    private bool DoorMatchesRouteIntent(DoorCandidate door, Vec3d goal)
    {
        Vec3d center = new(
            door.Position.X + 0.5,
            door.Position.InternalY + 0.5,
            door.Position.Z + 0.5);
        double routeX = goal.X - entity.Pos.X;
        double routeZ = goal.Z - entity.Pos.Z;
        double routeLengthSquared = routeX * routeX + routeZ * routeZ;
        if (routeLengthSquared < 0.5d * 0.5d) return false;

        double doorX = center.X - entity.Pos.X;
        double doorZ = center.Z - entity.Pos.Z;
        double projection = (doorX * routeX + doorZ * routeZ) / routeLengthSquared;
        if (projection <= 0.03d || projection >= 0.98d) return false;

        double routeY = entity.Pos.Y + (goal.Y - entity.Pos.Y) * projection;
        if (routeY < door.Position.InternalY - 0.55d
            || routeY > door.Position.InternalY + 2.45d) return false;

        double nearestX = entity.Pos.X + routeX * projection;
        double nearestZ = entity.Pos.Z + routeZ * projection;
        double lateralX = center.X - nearestX;
        double lateralZ = center.Z - nearestZ;
        if (lateralX * lateralX + lateralZ * lateralZ > 1.2d * 1.2d) return false;

        double normalX = door.Normal.Normali.X;
        double normalZ = door.Normal.Normali.Z;
        double startSide = (entity.Pos.X - center.X) * normalX + (entity.Pos.Z - center.Z) * normalZ;
        double goalSide = (goal.X - center.X) * normalX + (goal.Z - center.Z) * normalZ;
        return Math.Abs(startSide) >= 0.12d && startSide * goalSide < -0.04d;
    }

    private void TrackDoorTransit(string ownerUid, DoorCandidate door)
    {
        Vec3d center = new(door.Position.X + 0.5, door.Position.InternalY + 0.5, door.Position.Z + 0.5);
        double startSide = (entity.Pos.X - center.X) * door.Normal.Normali.X
            + (entity.Pos.Z - center.Z) * door.Normal.Normali.Z;
        long now = entity.World.ElapsedMilliseconds;
        PendingDoorTransit transit = new()
        {
            Token = Interlocked.Increment(ref nextDoorTransitToken),
            EntityId = entity.EntityId,
            OwnerUid = ownerUid,
            Api = entity.Api,
            System = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>(),
            World = entity.World,
            Position = door.Position.Copy(),
            Normal = door.Normal,
            StartSide = startSide,
            OpenedAtMs = now,
            CloseSound = door.CloseSound,
            ClosePitch = door.ClosePitch
        };
        var key = (transit.World, DoorKey(transit.Position));
        PendingDoorTransits[key] = transit;
        transit.World.RegisterCallback(_ => CheckDoorTransit(key, transit), DoorTransitPollMs);
    }

    private static void CheckDoorTransit((IWorldAccessor World, string Door) key, PendingDoorTransit expected)
    {
        if (!PendingDoorTransits.TryGetValue(key, out PendingDoorTransit? transit)
            || transit.Token != expected.Token) return;

        long now = transit.World.ElapsedMilliseconds;
        if (transit.World is not IServerWorldAccessor server || !server.IsFullyLoadedChunk(transit.Position))
        {
            LogDoorTransit(transit, "unloaded");
            PendingDoorTransits.TryRemove(key, out _);
            return;
        }
        if (!IsDoorOpen(transit.World, transit.Position))
        {
            PendingDoorTransits.TryRemove(key, out _);
            return;
        }

        Entity? traveler = transit.World.GetEntityById(transit.EntityId);
        bool crossedAndClear = false;
        if (traveler?.Alive == true && traveler.Pos.Dimension == transit.Position.dimension)
        {
            Vec3d center = new(
                transit.Position.X + 0.5,
                transit.Position.InternalY + 0.5,
                transit.Position.Z + 0.5);
            double currentSide = (traveler.Pos.X - center.X) * transit.Normal.Normali.X
                + (traveler.Pos.Z - center.Z) * transit.Normal.Normali.Z;
            crossedAndClear = transit.StartSide * currentSide < -0.08d
                && Math.Abs(currentSide) >= 0.85d;
        }

        bool occupied = IsDoorwayOccupied(transit);
        int delay = CompanionDoorClosePolicy.NextPollDelay(now - transit.OpenedAtMs, crossedAndClear, occupied);
        if (delay > 0)
        {
            if (occupied && !transit.ReportedOccupied && now - transit.OpenedAtMs >= 10000)
            {
                transit.ReportedOccupied = true;
                LogDoorTransit(transit, "waiting-for-clear-doorway");
            }
            transit.World.RegisterCallback(_ => CheckDoorTransit(key, transit), delay);
            return;
        }

        IPlayer? owner = transit.World.PlayerByUid(transit.OwnerUid);
        Block block = transit.World.BlockAccessor.GetBlock(transit.Position);
        if (owner != null
            && IsClaimTraverseable(block)
            && transit.World.Claims.TestAccess(owner, transit.Position, EnumBlockAccessFlags.Use)
                == EnumWorldAccessResponse.Granted)
        {
            ModSystemBlockReinforcement? reinforcement =
                transit.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
            if (reinforcement?.IsLockedForInteract(transit.Position, owner) != true)
            {
                block.OnBlockInteractStart(transit.World, owner, new BlockSelection
                {
                    Position = transit.Position,
                    Face = BlockFacing.UP
                });
                if (!IsDoorOpen(transit.World, transit.Position))
                {
                    transit.System.SendOwnerSpatialSound(
                        owner, transit.CloseSound, transit.Position, transit.ClosePitch);
                }
            }
        }

        PendingDoorTransits.TryRemove(key, out _);
        LogDoorTransit(transit, IsDoorOpen(transit.World, transit.Position) ? "close-not-permitted-or-failed" : "closed");
    }

    private static void LogDoorTransit(PendingDoorTransit transit, string outcome)
    {
        if (transit.System.DiagnosticLoggingEnabled)
            transit.Api.Logger.Debug("[FeralKinshipCompanions] Door transit fox {0}: door={1} outcome={2} elapsedMs={3}",
                transit.EntityId, transit.Position, outcome, transit.World.ElapsedMilliseconds - transit.OpenedAtMs);
    }

    internal static void ClearDoorTransits(IWorldAccessor world)
    {
        foreach (var key in PendingDoorTransits.Keys)
            if (ReferenceEquals(key.World, world)) PendingDoorTransits.TryRemove(key, out _);
    }

    private static bool IsDoorwayOccupied(PendingDoorTransit transit)
    {
        Vec3d center = new(
            transit.Position.X + 0.5,
            transit.Position.InternalY + 1d,
            transit.Position.Z + 0.5);
        Cuboidd doorway = new(
            transit.Position.X - 0.15,
            transit.Position.InternalY,
            transit.Position.Z - 0.15,
            transit.Position.X + 1.15,
            transit.Position.InternalY + 2.2,
            transit.Position.Z + 1.15);
        return transit.World.GetEntitiesAround(center, 1.6f, 2.5f,
                candidate => candidate is EntityAgent && candidate.Alive && candidate.Pos.Dimension == transit.Position.dimension)
            .Any(candidate => doorway.Intersects(candidate.CollisionBox.ToDouble().Translate(
                candidate.Pos.X, candidate.Pos.Y, candidate.Pos.Z)));
    }

    private static bool IsDoorOpen(IWorldAccessor world, BlockPos position)
    {
        Block block = world.BlockAccessor.GetBlock(position);
        if (block is BlockBaseDoor baseDoor) return baseDoor.IsOpened();
        return BlockBehaviorDoor.getDoorAt(world, position)?.Opened == true;
    }

    private static string DoorKey(BlockPos position) =>
        $"{position.dimension}:{position.X}:{position.Y}:{position.Z}";

    private static bool IsClaimTraverseable(Block block)
    {
        return block is IClaimTraverseable
            || block.BlockBehaviors?.Any(behavior => behavior is IClaimTraverseable) == true;
    }

    private void HookFoundPath()
    {
        if (foundPathHooked) return;
        traverser.OnFoundPath += OnFoundPath;
        foundPathHooked = true;
    }

    private void UnhookFoundPath()
    {
        if (!foundPathHooked) return;
        traverser.OnFoundPath -= OnFoundPath;
        foundPathHooked = false;
    }

    private void OnFoundPath()
    {
        if (queuedPathLease != 0 && OwnsLease(queuedPathLease))
        {
            queuedPathLease = 0;
            if (!entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>().DoorUseEnabled
                && RejectClosedDoorRoute())
            {
                return;
            }
            searchBudget.RouteFound();
            // A queued asynchronous search can consume the whole original
            // progress window before vanilla activates the returned route.
            // Start measuring only now, when the traverser can actually move.
            ResetProgressTracking();
            return;
        }

        // The vanilla traverser has just activated a route produced by an
        // older async request. Stop it before its first movement tick. A newer
        // request on this helper remains queued and will start next tick.
        traverser.Stop();
        queuedPathLease = 0;
        if (running && OwnsLease())
        {
            queuePending = true;
            nextQueueAtMs = entity.World.ElapsedMilliseconds + QueueRetryMs;
            return;
        }

        running = false;
        queuePending = false;
        UnhookFoundPath();
    }

    private void ResetProgressTracking()
    {
        // OnFoundPath resets this again against the newly activated route.
        if (TryReadRouteProgress(out int waypoint, out double distance))
            routeProgress.Reset(waypoint, distance);
        else
            routeProgress.Reset(-1, double.PositiveInfinity);
        nextProgressCheckAtMs = entity.World.ElapsedMilliseconds + ProgressCheckMs;
    }

    private bool TryReadRouteProgress(out int waypoint, out double squaredDistance)
    {
        waypoint = -1;
        squaredDistance = double.PositiveInfinity;
        if (WaypointIndexField?.GetValue(traverser) is not int index
            || WaypointsField?.GetValue(traverser) is not List<Vec3d> route
            || index < 0 || index >= route.Count) return false;
        waypoint = index;
        squaredDistance = entity.Pos.SquareDistanceTo(route[index]);
        return double.IsFinite(squaredDistance);
    }

    private void CaptureMovementFailure(string reason)
    {
        if (searchDiagnostics == null) return;
        int index = WaypointIndexField?.GetValue(traverser) is int value ? value : -1;
        List<Vec3d>? route = WaypointsField?.GetValue(traverser) as List<Vec3d>;
        string Point(int at) => route != null && at >= 0 && at < route.Count ? route[at].ToString() : "none";
        lastMovementFailure = $"reason={reason} fox=({entity.Pos.XYZ}) candidate=({activeCandidate}) "
            + $"waypoint={index}/{route?.Count ?? 0} previous=({Point(index - 1)}) "
            + $"next=({Point(index)}) end=({Point((route?.Count ?? 0) - 1)})";
    }

    private void LogNavigationSummary(string outcome)
    {
        FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (!system.DiagnosticLoggingEnabled) return;
        system.LogCompanionNavigationDiagnostic(entity,
            $"outcome={outcome} task={taskName} kind={targetKind} adaptive={adaptiveSearch} goal={desiredTarget} "
            + $"elapsedMs={entity.World.ElapsedMilliseconds - startedAtMs} "
            + (useJourneyPlanner ? $"requests={ownedPlans} workAllowanceUsed={ownedWork}/480000 "
                : $"requests={searchBudget.Requests} depthAllowanceUsed={searchBudget.CommittedDepth}/{searchBudget.Allowance} ")
            + DescribeOwnedJourney()
            + $"lastDepth={searchBudget.LastDepth} noRoute={noRouteFailures} startBlocked={blockedStartFailures} "
            + $"traverserStuck={traverserStuckFailures} routeStalled={progressFailures} arrivalRejected={arrivalFailures} "
            + (useJourneyPlanner ? "" : searchDiagnostics?.Describe() ?? "searchDiagnostics=off")
            + $" lastMovementFailure=[{lastMovementFailure}]");
    }

    private bool RejectClosedDoorRoute()
    {
        if (WaypointsField?.GetValue(traverser) is not List<Vec3d> waypoints
            || waypoints.Count == 0) return false;

        HashSet<CompanionBlockedPathCell> routeDoors = FindClosedDoorCellsOnRoute(waypoints);
        if (routeDoors.Count == 0) return false;

        bool learnedNewDoor = false;
        foreach (CompanionBlockedPathCell cell in routeDoors)
        {
            learnedNewDoor |= blockedDoorCells.Add(cell);
        }

        traverser.Stop();
        if (learnedNewDoor && doorRouteReplans++ < 6)
        {
            // The pathfinder has now learned the exact closed-door cells that
            // invalidated this route. Retry the same candidate without
            // consuming its ordinary approach-attempt budget. Adaptive journeys
            // still charge the search against their total node allowance.
            candidateIndex = Math.Max(0, candidateIndex - 1);
            attempts = Math.Max(0, attempts - 1);
            queuePending = true;
            nextQueueAtMs = entity.World.ElapsedMilliseconds + retryDelayMs;
            return true;
        }

        // Never accept a known closed-door route while door use is disabled.
        // If the exclusion cannot produce a different route, advance through
        // the normal bounded candidates and eventually abandon cleanly.
        RetryCurrentFailure();
        return true;
    }

    private HashSet<CompanionBlockedPathCell> FindClosedDoorCellsOnRoute(List<Vec3d> waypoints)
    {
        HashSet<CompanionBlockedPathCell> result = new();
        Vec3d from = entity.Pos.XYZ;
        foreach (Vec3d to in waypoints)
        {
            double segmentLength = Math.Sqrt(from.SquareDistanceTo(to));
            int samples = Math.Max(1, (int)Math.Ceiling(segmentLength / 0.15d));
            int minX = (int)Math.Floor(Math.Min(from.X, to.X)) - 1;
            int maxX = (int)Math.Floor(Math.Max(from.X, to.X)) + 1;
            int minY = (int)Math.Floor(Math.Min(from.Y, to.Y)) - 1;
            int maxY = (int)Math.Floor(Math.Max(from.Y, to.Y) + entity.CollisionBox.YSize) + 1;
            int minZ = (int)Math.Floor(Math.Min(from.Z, to.Z)) - 1;
            int maxZ = (int)Math.Floor(Math.Max(from.Z, to.Z)) + 1;

            for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            for (int z = minZ; z <= maxZ; z++)
            {
                BlockPos position = new(x, y, z, entity.Pos.Dimension);
                if (!TryGetClosedDoorCollision(position, out Cuboidf[]? collisionBoxes)
                    || collisionBoxes == null) continue;

                bool intersectsRoute = false;
                for (int sample = 0; sample <= samples && !intersectsRoute; sample++)
                {
                    double t = sample / (double)samples;
                    double px = from.X + (to.X - from.X) * t;
                    double py = from.Y + (to.Y - from.Y) * t;
                    double pz = from.Z + (to.Z - from.Z) * t;
                    Cuboidd movingBox = entity.CollisionBox.ToDouble().Translate(px, py, pz);
                    intersectsRoute = collisionBoxes.Any(box => movingBox.Intersects(
                        box,
                        position.X,
                        position.InternalY,
                        position.Z));
                }

                if (intersectsRoute)
                {
                    result.Add(new CompanionBlockedPathCell(
                        position.dimension,
                        position.X,
                        position.Y,
                        position.Z));
                }
            }

            from = to;
        }

        return result;
    }

    private bool TryGetClosedDoorCollision(BlockPos position, out Cuboidf[]? collisionBoxes)
    {
        collisionBoxes = null;
        Block block = entity.World.BlockAccessor.GetBlock(position);
        bool closedDoor = block is BlockBaseDoor baseDoor && !baseDoor.IsOpened();
        if (!closedDoor
            && (block is BlockMultiblock || block.GetBehavior<BlockBehaviorDoor>() != null))
        {
            closedDoor = BlockBehaviorDoor.getDoorAt(entity.World, position)?.Opened == false;
        }
        if (!closedDoor) return false;

        collisionBoxes = block.GetCollisionBoxes(entity.World.BlockAccessor, position);
        return collisionBoxes?.Length > 0;
    }

    private bool OwnsLease()
    {
        return OwnsLease(lease);
    }

    private bool OwnsLease(long expectedLease)
    {
        return expectedLease != 0
            && ActiveLeases.TryGetValue(entity.EntityId, out long current)
            && current == expectedLease;
    }

    private void StopStaleCallback(long staleLease)
    {
        traverser.Stop();
        if (OwnsLease() && lease != staleLease)
        {
            queuePending = true;
            nextQueueAtMs = entity.World.ElapsedMilliseconds + QueueRetryMs;
            return;
        }

        running = false;
        queuePending = false;
        UnhookFoundPath();
    }

    private void Fail(bool reportNavigationFailure = false)
    {
        CancelOwnedSearch();
        if (reportNavigationFailure) ReportNavigationFailure();
        LogNavigationSummary("failed");
        running = false;
        queuePending = false;
        CompanionPathDiagnostics.Unregister(traverser, searchDiagnostics);
        traverser.Stop();
        if (traverser.Ready)
        {
            queuedPathLease = 0;
            UnhookFoundPath();
        }
        else
        {
            HookFoundPath();
        }
        onFailed?.Invoke();
    }

    private static bool IsFinite(Vec3d value)
    {
        return double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
    }
}
