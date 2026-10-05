#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

/// <summary>
/// A short-range snow escape action. It is deliberately separate from the
/// normal Snow Shoveling route so a companion can clear a nearby layer that is
/// blocking movement even while the shelter AI is otherwise active.
/// </summary>
public sealed class AiTaskFeralKinshipClearSnow : AiTaskBase
{
    private readonly bool emergencyOnly;
    private BlockPos? targetPos;
    private bool done;
    private bool emergencyMode;
    private long breakAtMs;
    private string attackAnimation = string.Empty;

    public AiTaskFeralKinshipClearSnow(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        emergencyOnly = taskConfig["emergencyOnly"].AsBool(false);
    }

    protected override void SetDefaultValues()
    {
        base.SetDefaultValues();
        Priority = 1.80f;
        priorityForCancel = 1.80f;
        MinCooldownMs = 2500;
        MaxCooldownMs = 5000;
    }

    public override bool ShouldExecute()
    {
        if (FeralKinshipCompanionSystem.IsCompanionJuvenile(entity)
            || FeralKinshipCompanionSystem.IsFoxAwayFromWorld(entity)
            || FeralKinshipCompanionSystem.IsFoxIncapacitated(entity)
            || entity.Swimming
            || entity.FeetInLiquid) return false;

        FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (emergencyOnly)
        {
            // Snow recovery is still a duty, not an unconditional command.
            // Navigation failures can leave a temporary recovery intent near
            // any blocked route; without this gate an unassigned companion
            // could start shoveling simply because snow happened to be nearby.
            if (!FeralKinshipCompanionSystem.IsSnowShovelingEnabled(entity)) return false;

            if (!system.TryFindSnowRescueTarget(entity, out targetPos, out emergencyMode)
                || !emergencyMode
                || targetPos == null) return false;
            if (system.HasActiveCompanionCombatResponse(entity))
            {
                CompanionNavigation.ClearEmergencySnowGoal(entity);
                return false;
            }
            return true;
        }

        if (!system.TryFindSnowRescueTarget(entity, out targetPos, out emergencyMode)
            || targetPos == null
            || emergencyMode) return false;

        return FeralKinshipCompanionSystem.IsSnowShovelingEnabled(entity)
            && FeralKinshipCompanionSystem.GetCompanionActivityMode(entity) == CompanionActivityMode.AtEase
            && !FeralKinshipCompanionSystem.HasFoxCommandedStorageJob(entity)
            && !FeralKinshipCompanionSystem.HasFoxStorageCargo(entity)
            && !IsOnCooldown();
    }

    public override void StartExecute()
    {
        base.StartExecute();
        done = false;
        if (targetPos == null
            || !entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()
                .TryReserveSnowTarget(entity, targetPos))
        {
            done = true;
            return;
        }
        entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()
            .EmitCompanionRuntimeDialogue(entity,
                !emergencyMode && FeralKinshipCompanionSystem.IsSnowballCollectionEnabled(entity)
                    ? "work.snowball_collection.begin" : "work.snow_clearing.begin",
                CompanionDialoguePriority.Low);
        breakAtMs = entity.World.ElapsedMilliseconds + 650;
        attackAnimation = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, "attack");
        entity.Controls.StopAllMovement();
        FaceTarget();
        if (!string.IsNullOrWhiteSpace(attackAnimation))
        {
            entity.AnimManager?.StopAnimation("Walk");
            entity.AnimManager?.StartAnimation(attackAnimation);
        }
    }

    public override bool CanContinueExecute() => true;

    public override bool ContinueExecute(float dt)
    {
        if (done || targetPos == null
            || FeralKinshipCompanionSystem.IsFoxAwayFromWorld(entity)
            || FeralKinshipCompanionSystem.IsFoxIncapacitated(entity)
            || entity.Swimming
            || entity.FeetInLiquid
            || (!emergencyMode
                && (!FeralKinshipCompanionSystem.IsSnowShovelingEnabled(entity)
                    || FeralKinshipCompanionSystem.GetCompanionActivityMode(entity) != CompanionActivityMode.AtEase))
            || !IsTargetStillClose()) return false;

        FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (emergencyMode && system.HasActiveCompanionCombatResponse(entity))
        {
            CompanionNavigation.ClearEmergencySnowGoal(entity);
            return false;
        }
        if (!system.RefreshSnowTargetReservation(entity, targetPos)) return false;

        entity.Controls.StopAllMovement();
        FaceTarget();
        if (entity.World.ElapsedMilliseconds < breakAtMs) return true;

        done = true;
        system.TryTakeSnow(
            entity,
            targetPos,
            !emergencyMode && FeralKinshipCompanionSystem.IsSnowballCollectionEnabled(entity),
            allowWhileCarrying: emergencyMode);
        return false;
    }

    public override void FinishExecute(bool cancelled)
    {
        entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()
            .ReleaseSnowTargetReservation(entity, targetPos);
        if (!string.IsNullOrWhiteSpace(attackAnimation))
        {
            entity.AnimManager?.StopAnimation(attackAnimation);
        }
        if (!cancelled && done)
        {
            FeralKinshipCompanionSystem system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
            system.EmitCompanionRuntimeDialogue(entity,
                system.IsFoxAssignedToWorkCart(entity) ? "work.work_cart.chore_finished" : "work.chore_finished",
                CompanionDialoguePriority.Low);
        }
        base.FinishExecute(cancelled);
        targetPos = null;
        emergencyMode = false;
    }

    private bool IsTargetStillClose()
    {
        if (targetPos == null || !FeralKinshipCompanionSystem.IsSnowLayer(entity.World.BlockAccessor.GetBlock(targetPos)))
        {
            return false;
        }

        return CompanionNavigation.CanReachSnowLayer(entity, targetPos, 3.2);
    }

    private void FaceTarget()
    {
        if (targetPos == null) return;
        double dx = targetPos.X + 0.5 - entity.Pos.X;
        double dz = targetPos.Z + 0.5 - entity.Pos.Z;
        if (dx * dx + dz * dz >= 0.001d)
        {
            entity.Pos.Yaw = (float)Math.Atan2(dx, dz);
        }
    }

}
