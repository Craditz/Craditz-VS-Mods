#nullable enable
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed class AiTaskFeralKinshipQuarry : AiTaskBase
{
    private readonly CompanionNavigation navigation;
    private WorkCartQuarryJob? job;
    private bool arrived, failed;
    private Vec3d? workerApproach;
    private long nextScan, nextSwing;
    private string attack = "";
    private FeralKinshipCompanionSystem System => entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
    public AiTaskFeralKinshipQuarry(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig) { navigation = new(entity, pathTraverser); }
    protected override void SetDefaultValues()
    {
        base.SetDefaultValues(); Priority = 1.366f; priorityForCancel = 1.366f;
        MinCooldownMs = 100; MaxCooldownMs = 350;
    }
    public override bool ShouldExecute()
    {
        long now = entity.World.ElapsedMilliseconds;
        if (now < nextScan || IsOnCooldown()) return false;
        nextScan = now + 650 + rand.Next(500);
        return System.TryCreateQuarryJob(entity, out job) && job != null;
    }
    public override void StartExecute()
    {
        base.StartExecute(); arrived = failed = false; nextSwing = 0;
        if (job == null || job.Done || !System.TryGetCartJobApproach(entity, job, out workerApproach) || workerApproach == null) { failed = true; return; }
        if (!navigation.Start(workerApproach, FeralKinshipCompanionSystem.GetDutyMovementSpeed(entity, .024f),
            job.Cart.MiningMode ? .35f : .55f, Reached, Stuck, 10000, CompanionNavigationTargetKind.Position, 6, adaptiveSearch: true, exactTargetOnly: true)
            && !arrived) failed = true;
    }
    public override bool CanContinueExecute() => true;
    public override bool ContinueExecute(float dt)
    {
        if (job == null || job.Done || failed || !System.CanContinueQuarry(entity, job.Cart)) return false;
        long now = entity.World.ElapsedMilliseconds;
        job.Workers[entity.EntityId] = now;
        if (job.Cart.QuarrySupplyWorker == entity.EntityId) job.Cart.QuarrySupplyLease = now + 15000;
        if (!arrived)
        {
            if (navigation.IsAtValidatedDestination) Reached();
            else navigation.Tick();
            return arrived || navigation.IsRunning;
        }
        entity.Controls.StopAllMovement();
        if (job.Kind is QuarryJobKind.Mine or QuarryJobKind.Build && now >= nextSwing)
        {
            double dx = job.Target.X + .5 - entity.Pos.X, dz = job.Target.Z + .5 - entity.Pos.Z;
            if (dx * dx + dz * dz > .01) entity.Pos.Yaw = (float)Math.Atan2(dx, dz);
            if (attack.Length == 0) attack = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, "attack");
            if (attack.Length > 0) { entity.AnimManager?.StopAnimation(attack); entity.AnimManager?.StartAnimation(attack); }
            System.PlayQuarryWorkSound(entity);
            nextSwing = now + 650;
        }
        bool keepWorking = System.AdvanceQuarryWork(entity, job, dt);
        if (!keepWorking && !job.Done) failed = true;
        return keepWorking;
    }
    public override void FinishExecute(bool cancelled)
    {
        navigation.Cancel();
        if (attack.Length > 0) entity.AnimManager?.StopAnimation(attack);
        if (job != null) System.ReleaseQuarryJob(entity, job, failed);
        job = null; attack = "";
        base.FinishExecute(cancelled);
    }
    private void Reached()
    {
        navigation.Cancel();
        if (job == null || workerApproach == null || entity.Pos.SquareDistanceTo(workerApproach) > 2.25) { Stuck(); return; }
        arrived = true;
    }
    private void Stuck() { failed = true; nextScan = entity.World.ElapsedMilliseconds + 8000; }
}
