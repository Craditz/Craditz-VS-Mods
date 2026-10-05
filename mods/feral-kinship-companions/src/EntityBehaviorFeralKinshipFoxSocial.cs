using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed partial class EntityBehaviorFeralKinshipFoxSocial : EntityBehavior
{
    private static readonly byte[] FoxfireLightHsv = { 34, 5, 8 };
    private static readonly byte[] StrongFoxfireLightHsv = { 34, 5, 16 };
    private float serverTickAccumulator;
    private long movementSpeedSuppressedUntilMs;
    private bool hasAppliedMovementScaling;
    private float lastAppliedMovementMultiplier = 1f;
    private double lastScaledWalkX;
    private double lastScaledWalkY;
    private double lastScaledWalkZ;
    private double lastScaledFlyX;
    private double lastScaledFlyY;
    private double lastScaledFlyZ;
    private bool expeditionSuspended;
    private Cuboidf? savedCollisionBox;
    private Cuboidf? savedOriginCollisionBox;
    private Cuboidf? savedSelectionBox;
    private Cuboidf? savedOriginSelectionBox;
    private EnumEntityState savedServerState = EnumEntityState.Active;
    private byte[]? lightBeforeFoxfire;
    private bool foxfireApplied;
    private long companionTaskReinstallAtMs;
    private long nextAdultTaskRepairAtMs;
    private bool adultCompanionTasksInstalled;
    private bool companionTaskFilterInstalled;
    private bool guideTasksInstalled;
    private bool hasSourceIdleSoundTasks;
    private string[] sourceSoundIdleTaskJson = Array.Empty<string>();
    private readonly CompanionSourceIdleEligibilityGate sourceIdleEligibilityGate = new();

    public EntityBehaviorFeralKinshipFoxSocial(Entity entity) : base(entity)
    {
    }

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);
        EnsureJuvenileSafety();
        // A full snow layer on the upper side of a one-block rise adds 1/8
        // block to the effective step. Keep the companion's physics step
        // slightly above the ordinary one-block threshold so the pathfinder
        // and the movement solver agree about snow-covered ledges.  Keep this
        // below fence height: it is tolerance for a one-block step plus thin
        // cover, not permission to climb through ordinary barriers.
        EntityBehaviorControlledPhysics? physics = entity.GetBehavior<EntityBehaviorControlledPhysics>();
        if (physics != null)
        {
            physics.StepHeight = Math.Max(physics.StepHeight, 1.35f);
        }
        if (entity.Api.Side == EnumAppSide.Server)
        {
            // Keep an immutable source snapshot before adult installation can
            // stop and replace the source task registrations.
            sourceSoundIdleTaskJson = CaptureSourceSoundIdleTasks(properties);

            // The progress marker is presentation state, not a saved job.
            // Clear a marker restored from a previous session; a live logging
            // task will publish it again when it selects its next tree.
            entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()
                .ClearWorkCartLoggingDisplay(entity);

            if (CompanionBreedingCatalog.TryGetForJuvenile(entity, out CompanionBreedingDefinition juvenileDefinition)
                && CompanionBreedingCatalog.TryGetSpeciesProfile(juvenileDefinition.SpeciesId, out CompanionSpeciesProfile juvenileProfile))
            {
                InstallJuvenileCommandTasks(juvenileProfile);
            }
            else if (FeralKinshipCompanionSystem.IsGuideFox(entity))
            {
                InstallGuideTasks();
            }
            else if (FeralKinshipCompanionSystem.TryGetCompanionSpecies(entity, out CompanionSpeciesProfile profile)
                && profile.UsesExternalPetAiTaskSet)
            {
                // Tamables:FOTSA and Tamables:Critters install their own tamed
                // task instances during entity setup. Delay the Companion-
                // owned replacement until that setup has completed, otherwise
                // their source task list can win back the task manager.
                companionTaskReinstallAtMs = entity.World.ElapsedMilliseconds + 500;
            }
            else
            {
                InstallCompanionTasks();
            }
        }
    }

    private void EnsureJuvenileSafety()
    {
        if (entity.Api.Side != EnumAppSide.Server
            || !FeralKinshipCompanionSystem.IsCompanionJuvenile(entity))
        {
            return;
        }

        // Vanilla kits retain the fox entity definition, so they arrive with
        // predator/huntable tags and without the adult tame's pet tag. Tags
        // are runtime data, not saved data; normalize them whenever the kit
        // is created or loaded and sync the result to clients.
        if (entity.Api.EntityTagRegistry.TryCreateTagSet(
                out TagSetFast petTag,
                "pet") == TagRegistryError.None
            && entity.Api.EntityTagRegistry.TryCreateTagSet(
                out TagSetFast unsafeTags,
                "predator",
                "huntable") == TagRegistryError.None)
        {
            TagSetFast normalized = (entity.Tags & ~unsafeTags) | petTag;
            if (normalized != entity.Tags)
            {
                entity.Tags = normalized;
                entity.MarkTagsDirty();
            }
        }

        // The JSON overlay is intentionally still present for normal entity
        // construction, but runtime installation also covers kits produced
        // by alternate breeding/source-mod paths where that patch does not
        // land on the final baby definition.
        if (entity.GetBehavior<EntityBehaviorFeralKinshipDamageGuard>() == null)
        {
            entity.AddBehavior(new EntityBehaviorFeralKinshipDamageGuard(entity));
        }
    }

    private void InstallJuvenileCommandTasks(CompanionSpeciesProfile profile)
    {
        adultCompanionTasksInstalled = false;
        if (entity is not EntityAgent agent)
        {
            return;
        }

        EntityBehaviorTaskAI? taskBehavior = entity.GetBehavior<EntityBehaviorTaskAI>();
        if (taskBehavior == null)
        {
            entity.World.Logger.Warning("[FeralKinshipCompanions] Juvenile {0} has no task AI; source growth remains untouched but companion commands are unavailable.", entity.Code);
            return;
        }

        string walk = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, "walk");
        string run = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, "run");
        string sleep = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, "sleep");
        string idle = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, "idle");

        // Preserve the source juvenile AI. Babies still need their ordinary
        // feeding, play, parent-follow, flee, and idle behavior; Companion
        // command tasks are added alongside it with higher priorities when a
        // command is active.
        taskBehavior.TaskManager.StopTasks();

        JsonObject aiConfig = JsonObject.FromJson($$"""
        {
          "aitasks": [
            {"code":"feralkinshipgetoutofwater","priority":4.2,"priorityForCancel":4.2,"movespeed":0.015,"animation":"{{walk}}","animationSpeed":2.2},
            {"code":"feralkinshipseekfood","emergencyOnly":true,"priority":4.15,"priorityForCancel":4.15,"movespeed":0.035,"animation":"{{walk}}","animationSpeed":2.2},
            {"code":"feralkinshipstarvingrest","priority":4.1,"priorityForCancel":4.1,"movespeed":0.02},
            {"code":"feralkinshipcommandreturnhome","priority":3.7,"priorityForCancel":3.7,"movespeed":0.035,"animation":"{{run}}","animationSpeed":2.0},
            {"code":"feralkinshipcommandrestapproach","priority":3.6,"priorityForCancel":3.6,"movespeed":0.018,"animation":"{{walk}}","animationSpeed":2.0},
            {"code":"feralkinshipcommandrest","priority":3.59,"priorityForCancel":3.59,"animation":"{{sleep}}","animationSpeed":1.0},
            {"code":"feralkinshipreturntoden","priority":2.7,"priorityForCancel":2.7,"movespeed":0.018,"animation":"{{walk}}","animationSpeed":2.0},
            {"code":"feralkinshiprestatden","priority":2.69,"priorityForCancel":2.69,"animation":"{{sleep}}","animationSpeed":1.0},
            {"code":"feralkinshipfollowmaster","priority":3.5,"priorityForCancel":3.5,"movespeed":0.035,"animation":"{{run}}","animationSpeed":2.0},
            {"code":"feralkinshipfollowidle","priority":1.21,"priorityForCancel":1.21,"minduration":4500,"maxduration":6500,"mincooldown":4000,"maxcooldown":9000},
            {"code":"feralkinshipchildfamilycheck","priority":2.85,"priorityForCancel":2.85,"movespeed":0.035,"animation":"{{run}}","animationSpeed":2.0},
            {"code":"feralkinshipmoodidle","priority":1.36,"priorityForCancel":1.36,"minduration":6000,"maxduration":12000,"mincooldown":8000,"maxcooldown":18000},
            {"code":"feralkinshipfetchdroppeditem","routeStorage":true,"priority":1.40,"priorityForCancel":1.40,"animation":"{{walk}}","animationSpeed":2.2},
            {"code":"feralkinshippackidle","priority":1.361,"priorityForCancel":1.361,"animation":"{{walk}}","animationSpeed":2.0},
            {"code":"feralkinshipmowgrass","priority":1.362,"priorityForCancel":1.362,"animation":"{{walk}}","animationSpeed":2.0},
            {"code":"feralkinshipreturntoden","stormMode":false,"priority":1.75,"priorityForCancel":1.75,"movespeed":0.018,"territoryDistance":36,"animation":"{{walk}}","animationSpeed":2.0},
            {"code":"feralkinshipseekfood","emergencyOnly":false,"priority":1.74,"priorityForCancel":1.74,"movespeed":0.018,"animation":"{{walk}}","animationSpeed":2.0},
            {"code":"feralkinshiprestatden","priority":1.76,"priorityForCancel":1.76,"animation":"{{sleep}}"},
            {"code":"feralkinshipreturntoden","dayTerritoryMode":true,"priority":1.37,"priorityForCancel":1.37,"movespeed":0.018,"territoryDistance":36,"animation":"{{walk}}","animationSpeed":2.0},
            {"code":"idle","priority":1.2,"priorityForCancel":1.35,"minduration":2500,"maxduration":7500,"mincooldown":10000,"maxcooldown":50000,"animation":"{{idle}}","animationSpeed":1.25},
            {"code":"wander","priority":1,"priorityForCancel":1.35,"movespeed":0.004,"animation":"{{walk}}"},
            {"code":"lookaround","priority":0.5}
          ]
        }
        """);

        foreach (JsonObject taskConfig in aiConfig["aitasks"].AsArray() ?? Array.Empty<JsonObject>())
        {
            string? taskCode = taskConfig["code"].AsString();
            if (!string.IsNullOrWhiteSpace(taskCode)
                && AiTaskRegistry.TaskTypes.TryGetValue(taskCode, out Type? taskType)
                && Activator.CreateInstance(taskType, agent, taskConfig, aiConfig) is IAiTask task)
            {
                // These tasks supplement the source juvenile task set. The
                // command tasks take over only when their command is active.
                taskBehavior.TaskManager.AddTask(task);
            }
        }
    }

    private void InstallCompanionTasks()
    {
        if (adultCompanionTasksInstalled)
        {
            return;
        }

        if (entity is not EntityAgent agent
            || !FeralKinshipCompanionSystem.TryGetCompanionSpecies(entity, out CompanionSpeciesProfile profile))
        {
            entity.World.Logger.Warning(
                "[FeralKinshipCompanions] Skipped companion AI installation for unsupported entity {0}.",
                entity.Code
            );
            return;
        }

        EntityBehaviorTaskAI? taskBehavior = entity.GetBehavior<EntityBehaviorTaskAI>();
        if (taskBehavior == null)
        {
            entity.World.Logger.Error(
                "[FeralKinshipCompanions] Supported companion {0} has no taskai behavior.",
                entity.Code
            );
            return;
        }

        JsonObject aiConfig = BuildCompanionAiConfig(entity, profile);
        if (GetSystem().SourceAnimalIdleVocalizationsEnabled)
        {
            AppendSourceSoundIdleTasks(aiConfig, sourceSoundIdleTaskJson);
        }
        JsonObject[]? taskConfigs = aiConfig["aitasks"].AsArray();
        if (taskConfigs == null)
        {
            return;
        }

        // Stop running instances before replacing their registrations. Clearing
        // AllTasks alone leaves the manager's running slots holding old task
        // objects, so a command can remain blocked by an orphaned task.
        taskBehavior.TaskManager.StopTasks();
        agent.Controls.StopAllMovement();
        agent.Pos.Motion.Set(0, 0, 0);
        taskBehavior.TaskManager.AllTasks.Clear();
        if (!companionTaskFilterInstalled)
        {
            taskBehavior.TaskManager.OnShouldExecuteTask += task =>
                FeralKinshipCompanionSystem.IsWhistleHoldActive(agent)
                    ? false
                    : FeralKinshipCompanionSystem.IsExpeditionDeparturePending(agent)
                        ? task is AiTaskFeralKinshipExpeditionDepart
                        : !FeralKinshipCompanionSystem.IsAmbientCompanionTask(task)
                            || IsAmbientCompanionTaskAllowed(agent, task);
            companionTaskFilterInstalled = true;
        }
        foreach (JsonObject taskConfig in taskConfigs)
        {
            string? taskCode = taskConfig["code"].AsString();
            if (string.IsNullOrWhiteSpace(taskCode)
                || !AiTaskRegistry.TaskTypes.TryGetValue(taskCode, out Type? taskType))
            {
                entity.World.Logger.Error(
                    "[FeralKinshipCompanions] Companion AI task '{0}' is not registered for {1}.",
                    taskCode ?? "(missing)",
                    entity.Code
                );
                continue;
            }

            IAiTask? task = (IAiTask?)Activator.CreateInstance(taskType, agent, taskConfig, aiConfig);
            if (task != null)
            {
                taskBehavior.TaskManager.AddTask(task);
            }
        }

        hasSourceIdleSoundTasks = taskBehavior.TaskManager.AllTasks
            .Any(task => task is AiTaskFeralKinshipSourceIdleSound);
        if (GetSystem().SourceAnimalIdleVocalizationsEnabled && hasSourceIdleSoundTasks)
        {
            UpdateSourceIdleSoundEligibility(agent);
        }

        adultCompanionTasksInstalled = true;
    }

    private static string[] CaptureSourceSoundIdleTasks(EntityProperties properties)
    {
        List<(float Priority, int Order, string Json)> captured = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        JsonObject[] behaviors = properties.Server?.BehaviorsAsJsonObj ?? Array.Empty<JsonObject>();
        int order = 0;

        foreach (JsonObject behavior in behaviors)
        {
            if (!string.Equals(behavior["code"].AsString(), "taskai", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (JsonObject task in behavior["aitasks"].AsArray() ?? Array.Empty<JsonObject>())
            {
                string? sound = task["sound"].AsString();
                if (!string.Equals(task["code"].AsString(), "idle", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(sound)
                    || task["priority"].AsFloat() < 0
                    || task.Token is not JObject taskObject)
                {
                    continue;
                }

                string json = taskObject.ToString(Newtonsoft.Json.Formatting.None);
                if (seen.Add(json))
                {
                    captured.Add((task["priority"].AsFloat(), order++, json));
                }
            }
        }

        return captured
            .OrderByDescending(task => task.Priority)
            .ThenBy(task => task.Order)
            .Select(task => task.Json)
            .ToArray();
    }

    private static void AppendSourceSoundIdleTasks(JsonObject aiConfig, IEnumerable<string> sourceTaskJson)
    {
        if (aiConfig["aitasks"].Token is not JArray taskArray)
        {
            return;
        }

        foreach (string sourceJson in sourceTaskJson)
        {
            JObject soundTask = JObject.Parse(sourceJson);
            // Keep source idle timing, conditions, priorities, and sound metadata,
            // but do not restore the source animation or give this task movement.
            soundTask.Remove("animation");
            soundTask.Remove("animationSpeed");
            string? sourceSound = soundTask["sound"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(sourceSound)) continue;
            soundTask.Remove("sound");
            soundTask["sourceSound"] = sourceSound;
            soundTask["code"] = "feralkinshipsourceidle";
            soundTask["slot"] = CompanionSourceIdleSoundPolicy.Slot;
            float effectivePriority = CompanionSourceIdleSoundPolicy.EffectivePriority(
                soundTask.Value<float?>("priority"));
            soundTask["priority"] = effectivePriority;
            soundTask["priorityForCancel"] = CompanionSourceIdleSoundPolicy.MaximumPriority;
            taskArray.Add(soundTask);
        }
        JToken[] sourceTasks = taskArray.Children<JObject>()
            .Where(task => string.Equals((string?)task["code"], "feralkinshipsourceidle", StringComparison.Ordinal))
            .ToArray();
        if (sourceTasks.Length == 0) return;
        float lowestPriority = sourceTasks.Min(task => (float?)task["priority"] ?? CompanionSourceIdleSoundPolicy.MaximumPriority);
        JObject? fallbackIdle = taskArray.Children<JObject>()
            .FirstOrDefault(task => string.Equals((string?)task["code"], "idle", StringComparison.Ordinal));
        if (fallbackIdle != null)
        {
            fallbackIdle["priorityForCancel"] = CompanionSourceIdleSoundPolicy.FallbackPriorityForCancel(lowestPriority);
        }
    }

    private void InstallGuideTasks()
    {
        if (guideTasksInstalled || entity is not EntityAgent agent)
        {
            return;
        }

        EntityBehaviorTaskAI? taskBehavior = entity.GetBehavior<EntityBehaviorTaskAI>();
        if (taskBehavior == null)
        {
            entity.World.Logger.Warning(
                "[FeralKinshipCompanions] Guide fox {0} has no taskai behavior; it will remain stationary.",
                entity.Code
            );
            guideTasksInstalled = true;
            return;
        }

        string run = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, "run");
        JsonObject aiConfig = JsonObject.FromJson($$"""
        {
          "aitasks": [
            {"code":"feralkinshipfollowmaster","priority":3.5,"priorityForCancel":3.5,"movespeed":0.075,"animation":"{{run}}","animationSpeed":2.2},
            {"code":"feralkinshipbramblehome","priority":3.4,"priorityForCancel":3.4,"movespeed":0.035}
          ]
        }
        """);

        taskBehavior.TaskManager.StopTasks();
        taskBehavior.TaskManager.AllTasks.Clear();
        foreach (JsonObject taskConfig in aiConfig["aitasks"].AsArray() ?? Array.Empty<JsonObject>())
        {
            string? taskCode = taskConfig["code"].AsString();
            if (!string.IsNullOrWhiteSpace(taskCode)
                && AiTaskRegistry.TaskTypes.TryGetValue(taskCode, out Type? taskType)
                && Activator.CreateInstance(taskType, agent, taskConfig, aiConfig) is IAiTask task)
            {
                taskBehavior.TaskManager.AddTask(task);
            }
        }

        guideTasksInstalled = true;
    }

    /// <summary>
    /// Rebuilds the adult task set after vanilla growth has replaced one of
    /// our tagged juveniles. The replacement entity can be created before
    /// its final watched attributes are copied, so relying only on Initialize
    /// can leave the juvenile task set in place.
    /// </summary>
    internal void EnsureAdultCompanionTasks()
    {
        if (entity.Api.Side == EnumAppSide.Server
            && FeralKinshipCompanionSystem.IsTamedFox(entity)
            && !FeralKinshipCompanionSystem.IsCompanionJuvenile(entity))
        {
            EntityBehaviorTaskAI? taskBehavior = entity.GetBehavior<EntityBehaviorTaskAI>();
            bool hasAdultTaskSet = taskBehavior?.TaskManager.AllTasks
                .Any(task => task is AiTaskFeralKinshipSeekFood) == true;
            if (!hasAdultTaskSet)
            {
                adultCompanionTasksInstalled = false;
                InstallCompanionTasks();
            }
        }
    }

    private static JsonObject BuildCompanionAiConfig(Entity entity, CompanionSpeciesProfile profile)
    {
        string walk = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, "walk");
        string run = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, "run");
        string sleep = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, "sleep");
        string attack = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, "attack");
        string idle = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, "idle");
        string damage = FeralKinshipCompanionSystem.GetCompanionBaseMeleeDamage(entity)
            .ToString(CultureInfo.InvariantCulture);
        string catHeldFoodTask = profile.Id == "cat"
            ? "            {\"code\":\"feralkinshipcatuseinventory\",\"priority\":4.16,\"priorityForCancel\":4.16,\"animation\":\"eat\",\"animationSpeed\":0.75,\"eatItemCategories\":[\"Protein\"],\"eatItemCodes\":[\"bushmeat-raw\",\"redmeat-raw\",\"poultry-raw\",\"fish-raw\",\"feralkinshipcompanions:kibble-meat-raw\",\"feralkinshipcompanions:kibble-meat-dry\",\"petai:petcookie-meat-perfect\"],\"useTime\":1.5,\"mincooldownHours\":1,\"maxcooldownHours\":1.5},\r\n"
            : string.Empty;

        // AiTaskManager evaluates tasks in insertion order. Keep each slot's
        // entries in descending priority so a lower task cannot start an
        // asynchronous route and then be cancelled by a higher task later in
        // the same StartNewTasks pass.
        return JsonObject.FromJson($$"""
        {
          "aitasks": [
            {"code":"feralkinshipbackpackdelivery","priority":5.0,"priorityForCancel":5.0,"movespeed":0.045,"animation":"{{run}}","animationSpeed":2.2},
            {"code":"feralkinshipclearsnow","emergencyOnly":true,"priority":4.60,"priorityForCancel":4.60,"animation":"{{walk}}","animationSpeed":2.2},
            {"code":"feralkinshipexpeditiondepart","priority":4.5,"priorityForCancel":4.5,"movespeed":0.045,"animation":"{{walk}}","animationSpeed":2.2},
            {"code":"feralkinshipgetoutofwater","priority":4.2,"priorityForCancel":4.2,"movespeed":0.015,"animation":"{{walk}}","animationSpeed":2.2},
        {{catHeldFoodTask}}
            {"code":"feralkinshipseekfood","emergencyOnly":true,"priority":4.15,"priorityForCancel":4.15,"movespeed":0.035,"animation":"{{walk}}","animationSpeed":2.2},
            {"code":"feralkinshipstarvingrest","priority":4.1,"priorityForCancel":4.1,"movespeed":0.02},
            {"code":"feralkinshippetmeleeattack","priority":4.05,"priorityForCancel":4.05,"slot":1,"minDist":1.0,"minVerDist":0.9,"attackAngleRangeDeg":35,"attackDurationMs":1000,"damagePlayerAtMs":500,"animation":"{{attack}}","animationSpeed":2.5,"mincooldown":900,"maxcooldown":1200,"damage":{{damage}},"damageType":"SlashingAttack","damageTier":1},
            {"code":"feralkinshipcombatapproach","priority":4.0,"priorityForCancel":4.0,"movespeed":0.045,"animation":"{{run}}","animationSpeed":2.2},
            {"code":"feralkinshipcommandreturnhome","priority":3.7,"priorityForCancel":3.7,"movespeed":0.045,"animation":"{{run}}","animationSpeed":2.2},
            {"code":"feralkinshipcommandrestapproach","priority":3.6,"priorityForCancel":3.6,"movespeed":0.02,"animation":"{{walk}}","animationSpeed":2.2},
            {"code":"feralkinshipcommandrest","priority":3.59,"priorityForCancel":3.59,"animation":"{{sleep}}","animationSpeed":1.0},
            {"code":"feralkinshipfollowmaster","priority":3.5,"priorityForCancel":3.5,"movespeed":0.045,"animation":"{{run}}","animationSpeed":2.2},
            {"code":"feralkinshipfollowidle","priority":1.21,"priorityForCancel":1.21,"minduration":4500,"maxduration":6500,"mincooldown":4000,"maxcooldown":9000},
            {"code":"feralkinshipfetchdroppeditem","commandedCourier":true,"priority":3.4,"priorityForCancel":3.4,"animation":"{{walk}}","animationSpeed":2.2},
            {"code":"feralkinshipreturntoden","stormMode":true,"priority":2.55,"priorityForCancel":2.55,"movespeed":0.045,"territoryDistance":36,"animation":"{{run}}","animationSpeed":2.2},
            {"code":"feralkinshipblueberrymode","priority":1.80,"priorityForCancel":1.80,"animation":"{{walk}}","animationSpeed":2.2},
            {"code":"feralkinshiprestatden","priority":1.76,"priorityForCancel":1.76,"animation":"{{sleep}}"},
            {"code":"feralkinshipreturntoden","stormMode":false,"priority":1.75,"priorityForCancel":1.75,"movespeed":0.02,"territoryDistance":36,"animation":"{{walk}}","animationSpeed":2.2},
            {"code":"feralkinshipseekfood","emergencyOnly":false,"priority":1.74,"priorityForCancel":1.74,"movespeed":0.02,"animation":"{{walk}}","animationSpeed":2.2},
            {"code":"feralkinshipfetchdroppeditem","shovelCharcoal":true,"priority":1.39,"priorityForCancel":1.39,"animation":"{{walk}}","animationSpeed":2.2},
            {"code":"feralkinshipreturntoden","dayTerritoryMode":true,"priority":1.37,"priorityForCancel":1.37,"movespeed":0.02,"territoryDistance":36,"animation":"{{walk}}","animationSpeed":2.2},
            {"code":"feralkinshipfetchdroppeditem","dutyCoordinator":true,"priority":1.3655,"priorityForCancel":1.3655,"animation":"{{walk}}","animationSpeed":2.2},
            {"code":"feralkinshiplogging","priority":1.3645,"priorityForCancel":1.3645,"animation":"{{walk}}","animationSpeed":2.2},
            {"code":"feralkinshipmowgrass","priority":1.362,"priorityForCancel":1.362,"animation":"{{walk}}","animationSpeed":2.2},
            {"code":"feralkinshippackidle","priority":1.361,"priorityForCancel":1.361,"animation":"{{walk}}","animationSpeed":2.0},
            {"code":"feralkinshipmoodidle","priority":1.36,"priorityForCancel":1.36,"minduration":6000,"maxduration":12000,"mincooldown":8000,"maxcooldown":18000},
            {"code":"idle","priority":1.2,"priorityForCancel":1.35,"minduration":2500,"maxduration":7500,"mincooldown":10000,"maxcooldown":50000,"animation":"{{idle}}","animationSpeed":1.25},
            {"code":"wander","priority":1,"priorityForCancel":1.35,"movespeed":0.004,"animation":"{{walk}}"},
            {"code":"lookaround","priority":0.5}
          ]
        }
        """);
    }

    public override void OnInteract(
        EntityAgent byEntity,
        ItemSlot itemslot,
        Vec3d hitPosition,
        EnumInteractMode mode,
        ref EnumHandling handled)
    {
        base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);

        if (entity.Api.Side != EnumAppSide.Client
            || mode != EnumInteractMode.Interact
            || byEntity is not EntityPlayer player)
        {
            return;
        }

        if (FeralKinshipCompanionSystem.IsGuideFox(entity))
        {
            handled = EnumHandling.PreventSubsequent;
            if ((itemslot == null || itemslot.Empty) && !player.Controls.Sneak)
            {
                GetSystem().TryOpenGuideGui(entity);
            }
            return;
        }

        if (player.Controls.Sneak
            && FeralKinshipCompanionSystem.IsTamedFox(entity)
            && !FeralKinshipCompanionSystem.IsCompanionJuvenile(entity))
        {
            handled = EnumHandling.PreventSubsequent;
            GetSystem().RequestOpenCompanionBackpack(entity.EntityId);
            return;
        }

        if (itemslot != null && !itemslot.Empty)
        {
            return;
        }

        if (!entity.Alive)
        {
            // Dead records remain in the pack archive, but the entity should
            // no longer behave like a live fox target in the world.
            handled = EnumHandling.PreventSubsequent;
            return;
        }

        if (FeralKinshipCompanionSystem.IsFoxIncapacitated(entity))
        {
            // A downed fox can still be treated with a held healing item, but
            // ordinary empty-hand interaction must not open its social menu.
            handled = EnumHandling.PreventSubsequent;
            return;
        }

        FeralKinshipCompanionSystem? system = GetSystem();
        if (system.TryOpenFoxSocialGui(entity))
        {
            // Kinship owns the complete companion screen and command model.
            handled = EnumHandling.PreventSubsequent;
        }
    }

    public override void AfterInitialized(bool onFirstSpawn)
    {
        base.AfterInitialized(onFirstSpawn);
        InitializeCompanionBackpack();
        RefreshExpeditionSuspension();
        if (entity.Api.Side == EnumAppSide.Server)
        {
            GetSystem().RegisterLoadedFox(entity);
        }
    }

    private void ApplyMovementSpeedToCurrentVector()
    {
        if (entity is not EntityAgent agent
            || !FeralKinshipCompanionSystem.IsTamedFox(entity))
        {
            return;
        }

        RestorePreviousMovementScaling(agent);

        long now = entity.World.ElapsedMilliseconds;
        if (agent.Swimming || agent.FeetInLiquid)
        {
            movementSpeedSuppressedUntilMs = now + 2000;
        }

        float movementMultiplier = now < movementSpeedSuppressedUntilMs
            ? 1f
            : FeralKinshipCompanionSystem.GetFoxMovementSpeedMultiplier(entity);
        if (Math.Abs(movementMultiplier - 1f) >= 0.0001f)
        {
            // Scale the final server movement vector without depending on
            // which Kinship path task is currently active. Restore the
            // previous application first because these mutable controls can
            // persist when a task does not rewrite them every tick.
            agent.Controls.WalkVector.Mul(movementMultiplier);
            agent.Controls.FlyVector.Mul(movementMultiplier);

            hasAppliedMovementScaling = true;
            lastAppliedMovementMultiplier = movementMultiplier;
            lastScaledWalkX = agent.Controls.WalkVector.X;
            lastScaledWalkY = agent.Controls.WalkVector.Y;
            lastScaledWalkZ = agent.Controls.WalkVector.Z;
            lastScaledFlyX = agent.Controls.FlyVector.X;
            lastScaledFlyY = agent.Controls.FlyVector.Y;
            lastScaledFlyZ = agent.Controls.FlyVector.Z;
        }
    }

    private void RestorePreviousMovementScaling(EntityAgent agent)
    {
        if (!hasAppliedMovementScaling)
        {
            return;
        }

        if (VectorsMatch(agent.Controls.WalkVector, lastScaledWalkX, lastScaledWalkY, lastScaledWalkZ))
        {
            agent.Controls.WalkVector.Mul(1d / lastAppliedMovementMultiplier);
        }

        if (VectorsMatch(agent.Controls.FlyVector, lastScaledFlyX, lastScaledFlyY, lastScaledFlyZ))
        {
            agent.Controls.FlyVector.Mul(1d / lastAppliedMovementMultiplier);
        }

        hasAppliedMovementScaling = false;
        lastAppliedMovementMultiplier = 1f;
    }

    private static bool VectorsMatch(Vec3d vector, double x, double y, double z)
    {
        const double tolerance = 0.000000001d;
        return Math.Abs(vector.X - x) <= tolerance
            && Math.Abs(vector.Y - y) <= tolerance
            && Math.Abs(vector.Z - z) <= tolerance;
    }

    public override void OnEntitySpawn()
    {
        base.OnEntitySpawn();
        InitializeCompanionBackpack();
        RefreshExpeditionSuspension();
        if (entity.Api.Side == EnumAppSide.Server)
        {
            GetSystem().RegisterLoadedFox(entity);
        }
    }

    public override void OnEntityLoaded()
    {
        base.OnEntityLoaded();
        InitializeCompanionBackpack();
        RefreshExpeditionSuspension();
        if (entity.Api.Side == EnumAppSide.Server)
        {
            GetSystem().RegisterLoadedFox(entity);
            BlockPos cell = entity.Pos.AsBlockPos;
            GetSystem().LogCompanionPhysicsDiagnostic(
                entity,
                "entity-loaded",
                $"pos={entity.Pos.X:0.00},{entity.Pos.Y:0.00},{entity.Pos.Z:0.00} "
                + $"chunk-loaded={entity.World is IServerWorldAccessor serverWorld && serverWorld.IsFullyLoadedChunk(cell)} "
                + $"chunk={cell.X >> 4},{cell.Z >> 4}");
        }
    }

    public override void OnGameTick(float deltaTime)
    {
        base.OnGameTick(deltaTime);
        RefreshExpeditionSuspension();
        ApplyFoxfireLight();
        if (entity.Api.Side != EnumAppSide.Server)
        {
            return;
        }

        if (FeralKinshipCompanionSystem.IsGuideFox(entity))
        {
            InstallGuideTasks();
            if (entity is EntityAgent guideAgent
                && FeralKinshipCompanionSystem.GetCompanionActivityMode(entity)
                    != CompanionActivityMode.Follow
                && !GetSystem().IsBrambleHomeModeActive(entity))
            {
                guideAgent.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.StopTasks();
                guideAgent.Controls.StopAllMovement();
                entity.Pos.Motion.Set(0, 0, 0);
            }

            serverTickAccumulator += deltaTime;
            if (serverTickAccumulator >= 1f)
            {
                float guideElapsed = serverTickAccumulator;
                serverTickAccumulator = 0f;
                GetSystem().UpdateGuideFox(entity, guideElapsed);
            }
            return;
        }

        if (companionTaskReinstallAtMs > 0
            && entity.World.ElapsedMilliseconds >= companionTaskReinstallAtMs)
        {
            companionTaskReinstallAtMs = 0;
            if (FeralKinshipCompanionSystem.TryGetCompanionSpecies(entity, out CompanionSpeciesProfile profile)
                && profile.UsesExternalPetAiTaskSet)
            {
                InstallCompanionTasks();
            }
        }

        if (!FeralKinshipCompanionSystem.IsTamedFox(entity))
        {
            return;
        }

        // Growth can replace the entity and finish before its behavior/task
        // manager has completed initialization. Check the actual task list,
        // not only the in-memory installation flag, so an adult cannot remain
        // stuck with the juvenile command set or an empty AI list. This also
        // repairs adults that were already grown by an earlier build.
        long now = entity.World.ElapsedMilliseconds;
        if (now >= nextAdultTaskRepairAtMs)
        {
            nextAdultTaskRepairAtMs = now + 1000;
            EnsureAdultCompanionTasks();
        }

        ApplyMovementSpeedToCurrentVector();
        if (entity is EntityAgent agent)
        {
            if (hasSourceIdleSoundTasks && GetSystem().SourceAnimalIdleVocalizationsEnabled)
            {
                UpdateSourceIdleSoundEligibility(agent);
            }
            if (FeralKinshipCompanionSystem.IsWhistleHoldActive(agent))
            {
                agent.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.StopTasks();
                agent.Controls.StopAllMovement();
                agent.Pos.Motion.Set(0, 0, 0);
            }
            else
            {
                FeralKinshipCompanionSystem.StopAmbientCompanionAi(agent);
            }
        }

        serverTickAccumulator += deltaTime;
        if (serverTickAccumulator < 1f)
        {
            return;
        }

        float elapsed = serverTickAccumulator;
        serverTickAccumulator = 0f;
        GetSystem().UpdateFox(entity, elapsed);
    }

    private void UpdateSourceIdleSoundEligibility(EntityAgent agent)
    {
        bool blocked = IsSourceIdleSoundAmbientBlocked(agent);
        sourceIdleEligibilityGate.Update(
            blocked,
            entity.World.ElapsedMilliseconds,
            () => entity.World.Rand.Next(0, 12001));
    }

    private bool IsAmbientCompanionTaskAllowed(EntityAgent agent, IAiTask task)
    {
        bool taskAllowed = !FeralKinshipCompanionSystem.ShouldPauseAmbientCompanionAi(agent, task);
        if (task is not AiTaskFeralKinshipSourceIdleSound
            || !hasSourceIdleSoundTasks
            || !GetSystem().SourceAnimalIdleVocalizationsEnabled)
        {
            return taskAllowed;
        }

        long nowMs = entity.World.ElapsedMilliseconds;
        return sourceIdleEligibilityGate.CanStart(
            taskAllowed,
            IsSourceIdleSoundAmbientBlocked(agent),
            nowMs,
            () => entity.World.Rand.Next(0, 12001));
    }

    private static bool IsSourceIdleSoundAmbientBlocked(EntityAgent agent)
        => FeralKinshipCompanionSystem.IsWhistleHoldActive(agent)
            || FeralKinshipCompanionSystem.IsExpeditionDeparturePending(agent)
            || FeralKinshipCompanionSystem.ShouldPauseAmbientCompanionAi(agent);


    private void ApplyFoxfireLight()
    {
        int foxfireRank = FeralKinshipCompanionSystem.IsTamedFox(entity)
            ? FeralKinshipCompanionSystem.GetFoxPerkRank(entity, "lantern-fox")
            : 0;
        bool shouldGlow = !expeditionSuspended && foxfireRank > 0;
        if (shouldGlow)
        {
            if (!foxfireApplied) lightBeforeFoxfire = entity.LightHsv;
            entity.LightHsv = foxfireRank >= 2 ? StrongFoxfireLightHsv : FoxfireLightHsv;
            foxfireApplied = true;
        }
        else if (foxfireApplied)
        {
            ClearFoxfireLight();
        }
    }

    private void ClearFoxfireLight()
    {
        if (!foxfireApplied)
        {
            return;
        }

        entity.LightHsv = lightBeforeFoxfire;
        lightBeforeFoxfire = null;
        foxfireApplied = false;
    }

    internal void RefreshExpeditionSuspension()
    {
        bool shouldSuspend = FeralKinshipCompanionSystem.IsFoxAwayFromWorld(entity);
        if (shouldSuspend == expeditionSuspended)
        {
            return;
        }

        if (shouldSuspend)
        {
            ClearFoxfireLight();
            savedCollisionBox = entity.CollisionBox?.Clone();
            savedOriginCollisionBox = entity.OriginCollisionBox?.Clone();
            savedSelectionBox = entity.SelectionBox?.Clone();
            savedOriginSelectionBox = entity.OriginSelectionBox?.Clone();
            entity.SetCollisionBox(0f, 0f);
            entity.SetSelectionBox(0f, 0f);
            if (entity.Api.Side == EnumAppSide.Server)
            {
                savedServerState = entity.State;
                entity.State = EnumEntityState.Inactive;
                if (entity is EntityAgent agent)
                {
                    agent.Controls.StopAllMovement();
                    agent.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.StopTasks();
                    // Clear velocity as well as controls. A third-party AI may
                    // have written motion before the expedition suspension was
                    // observed; leaving that velocity alive can feed invalid
                    // values into vanilla controlled physics on the next tick.
                    entity.Pos.Motion.Set(0, 0, 0);
                }
            }
        }
        else
        {
            if (savedCollisionBox != null) entity.CollisionBox = savedCollisionBox;
            if (savedOriginCollisionBox != null) entity.OriginCollisionBox = savedOriginCollisionBox;
            if (savedSelectionBox != null) entity.SelectionBox = savedSelectionBox;
            if (savedOriginSelectionBox != null) entity.OriginSelectionBox = savedOriginSelectionBox;
            if (entity.Api.Side == EnumAppSide.Server)
            {
                entity.State = savedServerState is EnumEntityState.Despawned
                    ? EnumEntityState.Active
                    : savedServerState;
            }
        }

        expeditionSuspended = shouldSuspend;
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        DisposeCompanionBackpack();
        base.OnEntityDespawn(despawn);
        if (entity.Api.Side == EnumAppSide.Server)
        {
            if (FeralKinshipCompanionSystem.IsGuideFox(entity))
            {
                GetSystem().UnregisterLoadedBramble(entity);
            }
            else
            {
                BlockPos cell = entity.Pos.AsBlockPos;
                GetSystem().LogCompanionPhysicsDiagnostic(
                    entity,
                    "entity-unloaded",
                    $"alive={entity.Alive} pos={entity.Pos.X:0.00},{entity.Pos.Y:0.00},{entity.Pos.Z:0.00} "
                    + $"chunk-loaded={entity.World is IServerWorldAccessor serverWorld && serverWorld.IsFullyLoadedChunk(cell)} "
                    + $"chunk={cell.X >> 4},{cell.Z >> 4}");
                GetSystem().UnregisterLoadedFox(
                    entity,
                    entity.Alive ? "Not currently loaded" : "Dead"
                );
            }
        }
    }

    public override void OnEntityDeath(DamageSource damageSourceForDeath)
    {
        base.OnEntityDeath(damageSourceForDeath);
        if (entity.Api.Side == EnumAppSide.Server)
        {
            if (!FeralKinshipCompanionSystem.IsGuideFox(entity))
            {
                GetSystem().OnFoxDeath(entity);
            }
        }
    }

    private FeralKinshipCompanionSystem GetSystem()
    {
        return entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
    }

    public override string PropertyName()
    {
        return FeralKinshipCompanionSystem.SocialBehaviorCode;
    }
}
