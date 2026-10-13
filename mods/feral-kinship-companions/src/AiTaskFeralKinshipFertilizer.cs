#nullable enable
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
namespace FeralKinshipCompanions;

public sealed class AiTaskFeralKinshipFertilizer : AiTaskBase
{
    private readonly CompanionNavigation navigation;
    private FertilizerJob? job;
    private bool carrying, done;
    private long nextScan;
    public AiTaskFeralKinshipFertilizer(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig) => navigation = new CompanionNavigation(entity, pathTraverser);
    protected override void SetDefaultValues()
    {
        base.SetDefaultValues(); Priority = 1.3635f; priorityForCancel = 1.3635f;
        MinCooldownMs = 18000; MaxCooldownMs = 36000;
    }
    public override bool ShouldExecute()
    {
        long now = entity.World.ElapsedMilliseconds;
        if (now < nextScan || IsOnCooldown()) return false;
        if (nextScan == 0) { nextScan = now + 750 + rand.Next(4500); return false; }
        nextScan = now + (long)((7000 + rand.Next(7000)) * FeralKinshipCompanionSystem.GetDutyScanIntervalMultiplier(entity, true));
        var system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        return !FeralKinshipCompanionSystem.ShouldPauseAmbientCompanionAi(entity, this)
            && system.TryPlanFertilizer(entity, out job);
    }
    public override void StartExecute()
    {
        base.StartExecute(); carrying = done = false; Navigate();
    }
    private void Navigate()
    {
        if (job == null) { done = true; return; }
        navigation.Start(carrying ? job.CropApproach : job.SourceApproach, .022f, .5f,
            Arrived, () => done = true, 2400, CompanionNavigationTargetKind.StorageApproach,
            adaptiveSearch: true, resumableSearch: true);
    }
    private void Arrived()
    {
        if (done || job == null) return;
        var system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (!carrying)
        {
            if (!system.TakeFertilizerDose(entity, job)) { done = true; return; }
            // Finish delivery of this one fetched dose before ordinary storage
            // tasks interpret it as cargo to put away. Commands/combat/food can
            // still interrupt, leaving the real item for existing recovery.
            carrying = true; Priority = 1.401f; priorityForCancel = 1.401f; Navigate();
        }
        else { done = true; system.ApplyFertilizerDose(entity, job); }
    }
    public override bool CanContinueExecute() => true;
    public override bool ContinueExecute(float dt)
    {
        if (done || job == null) return false;
        var system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (FeralKinshipCompanionSystem.ShouldPauseAmbientCompanionAi(entity, this)
            || !system.CanTendCrop(entity, job.Crop)) return false;
        if (navigation.IsAtValidatedDestination) Arrived(); else navigation.Tick();
        return !done && navigation.IsRunning;
    }
    public override void FinishExecute(bool cancelled)
    {
        navigation.Cancel(); job = null; done = true; carrying = false;
        Priority = 1.3635f; priorityForCancel = 1.3635f; base.FinishExecute(cancelled);
        // Existing persisted cargo recovery returns unused doses to enabled storage.
    }
}
