#nullable enable

using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

/// <summary>
/// Hidden Blueberry Mode behavior: adult tamed wolves seek any vanilla
/// blueberry bush state or cutting, eat it, and leave one cutting behind.
/// </summary>
public sealed class AiTaskFeralKinshipBlueberryMode : AiTaskBase
{
    private const int BaseMinCooldownMs = 12000;
    private const int BaseMaxCooldownMs = 28000;
    private BlockPos? targetPos;
    private Vec3d? target;
    private bool arrived;
    private bool stuck;
    private bool eaten;
    private long eatAtMs;
    private long nextCheckAtMs;
    private string attackAnimation = string.Empty;
    private readonly CompanionNavigation navigation;

    public AiTaskFeralKinshipBlueberryMode(
        EntityAgent entity,
        JsonObject taskConfig,
        JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        navigation = new CompanionNavigation(entity, pathTraverser);
    }

    protected override void SetDefaultValues()
    {
        base.SetDefaultValues();
        Priority = 1.80f;
        priorityForCancel = 1.80f;
        MinCooldownMs = BaseMinCooldownMs;
        MaxCooldownMs = BaseMaxCooldownMs;
    }

    public override bool ShouldExecute()
    {
        FeralKinshipCompanionSystem system = entity.Api.ModLoader
            .GetModSystem<FeralKinshipCompanionSystem>();
        long now = entity.World.ElapsedMilliseconds;
        if (now < nextCheckAtMs || IsOnCooldown()
            || !system.BlueberryModeEnabled
            || !FeralKinshipCompanionSystem.IsTamedFox(entity)
            || !FeralKinshipCompanionSystem.TryGetCompanionSpecies(
                entity,
                out CompanionSpeciesProfile species)
            || !string.Equals(species.Id, "wolf", StringComparison.OrdinalIgnoreCase)
            || FeralKinshipCompanionSystem.IsCompanionJuvenile(entity)
            || FeralKinshipCompanionSystem.GetCompanionActivityMode(entity)
                != CompanionActivityMode.AtEase
            || FeralKinshipCompanionSystem.HasFoxStorageJob(entity)
            || FeralKinshipCompanionSystem.IsFoxAwayFromWorld(entity)
            || FeralKinshipCompanionSystem.IsFoxIncapacitated(entity)
            || system.ShouldFoxSeekDenShelter(entity)
            || system.IsCompanionFoodRestricted(entity)) return false;

        if (nextCheckAtMs == 0)
        {
            nextCheckAtMs = now + 750 + rand.Next(3500);
            return false;
        }

        nextCheckAtMs = now + 7000 + rand.Next(9000);
        return system.TryFindBlueberryModeTarget(entity, out targetPos, out target)
            && targetPos != null
            && target != null;
    }

    public override void StartExecute()
    {
        arrived = target != null && CompanionNavigation.IsClearArrival(
            entity,
            target,
            TargetDistance(),
            allowTargetBlock: true);
        stuck = false;
        eaten = false;
        eatAtMs = entity.World.ElapsedMilliseconds + 650;
        attackAnimation = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, "attack");
        animMeta = ResolveAnimation(arrived ? attackAnimation : "walk");
        base.StartExecute();

        if (arrived)
        {
            BeginEating();
            return;
        }

        if (target == null || !navigation.Start(
            target,
            FeralKinshipCompanionSystem.GetDutyMovementSpeed(entity, 0.020f),
            TargetDistance(),
            OnGoalReached,
            OnStuck,
            2200,
            CompanionNavigationTargetKind.NaturalCleanup,
            5))
        {
            if (!stuck) OnStuck();
        }
    }

    public override bool CanContinueExecute() => true;

    public override bool ContinueExecute(float dt)
    {
        FeralKinshipCompanionSystem system = entity.Api.ModLoader
            .GetModSystem<FeralKinshipCompanionSystem>();
        if (targetPos == null || target == null || stuck
            || FeralKinshipCompanionSystem.IsFoxAwayFromWorld(entity)
            || FeralKinshipCompanionSystem.IsFoxIncapacitated(entity)
            || !system.BlueberryModeEnabled
            || FeralKinshipCompanionSystem.GetCompanionActivityMode(entity)
                != CompanionActivityMode.AtEase)
        {
            return false;
        }

        if (!arrived)
        {
            if (navigation.IsAtValidatedDestination)
            {
                OnGoalReached();
                return true;
            }

            navigation.Tick();
            if (navigation.IsAtValidatedDestination) OnGoalReached();
            return navigation.IsRunning || arrived;
        }

        entity.Controls.StopAllMovement();
        FacePosition(target);
        if (entity.World.ElapsedMilliseconds < eatAtMs) return true;

        eaten = system.TryEatBlueberryBush(entity, targetPos);
        return false;
    }

    public override void FinishExecute(bool cancelled)
    {
        navigation.Cancel();
        if (!string.IsNullOrWhiteSpace(attackAnimation))
        {
            entity.AnimManager?.StopAnimation(attackAnimation);
        }

        if (targetPos != null)
        {
            entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()
                .ReleaseBlueberryModeTarget(entity, targetPos);
        }

        int configuredMinCooldown = MinCooldownMs;
        int configuredMaxCooldown = MaxCooldownMs;
        if (!cancelled && eaten)
        {
            MinCooldownMs = BaseMinCooldownMs;
            MaxCooldownMs = BaseMaxCooldownMs;
        }

        base.FinishExecute(cancelled);
        MinCooldownMs = configuredMinCooldown;
        MaxCooldownMs = configuredMaxCooldown;
        targetPos = null;
        target = null;
    }

    private float TargetDistance()
    {
        return Math.Max(
            0.75f,
            FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity));
    }

    private void BeginEating()
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
        eatAtMs = entity.World.ElapsedMilliseconds + 650;
        BeginEating();
    }

    private void OnStuck()
    {
        stuck = true;
        nextCheckAtMs = entity.World.ElapsedMilliseconds
            + FeralKinshipCompanionSystem.GetDutyFailureRetryMs(entity, 5000);
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

    private AnimationMetaData ResolveAnimation(string code)
    {
        AnimationMetaData? configured = entity.Properties.Client.Animations?
            .FirstOrDefault(animation => string.Equals(
                animation.Code,
                code,
                StringComparison.OrdinalIgnoreCase));
        return configured?.Clone() ?? new AnimationMetaData
        {
            Code = code,
            Animation = code,
            AnimationSpeed = 1f,
            EaseInSpeed = 2f,
            EaseOutSpeed = 2f
        }.Init();
    }
}
