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

/// <summary>Low-priority, interruptible pack life using existing fox animations.</summary>
public sealed class AiTaskFeralKinshipPackIdle : AiTaskBase
{
    private FoxIdlePlan? plan;
    private bool arrived;
    private bool stuck;
    private long endsAtMs;
    private long nextCheckAtMs;
    private string restAnimation = string.Empty;
    private int playRunLegs;
    private long nextShadowRefreshAtMs;
    private long nextPlayRefreshAtMs;
    private long nextPackSpaceMoveAtMs;
    private long nextLookAroundAtMs;
    private int lookAroundTurns;
    private readonly CompanionNavigation navigation;

    public AiTaskFeralKinshipPackIdle(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        navigation = new CompanionNavigation(entity, pathTraverser);
    }

    protected override void SetDefaultValues()
    {
        base.SetDefaultValues();
        Priority = 1.361f;
        priorityForCancel = 1.361f;
        MinCooldownMs = 10000;
        MaxCooldownMs = 26000;
    }

    public override bool ShouldExecute()
    {
        FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        bool hasInvitation = system.HasPendingFoxIdleInvitation(entity);
        bool hasForcedPlan = system.HasForcedFoxIdlePlan(entity);
        if (!hasInvitation && !hasForcedPlan
            && (entity.World.ElapsedMilliseconds < nextCheckAtMs || IsOnCooldown())) return false;
        nextCheckAtMs = entity.World.ElapsedMilliseconds + 4000 + rand.Next(7000);
        return system.TryCreateFoxIdlePlan(entity, out plan) && plan != null;
    }

    public override void StartExecute()
    {
        arrived = plan?.Immediate == true;
        stuck = false;
        long now = entity.World.ElapsedMilliseconds;
        endsAtMs = plan?.AbsoluteEndAtMs > now
            ? plan.AbsoluteEndAtMs
            : now + (plan?.DurationMs ?? 8000);
        restAnimation = plan?.Animation ?? string.Empty;
        playRunLegs = plan?.Kind == "play-runner" ? 1 : 0;
        nextShadowRefreshAtMs = entity.World.ElapsedMilliseconds;
        nextPlayRefreshAtMs = entity.World.ElapsedMilliseconds;
        nextPackSpaceMoveAtMs = 0;
        nextLookAroundAtMs = entity.World.ElapsedMilliseconds + 500;
        lookAroundTurns = 0;
        string movingAnimation = plan?.Kind is "chase" or "play-runner" ? "Run" : "Walk";
        animMeta = ResolveAnimation(arrived ? restAnimation : movingAnimation);
        base.StartExecute();

        if (arrived)
        {
            entity.Controls.StopAllMovement();
            return;
        }

        if (plan != null)
        {
            NavigatePlanTarget(1800);
        }
    }

    public override bool CanContinueExecute() => true;

    public override bool ContinueExecute(float dt)
    {
        if (plan == null || stuck
            || FeralKinshipCompanionSystem.IsFoxAwayFromWorld(entity)
            || FeralKinshipCompanionSystem.IsFoxIncapacitated(entity)) return false;

        FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        navigation.Tick();
        if (plan.Kind == "menu-attention")
        {
            if (!system.IsMenuAttentionActive(entity, out Entity? owner) || owner == null
                || FeralKinshipCompanionSystem.ShouldPauseAmbientCompanionAi(entity, this)) return false;
            entity.Controls.StopAllMovement();
            FaceEntity(owner);
            return true;
        }

        if (plan.Kind == "interest-watch")
        {
            if (!system.IsAmbientInterestStillValid(entity, plan.ReservationKey)) return false;
            if (system.IsAmbientInterestPersistent(plan.ReservationKey))
            {
                // Smithing and hot food are live sources. Their initial plan
                // duration is only a recruitment stagger; remain interested
                // until the source itself disappears or higher-priority AI wins.
                endsAtMs = Math.Max(endsAtMs, entity.World.ElapsedMilliseconds + 2000);
            }
            else if (entity.World.ElapsedMilliseconds >= endsAtMs)
            {
                return false;
            }

            if (!plan.IgnoreShelter && system.ShouldFoxSeekDenShelter(entity)) return false;
            if (arrived)
            {
                entity.Controls.StopAllMovement();
                if (plan.LookAtTarget != null) FacePosition(plan.LookAtTarget);
                return true;
            }

            return navigation.IsRunning;
        }

        if (entity.World.ElapsedMilliseconds >= endsAtMs) return false;
        if (!plan.IgnoreShelter && system.ShouldFoxSeekDenShelter(entity)) return false;

        if (plan.Kind == "perimeter-walk")
        {
            return !arrived && navigation.IsRunning;
        }

        if (plan.Kind == "shadow-follow")
        {
            Entity? owner = entity.World.GetEntityById(plan.OwnerEntityId);
            if (owner is not EntityPlayer || !owner.Alive
                || !system.IsCompanionOwnerInsidePackCamp(entity, owner)) return false;

            double distanceSquared = entity.Pos.SquareDistanceTo(owner.Pos);
            if (CompanionNavigation.IsClearArrival(
                    entity, owner.Pos.XYZ, plan.TargetDistance, allowTargetBlock: false))
            {
                arrived = true;
                navigation.Cancel();
                entity.Controls.StopAllMovement();
                FaceEntity(owner);
                return true;
            }

            long now = entity.World.ElapsedMilliseconds;
            bool ownerMovedBeyondRoute = plan.Target.SquareDistanceTo(owner.Pos.XYZ) > 2.25d * 2.25d;
            if (arrived || !navigation.IsRunning || (now >= nextShadowRefreshAtMs && ownerMovedBeyondRoute))
            {
                nextShadowRefreshAtMs = now + 750;
                arrived = false;
                entity.AnimManager?.StopAnimation(restAnimation);
                plan.Target.Set(owner.Pos.X, owner.Pos.Y, owner.Pos.Z);
                NavigatePlanTarget(2400, Math.Max(0.65f, plan.TargetDistance));
            }
            return true;
        }

        if (plan.Kind == "pack-cart-sit")
        {
            Entity? owner = plan.OwnerEntityId > 0
                ? entity.World.GetEntityById(plan.OwnerEntityId)
                : null;
            if (owner is not EntityPlayer || !owner.Alive
                || !system.IsCompanionOwnerInsidePackCamp(entity, owner)) return false;

            if (arrived)
            {
                entity.Controls.StopAllMovement();
                FaceEntity(owner);
                return true;
            }

            return navigation.IsRunning;
        }

        if (plan.Kind == "work-cart-sit")
        {
            if (!system.TryGetWorkCartIdleTarget(entity, out Vec3d? currentTarget)
                || currentTarget == null)
            {
                return false;
            }

            if (arrived)
            {
                entity.Controls.StopAllMovement();
                return true;
            }

            return navigation.IsRunning;
        }

        if (plan.Kind == "see-off")
        {
            Entity? owner = plan.OwnerEntityId > 0
                ? entity.World.GetEntityById(plan.OwnerEntityId)
                : null;
            if (owner is not EntityPlayer || !owner.Alive
                || owner.Pos.Dimension != entity.Pos.Dimension
                || FeralKinshipCompanionSystem.IsFoxAwayFromWorld(owner)
                || system.IsCompanionOwnerInsidePackCamp(entity, owner)) return false;

            if (arrived)
            {
                entity.Controls.StopAllMovement();
                FaceEntity(owner);
                return true;
            }

            return navigation.IsRunning;
        }

        if (plan.Kind == "look-around")
        {
            long now = entity.World.ElapsedMilliseconds;
            if (now >= nextLookAroundAtMs)
            {
                entity.Pos.Yaw += lookAroundTurns % 2 == 0 ? 0.85f : -1.7f;
                lookAroundTurns++;
                nextLookAroundAtMs = now + 900;
            }
            return true;
        }

        if (plan.Kind == "startle-response")
        {
            if (plan.LookAtTarget != null)
            {
                FacePosition(plan.LookAtTarget);
            }

            long now = entity.World.ElapsedMilliseconds;
            if (now >= nextLookAroundAtMs)
            {
                entity.Pos.Yaw += lookAroundTurns % 2 == 0 ? 0.95f : -1.9f;
                lookAroundTurns++;
                nextLookAroundAtMs = now + 650;
            }
            return true;
        }

        if (plan.Kind == "camp-lookout")
        {
            if (!arrived) return navigation.IsRunning;

            long now = entity.World.ElapsedMilliseconds;
            entity.Controls.StopAllMovement();
            if (now >= nextLookAroundAtMs)
            {
                entity.Pos.Yaw += lookAroundTurns % 2 == 0 ? 0.85f : -1.7f;
                lookAroundTurns++;
                nextLookAroundAtMs = now + 1100;
            }
            return true;
        }

        if (plan.Kind is "pack-space-visit" or "scent-investigation")
        {
            if (!arrived) return navigation.IsRunning;

            long now = entity.World.ElapsedMilliseconds;
            if (plan.LookAtTarget != null) FacePosition(plan.LookAtTarget);
            if (now < nextPackSpaceMoveAtMs) return true;

            if (plan.WaypointIndex + 1 < plan.Waypoints.Count)
            {
                plan.WaypointIndex++;
                plan.Target = plan.Waypoints[plan.WaypointIndex].Clone();
                arrived = false;
                entity.AnimManager?.StopAnimation(restAnimation);
                NavigatePlanTarget(1800);
                return true;
            }

            return false;
        }

        if (plan.PartnerEntityId > 0)
        {
            Entity? partner = entity.World.GetEntityById(plan.PartnerEntityId);
            if (partner == null || !partner.Alive || partner.Pos.Dimension != entity.Pos.Dimension
                || partner.Pos.SquareDistanceTo(entity.Pos) > 30 * 30) return false;
            if (plan.Kind == "chase")
            {
                long now = entity.World.ElapsedMilliseconds;
                bool partnerMovedBeyondRoute = plan.Target.SquareDistanceTo(partner.Pos.XYZ) > 1.5d * 1.5d;
                if (!navigation.IsRunning || (now >= nextPlayRefreshAtMs && partnerMovedBeyondRoute))
                {
                    nextPlayRefreshAtMs = now + 650;
                    arrived = false;
                    navigation.Cancel();
                    plan.Target.Set(partner.Pos.X, partner.Pos.Y, partner.Pos.Z);
                    NavigatePlanTarget(1800);
                }
                return true;
            }
            if (plan.Kind == "play-runner")
            {
                long now = entity.World.ElapsedMilliseconds;
                if (!navigation.IsRunning && now >= nextPlayRefreshAtMs)
                {
                    if (!system.TryGetNextPlayEscapeTarget(entity, plan.PartnerEntityId, out Vec3d? nextTarget)
                        || nextTarget == null) return false;
                    arrived = false;
                    nextPlayRefreshAtMs = now + 650;
                    plan.Target = nextTarget;
                    NavigatePlanTarget(1800, failAsArrival: true);
                }
                return true;
            }
            if (plan.Kind is "companion-rest" or "packmate-greeting")
            {
                if (arrived)
                {
                    FaceEntity(partner);
                    return true;
                }
                return navigation.IsRunning;
            }
            return CompanionNavigation.IsClearArrival(entity, partner.Pos.XYZ, 4.5, allowTargetBlock: false);
        }

        return arrived || navigation.IsRunning;
    }

    public override void FinishExecute(bool cancelled)
    {
        navigation.Cancel();
        if (!string.IsNullOrEmpty(restAnimation)) entity.AnimManager?.StopAnimation(restAnimation);
        entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()
            .ReleaseAmbientIdleReservation(entity, plan);
        base.FinishExecute(cancelled);
        plan = null;
    }

    private void OnGoalReached()
    {
        if (plan?.Kind == "chase")
        {
            arrived = false;
            nextPlayRefreshAtMs = entity.World.ElapsedMilliseconds;
            return;
        }
        if (plan?.Kind == "play-runner")
        {
            FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
            if (playRunLegs < 3 && entity.World.ElapsedMilliseconds + 750 < endsAtMs
                && system.TryGetNextPlayEscapeTarget(entity, plan.PartnerEntityId, out Vec3d? nextTarget)
                && nextTarget != null)
            {
                playRunLegs++;
                plan.Target = nextTarget;
                NavigatePlanTarget(1800, failAsArrival: true);
                return;
            }
            arrived = true;
            nextPlayRefreshAtMs = entity.World.ElapsedMilliseconds + 500;
            return;
        }
        if (plan?.Kind == "perimeter-walk")
        {
            if (plan.WaypointIndex + 1 < plan.Waypoints.Count)
            {
                plan.WaypointIndex++;
                plan.Target = plan.Waypoints[plan.WaypointIndex].Clone();
                NavigatePlanTarget(2400);
                return;
            }

            arrived = true;
            navigation.Cancel();
            entity.Controls.StopAllMovement();
            return;
        }
        arrived = true;
        navigation.Cancel();
        entity.Controls.StopAllMovement();
        entity.AnimManager?.StopAnimation("Walk");
        if (!string.IsNullOrEmpty(restAnimation)) entity.AnimManager?.StartAnimation(restAnimation);
        if (plan?.Kind is "pack-space-visit" or "scent-investigation")
        {
            if (plan.LookAtTarget != null) FacePosition(plan.LookAtTarget);
            nextPackSpaceMoveAtMs = entity.World.ElapsedMilliseconds
                + (restAnimation.Equals("sniff", StringComparison.OrdinalIgnoreCase) ? 5000 : 1800);
        }
        if (plan?.Kind == "pack-cart-sit" && plan.OwnerEntityId > 0
            && entity.World.GetEntityById(plan.OwnerEntityId) is Entity owner)
        {
            FaceEntity(owner);
        }
        if (plan?.Kind == "interest-watch" && plan.LookAtTarget != null)
        {
            FacePosition(plan.LookAtTarget);
        }
    }

    private void FaceEntity(Entity target)
    {
        FacePosition(target.Pos.XYZ);
    }

    private void FacePosition(Vec3d target)
    {
        double dx = target.X - entity.Pos.X;
        double dz = target.Z - entity.Pos.Z;
        if (dx * dx + dz * dz < 0.001d) return;
        entity.Pos.Yaw = (float)Math.Atan2(dx, dz);
    }

    private void OnStuck() => stuck = true;

    private void NavigatePlanTarget(int searchDepth, float? arrivalOverride = null, bool failAsArrival = false)
    {
        if (plan == null)
        {
            stuck = true;
            return;
        }

        bool approachesMovingEntity = plan.Kind is "shadow-follow" or "chase"
            or "companion-rest" or "packmate-greeting";
        Action failed = failAsArrival
            ? () =>
            {
                arrived = true;
                nextPlayRefreshAtMs = entity.World.ElapsedMilliseconds + 500;
            }
            : OnStuck;
        bool queued = navigation.Start(
            plan.Target,
            plan.MoveSpeed,
            arrivalOverride ?? plan.TargetDistance,
            OnGoalReached,
            failed,
            searchDepth,
            approachesMovingEntity
                ? CompanionNavigationTargetKind.Approach
                : CompanionNavigationTargetKind.Position,
            4);
        if (!queued && !stuck && !failAsArrival)
        {
            OnStuck();
        }
    }

    private AnimationMetaData ResolveAnimation(string code)
    {
        code = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, code);
        AnimationMetaData? configured = entity.Properties.Client.Animations?
            .FirstOrDefault(animation => string.Equals(animation.Code, code, StringComparison.OrdinalIgnoreCase));
        return configured?.Clone() ?? new AnimationMetaData
        {
            Code = code, Animation = code, AnimationSpeed = 1f, EaseInSpeed = 2f, EaseOutSpeed = 2f
        }.Init();
    }
}

internal sealed class FoxIdlePlan
{
    public string Kind = string.Empty;
    public Vec3d Target = new();
    public long PartnerEntityId;
    public string Animation = "sit";
    public bool Immediate;
    public int DurationMs = 10000;
    public float MoveSpeed = 0.018f;
    public float TargetDistance = 0.65f;
    public long OwnerEntityId;
    public long AbsoluteEndAtMs;
    public bool IgnoreShelter;
    public bool RespectShelterWhenForced;
    public List<Vec3d> Waypoints = new();
    public int WaypointIndex;
    public Vec3d? LookAtTarget;
    public string ReservationKey = string.Empty;
}
