#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// Walks a selected fox to the active pack marker before the expedition hides
/// it from the world. The persistent expedition deadline remains the fallback
/// when no usable route can be completed.
/// </summary>
public sealed class AiTaskFeralKinshipExpeditionDepart : AiTaskBase
{
    private const int PathSearchDepth = 3000;
    private const int SafetyAbortMs = 18000;
    private const int PauseBeforeLookMs = 650;
    private const int LookAtOwnerMs = 900;
    private const int PauseBeforeTurnMs = 400;
    private const int TurnBackMs = 500;
    private readonly float moveSpeed;
    private Vec3d? target;
    private bool noPath;
    private bool departed;
    private long nextCheckAtMs;
    private long startedAtMs;
    private long departureMomentEndsAtMs;
    private DepartureMomentStage departureMomentStage;
    private Entity? departureOwner;
    private readonly CompanionNavigation navigation;

    private enum DepartureMomentStage
    {
        None,
        PauseBeforeLook,
        LookAtOwner,
        PauseBeforeTurn,
        TurnBack
    }

    public AiTaskFeralKinshipExpeditionDepart(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        moveSpeed = taskConfig["movespeed"].AsFloat(0.045f);
        navigation = new CompanionNavigation(entity, pathTraverser);
    }

    public override bool ShouldExecute()
    {
        if (entity.World.ElapsedMilliseconds < nextCheckAtMs
            || !entity.Alive
            || FeralKinshipCompanionSystem.IsFoxAwayFromWorld(entity)
            || FeralKinshipCompanionSystem.IsFoxIncapacitated(entity))
        {
            return false;
        }

        nextCheckAtMs = entity.World.ElapsedMilliseconds + 250;
        FeralKinshipCompanionSystem? system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        return system?.TryGetPendingExpeditionDepartureTarget(entity, out target) == true
            && target != null;
    }

    public override void StartExecute()
    {
        base.StartExecute();
        noPath = false;
        departed = false;
        startedAtMs = entity.World.ElapsedMilliseconds;
        departureMomentEndsAtMs = 0;
        departureMomentStage = DepartureMomentStage.None;
        departureOwner = null;

        entity.Controls.StopAllMovement();
        entity.Pos.Motion.Set(0, 0, 0);

        if (target == null || !IsFinite(target) || !IsFinite(entity.Pos))
        {
            FailSafeDeparture();
            return;
        }

        float targetDistance = FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity);
        if (CompanionNavigation.IsClearArrival(
                entity, target, targetDistance, allowTargetBlock: true))
        {
            OnCartReached();
            return;
        }

        navigation.Start(
            target,
            moveSpeed,
            targetDistance,
            OnCartReached,
            FailSafeDeparture,
            PathSearchDepth,
            CompanionNavigationTargetKind.Position,
            5
        );
    }

    public override bool CanContinueExecute() => true;

    public override bool ContinueExecute(float dt)
    {
        if (noPath || departed || target == null)
        {
            return false;
        }

        // Hunger can cross the starvation threshold after departure has
        // already begun. Re-check before every departure stage so this task
        // yields immediately to the emergency feeding/rest tasks instead of
        // completing the hide-from-world transition.
        FeralKinshipCompanionSystem? system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (system?.IsCompanionFoodRestricted(entity) == true)
        {
            return false;
        }

        long now = entity.World.ElapsedMilliseconds;
        if (now - startedAtMs >= SafetyAbortMs
            || !IsFinite(target)
            || !IsFinite(entity.Pos))
        {
            FailSafeDeparture();
            return false;
        }

        if (departureMomentStage != DepartureMomentStage.None)
        {
            return ContinueDepartureMoment(now, dt);
        }

        navigation.Tick();
        return system?.TryGetPendingExpeditionDepartureTarget(entity, out _) == true
            && navigation.IsRunning;
    }

    public override void FinishExecute(bool cancelled)
    {
        navigation.Cancel();
        if (cancelled && !departed)
        {
            entity.Controls.StopAllMovement();
            entity.Pos.Motion.Set(0, 0, 0);
        }
        base.FinishExecute(cancelled);
    }

    private void CompleteDeparture()
    {
        FeralKinshipCompanionSystem? system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        departed = system?.CompletePendingExpeditionDeparture(entity) == true;
    }

    private void OnCartReached()
    {
        navigation.Cancel();
        entity.Controls.StopAllMovement();
        entity.Pos.Motion.Set(0, 0, 0);
        FeralKinshipCompanionSystem? system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (system?.ShouldPlayExpeditionDepartureMoment(entity) != true)
        {
            CompleteDeparture();
            departureMomentStage = DepartureMomentStage.None;
            return;
        }
        departureOwner = ResolveOwner();
        departureMomentStage = DepartureMomentStage.PauseBeforeLook;
        departureMomentEndsAtMs = entity.World.ElapsedMilliseconds + PauseBeforeLookMs;
    }

    private bool ContinueDepartureMoment(long now, float dt)
    {
        if (target == null)
        {
            FailSafeDeparture();
            return false;
        }

        entity.Controls.StopAllMovement();
        entity.Pos.Motion.Set(0, 0, 0);
        switch (departureMomentStage)
        {
            case DepartureMomentStage.PauseBeforeLook:
                if (now < departureMomentEndsAtMs) return true;
                departureMomentStage = DepartureMomentStage.LookAtOwner;
                departureMomentEndsAtMs = now + LookAtOwnerMs;
                FaceEntity(departureOwner);
                return true;

            case DepartureMomentStage.LookAtOwner:
                FaceEntity(departureOwner);
                if (now < departureMomentEndsAtMs) return true;
                departureMomentStage = DepartureMomentStage.PauseBeforeTurn;
                departureMomentEndsAtMs = now + PauseBeforeTurnMs;
                return true;

            case DepartureMomentStage.PauseBeforeTurn:
                if (now < departureMomentEndsAtMs) return true;
                departureMomentStage = DepartureMomentStage.TurnBack;
                departureMomentEndsAtMs = now + TurnBackMs;
                FacePoint(target);
                return true;

            case DepartureMomentStage.TurnBack:
                FacePoint(target);
                if (now < departureMomentEndsAtMs) return true;
                CompleteDeparture();
                departureMomentStage = DepartureMomentStage.None;
                return !departed;

            default:
                return false;
        }
    }

    private Entity? ResolveOwner()
    {
        string ownerUid = FeralKinshipCompanionSystem.GetCompanionOwnerUid(entity);
        return string.IsNullOrWhiteSpace(ownerUid)
            ? null
            : entity.World.PlayerByUid(ownerUid)?.Entity;
    }

    private void FaceEntity(Entity? targetEntity)
    {
        if (targetEntity == null || targetEntity.Pos.Dimension != entity.Pos.Dimension) return;
        FacePoint(targetEntity.Pos.XYZ);
    }

    private void FacePoint(Vec3d point)
    {
        double dx = point.X - entity.Pos.X;
        double dz = point.Z - entity.Pos.Z;
        if (dx * dx + dz * dz < 0.001d) return;
        entity.Pos.Yaw = (float)Math.Atan2(dx, dz);
    }

    private void FailSafeDeparture()
    {
        navigation.Cancel();
        entity.Controls.StopAllMovement();
        entity.Pos.Motion.Set(0, 0, 0);
        FeralKinshipCompanionSystem? system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        departed = system?.CompletePendingExpeditionDeparture(entity) == true;
        noPath = true;
    }

    private static bool IsFinite(Vec3d value)
    {
        return IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);
    }

    private static bool IsFinite(EntityPos value)
    {
        return IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z)
            && IsFinite(value.Motion);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
