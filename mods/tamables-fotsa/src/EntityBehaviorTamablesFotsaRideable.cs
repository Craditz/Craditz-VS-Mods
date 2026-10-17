#nullable enable

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace TamablesFotsa;

/// <summary>
/// Keeps vanilla riding while every dedicated pet is already saddle-broken.
/// No saddle-training conversion may replace ownership, equipment or Companion ID.
/// Native FotSA source animals continue to use the unmodified vanilla behavior.
/// </summary>
internal sealed class EntityBehaviorTamablesFotsaRideable(Entity entity) : EntityBehaviorRideable(entity)
{
    public const string BehaviorCode = "tamablesfotsarideable";
    private AiTaskManager? taskManager;

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);
        // Also clear saved unfinished training on semi-tamed fallback forms.
        if (entity.Api.Side == EnumAppSide.Server)
        {
            RemainingSaddleBreaks = 0;
            entity.WatchedAttributes.MarkPathDirty("remainingSaddleBreaksRequired");
        }
    }

    public override void AfterInitialized(bool onFirstSpawn)
    {
        base.AfterInitialized(onFirstSpawn);
        if (entity.Api.Side != EnumAppSide.Server) return;
        taskManager = entity.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager;
        if (taskManager != null) taskManager.OnExecuteTask += AllowTaskExecution;
    }

    private bool AllowTaskExecution(IAiTask task, IAiTask? activeTask) => !AnyMounted();

    public override void DidMount(EntityAgent passenger)
    {
        base.DidMount(passenger);
        if (entity.Api.Side != EnumAppSide.Server) return;
        // The normal rideable scheduler filter prevents new ambient tasks;
        // also stop already-running pet navigation and reject direct command
        // starts, which bypass that scheduler filter.
        taskManager?.StopTasks();
        entity.GetBehavior<EntityBehaviorTaskAI>()?.PathTraverser?.Stop();
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        if (taskManager != null) taskManager.OnExecuteTask -= AllowTaskExecution;
        base.OnEntityDespawn(despawn);
    }

    protected override void ConvertToTamedAnimal()
    {
        // Defensive guard: even an external caller cannot swap this owned pet
        // through the vanilla saddle-breaking replacement path.
        if (entity.Api.Side != EnumAppSide.Server) return;
        RemainingSaddleBreaks = 0;
        entity.WatchedAttributes.MarkPathDirty("remainingSaddleBreaksRequired");
    }
}
