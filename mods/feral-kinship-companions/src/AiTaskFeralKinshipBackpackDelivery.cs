#nullable enable

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed class AiTaskFeralKinshipBackpackDelivery : AiTaskBase
{
    private const int PathSearchDepth = 5000;
    private readonly float moveSpeed;
    private readonly CompanionNavigation navigation;
    private FeralKinshipCompanionSystem? system;
    private Vec3d? target;
    private bool stopped;
    private string phaseAtStart = string.Empty;
    private long nextAttemptAtMs;

    public AiTaskFeralKinshipBackpackDelivery(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        moveSpeed = taskConfig["movespeed"].AsFloat(0.045f);
        navigation = new CompanionNavigation(entity, pathTraverser);
    }

    public override bool ShouldExecute()
    {
        if (entity.World.ElapsedMilliseconds < nextAttemptAtMs
            || !FeralKinshipCompanionSystem.IsBackpackDeliveryActive(entity)
            || FeralKinshipCompanionSystem.IsFoxIncapacitated(entity)) return false;
        system ??= entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (system.TryResolveBackpackDeliveryTarget(entity, out target) && target != null) return true;
        return FeralKinshipCompanionSystem.IsBackpackDeliveryActive(entity);
    }

    public override void StartExecute()
    {
        base.StartExecute();
        stopped = false;
        phaseAtStart = entity.WatchedAttributes.GetString(
            FeralKinshipCompanionSystem.BackpackDeliveryPhaseKey,
            string.Empty);
        if (target == null)
        {
            entity.Controls.StopAllMovement();
            entity.Pos.Motion.Set(0, 0, 0);
            return;
        }
        if (!navigation.Start(
                target,
                moveSpeed,
                FeralKinshipCompanionSystem.GetCompanionArrivalRadius(entity),
                OnArrived,
                OnFailed,
                PathSearchDepth,
                CompanionNavigationTargetKind.Position,
                6,
                adaptiveSearch: true))
        {
            if (!stopped) OnFailed();
        }
    }

    public override bool CanContinueExecute() => true;

    public override bool ContinueExecute(float dt)
    {
        if (stopped
            || !FeralKinshipCompanionSystem.IsBackpackDeliveryActive(entity)
            || !string.Equals(
                phaseAtStart,
                entity.WatchedAttributes.GetString(
                    FeralKinshipCompanionSystem.BackpackDeliveryPhaseKey,
                    string.Empty),
                System.StringComparison.Ordinal)) return false;
        if (target == null)
        {
            entity.Controls.StopAllMovement();
            entity.Pos.Motion.Set(0, 0, 0);
            return true;
        }
        navigation.Tick();
        return navigation.IsRunning;
    }

    public override void FinishExecute(bool cancelled)
    {
        navigation.Cancel();
        target = null;
        base.FinishExecute(cancelled);
    }

    private void OnArrived()
    {
        stopped = true;
        system?.OnBackpackDeliveryArrived(entity);
    }

    private void OnFailed()
    {
        stopped = true;
        nextAttemptAtMs = entity.World.ElapsedMilliseconds + 1200;
        system?.OnBackpackDeliveryRouteFailed(entity);
    }
}
