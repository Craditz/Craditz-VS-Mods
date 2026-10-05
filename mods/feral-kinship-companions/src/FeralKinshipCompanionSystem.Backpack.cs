#nullable enable

using System;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using System.Collections.Generic;
using System.Linq;

namespace FeralKinshipCompanions;

public enum CompanionBackpackAction
{
    Open = 0,
    Close = 1,
    UnloadAtHome = 2,
    UnloadAndReturn = 3
}

[ProtoContract]
public sealed class CompanionBackpackRequestPacket
{
    [ProtoMember(1)] public long TargetEntityId { get; set; }
    [ProtoMember(2)] public CompanionBackpackAction Action { get; set; }
}

[ProtoContract]
public sealed class CompanionBackpackStatePacket
{
    [ProtoMember(1)] public long TargetEntityId { get; set; }
    [ProtoMember(2)] public byte[] BackpackItem { get; set; } = Array.Empty<byte>();
    [ProtoMember(3)] public bool Locked { get; set; }
    [ProtoMember(4)] public string Title { get; set; } = "Companion backpack";
    [ProtoMember(5)] public bool Reopen { get; set; }
}

public sealed partial class FeralKinshipCompanionSystem
{
    private readonly Dictionary<string, long> backpackViewByOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<long, BackpackPreflight> backpackPreflights = new();
    private readonly Dictionary<string, long> backpackChunkWakeRetryAtUtcMs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> backpackDiagnosticThrottleAtUtcMs = new(StringComparer.Ordinal);
    private readonly HashSet<string> backpackMissingEntityRecoveryChecks = new(StringComparer.Ordinal);
    private readonly HashSet<string> backpackStorageAreaLoads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> backpackStorageAreaLoadDeadlineUtcMs = new(StringComparer.Ordinal);
    internal const string CompanionBackpackItemKey = "feralKinshipCompanionBackpack";
    internal const string BackpackDeliveryActiveKey = "feralKinshipBackpackDeliveryActive";
    internal const string BackpackDeliveryPhaseKey = "feralKinshipBackpackDeliveryPhase";
    private const string BackpackReturnToPlayerKey = "feralKinshipBackpackReturnToPlayer";
    private const string BackpackPreviousActivityKey = "feralKinshipBackpackPreviousActivity";
    private const string BackpackPhaseStartedUtcMsKey = "feralKinshipBackpackPhaseStartedUtcMs";
    private const string BackpackReturnDueUtcMsKey = "feralKinshipBackpackReturnDueUtcMs";
    private const string BackpackReturnRetryUntilUtcMsKey = "feralKinshipBackpackReturnRetryUntilUtcMs";
    private const string BackpackReturnSnapshotTakenKey = "feralKinshipBackpackReturnSnapshotTaken";
    private const string BackpackCartXKey = "feralKinshipBackpackCartX";
    private const string BackpackCartYKey = "feralKinshipBackpackCartY";
    private const string BackpackCartZKey = "feralKinshipBackpackCartZ";
    private const string BackpackCartDimensionKey = "feralKinshipBackpackCartDimension";
    private const string BackpackStorageXKey = "feralKinshipBackpackStorageX";
    private const string BackpackStorageYKey = "feralKinshipBackpackStorageY";
    private const string BackpackStorageZKey = "feralKinshipBackpackStorageZ";
    private const string BackpackUnloadAccessFailedKey = "feralKinshipBackpackUnloadAccessFailed";
    private const string BackpackDeliveryStartingItemCountKey = "feralKinshipBackpackDeliveryStartingItemCount";
    private const string BackpackDeliveryMovedItemCountKey = "feralKinshipBackpackDeliveryMovedItemCount";
    private const string BackpackLastDeliverySummaryKey = "feralKinshipBackpackLastDeliverySummary";
    private const long BackpackHomeTimeoutMs = 60_000;
    private const long BackpackUnloadTimeoutMs = 30_000;
    private const long BackpackReturnPlacementTimeoutMs = 60_000;
    private const long BackpackChunkWakeRetryMs = 5_000;
    private const long BackpackStorageAreaLoadTimeoutMs = 30_000;
    private const int BackpackMissingEntityMaximumChecks = 20;
    private const int BackpackMissingEntityCheckIntervalMs = 500;

    private sealed class BackpackPreflight
    {
        internal required string OwnerUid { get; init; }
        internal required bool ReturnToPlayer { get; init; }
        internal required long DeadlineUtcMs { get; init; }
    }

    internal void RequestOpenCompanionBackpack(long entityId)
    {
        SendCompanionBackpackAction(entityId, CompanionBackpackAction.Open);
    }

    internal void SendCompanionBackpackAction(long entityId, CompanionBackpackAction action)
    {
        clientChannel?.SendPacket(new CompanionBackpackRequestPacket
        {
            TargetEntityId = entityId,
            Action = action
        });
    }

    private void OnCompanionBackpackRequest(
        IServerPlayer fromPlayer,
        CompanionBackpackRequestPacket packet)
    {
        if (serverApi?.World.GetEntityById(packet.TargetEntityId) is not Entity entity)
        {
            if (packet.Action == CompanionBackpackAction.Close
                && backpackViewByOwner.Remove(fromPlayer.PlayerUID, out long staleEntityId))
            {
                EndMenuAttention(staleEntityId);
            }
            return;
        }

        EntityBehaviorFeralKinshipFoxSocial? behavior =
            entity.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>();
        if (behavior == null) return;

        if (packet.Action == CompanionBackpackAction.Close)
        {
            behavior.CloseCompanionBackpackServer(fromPlayer);
            backpackViewByOwner.Remove(fromPlayer.PlayerUID);
            EndMenuAttention(entity.EntityId);
            return;
        }

        if (!CanPlayerUseCompanionBackpack(fromPlayer, entity)) return;

        if (packet.Action == CompanionBackpackAction.Open)
        {
            behavior.OpenCompanionBackpackServer(fromPlayer);
            if (!IsBackpackDeliveryActive(entity))
            {
                backpackViewByOwner[fromPlayer.PlayerUID] = entity.EntityId;
                BeginMenuAttention(entity, fromPlayer.Entity);
            }
            SendCompanionBackpackState(fromPlayer, entity, reopen: false);
            return;
        }

        if (!behavior.HasCompanionBackpack
            || IsBackpackDeliveryActive(entity))
        {
            return;
        }

        StartBackpackDelivery(
            fromPlayer,
            entity,
            returnToPlayer: packet.Action == CompanionBackpackAction.UnloadAndReturn);
    }

    internal bool CanPlayerUseCompanionBackpack(IServerPlayer player, Entity entity)
    {
        return entity.Alive
            && IsTamedFox(entity)
            && !IsCompanionJuvenile(entity)
            && !IsFoxIncapacitated(entity)
            && string.Equals(GetCompanionOwnerUid(entity), player.PlayerUID, StringComparison.Ordinal)
            && entity.Pos.Dimension == player.Entity.Pos.Dimension
            && entity.Pos.SquareDistanceTo(player.Entity.Pos.XYZ) <= SocialActionRangeSquared;
    }

    internal void SendCompanionBackpackState(
        IServerPlayer player,
        Entity entity,
        bool reopen)
    {
        EntityBehaviorFeralKinshipFoxSocial? behavior =
            entity.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>();
        ItemStack? stack = behavior?.CompanionBackpackStack;
        serverChannel?.SendPacket(new CompanionBackpackStatePacket
        {
            TargetEntityId = entity.EntityId,
            BackpackItem = stack?.ToBytes() ?? Array.Empty<byte>(),
            Locked = IsBackpackDeliveryActive(entity),
            Title = $"{GetFoxDisplayName(entity)}'s backpack",
            Reopen = reopen
        }, player);
    }

    private void OnCompanionBackpackState(CompanionBackpackStatePacket packet)
    {
        if (clientApi?.World.GetEntityById(packet.TargetEntityId) is not Entity entity) return;
        ItemStack? stack = null;
        if (packet.BackpackItem?.Length > 0)
        {
            stack = new ItemStack(packet.BackpackItem);
            stack.ResolveBlockOrItem(clientApi.World);
        }
        entity.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?
            .OpenCompanionBackpackClient(stack, packet.Locked, packet.Title);
    }

    internal void OnCompanionBackpackChanged(Entity entity)
    {
        if (entity.Api.Side != EnumAppSide.Server) return;
        PersistCompanionBackpack(entity);
        packRepository?.Save();
    }

    internal static bool IsBackpackDeliveryActive(Entity entity) =>
        entity.WatchedAttributes.GetBool(BackpackDeliveryActiveKey, false);

    private void RefuseBackpackDelivery(IServerPlayer owner, Entity companion, string reason)
    {
        LogBackpackDelivery(companion, "job-refused", $"reason={reason}");
        serverChannel?.SendPacket(new CompanionThoughtPacket
        {
            TargetEntityId = companion.EntityId,
            Text = "There’s nowhere at home to put this.",
            DurationMs = 5000
        }, owner);
        owner.SendMessage(
            GlobalConstants.GeneralChatGroup,
            $"{GetFoxDisplayName(companion)} cannot unload: {reason}",
            EnumChatType.Notification);
    }

    private void RefuseEmptyBackpackDelivery(IServerPlayer owner, Entity companion)
    {
        LogBackpackDelivery(companion, "job-refused", "reason=backpack is empty");
        serverChannel?.SendPacket(new CompanionThoughtPacket
        {
            TargetEntityId = companion.EntityId,
            Text = "My backpack is empty.",
            DurationMs = 4000
        }, owner);
        owner.SendMessage(
            GlobalConstants.GeneralChatGroup,
            $"{GetFoxDisplayName(companion)} has nothing in the backpack to unload.",
            EnumChatType.Notification);
    }

    private void StartBackpackDelivery(IServerPlayer owner, Entity companion, bool returnToPlayer)
    {
        if (!CompanionBackpackHasCargo(companion))
        {
            RefuseEmptyBackpackDelivery(owner, companion);
            return;
        }

        BlockPos? cart = GetActiveCairnPosition(owner.PlayerUID, discoverNearby: true);
        if (cart == null)
        {
            RefuseBackpackDelivery(owner, companion,
                "there is no usable General storage at the active Pack Cart.");
            return;
        }
        if (!TryFindGeneralBackpackStorage(companion, cart, null, requireCapacity: false, out _))
        {
            if (RequestPendingGeneralBackpackStorageChunks(companion, cart))
            {
                QueueBackpackPreflight(owner, companion, returnToPlayer);
                return;
            }
            RefuseBackpackDelivery(owner, companion,
                "there is no usable General storage at the active Pack Cart.");
            return;
        }

        EntityBehaviorFeralKinshipFoxSocial? behavior =
            companion.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>();
        if (behavior?.HasCompanionBackpack != true) return;

        ITreeAttribute status = GetDomesticationStatus(companion, true)!;
        string previousActivity = GetCompanionActivityMode(companion);
        behavior.CloseCompanionBackpackServer(owner);
        backpackViewByOwner.Remove(owner.PlayerUID);
        EndMenuAttention(companion.EntityId);
        ClearAutomaticRetreat(companion, restorePreviousActivity: false);
        CancelTargetedAttack(companion);
        DisengageHostileTargets(companion);

        status.SetString(BackpackPreviousActivityKey, previousActivity);
        status.SetBool(BackpackReturnToPlayerKey, returnToPlayer);
        status.SetString(BackpackDeliveryPhaseKey, "home");
        status.SetLong(BackpackPhaseStartedUtcMsKey, UtcNowMs());
        status.SetLong(BackpackReturnDueUtcMsKey, 0);
        status.SetLong(BackpackReturnRetryUntilUtcMsKey, 0);
        status.SetBool(BackpackReturnSnapshotTakenKey, false);
        status.SetBool(BackpackUnloadAccessFailedKey, false);
        status.SetInt(BackpackDeliveryStartingItemCountKey, GetCompanionBackpackCargoCount(companion));
        status.SetInt(BackpackDeliveryMovedItemCountKey, 0);
        status.SetInt(BackpackCartXKey, cart.X);
        status.SetInt(BackpackCartYKey, cart.Y);
        status.SetInt(BackpackCartZKey, cart.Z);
        status.SetInt(BackpackCartDimensionKey, cart.dimension);
        companion.WatchedAttributes.SetBool(BackpackDeliveryActiveKey, true);
        companion.WatchedAttributes.SetString(BackpackDeliveryPhaseKey, "home");
        companion.WatchedAttributes.MarkPathDirty(BackpackDeliveryActiveKey);
        companion.WatchedAttributes.MarkPathDirty(BackpackDeliveryPhaseKey);
        SetCompanionActivityState(companion, CompanionActivityMode.AtEase);
        behavior.RefreshCompanionBackpackLock();
        PersistCompanionBackpack(companion);
        packRepository?.Save();
        LogBackpackDelivery(companion, "job-started",
            $"returnToPlayer={returnToPlayer} cart={FormatBackpackPosition(cart)} previousActivity={previousActivity} "
            + $"cargoItems={GetCompanionBackpackCargoCount(companion)}");
        RefreshBackpackLedgerViewer(companion);
        owner.SendMessage(
            GlobalConstants.GeneralChatGroup,
            $"{GetFoxDisplayName(companion)} is heading home to unload.",
            EnumChatType.Notification);
    }

    private void PersistCompanionBackpack(Entity entity)
    {
        if (packRepository?.Loaded != true) return;
        ITreeAttribute? status = GetDomesticationStatus(entity);
        string foxId = status?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(foxId)
            || !packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
            || record == null) return;

        ItemStack? stack = entity.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.CompanionBackpackStack
            ?? entity.WatchedAttributes.GetItemstack(CompanionBackpackItemKey);
        record.BackpackItem = stack?.ToBytes() ?? Array.Empty<byte>();
        record.BackpackDeliveryActive = IsBackpackDeliveryActive(entity);
        record.BackpackDeliveryPhase = status?.GetString(BackpackDeliveryPhaseKey, string.Empty) ?? string.Empty;
        record.BackpackReturnToPlayer = status?.GetBool(BackpackReturnToPlayerKey, false) == true;
        record.BackpackPreviousActivity = status?.GetString(BackpackPreviousActivityKey, string.Empty) ?? string.Empty;
        record.BackpackPhaseStartedUtcMs = status?.GetLong(BackpackPhaseStartedUtcMsKey, 0) ?? 0;
        record.BackpackReturnDueUtcMs = status?.GetLong(BackpackReturnDueUtcMsKey, 0) ?? 0;
        record.BackpackReturnRetryUntilUtcMs = status?.GetLong(BackpackReturnRetryUntilUtcMsKey, 0) ?? 0;
        record.BackpackReturnSnapshotTaken = status?.GetBool(BackpackReturnSnapshotTakenKey, false) == true;
        record.BackpackCartX = status?.GetInt(BackpackCartXKey, 0) ?? 0;
        record.BackpackCartY = status?.GetInt(BackpackCartYKey, 0) ?? 0;
        record.BackpackCartZ = status?.GetInt(BackpackCartZKey, 0) ?? 0;
        record.BackpackCartDimension = status?.GetInt(BackpackCartDimensionKey, 0) ?? 0;
        record.BackpackStorageX = status?.GetInt(BackpackStorageXKey, 0) ?? 0;
        record.BackpackStorageY = status?.GetInt(BackpackStorageYKey, 0) ?? 0;
        record.BackpackStorageZ = status?.GetInt(BackpackStorageZKey, 0) ?? 0;
        record.BackpackUnloadAccessFailed = status?.GetBool(BackpackUnloadAccessFailedKey, false) == true;
        record.BackpackDeliveryStartingItemCount = status?.GetInt(BackpackDeliveryStartingItemCountKey, 0) ?? 0;
        record.BackpackDeliveryMovedItemCount = status?.GetInt(BackpackDeliveryMovedItemCountKey, 0) ?? 0;
        record.BackpackLastDeliverySummary = status?.GetString(
            BackpackLastDeliverySummaryKey,
            record.BackpackLastDeliverySummary) ?? string.Empty;
    }

    private void HydrateCompanionBackpackFromRecord(
        Entity entity,
        ITreeAttribute status,
        FoxPackRecordV2 record)
    {
        ItemStack? stack = null;
        if (record.BackpackItem?.Length > 0)
        {
            try
            {
                stack = new ItemStack(record.BackpackItem);
                if (!stack.ResolveBlockOrItem(entity.World)
                    || stack.Collectible?.GetCollectibleInterface<IHeldBag>() == null)
                {
                    stack = null;
                }
            }
            catch (Exception exception)
            {
                serverApi?.Logger.Error(
                    "[FeralKinshipCompanions] Could not restore backpack for {0}: {1}",
                    record.FoxId,
                    exception.Message);
            }
        }

        entity.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?
            .SetCompanionBackpackFromLedger(stack);

        status.SetString(BackpackLastDeliverySummaryKey, record.BackpackLastDeliverySummary ?? string.Empty);
        status.SetInt(BackpackDeliveryStartingItemCountKey, record.BackpackDeliveryStartingItemCount);
        status.SetInt(BackpackDeliveryMovedItemCountKey, record.BackpackDeliveryMovedItemCount);

        bool validJob = record.BackpackDeliveryActive
            && stack != null
            && record.BackpackDeliveryPhase is "home" or "unload" or "wait-return"
            && record.BackpackPhaseStartedUtcMs > 0;
        if (!validJob)
        {
            record.BackpackDeliveryActive = false;
            record.BackpackDeliveryPhase = string.Empty;
            entity.WatchedAttributes.SetBool(BackpackDeliveryActiveKey, false);
            entity.WatchedAttributes.SetString(BackpackDeliveryPhaseKey, string.Empty);
            entity.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.RefreshCompanionBackpackLock();
            return;
        }

        status.SetString(BackpackDeliveryPhaseKey, record.BackpackDeliveryPhase);
        status.SetBool(BackpackReturnToPlayerKey, record.BackpackReturnToPlayer);
        status.SetString(BackpackPreviousActivityKey, record.BackpackPreviousActivity);
        status.SetLong(BackpackPhaseStartedUtcMsKey, record.BackpackPhaseStartedUtcMs);
        status.SetLong(BackpackReturnDueUtcMsKey, record.BackpackReturnDueUtcMs);
        status.SetLong(BackpackReturnRetryUntilUtcMsKey, record.BackpackReturnRetryUntilUtcMs);
        status.SetBool(BackpackReturnSnapshotTakenKey, record.BackpackReturnSnapshotTaken);
        status.SetInt(BackpackCartXKey, record.BackpackCartX);
        status.SetInt(BackpackCartYKey, record.BackpackCartY);
        status.SetInt(BackpackCartZKey, record.BackpackCartZ);
        status.SetInt(BackpackCartDimensionKey, record.BackpackCartDimension);
        status.SetInt(BackpackStorageXKey, record.BackpackStorageX);
        status.SetInt(BackpackStorageYKey, record.BackpackStorageY);
        status.SetInt(BackpackStorageZKey, record.BackpackStorageZ);
        status.SetBool(BackpackUnloadAccessFailedKey, record.BackpackUnloadAccessFailed);
        entity.WatchedAttributes.SetBool(BackpackDeliveryActiveKey, true);
        entity.WatchedAttributes.SetString(BackpackDeliveryPhaseKey, record.BackpackDeliveryPhase);
        entity.WatchedAttributes.MarkPathDirty(BackpackDeliveryActiveKey);
        entity.WatchedAttributes.MarkPathDirty(BackpackDeliveryPhaseKey);
        entity.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.RefreshCompanionBackpackLock();
    }

    private bool TryFindGeneralBackpackStorage(
        Entity companion,
        BlockPos cart,
        ItemStack? stack,
        bool requireCapacity,
        out BlockPos? storage)
    {
        storage = null;
        if (serverApi == null || packRepository?.Loaded != true) return false;
        string ownerUid = GetCompanionOwnerUid(companion);
        double radius = GetOwnerCampRadius(ownerUid);
        var candidates = new List<(BlockPos Position, bool Contains, double Fullness, double Distance)>();
        foreach (FoxPackAmenityRecord record in packRepository.GetAmenitiesForOwner(ownerUid))
        {
            int mask = GetEffectiveStorageRoutingMask(record);
            if (record.Dimension != cart.dimension
                || !IsStorageRoutingEnabled(record)
                || !FoxStorageRouting.IsGeneral(record, mask)
                || !IsWithinStorageRoutingScope(record, cart, radius)) continue;

            BlockPos pos = new(record.X, record.Y, record.Z, record.Dimension);
            if (serverApi.World.BlockAccessor.GetChunkAtBlockPos(pos) == null)
            {
                continue;
            }
            Block? block = serverApi.World.BlockAccessor.GetBlock(pos);
            BlockEntity? blockEntity = serverApi.World.BlockAccessor.GetBlockEntity(pos);
            if (!FoxStorageRouting.IsSupportedStorageTarget(block, blockEntity)
                || blockEntity is not IBlockEntityContainer container
                || !TryGetFoxStorageApproachTarget(companion, pos, out _)) continue;
            IInventory? inventory;
            try { inventory = container.Inventory; }
            catch { continue; }
            if (inventory == null || (requireCapacity && stack != null && !CanInventoryAcceptOne(inventory, stack)))
            {
                continue;
            }
            bool contains = stack != null && InventoryContainsItem(inventory, stack);
            candidates.Add((
                pos,
                contains,
                GetInventoryFillRatio(inventory),
                companion.Pos.SquareDistanceTo(new Vec3d(
                    pos.X + 0.5,
                    pos.Y + pos.dimension * BlockPos.DimensionBoundary,
                    pos.Z + 0.5))));
        }
        if (candidates.Count == 0) return false;
        storage = candidates
            .OrderByDescending(candidate => candidate.Contains)
            .ThenByDescending(candidate => candidate.Fullness)
            .ThenBy(candidate => candidate.Distance)
            .First().Position;
        return true;
    }

    private void RequestBackpackStorageChunk(BlockPos pos)
    {
        if (serverApi == null) return;
        int chunkX = (int)Math.Floor(pos.X / (double)GlobalConstants.ChunkSize);
        int chunkZ = (int)Math.Floor(pos.Z / (double)GlobalConstants.ChunkSize);
        if (pos.dimension == 0)
        {
            serverApi.WorldManager.LoadChunkColumnPriority(
                chunkX,
                chunkZ,
                new ChunkLoadOptions { KeepLoaded = false });
        }
        else
        {
            serverApi.WorldManager.LoadChunkColumnForDimension(chunkX, chunkZ, pos.dimension);
        }
    }

    private bool RequestPendingGeneralBackpackStorageChunks(
        Entity companion,
        BlockPos cart,
        bool resumeUnloadWhenLoaded = false)
    {
        if (serverApi == null || packRepository?.Loaded != true) return false;
        string ownerUid = GetCompanionOwnerUid(companion);
        double radius = GetOwnerCampRadius(ownerUid);
        List<BlockPos> pendingPositions = new();
        foreach (FoxPackAmenityRecord record in packRepository.GetAmenitiesForOwner(ownerUid))
        {
            int mask = GetEffectiveStorageRoutingMask(record);
            if (record.Dimension != cart.dimension
                || !IsStorageRoutingEnabled(record)
                || !FoxStorageRouting.IsGeneral(record, mask)
                || !IsWithinStorageRoutingScope(record, cart, radius)) continue;
            BlockPos pos = new(record.X, record.Y, record.Z, record.Dimension);
            if (serverApi.World.BlockAccessor.GetChunkAtBlockPos(pos) != null) continue;
            pendingPositions.Add(pos);
        }
        if (pendingPositions.Count == 0) return false;
        if (!resumeUnloadWhenLoaded)
        {
            foreach (BlockPos position in pendingPositions)
            {
                RequestBackpackStorageChunk(position);
            }
            return true;
        }

        RequestBackpackStorageAreaLoad(companion, cart, radius, pendingPositions.Count);
        return true;
    }

    internal static (int MinChunkX, int MinChunkZ, int MaxChunkX, int MaxChunkZ)
        GetBackpackCampChunkBounds(BlockPos cart, double campRadius, BlockPos companionPosition)
    {
        double radius = Math.Max(0, campRadius);
        double cartCenterX = cart.X + 0.5;
        double cartCenterZ = cart.Z + 0.5;
        int minChunkX = (int)Math.Floor((cartCenterX - radius) / GlobalConstants.ChunkSize);
        int maxChunkX = (int)Math.Floor((cartCenterX + radius) / GlobalConstants.ChunkSize);
        int minChunkZ = (int)Math.Floor((cartCenterZ - radius) / GlobalConstants.ChunkSize);
        int maxChunkZ = (int)Math.Floor((cartCenterZ + radius) / GlobalConstants.ChunkSize);
        int companionChunkX = (int)Math.Floor(companionPosition.X / (double)GlobalConstants.ChunkSize);
        int companionChunkZ = (int)Math.Floor(companionPosition.Z / (double)GlobalConstants.ChunkSize);
        return (
            Math.Min(minChunkX, companionChunkX),
            Math.Min(minChunkZ, companionChunkZ),
            Math.Max(maxChunkX, companionChunkX),
            Math.Max(maxChunkZ, companionChunkZ));
    }

    private void RequestBackpackStorageAreaLoad(
        Entity companion,
        BlockPos cart,
        double campRadius,
        int pendingStorageChunkCount)
    {
        if (serverApi == null || pendingStorageChunkCount <= 0) return;
        string foxId = GetDomesticationStatus(companion)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(foxId) || !backpackStorageAreaLoads.Add(foxId)) return;

        BlockPos companionPosition = companion.Pos.AsBlockPos;
        (int minChunkX, int minChunkZ, int maxChunkX, int maxChunkZ) =
            GetBackpackCampChunkBounds(cart, campRadius, companionPosition);

        backpackStorageAreaLoadDeadlineUtcMs[foxId] = UtcNowMs() + BackpackStorageAreaLoadTimeoutMs;
        LogBackpackDelivery(companion, "storage-area-load-requested",
            $"campRadius={campRadius:0.##} campBounds={minChunkX},{minChunkZ}..{maxChunkX},{maxChunkZ} "
            + $"pendingStorageChunks={pendingStorageChunkCount}");

        void OnLoaded()
        {
            backpackStorageAreaLoads.Remove(foxId);
            backpackStorageAreaLoadDeadlineUtcMs.Remove(foxId);
            if (serverApi == null || packRepository?.Loaded != true) return;
            RefreshLoadedFoxPackRecords();
            Entity? active = FindLoadedCompanionForRecovery(foxId);
            if (active == null)
            {
                if (packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
                    && record?.BackpackDeliveryActive == true)
                {
                    LogBackpackDeliveryThrottled(record, "storage-area-companion-missed", 10_000,
                        $"campRadius={campRadius:0.##} campBounds={minChunkX},{minChunkZ}..{maxChunkX},{maxChunkZ}");
                    BeginBackpackMissingEntityRecovery(record, minChunkX, minChunkZ);
                }
                return;
            }
            if (!IsBackpackDeliveryActive(active)
                || !string.Equals(
                    GetDomesticationStatus(active)?.GetString(BackpackDeliveryPhaseKey, string.Empty),
                    "unload",
                    StringComparison.Ordinal))
            {
                return;
            }
            GetDomesticationStatus(active, true)!.SetBool(BackpackUnloadAccessFailedKey, false);
            LogBackpackDelivery(active, "storage-area-ready",
                $"campRadius={campRadius:0.##} campBounds={minChunkX},{minChunkZ}..{maxChunkX},{maxChunkZ}");
            ProcessUnloadedBackpackUnload(active);
        }

        try
        {
            if (companionPosition.dimension == 0)
            {
                serverApi.WorldManager.LoadChunkColumnPriority(
                    minChunkX,
                    minChunkZ,
                    maxChunkX,
                    maxChunkZ,
                    new ChunkLoadOptions { KeepLoaded = false, OnLoaded = OnLoaded });
            }
            else
            {
                for (int chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
                {
                    for (int chunkZ = minChunkZ; chunkZ <= maxChunkZ; chunkZ++)
                    {
                        serverApi.WorldManager.LoadChunkColumnForDimension(
                            chunkX,
                            chunkZ,
                            companionPosition.dimension);
                    }
                }
                serverApi.World.RegisterCallback(_ => OnLoaded(), 1000);
            }
        }
        catch (Exception exception)
        {
            backpackStorageAreaLoads.Remove(foxId);
            backpackStorageAreaLoadDeadlineUtcMs.Remove(foxId);
            GetDomesticationStatus(companion, true)!.SetBool(BackpackUnloadAccessFailedKey, true);
            PersistCompanionBackpack(companion);
            packRepository?.Save();
            LogBackpackDelivery(companion, "storage-area-load-failed",
                $"campRadius={campRadius:0.##} campBounds={minChunkX},{minChunkZ}..{maxChunkX},{maxChunkZ} "
                + $"error={exception.Message}");
        }
    }

    private void QueueBackpackPreflight(
        IServerPlayer owner,
        Entity companion,
        bool returnToPlayer)
    {
        if (serverApi == null) return;
        backpackPreflights[companion.EntityId] = new BackpackPreflight
        {
            OwnerUid = owner.PlayerUID,
            ReturnToPlayer = returnToPlayer,
            DeadlineUtcMs = UtcNowMs() + 15_000
        };
        LogBackpackDelivery(companion, "preflight-waiting-for-chunks",
            $"returnToPlayer={returnToPlayer} deadlineUtcMs={backpackPreflights[companion.EntityId].DeadlineUtcMs}");
        serverApi.Event.RegisterCallback(
            _ => ContinueBackpackPreflight(companion.EntityId),
            250);
    }

    private void ContinueBackpackPreflight(long entityId)
    {
        if (serverApi == null
            || !backpackPreflights.TryGetValue(entityId, out BackpackPreflight? preflight)) return;
        if (serverApi.World.GetEntityById(entityId) is not Entity companion
            || serverApi.World.PlayerByUid(preflight.OwnerUid) is not IServerPlayer owner
            || !CanPlayerUseCompanionBackpack(owner, companion))
        {
            backpackPreflights.Remove(entityId);
            return;
        }

        BlockPos? cart = GetActiveCairnPosition(owner.PlayerUID, discoverNearby: true);
        if (cart != null
            && TryFindGeneralBackpackStorage(companion, cart, null, requireCapacity: false, out _))
        {
            backpackPreflights.Remove(entityId);
            StartBackpackDelivery(owner, companion, preflight.ReturnToPlayer);
            return;
        }

        bool pending = cart != null && RequestPendingGeneralBackpackStorageChunks(companion, cart);
        if (pending && UtcNowMs() < preflight.DeadlineUtcMs)
        {
            serverApi.Event.RegisterCallback(_ => ContinueBackpackPreflight(entityId), 250);
            return;
        }

        backpackPreflights.Remove(entityId);
        RefuseBackpackDelivery(owner, companion,
            "there is no usable General storage at the active Pack Cart.");
    }

    internal bool TryResolveBackpackDeliveryTarget(Entity companion, out Vec3d? target)
    {
        target = null;
        if (!IsBackpackDeliveryActive(companion) || serverApi == null) return false;
        ITreeAttribute status = GetDomesticationStatus(companion, true)!;
        string phase = status.GetString(BackpackDeliveryPhaseKey, string.Empty);
        BlockPos cart = GetBackpackCart(status);
        if (phase == "home")
        {
            Block block = serverApi.World.BlockAccessor.GetBlock(cart);
            BlockPos portal = GetPackMarkerPortalBlock(cart, block);
            target = new Vec3d(portal.X + 0.5,
                portal.Y + portal.dimension * BlockPos.DimensionBoundary,
                portal.Z + 0.5);
            return companion.Pos.Dimension == cart.dimension;
        }
        if (phase != "unload") return false;

        EntityBehaviorFeralKinshipFoxSocial? behavior = companion.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>();
        ItemStack? next = behavior?.CompanionBackpackContents?
            .FirstOrDefault(slot => !slot.Empty)?.Itemstack;
        if (next == null)
        {
            CompleteBackpackUnload(companion);
            return false;
        }
        if (!TryFindGeneralBackpackStorage(companion, cart, next, requireCapacity: true, out BlockPos? storage)
            || storage == null)
        {
            if (RequestPendingGeneralBackpackStorageChunks(
                    companion,
                    cart,
                    resumeUnloadWhenLoaded: true))
            {
                LogBackpackDeliveryThrottled(companion, "unload-waiting-for-chunks", 10_000,
                    $"cart={FormatBackpackPosition(cart)} remaining={GetCompanionBackpackCargoCount(companion)}");
                return false;
            }
            status.SetBool(BackpackUnloadAccessFailedKey, false);
            LogBackpackDelivery(companion, "unload-no-capacity",
                $"cart={FormatBackpackPosition(cart)} remaining={GetCompanionBackpackCargoCount(companion)}");
            CompleteBackpackUnload(companion);
            return false;
        }
        status.SetInt(BackpackStorageXKey, storage.X);
        status.SetInt(BackpackStorageYKey, storage.Y);
        status.SetInt(BackpackStorageZKey, storage.Z);
        target = GetFoxStorageApproachTarget(companion, storage);
        PersistCompanionBackpack(companion);
        return true;
    }

    internal void OnBackpackDeliveryArrived(Entity companion)
    {
        if (!IsBackpackDeliveryActive(companion)) return;
        ITreeAttribute status = GetDomesticationStatus(companion, true)!;
        string phase = status.GetString(BackpackDeliveryPhaseKey, string.Empty);
        if (phase == "home")
        {
            LogBackpackDelivery(companion, "arrived-home", "switching to unload phase");
            RegisterFoxInPack(companion);
            SetBackpackDeliveryPhase(companion, status, "unload");
            return;
        }
        if (phase != "unload" || serverApi == null) return;

        BlockPos storagePos = new(
            status.GetInt(BackpackStorageXKey, 0),
            status.GetInt(BackpackStorageYKey, 0),
            status.GetInt(BackpackStorageZKey, 0),
            companion.Pos.Dimension);
        UnloadCompanionBackpackIntoStorage(companion, storagePos, "path-arrival");
    }

    private int UnloadCompanionBackpackIntoStorage(
        Entity companion,
        BlockPos storagePos,
        string source)
    {
        if (serverApi == null) return -1;
        FoxPackAmenityRecord? routingRecord = packRepository?.GetAmenity(storagePos);
        if (routingRecord == null
            || !string.Equals(routingRecord.OwnerUid, GetCompanionOwnerUid(companion), StringComparison.Ordinal)
            || !IsStorageRoutingEnabled(routingRecord)
            || !FoxStorageRouting.IsGeneral(routingRecord, GetEffectiveStorageRoutingMask(routingRecord)))
        {
            LogBackpackDeliveryThrottled(companion, "invalid-storage", 10_000,
                $"source={source} storage={FormatBackpackPosition(storagePos)} routing record is missing or no longer General");
            return -1;
        }
        BlockEntity? blockEntity = serverApi.World.BlockAccessor.GetBlockEntity(storagePos);
        int movedTotal = 0;
        int beforeTotal = 0;
        if (blockEntity is IBlockEntityContainer container)
        {
            GetDomesticationStatus(companion, true)!.SetBool(BackpackUnloadAccessFailedKey, false);
            IInventory? inventory = null;
            try { inventory = container.Inventory; } catch { }
            InventoryGeneric? contents = companion.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.CompanionBackpackContents;
            if (inventory != null && contents != null)
            {
                foreach (ItemSlot slot in contents.Where(slot => !slot.Empty).ToArray())
                {
                    ItemStack cargo = slot.Itemstack!;
                    beforeTotal += cargo.StackSize;
                    int moved = TransferCargoIntoInventory(companion, cargo, inventory, blockEntity, out ItemStack? remaining);
                    if (moved <= 0) continue;
                    movedTotal += moved;
                    slot.Itemstack = remaining;
                    slot.MarkDirty();
                }
            }
            int remainingTotal = GetCompanionBackpackCargoCount(companion);
            if (movedTotal > 0)
            {
                ITreeAttribute status = GetDomesticationStatus(companion, true)!;
                status.SetInt(
                    BackpackDeliveryMovedItemCountKey,
                    status.GetInt(BackpackDeliveryMovedItemCountKey, 0) + movedTotal);
            }
            LogBackpackDelivery(companion, "unload-attempt",
                $"source={source} storage={FormatBackpackPosition(storagePos)} before={beforeTotal} moved={movedTotal} remaining={remainingTotal}");
        }
        else
        {
            LogBackpackDeliveryThrottled(companion, "invalid-storage", 10_000,
                $"source={source} storage={FormatBackpackPosition(storagePos)} block entity is not a container");
            return -1;
        }
        PersistCompanionBackpack(companion);
        packRepository?.Save();
        return movedTotal;
    }

    private void ProcessUnloadedBackpackUnload(Entity companion)
    {
        if (!IsBackpackDeliveryActive(companion) || serverApi == null) return;
        ITreeAttribute status = GetDomesticationStatus(companion, true)!;
        if (!string.Equals(
                status.GetString(BackpackDeliveryPhaseKey, string.Empty),
                "unload",
                StringComparison.Ordinal)) return;

        BlockPos cart = GetBackpackCart(status);
        int startingTotal = GetCompanionBackpackCargoCount(companion);
        if (startingTotal <= 0)
        {
            LogBackpackDelivery(companion, "background-unload-complete", "starting=0 moved=0 remaining=0");
            CompleteBackpackUnload(companion);
            return;
        }

        int movedTotal = 0;
        for (int attempt = 0; attempt < 64 && IsBackpackDeliveryActive(companion); attempt++)
        {
            ItemStack? next = companion.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?
                .CompanionBackpackContents?.FirstOrDefault(slot => !slot.Empty)?.Itemstack;
            if (next == null)
            {
                LogBackpackDelivery(companion, "background-unload-complete",
                    $"starting={startingTotal} moved={movedTotal} remaining=0");
                CompleteBackpackUnload(companion);
                return;
            }
            if (!TryFindGeneralBackpackStorage(
                    companion,
                    cart,
                    next,
                    requireCapacity: true,
                    out BlockPos? storage)
                || storage == null)
            {
                if (RequestPendingGeneralBackpackStorageChunks(
                        companion,
                        cart,
                        resumeUnloadWhenLoaded: true))
                {
                    LogBackpackDeliveryThrottled(companion, "background-unload-waiting-for-chunks", 10_000,
                        $"cart={FormatBackpackPosition(cart)} moved={movedTotal} remaining={GetCompanionBackpackCargoCount(companion)}");
                    return;
                }
                status.SetBool(BackpackUnloadAccessFailedKey, false);
                LogBackpackDelivery(companion, "background-unload-no-capacity",
                    $"cart={FormatBackpackPosition(cart)} moved={movedTotal} remaining={GetCompanionBackpackCargoCount(companion)}");
                CompleteBackpackUnload(companion);
                return;
            }

            int moved = UnloadCompanionBackpackIntoStorage(companion, storage, "unloaded-chunk");
            if (moved <= 0)
            {
                LogBackpackDelivery(companion, "background-unload-no-progress",
                    $"storage={FormatBackpackPosition(storage)} moved={movedTotal} remaining={GetCompanionBackpackCargoCount(companion)}");
                CompleteBackpackUnload(companion);
                return;
            }
            movedTotal += moved;
        }

        int remaining = GetCompanionBackpackCargoCount(companion);
        LogBackpackDelivery(companion, "background-unload-guard",
            $"starting={startingTotal} moved={movedTotal} remaining={remaining}; continuing on the next wake");
    }

    internal void OnBackpackDeliveryRouteFailed(Entity companion)
    {
        // Keep the durable phase intact. The high-priority task will retry;
        // the phase watchdog supplies the bounded safe fallback.
        LogBackpackDeliveryThrottled(companion, "route-failed", 10_000,
            $"position={FormatBackpackPosition(companion.Pos.AsBlockPos)}; task will retry until the phase watchdog fires");
    }

    internal bool UpdateBackpackDelivery(Entity companion)
    {
        if (!IsBackpackDeliveryActive(companion)) return false;
        ITreeAttribute status = GetDomesticationStatus(companion, true)!;
        if (!companion.Alive || IsFoxIncapacitated(companion))
        {
            CancelBackpackDelivery(companion, restorePreviousActivity: false);
            return false;
        }

        long now = UtcNowMs();
        string phase = status.GetString(BackpackDeliveryPhaseKey, string.Empty);
        long started = status.GetLong(BackpackPhaseStartedUtcMsKey, now);
        if (phase == "home" && now - started >= BackpackHomeTimeoutMs)
        {
            BlockPos cart = GetBackpackCart(status);
            Vec3d target = new(cart.X + 0.5,
                cart.Y + cart.dimension * BlockPos.DimensionBoundary,
                cart.Z + 0.5);
            bool teleported = TryTeleportCompanionToCommandHome(companion, target);
            LogBackpackDeliveryThrottled(companion, "home-watchdog", 5_000,
                $"cart={FormatBackpackPosition(cart)} teleportSucceeded={teleported}");
            if (teleported)
            {
                RegisterFoxInPack(companion);
                SetBackpackDeliveryPhase(companion, status, "unload");
            }
        }
        else if (phase == "unload" && now - started >= BackpackUnloadTimeoutMs)
        {
            string foxId = status.GetString(FoxIdKey, string.Empty);
            if (backpackStorageAreaLoads.Contains(foxId)
                && backpackStorageAreaLoadDeadlineUtcMs.TryGetValue(foxId, out long loadDeadline)
                && now < loadDeadline)
            {
                LogBackpackDeliveryThrottled(companion, "unload-watchdog-waiting-for-storage-area", 5_000,
                    $"loadDeadlineUtcMs={loadDeadline} remaining={GetCompanionBackpackCargoCount(companion)}");
                return true;
            }
            if (backpackStorageAreaLoads.Remove(foxId))
            {
                backpackStorageAreaLoadDeadlineUtcMs.Remove(foxId);
                status.SetBool(BackpackUnloadAccessFailedKey, true);
                LogBackpackDelivery(companion, "storage-area-load-timeout",
                    $"remaining={GetCompanionBackpackCargoCount(companion)}; preserving every untransferred item");
            }
            LogBackpackDelivery(companion, "unload-watchdog",
                $"remaining={GetCompanionBackpackCargoCount(companion)}; completing with anything that fit");
            CompleteBackpackUnload(companion);
        }
        else if (phase == "wait-return")
        {
            if (!status.GetBool(BackpackReturnSnapshotTakenKey, false))
            {
                ScheduleBackpackReturn(companion, status);
            }
            else if (now >= status.GetLong(BackpackReturnDueUtcMsKey, long.MaxValue))
            {
                TryFinishBackpackReturn(companion, status, now);
            }
        }
        return IsBackpackDeliveryActive(companion);
    }

    private void CompleteBackpackUnload(Entity companion)
    {
        ITreeAttribute status = GetDomesticationStatus(companion, true)!;
        bool accessFailed = status.GetBool(BackpackUnloadAccessFailedKey, false);
        FinalizeBackpackDeliverySummary(companion, status, accessFailed);
        if (!status.GetBool(BackpackReturnToPlayerKey, false))
        {
            bool partial = CompanionBackpackHasCargo(companion);
            SetCompanionActivityState(companion, CompanionActivityMode.AtEase);
            CancelBackpackDelivery(companion, restorePreviousActivity: false);
            NotifyBackpackDeliveryComplete(companion,
                accessFailed
                    ? "could not access home storage and is staying home with the items it could not unload."
                    : partial
                        ? "unloaded everything it could and is staying home."
                        : "unloaded everything and is staying home.");
            return;
        }

        status.SetLong(BackpackReturnDueUtcMsKey, 0);
        status.SetLong(BackpackReturnRetryUntilUtcMsKey, 0);
        status.SetBool(BackpackReturnSnapshotTakenKey, false);
        SetBackpackDeliveryPhase(companion, status, "wait-return", resetStarted: false);
        SetCompanionActivityState(companion, CompanionActivityMode.AtEase);
        RegisterFoxInPack(companion);
        ScheduleBackpackReturn(companion, status);
    }

    private void ScheduleBackpackReturn(Entity companion, ITreeAttribute status)
    {
        BlockPos cart = GetBackpackCart(status);
        IServerPlayer? ownerPlayer = GetOnlineBackpackOwner(companion);
        Entity? owner = ownerPlayer?.Entity;
        if (owner == null || owner.Pos.Dimension != cart.dimension)
        {
            LogBackpackDeliveryThrottled(companion, "return-wait-owner", 30_000,
                owner == null
                    ? "owner is offline or not fully playing"
                    : $"ownerDimension={owner.Pos.Dimension} cartDimension={cart.dimension}");
            return;
        }

        double distance = Math.Sqrt(owner.Pos.SquareDistanceTo(new Vec3d(
            cart.X + 0.5,
            cart.Y + cart.dimension * BlockPos.DimensionBoundary,
            cart.Z + 0.5)));
        double blocksPerSecond = Math.Max(0.05d, 0.9d * GetFoxMovementSpeedMultiplier(companion));
        int returnTalentRank = GetFoxPerkRank(companion, "homeward-bound");
        long travelMs = CalculateBackpackReturnTravelMs(distance, blocksPerSecond, returnTalentRank);
        long dueAtUtcMs = UtcNowMs() + travelMs;
        status.SetLong(BackpackReturnDueUtcMsKey, dueAtUtcMs);
        status.SetBool(BackpackReturnSnapshotTakenKey, true);
        PersistCompanionBackpack(companion);
        packRepository?.Save();
        bool partial = CompanionBackpackHasCargo(companion);
        bool accessFailed = status.GetBool(BackpackUnloadAccessFailedKey, false);
        ownerPlayer!.SendMessage(
            GlobalConstants.GeneralChatGroup,
            $"{GetFoxDisplayName(companion)} "
                + (accessFailed
                    ? "could not access home storage"
                    : partial ? "unloaded everything it could" : "unloaded everything")
                + $" and will return in {FormatBackpackTravelTime(travelMs)}.",
            EnumChatType.Notification);
        LogBackpackDelivery(companion, "return-scheduled",
            $"distance={distance:0.0} speed={blocksPerSecond:0.###} homewardBoundRank={returnTalentRank} "
            + $"timeReductionPercent={returnTalentRank * 15} travelMs={travelMs} dueUtcMs={dueAtUtcMs} partial={partial}");
        RefreshBackpackLedgerViewer(companion);
    }

    internal static long CalculateBackpackReturnTravelMs(
        double distance,
        double blocksPerSecond,
        int homewardBoundRank)
    {
        double safeDistance = double.IsFinite(distance) ? Math.Max(0d, distance) : 0d;
        double safeSpeed = double.IsFinite(blocksPerSecond)
            ? Math.Max(0.05d, blocksPerSecond)
            : 0.05d;
        double rawTravelMs = safeDistance / safeSpeed * 1000d;
        long baseTravelMs = rawTravelMs >= long.MaxValue
            ? long.MaxValue
            : Math.Max(1000L, (long)Math.Ceiling(rawTravelMs));
        int rank = Math.Clamp(homewardBoundRank, 0, 3);
        int remainingPercent = 100 - rank * 15;

        // Apply the percentage with integer ceiling arithmetic so the timer
        // never finishes earlier than the exact reduced duration and cannot
        // overflow even for extreme world-coordinate distances.
        long reducedTravelMs = baseTravelMs / 100 * remainingPercent
            + ((baseTravelMs % 100) * remainingPercent + 99) / 100;
        return Math.Max(1000L, reducedTravelMs);
    }

    private void TryFinishBackpackReturn(Entity companion, ITreeAttribute status, long now)
    {
        IServerPlayer? ownerPlayer = GetOnlineBackpackOwner(companion);
        Entity? owner = ownerPlayer?.Entity;
        if (owner == null || owner.Pos.Dimension != companion.Pos.Dimension)
        {
            // Offline and cross-dimension owners are not placement failures;
            // the companion waits safely at home until a valid return exists.
            LogBackpackDeliveryThrottled(companion, "return-wait-owner", 30_000,
                owner == null
                    ? "owner is offline or not fully playing"
                    : $"ownerDimension={owner.Pos.Dimension} companionDimension={companion.Pos.Dimension}");
            return;
        }
        long retryUntil = status.GetLong(BackpackReturnRetryUntilUtcMsKey, 0);
        if (retryUntil <= 0)
        {
            retryUntil = now + BackpackReturnPlacementTimeoutMs;
            status.SetLong(BackpackReturnRetryUntilUtcMsKey, retryUntil);
        }
        Vec3d? safe = FindSafeEntityPosition(owner.Pos.AsBlockPos, companion.Properties, companion, 6, 4);
        if (safe != null)
        {
            companion.TeleportTo(safe);
            companion.PositionBeforeFalling.Set(safe.X, safe.Y, safe.Z);
            bool partial = CompanionBackpackHasCargo(companion);
            bool accessFailed = status.GetBool(BackpackUnloadAccessFailedKey, false);
            LogBackpackDelivery(companion, "return-succeeded",
                $"destination={safe.X:0.0},{safe.Y:0.0},{safe.Z:0.0} partial={partial} accessFailed={accessFailed}");
            CancelBackpackDelivery(companion, restorePreviousActivity: true);
            NotifyBackpackDeliveryComplete(companion,
                accessFailed
                    ? "could not access home storage and came back with the items it could not unload."
                    : partial
                        ? "unloaded everything it could and came back."
                        : "unloaded everything and came back.");
            return;
        }
        if (now >= retryUntil)
        {
            LogBackpackDelivery(companion, "return-placement-timeout",
                $"ownerPosition={FormatBackpackPosition(owner.Pos.AsBlockPos)} retryUntilUtcMs={retryUntil}");
            CancelBackpackDelivery(companion, restorePreviousActivity: false);
            serverChannel?.SendPacket(new CompanionThoughtPacket
            {
                TargetEntityId = companion.EntityId,
                Text = "I couldn’t find a safe way back.",
                DurationMs = 5000
            }, ownerPlayer);
            NotifyBackpackDeliveryComplete(companion,
                "could not find a safe place to return and is staying home.");
        }
    }

    private void SetBackpackDeliveryPhase(
        Entity companion,
        ITreeAttribute status,
        string phase,
        bool resetStarted = true)
    {
        status.SetString(BackpackDeliveryPhaseKey, phase);
        if (resetStarted) status.SetLong(BackpackPhaseStartedUtcMsKey, UtcNowMs());
        companion.WatchedAttributes.SetString(BackpackDeliveryPhaseKey, phase);
        companion.WatchedAttributes.MarkPathDirty(BackpackDeliveryPhaseKey);
        PersistCompanionBackpack(companion);
        packRepository?.Save();
        LogBackpackDelivery(companion, "phase-changed",
            $"newPhase={phase} resetStarted={resetStarted}");
        RefreshBackpackLedgerViewer(companion);
    }

    private void CancelBackpackDelivery(Entity companion, bool restorePreviousActivity)
    {
        ITreeAttribute status = GetDomesticationStatus(companion, true)!;
        string previous = status.GetString(BackpackPreviousActivityKey, CompanionActivityMode.AtEase);
        string phase = status.GetString(BackpackDeliveryPhaseKey, string.Empty);
        companion.WatchedAttributes.SetBool(BackpackDeliveryActiveKey, false);
        companion.WatchedAttributes.SetString(BackpackDeliveryPhaseKey, string.Empty);
        companion.WatchedAttributes.MarkPathDirty(BackpackDeliveryActiveKey);
        companion.WatchedAttributes.MarkPathDirty(BackpackDeliveryPhaseKey);
        status.SetString(BackpackDeliveryPhaseKey, string.Empty);
        status.SetLong(BackpackPhaseStartedUtcMsKey, 0);
        status.SetLong(BackpackReturnDueUtcMsKey, 0);
        status.SetLong(BackpackReturnRetryUntilUtcMsKey, 0);
        status.SetBool(BackpackReturnSnapshotTakenKey, false);
        status.SetBool(BackpackUnloadAccessFailedKey, false);
        status.SetInt(BackpackDeliveryStartingItemCountKey, 0);
        status.SetInt(BackpackDeliveryMovedItemCountKey, 0);
        string foxId = status.GetString(FoxIdKey, string.Empty);
        backpackStorageAreaLoads.Remove(foxId);
        backpackStorageAreaLoadDeadlineUtcMs.Remove(foxId);
        if (restorePreviousActivity && CompanionActivityMode.IsValid(previous))
        {
            SetCompanionActivityState(companion, CompanionActivityMode.Normalize(previous));
        }
        companion.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.RefreshCompanionBackpackLock();
        PersistCompanionBackpack(companion);
        RegisterFoxInPack(companion);
        packRepository?.Save();
        LogBackpackDelivery(companion, "job-ended",
            $"previousPhase={phase} restorePreviousActivity={restorePreviousActivity} restoredActivity={previous}");
        RefreshBackpackLedgerViewer(companion);
    }

    private void MaintainBackpackDeliveries(long nowUtcMs)
    {
        if (serverApi == null || packRepository?.Loaded != true) return;

        FoxPackRecordV2[] activeRecords = packRepository.GetAllRecords()
            .Where(record => record.BackpackDeliveryActive && !record.Archived)
            .ToArray();
        HashSet<string> activeIds = activeRecords
            .Select(record => record.FoxId)
            .Where(foxId => !string.IsNullOrWhiteSpace(foxId))
            .ToHashSet(StringComparer.Ordinal);
        foreach (string staleId in backpackChunkWakeRetryAtUtcMs.Keys
                     .Where(foxId => !activeIds.Contains(foxId)).ToArray())
        {
            backpackChunkWakeRetryAtUtcMs.Remove(staleId);
            backpackMissingEntityRecoveryChecks.Remove(staleId);
        }

        foreach (FoxPackRecordV2 record in activeRecords)
        {
            if (string.IsNullOrWhiteSpace(record.FoxId)) continue;
            if (string.Equals(record.BackpackDeliveryPhase, "wait-return", StringComparison.Ordinal)
                && record.BackpackReturnSnapshotTaken
                && record.BackpackReturnDueUtcMs > nowUtcMs)
            {
                // The return deadline is durable. Nothing at home needs to
                // tick while the simulated trip is still in progress.
                backpackChunkWakeRetryAtUtcMs.Remove(record.FoxId);
                continue;
            }
            Entity? loaded = FindLoadedCompanionByFoxId(record.FoxId)
                ?? (record.EntityId > 0 ? serverApi.World.GetEntityById(record.EntityId) : null);
            if (loaded != null)
            {
                backpackChunkWakeRetryAtUtcMs.Remove(record.FoxId);
                continue;
            }
            if (!record.HasLastKnownPosition)
            {
                LogBackpackDeliveryThrottled(record, "wake-missing-position", 30_000,
                    "active saved job has no last-known position");
                continue;
            }
            if (backpackChunkWakeRetryAtUtcMs.TryGetValue(record.FoxId, out long retryAt)
                && nowUtcMs < retryAt) continue;

            backpackChunkWakeRetryAtUtcMs[record.FoxId] = nowUtcMs + BackpackChunkWakeRetryMs;
            RequestBackpackDeliveryChunkWake(record);
        }
    }

    private void RequestBackpackDeliveryChunkWake(FoxPackRecordV2 record)
    {
        if (serverApi == null) return;
        BlockPos position = new(
            record.LastKnownX,
            record.LastKnownY,
            record.LastKnownZ,
            record.LastKnownDimension);
        int chunkX = (int)Math.Floor(position.X / (double)GlobalConstants.ChunkSize);
        int chunkZ = (int)Math.Floor(position.Z / (double)GlobalConstants.ChunkSize);
        LogBackpackDeliveryThrottled(record, "chunk-wake-requested", 10_000,
            $"position={FormatBackpackPosition(position)} chunk={chunkX},{chunkZ}");

        void OnLoaded()
        {
            RefreshLoadedFoxPackRecords();
            Entity? companion = FindLoadedCompanionByFoxId(record.FoxId);
            if (companion == null)
            {
                LogBackpackDeliveryThrottled(record, "chunk-wake-missed", 10_000,
                    $"loaded chunk={chunkX},{chunkZ} but the saved companion was not found");
                BeginBackpackMissingEntityRecovery(record, chunkX, chunkZ);
                return;
            }
            ResumeBackpackDeliveryAfterChunkWake(companion, $"loaded chunk={chunkX},{chunkZ}");
        }

        if (position.dimension == 0)
        {
            serverApi.WorldManager.LoadChunkColumnPriority(
                chunkX,
                chunkZ,
                new ChunkLoadOptions { KeepLoaded = false, OnLoaded = OnLoaded });
        }
        else
        {
            serverApi.WorldManager.LoadChunkColumnForDimension(chunkX, chunkZ, position.dimension);
            serverApi.World.RegisterCallback(_ => OnLoaded(), 1000);
        }
    }

    private void BeginBackpackMissingEntityRecovery(
        FoxPackRecordV2 record,
        int sourceChunkX,
        int sourceChunkZ)
    {
        if (serverApi == null
            || string.IsNullOrWhiteSpace(record.FoxId)
            || !backpackMissingEntityRecoveryChecks.Add(record.FoxId))
        {
            return;
        }

        string foxId = record.FoxId;
        long missingEntityId = record.EntityId;
        BlockPos savedPosition = new(
            record.LastKnownX,
            record.LastKnownY,
            record.LastKnownZ,
            record.LastKnownDimension);
        int checks = 0;
        LogBackpackDeliveryThrottled(record, "missing-entity-probe-started", 30_000,
            $"sourceChunk={sourceChunkX},{sourceChunkZ} checks={BackpackMissingEntityMaximumChecks}");

        void FinishProbe()
        {
            backpackMissingEntityRecoveryChecks.Remove(foxId);
        }

        void CheckForCompanion()
        {
            if (serverApi == null || !backpackMissingEntityRecoveryChecks.Contains(foxId)) return;
            if (packRepository?.Loaded != true
                || !packRepository.TryGetRecord(foxId, out FoxPackRecordV2? current)
                || current == null
                || !current.BackpackDeliveryActive
                || current.Archived)
            {
                FinishProbe();
                return;
            }

            RefreshLoadedFoxPackRecords();
            Entity? companion = FindLoadedCompanionForRecovery(foxId);
            if (companion != null)
            {
                FinishProbe();
                ResumeBackpackDeliveryAfterChunkWake(
                    companion,
                    $"sourceChunk={sourceChunkX},{sourceChunkZ} appearedDuringProbe=true");
                return;
            }

            checks++;
            if (checks < BackpackMissingEntityMaximumChecks)
            {
                serverApi.World.RegisterCallback(_ => CheckForCompanion(), BackpackMissingEntityCheckIntervalMs);
                return;
            }

            FinishProbe();
            TryAutomaticallyRecoverBackpackDelivery(
                current,
                missingEntityId,
                savedPosition,
                sourceChunkX,
                sourceChunkZ);
        }

        CheckForCompanion();
    }

    private void TryAutomaticallyRecoverBackpackDelivery(
        FoxPackRecordV2 record,
        long missingEntityId,
        BlockPos savedPosition,
        int sourceChunkX,
        int sourceChunkZ)
    {
        if (serverApi == null || packRepository?.Loaded != true) return;
        if (FindLoadedCompanionForRecovery(record.FoxId) is Entity existing)
        {
            ResumeBackpackDeliveryAfterChunkWake(
                existing,
                $"sourceChunk={sourceChunkX},{sourceChunkZ} appearedBeforeRecovery=true");
            return;
        }
        if (serverApi.World.PlayerByUid(record.OwnerUid) is not IServerPlayer owner
            || owner.ConnectionState != EnumClientState.Playing
            || owner.Entity == null)
        {
            LogBackpackDeliveryThrottled(record, "auto-recovery-wait-owner", 30_000,
                $"sourceChunk={sourceChunkX},{sourceChunkZ} owner is offline or not fully playing");
            return;
        }

        bool returningNow = string.Equals(
                record.BackpackDeliveryPhase,
                "wait-return",
                StringComparison.Ordinal)
            && record.BackpackReturnSnapshotTaken
            && record.BackpackReturnDueUtcMs <= UtcNowMs();
        BlockPos? spawnOrigin = returningNow ? null : savedPosition;
        bool recovered = RecoverCompanionDeveloper(
            owner,
            record.FoxId,
            sendResult: false,
            allowTerminalStatus: false,
            allowActiveExpedition: false,
            preferredSpawnOrigin: spawnOrigin);
        Entity? companion = recovered ? FindLoadedCompanionForRecovery(record.FoxId) : null;
        if (companion == null)
        {
            LogBackpackDeliveryThrottled(record, "auto-recovery-failed", 30_000,
                $"sourceChunk={sourceChunkX},{sourceChunkZ} missingEntityId={missingEntityId} phase={record.BackpackDeliveryPhase}");
            return;
        }

        backpackChunkWakeRetryAtUtcMs.Remove(record.FoxId);
        LogBackpackDelivery(companion, "auto-recovery-succeeded",
            $"sourceChunk={sourceChunkX},{sourceChunkZ} missingEntityId={missingEntityId} newEntityId={companion.EntityId} "
            + $"spawnOrigin={(spawnOrigin == null ? "owner" : FormatBackpackPosition(spawnOrigin))}");
        ResumeBackpackDeliveryAfterChunkWake(
            companion,
            $"sourceChunk={sourceChunkX},{sourceChunkZ} automaticRecovery=true");
    }

    private void ResumeBackpackDeliveryAfterChunkWake(Entity companion, string details)
    {
        if (!IsBackpackDeliveryActive(companion)) return;
        LogBackpackDelivery(companion, "chunk-wake-resumed", details);
        ITreeAttribute? status = GetDomesticationStatus(companion);
        if (string.Equals(
                status?.GetString(BackpackDeliveryPhaseKey, string.Empty),
                "unload",
                StringComparison.Ordinal))
        {
            ProcessUnloadedBackpackUnload(companion);
            return;
        }

        UpdateBackpackDelivery(companion);
        status = GetDomesticationStatus(companion);
        if (IsBackpackDeliveryActive(companion)
            && string.Equals(
                status?.GetString(BackpackDeliveryPhaseKey, string.Empty),
                "unload",
                StringComparison.Ordinal))
        {
            ProcessUnloadedBackpackUnload(companion);
        }
    }

    private IServerPlayer? GetOnlineBackpackOwner(Entity companion)
    {
        return serverApi?.World.PlayerByUid(GetCompanionOwnerUid(companion)) is IServerPlayer owner
            && owner.ConnectionState == EnumClientState.Playing
                ? owner
                : null;
    }

    private static bool CompanionBackpackHasCargo(Entity companion) =>
        companion.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.CompanionBackpackContents?
            .Any(slot => !slot.Empty) == true;

    private static int GetCompanionBackpackCargoCount(Entity companion) =>
        companion.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.CompanionBackpackContents?
            .Where(slot => !slot.Empty)
            .Sum(slot => slot.StackSize) ?? 0;

    private void FinalizeBackpackDeliverySummary(
        Entity companion,
        ITreeAttribute status,
        bool accessFailed)
    {
        int retained = Math.Max(0, GetCompanionBackpackCargoCount(companion));
        int starting = Math.Max(0, status.GetInt(BackpackDeliveryStartingItemCountKey, 0));
        int moved = Math.Max(
            Math.Max(0, status.GetInt(BackpackDeliveryMovedItemCountKey, 0)),
            Math.Max(0, starting - retained));
        string summary = FormatBackpackDeliverySummary(moved, retained, accessFailed);
        status.SetString(BackpackLastDeliverySummaryKey, summary);
        PersistCompanionBackpack(companion);
        packRepository?.Save();
        LogBackpackDelivery(companion, "delivery-summary-saved",
            $"starting={starting} moved={moved} retained={retained} accessFailed={accessFailed} summary={summary}");
    }

    internal static string FormatBackpackDeliverySummary(
        int moved,
        int retained,
        bool accessFailed)
    {
        int safeMoved = Math.Max(0, moved);
        int safeRetained = Math.Max(0, retained);
        string movedLabel = safeMoved == 1 ? "item" : "items";
        string access = accessFailed ? " Home storage could not be fully accessed." : string.Empty;
        return $"Last trip: unloaded {safeMoved} {movedLabel}, retained {safeRetained}.{access}";
    }

    private void GetBackpackLedgerDisplay(
        Entity? companion,
        FoxPackRecordV2 record,
        out bool equipped,
        out string name,
        out int occupiedSlots,
        out int totalSlots)
    {
        equipped = false;
        name = string.Empty;
        occupiedSlots = 0;
        totalSlots = 0;
        if (serverApi == null) return;

        EntityBehaviorFeralKinshipFoxSocial? behavior =
            companion?.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>();
        ItemStack? stack = behavior?.CompanionBackpackStack;
        if (stack == null && record.BackpackItem?.Length > 0)
        {
            try
            {
                stack = new ItemStack(record.BackpackItem);
                if (!stack.ResolveBlockOrItem(serverApi.World)) stack = null;
            }
            catch
            {
                stack = null;
            }
        }

        IHeldBag? bag = stack?.Collectible?.GetCollectibleInterface<IHeldBag>();
        if (stack == null || bag == null) return;

        equipped = true;
        name = stack.GetName();
        totalSlots = Math.Max(0, bag.GetQuantitySlots(stack));
        InventoryGeneric? liveContents = behavior?.CompanionBackpackContents;
        if (liveContents != null)
        {
            occupiedSlots = liveContents.Count(slot => !slot.Empty);
            return;
        }

        try
        {
            occupiedSlots = (bag.GetContents(stack, serverApi.World) ?? Array.Empty<ItemStack>())
                .Count(content => content != null && content.StackSize > 0);
        }
        catch
        {
            occupiedSlots = 0;
        }
    }

    private static string FormatBackpackTravelTime(long travelMs)
    {
        TimeSpan duration = TimeSpan.FromMilliseconds(Math.Max(1000L, travelMs));
        if (duration.Days > 0) return $"{duration.Days}d {duration.Hours}h {duration.Minutes}m {duration.Seconds}s";
        if (duration.Hours > 0) return $"{duration.Hours}h {duration.Minutes}m {duration.Seconds}s";
        if (duration.Minutes > 0) return $"{duration.Minutes}m {duration.Seconds}s";
        return $"{Math.Max(1, duration.Seconds)}s";
    }

    private static string FormatBackpackPosition(BlockPos position) =>
        $"{position.X},{position.Y},{position.Z},dim={position.dimension}";

    private void LogBackpackDelivery(Entity companion, string eventName, string details)
    {
        if (serverApi == null) return;
        ITreeAttribute? status = GetDomesticationStatus(companion);
        serverApi.Logger.Notification(
            "[FeralKinshipCompanions][BackpackDelivery] event={0} foxId={1} name={2} entityId={3} phase={4} position={5} {6}",
            eventName,
            status?.GetString(FoxIdKey, string.Empty) ?? string.Empty,
            GetFoxDisplayName(companion),
            companion.EntityId,
            status?.GetString(BackpackDeliveryPhaseKey, string.Empty) ?? string.Empty,
            FormatBackpackPosition(companion.Pos.AsBlockPos),
            details);
    }

    private void LogBackpackDeliveryThrottled(
        Entity companion,
        string eventName,
        long intervalMs,
        string details)
    {
        string foxId = GetDomesticationStatus(companion)?.GetString(FoxIdKey, string.Empty)
            ?? companion.EntityId.ToString();
        if (!CanLogBackpackDiagnostic(foxId, eventName, intervalMs)) return;
        LogBackpackDelivery(companion, eventName, details);
    }

    private void LogBackpackDeliveryThrottled(
        FoxPackRecordV2 record,
        string eventName,
        long intervalMs,
        string details)
    {
        if (serverApi == null || !CanLogBackpackDiagnostic(record.FoxId, eventName, intervalMs)) return;
        serverApi.Logger.Notification(
            "[FeralKinshipCompanions][BackpackDelivery] event={0} foxId={1} name={2} entityId={3} phase={4} position={5},{6},{7},dim={8} {9}",
            eventName,
            record.FoxId,
            record.Name,
            record.EntityId,
            record.BackpackDeliveryPhase,
            record.LastKnownX,
            record.LastKnownY,
            record.LastKnownZ,
            record.LastKnownDimension,
            details);
    }

    private bool CanLogBackpackDiagnostic(string foxId, string eventName, long intervalMs)
    {
        string key = $"{foxId}:{eventName}";
        long now = UtcNowMs();
        if (backpackDiagnosticThrottleAtUtcMs.TryGetValue(key, out long nextAt) && now < nextAt)
        {
            return false;
        }
        backpackDiagnosticThrottleAtUtcMs[key] = now + Math.Max(1000L, intervalMs);
        return true;
    }

    private void NotifyBackpackDeliveryComplete(Entity companion, string suffix)
    {
        if (serverApi?.World.PlayerByUid(GetCompanionOwnerUid(companion)) is IServerPlayer owner)
        {
            owner.SendMessage(GlobalConstants.GeneralChatGroup,
                $"{GetFoxDisplayName(companion)} {suffix}", EnumChatType.Notification);
        }
    }

    private void RefreshBackpackLedgerViewer(Entity companion)
    {
        IServerPlayer? owner = GetOnlineBackpackOwner(companion);
        if (owner != null && packViewers.Contains(owner.PlayerUID))
        {
            SendPackState(owner);
        }
    }

    private static BlockPos GetBackpackCart(ITreeAttribute status) => new(
        status.GetInt(BackpackCartXKey, 0),
        status.GetInt(BackpackCartYKey, 0),
        status.GetInt(BackpackCartZKey, 0),
        status.GetInt(BackpackCartDimensionKey, 0));

    private void HandleCompanionBackpackDeath(Entity companion)
    {
        if (serverApi == null) return;
        if (IsBackpackDeliveryActive(companion))
        {
            CancelBackpackDelivery(companion, restorePreviousActivity: false);
        }
        EntityBehaviorFeralKinshipFoxSocial? behavior =
            companion.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>();
        ItemStack? bag = behavior?.RemoveCompanionBackpackForDeath();
        if (bag == null) return;

        bool placed = TryPlaceDeadCompanionBackpack(companion, bag);
        bool dropped = false;
        if (!placed)
        {
            try
            {
                dropped = serverApi.World.SpawnItemEntity(
                    bag,
                    companion.Pos.XYZ.AddCopy(0, 0.25, 0)) != null;
            }
            catch (Exception exception)
            {
                serverApi.Logger.Error(
                    "[FeralKinshipCompanions] Could not drop backpack from dead companion {0}: {1}",
                    companion.EntityId,
                    exception.Message);
            }
        }

        if (!placed && !dropped)
        {
            // Retain the exact full stack in the durable ledger rather than
            // losing it if both world-placement paths fail.
            behavior?.SetCompanionBackpackFromLedger(bag);
        }
        PersistCompanionBackpack(companion);
        packRepository?.Save();
    }

    private bool TryPlaceDeadCompanionBackpack(Entity companion, ItemStack bag)
    {
        if (serverApi == null
            || bag.Collectible?.GetBehavior<CollectibleBehaviorGroundStorable>() is not { } groundBehavior
            || serverApi.World.GetBlock(new AssetLocation("game:groundstorage")) is not BlockGroundStorage groundBlock)
        {
            return false;
        }

        BlockPos origin = companion.Pos.AsBlockPos;
        (int X, int Z)[] offsets =
        {
            (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1),
            (1, 1), (1, -1), (-1, 1), (-1, -1)
        };
        foreach ((int x, int z) in offsets)
        {
            BlockPos pos = origin.AddCopy(x, 0, z);
            BlockPos below = pos.DownCopy();
            Block existing = serverApi.World.BlockAccessor.GetBlock(pos);
            if (existing.Replaceable < 6000
                || !serverApi.World.BlockAccessor.GetBlock(below)
                    .CanAttachBlockAt(serverApi.World.BlockAccessor, groundBlock, below, BlockFacing.UP))
            {
                continue;
            }
            try
            {
                serverApi.World.BlockAccessor.SetBlock(groundBlock.BlockId, pos);
                if (serverApi.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityGroundStorage storage)
                {
                    serverApi.World.BlockAccessor.SetBlock(0, pos);
                    continue;
                }
                storage.ForceStorageProps(groundBehavior.StorageProps);
                storage.Inventory[0].Itemstack = bag;
                storage.Inventory[0].MarkDirty();
                storage.MarkDirty(true);
                return true;
            }
            catch (Exception exception)
            {
                if (serverApi.World.BlockAccessor.GetBlock(pos).Id == groundBlock.Id)
                {
                    serverApi.World.BlockAccessor.SetBlock(0, pos);
                }
                serverApi.Logger.Warning(
                    "[FeralKinshipCompanions] Could not ground-place dead companion backpack at {0}: {1}",
                    pos,
                    exception.Message);
            }
        }
        return false;
    }

}
