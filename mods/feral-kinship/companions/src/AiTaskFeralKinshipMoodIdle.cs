using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace FeralKinshipCompanions;

/// <summary>
/// Lets the fox's current Feral Kinship mood choose an idle animation while
/// still participating in the shared companion task priority system.
/// </summary>
public sealed class AiTaskFeralKinshipMoodIdle : AiTaskBase
{
    private readonly int minimumDurationMs;
    private readonly int maximumDurationMs;
    private long idleUntilMs;
    private string activeMood = string.Empty;
    private bool persistentRest;

    public AiTaskFeralKinshipMoodIdle(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        minimumDurationMs = taskConfig["minduration"].AsInt(6000);
        maximumDurationMs = Math.Max(minimumDurationMs, taskConfig["maxduration"].AsInt(12000));
    }

    protected override void SetDefaultValues()
    {
        base.SetDefaultValues();
        Priority = 1.36f;
        priorityForCancel = 1.36f;
        ExecutionChance = 0.18f;
        MinCooldownMs = 8000;
        MaxCooldownMs = 18000;
    }

    public override bool ShouldExecute()
    {
        return !IsOnCooldown()
            && FeralKinshipCompanionSystem.IsTamedFox(entity)
            && CanUseMoodIdleAnimation()
            && !string.IsNullOrEmpty(FeralKinshipCompanionSystem.GetMood(entity))
            && rand.NextDouble() <= ExecutionChance;
    }

    public override void StartExecute()
    {
        activeMood = FeralKinshipCompanionSystem.GetMood(entity);
        persistentRest = FeralKinshipCompanionSystem.IsPersistentRestMood(entity);

        if (activeMood == "sleepy")
        {
            MinCooldownMs = 20000;
            MaxCooldownMs = 40000;
        }
        else
        {
            MinCooldownMs = 8000;
            MaxCooldownMs = 18000;
        }

        string animationCode = FeralKinshipCompanionSystem.GetMoodIdleAnimation(entity);
        animMeta = ResolveAnimation(animationCode);
        idleUntilMs = persistentRest
            ? long.MaxValue
            : entity.World.ElapsedMilliseconds
                + minimumDurationMs
                + rand.Next(Math.Max(1, maximumDurationMs - minimumDurationMs + 1));

        base.StartExecute();
    }

    public override bool ContinueExecute(float dt)
    {
        // Do not require movement controls to be clear here. This task is
        // specifically allowed to replace the lower-priority wander task;
        // higher-priority companion tasks cancel it through the normal manager.
        return CanUseMoodIdleAnimation()
            && string.Equals(activeMood, FeralKinshipCompanionSystem.GetMood(entity), StringComparison.Ordinal)
            && entity.World.ElapsedMilliseconds < idleUntilMs;
    }

    private bool CanUseMoodIdleAnimation()
    {
        return entity.Alive
            && entity.MountedOn == null
            && !entity.Swimming
            && !entity.FeetInLiquid
            && (entity.RightHandItemSlot == null || entity.RightHandItemSlot.Empty);
    }

    private AnimationMetaData ResolveAnimation(string animationCode)
    {
        animationCode = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, animationCode);
        AnimationMetaData? configured = entity.Properties.Client.Animations?
            .FirstOrDefault(animation => string.Equals(animation.Code, animationCode, StringComparison.OrdinalIgnoreCase));

        if (configured != null)
        {
            return configured.Clone();
        }

        return new AnimationMetaData
        {
            Code = animationCode,
            Animation = animationCode,
            AnimationSpeed = 1f,
            EaseInSpeed = 1f,
            EaseOutSpeed = 1f
        }.Init();
    }
}
