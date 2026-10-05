#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private const long MenuAttentionCloseLeaseMs = 1800;
    private const long AmbientInterestScanIntervalMs = 1500;
    private const long AmbientInterestMissingGraceMs = 3500;
    private const double AmbientInterestSearchRadius = 15d;
    private const int AmbientInterestVerticalRange = 5;
    private const double AmbientStartleCampRadius = 22d;

    private sealed class AmbientInterestSource
    {
        public string Key = string.Empty;
        public string OwnerUid = string.Empty;
        public string Kind = string.Empty;
        public BlockPos Anchor = new(0);
        public Vec3d Focus = new();
        public int MaxWatchers;
        public long LastSeenAtMs;
        public long GatherUntilMs;
        public long NextRecruitAtMs;
        public bool Persistent;
    }

    private sealed class AmbientInterestReservation
    {
        public string SourceKey = string.Empty;
        public string OwnerUid = string.Empty;
        public int SlotIndex;
    }

    private readonly Dictionary<long, long> menuAttentionLeaseByEntity = new();
    private readonly Dictionary<string, AmbientInterestSource> ambientInterestSources = new(StringComparer.Ordinal);
    private readonly Dictionary<long, AmbientInterestReservation> ambientInterestReservations = new();
    private readonly Dictionary<string, long> ambientInterestCooldowns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> perimeterWalkerByOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<long>> seeOffByOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> seeOffCooldownByOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> lookoutByOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> packCartSitReservations = new(StringComparer.Ordinal);
    private long nextAmbientInterestScanAtMs;

    private void MaintainAmbientLife()
    {
        if (serverApi == null || packRepository?.Loaded != true)
        {
            return;
        }

        long now = serverApi.World.ElapsedMilliseconds;
        MaintainMenuAttention(now);
        if (now < nextAmbientInterestScanAtMs)
        {
            return;
        }

        nextAmbientInterestScanAtMs = now + AmbientInterestScanIntervalMs;
        RefreshAmbientInterestSources(now);
        CleanupAmbientLifeState(now);
        RecruitAmbientInterestWatchers(now);
    }

    private void OnAmbientExplosionEvent(string eventName, ref EnumHandling handling, IAttribute data)
    {
        if (serverApi == null || data is not ITreeAttribute tree)
        {
            return;
        }

        BlockPos? position = tree.GetBlockPos("pos");
        if (position == null)
        {
            return;
        }

        double injuryRadius = Math.Max(4d, tree.GetDouble("injureRadius", 0d));
        QueueAmbientStartle(position.ToVec3d().Add(0.5, 0.5, 0.5), position.dimension, "explosion", injuryRadius + 8d, 3);
    }

    internal void NotifyAmbientStartleFromAttack(EntityAgent attacker, EntityAgent target, DamageSource source)
    {
        if (serverApi == null || source.Type == EnumDamageType.Heal
            || source.Source is not (EnumDamageSource.Entity or EnumDamageSource.Player)
            || attacker == null || target == null || !attacker.Alive
            || attacker.Pos.Dimension != target.Pos.Dimension)
        {
            return;
        }

        QueueAmbientStartle(target.Pos.XYZ, target.Pos.Dimension, "combat", 18d, 2);
    }

    private void QueueAmbientStartle(Vec3d sourcePosition, int dimension, string kind, double radius, int maximumResponders)
    {
        if (serverApi == null || packRepository?.Loaded != true)
        {
            return;
        }

        foreach (IGrouping<string, Entity> group in loadedFoxes.Values
            .Where(fox => fox.Alive && fox.State == EnumEntityState.Active)
            .GroupBy(GetCompanionOwnerUid)
            .ToArray())
        {
            string ownerUid = group.Key;
            if (string.IsNullOrEmpty(ownerUid))
            {
                continue;
            }

            BlockPos? camp = GetActiveCairnPosition(ownerUid);
            if (camp == null || camp.dimension != group.First().Pos.Dimension
                || dimension != camp.dimension
                || HorizontalSquareDistance(sourcePosition, camp) > AmbientStartleCampRadius * AmbientStartleCampRadius)
            {
                continue;
            }

            List<Entity> candidates = group
                .Where(fox => IsInsideAmbientCamp(camp, fox)
                    && GetCompanionActivityMode(fox) == CompanionActivityMode.AtEase
                    && !IsFoxAwayFromWorld(fox) && !IsFoxIncapacitated(fox)
                    && !ShouldFoxSeekDenShelter(fox)
                    && !HasDirectedCompanionTask(fox)
                    && !forcedIdlePlans.ContainsKey(fox.EntityId)
                    && !companionIdleInvitations.ContainsKey(fox.EntityId)
                    && !IsMenuAttentionActive(fox, out _))
                .OrderBy(_ => serverApi.World.Rand.Next())
                .Take(Math.Max(1, maximumResponders))
                .ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            foreach (Entity fox in candidates)
            {
                StopAmbientCompanionTask(fox);
                forcedIdlePlans[fox.EntityId] = new FoxIdlePlan
                {
                    Kind = "startle-response",
                    OwnerEntityId = serverApi.World.PlayerByUid(ownerUid)?.Entity?.EntityId ?? 0,
                    Target = sourcePosition.Clone(),
                    LookAtTarget = sourcePosition.Clone(),
                    Animation = "idle",
                    Immediate = true,
                    DurationMs = kind == "explosion"
                        ? 3200 + serverApi.World.Rand.Next(1800)
                        : 2600 + serverApi.World.Rand.Next(1800),
                    RespectShelterWhenForced = true
                };
            }
        }
    }

    private void MaintainMenuAttention(long now)
    {
        foreach ((string ownerUid, long entityId) in socialViewByOwner
                     .Concat(perkViewByOwner)
                     .Concat(backpackViewByOwner)
                     .ToArray())
        {
            Entity? companion = serverApi?.World.GetEntityById(entityId);
            IServerPlayer? ownerPlayer = serverApi?.World.PlayerByUid(ownerUid) as IServerPlayer;
            Entity? owner = ownerPlayer?.Entity;
            if (companion?.Alive != true || owner is not EntityPlayer || !owner.Alive
                || ownerPlayer == null || !IsOwner(companion, ownerUid)
                || !IsSocialActionInRange(companion, ownerPlayer))
            {
                continue;
            }

            menuAttentionLeaseByEntity[entityId] = now + MenuAttentionCloseLeaseMs;
            QueueMenuAttentionPlan(companion, owner);
        }
    }

    private void BeginMenuAttention(Entity companion, Entity owner)
    {
        if (serverApi == null)
        {
            return;
        }

        menuAttentionLeaseByEntity[companion.EntityId] = serverApi.World.ElapsedMilliseconds + MenuAttentionCloseLeaseMs;
        QueueMenuAttentionPlan(companion, owner);
    }

    private void EndMenuAttention(long entityId)
    {
        if (serverApi != null)
        {
            menuAttentionLeaseByEntity[entityId] = serverApi.World.ElapsedMilliseconds + MenuAttentionCloseLeaseMs;
        }
    }

    private void QueueMenuAttentionPlan(Entity companion, Entity owner)
    {
        if (companion is not EntityAgent agent
            || GetCompanionActivityMode(companion) != CompanionActivityMode.AtEase
            || ShouldPauseAmbientCompanionAi(agent)
            || forcedIdlePlans.ContainsKey(companion.EntityId))
        {
            return;
        }

        StopAmbientCompanionTask(companion);
        companionIdleInvitations.Remove(companion.EntityId);
        forcedIdlePlans[companion.EntityId] = new FoxIdlePlan
        {
            Kind = "menu-attention",
            OwnerEntityId = owner.EntityId,
            Target = companion.Pos.XYZ.Clone(),
            Animation = "idle",
            Immediate = true,
            DurationMs = 30000,
            IgnoreShelter = false,
            RespectShelterWhenForced = true
        };
    }

    internal bool IsMenuAttentionActive(Entity companion, out Entity? owner)
    {
        owner = null;
        if (serverApi == null)
        {
            return false;
        }

        string ownerUid = GetCompanionOwnerUid(companion);
        bool viewOpen = socialViewByOwner.TryGetValue(ownerUid, out long socialEntityId) && socialEntityId == companion.EntityId
            || perkViewByOwner.TryGetValue(ownerUid, out long perkEntityId) && perkEntityId == companion.EntityId
            || backpackViewByOwner.TryGetValue(ownerUid, out long backpackEntityId) && backpackEntityId == companion.EntityId;
        bool leaseActive = menuAttentionLeaseByEntity.TryGetValue(companion.EntityId, out long leaseUntil)
            && leaseUntil >= serverApi.World.ElapsedMilliseconds;
        if (!viewOpen && !leaseActive)
        {
            menuAttentionLeaseByEntity.Remove(companion.EntityId);
            return false;
        }

        owner = serverApi.World.PlayerByUid(ownerUid)?.Entity;
        return owner is EntityPlayer && owner.Alive && owner.Pos.Dimension == companion.Pos.Dimension;
    }

    private void RefreshAmbientInterestSources(long now)
    {
        if (serverApi == null)
        {
            return;
        }

        foreach (IPlayer playerValue in serverApi.World.AllOnlinePlayers)
        {
            if (playerValue is not IServerPlayer player || player.Entity?.Alive != true)
            {
                continue;
            }

            BlockPos? camp = GetActiveCairnPosition(player.PlayerUID);
            float playerCampRadius = GetOwnerCampRadius(player.PlayerUID);
            if (camp == null || camp.dimension != player.Entity.Pos.Dimension
                || HorizontalSquareDistance(player.Entity.Pos.XYZ, camp) > playerCampRadius * playerCampRadius)
            {
                continue;
            }

            if (TryGetActiveSmithingTarget(player, out BlockPos? anvil, out bool hammering) && anvil != null)
            {
                RegisterAmbientInterestSource(
                    $"smith:{player.PlayerUID}", player.PlayerUID, "smithing", anvil,
                    maxWatchers: 16, persistent: true, gatherDurationMs: 3000, now
                );
                if (hammering)
                {
                    QueueAmbientStartle(
                        BlockCenter(anvil, 0.55), anvil.dimension, "hammer", 16d, 2);
                }
            }

            ScanInterestingBlocks(player.PlayerUID, player.Entity.Pos.AsBlockPos, now);
        }
    }

    private void ScanInterestingBlocks(string ownerUid, BlockPos center, long now)
    {
        if (serverApi == null)
        {
            return;
        }

        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        int range = (int)Math.Ceiling(AmbientInterestSearchRadius);
        double radiusSquared = AmbientInterestSearchRadius * AmbientInterestSearchRadius;
        BlockPos pos = new(center.dimension);
        for (int x = center.X - range; x <= center.X + range; x++)
        {
            int dx = x - center.X;
            for (int z = center.Z - range; z <= center.Z + range; z++)
            {
                int dz = z - center.Z;
                if (dx * dx + dz * dz > radiusSquared)
                {
                    continue;
                }

                for (int y = Math.Max(1, center.Y - AmbientInterestVerticalRange);
                     y <= center.Y + AmbientInterestVerticalRange; y++)
                {
                    pos.Set(x, y, z);
                    BlockEntity? blockEntity = blocks.GetBlockEntity(pos);
                    if (blockEntity is BlockEntityBloomery { IsBurning: true })
                    {
                        RegisterAmbientInterestSource(
                            BlockInterestKey(ownerUid, "bloomery", pos), ownerUid, "bloomery", pos,
                            maxWatchers: 3, persistent: false, gatherDurationMs: 26000, now
                        );
                    }
                    else if (blockEntity is BlockEntityFirepit firepit)
                    {
                        bool hotPreparedFood = HasHotPreparedFood(firepit);
                        if (hotPreparedFood)
                        {
                            RegisterAmbientInterestSource(
                                BlockInterestKey(ownerUid, "hotfood", pos), ownerUid, "hot-food", pos,
                                maxWatchers: 8, persistent: true, gatherDurationMs: 5000, now
                            );
                        }
                        else if (firepit.IsBurning)
                        {
                            string kind = firepit.inputSlot.Empty ? "campfire" : "cooking";
                            RegisterAmbientInterestSource(
                                BlockInterestKey(ownerUid, kind, pos), ownerUid, kind, pos,
                                maxWatchers: 3, persistent: false, gatherDurationMs: 24000, now
                            );
                        }
                    }
                    else if (blockEntity is BlockEntityGroundStorage groundStorage
                        && HasHotPreparedFood(groundStorage.Inventory))
                    {
                        RegisterAmbientInterestSource(
                            BlockInterestKey(ownerUid, "hotfood", pos), ownerUid, "hot-food", pos,
                            maxWatchers: 8, persistent: true, gatherDurationMs: 5000, now
                        );
                    }
                }
            }
        }
    }

    private static bool HasHotPreparedFood(BlockEntityFirepit firepit)
    {
        return IsPreparedFood(firepit.outputSlot.Itemstack) && firepit.OutputStackTemp > 40f
            || IsPreparedFood(firepit.inputSlot.Itemstack) && firepit.InputStackTemp > 40f;
    }

    private static bool HasHotPreparedFood(InventoryBase inventory)
    {
        foreach (ItemSlot slot in inventory)
        {
            ItemStack? stack = slot.Itemstack;
            if (IsPreparedFood(stack) && stack!.Collectible.GetTemperature(inventory.Api.World, stack) > 40f)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPreparedFood(ItemStack? stack)
    {
        if (stack?.Collectible == null)
        {
            return false;
        }

        string path = stack.Collectible.Code?.Path ?? string.Empty;
        return stack.Collectible is BlockCookedContainer or BlockMeal or BlockPie
            || path.Contains("cooked", StringComparison.OrdinalIgnoreCase)
            || path.Contains("meal", StringComparison.OrdinalIgnoreCase)
            || path.Contains("pie", StringComparison.OrdinalIgnoreCase)
            || path.Contains("bread", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetActiveSmithingTarget(IServerPlayer player, out BlockPos? anvil, out bool hammering)
    {
        anvil = null;
        hammering = false;
        EntityPlayer entity = player.Entity;
        ItemSlot? activeSlot = player.InventoryManager.ActiveHotbarSlot;
        ItemStack? activeStack = activeSlot?.Itemstack;
        if (activeSlot == null || activeStack == null
            || activeStack.Collectible.GetTool(activeSlot) != EnumTool.Hammer)
        {
            return false;
        }

        // The third-person hammer animation is short and is not reliably visible
        // to the server between the ambient-life scans.  These two signals cover
        // the authoritative held-use state; the animation remains a useful
        // fallback for single strikes.
        hammering = entity.ServerControls.LeftMouseDown
            || entity.ServerControls.HandUse == EnumHandInteract.HeldItemAttack
            || activeStack.TempAttributes.GetBool("isAnvilAction")
            || entity.AnimManager?.IsAnimationActive("hammerhit") == true;

        BlockSelection? selection = player.CurrentBlockSelection;
        if (selection?.Position != null
            && serverApi?.World.BlockAccessor.GetBlockEntity(selection.Position) is BlockEntityAnvil selectedAnvil
            && selectedAnvil.WorkItemStack != null)
        {
            bool closeEnoughToAnvil = entity.Pos.SquareDistanceTo(BlockCenter(selection.Position, 0.55)) <= 6.25 * 6.25;
            if (hammering || closeEnoughToAnvil)
            {
                anvil = selection.Position.Copy();
                return true;
            }
        }

        if (!hammering || serverApi == null)
        {
            return false;
        }

        BlockPos center = entity.Pos.AsBlockPos;
        BlockPos check = new(center.dimension);
        for (int dx = -4; dx <= 4; dx++)
        for (int dz = -4; dz <= 4; dz++)
        for (int dy = -2; dy <= 2; dy++)
        {
            check.Set(center.X + dx, center.Y + dy, center.Z + dz);
            if (serverApi.World.BlockAccessor.GetBlockEntity(check) is BlockEntityAnvil nearbyAnvil
                && nearbyAnvil.WorkItemStack != null)
            {
                anvil = check.Copy();
                return true;
            }
        }

        return false;
    }

    private void RegisterAmbientInterestSource(
        string key,
        string ownerUid,
        string kind,
        BlockPos anchor,
        int maxWatchers,
        bool persistent,
        int gatherDurationMs,
        long now)
    {
        bool isNew = !ambientInterestSources.TryGetValue(key, out AmbientInterestSource? source);
        source ??= new AmbientInterestSource { Key = key };
        source.OwnerUid = ownerUid;
        source.Kind = kind;
        source.Anchor = anchor.Copy();
        source.Focus = BlockCenter(anchor, 0.55);
        source.MaxWatchers = maxWatchers;
        source.Persistent = persistent;
        source.LastSeenAtMs = now;
        if (isNew)
        {
            source.GatherUntilMs = now + gatherDurationMs;
            source.NextRecruitAtMs = now + Math.Abs(key.GetHashCode(StringComparison.Ordinal) % 1200);
            ambientInterestSources[key] = source;
        }
        else if (persistent)
        {
            source.GatherUntilMs = now + gatherDurationMs;
        }
    }

    private void RecruitAmbientInterestWatchers(long now)
    {
        foreach (AmbientInterestSource source in ambientInterestSources.Values
            .OrderByDescending(SourcePriority).ThenBy(source => source.Key, StringComparer.Ordinal).ToArray())
        {
            if (source.NextRecruitAtMs > now || (!source.Persistent && source.GatherUntilMs < now))
            {
                continue;
            }

            int watcherCount = ambientInterestReservations.Values.Count(r => r.SourceKey == source.Key);
            if (watcherCount >= source.MaxWatchers)
            {
                continue;
            }

            int recruitBudget = source.Kind == "smithing" ? 3 : 1;
            for (int recruited = 0; recruited < recruitBudget && watcherCount < source.MaxWatchers; recruited++)
            {
                Entity? companion = loadedFoxes.Values
                    .Where(candidate => IsEligibleForAmbientInterest(candidate, source, now))
                    .OrderBy(candidate => candidate.Pos.SquareDistanceTo(source.Focus))
                    .ThenBy(candidate => candidate.EntityId)
                    .FirstOrDefault();
                if (companion == null || !TryReserveAmbientInterestSlot(companion, source, out Vec3d? slotTarget)
                    || slotTarget == null)
                {
                    source.NextRecruitAtMs = now + 2500;
                    break;
                }

                ICoreServerAPI api = serverApi!;
                string animation = source.Kind == "hot-food" || api.World.Rand.NextDouble() < 0.45
                    ? "sit"
                    : "idle";
                int duration = source.Persistent
                    ? 14000 + api.World.Rand.Next(10000)
                    : 9000 + api.World.Rand.Next(8000);
                forcedIdlePlans[companion.EntityId] = new FoxIdlePlan
                {
                    Kind = "interest-watch",
                    Target = slotTarget,
                    LookAtTarget = source.Focus.Clone(),
                    Animation = animation,
                    DurationMs = duration,
                    MoveSpeed = 0.018f,
                    TargetDistance = 0.65f,
                    ReservationKey = source.Key,
                    IgnoreShelter = false,
                    RespectShelterWhenForced = true
                };
                StopAmbientCompanionTask(companion);
                watcherCount++;
                source.NextRecruitAtMs = now + RecruitIntervalMs(source.Kind);
            }
        }
    }

    private bool IsEligibleForAmbientInterest(Entity companion, AmbientInterestSource source, long now)
    {
        if (companion is not EntityAgent agent || !companion.Alive || !IsOwner(companion, source.OwnerUid)
            || companion.Pos.Dimension != source.Anchor.dimension || IsFoxAwayFromWorld(companion)
            || IsFoxIncapacitated(companion) || GetCompanionActivityMode(companion) != CompanionActivityMode.AtEase
            || ShouldPauseAmbientCompanionAi(agent) || ShouldFoxSeekDenShelter(companion)
            || ambientInterestReservations.ContainsKey(companion.EntityId)
            || forcedIdlePlans.ContainsKey(companion.EntityId)
            || companionIdleInvitations.ContainsKey(companion.EntityId)
            || IsMenuAttentionActive(companion, out _)
            || companion.Pos.SquareDistanceTo(source.Focus) > 30 * 30)
        {
            return false;
        }

        string cooldownKey = InterestCooldownKey(companion.EntityId, source.Key);
        return !ambientInterestCooldowns.TryGetValue(cooldownKey, out long cooldownUntil) || cooldownUntil <= now;
    }

    private bool TryReserveAmbientInterestSlot(Entity companion, AmbientInterestSource source, out Vec3d? target)
    {
        target = null;
        HashSet<int> occupied = ambientInterestReservations.Values
            .Where(reservation => reservation.SourceKey == source.Key)
            .Select(reservation => reservation.SlotIndex)
            .ToHashSet();
        int slotCount = source.Kind == "hot-food" || source.Kind == "smithing" ? 18 : 10;
        int start = (int)(Math.Abs(companion.EntityId) % slotCount);
        for (int offset = 0; offset < slotCount; offset++)
        {
            int slot = (start + offset) % slotCount;
            if (occupied.Contains(slot))
            {
                continue;
            }

            double radius = InterestSlotRadius(source.Kind, slot);
            double angle = StableSourceAngle(source.Key) + slot * GameMath.TWOPI / slotCount;
            double x = source.Anchor.X + 0.5 + Math.Cos(angle) * radius;
            double z = source.Anchor.Z + 0.5 + Math.Sin(angle) * radius;
            if (!TryFindSafeAmbientGround(companion, x, z, source.Anchor.Y, out Vec3d? candidate)
                || candidate == null)
            {
                continue;
            }

            ambientInterestReservations[companion.EntityId] = new AmbientInterestReservation
            {
                SourceKey = source.Key,
                OwnerUid = source.OwnerUid,
                SlotIndex = slot
            };
            target = candidate;
            return true;
        }

        return false;
    }

    internal bool IsAmbientInterestStillValid(Entity companion, string sourceKey)
    {
        if (serverApi == null || !ambientInterestSources.TryGetValue(sourceKey, out AmbientInterestSource? source)
            || source.LastSeenAtMs + AmbientInterestMissingGraceMs < serverApi.World.ElapsedMilliseconds
            || !ambientInterestReservations.TryGetValue(companion.EntityId, out AmbientInterestReservation? reservation)
            || reservation.SourceKey != sourceKey || !IsOwner(companion, source.OwnerUid)
            || companion.Pos.Dimension != source.Anchor.dimension)
        {
            return false;
        }

        return source.Persistent || source.GatherUntilMs + 20000 >= serverApi.World.ElapsedMilliseconds;
    }

    internal bool IsAmbientInterestPersistent(string sourceKey) =>
        ambientInterestSources.TryGetValue(sourceKey, out AmbientInterestSource? source) && source.Persistent;

    internal void ReleaseAmbientIdleReservation(Entity companion, FoxIdlePlan? plan)
    {
        if (plan?.Kind is "pack-cart-sit" or "work-cart-sit"
            && !string.IsNullOrEmpty(plan.ReservationKey)
            && packCartSitReservations.TryGetValue(plan.ReservationKey, out long holderId)
            && holderId == companion.EntityId)
        {
            packCartSitReservations.Remove(plan.ReservationKey);
        }

        if (plan?.Kind == "interest-watch"
            && ambientInterestReservations.Remove(companion.EntityId, out AmbientInterestReservation? reservation)
            && serverApi != null)
        {
            long cooldown = plan.ReservationKey.Contains("smith:", StringComparison.Ordinal)
                ? 3500
                : plan.ReservationKey.Contains(":hotfood:", StringComparison.Ordinal) ? 9000 : 45000;
            ambientInterestCooldowns[InterestCooldownKey(companion.EntityId, reservation.SourceKey)] =
                serverApi.World.ElapsedMilliseconds + cooldown;
        }

        if (plan?.Kind == "perimeter-walk")
        {
            string ownerUid = GetCompanionOwnerUid(companion);
            if (perimeterWalkerByOwner.TryGetValue(ownerUid, out long walkerId) && walkerId == companion.EntityId)
            {
                perimeterWalkerByOwner.Remove(ownerUid);
            }
        }

        if (plan?.Kind == "see-off")
        {
            string ownerUid = GetCompanionOwnerUid(companion);
            if (seeOffByOwner.TryGetValue(ownerUid, out HashSet<long>? watchers))
            {
                watchers.Remove(companion.EntityId);
                if (watchers.Count == 0)
                {
                    seeOffByOwner.Remove(ownerUid);
                    if (serverApi != null)
                    {
                        seeOffCooldownByOwner[ownerUid] = serverApi.World.ElapsedMilliseconds + 45000;
                    }
                }
            }
        }

        if (plan?.Kind == "camp-lookout")
        {
            string ownerUid = GetCompanionOwnerUid(companion);
            if (lookoutByOwner.TryGetValue(ownerUid, out long walkerId) && walkerId == companion.EntityId)
            {
                lookoutByOwner.Remove(ownerUid);
            }
        }
    }

    internal bool TryCreateSeeOffPlan(Entity companion, out FoxIdlePlan? plan, bool forced = false)
    {
        plan = null;
        if (serverApi == null || companion is not EntityAgent agent
            || !forced && serverApi.World.Rand.NextDouble() >= 0.32
            || GetCompanionActivityMode(companion) != CompanionActivityMode.AtEase
            || ShouldPauseAmbientCompanionAi(agent) || ShouldFoxSeekDenShelter(companion))
        {
            return false;
        }

        string ownerUid = GetCompanionOwnerUid(companion);
        BlockPos? camp = GetActiveCairnPosition(ownerUid);
        Entity? owner = serverApi.World.PlayerByUid(ownerUid)?.Entity;
        if (camp == null || owner is not EntityPlayer || !owner.Alive
            || camp.dimension != companion.Pos.Dimension || camp.dimension != owner.Pos.Dimension
            || !IsInsideAmbientCamp(camp, companion) || IsInsideAmbientCamp(camp, owner))
        {
            return false;
        }

        long now = serverApi.World.ElapsedMilliseconds;
        if (seeOffCooldownByOwner.TryGetValue(ownerUid, out long cooldownUntil) && cooldownUntil > now)
        {
            return false;
        }

        if (!seeOffByOwner.TryGetValue(ownerUid, out HashSet<long>? watchers))
        {
            watchers = new HashSet<long>();
            seeOffByOwner[ownerUid] = watchers;
        }
        if (watchers.Count >= 2) return false;

        double angle = Math.Atan2(companion.Pos.Z - (camp.Z + 0.5), companion.Pos.X - (camp.X + 0.5));
        if (Math.Abs(companion.Pos.X - (camp.X + 0.5)) + Math.Abs(companion.Pos.Z - (camp.Z + 0.5)) < 0.25)
        {
            angle = StableSourceAngle($"see-off:{ownerUid}:{companion.EntityId}");
        }
        if (!TryGetCampEdgeTarget(companion, camp, angle, out Vec3d? target) || target == null)
        {
            return false;
        }

        watchers.Add(companion.EntityId);
        plan = new FoxIdlePlan
        {
            Kind = "see-off",
            OwnerEntityId = owner.EntityId,
            Target = target,
            Animation = "sit",
            DurationMs = 11000 + serverApi.World.Rand.Next(8000),
            MoveSpeed = 0.018f,
            TargetDistance = 0.75f
        };
        return true;
    }

    internal bool TryCreateCampLookoutPlan(Entity companion, out FoxIdlePlan? plan, bool forced = false)
    {
        plan = null;
        if (serverApi == null || companion is not EntityAgent agent
            || !forced && serverApi.World.Rand.NextDouble() >= 0.035
            || GetCompanionActivityMode(companion) != CompanionActivityMode.AtEase
            || ShouldPauseAmbientCompanionAi(agent) || ShouldFoxSeekDenShelter(companion))
        {
            return false;
        }

        string ownerUid = GetCompanionOwnerUid(companion);
        BlockPos? camp = GetActiveCairnPosition(ownerUid);
        Entity? owner = serverApi.World.PlayerByUid(ownerUid)?.Entity;
        if (camp == null || owner is not EntityPlayer || !owner.Alive
            || camp.dimension != companion.Pos.Dimension || camp.dimension != owner.Pos.Dimension
            || !IsInsideAmbientCamp(camp, companion) || !IsInsideAmbientCamp(camp, owner)
            || lookoutByOwner.ContainsKey(ownerUid))
        {
            return false;
        }

        double angle = Math.Atan2(companion.Pos.Z - (camp.Z + 0.5), companion.Pos.X - (camp.X + 0.5));
        if (!TryGetCampEdgeTarget(companion, camp, angle, out Vec3d? target) || target == null)
        {
            return false;
        }

        lookoutByOwner[ownerUid] = companion.EntityId;
        plan = new FoxIdlePlan
        {
            Kind = "camp-lookout",
            Target = target,
            Animation = "sit",
            DurationMs = 15000 + serverApi.World.Rand.Next(10000),
            MoveSpeed = 0.018f,
            TargetDistance = 0.75f
        };
        return true;
    }

    internal bool TryCreateScentInvestigationPlan(Entity companion, out FoxIdlePlan? plan, bool forced = false)
    {
        plan = null;
        if (serverApi == null || companion is not EntityAgent agent
            || !forced && serverApi.World.Rand.NextDouble() >= 0.11
            || GetCompanionActivityMode(companion) != CompanionActivityMode.AtEase
            || ShouldPauseAmbientCompanionAi(agent) || ShouldFoxSeekDenShelter(companion))
        {
            return false;
        }

        string ownerUid = GetCompanionOwnerUid(companion);
        BlockPos? camp = GetActiveCairnPosition(ownerUid);
        Entity? owner = serverApi.World.PlayerByUid(ownerUid)?.Entity;
        if (camp == null || owner is not EntityPlayer || !owner.Alive
            || camp.dimension != companion.Pos.Dimension || camp.dimension != owner.Pos.Dimension
            || !IsInsideAmbientCamp(camp, companion) || !IsInsideAmbientCamp(camp, owner))
        {
            return false;
        }

        List<Vec3d> points = new();
        double baseAngle = StableSourceAngle($"scent:{ownerUid}:{companion.EntityId}:{serverApi.World.ElapsedMilliseconds / 30000}");
        for (int index = 0; index < 5 && points.Count < 3; index++)
        {
            double angle = baseAngle + index * 1.7 + serverApi.World.Rand.NextDouble() * 0.45;
            double radius = 3.5 + serverApi.World.Rand.NextDouble() * 7.5;
            double x = camp.X + 0.5 + Math.Cos(angle) * radius;
            double z = camp.Z + 0.5 + Math.Sin(angle) * radius;
            if (points.Any(existing =>
            {
                double dx = existing.X - x;
                double dz = existing.Z - z;
                return dx * dx + dz * dz < 2.2 * 2.2;
            }))
            {
                continue;
            }

            if (TryFindSafeAmbientGround(companion, x, z, camp.Y, out Vec3d? point) && point != null)
            {
                points.Add(point);
            }
        }

        if (points.Count < 2) return false;
        plan = new FoxIdlePlan
        {
            Kind = "scent-investigation",
            Target = points[0],
            Animation = "sniff",
            DurationMs = 18000 + serverApi.World.Rand.Next(12000),
            MoveSpeed = 0.018f,
            TargetDistance = 0.7f,
            Waypoints = points
        };
        return true;
    }

    internal bool TryCreatePerimeterWalkPlan(Entity companion, out FoxIdlePlan? plan)
    {
        plan = null;
        if (serverApi == null || companion is not EntityAgent agent
            || serverApi.World.Rand.NextDouble() >= 0.012
            || GetCompanionActivityMode(companion) != CompanionActivityMode.AtEase
            || ShouldPauseAmbientCompanionAi(agent) || ShouldFoxSeekDenShelter(companion))
        {
            return false;
        }

        string ownerUid = GetCompanionOwnerUid(companion);
        BlockPos? camp = GetActiveCairnPosition(ownerUid);
        Entity? owner = serverApi.World.PlayerByUid(ownerUid)?.Entity;
        if (camp == null || owner == null || camp.dimension != companion.Pos.Dimension
            || !IsCompanionOwnerInsidePackCamp(companion, owner)
            || perimeterWalkerByOwner.ContainsKey(ownerUid))
        {
            return false;
        }

        double startAngle = Math.Atan2(companion.Pos.Z - (camp.Z + 0.5), companion.Pos.X - (camp.X + 0.5));
        List<Vec3d> waypoints = new();
        const int waypointCount = 12;
        for (int index = 0; index <= waypointCount; index++)
        {
            double angle = startAngle + index * GameMath.TWOPI / waypointCount;
            Vec3d? candidate = null;
            float campRadius = GetCompanionCampRadius(companion);
            foreach (double radius in new[] { campRadius - 2.75, campRadius - 4.5, campRadius - 6.0 })
            {
                double x = camp.X + 0.5 + Math.Cos(angle) * radius;
                double z = camp.Z + 0.5 + Math.Sin(angle) * radius;
                if (TryFindSafeAmbientGround(companion, x, z, camp.Y, out candidate) && candidate != null)
                {
                    break;
                }
            }

            if (candidate != null)
            {
                waypoints.Add(candidate);
            }
        }

        if (waypoints.Count < 10)
        {
            return false;
        }

        perimeterWalkerByOwner[ownerUid] = companion.EntityId;
        plan = new FoxIdlePlan
        {
            Kind = "perimeter-walk",
            Target = waypoints[0],
            Animation = "idle",
            DurationMs = 120000,
            MoveSpeed = 0.018f,
            TargetDistance = 0.8f,
            Waypoints = waypoints,
            IgnoreShelter = false
        };
        return true;
    }

    private bool TryGetCampEdgeTarget(Entity companion, BlockPos camp, double angle, out Vec3d? target)
    {
        target = null;
        float campRadius = GetCompanionCampRadius(companion);
        foreach (double radius in new[] { campRadius - 2.75, campRadius - 4.5, campRadius - 6.0 })
        {
            double x = camp.X + 0.5 + Math.Cos(angle) * radius;
            double z = camp.Z + 0.5 + Math.Sin(angle) * radius;
            if (TryFindSafeAmbientGround(companion, x, z, camp.Y, out target) && target != null)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInsideAmbientCamp(BlockPos camp, Entity entity)
    {
        if (camp.dimension != entity.Pos.Dimension) return false;
        double dx = entity.Pos.X - (camp.X + 0.5);
        double dz = entity.Pos.Z - (camp.Z + 0.5);
        float radius = GetCompanionCampRadius(entity);
        return dx * dx + dz * dz <= radius * radius;
    }

    private bool TryFindSafeAmbientGround(Entity companion, double x, double z, int centerY, out Vec3d? candidate)
    {
        candidate = null;
        if (serverApi == null)
        {
            return false;
        }

        int blockX = (int)Math.Floor(x);
        int blockZ = (int)Math.Floor(z);
        for (int offset = 4; offset >= -6; offset--)
        {
            int y = centerY + offset;
            BlockPos feet = new(blockX, y, blockZ, companion.Pos.Dimension);
            Vec3d position = new(x, y + companion.Pos.Dimension * BlockPos.DimensionBoundary, z);
            if (IsSafeAmbientPosition(feet, position, companion))
            {
                candidate = position;
                return true;
            }
        }

        return false;
    }

    private bool IsSafeAmbientPosition(BlockPos feet, Vec3d candidate, Entity companion)
    {
        if (serverApi == null)
        {
            return false;
        }

        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        Block belowBlock = blocks.GetBlock(feet.DownCopy());
        if (!belowBlock.SideSolid[BlockFacing.UP.Index]
            || blocks.GetBlock(feet, BlockLayersAccess.Fluid).IsLiquid()
            || blocks.GetBlock(feet.UpCopy(), BlockLayersAccess.Fluid).IsLiquid()
            || serverApi.World.CollisionTester.IsColliding(blocks, companion.Properties.SpawnCollisionBox, candidate, false))
        {
            return false;
        }

        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            BlockPos nearby = feet.AddCopy(dx, 0, dz);
            if (IsStaticAmbientHazard(nearby))
            {
                return false;
            }

            Block solid = blocks.GetBlock(nearby);
            Block liquid = blocks.GetBlock(nearby, BlockLayersAccess.Fluid);
            string path = solid.Code?.Path ?? string.Empty;
            if (liquid.LiquidCode == "lava"
                || path.Contains("fire", StringComparison.OrdinalIgnoreCase)
                || path.Contains("lava", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Reject exposed ledge squares. Pathfinding remains authoritative, but
        // this keeps an attraction slot from deliberately choosing a drop.
        foreach (BlockFacing facing in BlockFacing.HORIZONTALS)
        {
            BlockPos neighborFeet = feet.AddCopy(facing);
            bool nearbySupport = blocks.GetBlock(neighborFeet.DownCopy()).SideSolid[BlockFacing.UP.Index]
                || blocks.GetBlock(neighborFeet.DownCopy(2)).SideSolid[BlockFacing.UP.Index];
            if (!nearbySupport)
            {
                return false;
            }
        }

        return serverApi.World.GetEntitiesAround(candidate, 0.8f, 1.5f,
            nearby => nearby != companion && nearby.Alive && !IsFoxAwayFromWorld(nearby)).Length == 0;
    }

    private bool IsStaticAmbientHazard(BlockPos pos)
    {
        if (serverApi == null)
        {
            return true;
        }

        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        Block block = blocks.GetBlock(pos);
        string path = block.Code?.Path ?? string.Empty;
        if (block is BlockDamageOnTouch
            || path.Contains("lava", StringComparison.OrdinalIgnoreCase)
            || path.Contains("firepit", StringComparison.OrdinalIgnoreCase)
            || path.Contains("forge", StringComparison.OrdinalIgnoreCase)
            || path.Contains("bloomery", StringComparison.OrdinalIgnoreCase)
            || path.Contains("anvil", StringComparison.OrdinalIgnoreCase)
            || path.Contains("ingotmold", StringComparison.OrdinalIgnoreCase)
            || path.Contains("toolmold", StringComparison.OrdinalIgnoreCase)
            || path.Contains("kiln", StringComparison.OrdinalIgnoreCase)
            || path.Contains("cactus", StringComparison.OrdinalIgnoreCase)
            || path.Contains("thorn", StringComparison.OrdinalIgnoreCase)
            || path.Contains("spike", StringComparison.OrdinalIgnoreCase)
            || path.Contains("windmill", StringComparison.OrdinalIgnoreCase)
            || path.Contains("helvehammer", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        BlockEntity? blockEntity = blocks.GetBlockEntity(pos);
        return blockEntity is BlockEntityAnvil
            or BlockEntityAnvilPart
            or BlockEntityForge
            or BlockEntityBloomery
            or BlockEntityFirepit { IsBurning: true }
            or BlockEntityBoiler { IsBurning: true }
            or BlockEntityOven { IsBurning: true }
            or BlockEntityCoalPile { IsBurning: true }
            or BlockEntityGroundStorage { IsBurning: true }
            or BlockEntityIngotMold { IsHot: true }
            or BlockEntityToolMold { IsHot: true };
    }

    private void CleanupAmbientLifeState(long now)
    {
        foreach (string key in ambientInterestSources
            .Where(entry => entry.Value.LastSeenAtMs + AmbientInterestMissingGraceMs < now)
            .Select(entry => entry.Key).ToArray())
        {
            ambientInterestSources.Remove(key);
        }

        foreach ((long entityId, AmbientInterestReservation reservation) in ambientInterestReservations.ToArray())
        {
            Entity? companion = serverApi?.World.GetEntityById(entityId);
            if (companion?.Alive != true || !ambientInterestSources.ContainsKey(reservation.SourceKey))
            {
                ambientInterestReservations.Remove(entityId);
            }
        }

        foreach (string key in ambientInterestCooldowns.Where(entry => entry.Value < now).Select(entry => entry.Key).ToArray())
        {
            ambientInterestCooldowns.Remove(key);
        }

        foreach ((string ownerUid, long entityId) in perimeterWalkerByOwner.ToArray())
        {
            if (serverApi?.World.GetEntityById(entityId)?.Alive != true)
            {
                perimeterWalkerByOwner.Remove(ownerUid);
            }
        }

        foreach ((string ownerUid, HashSet<long> watchers) in seeOffByOwner.ToArray())
        {
            watchers.RemoveWhere(entityId => serverApi?.World.GetEntityById(entityId)?.Alive != true);
            if (watchers.Count == 0)
            {
                seeOffByOwner.Remove(ownerUid);
                if (!seeOffCooldownByOwner.ContainsKey(ownerUid))
                {
                    seeOffCooldownByOwner[ownerUid] = now + 45000;
                }
            }
        }

        foreach ((string ownerUid, long entityId) in lookoutByOwner.ToArray())
        {
            if (serverApi?.World.GetEntityById(entityId)?.Alive != true)
            {
                lookoutByOwner.Remove(ownerUid);
            }
        }

        foreach (string ownerUid in seeOffCooldownByOwner
            .Where(entry => entry.Value <= now)
            .Select(entry => entry.Key).ToArray())
        {
            seeOffCooldownByOwner.Remove(ownerUid);
        }

    }

    private void ReleaseAmbientLifeForEntity(Entity companion)
    {
        menuAttentionLeaseByEntity.Remove(companion.EntityId);
        ambientInterestReservations.Remove(companion.EntityId);
        string ownerUid = GetCompanionOwnerUid(companion);
        if (perimeterWalkerByOwner.TryGetValue(ownerUid, out long entityId) && entityId == companion.EntityId)
        {
            perimeterWalkerByOwner.Remove(ownerUid);
        }

        if (seeOffByOwner.TryGetValue(ownerUid, out HashSet<long>? watchers))
        {
            watchers.Remove(companion.EntityId);
            if (watchers.Count == 0) seeOffByOwner.Remove(ownerUid);
        }

        if (lookoutByOwner.TryGetValue(ownerUid, out long lookoutId) && lookoutId == companion.EntityId)
        {
            lookoutByOwner.Remove(ownerUid);
        }
    }

    private void ReleaseAmbientLifeForOwner(string ownerUid)
    {
        foreach (long entityId in ambientInterestReservations
            .Where(entry => entry.Value.OwnerUid == ownerUid).Select(entry => entry.Key).ToArray())
        {
            ambientInterestReservations.Remove(entityId);
        }
        foreach (string key in ambientInterestSources
            .Where(entry => entry.Value.OwnerUid == ownerUid).Select(entry => entry.Key).ToArray())
        {
            ambientInterestSources.Remove(key);
        }
        perimeterWalkerByOwner.Remove(ownerUid);
        seeOffByOwner.Remove(ownerUid);
        seeOffCooldownByOwner.Remove(ownerUid);
        lookoutByOwner.Remove(ownerUid);
        packCartSitReservations.Keys
            .Where(key => key.StartsWith(ownerUid + ":", StringComparison.Ordinal))
            .ToList()
            .ForEach(key => packCartSitReservations.Remove(key));
    }

    private void ClearAmbientLifeState()
    {
        menuAttentionLeaseByEntity.Clear();
        ambientInterestSources.Clear();
        ambientInterestReservations.Clear();
        ambientInterestCooldowns.Clear();
        perimeterWalkerByOwner.Clear();
        seeOffByOwner.Clear();
        seeOffCooldownByOwner.Clear();
        lookoutByOwner.Clear();
        packCartSitReservations.Clear();
        nextAmbientInterestScanAtMs = 0;
    }

    private static string BlockInterestKey(string ownerUid, string kind, BlockPos pos) =>
        $"{ownerUid}:{kind}:{pos.dimension}:{pos.X}:{pos.Y}:{pos.Z}";

    private static string InterestCooldownKey(long entityId, string sourceKey) => $"{entityId}:{sourceKey}";

    private static int SourcePriority(AmbientInterestSource source) => source.Kind switch
    {
        "smithing" => 3,
        "hot-food" => 2,
        _ => 1
    };

    private static int RecruitIntervalMs(string kind) => kind switch
    {
        "smithing" => 1100,
        "hot-food" => 1600,
        _ => 3400
    };

    private static double InterestSlotRadius(string kind, int slot) => kind switch
    {
        "bloomery" or "campfire" or "cooking" => 4.5 + slot / 6 * 1.5,
        "hot-food" => 3.0 + slot / 6 * 1.4,
        _ => 3.2 + slot / 6 * 1.35
    };

    private static double StableSourceAngle(string key) =>
        Math.Abs(key.GetHashCode(StringComparison.Ordinal) % 6283) / 1000d;

    private static Vec3d BlockCenter(BlockPos pos, double yOffset) => new(
        pos.X + 0.5,
        pos.Y + yOffset + pos.dimension * BlockPos.DimensionBoundary,
        pos.Z + 0.5
    );

    private static double HorizontalSquareDistance(Vec3d position, BlockPos block)
    {
        double dx = position.X - (block.X + 0.5);
        double dz = position.Z - (block.Z + 0.5);
        return dx * dx + dz * dz;
    }
}
