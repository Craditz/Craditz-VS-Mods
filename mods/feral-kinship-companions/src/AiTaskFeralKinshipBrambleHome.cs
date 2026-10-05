#nullable enable

using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

/// <summary>
/// Keeps Bramble at his explicitly assigned bed or near the owner's active
/// Pack Cart while he is in Stay mode. A missing cart deliberately leaves him
/// where he is until one becomes available.
/// </summary>
public sealed class AiTaskFeralKinshipBrambleHome : AiTaskBase
{
    private readonly float moveSpeed;
    private readonly CompanionNavigation navigation;
    private Vec3d? target;
    private bool assignedBed;
    private bool arrived;
    private bool stopped;
    private string restAnimation = string.Empty;
    private string walkAnimation = string.Empty;
    private long nextAttemptAtMs;

    public AiTaskFeralKinshipBrambleHome(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        moveSpeed = taskConfig["movespeed"].AsFloat(0.035f);
        navigation = new CompanionNavigation(entity, pathTraverser);
    }

    protected override void SetDefaultValues()
    {
        base.SetDefaultValues();
        Priority = 3.4f;
        priorityForCancel = 3.4f;
    }

    public override bool ShouldExecute()
    {
        if (entity.World.ElapsedMilliseconds < nextAttemptAtMs
            || !FeralKinshipCompanionSystem.CanRunCompanionCommandTask(entity)
            || FeralKinshipCompanionSystem.GetCompanionActivityMode(entity) == CompanionActivityMode.Follow)
        {
            return false;
        }

        FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (!system.TryResolveBrambleStayTarget(entity, out target, out assignedBed) || target == null)
        {
            target = null;
            return false;
        }

        return true;
    }

    public override void StartExecute()
    {
        stopped = false;
        walkAnimation = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, "walk");
        arrived = target != null && CompanionNavigation.IsClearArrival(
            entity,
            target,
            Math.Max(0.7f, FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity)),
            allowTargetBlock: true);
        animMeta = ResolveAnimation(arrived ? GetRestAnimationCode() : "walk");
        base.StartExecute();
        if (arrived)
        {
            OnGoalReached();
            return;
        }

        if (target == null || !navigation.Start(
                target,
                moveSpeed,
                Math.Max(0.7f, FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity)),
                OnGoalReached,
                OnFailed,
                5000,
                CompanionNavigationTargetKind.Position,
                5))
        {
            if (!stopped) OnFailed();
        }
    }

    public override bool CanContinueExecute() => true;

    public override bool ContinueExecute(float dt)
    {
        if (stopped
            || FeralKinshipCompanionSystem.GetCompanionActivityMode(entity) == CompanionActivityMode.Follow)
        {
            return false;
        }

        FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (!system.TryResolveBrambleStayTarget(entity, out Vec3d? currentTarget, out bool currentAssignedBed)
            || currentTarget == null)
        {
            return false;
        }

        assignedBed = currentAssignedBed;
        if (!arrived)
        {
            if (CompanionNavigation.IsClearArrival(
                    entity,
                    currentTarget,
                    Math.Max(0.7f, FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity)),
                    allowTargetBlock: true))
            {
                OnGoalReached();
                return true;
            }

            navigation.Tick();
            return navigation.IsRunning;
        }

        if (target == null || target.SquareDistanceTo(currentTarget) > 1.5d * 1.5d)
        {
            arrived = false;
            target = currentTarget;
            entity.AnimManager?.StopAnimation(restAnimation);
            animMeta = ResolveAnimation("walk");
            navigation.Cancel();
            if (!navigation.Start(
                    target,
                    moveSpeed,
                    Math.Max(0.7f, FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity)),
                    OnGoalReached,
                    OnFailed,
                    5000,
                    CompanionNavigationTargetKind.Position,
                    5))
            {
                OnFailed();
            }
            return true;
        }

        entity.Controls.StopAllMovement();
        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        navigation.Cancel();
        if (!string.IsNullOrWhiteSpace(restAnimation)) entity.AnimManager?.StopAnimation(restAnimation);
        if (!string.IsNullOrWhiteSpace(walkAnimation)) entity.AnimManager?.StopAnimation(walkAnimation);
        target = null;
        restAnimation = string.Empty;
        walkAnimation = string.Empty;
        base.FinishExecute(cancelled);
    }

    private void OnGoalReached()
    {
        arrived = true;
        stopped = false;
        navigation.Cancel();
        entity.Controls.StopAllMovement();
        if (!string.IsNullOrWhiteSpace(walkAnimation)) entity.AnimManager?.StopAnimation(walkAnimation);
        restAnimation = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, GetRestAnimationCode());
        animMeta = ResolveAnimation(GetRestAnimationCode());
        entity.AnimManager?.StartAnimation(new AnimationMetaData
        {
            Code = restAnimation,
            Animation = restAnimation,
            AnimationSpeed = 1f
        });
    }

    private void OnFailed()
    {
        stopped = true;
        nextAttemptAtMs = entity.World.ElapsedMilliseconds + 1500;
    }

    private string GetRestAnimationCode()
    {
        if (assignedBed)
        {
            double hour = entity.World.Calendar.HourOfDay;
            if (hour < 6.5d || hour >= 19.5d) return "sleep";
        }
        return "sit";
    }

    private AnimationMetaData ResolveAnimation(string requested)
    {
        string code = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, requested);
        AnimationMetaData? configured = entity.Properties.Client.Animations?
            .FirstOrDefault(animation => string.Equals(animation.Code, code, StringComparison.OrdinalIgnoreCase));
        return configured?.Clone() ?? new AnimationMetaData
        {
            Code = code,
            Animation = code,
            AnimationSpeed = 1f,
            EaseInSpeed = 1f,
            EaseOutSpeed = 1f
        }.Init();
    }
}
