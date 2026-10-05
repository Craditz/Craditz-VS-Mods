#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// Incremental, cart-owned discovery for ordinary chores. A cart scans each
/// column once and hands cached candidates to workers; task starts never do a
/// full-radius block scan. This deliberately mirrors logging's bounded queue
/// ownership while remaining independently disposable if a chore misbehaves.
/// </summary>
public sealed partial class FeralKinshipCompanionSystem
{
    private const int WorkCartChoreColumnsPerTick = 6;
    private const int WorkCartChoreCandidateLimit = 768;
    private const long WorkCartChoreRescanMs = 15000;
    private const long WorkCartDroppedItemRefreshMs = 1000;
    private readonly Dictionary<string, WorkCartChoreQueue> workCartChoreQueues = new(StringComparer.Ordinal);
    private int workCartChoreRoundRobinOffset;

    private void MaintainWorkCartChoreQueues()
    {
        if (serverApi == null || packRepository?.Loaded != true) return;
        if (packRepository.WorkCartCount == 0 && workCartChoreQueues.Count == 0) return;

        List<FoxWorkCartRecord> carts = packRepository.GetAllWorkCarts()
            .Where(cart => cart.AssignedFoxIds.Count > 0)
            .OrderBy(cart => WorkCartPositionKey(new BlockPos(cart.X, cart.Y, cart.Z, cart.Dimension)))
            .ToList();
        HashSet<string> active = new(StringComparer.Ordinal);
        int columns = WorkCartChoreColumnsPerTick;
        int droppedRefreshes = 1;
        int start = carts.Count == 0 ? 0 : workCartChoreRoundRobinOffset++ % carts.Count;

        for (int offset = 0; offset < carts.Count; offset++)
        {
            FoxWorkCartRecord cart = carts[(start + offset) % carts.Count];
            BlockPos cartPos = new(cart.X, cart.Y, cart.Z, cart.Dimension);
            if (serverApi.World.BlockAccessor.GetChunkAtBlockPos(cartPos) == null
                || !IsWorkCartCode(serverApi.World.BlockAccessor.GetBlock(cartPos).Code)) continue;

            string key = WorkCartPositionKey(cartPos);
            active.Add(key);
            float radius = GetOwnerWorkCartRadius(cart.OwnerUid);
            if (!workCartChoreQueues.TryGetValue(key, out WorkCartChoreQueue? queue)
                || Math.Abs(queue.Radius - radius) > 0.01f)
            {
                queue = new WorkCartChoreQueue(cartPos, radius);
                workCartChoreQueues[key] = queue;
            }

            RefreshWorkCartDroppedItems(queue, ref droppedRefreshes);
            AdvanceWorkCartChoreScan(queue, ref columns);
        }

        foreach (string stale in workCartChoreQueues.Keys.Where(key => !active.Contains(key)).ToArray())
        {
            workCartChoreQueues.Remove(stale);
        }
    }

    private void AdvanceWorkCartChoreScan(WorkCartChoreQueue queue, ref int budget)
    {
        if (serverApi == null || budget <= 0 || queue.NextScanAtMs > serverApi.World.ElapsedMilliseconds) return;
        if (queue.ScanIndex >= queue.Columns.Count)
        {
            // Refresh the whole snapshot on schedule even when an unselected
            // chore category still has candidates. Otherwise disabled flower
            // work, for example, could prevent harvest discovery forever.
            queue.Reset(0);
        }

        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        while (budget > 0 && queue.ScanIndex < queue.Columns.Count)
        {
            budget--;
            WorkCartScanColumn column = queue.Columns[queue.ScanIndex++];
            BlockPos probe = new(queue.CartPos.X + column.Dx, queue.CartPos.Y,
                queue.CartPos.Z + column.Dz, queue.CartPos.dimension);
            int terrain = blocks.GetTerrainMapheightAt(probe);
            int rain = blocks.GetRainMapHeightAt(probe);
            int minY = Math.Min(terrain, rain) - 8;
            int maxY = Math.Max(terrain, rain) + 8;
            for (int y = minY; y <= maxY; y++)
            {
                BlockPos pos = new(probe.X, y, probe.Z, probe.dimension);
                Block? block = blocks.GetBlock(pos);
                if (block == null || block.Id == 0) continue;
                if (FeralKinshipNaturalCleanup.IsEligible(block)) queue.Add(queue.Natural, pos);
                if (IsFinishedProductBlock(block, pos)) queue.Add(queue.Harvest, pos);
                if (IsFlowerRemovalBlock(block)) queue.Add(queue.Flowers, pos);
                if (IsSnowShovelableBlock(block)) queue.Add(queue.Snow, pos);
                // Vanilla charcoal piles are vertical stacks. Queue only the
                // highest contiguous block so a cart never sends a companion
                // to break a buried layer first.
                if (IsCharcoalPileBlock(block)
                    && !IsCharcoalPileBlock(blocks.GetBlock(pos.UpCopy())))
                {
                    queue.Add(queue.Charcoal, pos);
                }
                if (AiTaskFeralKinshipMowGrass.IsMowableGrass(block)) queue.Add(queue.Mowing, pos);
            }
        }

        if (queue.ScanIndex >= queue.Columns.Count)
        {
            queue.NextScanAtMs = serverApi.World.ElapsedMilliseconds + WorkCartChoreRescanMs;
        }
    }

    private void RefreshWorkCartDroppedItems(WorkCartChoreQueue queue, ref int budget)
    {
        if (serverApi == null || budget <= 0
            || queue.NextDroppedRefreshAtMs > serverApi.World.ElapsedMilliseconds) return;
        budget--;
        Vec3d center = new(queue.CartPos.X + 0.5,
            queue.CartPos.Y + queue.CartPos.dimension * BlockPos.DimensionBoundary + 0.5,
            queue.CartPos.Z + 0.5);
        queue.DroppedItems = serverApi.World.GetEntitiesAround(
                center, queue.Radius + 8f, serverApi.World.BlockAccessor.MapSizeY,
                candidate => candidate is EntityItem item && item.Alive
                    && item.Pos.Dimension == queue.CartPos.dimension
                    && IsInsideWorkCartHorizontalRadius(item, center, queue.Radius + 8f)
                    && item.Itemstack != null && item.Itemstack.StackSize > 0
                    && !CompanionPickupPolicy.IsCorpse(item.Itemstack.Collectible?.GetType())
                    && serverApi.World.ElapsedMilliseconds - item.itemSpawnedMilliseconds >= 2500)
            .OfType<EntityItem>()
            .OrderBy(item => IsLoggingDebris(item.Itemstack) ? 0 : 1)
            .ThenBy(item => item.Pos.SquareDistanceTo(center))
            .Take(WorkCartChoreCandidateLimit)
            .Select(item => item.EntityId)
            .ToList();
        queue.NextDroppedRefreshAtMs = serverApi.World.ElapsedMilliseconds + WorkCartDroppedItemRefreshMs;
    }

    private bool TryGetWorkCartChoreQueue(Entity fox, out WorkCartChoreQueue? queue)
    {
        queue = null;
        if (!TryGetAssignedWorkCart(fox, requireLogging: false, out _, out BlockPos? cartPos)
            || cartPos == null) return false;
        return workCartChoreQueues.TryGetValue(WorkCartPositionKey(cartPos), out queue);
    }

    private bool HasQueuedWorkCartChore(Entity fox, WorkCartChoreKind kind)
    {
        if (!TryGetWorkCartChoreQueue(fox, out WorkCartChoreQueue? queue) || queue == null) return false;
        return kind switch
        {
            WorkCartChoreKind.Natural => IsGroundCleanupEnabled(fox)
                && queue.Natural.Any(pos =>
                    IsNaturalCleanupOptionEnabled(fox,
                        FeralKinshipNaturalCleanup.GetPriority(serverApi!.World.BlockAccessor.GetBlock(pos))))
                || ((IsGroundDroppedItemsEnabled(fox) || IsWorkCartLoggingCleanupEnabled(fox))
                    && queue.DroppedItems.Count > 0),
            WorkCartChoreKind.Harvest => queue.Harvest.Any(pos =>
                IsFinishedProductOptionEnabled(fox, serverApi!.World.BlockAccessor.GetBlock(pos))),
            WorkCartChoreKind.Flowers => IsFlowerRemovalEnabled(fox) && queue.Flowers.Count > 0,
            WorkCartChoreKind.Snow => IsSnowShovelingEnabled(fox) && queue.Snow.Count > 0,
            WorkCartChoreKind.Charcoal => IsCharcoalShovelingEnabled(fox) && queue.Charcoal.Count > 0,
            WorkCartChoreKind.Mowing => queue.Mowing.Count > 0,
            _ => false
        };
    }

    /// <summary>
    /// Mowing must not begin while a work cart can still discover or already
    /// has a higher-priority cleanup target. This is checked by the mowing AI
    /// before it selects a target, rather than only when the target is claimed.
    /// </summary>
    internal bool ShouldDeferWorkCartMowing(Entity fox)
    {
        if (!IsFoxAssignedToWorkCart(fox)) return false;

        if (!TryGetWorkCartChoreQueue(fox, out WorkCartChoreQueue? queue) || queue == null)
        {
            // The cart coordinator has not published its first snapshot yet.
            // Wait instead of allowing the independent local mowing task to
            // get ahead of higher-priority cleanup.
            return true;
        }

        if (queue.ScanIndex < queue.Columns.Count) return true;
        return HasQueuedWorkCartChore(fox, WorkCartChoreKind.Natural)
            || HasQueuedWorkCartChore(fox, WorkCartChoreKind.Charcoal)
            || HasQueuedWorkCartChore(fox, WorkCartChoreKind.Harvest)
            || HasQueuedWorkCartChore(fox, WorkCartChoreKind.Flowers)
            || HasQueuedWorkCartChore(fox, WorkCartChoreKind.Snow);
    }

    private EntityItem? TryClaimQueuedWorkCartDroppedItem(Entity fox)
    {
        if (serverApi == null || !TryGetWorkCartChoreQueue(fox, out WorkCartChoreQueue? queue) || queue == null) return null;
        for (int index = 0; index < queue.DroppedItems.Count;)
        {
            long id = queue.DroppedItems[index];
            EntityItem? item = serverApi.World.GetEntityById(id) as EntityItem;
            if (item == null || !item.Alive || item.Itemstack == null || item.Itemstack.StackSize <= 0
                || CompanionPickupPolicy.IsCorpse(item.Itemstack.Collectible?.GetType())
                || (droppedItemReservations.TryGetValue(id, out long reservedBy) && reservedBy != fox.EntityId))
            {
                queue.DroppedItems.RemoveAt(index);
                continue;
            }
            queue.DroppedItems.RemoveAt(index);
            droppedItemReservations[id] = fox.EntityId;
            return item;
        }
        return null;
    }

    private bool TryClaimQueuedWorkCartBlock(Entity fox, WorkCartChoreKind kind,
        out BlockPos? target, out Vec3d? approach)
    {
        target = null;
        approach = null;
        if (serverApi == null || !TryGetWorkCartChoreQueue(fox, out WorkCartChoreQueue? queue) || queue == null) return false;
        // Mowing is the lowest-priority cart chore, but the cart discovers
        // natural debris and grass in the same incremental scan. Do not let a
        // grass target start before the scan has finished: otherwise a fox can
        // mow a discovered patch while an undiscovered cattail (or other
        // higher-priority natural cleanup) is still waiting farther along the
        // cart's radius. This also gates mowing during each scheduled rescan.
        if (kind == WorkCartChoreKind.Mowing && queue.ScanIndex < queue.Columns.Count) return false;
        List<BlockPos> candidates = queue.For(kind);
        IEnumerable<BlockPos> ordered = kind == WorkCartChoreKind.Natural
            ? candidates.OrderBy(pos => FeralKinshipNaturalCleanup.GetPriority(serverApi.World.BlockAccessor.GetBlock(pos)))
            : kind == WorkCartChoreKind.Snow
                ? candidates.OrderBy(GetSnowLogisticsPriority)
                : candidates;

        foreach (BlockPos candidate in ordered.ToArray())
        {
            BlockPos effectiveCandidate = candidate;
            if (kind == WorkCartChoreKind.Charcoal
                && !TryResolveCharcoalTopTarget(candidate, out effectiveCandidate))
            {
                candidates.Remove(candidate);
                continue;
            }

            Block? block = serverApi.World.BlockAccessor.GetBlock(effectiveCandidate);
            bool stillValid = kind switch
            {
                WorkCartChoreKind.Natural => FeralKinshipNaturalCleanup.IsEligible(block),
                WorkCartChoreKind.Harvest => IsFinishedProductBlock(block, effectiveCandidate),
                WorkCartChoreKind.Flowers => IsFlowerRemovalBlock(block),
                WorkCartChoreKind.Snow => IsSnowShovelableBlock(block),
                WorkCartChoreKind.Charcoal => IsCharcoalPileBlock(block),
                WorkCartChoreKind.Mowing => AiTaskFeralKinshipMowGrass.IsMowableGrass(block),
                _ => false
            };
            if (!stillValid)
            {
                candidates.Remove(candidate);
                continue;
            }

            bool enabledForFox = kind switch
            {
                WorkCartChoreKind.Natural => IsNaturalCleanupOptionEnabled(
                    fox, FeralKinshipNaturalCleanup.GetPriority(block)),
                WorkCartChoreKind.Harvest => IsFinishedProductOptionEnabled(fox, block),
                WorkCartChoreKind.Charcoal => IsCharcoalShovelingEnabled(fox),
                _ => true
            };
            if (!enabledForFox
                || (kind == WorkCartChoreKind.Snow && !IsSnowTargetAvailable(fox, effectiveCandidate))
                || (kind == WorkCartChoreKind.Charcoal && !IsCharcoalTargetAvailable(fox, effectiveCandidate))
                || !CanCompanionModifyBlock(fox, effectiveCandidate)
                || !TryGetNaturalCleanupApproachTarget(fox, effectiveCandidate, out Vec3d? candidateApproach,
                    kind == WorkCartChoreKind.Snow ? value => IsSnowApproachAvailable(fox, value) : null)
                || candidateApproach == null)
            {
                continue;
            }

            if (kind == WorkCartChoreKind.Charcoal && !TryReserveCharcoalTarget(fox, effectiveCandidate))
            {
                continue;
            }

            candidates.Remove(candidate);
            target = effectiveCandidate.Copy();
            approach = candidateApproach;
            return true;
        }
        return false;
    }

    internal bool TryClaimQueuedWorkCartMowingTarget(Entity fox, out BlockPos? target, out Vec3d? approach)
        => TryClaimQueuedWorkCartBlock(fox, WorkCartChoreKind.Mowing, out target, out approach);

    private enum WorkCartChoreKind { Natural, Harvest, Flowers, Snow, Charcoal, Mowing }

    private sealed class WorkCartChoreQueue
    {
        public WorkCartChoreQueue(BlockPos cartPos, float radius)
        {
            CartPos = cartPos.Copy();
            Radius = radius;
            int edge = (int)Math.Ceiling(radius);
            Columns = Enumerable.Range(-edge, edge * 2 + 1)
                .SelectMany(dx => Enumerable.Range(-edge, edge * 2 + 1)
                    .Select(dz => new WorkCartScanColumn(dx, dz)))
                .Where(column => column.DistanceFromCart <= radius * radius)
                .OrderBy(column => column.DistanceFromCart).ToList();
        }
        public BlockPos CartPos { get; }
        public float Radius { get; }
        public List<WorkCartScanColumn> Columns { get; }
        public List<BlockPos> Natural { get; } = new();
        public List<BlockPos> Harvest { get; } = new();
        public List<BlockPos> Flowers { get; } = new();
        public List<BlockPos> Snow { get; } = new();
        public List<BlockPos> Charcoal { get; } = new();
        public List<BlockPos> Mowing { get; } = new();
        public List<long> DroppedItems { get; set; } = new();
        public int ScanIndex { get; set; }
        public long NextScanAtMs { get; set; }
        public long NextDroppedRefreshAtMs { get; set; }
        public bool HasCandidates => Natural.Count + Harvest.Count + Flowers.Count + Snow.Count + Charcoal.Count + Mowing.Count > 0;
        public void Add(List<BlockPos> list, BlockPos pos)
        {
            if (list.Count < WorkCartChoreCandidateLimit) list.Add(pos.Copy());
        }
        public List<BlockPos> For(WorkCartChoreKind kind) => kind switch
        {
            WorkCartChoreKind.Natural => Natural,
            WorkCartChoreKind.Harvest => Harvest,
            WorkCartChoreKind.Flowers => Flowers,
            WorkCartChoreKind.Snow => Snow,
            WorkCartChoreKind.Charcoal => Charcoal,
            WorkCartChoreKind.Mowing => Mowing,
            _ => Natural
        };
        public void Reset(long nextScanAtMs)
        {
            Natural.Clear(); Harvest.Clear(); Flowers.Clear(); Snow.Clear(); Charcoal.Clear(); Mowing.Clear();
            ScanIndex = 0;
            NextScanAtMs = nextScanAtMs;
        }
    }
}
