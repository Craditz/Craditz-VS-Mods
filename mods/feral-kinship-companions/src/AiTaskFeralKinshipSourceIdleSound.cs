#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// Source-defined idle vocalization without the native base class's
/// uncancellable delayed sound callback.
/// </summary>
public sealed class AiTaskFeralKinshipSourceIdleSound : AiTaskIdle
{
    private readonly AssetLocation? sourceSound;
    private readonly int configuredDelayMs;
    private long runGeneration;
    private bool initialSoundPending;

    public AiTaskFeralKinshipSourceIdleSound(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        string? soundCode = taskConfig["sourceSound"].AsString();
        if (!string.IsNullOrWhiteSpace(soundCode))
        {
            sourceSound = AssetLocation.Create(soundCode, entity.Code.Domain)
                .WithPathPrefixOnce("sounds/");
        }
        configuredDelayMs = (int)Math.Clamp(soundStartMs, 0, int.MaxValue);
    }

    public override void StartExecute()
    {
        long generation = ++runGeneration;
        bool shouldStartSound = sourceSound != null
            && entity.World.Rand.NextDouble() <= soundChance;
        base.StartExecute();

        if (!shouldStartSound)
        {
            return;
        }

        initialSoundPending = true;
        // A zero-delay callback lets explicit AiTaskManager.ExecuteTask calls
        // finish assigning their slot before the active-slot check runs.
        entity.World.RegisterCallback(_ => PlayIfCurrent(generation), configuredDelayMs);
    }

    public override bool ContinueExecute(float dt)
    {
        if (sourceSound != null
            && !initialSoundPending
            && soundRepeatMs > 0
            && entity.World.ElapsedMilliseconds - lastSoundTotalMs > soundRepeatMs)
        {
            PlayIfCurrent(runGeneration);
        }

        return base.ContinueExecute(dt);
    }

    public override void FinishExecute(bool cancelled)
    {
        // TaskManager still exposes the active slot while this runs.
        runGeneration++;
        initialSoundPending = false;
        base.FinishExecute(cancelled);
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        // The engine notifies tasks of despawn without necessarily calling
        // FinishExecute or clearing the active slot.
        runGeneration++;
        initialSoundPending = false;
        base.OnEntityDespawn(despawn);
    }

    private void PlayIfCurrent(long callbackGeneration)
    {
        EntityBehaviorTaskAI? taskBehavior = entity.GetBehavior<EntityBehaviorTaskAI>();
        AiTaskManager? taskManager = taskBehavior?.TaskManager;
        bool activeSlot = taskManager != null
            && taskManager.ActiveTasksBySlot != null
            && taskManager.ActiveTasksBySlot.Length > CompanionSourceIdleSoundPolicy.Slot
            && ReferenceEquals(taskManager.ActiveTasksBySlot[CompanionSourceIdleSoundPolicy.Slot], this);
        bool entityActive = entity.State == EnumEntityState.Active && !entity.ShouldDespawn;
        bool entityStillInWorld = ReferenceEquals(entity.World.GetEntityById(entity.EntityId), entity);

        if (sourceSound == null
            || !CompanionSourceIdleSoundPolicy.IsPlaybackCurrent(
                callbackGeneration,
                runGeneration,
                activeSlot,
                entity.Alive,
                entityActive,
                entityStillInWorld))
        {
            return;
        }

        initialSoundPending = false;
        entity.World.PlaySoundAt(
            sourceSound,
            entity.Pos.X,
            entity.Pos.InternalY,
            entity.Pos.Z,
            null,
            true,
            soundRange);
        lastSoundTotalMs = entity.World.ElapsedMilliseconds;
    }
}
