#nullable enable
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>After a failed cart journey, genuinely try returning to the cart.
/// This still works when a new wall means the mining planner has no new job.</summary>
public sealed class AiTaskFeralKinshipCartRecovery : AiTaskBase
{
    private readonly CompanionNavigation navigation;
    private Vec3d? goal;
    private bool done;
    private long nextAttempt;
    private FeralKinshipCompanionSystem System => entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
    public AiTaskFeralKinshipCartRecovery(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig) { navigation = new(entity, pathTraverser); }
    protected override void SetDefaultValues()
    { base.SetDefaultValues(); Priority = 1.368f; priorityForCancel = 1.368f; MinCooldownMs = MaxCooldownMs = 1000; }
    public override bool ShouldExecute()
        => entity.World.ElapsedMilliseconds >= nextAttempt && System.TryCartRecoveryGoal(entity, out goal);
    public override void StartExecute()
    {
        base.StartExecute(); done = false;
        if (goal == null) { done = true; return; }
        navigation.Start(goal, FeralKinshipCompanionSystem.GetDutyMovementSpeed(entity, .024f), .6f,
            () => { done = true; navigation.Cancel(); }, () => { done = true; },
            6000, CompanionNavigationTargetKind.Position, 2, adaptiveSearch: true);
    }
    public override bool CanContinueExecute() => true;
    public override bool ContinueExecute(float dt)
    {
        if (done || !System.TryCartRecoveryGoal(entity, out _)) return false;
        navigation.Tick(); return !done && navigation.IsRunning;
    }
    public override void FinishExecute(bool cancelled)
    { navigation.Cancel(); nextAttempt = entity.World.ElapsedMilliseconds + 8000; base.FinishExecute(cancelled); }
}
