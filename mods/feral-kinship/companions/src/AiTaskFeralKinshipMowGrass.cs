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

/// <summary>
/// An optional idle chore that removes only ordinary, naturally-spawned tall
/// grass. The block is broken normally, then one dry-grass item is spawned
/// explicitly because vanilla only grants the cut-grass drop to knife holders.
/// </summary>
public sealed class AiTaskFeralKinshipMowGrass : AiTaskBase
{
    private const int BaseMinCooldownMs = 18000;
    private const int BaseMaxCooldownMs = 36000;
    private static readonly HashSet<string> MowableGrassPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "tallgrass-veryshort-free",
        "tallgrass-short-free",
        "tallgrass-mediumshort-free",
        "tallgrass-medium-free",
        "tallgrass-tall-free",
        "tallgrass-verytall-free"
    };

    private BlockPos? targetPos;
    private Vec3d? target;
    private bool arrived;
    private bool stuck;
    private bool mowed;
    private long mowAtMs;
    private long nextCheckAtMs;
    private string? mowingReservationKey;
    private string attackAnimation = string.Empty;
    private readonly CompanionNavigation navigation;

    public AiTaskFeralKinshipMowGrass(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        navigation = new CompanionNavigation(entity, pathTraverser);
    }

    internal static bool IsMowableGrass(Block? block)
    {
        AssetLocation? code = block?.Code;
        return string.Equals(code?.Domain, "game", StringComparison.OrdinalIgnoreCase)
            && code?.Path != null
            && MowableGrassPaths.Contains(code.Path);
    }

    protected override void SetDefaultValues()
    {
        base.SetDefaultValues();
        // Ground Cleanup duties are deliberately just above this task. Their
        // higher priorities let the AI manager start or pre-empt mowing without
        // making this task repeat every camp-wide duty scan itself.
        Priority = 1.362f;
        priorityForCancel = 1.362f;
        MinCooldownMs = BaseMinCooldownMs;
        MaxCooldownMs = BaseMaxCooldownMs;
    }

    public override bool ShouldExecute()
    {
        FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        long now = entity.World.ElapsedMilliseconds;
        if (now < nextCheckAtMs || IsOnCooldown()) return false;

        if (!FeralKinshipCompanionSystem.IsMowLawnEnabled(entity)
            || FeralKinshipCompanionSystem.GetCompanionActivityMode(entity) != CompanionActivityMode.AtEase
            || FeralKinshipCompanionSystem.IsCompanionJuvenile(entity)
            || FeralKinshipCompanionSystem.HasFoxStorageJob(entity)) return false;

        // Keep the expensive higher-priority duty arbitration behind the
        // mower's own scan throttle. Otherwise every mower task instance
        // rescans the entire camp on every server tick while it is waiting
        // for its next normal check.
        if (system.ShouldDeferMowingForHigherPriorityDuty(entity))
        {
            nextCheckAtMs = now
                + FeralKinshipCompanionSystem.GetDutyFailureRetryMs(entity, 2500);
            return false;
        }

        // Dephase newly loaded foxes before their first local grass search.
        // Afterwards this task's normal random interval keeps the pack from
        // doing its discovery work on the same server tick.
        if (nextCheckAtMs == 0)
        {
            nextCheckAtMs = now + 750 + rand.Next(3500);
            return false;
        }

        float scanMultiplier = FeralKinshipCompanionSystem.GetDutyScanIntervalMultiplier(
            entity,
            finishedProducts: false);
        nextCheckAtMs = now + (long)Math.Round((5000 + rand.Next(7000)) * scanMultiplier);
        return TryFindGrass(out targetPos, out target);
    }

    public override void StartExecute()
    {
        arrived = target != null && CompanionNavigation.IsClearArrival(
            entity, target, TargetDistance(), allowTargetBlock: true);
        stuck = false;
        mowed = false;
        mowAtMs = entity.World.ElapsedMilliseconds + 650;
        attackAnimation = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, "attack");
        animMeta = ResolveAnimation(arrived ? attackAnimation : "walk");
        base.StartExecute();
        entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()
            .EmitCompanionRuntimeDialogue(entity, "work.mowing.begin", CompanionDialoguePriority.Low);

        if (arrived)
        {
            BeginMowing();
            return;
        }

        if (target == null || !navigation.Start(
                target,
                FeralKinshipCompanionSystem.GetDutyMovementSpeed(entity, 0.020f),
                TargetDistance(),
                OnGoalReached,
                OnStuck,
                2200,
                CompanionNavigationTargetKind.Position,
                4))
        {
            if (!stuck) OnStuck();
        }
    }

    public override bool CanContinueExecute() => true;

    public override bool ContinueExecute(float dt)
    {
        FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (targetPos == null || target == null || stuck || mowed
            || !FeralKinshipCompanionSystem.IsMowLawnEnabled(entity)
            || FeralKinshipCompanionSystem.GetCompanionActivityMode(entity) != CompanionActivityMode.AtEase
            || FeralKinshipCompanionSystem.IsCompanionJuvenile(entity)
            || FeralKinshipCompanionSystem.HasFoxStorageJob(entity)
            || FeralKinshipCompanionSystem.IsFoxAwayFromWorld(entity)
            || FeralKinshipCompanionSystem.IsFoxIncapacitated(entity)
            || !IsMowableGrass(entity.World.BlockAccessor.GetBlock(targetPos))) return false;

        if (!arrived)
        {
            if (CompanionNavigation.IsClearArrival(entity, target, TargetDistance(), allowTargetBlock: true))
            {
                OnGoalReached();
                return true;
            }
            navigation.Tick();
            return navigation.IsRunning;
        }

        entity.Controls.StopAllMovement();
        FacePosition(target);
        if (entity.World.ElapsedMilliseconds < mowAtMs) return true;

        // A cart scan or another fox may have exposed higher-priority cleanup
        // while this fox was walking to the grass. Recheck at the final action
        // boundary so mowing cannot win that race.
        if (system.ShouldDeferMowingForHigherPriorityDuty(entity)) return false;

        entity.World.BlockAccessor.BreakBlock(targetPos, null, 1f);
        Item? dryGrass = entity.World.GetItem(new AssetLocation("game:drygrass"));
        if (dryGrass != null)
        {
            entity.World.SpawnItemEntity(
                new ItemStack(dryGrass),
                target.AddCopy(0, 0.15, 0)
            );
        }
        mowed = true;
        system.RecordCleanupDutyUnit(entity);
        return false;
    }

    public override void FinishExecute(bool cancelled)
    {
        navigation.Cancel();
        entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()
            .ReleaseMowingSlot(entity, mowingReservationKey);
        mowingReservationKey = null;
        if (!string.IsNullOrWhiteSpace(attackAnimation))
        {
            entity.AnimManager?.StopAnimation(attackAnimation);
        }
        int configuredMinCooldown = MinCooldownMs;
        int configuredMaxCooldown = MaxCooldownMs;
        if (!cancelled && mowed)
        {
            float cooldownMultiplier = FeralKinshipCompanionSystem.GetDutySuccessCooldownMultiplier(
                entity,
                finishedProducts: false);
            MinCooldownMs = Math.Max(0, (int)Math.Round(BaseMinCooldownMs * cooldownMultiplier));
            MaxCooldownMs = Math.Max(MinCooldownMs, (int)Math.Round(BaseMaxCooldownMs * cooldownMultiplier));
        }
        if (!cancelled && mowed)
        {
            FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
            system.EmitCompanionRuntimeDialogue(entity,
                system.IsFoxAssignedToWorkCart(entity) ? "work.work_cart.chore_finished" : "work.chore_finished",
                CompanionDialoguePriority.Low);
        }
        else if (stuck)
            entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()
                .EmitCompanionRuntimeDialogue(entity, "work.no_valid_work", CompanionDialoguePriority.Low);
        base.FinishExecute(cancelled);
        MinCooldownMs = configuredMinCooldown;
        MaxCooldownMs = configuredMaxCooldown;
        targetPos = null;
        target = null;
    }

    private bool TryFindGrass(out BlockPos? foundPos, out Vec3d? foundTarget)
    {
        FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (!system.TryGetCompanionDutyCenter(entity, out BlockPos? dutyCenter, out float radius)
            || dutyCenter == null
            || system.ShouldDeferMowingForHigherPriorityDuty(entity))
        {
            foundPos = null;
            foundTarget = null;
            return false;
        }

        if (!system.TryReserveMowingSlot(entity, dutyCenter, out string reservationKey))
        {
            foundPos = null;
            foundTarget = null;
            return false;
        }

        bool found = system.IsFoxAssignedToWorkCart(entity)
            ? system.TryClaimQueuedWorkCartMowingTarget(entity, out foundPos, out foundTarget)
            : TryFindLocalGrass(system, dutyCenter, radius, out foundPos, out foundTarget);
        if (!found || foundPos == null || foundTarget == null)
        {
            system.ReleaseMowingSlot(entity, reservationKey);
            foundPos = null;
            foundTarget = null;
            return false;
        }

        mowingReservationKey = reservationKey;
        return true;
    }

    private bool TryFindLocalGrass(
        FeralKinshipCompanionSystem system,
        BlockPos dutyCenter,
        float radius,
        out BlockPos? foundPos,
        out Vec3d? foundTarget)
    {
        foundPos = null;
        foundTarget = null;
        List<(BlockPos Position, double Distance)> candidates = new();
        int horizontalRadius = (int)Math.Ceiling(radius);

        for (int dx = -horizontalRadius; dx <= horizontalRadius; dx++)
        {
            for (int dz = -horizontalRadius; dz <= horizontalRadius; dz++)
            {
                if (dx * dx + dz * dz > radius * radius) continue;

                int terrainHeight = entity.World.BlockAccessor.GetTerrainMapheightAt(
                    new BlockPos(dutyCenter.X + dx, dutyCenter.Y, dutyCenter.Z + dz, entity.Pos.Dimension));
                int rainHeight = entity.World.BlockAccessor.GetRainMapHeightAt(
                    new BlockPos(dutyCenter.X + dx, dutyCenter.Y, dutyCenter.Z + dz, entity.Pos.Dimension));
                int minSurfaceHeight = Math.Min(terrainHeight, rainHeight) - 2;
                int maxSurfaceHeight = Math.Max(terrainHeight, rainHeight) + 2;
                for (int y = minSurfaceHeight; y <= maxSurfaceHeight; y++)
                {
                    BlockPos candidate = new(dutyCenter.X + dx, y, dutyCenter.Z + dz, entity.Pos.Dimension);
                    if (!IsMowableGrass(entity.World.BlockAccessor.GetBlock(candidate))) continue;

                    Vec3d candidateTarget = new(candidate.X + 0.5, candidate.Y, candidate.Z + 0.5);
                    candidates.Add((candidate, entity.Pos.SquareDistanceTo(candidateTarget)));
                }
            }
        }

        // Only test the nearest few candidates. A worksite with a large amount
        // of grass should not turn a failed approach into another full scan.
        foreach ((BlockPos position, double _) in candidates.OrderBy(candidate => candidate.Distance).Take(24))
        {
            if (!system.TryGetMowingApproachTarget(entity, position, out Vec3d? approachTarget)
                || approachTarget == null) continue;
            foundPos = position;
            foundTarget = approachTarget;
            return true;
        }

        return false;
    }

    private float TargetDistance()
    {
        return Math.Max(0.75f, FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity));
    }

    private void BeginMowing()
    {
        navigation.Cancel();
        entity.Controls.StopAllMovement();
        if (!string.IsNullOrWhiteSpace(attackAnimation))
        {
            entity.AnimManager?.StopAnimation("Walk");
            entity.AnimManager?.StartAnimation(attackAnimation);
        }
    }

    private void OnGoalReached()
    {
        arrived = true;
        mowAtMs = entity.World.ElapsedMilliseconds + 650;
        BeginMowing();
    }

    private void FacePosition(Vec3d position)
    {
        double dx = position.X - entity.Pos.X;
        double dz = position.Z - entity.Pos.Z;
        if (dx * dx + dz * dz >= 0.001d)
        {
            entity.Pos.Yaw = (float)Math.Atan2(dx, dz);
        }
    }

    private void OnStuck()
    {
        stuck = true;
        nextCheckAtMs = entity.World.ElapsedMilliseconds
            + FeralKinshipCompanionSystem.GetDutyFailureRetryMs(entity, 8000);
    }

    private AnimationMetaData ResolveAnimation(string code)
    {
        AnimationMetaData? configured = entity.Properties.Client.Animations?
            .FirstOrDefault(animation => string.Equals(animation.Code, code, StringComparison.OrdinalIgnoreCase));
        return configured?.Clone() ?? new AnimationMetaData
        {
            Code = code, Animation = code, AnimationSpeed = 1f, EaseInSpeed = 2f, EaseOutSpeed = 2f
        }.Init();
    }
}
