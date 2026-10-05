#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace TamablesCritters;

/// <summary>
/// Starts the dedicated follow task at the same moment PetAI accepts the
/// follow command. The normal task scheduler remains responsible for keeping
/// the task running and restarting it when the pet falls outside its radius.
/// </summary>
internal sealed class EntityBehaviorTamablesCrittersCommandBridge : EntityBehavior
{
    public const string BehaviorCode = "tamablescritterscommandbridge";
    private bool taskSetInstalled;

    public EntityBehaviorTamablesCrittersCommandBridge(Entity entity) : base(entity)
    {
    }

    public override void Initialize(EntityProperties properties, Vintagestory.API.Datastructures.JsonObject attributes)
    {
        base.Initialize(properties, attributes);
        if (entity.Api.Side != EnumAppSide.Server) return;

        InstallTaskSet(properties);
        entity.WatchedAttributes.RegisterModifiedListener("activeCommand", ActivateFollowIfRequested);
    }

    public override void OnEntitySpawn()
    {
        base.OnEntitySpawn();
        ActivateFollowIfRequested();
    }

    public override void OnEntityLoaded()
    {
        base.OnEntityLoaded();
        ActivateFollowIfRequested();
    }

    public override string PropertyName() => BehaviorCode;

    private void ActivateFollowIfRequested()
    {
        if (!string.Equals(
                entity.WatchedAttributes.GetString("activeCommand", string.Empty),
                "followmaster",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        EntityBehaviorTaskAI? taskBehavior = entity.GetBehavior<EntityBehaviorTaskAI>();
        AiTaskTamablesCrittersBasicFollow? followTask = taskBehavior?.TaskManager
            .GetTask<AiTaskTamablesCrittersBasicFollow>();
        if (followTask?.ShouldExecute() == true)
        {
            taskBehavior!.TaskManager.ExecuteTask(followTask, followTask.Slot);
            return;
        }

        AiTaskTamablesCrittersAirFollow? airFollowTask = taskBehavior?.TaskManager
            .GetTask<AiTaskTamablesCrittersAirFollow>();
        if (airFollowTask?.ShouldExecute() == true)
        {
            taskBehavior!.TaskManager.ExecuteTask(airFollowTask, airFollowTask.Slot);
        }
    }

    private void InstallTaskSet(EntityProperties properties)
    {
        if (taskSetInstalled || entity is not EntityAgent agent)
        {
            return;
        }

        EntityBehaviorTaskAI? taskBehavior = entity.GetBehavior<EntityBehaviorTaskAI>();
        JsonObject? taskAiConfig = FindTaskAi(properties.Server?.BehaviorsAsJsonObj);
        JsonObject[]? taskConfigs = taskAiConfig?["aitasks"].AsArray();
        if (taskBehavior == null || taskAiConfig == null || taskConfigs == null)
        {
            return;
        }

        taskBehavior.TaskManager.StopTasks();
        taskBehavior.TaskManager.AllTasks.Clear();

        foreach (JsonObject taskConfig in taskConfigs)
        {
            if (!(taskConfig["enabled"]?.AsBool(true) ?? true)) continue;

            string? taskCode = taskConfig["code"]?.AsString();
            if (string.IsNullOrWhiteSpace(taskCode)
                || !AiTaskRegistry.TaskTypes.TryGetValue(taskCode, out Type? taskType))
            {
                continue;
            }

            if (Activator.CreateInstance(taskType, agent, taskConfig, taskAiConfig) is not IAiTask task)
            {
                continue;
            }

            taskBehavior.TaskManager.AddTask(task);
        }

        taskSetInstalled = true;
    }

    private static JsonObject? FindTaskAi(JsonObject[]? behaviors)
    {
        if (behaviors == null) return null;

        foreach (JsonObject behavior in behaviors)
        {
            if (string.Equals(behavior["code"].AsString(), "taskai", StringComparison.OrdinalIgnoreCase))
            {
                return behavior;
            }
        }

        return null;
    }
}
