#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// Gives a juvenile an occasional lost-child fallback without making it
/// permanently follow a parent. Normal Companion ambient tasks own At Ease
/// behavior; this task only wakes up after a long interval and only when a
/// loaded, home-based parent is genuinely far away.
/// </summary>
public sealed class AiTaskFeralKinshipChildFamilyCheck : AiTaskBase
{
    private const long MinimumCheckIntervalMs = 45_000;
    private const long MaximumCheckIntervalMs = 75_000;
    private const float PanicDistance = 12f;
    private const float QuietNurseryPanicDistance = 18f;
    private const float ArrivalDistance = 6f;
    private const float ParentGoalDistance = 1.2f;

    private readonly float moveSpeed;
    private Entity? parent;
    private long nextCheckAtMs;
    private bool reachedParent;
    private readonly CompanionNavigation navigation;

    public AiTaskFeralKinshipChildFamilyCheck(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        moveSpeed = taskConfig["movespeed"].AsFloat(0.035f);
        nextCheckAtMs = entity.World.ElapsedMilliseconds + RandomCheckInterval();
        navigation = new CompanionNavigation(entity, pathTraverser);
    }

    protected override void SetDefaultValues()
    {
        base.SetDefaultValues();
        Priority = 2.85f;
        priorityForCancel = 2.85f;
    }

    public override bool ShouldExecute()
    {
        long now = entity.World.ElapsedMilliseconds;
        if (now < nextCheckAtMs
            || !FeralKinshipCompanionSystem.IsCompanionJuvenile(entity)
            || FeralKinshipCompanionSystem.GetCompanionActivityMode(entity) != CompanionActivityMode.AtEase)
        {
            return false;
        }

        // Whether or not the child panics, do not check again immediately.
        // This is deliberately a distracted-child moment, not a leash.
        nextCheckAtMs = now + RandomCheckInterval();
        parent = ResolveAvailableParent();
        float panicDistance = FeralKinshipCompanionSystem.GetFoxPackTalentRank(entity, "quiet-nursery") > 0
            ? QuietNurseryPanicDistance
            : PanicDistance;
        return parent != null && entity.Pos.SquareDistanceTo(parent.Pos) > panicDistance * panicDistance;
    }

    public override void StartExecute()
    {
        reachedParent = false;
        base.StartExecute();
        PlayPanicCry();

        if (parent == null || !navigation.Start(
                parent.Pos.XYZ,
                moveSpeed,
                Math.Max(ParentGoalDistance, ArrivalDistance),
                OnGoalReached,
                OnStuck,
                4000,
                CompanionNavigationTargetKind.Approach,
                4))
        {
            reachedParent = true;
        }
    }

    public override bool CanContinueExecute() => true;

    public override bool ContinueExecute(float dt)
    {
        if (reachedParent
            || !FeralKinshipCompanionSystem.IsCompanionJuvenile(entity)
            || FeralKinshipCompanionSystem.GetCompanionActivityMode(entity) != CompanionActivityMode.AtEase)
        {
            return false;
        }

        parent = ResolveAvailableParent();
        if (parent == null)
        {
            return false;
        }

        if (CompanionNavigation.IsClearArrival(
                entity, parent.Pos.XYZ, ArrivalDistance, allowTargetBlock: false))
        {
            reachedParent = true;
            return false;
        }

        navigation.Tick();
        return navigation.IsRunning;
    }

    public override void FinishExecute(bool cancelled)
    {
        navigation.Cancel();
        parent = null;
        base.FinishExecute(cancelled);
    }

    private Entity? ResolveAvailableParent()
    {
        Entity? mother = entity.World.GetEntityById(entity.WatchedAttributes.GetLong("motherId", 0));
        if (IsAvailableParent(mother))
        {
            return mother;
        }

        ITreeAttribute? status = FeralKinshipCompanionSystem.GetDomesticationStatus(entity);
        string fatherFoxId = status?.GetString(FeralKinshipCompanionSystem.ParentFatherIdKey, string.Empty) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fatherFoxId))
        {
            return null;
        }

        FeralKinshipCompanionSystem? system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        return system?.FindLoadedCompanionByFoxId(fatherFoxId) is Entity father && IsAvailableParent(father)
            ? father
            : null;
    }

    private bool IsAvailableParent(Entity? candidate)
    {
        if (candidate?.Alive != true
            || candidate.Pos.Dimension != entity.Pos.Dimension
            || FeralKinshipCompanionSystem.IsFoxAwayFromWorld(candidate)
            || FeralKinshipCompanionSystem.IsFoxIncapacitated(candidate))
        {
            return false;
        }

        string mode = FeralKinshipCompanionSystem.GetCompanionActivityMode(candidate);
        return mode is CompanionActivityMode.AtEase or CompanionActivityMode.Rest;
    }

    private long RandomCheckInterval()
    {
        long interval = MinimumCheckIntervalMs
            + entity.World.Rand.Next((int)(MaximumCheckIntervalMs - MinimumCheckIntervalMs + 1));
        // Doubling the interval halves the check frequency without adding a
        // new nursery state machine or parent-tracking subsystem.
        return FeralKinshipCompanionSystem.GetFoxPackTalentRank(entity, "quiet-nursery") > 0
            ? interval * 2
            : interval;
    }

    private void PlayPanicCry()
    {
        if (entity.Properties.Sounds != null
            && entity.Properties.Sounds.TryGetValue("idle", out SoundAttributes idleSound))
        {
            entity.World.PlaySoundAt(idleSound, entity);
        }
    }

    private void OnGoalReached()
    {
        reachedParent = true;
    }

    private void OnStuck()
    {
        reachedParent = true;
    }
}
