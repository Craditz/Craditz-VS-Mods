#nullable enable

using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>Non-lethal starvation fallback: go home and remain lying down.</summary>
public sealed class AiTaskFeralKinshipStarvingRest : AiTaskBase
{
    private readonly float moveSpeed;
    private Vec3d? target;
    private bool arrived;
    private bool stuck;
    private long nextCheckAtMs;
    private string sleepAnimation = string.Empty;
    private string walkAnimation = string.Empty;
    private readonly CompanionNavigation navigation;

    public AiTaskFeralKinshipStarvingRest(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        moveSpeed = taskConfig["movespeed"].AsFloat(0.02f);
        navigation = new CompanionNavigation(entity, pathTraverser);
    }

    public override bool ShouldExecute()
    {
        if (entity.World.ElapsedMilliseconds < nextCheckAtMs
            || FeralKinshipCompanionSystem.IsFoxAwayFromWorld(entity)
            || FeralKinshipCompanionSystem.IsFoxIncapacitated(entity))
        {
            return false;
        }
        nextCheckAtMs = entity.World.ElapsedMilliseconds + 1000;
        FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        return system.IsCompanionFoodRestricted(entity)
            && system.TryResolveCompanionStarvingRestTarget(entity, out target)
            && target != null;
    }

    public override void StartExecute()
    {
        arrived = false;
        stuck = false;
        animMeta = ResolveAnimation("Walk");
        base.StartExecute();
        if (target == null)
        {
            stuck = true;
            return;
        }

        float arrival = Math.Max(0.75f, FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity));
        bool queued = navigation.Start(
            target,
            moveSpeed,
            arrival,
            OnGoalReached,
            OnFailed,
            3000,
            CompanionNavigationTargetKind.Position,
            4);
        if (!queued && !stuck) OnFailed();
    }

    public override bool CanContinueExecute() => true;

    public override bool ContinueExecute(float dt)
    {
        FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (stuck || !system.IsCompanionFoodRestricted(entity))
        {
            return false;
        }
        if (arrived)
        {
            entity.Controls.StopAllMovement();
            return true;
        }
        navigation.Tick();
        return navigation.IsRunning;
    }

    public override void FinishExecute(bool cancelled)
    {
        navigation.Cancel();
        if (!string.IsNullOrWhiteSpace(sleepAnimation))
        {
            entity.AnimManager?.StopAnimation(sleepAnimation);
            sleepAnimation = string.Empty;
        }
        if (!string.IsNullOrWhiteSpace(walkAnimation))
        {
            entity.AnimManager?.StopAnimation(walkAnimation);
            walkAnimation = string.Empty;
        }
        target = null;
        base.FinishExecute(cancelled);
    }

    private void OnGoalReached()
    {
        arrived = true;
        navigation.Cancel();
        entity.Controls.StopAllMovement();
        walkAnimation = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, "walk");
        entity.AnimManager?.StopAnimation(walkAnimation);
        sleepAnimation = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, "sleep");
        // AiTaskBase keeps applying animMeta while the task remains active.
        // Leaving the approach metadata as Walk made a starving companion
        // visibly walk in place after reaching its resting fallback.
        animMeta = ResolveAnimation("Sleep");
        entity.AnimManager?.StartAnimation(new AnimationMetaData
        {
            Code = sleepAnimation,
            Animation = sleepAnimation,
            AnimationSpeed = 1f
        });
    }

    private void OnFailed()
    {
        stuck = true;
        nextCheckAtMs = entity.World.ElapsedMilliseconds + 3000;
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
            AnimationSpeed = 2f
        };
    }
}
