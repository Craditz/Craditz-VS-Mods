#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// Returns a fox to precise shelter at its bed, or loosely to the wider pack
/// territory during safe daytime, without overriding higher-priority behavior.
/// </summary>
public sealed class AiTaskFeralKinshipReturnToDen : AiTaskBase
{
    public bool StormMode { get; set; }
    public bool DayTerritoryMode { get; set; }

    private const int PathSearchDepth = 3000;
    private const int DenCheckIntervalMs = 1000;
    private const int NoPathRetryMs = 5000;
    private readonly float moveSpeed;
    private readonly float configuredTerritoryDistance;
    private Vec3d? target;
    private bool noPath;
    private bool goalReached;
    private bool returningForShelter;
    private long nextCheckAtMs;
    private bool routineShelterPending;
    private long routineShelterEligibleAtMs;
    private readonly CompanionNavigation navigation;

    public AiTaskFeralKinshipReturnToDen(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        // As with the courier task, multiple configured instances share this
        // class. Read their roles explicitly instead of depending on generic
        // property population to distinguish storm, routine, and territory AI.
        StormMode = taskConfig["stormMode"].AsBool(false);
        DayTerritoryMode = taskConfig["dayTerritoryMode"].AsBool(false);
        moveSpeed = taskConfig["movespeed"].AsFloat(0.02f);
        float territoryDistance = taskConfig["territoryDistance"].AsFloat(36f);
        configuredTerritoryDistance = territoryDistance;
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
        nextCheckAtMs = entity.World.ElapsedMilliseconds + DenCheckIntervalMs;

        FeralKinshipCompanionSystem? system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        bool stormActive = system?.IsTemporalStormActive() == true;
        if (DayTerritoryMode)
        {
            if (stormActive || system?.ShouldFoxSeekDenShelter(entity) == true) return false;
            returningForShelter = false;
            if (system?.TryGetFoxDayTerritoryCenter(entity, out target) != true || target == null)
            {
                target = null;
                return false;
            }
            float dayTerritoryDistance = system?.GetCompanionTerritoryRadius(entity)
                ?? configuredTerritoryDistance;
            return entity.Pos.SquareDistanceTo(target)
                > dayTerritoryDistance * dayTerritoryDistance;
        }
        if (StormMode != stormActive)
        {
            if (!StormMode)
            {
                routineShelterPending = false;
                routineShelterEligibleAtMs = 0;
            }
            return false;
        }
        returningForShelter = system?.ShouldFoxSeekDenShelter(entity) == true;
        if (!returningForShelter)
        {
            routineShelterPending = false;
            routineShelterEligibleAtMs = 0;
            return false;
        }
        if (!StormMode)
        {
            if (!routineShelterPending)
            {
                routineShelterPending = true;
                routineShelterEligibleAtMs = entity.World.ElapsedMilliseconds + 5000 + rand.Next(40001);
            }
            if (entity.World.ElapsedMilliseconds < routineShelterEligibleAtMs) return false;
        }
        bool hasTarget = system?.TryGetAssignedFoxDen(entity, out target) == true;
        if (!hasTarget || target == null)
        {
            target = null;
            return false;
        }

        double distanceSquared = entity.Pos.SquareDistanceTo(target);
        double shelterDistance = Math.Max(0.55f, FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity));
        float shelterTerritoryDistance = system?.GetCompanionTerritoryRadius(entity)
            ?? configuredTerritoryDistance;
        if (!returningForShelter)
        {
            return distanceSquared > shelterTerritoryDistance * shelterTerritoryDistance;
        }
        return distanceSquared > shelterDistance * shelterDistance
            || !CompanionNavigation.IsClearArrival(
                entity, target, shelterDistance, allowTargetBlock: true);
    }

    public override void StartExecute()
    {
        base.StartExecute();
        noPath = false;
        goalReached = false;
        if (target != null)
        {
            // Let A* route to a traversable block beside the low bed, then let
            // the traverser use the exact bed-surface target for the last step.
            // A raised air-block target is not a reliable A* destination.
            bool queued = navigation.Start(
                target,
                moveSpeed,
                FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity),
                OnGoalReached,
                OnNavigationFailed,
                PathSearchDepth,
                CompanionNavigationTargetKind.Position,
                5,
                adaptiveSearch: true
            );
            if (!queued && !noPath)
            {
                OnNoPath();
            }
        }
    }

    public override bool CanContinueExecute()
    {
        // ContinueExecute handles both the queued route and its asynchronous
        // terminal callback; returning false here would strand the task slot.
        return true;
    }

    public override bool ContinueExecute(float dt)
    {
        if (noPath || goalReached || target == null)
        {
            return false;
        }

        if (returningForShelter)
        {
            FeralKinshipCompanionSystem? system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
            if (system?.ShouldFoxSeekDenShelter(entity) != true
                || (system.IsTemporalStormActive() != StormMode))
            {
                return false;
            }
        }
        else
        {
            FeralKinshipCompanionSystem? system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
            if (system?.ShouldFoxSeekDenShelter(entity) == true) return false;
        }

        navigation.Tick();
        return navigation.IsRunning;
    }

    public override void FinishExecute(bool cancelled)
    {
        navigation.Cancel();
        base.FinishExecute(cancelled);
    }

    private void OnGoalReached()
    {
        goalReached = true;
    }

    private void OnNoPath()
    {
        noPath = true;
        nextCheckAtMs = entity.World.ElapsedMilliseconds + NoPathRetryMs;
    }

    private void OnNavigationFailed()
    {
        // The shared navigator has already exhausted alternate approaches,
        // including its bounded stuck recovery, so use the longer no-route
        // delay instead of immediately hammering the same destination.
        OnNoPath();
    }
}
