#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Essentials;
using Vintagestory.GameContent;
using Vintagestory.Systems;

namespace FeralKinshipCompanions;

internal sealed partial class CompanionNavigation
{
    private static readonly FieldInfo? AsyncSearchField = typeof(WaypointsTraverser).GetField(
        "asyncSearchObject", BindingFlags.Instance | BindingFlags.NonPublic);
    private bool useJourneyPlanner;
    private CompanionJourneySearch? ownedSearch;
    private CompanionJourneyTerrain? ownedTerrain;
    private readonly Dictionary<JourneyCell, Vec3d> ownedGoals = new();
    private readonly List<JourneyEdge> ownedRoute = new();
    private readonly List<string> ownedResults = new();
    private int ownedCursor;
    private int ownedPlans;
    private int ownedWork;
    private int ownedExpanded;
    private int ownedSlices;
    private int ownedDoorsOpened;
    private double ownedSearchMs;
    private long ownedDeadline;
    private string ownedPhase = "idle";
    private string ownedReason = "none";
    private long ownedWalkToken;
    private long ownedCacheId;
    private int ownedCacheHints, ownedCacheUses, ownedCacheRejects;
    private bool ownedCacheRejectionRecorded;
    private bool ownedArrivalRefined;
    private int ownedArrivalRefinements;
    private long ownedSettleUntil;
    private readonly List<string> ownedRecoveries = new();

    private void ResetOwnedJourney()
    {
        ownedSearch = null;
        ownedTerrain = null;
        ownedRoute.Clear(); ownedGoals.Clear(); ownedResults.Clear();
        ownedCursor = ownedPlans = ownedWork = ownedExpanded = ownedSlices = ownedDoorsOpened = 0;
        ownedSearchMs = 0;
        ownedDeadline = entity.World.ElapsedMilliseconds + 180000;
        ownedPhase = "queued";
        ownedReason = "none";
        ownedWalkToken++;
        ownedCacheId = 0;
        ownedCacheHints = ownedCacheUses = ownedCacheRejects = 0;
        ownedCacheRejectionRecorded = false;
        ownedArrivalRefined = false;
        ownedArrivalRefinements = 0;
        ownedSettleUntil = 0;
        ownedRecoveries.Clear();
    }

    private void CollectOwnedSearch()
    {
        if (ownedSearch == null) return;
        ownedWork += ownedSearch.Work;
        ownedExpanded += ownedSearch.Expanded;
        ownedSlices += ownedSearch.Slices;
        ownedSearchMs += ownedSearch.ProcessingMs;
        ownedResults.Add($"{ownedSearch.State}:{ownedSearch.Expanded}nodes:{ownedSearch.Work}work:{ownedSearch.Slices}slices:{ownedSearch.ProcessingMs:0.00}ms:{ownedSearch.Error}:cacheUsed={ownedSearch.CacheUsed}:cacheRejected={ownedSearch.CacheRejected}");
    }

    private void CancelOwnedSearch()
    {
        if (ownedSearch == null) return;
        ObserveCacheRejection();
        if (ownedSearch.Pending) ownedSearch.Finish(JourneySearchState.Cancelled);
        CollectOwnedSearch();
        ownedSearch = null;
    }

    private void ObserveCacheRejection()
    {
        if (ownedSearch?.CacheRejected != true || ownedCacheRejectionRecorded) return;
        ownedCacheRejectionRecorded = true;
        ownedCacheRejects++;
        entity.Api.ModLoader.GetModSystem<CompanionJourneyService>().Routes.Invalidate(ownedCacheId);
        ownedCacheId = 0;
    }

    private string DescribeOwnedJourney() => !useJourneyPlanner ? "planner=vanilla " :
        $"planner=resumable phase={ownedPhase} plans={ownedPlans} work={ownedWork} expandedOwned={ownedExpanded} "
        + $"slices={ownedSlices} processingMs={ownedSearchMs:0.00} doorsOpened={ownedDoorsOpened} "
        + $"planningLane={(targetKind == CompanionNavigationTargetKind.NaturalCleanup ? "background" : "cargo")} "
        + $"cacheHints={ownedCacheHints} cacheUses={ownedCacheUses} cacheRejected={ownedCacheRejects} "
        + $"arrivalRefinements={ownedArrivalRefinements} recoveries=[{string.Join(";", ownedRecoveries)}] "
        + $"journeyReason={ownedReason} planResults=[{string.Join(";", ownedResults)}] ";

    private void QueueOwnedPlan()
    {
        queuePending = false;
        if (ownedSearch != null) return;
        long now = entity.World.ElapsedMilliseconds;
        if (now >= ownedDeadline || ownedPlans >= 3 || ownedWork >= 480000)
        {
            ownedReason = "journey-budget";
            ownedPhase = "failed";
            Fail(reportNavigationFailure: true);
            return;
        }
        if (AsyncSearchField == null)
        {
            ownedReason = "engine-contract-unavailable";
            Fail();
            return;
        }
        // An old vanilla request cannot be cancelled. Neutralize its completion
        // through the existing lease hook before supplying an owned route.
        if (AsyncSearchField.GetValue(traverser) != null)
        {
            queuePending = true;
            nextQueueAtMs = now + QueueRetryMs;
            return;
        }
        traverser.Stop();
        ownedWalkToken++;
        queuedPathLease = 0;
        UnhookFoundPath();
        ownedGoals.Clear();
        double allowed = Math.Max(0.35, desiredArrivalRadius) + 0.18;
        foreach (Vec3d candidate in candidates)
        {
            if (targetKind != CompanionNavigationTargetKind.StorageApproach
                && candidate.SquareDistanceTo(desiredTarget) > allowed * allowed) continue;
            if (!IsSafeStandingPosition(candidate, targetKind == CompanionNavigationTargetKind.NaturalCleanup)) continue;
            JourneyCell cell = CompanionJourneyTerrain.Cell(candidate.AsBlockPos);
            if (!ownedGoals.TryGetValue(cell, out Vec3d? existing)
                || candidate.SquareDistanceTo(desiredTarget) < existing.SquareDistanceTo(desiredTarget))
                ownedGoals[cell] = candidate.Clone();
        }
        if (ownedGoals.Count == 0)
        {
            ownedReason = "no-usable-arrival";
            Fail(reportNavigationFailure: true);
            return;
        }
        ICoreServerAPI api = (ICoreServerAPI)entity.Api;
        CompanionJourneyService service = api.ModLoader.GetModSystem<CompanionJourneyService>();
        EnumAICreatureType creature = targetKind == CompanionNavigationTargetKind.NaturalCleanup
            && IsInNaturalCleanupWater(desiredTarget) ? EnumAICreatureType.SeaCreature : EnumAICreatureType.LandCreature;
        ownedTerrain = new CompanionJourneyTerrain(api, service.Probe, entity, moveSpeed, creature, DescribeJourneyDoor);
        JourneyCell startCell = CompanionJourneyTerrain.Cell(entity.Pos.AsBlockPos);
        CompanionJourneyCache.Hint? hint = service.Routes.Find(
            ownedTerrain.CacheProfile(FeralKinshipCompanionSystem.GetCompanionOwnerUid(entity)), startCell, ownedGoals.Keys, now);
        ownedCacheId = hint?.Id ?? 0;
        ownedCacheRejectionRecorded = false;
        ownedSearch = new CompanionJourneySearch(ownedTerrain, CompanionJourneyTerrain.Cell(entity.Pos.AsBlockPos),
            ownedGoals.Keys, workLimit: Math.Min(240000, 480000 - ownedWork), cachedRoute: hint?.Route);
        if (!service.Submit(ownedSearch, Math.Min(ownedDeadline, now + 60000),
            cargo: targetKind != CompanionNavigationTargetKind.NaturalCleanup))
        {
            ownedSearch.Finish(JourneySearchState.Cancelled);
            ownedSearch = null;
            queuePending = true;
            nextQueueAtMs = now + 250;
            ownedPhase = "waiting-for-pack-budget";
            return;
        }
        ownedPlans++;
        if (hint != null) ownedCacheHints++;
        ownedPhase = "planning";
    }

    // True means this tick belongs to planning or terminal handling, not walking.
    private bool TickOwnedJourney(long now)
    {
        if (now >= ownedDeadline) { ownedReason = "journey-timeout"; Fail(reportNavigationFailure: true); return true; }
        if (ownedSettleUntil != 0)
        {
            if (IsAtValidatedDestination) CompleteOwnedArrival();
            else if (now >= ownedSettleUntil) RejectOwnedArrival();
            return true;
        }
        if (ownedSearch == null) return false;
        ObserveCacheRejection();
        if (ownedSearch.Pending) return true;
        CompanionJourneySearch result = ownedSearch;
        CollectOwnedSearch();
        ownedSearch = null;
        CompanionJourneyCache cache = entity.Api.ModLoader.GetModSystem<CompanionJourneyService>().Routes;
        if (result.State == JourneySearchState.Found)
        {
            // A push or a fall while planning invalidates the old starting point.
            Vec3d start = ownedTerrain!.Waypoint(result.Route.Count == 0 ? result.Reached : result.Route[0].From);
            if (entity.Pos.SquareDistanceTo(start) > 1.25 * 1.25)
            {
                RetryOwnedJourney("moved-during-planning");
                return true;
            }
            activeCandidate = ownedGoals[result.Reached];
            if (result.CacheUsed)
            {
                ownedCacheUses++;
                cache.ConfirmUse(ownedCacheId, now);
            }
            else
            {
                ownedCacheId = cache.Store(ownedTerrain.CacheProfile(FeralKinshipCompanionSystem.GetCompanionOwnerUid(entity)), result.Route, now);
            }
            ownedRoute.Clear(); ownedRoute.AddRange(result.Route);
            ownedCursor = 0;
            ownedArrivalRefined = false;
            BeginOwnedSegment();
            return true;
        }
        ownedReason = result.State.ToString();
        if (result.State is JourneySearchState.Invalidated or JourneySearchState.Unloaded)
            RetryOwnedJourney(ownedReason, 1000);
        else { ownedPhase = "failed"; Fail(reportNavigationFailure: true); }
        return true;
    }

    private void RetryOwnedJourney(string reason, int delay = 350)
    {
        if (!running || !OwnsLease()) return;
        CancelOwnedSearch();
        if (ownedCacheId != 0)
        {
            entity.Api.ModLoader.GetModSystem<CompanionJourneyService>().Routes.Invalidate(ownedCacheId);
            ownedCacheId = 0;
        }
        ownedSettleUntil = 0;
        ownedRecoveries.Add(reason);
        ownedReason = reason;
        ownedPhase = "recovering";
        ownedWalkToken++;
        traverser.Stop();
        ownedRoute.Clear();
        queuePending = true;
        nextQueueAtMs = entity.World.ElapsedMilliseconds + delay;
    }

    private void BeginOwnedSegment()
    {
        if (!running || !OwnsLease() || ownedTerrain == null || activeCandidate == null) return;
        List<Vec3d> waypoints = new();
        if (ownedCursor < ownedRoute.Count && ownedRoute[ownedCursor].Door is JourneyDoor door)
        {
            JourneyEdge crossing = ownedRoute[ownedCursor];
            Vec3d beforeDoor = ownedTerrain.Waypoint(crossing.From);
            if (entity.Pos.SquareDistanceTo(beforeDoor) > 0.55 * 0.55)
                waypoints.Add(beforeDoor);
            else
            {
                if (!OpenJourneyDoor(door)
                    || !ownedTerrain.ClearDoorPassage(crossing.From, crossing.To, door, ignoreClosedDoor: false))
                {
                    RetryOwnedJourney("door-no-longer-passable");
                    return;
                }
                waypoints.Add(ownedTerrain.Waypoint(crossing.To));
                ownedCursor++;
            }
        }
        // Stop the segment before the next closed-door action, even if a later
        // waypoint is nearby. Vanilla movement may only look ahead inside it.
        while (ownedCursor < ownedRoute.Count && ownedRoute[ownedCursor].Door == null)
        {
            waypoints.Add(ownedTerrain.Waypoint(ownedRoute[ownedCursor].To));
            ownedCursor++;
        }
        bool finalSegment = ownedCursor == ownedRoute.Count;
        if (finalSegment) waypoints.Add(activeCandidate.Clone());
        if (waypoints.Count == 0) { RetryOwnedJourney("empty-segment"); return; }
        ownedPhase = "walking";
        long routeLease = lease;
        long walkToken = ++ownedWalkToken;
        traverser.FollowRoute(waypoints, moveSpeed, ApproachWaypointRadius,
            () =>
            {
                if (!running || !OwnsLease(routeLease) || walkToken != ownedWalkToken) return;
                if (!finalSegment) { BeginOwnedSegment(); return; }
                if (!IsAtValidatedDestination)
                {
                    if (!RefineOwnedArrival()) RejectOwnedArrival();
                    return;
                }
                CompleteOwnedArrival();
            },
            () =>
            {
                if (!running || !OwnsLease(routeLease) || walkToken != ownedWalkToken) return;
                traverserStuckFailures++;
                CaptureMovementFailure("traverser-stuck");
                RetryOwnedJourney("traverser-stuck");
            });
        ResetProgressTracking();
    }

    private void CompleteOwnedArrival()
    {
        ownedSettleUntil = 0;
        ownedWalkToken++;
        ownedPhase = "arrived";
        SnowRecoveryIntents.TryRemove(entity.EntityId, out _);
        LogNavigationSummary("arrived");
        running = false;
        CompanionPathDiagnostics.Unregister(traverser, searchDiagnostics);
        onReached?.Invoke();
    }

    private void RejectOwnedArrival()
    {
        arrivalFailures++;
        CaptureMovementFailure("arrival-rejected");
        RetryOwnedJourney("arrival-rejected");
    }

    private bool RefineOwnedArrival()
    {
        if (ownedArrivalRefined || targetKind != CompanionNavigationTargetKind.StorageApproach || activeCandidate == null)
            return false;
        double dx = entity.Pos.X - activeCandidate.X, dz = entity.Pos.Z - activeCandidate.Z;
        double dy = entity.Pos.Y - activeCandidate.Y;
        if (dx * dx + dz * dz > 1 || dy < -0.5 || dy > 1.1 || !IsSafeStandingPosition(activeCandidate, false))
            return false;
        // The normal waypoint radius can stop with the body still overlapping
        // the 0.75-high pack box. Finish moving clear, then allow gravity to settle.
        // Arrival/deposit still uses the original collision and height checks.
        ownedArrivalRefined = true;
        ownedArrivalRefinements++;
        ownedPhase = "final-approach";
        long routeLease = lease;
        long walkToken = ++ownedWalkToken;
        traverser.FollowRoute(new List<Vec3d> { activeCandidate.Clone() }, moveSpeed, 0.06f,
            () =>
            {
                if (!running || !OwnsLease(routeLease) || walkToken != ownedWalkToken) return;
                ownedWalkToken++;
                traverser.Stop();
                if (IsAtValidatedDestination) CompleteOwnedArrival();
                else
                {
                    ownedPhase = "settling";
                    ownedSettleUntil = entity.World.ElapsedMilliseconds + 1200;
                }
            },
            () =>
            {
                if (!running || !OwnsLease(routeLease) || walkToken != ownedWalkToken) return;
                traverserStuckFailures++;
                CaptureMovementFailure("final-approach-stuck");
                RetryOwnedJourney("final-approach-stuck");
            });
        ResetProgressTracking();
        return true;
    }

    private JourneyDoor? DescribeJourneyDoor(BlockPos pos)
    {
        Block block = entity.World.BlockAccessor.GetBlock(pos);
        if (block is not BlockBaseDoor && block is not BlockMultiblock && block.GetBehavior<BlockBehaviorDoor>() == null)
            return null;
        if (!TryGetClosedDoorCollision(pos, out _)) return null;
        if (!TryDescribeClosedDoor(pos, out DoorCandidate? door) || door == null)
            return new JourneyDoor(CompanionJourneyTerrain.Cell(pos), 0, 0, false);
        FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        IPlayer? owner = entity.World.PlayerByUid(FeralKinshipCompanionSystem.GetCompanionOwnerUid(entity));
        bool allowed = system.DoorUseEnabled && owner != null && CanUseJourneyDoor(door, owner);
        return new JourneyDoor(CompanionJourneyTerrain.Cell(door.Position), door.Normal.Normali.X, door.Normal.Normali.Z, allowed);
    }

    private bool CanUseJourneyDoor(DoorCandidate door, IPlayer owner) =>
        IsClaimTraverseable(door.Block)
        && entity.World.Claims.TestAccess(owner, door.Position, EnumBlockAccessFlags.Use) == EnumWorldAccessResponse.Granted
        && entity.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>()?.IsLockedForInteract(door.Position, owner) != true;

    private bool OpenJourneyDoor(JourneyDoor planned)
    {
        BlockPos pos = ownedTerrain!.Position(planned.Position);
        if (IsDoorOpen(entity.World, pos)) return true;
        FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        string uid = FeralKinshipCompanionSystem.GetCompanionOwnerUid(entity);
        IPlayer? owner = entity.World.PlayerByUid(uid);
        if (!system.DoorUseEnabled || owner == null || entity.Pos.SquareDistanceTo(pos.X + 0.5, pos.InternalY, pos.Z + 0.5) > 2.8 * 2.8
            || !TryDescribeClosedDoor(pos, out DoorCandidate? door) || door == null || !CanUseJourneyDoor(door, owner)) return false;
        door.Block.OnBlockInteractStart(entity.World, owner, new BlockSelection { Position = door.Position, Face = BlockFacing.UP });
        if (!IsDoorOpen(entity.World, door.Position)) return false;
        system.SendOwnerSpatialSound(owner, door.OpenSound, door.Position, door.OpenPitch);
        TrackDoorTransit(uid, door);
        ownedDoorsOpened++;
        return true;
    }
}
