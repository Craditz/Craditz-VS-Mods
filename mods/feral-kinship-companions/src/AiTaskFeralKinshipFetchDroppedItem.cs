#nullable enable

using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// An interruptible communal chore: collect one reserved dropped stack and
/// carry it to the owner's collection box. Cargo is stored on the fox itself,
/// so an unload, command, shelter interruption, or full box cannot erase it.
/// </summary>
public sealed class AiTaskFeralKinshipFetchDroppedItem : AiTaskBase
{
    private const int BaseMinCooldownMs = 14000;
    private const int BaseMaxCooldownMs = 32000;
    public bool CommandedCourier { get; set; }
    public bool GatherFinishedProducts { get; set; }
    public bool RemoveFlowers { get; set; }
    public bool ShovelSnow { get; set; }
    public bool ShovelCharcoal { get; set; }
    public bool RouteStorage { get; set; }
    public bool DutyCoordinator { get; set; }

    private FoxStoragePlan? plan;
    private bool headingToStorage;
    private bool done;
    private bool stuck;
    private long nextCheckAtMs;
    private bool observedAssignedJob;
    private bool breakingSnow;
    private long snowBreakAtMs;
    private bool breakingCharcoal;
    private long charcoalBreakAtMs;
    private string attackAnimation = string.Empty;
    private bool travelAnimationActive;
    private bool depositFailed;
    private bool depositIncompatible;
    private bool suppressImmediateStorageFailure;
    private readonly HashSet<BlockPos> failedStorageTargets = new();
    private readonly CompanionNavigation navigation;
    private Vec3d plannedTarget = new();

    public AiTaskFeralKinshipFetchDroppedItem(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        // This task is deliberately declared twice in the companion AI: once
        // as a high-priority cart courier and once as a low-priority Ground
        // Cleanup duty. Read the discriminator directly so the two instances
        // can never both fall back to the duty mode if generic task population
        // skips it.
        CommandedCourier = taskConfig["commandedCourier"].AsBool(false);
        GatherFinishedProducts = taskConfig["gatherFinishedProducts"].AsBool(false);
        RemoveFlowers = taskConfig["removeFlowers"].AsBool(false);
        ShovelSnow = taskConfig["shovelSnow"].AsBool(false);
        ShovelCharcoal = taskConfig["shovelCharcoal"].AsBool(false);
        RouteStorage = taskConfig["routeStorage"].AsBool(false);
        DutyCoordinator = taskConfig["dutyCoordinator"].AsBool(false);
        navigation = new CompanionNavigation(entity, pathTraverser);
    }

    protected override void SetDefaultValues()
    {
        base.SetDefaultValues();
        Priority = 1.365f;
        priorityForCancel = 1.365f;
        MinCooldownMs = BaseMinCooldownMs;
        MaxCooldownMs = BaseMaxCooldownMs;
    }

    public override bool ShouldExecute()
    {
        FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        bool commandedJob = FeralKinshipCompanionSystem.HasFoxCommandedStorageJob(entity);
        if (CommandedCourier ? !commandedJob : commandedJob) return false;
        if (!CommandedCourier
            && FeralKinshipCompanionSystem.ShouldPauseAmbientCompanionAi(entity, this)) return false;
        bool hasCargo = FeralKinshipCompanionSystem.HasFoxStorageCargo(entity);
        bool assignedJob = CommandedCourier ? commandedJob : hasCargo;
        if (!CommandedCourier && !IsConfiguredDutyEnabled() && !hasCargo && !RouteStorage) return false;
        if (assignedJob && !observedAssignedJob)
        {
            nextCheckAtMs = 0;
            observedAssignedJob = true;
        }
        else if (!assignedJob)
        {
            observedAssignedJob = false;
        }
        long now = entity.World.ElapsedMilliseconds;
        if (now < nextCheckAtMs || (!assignedJob && IsOnCooldown())) return false;
        if (!assignedJob && nextCheckAtMs == 0)
        {
            // There are several independent cleanup-duty instances per fox.
            // Stagger their first camp scan so a freshly loaded pack does not
            // inspect every cleanup category on one shared server tick.
            nextCheckAtMs = now + 750 + rand.Next(4500);
            return false;
        }
        float scanMultiplier = FeralKinshipCompanionSystem.GetDutyScanIntervalMultiplier(
            entity,
            GatherFinishedProducts);
        nextCheckAtMs = now + (long)Math.Round((5000 + rand.Next(7000)) * scanMultiplier);
        if (DutyCoordinator)
        {
            return system.TryCreateFoxPriorityStoragePlan(entity, out plan) && plan != null;
        }

        return system.TryCreateFoxStoragePlan(
                entity,
                CommandedCourier,
                GatherFinishedProducts,
                RemoveFlowers,
                ShovelSnow,
                FeralKinshipCompanionSystem.IsSnowballCollectionEnabled(entity),
                RouteStorage,
                ShovelCharcoal,
                out plan) && plan != null;
    }

    public override void StartExecute()
    {
        base.StartExecute();
        travelAnimationActive = animMeta != null;
        UpdateTravelAnimation(false);
        done = false;
        stuck = false;
        depositFailed = false;
        depositIncompatible = false;
        suppressImmediateStorageFailure = false;
        failedStorageTargets.Clear();
        FeralKinshipCompanionSystem system =
            entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        foreach (BlockPos failedTarget in FeralKinshipCompanionSystem.GetFoxStorageFailedTargets(entity))
        {
            failedStorageTargets.Add(CopyBlockPos(failedTarget));
        }
        breakingSnow = false;
        breakingCharcoal = false;
        headingToStorage = plan?.AlreadyCarrying == true;
        if (plan?.SnowTarget != null
            && !entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()
                .TryReserveSnowTarget(entity, plan.SnowTarget, plan.ItemTarget))
        {
            stuck = true;
            nextCheckAtMs = entity.World.ElapsedMilliseconds
                + FeralKinshipCompanionSystem.GetDutyFailureRetryMs(entity, 2500);
            return;
        }
        system.EmitCompanionRuntimeDialogue(entity, GetStartDialogueEvent(), CompanionDialoguePriority.Low);
        NavigateToCurrentGoal();
    }

    public override bool CanContinueExecute() => true;

    public override bool ContinueExecute(float dt)
    {
        bool continuing = ContinueJourney(dt);
        UpdateTravelAnimation(continuing && !breakingSnow && !breakingCharcoal && navigation.HasActiveRoute);
        return continuing;
    }

    private void UpdateTravelAnimation(bool moving)
    {
        if (animMeta == null || travelAnimationActive == moving) return;
        if (moving) entity.AnimManager?.StartAnimation(animMeta);
        else entity.AnimManager?.StopAnimation(animMeta.Code);
        travelAnimationActive = moving;
    }

    private bool ContinueJourney(float dt)
    {
        if (plan == null || done || stuck || FeralKinshipCompanionSystem.IsFoxAwayFromWorld(entity)
            || FeralKinshipCompanionSystem.IsFoxIncapacitated(entity)) return false;
        bool commandedJob = FeralKinshipCompanionSystem.HasFoxCommandedStorageJob(entity);
        if (CommandedCourier ? !commandedJob : commandedJob) return false;
        FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (!headingToStorage
            && plan.SnowTarget != null
            && !system.RefreshSnowTargetReservation(entity, plan.SnowTarget, plan.ItemTarget)) return false;
        if (breakingSnow)
        {
            if (plan.SnowTarget == null
                || !FeralKinshipCompanionSystem.IsSnowLayer(entity.World.BlockAccessor.GetBlock(plan.SnowTarget))
                || !FeralKinshipCompanionSystem.IsSnowShovelingEnabled(entity)
                || entity.Pos.SquareDistanceTo(plan.ItemTarget) > 2.0 * 2.0)
            {
                return false;
            }

            entity.Controls.StopAllMovement();
            FaceSnowTarget();
            if (entity.World.ElapsedMilliseconds < snowBreakAtMs) return true;

            breakingSnow = false;
            BlockPos snowTarget = plan.SnowTarget;
            bool collectedSnow = system.TryTakeSnow(
                entity,
                snowTarget,
                FeralKinshipCompanionSystem.IsSnowballCollectionEnabled(entity));
            system.ReleaseSnowTargetReservation(entity, snowTarget, plan.ItemTarget);
            plan.SnowTarget = null;
            if (!collectedSnow)
            {
                done = true;
                return false;
            }

            headingToStorage = true;
            NavigateToCurrentGoal();
            return true;
        }
        if (breakingCharcoal)
        {
            if (plan.CharcoalTarget == null
                || !FeralKinshipCompanionSystem.IsCharcoalPileBlock(entity.World.BlockAccessor.GetBlock(plan.CharcoalTarget))
                || !FeralKinshipCompanionSystem.IsCharcoalShovelingEnabled(entity)
                || entity.Pos.SquareDistanceTo(plan.ItemTarget) > 2.0 * 2.0)
            {
                return false;
            }

            entity.Controls.StopAllMovement();
            FaceCharcoalTarget();
            if (entity.World.ElapsedMilliseconds < charcoalBreakAtMs) return true;

            breakingCharcoal = false;
            BlockPos charcoalTarget = plan.CharcoalTarget;
            bool collectedCharcoal = system.TryTakeCharcoal(entity, charcoalTarget);
            system.ReleaseCharcoalTarget(entity, charcoalTarget);
            plan.CharcoalTarget = null;
            if (!collectedCharcoal)
            {
                done = true;
                return false;
            }

            headingToStorage = true;
            NavigateToCurrentGoal();
            return true;
        }
        if (!CommandedCourier
            && !RouteStorage
            && ((!IsConfiguredDutyEnabled()
                    && !FeralKinshipCompanionSystem.HasFoxStorageCargo(entity))
                || system.ShouldFoxSeekDenShelter(entity))) return false;

        if (!headingToStorage)
        {
            if (IsLooseDroppedItemPickupLeg())
            {
                EntityItem? item = entity.World.GetEntityById(plan.ItemEntityId) as EntityItem;
                if (item?.Alive != true || item.Itemstack == null || item.Pos.Dimension != entity.Pos.Dimension) return false;
                plan.ItemTarget.Set(item.Pos.X, item.Pos.Y, item.Pos.Z);
                if (plannedTarget.SquareDistanceTo(plan.ItemTarget) > 1.5d * 1.5d)
                {
                    NavigateToCurrentGoal();
                }
            }
        }

        if (navigation.IsAtValidatedDestination)
        {
            OnGoalReached();
            // Reaching a source can synchronously collect the item and start
            // the destination leg. Do not finish the task and cancel that new
            // route merely because the first leg ended on this tick.
            return !done && !stuck && navigation.IsRunning;
        }
        navigation.Tick();
        return navigation.IsRunning;
    }

    public override void FinishExecute(bool cancelled)
    {
        UpdateTravelAnimation(false);
        navigation.Cancel();
        if (!string.IsNullOrWhiteSpace(attackAnimation))
        {
            entity.AnimManager?.StopAnimation(attackAnimation);
            attackAnimation = string.Empty;
        }
        if (plan != null)
        {
            FeralKinshipCompanionSystem system =
                entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
            system.ReleaseDroppedItemReservation(entity, plan.ItemEntityId);
            system.ReleaseNaturalCleanupTarget(entity, plan.NaturalBlockTarget);
            system.ReleaseCharcoalTarget(entity, plan.CharcoalTarget);
            system.ReleaseSnowTargetReservation(entity, plan.SnowTarget, plan.ItemTarget);
        }
        int configuredMinCooldown = MinCooldownMs;
        int configuredMaxCooldown = MaxCooldownMs;
        if (!CommandedCourier && !cancelled && done)
        {
            float cooldownMultiplier = FeralKinshipCompanionSystem.GetDutySuccessCooldownMultiplier(
                entity,
                GatherFinishedProducts);
            MinCooldownMs = Math.Max(0, (int)Math.Round(BaseMinCooldownMs * cooldownMultiplier));
            MaxCooldownMs = Math.Max(MinCooldownMs, (int)Math.Round(BaseMaxCooldownMs * cooldownMultiplier));
        }
        FeralKinshipCompanionSystem dialogueSystem =
            entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (!cancelled && done)
            dialogueSystem.EmitCompanionRuntimeDialogue(entity,
                depositIncompatible ? "work.storage.incompatible"
                    : depositFailed ? "work.storage.full"
                    : dialogueSystem.IsFoxAssignedToWorkCart(entity) ? "work.work_cart.chore_finished" : "work.chore_finished",
                CompanionDialoguePriority.Low);
        else if (stuck)
        {
            Dictionary<string, string> facts = new(StringComparer.OrdinalIgnoreCase)
            {
                ["item"] = GetUnreachableTargetLabel(dialogueSystem)
            };
            dialogueSystem.EmitCompanionRuntimeDialogue(
                entity,
                "work.item.unreachable",
                CompanionDialoguePriority.Low,
                facts);
        }
        base.FinishExecute(cancelled);
        MinCooldownMs = configuredMinCooldown;
        MaxCooldownMs = configuredMaxCooldown;
        plan = null;
    }

    private bool NavigateToCurrentGoal()
    {
        if (plan == null)
        {
            done = true;
            return false;
        }
        Vec3d target = CurrentTarget();
        plannedTarget.Set(target.X, target.Y, target.Z);
        bool rawDroppedItem = IsLooseDroppedItemPickupLeg();
        bool storageApproach = headingToStorage || plan.RouteStorage;
        bool naturalCleanup = !headingToStorage && plan.NaturalBlockTarget != null;
        bool queued = navigation.Start(
            target,
            CurrentMoveSpeed(),
            TargetDistance(),
            OnGoalReached,
            OnStuck,
            2400,
            storageApproach
                ? CompanionNavigationTargetKind.StorageApproach
                : rawDroppedItem ? CompanionNavigationTargetKind.Approach
                : naturalCleanup ? CompanionNavigationTargetKind.NaturalCleanup
                : CompanionNavigationTargetKind.Position,
            storageApproach ? 6 : 5,
            adaptiveSearch: true,
            resumableSearch: true);
        if (!queued && !stuck && !suppressImmediateStorageFailure) OnStuck();
        return queued;
    }

    private bool IsLooseDroppedItemPickupLeg()
    {
        return plan != null
            && !headingToStorage
            && !plan.CollectFromPackCart
            && !plan.RouteStorage
            && plan.ItemEntityId > 0
            && plan.NaturalBlockTarget == null
            && plan.FinishedProductTarget == null
            && plan.FlowerTarget == null
            && plan.SnowTarget == null
            && plan.CharcoalTarget == null;
    }

    private void OnGoalReached()
    {
        if (plan == null)
        {
            done = true;
            return;
        }
        FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (!headingToStorage)
        {
            if (plan.SnowTarget != null)
            {
                breakingSnow = true;
                snowBreakAtMs = entity.World.ElapsedMilliseconds + 650;
                attackAnimation = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, "attack");
                entity.Controls.StopAllMovement();
                FaceSnowTarget();
                if (!string.IsNullOrWhiteSpace(attackAnimation))
                {
                    entity.AnimManager?.StopAnimation("Walk");
                    entity.AnimManager?.StartAnimation(attackAnimation);
                }
                return;
            }

            if (plan.CharcoalTarget != null)
            {
                breakingCharcoal = true;
                charcoalBreakAtMs = entity.World.ElapsedMilliseconds + 650;
                attackAnimation = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, "attack");
                entity.Controls.StopAllMovement();
                FaceCharcoalTarget();
                if (!string.IsNullOrWhiteSpace(attackAnimation))
                {
                    entity.AnimManager?.StopAnimation("Walk");
                    entity.AnimManager?.StartAnimation(attackAnimation);
                }
                return;
            }

            bool collected = plan.RouteStorage
                ? system.TryTakeStorageRoutingItem(
                    entity,
                    plan.SourceStoragePos,
                    plan.SourceSlotIndex,
                    plan.StoragePos)
                : plan.CollectFromPackCart
                ? system.TryTakePackLootAtCart(entity, plan.StoragePos)
                : plan.FinishedProductTarget != null
                    ? system.TryTakeFinishedProduct(entity, plan.FinishedProductTarget)
                : plan.FlowerTarget != null
                    ? system.TryTakeFlower(entity, plan.FlowerTarget)
                    : plan.SnowTarget != null
                        ? system.TryTakeSnow(entity, plan.SnowTarget, plan.CollectSnowballs)
                    : plan.CharcoalTarget != null
                        ? system.TryTakeCharcoal(entity, plan.CharcoalTarget)
                    : plan.NaturalBlockTarget != null
                    ? system.TryTakeNaturalStorageItem(entity, plan.NaturalBlockTarget)
                    : system.TryTakeReservedStorageItem(entity, plan.ItemEntityId, plan.StoragePos);
            if (!collected)
            {
                done = true;
                return;
            }
            headingToStorage = true;
            NavigateToCurrentGoal();
            return;
        }

        BlockPos attemptedStoragePos = CopyBlockPos(plan.StoragePos);
        bool deposited = system.TryDepositFoxStorageCargo(
            entity,
            plan.StoragePos,
            failedStorageTargets,
            out bool allDeposited,
            out bool attemptIncompatible,
            out BlockPos? nextStoragePos);
        failedStorageTargets.Add(attemptedStoragePos);
        if (FeralKinshipCompanionSystem.HasFoxStorageCargo(entity))
        {
            FeralKinshipCompanionSystem.RememberFoxStorageFailedTarget(entity, attemptedStoragePos);
        }
        if (nextStoragePos != null)
        {
            depositIncompatible = false;
            plan.StoragePos = nextStoragePos;
            plan.StorageTarget = system.GetFoxStorageApproachTarget(entity, nextStoragePos);
            NavigateToCurrentGoal();
            return;
        }

        depositIncompatible = attemptIncompatible;
        depositFailed = !deposited || !allDeposited;
        done = true;
    }

    private bool IsConfiguredDutyEnabled()
    {
        if (DutyCoordinator)
        {
            return entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()
                .HasAnyAmbientDutyEnabled(entity);
        }
        if (GatherFinishedProducts) return FeralKinshipCompanionSystem.IsFinishedProductsEnabled(entity);
        if (RemoveFlowers) return FeralKinshipCompanionSystem.IsFlowerRemovalEnabled(entity);
        if (ShovelSnow) return FeralKinshipCompanionSystem.IsSnowShovelingEnabled(entity);
        if (ShovelCharcoal) return FeralKinshipCompanionSystem.IsCharcoalShovelingEnabled(entity);
        return FeralKinshipCompanionSystem.IsGroundCleanupEnabled(entity)
            || entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()
                .IsWorkCartLoggingCleanupEnabled(entity);
    }

    private Vec3d CurrentTarget()
    {
        return headingToStorage
            ? plan!.StorageTarget
            : plan!.ItemTarget;
    }

    private float TargetDistance()
    {
        float configured = headingToStorage
            ? 0.55f
            : plan?.CollectFromPackCart == true ? 0.45f : 0.65f;
        return Math.Max(configured, FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity));
    }

    private float CurrentMoveSpeed()
    {
        float speed = plan?.MoveSpeed ?? 0.022f;
        if (!CommandedCourier)
        {
            speed = FeralKinshipCompanionSystem.GetDutyMovementSpeed(entity, speed);
        }
        if (headingToStorage && FeralKinshipCompanionSystem.HasFoxStorageCargo(entity))
        {
            speed = FeralKinshipCompanionSystem.GetCargoMovementSpeed(entity, speed);
        }
        return speed;
    }

    private void FaceSnowTarget()
    {
        if (plan?.SnowTarget == null) return;
        double dx = plan.SnowTarget.X + 0.5 - entity.Pos.X;
        double dz = plan.SnowTarget.Z + 0.5 - entity.Pos.Z;
        if (dx * dx + dz * dz >= 0.001d)
        {
            entity.Pos.Yaw = (float)Math.Atan2(dx, dz);
        }
    }

    private void FaceCharcoalTarget()
    {
        if (plan?.CharcoalTarget == null) return;
        double dx = plan.CharcoalTarget.X + 0.5 - entity.Pos.X;
        double dz = plan.CharcoalTarget.Z + 0.5 - entity.Pos.Z;
        if (dx * dx + dz * dz >= 0.001d)
        {
            entity.Pos.Yaw = (float)Math.Atan2(dx, dz);
        }
    }

    private void OnStuck()
    {
        if (suppressImmediateStorageFailure) return;

        FeralKinshipCompanionSystem system =
            entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (headingToStorage && plan != null)
        {
            system.LogDutyNavigationFailure(entity, plan, "storage-leg:pathfinding-failed-or-no-progress");
            if (TryRecoverStorageDelivery(system)) return;

            system.ResolveFoxStorageCargoAfterNavigationFailure(entity);
            done = true;
            depositFailed = true;
            stuck = false;
            nextCheckAtMs = entity.World.ElapsedMilliseconds
                + FeralKinshipCompanionSystem.GetDutyFailureRetryMs(entity, 30000L);
            return;
        }

        stuck = true;
        string phase = headingToStorage ? "storage-leg" : "source-leg";
        if (!headingToStorage && plan?.NaturalBlockTarget != null)
        {
            system.MarkNaturalCleanupTargetFailed(
                entity,
                plan.NaturalBlockTarget,
                "pathfinding-failed-or-no-progress");
        }
        system.LogDutyNavigationFailure(entity, plan, $"{phase}:pathfinding-failed-or-no-progress");
        long retryDelay = RouteStorage || plan?.RouteStorage == true ? 30000L : 8000L;
        nextCheckAtMs = entity.World.ElapsedMilliseconds
            + FeralKinshipCompanionSystem.GetDutyFailureRetryMs(entity, retryDelay);
    }

    private bool TryRecoverStorageDelivery(FeralKinshipCompanionSystem system)
    {
        if (plan == null) return false;
        RememberFailedStorageTarget(plan.StoragePos);

        while (system.TryRetargetFoxStorageCargo(entity, plan, failedStorageTargets))
        {
            if (TryStartStorageRecovery()) return true;
            RememberFailedStorageTarget(plan.StoragePos);
        }

        if (system.TryGetFoxStorageReturnSourcePosition(entity, out BlockPos? sourcePos)
            && sourcePos != null
            && !failedStorageTargets.Contains(sourcePos))
        {
            plan.StoragePos = sourcePos;
            if (system.TryGetFoxStorageApproachTarget(entity, sourcePos, out Vec3d? sourceApproach)
                && sourceApproach != null)
            {
                plan.StorageTarget = sourceApproach;
                if (TryStartStorageRecovery()) return true;
            }
            RememberFailedStorageTarget(plan.StoragePos);
        }

        return false;
    }

    private static BlockPos CopyBlockPos(BlockPos pos) => new(pos.X, pos.Y, pos.Z, pos.dimension);

    private void RememberFailedStorageTarget(BlockPos target)
    {
        BlockPos copy = CopyBlockPos(target);
        failedStorageTargets.Add(copy);
        FeralKinshipCompanionSystem.RememberFoxStorageFailedTarget(entity, copy);
    }

    private bool TryStartStorageRecovery()
    {
        stuck = false;
        suppressImmediateStorageFailure = true;
        bool started = NavigateToCurrentGoal();
        suppressImmediateStorageFailure = false;
        return started;
    }

    private string GetUnreachableTargetLabel(FeralKinshipCompanionSystem system)
    {
        if (plan == null) return "that task";
        if (headingToStorage) return "storage";

        if (IsLooseDroppedItemPickupLeg())
        {
            EntityItem? item = entity.World.GetEntityById(plan.ItemEntityId) as EntityItem;
            if (item?.Itemstack != null) return FormatDialogueTarget(item.Itemstack.GetName());
            return "that dropped item";
        }

        if (plan.NaturalBlockTarget != null
            && system.TryGetNaturalCleanupItemPreview(
                plan.NaturalBlockTarget,
                out ItemStack? naturalItem)
            && naturalItem != null)
        {
            return FormatDialogueTarget(naturalItem.GetName());
        }

        BlockPos? blockTarget = plan.FinishedProductTarget
            ?? plan.FlowerTarget
            ?? plan.SnowTarget;
        if (blockTarget != null)
        {
            Block? block = entity.World.BlockAccessor.GetBlock(blockTarget);
            if (block != null && block.Id != 0)
            {
                return FormatDialogueTarget(new ItemStack(block).GetName());
            }
        }

        if (plan.CollectFromPackCart) return "the cart cargo";
        if (plan.RouteStorage) return "the stored item";
        return "that task";
    }

    private static string FormatDialogueTarget(string? name)
    {
        string trimmed = name?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(trimmed)
            ? "that task"
            : trimmed.ToLowerInvariant();
    }

    private string GetStartDialogueEvent()
    {
        if (plan?.RouteStorage == true) return "work.storage.general_to_specific";
        if (plan?.FlowerTarget != null) return "work.flower_removal.begin";
        if (plan?.SnowTarget != null)
            return plan.CollectSnowballs ? "work.snowball_collection.begin" : "work.snow_clearing.begin";
        if (plan?.CharcoalTarget != null) return "work.item.pickup";
        if (plan?.FinishedProductTarget != null)
        {
            Block block = entity.World.BlockAccessor.GetBlock(plan.FinishedProductTarget);
            if (block is BlockCrop) return "work.harvest.crop.begin";
            if (block is BlockMushroom) return "work.harvest.mushroom.begin";
            return "work.harvest.berry.begin";
        }
        if (plan?.CollectFromPackCart == true || CommandedCourier) return "work.work_cart.loaded";
        return "work.item.pickup";
    }
}

internal sealed class FoxStoragePlan
{
    public BlockPos StoragePos = null!;
    public BlockPos SourceStoragePos = null!;
    public int SourceSlotIndex = -1;
    public Vec3d StorageTarget = new();
    public long ItemEntityId;
    public Vec3d ItemTarget = new();
    public BlockPos? NaturalBlockTarget;
    public BlockPos? FinishedProductTarget;
    public BlockPos? FlowerTarget;
    public BlockPos? SnowTarget;
    public BlockPos? CharcoalTarget;
    public bool AlreadyCarrying;
    public bool RouteStorage;
    public bool CollectFromPackCart;
    public bool CollectSnowballs;
    public float MoveSpeed = 0.022f;
}
