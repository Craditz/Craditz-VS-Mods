#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    internal const string LoggingDisplayKey = "feralKinshipLoggingDisplay";
    internal const string LoggingDisplayActiveKey = "active";
    internal const string LoggingDisplayXKey = "x";
    internal const string LoggingDisplayYKey = "y";
    internal const string LoggingDisplayZKey = "z";
    internal const string LoggingDisplayDimensionKey = "dimension";
    internal const string LoggingDisplayProgressKey = "progress";
    internal const string LoggingDisplayTotalKey = "total";

    private const long WorkCartLoggingRescanIntervalMs = 30000;
    private const long WorkCartLoggingWorkerLeaseMs = 30000;
    private const long WorkCartLoggingIdlePlanLifetimeMs = 60000;
    private const long WorkCartLoggingBlockedLifetimeMs = 30000;
    private const long WorkCartLoggingDebrisRefreshMs = 1000;
    private const int WorkCartLoggingScanColumnsPerTick = 6;
    private const int WorkCartLoggingGraphNodesPerTick = 64;
    private const int WorkCartLoggingPartChecksPerTick = 96;
    private const int WorkCartLoggingBlocksBrokenPerTick = 12;
    private const int WorkCartLoggingClaimCleanupPerTick = 128;
    private const int WorkCartLoggingCandidateLimit = 8;
    private const int MaximumWorkCartTreeParts = 2499;
    // Tree parts may be inside the cart's work area while their drops land
    // just outside its cylinder. Give cleanup a little room without widening
    // the actual logging territory or tree search.
    private readonly Dictionary<long, WorkCartLoggingJobState> workCartLoggingJobs = new();
    private readonly Dictionary<string, WorkCartLoggingQueueState> workCartLoggingQueues =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkCartLoggingPlan> activeWorkCartTreesByRoot =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkCartLoggingPlan> activeWorkCartTreeParts =
        new(StringComparer.Ordinal);
    private readonly Queue<WorkCartLoggingPlan> terminalWorkCartLoggingPlans = new();
    private readonly HashSet<long> workCartLoggingWaitingForDebris = new();
    private int workCartLoggingRoundRobinOffset;
    private bool workCartLoggingStartupRecoveryPending;

    internal void ArmWorkCartLoggingStartupRecovery()
    {
        workCartLoggingStartupRecoveryPending = true;
    }

    /// <summary>
    /// Logging owns the debris it creates. This is intentionally separate from
    /// the player's general Ground Cleanup toggle: a logging cart should not
    /// fell trees and then leave the fox unable to collect their leaves.
    /// </summary>
    internal bool IsWorkCartLoggingCleanupEnabled(Entity fox)
    {
        return TryGetAssignedWorkCart(
            fox,
            requireLogging: true,
            out _,
            out _,
            requireFoxInsideRadius: false);
    }

    internal void UpdateWorkCartLoggingDisplay(Entity fox, WorkCartLoggingPlan plan)
    {
        if (serverApi == null || plan.BiteParts.Count == 0) return;

        int displayIndex = Math.Min(
            Math.Max(0, plan.BiteIndex),
            plan.BiteParts.Count - 1);
        BlockPos displayPosition = plan.BiteParts[displayIndex].Position;
        ITreeAttribute display = fox.WatchedAttributes.GetTreeAttribute(LoggingDisplayKey)
            ?? new TreeAttribute();
        display.SetBool(LoggingDisplayActiveKey, true);
        display.SetInt(LoggingDisplayXKey, displayPosition.X);
        display.SetInt(LoggingDisplayYKey, displayPosition.Y);
        display.SetInt(LoggingDisplayZKey, displayPosition.Z);
        display.SetInt(LoggingDisplayDimensionKey, displayPosition.dimension);
        display.SetInt(LoggingDisplayProgressKey, plan.BiteIndex);
        display.SetInt(LoggingDisplayTotalKey, plan.BiteParts.Count);
        fox.WatchedAttributes.SetAttribute(LoggingDisplayKey, display);
        fox.WatchedAttributes.MarkPathDirty(LoggingDisplayKey);
    }

    internal void ClearWorkCartLoggingDisplay(Entity fox)
    {
        ITreeAttribute? display = fox.WatchedAttributes.GetTreeAttribute(LoggingDisplayKey);
        if (display == null) return;

        display.SetBool(LoggingDisplayActiveKey, false);
        fox.WatchedAttributes.MarkPathDirty(LoggingDisplayKey);
    }

    internal bool TryCreateWorkCartLoggingPlan(Entity fox, out WorkCartLoggingPlan? plan,
        out string dialogueEvent)
    {
        plan = null;
        dialogueEvent = string.Empty;
        if (serverApi == null) return false;

        if (!CanContinueWorkCartLogging(fox, null)
            || !TryGetAssignedWorkCart(fox, true, out FoxWorkCartRecord? cart, out BlockPos? cartPos)
            || cart == null
            || cartPos == null)
        {
            workCartLoggingWaitingForDebris.Remove(fox.EntityId);
            return false;
        }
        if (!TryGetFoxStorage(fox, out BlockPos? storagePos) || storagePos == null)
        {
            dialogueEvent = "work.logging.no_storage";
            return false;
        }

        // A lower-priority task may have interrupted this fox after it reached
        // the tree. Keep the remembered plan and bite index instead of making
        // the fox abandon the tree and choose another one.
        if (workCartLoggingJobs.TryGetValue(fox.EntityId, out WorkCartLoggingJobState? rememberedJob))
        {
            if (IsCurrentWorkCartLoggingPlan(rememberedJob.Plan, cartPos))
            {
                plan = rememberedJob.Plan;
                workCartLoggingWaitingForDebris.Remove(fox.EntityId);
                dialogueEvent = "work.logging.recover_restart";
                return true;
            }

            RemoveWorkCartLoggingWorker(fox.EntityId, rememberedJob.Plan);
        }

        string cartKey = WorkCartPositionKey(cartPos);
        if (!workCartLoggingQueues.TryGetValue(cartKey, out WorkCartLoggingQueueState? queue))
        {
            return false;
        }

        // Keep one active tree per available adult worker whenever the cart
        // has enough queued candidates. Only share a tree after the distinct
        // candidates have been exhausted. Joining a plan remains O(1): fox
        // task starts never scan tree parts or perform discovery themselves.
        if (!queue.PrimaryDebrisPresent
            && queue.ActivePlans.Count(activePlan => !activePlan.IsTerminal)
                < Math.Max(1, queue.WorkersAtCartCount)
            && TryDequeueWorkCartLoggingPlan(fox, cart, cartPos, out plan))
        {
            if (workCartLoggingWaitingForDebris.Remove(fox.EntityId))
                dialogueEvent = "work.logging.resume_claim";
            return true;
        }

        if (queue.PrimaryDebrisPresent)
        {
            workCartLoggingWaitingForDebris.Add(fox.EntityId);
            dialogueEvent = "work.logging.wait_debris";
            return false;
        }

        WorkCartLoggingPlan? leastBusyPlan = queue.ActivePlans
            .Where(activePlan => !activePlan.IsTerminal)
            .OrderBy(activePlan => activePlan.WorkerHeartbeats.Count)
            .ThenBy(activePlan => activePlan.Root.HorDistanceSqTo(cartPos.X, cartPos.Z))
            .FirstOrDefault();
        if (leastBusyPlan != null)
        {
            plan = leastBusyPlan;
            if (workCartLoggingWaitingForDebris.Remove(fox.EntityId))
                dialogueEvent = "work.logging.resume_claim";
            return true;
        }

        bool dequeued = TryDequeueWorkCartLoggingPlan(fox, cart, cartPos, out plan);
        if (dequeued && workCartLoggingWaitingForDebris.Remove(fox.EntityId))
            dialogueEvent = "work.logging.resume_claim";
        else if (!dequeued)
            dialogueEvent = "work.logging.no_tree";
        return dequeued;
    }

    private int CountWorkCartFoxesAtCart(FoxWorkCartRecord cart)
    {
        int count = 0;
        float radius = GetOwnerWorkCartRadius(cart.OwnerUid);
        double radiusSq = radius * radius;
        foreach (string foxId in cart.AssignedFoxIds)
        {
            Entity? fox = FindLoadedCompanionByFoxId(foxId);
            if (fox == null
                || !fox.Alive
                || !IsTamedFox(fox)
                || !IsOwner(fox, cart.OwnerUid)
                || IsCompanionJuvenile(fox)
                || IsFoxAwayFromWorld(fox)
                || IsFoxIncapacitated(fox)
                || fox.Pos.Dimension != cart.Dimension)
            {
                continue;
            }

            double dx = fox.Pos.X - (cart.X + 0.5d);
            double dz = fox.Pos.Z - (cart.Z + 0.5d);
            if (dx * dx + dz * dz <= radiusSq) count++;
        }

        return count;
    }

    private bool IsCurrentWorkCartLoggingPlan(WorkCartLoggingPlan plan, BlockPos cartPos)
    {
        return !plan.IsTerminal
            && plan.CartPos.Equals(cartPos)
            && workCartLoggingQueues.TryGetValue(plan.CartKey, out WorkCartLoggingQueueState? queue)
            && queue.ActivePlans.Contains(plan);
    }

    internal bool CanContinueWorkCartLogging(Entity fox, BlockPos? expectedCart)
    {
        if (serverApi == null
            || !IsTamedFox(fox)
            || IsCompanionJuvenile(fox)
            || !fox.Alive
            || fox.State != EnumEntityState.Active
            || IsFoxAwayFromWorld(fox)
            || IsFoxIncapacitated(fox)
            || IsCompanionFoodRestricted(fox)
            || HasFoxStorageJob(fox)
            || GetCompanionActivityMode(fox) != CompanionActivityMode.AtEase
            || ShouldFoxSeekDenShelter(fox))
        {
            return false;
        }

        string mood = GetMood(fox);
        if (mood is "sleepy" or "resting" or "anxious" or "alarmed")
        {
            return false;
        }

        if (!TryGetAssignedWorkCart(
                fox,
                true,
                out _,
                out BlockPos? cartPos,
                requireFoxInsideRadius: expectedCart == null)
            || cartPos == null)
        {
            return false;
        }

        return expectedCart == null
            || (expectedCart.X == cartPos.X
                && expectedCart.Y == cartPos.Y
                && expectedCart.Z == cartPos.Z
                && expectedCart.dimension == cartPos.dimension);
    }

    private bool TryGetAssignedWorkCart(
        Entity fox,
        bool requireLogging,
        out FoxWorkCartRecord? cart,
        out BlockPos? cartPos,
        bool requireFoxInsideRadius = true)
    {
        cart = null;
        cartPos = null;
        if (serverApi == null || packRepository?.Loaded != true) return false;

        string foxId = GetDomesticationStatus(fox)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(foxId)
            || !packRepository.TryGetRecord(foxId, out FoxPackRecordV2? foxRecord)
            || foxRecord?.HasHome != true
            || !string.Equals(foxRecord.HomeType, "workcart", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        BlockPos position = new(
            foxRecord.HomeX,
            foxRecord.HomeY,
            foxRecord.HomeZ,
            foxRecord.HomeDimension);
        FoxWorkCartRecord? assignedCart = packRepository.GetWorkCart(position);
        if (assignedCart == null
            || !assignedCart.AssignedFoxIds.Contains(foxId, StringComparer.Ordinal)
            || (requireLogging && !assignedCart.LoggingEnabled)
            || position.dimension != fox.Pos.Dimension
            || serverApi.World.BlockAccessor.GetChunkAtBlockPos(position) == null
            || !IsWorkCartCode(serverApi.World.BlockAccessor.GetBlock(position).Code))
        {
            return false;
        }

        double dx = fox.Pos.X - (position.X + 0.5d);
        double dz = fox.Pos.Z - (position.Z + 0.5d);
        float radius = GetWorkCartRadius(fox);
        if (requireFoxInsideRadius && dx * dx + dz * dz > radius * radius)
        {
            return false;
        }

        cart = assignedCart;
        cartPos = position;
        return true;
    }

    /// <summary>
    /// The work cart, rather than each fox, owns tree discovery and execution.
    /// Every budget below is global for this 250 ms maintenance pass, not per
    /// cart or per worker. Round-robin ordering prevents one busy cart from
    /// monopolizing those budgets.
    /// </summary>
    internal void MaintainWorkCartLoggingQueues()
    {
        if (serverApi == null || packRepository?.Loaded != true) return;
        if (packRepository.WorkCartCount == 0
            && workCartLoggingQueues.Count == 0
            && workCartLoggingJobs.Count == 0
            && workCartLoggingWaitingForDebris.Count == 0) return;

        CleanupExpiredWorkCartLoggingWorkers();
        int claimCleanupBudget = WorkCartLoggingClaimCleanupPerTick;
        CleanupTerminalWorkCartLoggingClaims(ref claimCleanupBudget);

        HashSet<string> activeKeys = new(StringComparer.Ordinal);
        List<FoxWorkCartRecord> carts = packRepository.GetAllWorkCarts()
            .Where(cart => cart.LoggingEnabled && cart.AssignedFoxIds.Count > 0)
            .OrderBy(cart => WorkCartPositionKey(new BlockPos(cart.X, cart.Y, cart.Z, cart.Dimension)))
            .ToList();
        int scanColumnBudget = WorkCartLoggingScanColumnsPerTick;
        int graphNodeBudget = WorkCartLoggingGraphNodesPerTick;
        int partCheckBudget = WorkCartLoggingPartChecksPerTick;
        int blockBreakBudget = WorkCartLoggingBlocksBrokenPerTick;
        int debrisRefreshBudget = 1;
        int startIndex = carts.Count == 0 ? 0 : workCartLoggingRoundRobinOffset++ % carts.Count;
        for (int offset = 0; offset < carts.Count; offset++)
        {
            FoxWorkCartRecord cart = carts[(startIndex + offset) % carts.Count];

            BlockPos cartPos = new(cart.X, cart.Y, cart.Z, cart.Dimension);
            if (serverApi.World.BlockAccessor.GetChunkAtBlockPos(cartPos) == null) continue;

            string key = WorkCartPositionKey(cartPos);
            activeKeys.Add(key);
            float cartRadius = GetOwnerWorkCartRadius(cart.OwnerUid);
            if (!workCartLoggingQueues.TryGetValue(key, out WorkCartLoggingQueueState? queue)
                || Math.Abs(queue.Radius - cartRadius) > 0.01f)
            {
                if (queue != null) ClearWorkCartLoggingQueueByKey(key);
                queue = new WorkCartLoggingQueueState(cartPos, cartRadius);
                workCartLoggingQueues[key] = queue;
            }
            queue.WorkersAtCartCount = CountWorkCartFoxesAtCart(cart);

            RefreshWorkCartLoggingDebris(queue, ref debrisRefreshBudget);
            AdvanceWorkCartLoggingPlan(
                queue,
                ref partCheckBudget,
                ref blockBreakBudget);
            AdvanceWorkCartLoggingScan(
                cart,
                queue,
                ref scanColumnBudget,
                ref graphNodeBudget,
                ref partCheckBudget);
        }

        // Run this after the first pass has materialized the cart queues. On
        // a fresh world load those queues do not exist when OnSaveGameLoaded
        // arms recovery, so doing this at the top of the method used to make
        // the reset a no-op and could leave logger AI waiting at the cart.
        if (workCartLoggingStartupRecoveryPending)
        {
            RecoverWorkCartLoggingAfterLoad();
            workCartLoggingStartupRecoveryPending = false;
        }

        foreach (string key in workCartLoggingQueues.Keys
                     .Where(key => !activeKeys.Contains(key))
                     .ToArray())
        {
            ClearWorkCartLoggingQueueByKey(key);
        }
    }

    private void AdvanceWorkCartLoggingScan(
        FoxWorkCartRecord cart,
        WorkCartLoggingQueueState queue,
        ref int scanColumnBudget,
        ref int graphNodeBudget,
        ref int partCheckBudget)
    {
        if (serverApi == null || queue.NextScanAtMs > serverApi.World.ElapsedMilliseconds) return;

        IPlayer? owner = serverApi.World.PlayerByUid(cart.OwnerUid);
        if (owner == null) return;

        if (queue.ScanComplete)
        {
            // Never throw away trees that are already waiting for an
            // available fox. Refresh only after the current queue has been
            // drained; otherwise a slow or temporarily idle pack would lose
            // the cart's nearest-first work order every refresh interval.
            if (queue.Candidates.Count > 0 || queue.ActivePlans.Any(plan => !plan.IsTerminal)) return;
            queue.ResetScan(serverApi.World.ElapsedMilliseconds + WorkCartLoggingRescanIntervalMs);
            return;
        }

        if (queue.Candidates.Count >= WorkCartLoggingCandidateLimit) return;

        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        while (queue.PendingDiscovery != null && (graphNodeBudget > 0 || partCheckBudget > 0))
        {
            AdvanceWorkCartTreeDiscovery(cart, queue, owner, ref graphNodeBudget, ref partCheckBudget);
            if (queue.PendingDiscovery != null || queue.Candidates.Count >= WorkCartLoggingCandidateLimit) return;
        }

        while (queue.ScanIndex < queue.Columns.Count && scanColumnBudget > 0)
        {
            scanColumnBudget--;
            WorkCartScanColumn column = queue.Columns[queue.ScanIndex++];
            BlockPos surfaceProbe = new(
                queue.CartPos.X + column.Dx,
                queue.CartPos.Y,
                queue.CartPos.Z + column.Dz,
                queue.CartPos.dimension);
            int terrainHeight = blocks.GetTerrainMapheightAt(surfaceProbe);
            int rainHeight = blocks.GetRainMapHeightAt(surfaceProbe);
            int minY = Math.Max(1, Math.Min(terrainHeight, rainHeight) - 4);
            int maxY = Math.Min(blocks.MapSizeY - 2, Math.Max(terrainHeight, rainHeight) + 8);

            for (int y = minY; y <= maxY; y++)
            {
                BlockPos candidate = new(
                    surfaceProbe.X,
                    y,
                    surfaceProbe.Z,
                    queue.CartPos.dimension);
                Block candidateBlock = blocks.GetBlock(candidate);
                if (!IsOrdinaryTreeTrunk(candidateBlock)) continue;
                queue.PendingDiscovery = new WorkCartTreeDiscovery(candidate, queue.CartPos);
                break;
            }

            if (queue.PendingDiscovery != null && graphNodeBudget > 0)
            {
                AdvanceWorkCartTreeDiscovery(cart, queue, owner, ref graphNodeBudget, ref partCheckBudget);
                if (queue.PendingDiscovery != null || queue.Candidates.Count >= WorkCartLoggingCandidateLimit) return;
            }
        }

        if (queue.ScanIndex >= queue.Columns.Count)
        {
            queue.ScanComplete = true;
            queue.NextScanAtMs = serverApi.World.ElapsedMilliseconds + WorkCartLoggingRescanIntervalMs;
        }
    }

    private bool TryDequeueWorkCartLoggingPlan(
        Entity fox,
        FoxWorkCartRecord cart,
        BlockPos cartPos,
        out WorkCartLoggingPlan? plan)
    {
        plan = null;
        string key = WorkCartPositionKey(cartPos);
        if (!workCartLoggingQueues.TryGetValue(key, out WorkCartLoggingQueueState? queue)) return false;

        while (queue.Candidates.Count > 0)
        {
            WorkCartLoggingCandidate candidate = queue.Candidates[0];
            queue.Candidates.RemoveAt(0);
            string rootKey = WorkCartTreePartKey(candidate.Root);
            if (activeWorkCartTreesByRoot.TryGetValue(rootKey, out WorkCartLoggingPlan? claimed)
                && !claimed.IsTerminal) continue;

            if (!TryGetNaturalCleanupApproachTarget(
                    fox,
                    candidate.Root,
                    out Vec3d? approach,
                    candidateApproach =>
                    {
                        double approachDx = candidateApproach.X - (cartPos.X + 0.5d);
                        double approachDz = candidateApproach.Z - (cartPos.Z + 0.5d);
                        return approachDx * approachDx + approachDz * approachDz
                            <= queue.Radius * queue.Radius;
                    })
                || approach == null) continue;

            plan = new WorkCartLoggingPlan
            {
                CartKey = key,
                OwnerUid = cart.OwnerUid,
                CartPos = cartPos.Copy(),
                Root = candidate.Root.Copy(),
                ApproachTarget = approach,
                BiteParts = candidate.BreakParts,
                BreakParts = candidate.BreakParts,
                LastWorkerSeenAtMs = serverApi!.World.ElapsedMilliseconds
            };
            queue.ActivePlans.Add(plan);
            activeWorkCartTreesByRoot[rootKey] = plan;
            return true;
        }

        return false;
    }

    internal void ClearWorkCartLoggingQueue(BlockPos cartPos)
    {
        ClearWorkCartLoggingQueueByKey(WorkCartPositionKey(cartPos));
    }

    /// <summary>
    /// Re-arm the cart's runtime logging work after the player changes the
    /// logging switch. Queues, reservations, and AI task state are runtime
    /// state; clearing all three lets the next maintenance pass rebuild the
    /// work from the cart instead of leaving a fox parked on stale state.
    /// </summary>
    internal void RearmWorkCartLogging(BlockPos cartPos)
    {
        ClearWorkCartLoggingQueue(cartPos);

        foreach (string foxId in packRepository?.GetWorkCart(cartPos)?.AssignedFoxIds
                     ?? Enumerable.Empty<string>())
        {
            Entity? fox = FindLoadedCompanionByFoxId(foxId);
            if (fox == null) continue;

            AiTaskManager? manager = fox.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager;
            if (manager?.ActiveTasksBySlot.Any(task => task is AiTaskFeralKinshipLogging) == true)
            {
                manager.StopTasks();
            }
            ClearWorkCartLoggingDisplay(fox);
        }
    }

    private void ResetWorkCartLoggingRuntimeState()
    {
        workCartLoggingJobs.Clear();
        workCartLoggingWaitingForDebris.Clear();
        workCartLoggingQueues.Clear();
        activeWorkCartTreesByRoot.Clear();
        activeWorkCartTreeParts.Clear();
        terminalWorkCartLoggingPlans.Clear();
        workCartLoggingRoundRobinOffset = 0;
        workCartLoggingStartupRecoveryPending = false;
    }

    private void RecoverWorkCartLoggingAfterLoad()
    {
        if (serverApi == null || packRepository?.Loaded != true) return;

        long now = serverApi.World.ElapsedMilliseconds;
        foreach (FoxWorkCartRecord cart in packRepository.GetAllWorkCarts()
                     .Where(cart => cart.LoggingEnabled && cart.AssignedFoxIds.Count > 0))
        {
            BlockPos cartPos = new(cart.X, cart.Y, cart.Z, cart.Dimension);
            string key = WorkCartPositionKey(cartPos);
            if (workCartLoggingQueues.TryGetValue(key, out WorkCartLoggingQueueState? queue))
            {
                queue.ResetScan(now);
                queue.PrimaryDebrisPresent = false;
                queue.NextDebrisRefreshAtMs = 0;
            }
        }

        // AI tasks are not durable state, and a world reload can leave a
        // resident's task manager parked on the old runtime's decision. Reset
        // eligible At Ease cart residents once so they immediately reevaluate
        // the rebuilt queue. Commands, combat, cargo, and shelter residents
        // are excluded by CanRecoverWorkCartLoggingFox.
        foreach (Entity fox in loadedFoxes.Values.ToArray())
        {
            if (!CanRecoverWorkCartLoggingFox(fox, out FoxWorkCartRecord? cart)
                || cart == null) continue;

            AiTaskManager? manager = fox.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager;
            if (manager != null)
            {
                manager.StopTasks();
            }
            ClearWorkCartLoggingDisplay(fox);
        }
    }

    private bool CanRecoverWorkCartLoggingFox(Entity fox, out FoxWorkCartRecord? cart)
    {
        cart = null;
        if (serverApi == null
            || !fox.Alive
            || !IsTamedFox(fox)
            || IsCompanionJuvenile(fox)
            || IsFoxAwayFromWorld(fox)
            || IsFoxIncapacitated(fox)
            || GetCompanionActivityMode(fox) != CompanionActivityMode.AtEase
            || HasFoxStorageJob(fox)) return false;

        string foxId = GetDomesticationStatus(fox)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(foxId)) return false;
        cart = packRepository?.GetWorkCartForFox(foxId);
        return cart?.LoggingEnabled == true;
    }

    private void ClearWorkCartLoggingQueueByKey(string key)
    {
        if (!workCartLoggingQueues.TryGetValue(key, out WorkCartLoggingQueueState? queue)) return;
        foreach (WorkCartLoggingPlan plan in queue.ActivePlans.ToArray())
        {
            TerminateWorkCartLoggingPlan(plan, completed: false);
        }
        workCartLoggingQueues.Remove(key);
    }

    private void RefreshWorkCartLoggingDebris(WorkCartLoggingQueueState queue, ref int budget)
    {
        if (serverApi == null
            || budget <= 0
            || queue.NextDebrisRefreshAtMs > serverApi.World.ElapsedMilliseconds) return;
        budget--;
        queue.PrimaryDebrisPresent = HasWorkCartLoggingDebris(
            queue.CartPos,
            queue.Radius + 8f,
            includeLowPriority: false);
        queue.NextDebrisRefreshAtMs = serverApi.World.ElapsedMilliseconds + WorkCartLoggingDebrisRefreshMs;
    }

    private void AdvanceWorkCartTreeDiscovery(
        FoxWorkCartRecord cart,
        WorkCartLoggingQueueState queue,
        IPlayer owner,
        ref int graphNodeBudget,
        ref int partCheckBudget)
    {
        if (serverApi == null || queue.PendingDiscovery == null) return;
        WorkCartTreeDiscovery discovery = queue.PendingDiscovery;

        if (discovery.Stage is WorkCartTreeDiscoveryStage.FirstTraversal
            or WorkCartTreeDiscoveryStage.CanonicalTraversal)
        {
            int used = discovery.Traversal.Advance(
                serverApi.World,
                Math.Max(0, graphNodeBudget),
                MaximumWorkCartTreeParts);
            graphNodeBudget -= used;
            if (!discovery.Traversal.Complete) return;
            if (discovery.Traversal.Invalid || discovery.Traversal.FoundPositions.Count == 0)
            {
                queue.PendingDiscovery = null;
                return;
            }

            if (discovery.Stage == WorkCartTreeDiscoveryStage.FirstTraversal)
            {
                BlockPos? root = discovery.Traversal.CanonicalRoot;
                if (root == null || !queue.InspectedTrees.Add(WorkCartTreePartKey(root)))
                {
                    queue.PendingDiscovery = null;
                    return;
                }

                double dx = root.X - queue.CartPos.X;
                double dz = root.Z - queue.CartPos.Z;
                if (dx * dx + dz * dz > queue.Radius * queue.Radius)
                {
                    queue.PendingDiscovery = null;
                    return;
                }

                // The first traversal may begin on a side limb. A second,
                // still-incremental traversal from the canonical lowest wood
                // block produces the same complete graph a player axe uses.
                discovery.Root = root.Copy();
                discovery.Traversal = new IncrementalWorkCartTreeTraversal(root, queue.CartPos);
                discovery.Stage = WorkCartTreeDiscoveryStage.CanonicalTraversal;
                return;
            }

            discovery.Stage = WorkCartTreeDiscoveryStage.Validating;
            discovery.ValidationIndex = 0;
            discovery.ValidatedParts.Clear();
        }

        if (discovery.Stage != WorkCartTreeDiscoveryStage.Validating) return;
        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        while (discovery.ValidationIndex < discovery.Traversal.FoundPositions.Count
            && partCheckBudget > 0)
        {
            partCheckBudget--;
            BlockPos position = discovery.Traversal.FoundPositions[discovery.ValidationIndex++];
            if (blocks.GetChunkAtBlockPos(position) == null)
            {
                discovery.Invalid = true;
                break;
            }

            Block block = blocks.GetBlock(position);
            if (!IsOrdinaryTreePart(block))
            {
                discovery.Invalid = true;
                break;
            }
            // Keep ordinary leaves in the vanilla-style traversal so they can
            // connect the graph, but leave them for LeafBlockDecay. Branchy
            // leaves are the exception: they remain actionable because a
            // branchy leaf left in the canopy can preserve nearby foliage.
            if (block.BlockMaterial == EnumBlockMaterial.Wood || IsBranchyTreeLeaf(block))
            {
                if (!CanPlayerModifyBlock(owner, position))
                {
                    discovery.Invalid = true;
                    break;
                }
                discovery.ValidatedParts.Add(new WorkCartTreePart(position.Copy(), block.Id));
            }
        }

        if (discovery.Invalid)
        {
            queue.PendingDiscovery = null;
            return;
        }
        if (discovery.ValidationIndex < discovery.Traversal.FoundPositions.Count) return;
        if (discovery.Root == null || discovery.ValidatedParts.Count == 0)
        {
            queue.PendingDiscovery = null;
            return;
        }

        double rootDx = discovery.Root.X - queue.CartPos.X;
        double rootDz = discovery.Root.Z - queue.CartPos.Z;
        queue.Candidates.Add(new WorkCartLoggingCandidate
        {
            Root = discovery.Root.Copy(),
            BreakParts = discovery.ValidatedParts,
            DistanceFromCart = rootDx * rootDx + rootDz * rootDz
        });
        queue.Candidates.Sort((left, right) => left.DistanceFromCart.CompareTo(right.DistanceFromCart));
        queue.PendingDiscovery = null;
    }

    private void AdvanceWorkCartLoggingPlan(
        WorkCartLoggingQueueState queue,
        ref int partCheckBudget,
        ref int blockBreakBudget)
    {
        if (serverApi == null) return;
        foreach (WorkCartLoggingPlan plan in queue.ActivePlans
                     .Where(activePlan => !activePlan.IsTerminal)
                     .ToArray())
        {
            AdvanceWorkCartLoggingPlan(queue, plan, ref partCheckBudget, ref blockBreakBudget);
            if (partCheckBudget <= 0 && blockBreakBudget <= 0) return;
        }
    }

    private void AdvanceWorkCartLoggingPlan(
        WorkCartLoggingQueueState queue,
        WorkCartLoggingPlan plan,
        ref int partCheckBudget,
        ref int blockBreakBudget)
    {
        if (serverApi == null || plan.IsTerminal) return;
        long now = serverApi.World.ElapsedMilliseconds;

        if (plan.Phase == WorkCartLoggingPhase.Claiming)
        {
            while (plan.ClaimIndex < plan.BreakParts.Count && partCheckBudget > 0)
            {
                partCheckBudget--;
                WorkCartTreePart part = plan.BreakParts[plan.ClaimIndex++];
                string key = WorkCartTreePartKey(part.Position);
                if (activeWorkCartTreeParts.TryGetValue(key, out WorkCartLoggingPlan? other)
                    && !ReferenceEquals(other, plan)
                    && !other.IsTerminal)
                {
                    TerminateWorkCartLoggingPlan(plan, completed: false);
                    return;
                }
                activeWorkCartTreeParts[key] = plan;
                plan.ClaimedPartKeys.Add(key);
            }
            if (plan.ClaimIndex >= plan.BreakParts.Count)
            {
                plan.Phase = WorkCartLoggingPhase.Biting;
            }
        }

        if (plan.Phase == WorkCartLoggingPhase.Biting
            && plan.WorkerHeartbeats.Count == 0
            && now - plan.LastWorkerSeenAtMs >= WorkCartLoggingIdlePlanLifetimeMs)
        {
            TerminateWorkCartLoggingPlan(plan, completed: false);
            return;
        }

        if (plan.Phase == WorkCartLoggingPhase.Validating)
        {
            // Primary debris belongs in storage before this cart commits a
            // second tree. The cached cart-level observation avoids making
            // every waiting fox enumerate dropped entities.
            if (queue.PrimaryDebrisPresent) return;
            IPlayer? owner = serverApi.World.PlayerByUid(plan.OwnerUid);
            if (owner == null) return;
            while (plan.ValidationIndex < plan.BreakParts.Count && partCheckBudget > 0)
            {
                partCheckBudget--;
                WorkCartTreePart part = plan.BreakParts[plan.ValidationIndex];
                if (!IsWorkCartTreePartStillPresent(part)
                    || !CanPlayerModifyBlock(owner, part.Position))
                {
                    TerminateWorkCartLoggingPlan(plan, completed: false);
                    return;
                }
                plan.ValidationIndex++;
            }
            if (plan.ValidationIndex >= plan.BreakParts.Count)
            {
                plan.Phase = WorkCartLoggingPhase.Felling;
                plan.BlockedSinceMs = 0;
            }
        }

        if (plan.Phase == WorkCartLoggingPhase.Felling)
        {
            AdvanceWorkCartTreeFelling(plan, ref blockBreakBudget);
        }
    }

    private void AdvanceWorkCartTreeFelling(WorkCartLoggingPlan plan, ref int blockBreakBudget)
    {
        if (serverApi == null || blockBreakBudget <= 0) return;
        IPlayer? owner = serverApi.World.PlayerByUid(plan.OwnerUid);
        if (owner == null) return;

        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        while (plan.FellingIndex < plan.BreakParts.Count && blockBreakBudget > 0)
        {
            WorkCartTreePart part = plan.BreakParts[plan.FellingIndex];
            if (blocks.GetChunkAtBlockPos(part.Position) == null)
            {
                long now = serverApi.World.ElapsedMilliseconds;
                if (plan.BlockedSinceMs == 0) plan.BlockedSinceMs = now;
                if (now - plan.BlockedSinceMs >= WorkCartLoggingBlockedLifetimeMs)
                {
                    TerminateWorkCartLoggingPlan(plan, completed: false);
                }
                return;
            }

            plan.BlockedSinceMs = 0;
            if (!IsWorkCartTreePartStillPresent(part)
                || !CanPlayerModifyBlock(owner, part.Position))
            {
                // Nothing is silently deleted: already-felled blocks used the
                // normal drop path, and an externally changed remainder is
                // left untouched for the next cart scan.
                TerminateWorkCartLoggingPlan(plan, completed: false);
                return;
            }

            blockBreakBudget--;
            Block block = blocks.GetBlock(part.Position);
            bool branchy = IsBranchyTreeLeaf(block);
            bool leaves = block.BlockMaterial == EnumBlockMaterial.Leaves && !branchy;
            float dropMultiplier = branchy
                ? plan.BranchyLeavesDropMultiplier
                : leaves ? plan.LeavesDropMultiplier : 1f;
            bool ownerAlreadyHasAxe = owner.InventoryManager.ActiveTool == EnumTool.Axe;
            if (owner.WorldData.CurrentGameMode == EnumGameMode.Creative)
            {
                SpawnCreativeDrops(block, part.Position, owner, dropMultiplier);
                if (!ownerAlreadyHasAxe)
                {
                    SpawnVirtualAxeDrops(block, part.Position, owner, dropMultiplier);
                }
            }
            else if (!ownerAlreadyHasAxe)
            {
                SpawnVirtualAxeDrops(block, part.Position, owner, dropMultiplier);
            }
            block.OnBlockBroken(serverApi.World, part.Position, owner, dropMultiplier);
            blocks.TriggerNeighbourBlockUpdate(part.Position);
            plan.FellingIndex++;
            plan.BlocksBroken++;

            if (leaves && plan.LeavesDropMultiplier > 0.03f) plan.LeavesDropMultiplier *= 0.85f;
            if (branchy && plan.BranchyLeavesDropMultiplier > 0.015f)
            {
                plan.BranchyLeavesDropMultiplier *= 0.7f;
            }
        }

        if (plan.FellingIndex < plan.BreakParts.Count) return;
        if (plan.BlocksBroken > 0)
        {
            serverApi.World.PlaySoundAt(
                new AssetLocation("sounds/effect/treefell"),
                plan.Root,
                -0.25d,
                owner,
                false,
                32f,
                GameMath.Clamp(plan.BlocksBroken / 100f, 0.25f, 1f));
        }
        TerminateWorkCartLoggingPlan(plan, completed: plan.BlocksBroken > 0);
    }

    private static string WorkCartPositionKey(BlockPos position) =>
        $"{position.dimension}:{position.X}:{position.Y}:{position.Z}";

    private static bool IsOrdinaryTreeTrunk(Block? block)
    {
        if (block?.Code == null
            || block.BlockMaterial != EnumBlockMaterial.Wood
            || IsFruitTreePart(block)
            || block.Attributes?["treeFellingCanChop"].AsBool(true) == false)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(block.Attributes?["treeFellingGroupCode"].AsString())
            && (block.Attributes?["treeFellingGroupSpreadIndex"].AsInt(0) ?? 0) >= 2;
    }

    private static bool IsOrdinaryTreePart(Block? block)
    {
        if (block?.Code == null || IsFruitTreePart(block)) return false;
        if (block.BlockMaterial is not (EnumBlockMaterial.Wood or EnumBlockMaterial.Leaves)) return false;
        return !string.IsNullOrWhiteSpace(block.Attributes?["treeFellingGroupCode"].AsString());
    }

    private static bool IsBranchyTreeLeaf(Block? block)
    {
        return block?.BlockMaterial == EnumBlockMaterial.Leaves
            && !IsFruitTreePart(block)
            && block.Code?.Path.Contains("branchy", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsFruitTreePart(Block block)
    {
        return block is BlockFruitTreePart
            || block.Code?.Path.StartsWith("fruittree", StringComparison.OrdinalIgnoreCase) == true
            || string.Equals(
                block.Attributes?["treeFellingGroupCode"].AsString(),
                "fruittree",
                StringComparison.OrdinalIgnoreCase);
    }

    internal bool HasWorkCartLoggingDebris(BlockPos cartPos, float radius, bool includeLowPriority = true)
    {
        if (serverApi == null) return false;
        Vec3d center = new(
            cartPos.X + 0.5d,
            cartPos.Y + cartPos.dimension * BlockPos.DimensionBoundary + 0.5d,
            cartPos.Z + 0.5d);
        return serverApi.World.GetEntitiesAround(
                center,
                radius,
                serverApi.World.BlockAccessor.MapSizeY,
                entity => entity is EntityItem item
                    && item.Alive
                    && item.Pos.Dimension == cartPos.dimension
                    && IsInsideWorkCartHorizontalRadius(item, center, radius)
                    && item.Itemstack != null
                    && item.Itemstack.StackSize > 0
                    && IsLoggingDebris(item.Itemstack)
                    // A tree drop can remain suspended inside the tree (or
                    // high in its canopy) instead of becoming a reachable
                    // ground item. Do not let that one bad entity deadlock
                    // the cart's next-tree gate.
                    && !IsSuspendedTreeDebris(item)
                    && (includeLowPriority || GetLoggingDebrisPriority(item.Itemstack) == 0))
            .Length > 0;
    }

    private EntityItem? FindPrioritizedWorkCartLoggingDebris(Entity fox)
    {
        if (serverApi == null
            || !TryGetAssignedWorkCart(
                fox,
                true,
                out _,
                out BlockPos? cartPos,
                requireFoxInsideRadius: false)
            || cartPos == null) return null;

        Vec3d center = new(
            cartPos.X + 0.5d,
            cartPos.Y + cartPos.dimension * BlockPos.DimensionBoundary + 0.5d,
            cartPos.Z + 0.5d);
        long now = serverApi.World.ElapsedMilliseconds;
        float debrisRadius = GetWorkCartRadius(fox) + 8f;
        return serverApi.World.GetEntitiesAround(
                center,
                debrisRadius,
                serverApi.World.BlockAccessor.MapSizeY,
                entity => entity is EntityItem item
                    && item.Alive
                    && item.Pos.Dimension == cartPos.dimension
                    && IsInsideWorkCartHorizontalRadius(item, center, debrisRadius)
                    && item.Itemstack != null
                    && item.Itemstack.StackSize > 0
                    && now - item.itemSpawnedMilliseconds >= 2500
                    && IsLoggingDebris(item.Itemstack)
                    && !IsSuspendedTreeDebris(item)
                    && (!droppedItemReservations.TryGetValue(item.EntityId, out long reservedBy)
                        || reservedBy == fox.EntityId))
            .OfType<EntityItem>()
            .OrderBy(item => GetLoggingDebrisPriority(item.Itemstack))
            .ThenBy(item => item.Pos.SquareDistanceTo(fox.Pos))
            .FirstOrDefault();
    }

    private bool IsSuspendedTreeDebris(EntityItem item)
    {
        if (serverApi == null || item.Itemstack == null || !IsLoggingDebris(item.Itemstack))
        {
            return false;
        }

        BlockPos itemPos = item.Pos.AsBlockPos;
        Block? containingBlock = serverApi.World.BlockAccessor.GetBlock(itemPos);
        if (IsOrdinaryTreePart(containingBlock)) return true;

        // Some versions let the item settle just outside the tree block
        // rather than inside it. Ignore only clearly elevated tree drops;
        // ordinary ground debris still blocks the next tree as intended.
        int terrainHeight = serverApi.World.BlockAccessor.GetTerrainMapheightAt(itemPos);
        return item.Pos.Y > terrainHeight + 4.0d;
    }

    private static bool IsLoggingDebris(ItemStack stack)
    {
        // Leaves and branchy leaves do not all use a consistent code path
        // across vanilla versions and tree types. The block material is the
        // reliable part of the dropped stack, while fruit-tree parts remain
        // excluded from the logging cart's ordinary-tree job.
        if (stack.Block?.BlockMaterial == EnumBlockMaterial.Leaves
            && !IsFruitTreePart(stack.Block))
        {
            return true;
        }

        string path = stack.Collectible?.Code?.Path ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path)) return false;

        return string.Equals(path, "stick", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("treeseed-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("log-", StringComparison.OrdinalIgnoreCase)
            || path.Contains("branch", StringComparison.OrdinalIgnoreCase)
            || path.Contains("leaf", StringComparison.OrdinalIgnoreCase)
            || path.Contains("leaves", StringComparison.OrdinalIgnoreCase)
            || stack.Block?.BlockMaterial == EnumBlockMaterial.Wood
                && path.Contains("log", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetLoggingDebrisPriority(ItemStack stack)
    {
        string path = stack.Collectible?.Code?.Path ?? string.Empty;
        return path.Contains("leaf", StringComparison.OrdinalIgnoreCase)
            || path.Contains("leaves", StringComparison.OrdinalIgnoreCase)
            || path.Contains("branchy", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;
    }

    private static bool IsInsideWorkCartHorizontalRadius(Entity item, Vec3d center, float radius)
    {
        double dx = item.Pos.X - center.X;
        double dz = item.Pos.Z - center.Z;
        return dx * dx + dz * dz <= radius * radius;
    }

    internal bool TryReserveWorkCartTree(Entity fox, WorkCartLoggingPlan plan)
    {
        if (serverApi == null || plan.IsTerminal || !IsCurrentWorkCartLoggingPlan(plan, plan.CartPos))
        {
            return false;
        }

        if (workCartLoggingJobs.TryGetValue(fox.EntityId, out WorkCartLoggingJobState? existingJob)
            && !ReferenceEquals(existingJob.Plan, plan))
        {
            RemoveWorkCartLoggingWorker(fox.EntityId, existingJob.Plan);
        }

        long now = serverApi.World.ElapsedMilliseconds;
        workCartLoggingJobs[fox.EntityId] = new WorkCartLoggingJobState(plan);
        plan.WorkerHeartbeats[fox.EntityId] = now;
        plan.ParticipantIds.Add(fox.EntityId);
        plan.LastWorkerSeenAtMs = now;
        return true;
    }

    internal bool RefreshWorkCartTreeReservation(Entity fox, WorkCartLoggingPlan plan)
    {
        if (serverApi == null
            || plan.IsTerminal
            || !workCartLoggingJobs.TryGetValue(fox.EntityId, out WorkCartLoggingJobState? job)
            || !ReferenceEquals(job.Plan, plan)
            || !IsCurrentWorkCartLoggingPlan(plan, plan.CartPos)) return false;
        long now = serverApi.World.ElapsedMilliseconds;
        plan.WorkerHeartbeats[fox.EntityId] = now;
        plan.LastWorkerSeenAtMs = now;
        return true;
    }

    internal WorkCartLoggingTaskState GetWorkCartLoggingTaskState(WorkCartLoggingPlan plan)
    {
        if (plan.Phase == WorkCartLoggingPhase.Completed) return WorkCartLoggingTaskState.Completed;
        if (plan.IsTerminal || !IsCurrentWorkCartLoggingPlan(plan, plan.CartPos))
        {
            return WorkCartLoggingTaskState.Failed;
        }
        return plan.Phase == WorkCartLoggingPhase.Biting
            ? WorkCartLoggingTaskState.Biting
            : WorkCartLoggingTaskState.Waiting;
    }

    internal WorkCartLoggingBiteResult TryAdvanceWorkCartLoggingBite(Entity fox, WorkCartLoggingPlan plan)
    {
        if (serverApi == null
            || GetWorkCartLoggingTaskState(plan) == WorkCartLoggingTaskState.Failed
            || !RefreshWorkCartTreeReservation(fox, plan))
        {
            return WorkCartLoggingBiteResult.Failed;
        }

        if (plan.Phase != WorkCartLoggingPhase.Biting) return WorkCartLoggingBiteResult.Waiting;
        if (plan.BiteIndex >= plan.BiteParts.Count)
        {
            BeginWorkCartLoggingValidation(plan);
            return WorkCartLoggingBiteResult.Waiting;
        }

        WorkCartTreePart part = plan.BiteParts[plan.BiteIndex];
        if (!IsWorkCartTreePartStillPresent(part))
        {
            TerminateWorkCartLoggingPlan(plan, completed: false);
            return WorkCartLoggingBiteResult.Failed;
        }

        plan.BiteIndex++;
        plan.LastWorkerSeenAtMs = serverApi.World.ElapsedMilliseconds;
        if (plan.BiteIndex >= plan.BiteParts.Count) BeginWorkCartLoggingValidation(plan);
        return WorkCartLoggingBiteResult.Advanced;
    }

    private static void BeginWorkCartLoggingValidation(WorkCartLoggingPlan plan)
    {
        if (plan.Phase != WorkCartLoggingPhase.Biting) return;
        plan.Phase = WorkCartLoggingPhase.Validating;
        plan.ValidationIndex = 0;
    }

    internal void ReleaseWorkCartTreeReservation(Entity fox, WorkCartLoggingPlan? plan)
    {
        if (plan == null) return;
        RemoveWorkCartLoggingWorker(fox.EntityId, plan);
    }

    internal void ReleaseWorkCartTreeReservations(Entity fox)
    {
        if (workCartLoggingJobs.TryGetValue(fox.EntityId, out WorkCartLoggingJobState? job))
        {
            // A recovery entity can be torn down between creation and full
            // registration. Do not let a partially initialized logging job
            // turn cleanup into a second failure that hides the real error.
            workCartLoggingJobs.Remove(fox.EntityId);
            if (job?.Plan != null)
            {
                job.Plan.WorkerHeartbeats.Remove(fox.EntityId);
            }
        }
    }

    internal void AbandonWorkCartLoggingJob(Entity fox, WorkCartLoggingPlan? plan)
    {
        if (plan != null) TerminateWorkCartLoggingPlan(plan, completed: false);
    }

    private void RemoveWorkCartLoggingWorker(long entityId, WorkCartLoggingPlan? plan)
    {
        if (workCartLoggingJobs.TryGetValue(entityId, out WorkCartLoggingJobState? job)
            && ReferenceEquals(job?.Plan, plan))
        {
            workCartLoggingJobs.Remove(entityId);
        }
        plan?.WorkerHeartbeats.Remove(entityId);
    }

    private void CleanupExpiredWorkCartLoggingWorkers()
    {
        if (serverApi == null) return;
        long now = serverApi.World.ElapsedMilliseconds;
        foreach ((long entityId, WorkCartLoggingJobState job) in workCartLoggingJobs.ToArray())
        {
            if (job?.Plan == null
                || !job.Plan.WorkerHeartbeats.TryGetValue(entityId, out long heartbeat)
                || now - heartbeat < WorkCartLoggingWorkerLeaseMs) continue;
            RemoveWorkCartLoggingWorker(entityId, job.Plan);
        }
    }

    private void TerminateWorkCartLoggingPlan(WorkCartLoggingPlan plan, bool completed)
    {
        if (plan.IsTerminal) return;
        long[] participatingWorkers = completed
            ? plan.ParticipantIds.ToArray()
            : Array.Empty<long>();
        plan.Phase = completed ? WorkCartLoggingPhase.Completed : WorkCartLoggingPhase.Abandoned;
        if (workCartLoggingQueues.TryGetValue(plan.CartKey, out WorkCartLoggingQueueState? queue)
            && queue.ActivePlans.Remove(plan))
        {
            if (completed)
            {
                // Close the sub-tick window between spawning this tree's
                // drops and the next cached debris observation.
                queue.PrimaryDebrisPresent = true;
                queue.NextDebrisRefreshAtMs = 0;
            }
            queue.NextScanAtMs = Math.Min(
                queue.NextScanAtMs,
                serverApi?.World.ElapsedMilliseconds + 1000 ?? 0);
        }

        string rootKey = WorkCartTreePartKey(plan.Root);
        if (activeWorkCartTreesByRoot.TryGetValue(rootKey, out WorkCartLoggingPlan? rootPlan)
            && ReferenceEquals(rootPlan, plan))
        {
            activeWorkCartTreesByRoot.Remove(rootKey);
        }

        foreach (long workerId in plan.WorkerHeartbeats.Keys.ToArray())
        {
            if (workCartLoggingJobs.TryGetValue(workerId, out WorkCartLoggingJobState? job)
                && ReferenceEquals(job.Plan, plan))
            {
                workCartLoggingJobs.Remove(workerId);
            }
            if (loadedFoxes.TryGetValue(workerId, out Entity? worker)) ClearWorkCartLoggingDisplay(worker);
        }
        plan.WorkerHeartbeats.Clear();
        foreach (long workerId in participatingWorkers)
        {
            if (loadedFoxes.TryGetValue(workerId, out Entity? worker) && worker.Alive)
            {
                AwardDutyExperience(worker, 10);
            }
        }
        plan.ClaimReleaseIndex = 0;
        terminalWorkCartLoggingPlans.Enqueue(plan);
    }

    private void CleanupTerminalWorkCartLoggingClaims(ref int budget)
    {
        while (terminalWorkCartLoggingPlans.Count > 0 && budget > 0)
        {
            WorkCartLoggingPlan plan = terminalWorkCartLoggingPlans.Peek();
            while (plan.ClaimReleaseIndex < plan.ClaimedPartKeys.Count && budget > 0)
            {
                budget--;
                string key = plan.ClaimedPartKeys[plan.ClaimReleaseIndex++];
                if (activeWorkCartTreeParts.TryGetValue(key, out WorkCartLoggingPlan? holder)
                    && ReferenceEquals(holder, plan))
                {
                    activeWorkCartTreeParts.Remove(key);
                }
            }
            if (plan.ClaimReleaseIndex < plan.ClaimedPartKeys.Count) return;
            terminalWorkCartLoggingPlans.Dequeue();
        }
    }

    internal bool IsWorkCartTreePartStillPresent(WorkCartTreePart part)
    {
        return serverApi != null
            && serverApi.World.BlockAccessor.GetChunkAtBlockPos(part.Position) != null
            && serverApi.World.BlockAccessor.GetBlock(part.Position).Id == part.ExpectedBlockId;
    }

    private void SpawnCreativeDrops(
        Block block,
        BlockPos position,
        IPlayer owner,
        float dropQuantityMultiplier)
    {
        if (serverApi == null) return;

        ItemStack[]? drops = block.GetDrops(
            serverApi.World,
            position,
            owner,
            dropQuantityMultiplier);
        if (drops == null) return;

        foreach (ItemStack drop in drops)
        {
            if (drop != null && drop.StackSize > 0)
            {
                serverApi.World.SpawnItemEntity(drop.Clone(), position, null);
            }
        }
    }

    private void SpawnVirtualAxeDrops(Block block, BlockPos position, IPlayer owner, float dropQuantityMultiplier)
    {
        if (serverApi == null || block.Drops == null) return;

        foreach (BlockDropItemStack drop in block.Drops)
        {
            if (drop.Tool != EnumTool.Axe) continue;
            float statMultiplier = 1f;
            if (owner.Entity?.Stats != null && !string.IsNullOrWhiteSpace(drop.DropModbyStat))
            {
                statMultiplier = owner.Entity.Stats.GetBlended(drop.DropModbyStat);
            }

            ItemStack? stack = drop.GetNextItemStack(dropQuantityMultiplier * statMultiplier);
            if (stack == null) continue;

            if (stack.Collectible is IResolvableCollectible resolvable)
            {
                DummySlot slot = new(stack);
                resolvable.Resolve(slot, serverApi.World);
                stack = slot.Itemstack;
            }

            if (stack != null) serverApi.World.SpawnItemEntity(stack, position, null);
        }
    }

    private static string WorkCartTreePartKey(BlockPos position) =>
        $"{position.dimension}:{position.X}:{position.Y}:{position.Z}";

}

internal enum WorkCartLoggingPhase
{
    Claiming,
    Biting,
    Validating,
    Felling,
    Completed,
    Abandoned
}

internal enum WorkCartLoggingTaskState
{
    Biting,
    Waiting,
    Completed,
    Failed
}

internal enum WorkCartLoggingBiteResult
{
    Advanced,
    Waiting,
    Failed
}

internal sealed class WorkCartLoggingPlan
{
    public string CartKey = string.Empty;
    public string OwnerUid = string.Empty;
    public BlockPos CartPos = null!;
    public BlockPos Root = null!;
    public Vec3d ApproachTarget = new();
    public List<WorkCartTreePart> BiteParts = new();
    public List<WorkCartTreePart> BreakParts = new();
    public readonly Dictionary<long, long> WorkerHeartbeats = new();
    public readonly HashSet<long> ParticipantIds = new();
    public readonly List<string> ClaimedPartKeys = new();
    public WorkCartLoggingPhase Phase = WorkCartLoggingPhase.Claiming;
    public int BiteIndex;
    public int ClaimIndex;
    public int ClaimReleaseIndex;
    public int ValidationIndex;
    public int FellingIndex;
    public int BlocksBroken;
    public long LastWorkerSeenAtMs;
    public long BlockedSinceMs;
    public float LeavesDropMultiplier = 1f;
    public float BranchyLeavesDropMultiplier = 0.8f;

    public bool IsTerminal => Phase is WorkCartLoggingPhase.Completed or WorkCartLoggingPhase.Abandoned;
}

internal sealed class WorkCartLoggingJobState
{
    public WorkCartLoggingJobState(WorkCartLoggingPlan plan)
    {
        Plan = plan;
    }

    public WorkCartLoggingPlan Plan { get; }
}

internal sealed class WorkCartLoggingQueueState
{
    public WorkCartLoggingQueueState(BlockPos cartPos, float radius)
    {
        CartPos = cartPos.Copy();
        Radius = radius;
        Columns = BuildColumns(radius);
    }

    public BlockPos CartPos { get; }
    public float Radius { get; }
    public IReadOnlyList<WorkCartScanColumn> Columns { get; }
    public List<WorkCartLoggingCandidate> Candidates { get; } = new();
    public HashSet<string> InspectedTrees { get; } = new(StringComparer.Ordinal);
    public WorkCartTreeDiscovery? PendingDiscovery { get; set; }
    public List<WorkCartLoggingPlan> ActivePlans { get; } = new();
    public int WorkersAtCartCount { get; set; }
    public int ScanIndex { get; set; }
    public bool ScanComplete { get; set; }
    public long NextScanAtMs { get; set; }
    public long NextDebrisRefreshAtMs { get; set; }
    public bool PrimaryDebrisPresent { get; set; }

    public void ResetScan(long nextScanAtMs)
    {
        Candidates.Clear();
        InspectedTrees.Clear();
        PendingDiscovery = null;
        ScanIndex = 0;
        ScanComplete = false;
        NextScanAtMs = nextScanAtMs;
    }

    private static List<WorkCartScanColumn> BuildColumns(float dynamicRadius)
    {
        int radius = (int)Math.Ceiling(dynamicRadius);
        return Enumerable.Range(-radius, radius * 2 + 1)
            .SelectMany(dx => Enumerable.Range(-radius, radius * 2 + 1)
                .Select(dz => new WorkCartScanColumn(dx, dz)))
            .Where(column => column.DistanceFromCart <= dynamicRadius * dynamicRadius)
            .OrderBy(column => column.DistanceFromCart)
            .ThenBy(column => column.Dx)
            .ThenBy(column => column.Dz)
            .ToList();
    }
}

internal readonly struct WorkCartScanColumn
{
    public WorkCartScanColumn(int dx, int dz)
    {
        Dx = dx;
        Dz = dz;
        DistanceFromCart = dx * dx + dz * dz;
    }

    public int Dx { get; }
    public int Dz { get; }
    public int DistanceFromCart { get; }
}

internal sealed class WorkCartLoggingCandidate
{
    public BlockPos Root = null!;
    public List<WorkCartTreePart> BreakParts = new();
    public double DistanceFromCart;
}

internal enum WorkCartTreeDiscoveryStage
{
    FirstTraversal,
    CanonicalTraversal,
    Validating
}

internal sealed class WorkCartTreeDiscovery
{
    public WorkCartTreeDiscovery(BlockPos encounteredPart, BlockPos cartPos)
    {
        Traversal = new IncrementalWorkCartTreeTraversal(encounteredPart, cartPos);
    }

    public WorkCartTreeDiscoveryStage Stage = WorkCartTreeDiscoveryStage.FirstTraversal;
    public IncrementalWorkCartTreeTraversal Traversal;
    public BlockPos? Root;
    public int ValidationIndex;
    public bool Invalid;
    public List<WorkCartTreePart> ValidatedParts { get; } = new();
}

internal sealed class IncrementalWorkCartTreeTraversal
{
    private const int LeafGroups = 7;
    private readonly Queue<WorkCartTreeTraversalNode> woodQueue = new();
    private readonly Queue<WorkCartTreeTraversalNode> leafQueue = new();
    private readonly HashSet<BlockPos> checkedPositions = new();
    private readonly int[] adjacentLeafGroupsCounts = new int[LeafGroups];
    private readonly BlockPos cartPos;
    private int initialSpreadIndex;
    private string treeFellingGroupCode = string.Empty;
    private EnumTreeFellingBehavior behavior = EnumTreeFellingBehavior.Chop;
    private bool processingLeaves;

    public IncrementalWorkCartTreeTraversal(BlockPos startPos, BlockPos cartPos)
    {
        StartPos = startPos.Copy();
        this.cartPos = cartPos.Copy();
        // The actual block and felling metadata are read when Advance runs so
        // construction itself stays O(1) in an AI-adjacent call path.
        initialSpreadIndex = -1;
    }

    public BlockPos StartPos { get; }
    public List<BlockPos> FoundPositions { get; } = new();
    public BlockPos? CanonicalRoot { get; private set; }
    public bool Initialized { get; private set; }
    public bool Complete { get; private set; }
    public bool Invalid { get; private set; }

    public int Advance(IWorldAccessor world, int nodeBudget, int maximumParts)
    {
        if (Complete || Invalid || nodeBudget <= 0) return 0;
        if (!Initialized && !TryInitialize(world))
        {
            Invalid = true;
            Complete = true;
            return 0;
        }

        int used = 0;
        while (used < nodeBudget)
        {
            if (!processingLeaves && woodQueue.Count == 0)
            {
                SelectLeafGroup();
                processingLeaves = true;
            }

            Queue<WorkCartTreeTraversalNode> queue = processingLeaves ? leafQueue : woodQueue;
            if (queue.Count == 0)
            {
                Complete = true;
                break;
            }

            WorkCartTreeTraversalNode node = queue.Dequeue();
            used++;
            if (world.BlockAccessor.GetChunkAtBlockPos(node.Position) == null)
            {
                Invalid = true;
                Complete = true;
                break;
            }

            Block block = world.BlockAccessor.GetBlock(node.Position, BlockLayersAccess.Solid);
            if (block?.Code == null)
            {
                Invalid = true;
                Complete = true;
                break;
            }
            FoundPositions.Add(node.Position.Copy());
            if (FoundPositions.Count > maximumParts)
            {
                Invalid = true;
                Complete = true;
                break;
            }
            if (block.BlockMaterial == EnumBlockMaterial.Wood && IsBetterRoot(node.Position))
            {
                CanonicalRoot = node.Position.Copy();
            }

            if (block is ICustomTreeFellingBehavior custom)
            {
                behavior = custom.GetTreeFellingBehavior(StartPos, null, initialSpreadIndex);
            }
            if (behavior == EnumTreeFellingBehavior.NoChop) continue;
            AddNeighbours(
                world.BlockAccessor,
                node,
                behavior == EnumTreeFellingBehavior.ChopSpreadVertical,
                processingLeaves);
        }
        return used;
    }

    private bool TryInitialize(IWorldAccessor world)
    {
        Block block = world.BlockAccessor.GetBlock(StartPos, BlockLayersAccess.Solid);
        if (block?.Code == null
            || block.Attributes?["treeFellingCanChop"].AsBool(true) == false)
        {
            return false;
        }

        string? groupCode = block.Attributes?["treeFellingGroupCode"].AsString();
        int spreadIndex = block.Attributes?["treeFellingGroupSpreadIndex"].AsInt(0) ?? 0;
        if (spreadIndex < 2 || string.IsNullOrWhiteSpace(groupCode)) return false;

        behavior = EnumTreeFellingBehavior.Chop;
        if (block is ICustomTreeFellingBehavior custom)
        {
            behavior = custom.GetTreeFellingBehavior(StartPos, null, spreadIndex);
            if (behavior == EnumTreeFellingBehavior.NoChop) return false;
        }

        treeFellingGroupCode = groupCode;
        woodQueue.Enqueue(new WorkCartTreeTraversalNode(StartPos.Copy(), spreadIndex));
        checkedPositions.Add(StartPos.Copy());
        initialSpreadIndex = spreadIndex;
        Initialized = true;
        return true;
    }

    private void AddNeighbours(
        IBlockAccessor blocks,
        WorkCartTreeTraversalNode node,
        bool chopSpreadVertical,
        bool leafPass)
    {
        foreach (Vec3i facing in Vec3i.DirectAndIndirectNeighbours)
        {
            BlockPos neighbor = new(
                node.Position.X + facing.X,
                node.Position.Y + facing.Y,
                node.Position.Z + facing.Z,
                node.Position.dimension);
            float horizontalDistance = GameMath.Sqrt(neighbor.HorDistanceSqTo(StartPos.X, StartPos.Z));
            float verticalDistance = neighbor.Y - StartPos.Y;
            float spreadShape = chopSpreadVertical ? 0.5f : 2f;
            if (horizontalDistance - 1f >= spreadShape * verticalDistance
                || checkedPositions.Contains(neighbor)) continue;

            Block block = blocks.GetBlock(neighbor, BlockLayersAccess.Solid);
            if (block?.Code == null || block.Id == 0) continue;
            string? neighborGroup = block.Attributes?["treeFellingGroupCode"].AsString();
            Queue<WorkCartTreeTraversalNode> targetQueue;
            if (!string.Equals(neighborGroup, treeFellingGroupCode, StringComparison.Ordinal))
            {
                if (leafPass
                    || string.IsNullOrEmpty(neighborGroup)
                    || block.BlockMaterial != EnumBlockMaterial.Leaves
                    || neighborGroup.Length != treeFellingGroupCode.Length + 1
                    || !neighborGroup.EndsWith(treeFellingGroupCode, StringComparison.Ordinal)) continue;
                targetQueue = leafQueue;
                int leafGroup = GameMath.Clamp(neighborGroup[0] - '0', 1, LeafGroups);
                adjacentLeafGroupsCounts[leafGroup - 1]++;
            }
            else
            {
                targetQueue = leafPass ? leafQueue : woodQueue;
            }

            int nextSpreadIndex = block.Attributes?["treeFellingGroupSpreadIndex"].AsInt(0) ?? 0;
            if (node.SpreadIndex < nextSpreadIndex) continue;
            checkedPositions.Add(neighbor.Copy());
            if (chopSpreadVertical && !(facing.X == 0 && facing.Y == 1 && facing.Z == 0)
                && nextSpreadIndex > 0) continue;
            targetQueue.Enqueue(new WorkCartTreeTraversalNode(neighbor, nextSpreadIndex));
        }
    }

    private void SelectLeafGroup()
    {
        int largest = 0;
        int selected = -1;
        for (int index = 0; index < adjacentLeafGroupsCounts.Length; index++)
        {
            if (adjacentLeafGroupsCounts[index] <= largest) continue;
            largest = adjacentLeafGroupsCounts[index];
            selected = index;
        }
        if (selected >= 0) treeFellingGroupCode = (selected + 1) + treeFellingGroupCode;
    }

    private bool IsBetterRoot(BlockPos candidate)
    {
        if (CanonicalRoot == null) return true;
        if (candidate.Y != CanonicalRoot.Y) return candidate.Y < CanonicalRoot.Y;
        return candidate.HorDistanceSqTo(cartPos.X, cartPos.Z)
            < CanonicalRoot.HorDistanceSqTo(cartPos.X, cartPos.Z);
    }
}

internal readonly struct WorkCartTreeTraversalNode
{
    public WorkCartTreeTraversalNode(BlockPos position, int spreadIndex)
    {
        Position = position;
        SpreadIndex = spreadIndex;
    }

    public BlockPos Position { get; }
    public int SpreadIndex { get; }
}

internal sealed class WorkCartTreePart
{
    public WorkCartTreePart(BlockPos position, int expectedBlockId)
    {
        Position = position;
        ExpectedBlockId = expectedBlockId;
    }

    public BlockPos Position { get; }
    public int ExpectedBlockId { get; }
}
