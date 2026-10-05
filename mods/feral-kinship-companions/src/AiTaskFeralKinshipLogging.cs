#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// Work Cart logging is deliberately split from cargo collection. The logger
/// stages one visible attack for every vanilla-discovered tree part and only
/// then breaks the complete reserved graph. Ground Cleanup owns every drop.
/// </summary>
public sealed class AiTaskFeralKinshipLogging : AiTaskBase
{
    private const long BiteIntervalMs = 650;
    private readonly CompanionNavigation navigation;
    private WorkCartLoggingPlan? plan;
    private bool biting;
    private bool done;
    private bool stuck;
    private bool changedTree;
    private long nextBiteAtMs;
    private long nextScanAtMs;
    private long nextReservationRefreshAtMs;
    private string attackAnimation = string.Empty;

    public AiTaskFeralKinshipLogging(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        navigation = new CompanionNavigation(entity, pathTraverser);
    }

    protected override void SetDefaultValues()
    {
        base.SetDefaultValues();
        Priority = 1.3645f;
        priorityForCancel = 1.3645f;
        MinCooldownMs = 5000;
        MaxCooldownMs = 10000;
    }

    public override bool ShouldExecute()
    {
        long now = entity.World.ElapsedMilliseconds;
        if (now < nextScanAtMs || IsOnCooldown()) return false;
        if (nextScanAtMs == 0)
        {
            nextScanAtMs = now + 1000 + rand.Next(5000);
            return false;
        }

        nextScanAtMs = now + 7000 + rand.Next(8000);
        FeralKinshipCompanionSystem system =
            entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        bool found = system.TryCreateWorkCartLoggingPlan(entity, out plan, out string dialogueEvent) && plan != null;
        if (!string.IsNullOrWhiteSpace(dialogueEvent))
            system.EmitCompanionRuntimeDialogue(entity, dialogueEvent, CompanionDialoguePriority.Low);
        return found;
    }

    public override void StartExecute()
    {
        base.StartExecute();
        biting = false;
        done = false;
        stuck = false;
        changedTree = false;
        nextBiteAtMs = 0;
        nextReservationRefreshAtMs = 0;

        FeralKinshipCompanionSystem system =
            entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (plan == null || !system.TryReserveWorkCartTree(entity, plan))
        {
            stuck = true;
            nextScanAtMs = entity.World.ElapsedMilliseconds + 2500;
            return;
        }
        system.EmitCompanionRuntimeDialogue(entity, "work.logging.claim_tree", CompanionDialoguePriority.Low);
        system.UpdateWorkCartLoggingDisplay(entity, plan);

        bool queued = navigation.Start(
            plan.ApproachTarget,
            FeralKinshipCompanionSystem.GetDutyMovementSpeed(entity, 0.024f),
            Math.Max(0.65f, FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity)),
            OnTreeReached,
            OnStuck,
            3200,
            CompanionNavigationTargetKind.Position,
            5);
        if (!queued && !biting && !stuck) OnStuck();
    }

    public override bool CanContinueExecute() => true;

    public override bool ContinueExecute(float dt)
    {
        if (plan == null || done || stuck) return false;

        FeralKinshipCompanionSystem system =
            entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (!system.CanContinueWorkCartLogging(entity, plan.CartPos)) return false;

        WorkCartLoggingTaskState taskState = system.GetWorkCartLoggingTaskState(plan);
        if (taskState == WorkCartLoggingTaskState.Completed)
        {
            done = true;
            return false;
        }
        if (taskState == WorkCartLoggingTaskState.Failed)
        {
            changedTree = true;
            return false;
        }

        long now = entity.World.ElapsedMilliseconds;
        if (now >= nextReservationRefreshAtMs)
        {
            if (!system.RefreshWorkCartTreeReservation(entity, plan))
            {
                system.AbandonWorkCartLoggingJob(entity, plan);
                return false;
            }
            nextReservationRefreshAtMs = now + 2000;
        }

        if (!biting)
        {
            if (navigation.IsAtValidatedDestination)
            {
                OnTreeReached();
                return true;
            }
            navigation.Tick();
            if (biting) return true;
            return navigation.IsRunning;
        }

        entity.Controls.StopAllMovement();
        if (taskState == WorkCartLoggingTaskState.Waiting)
        {
            return true;
        }
        if (now < nextBiteAtMs) return true;

        WorkCartLoggingBiteResult biteResult = system.TryAdvanceWorkCartLoggingBite(entity, plan);
        if (biteResult == WorkCartLoggingBiteResult.Failed)
        {
            changedTree = true;
            return false;
        }
        if (biteResult == WorkCartLoggingBiteResult.Waiting) return true;

        Face(plan.Root);
        if (!string.IsNullOrWhiteSpace(attackAnimation))
        {
            entity.AnimManager?.StopAnimation(attackAnimation);
            entity.AnimManager?.StartAnimation(attackAnimation);
        }
        entity.World.PlaySoundAt(
            new AssetLocation($"sounds/block/chop{entity.World.Rand.Next(1, 4)}"),
            entity,
            null,
            0f,
            16f,
            0.7f);
        system.UpdateWorkCartLoggingDisplay(entity, plan);
        nextBiteAtMs = now + BiteIntervalMs;
        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        navigation.Cancel();
        if (!string.IsNullOrWhiteSpace(attackAnimation))
        {
            entity.AnimManager?.StopAnimation(attackAnimation);
            attackAnimation = string.Empty;
        }

        entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()
            .ClearWorkCartLoggingDisplay(entity);
        FeralKinshipCompanionSystem dialogueSystem = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (!cancelled && done)
            dialogueSystem.EmitCompanionRuntimeDialogue(entity, "work.work_cart.chore_finished", CompanionDialoguePriority.Low);
        else if (changedTree)
            dialogueSystem.EmitCompanionRuntimeDialogue(entity, "work.logging.abandon_changed_tree", CompanionDialoguePriority.Low);
        else if (stuck)
            dialogueSystem.EmitCompanionRuntimeDialogue(entity, "work.logging.abandon_unloaded", CompanionDialoguePriority.Low);
        base.FinishExecute(cancelled);
        plan = null;
        biting = false;
    }

    private void OnTreeReached()
    {
        if (plan == null)
        {
            done = true;
            return;
        }

        navigation.Cancel();
        biting = true;
        nextBiteAtMs = entity.World.ElapsedMilliseconds;
        attackAnimation = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, "attack");
        entity.Controls.StopAllMovement();
        Face(plan.Root);
    }

    private void OnStuck()
    {
        stuck = true;
        nextScanAtMs = entity.World.ElapsedMilliseconds
            + FeralKinshipCompanionSystem.GetDutyFailureRetryMs(entity, 8000);
    }

    private void Face(BlockPos target)
    {
        double dx = target.X + 0.5d - entity.Pos.X;
        double dz = target.Z + 0.5d - entity.Pos.Z;
        if (dx * dx + dz * dz >= 0.001d)
        {
            entity.Pos.Yaw = (float)Math.Atan2(dx, dz);
        }
    }
}
