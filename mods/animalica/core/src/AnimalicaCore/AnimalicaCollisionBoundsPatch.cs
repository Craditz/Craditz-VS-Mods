using HarmonyLib;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AnimalicaCore;

/// <summary>Clamp PML's finished, scaled collider, never the model definition or render size.</summary>
internal static class AnimalicaCollisionBoundsPatch
{
    private static ConditionalWeakTable<Entity, object> adjusted = new();
    private static readonly Dictionary<EntityBehavior, (EventInfo Event, Action Handler)> deferred = new();

    public static void Start(Harmony harmony, ICoreAPI api)
    {
        var method = AccessTools.Method(AccessTools.TypeByName("PlayerModelLib.PlayerSkinBehavior"), "UpdateEntityProperties");
        if (method == null)
        {
            api.Logger.Error("[AnimalicaCore] Could not install Seraph collision bounds patch.");
            return;
        }
        harmony.Patch(method,
            prefix: new HarmonyMethod(typeof(AnimalicaCollisionBoundsPatch), nameof(BeforeUpdate)),
            postfix: new HarmonyMethod(typeof(AnimalicaCollisionBoundsPatch), nameof(AfterUpdate)));
    }

    public static void Stop()
    {
        foreach (var entry in deferred) entry.Value.Event.RemoveEventHandler(entry.Key, entry.Value.Handler);
        deferred.Clear();
        adjusted = new();
    }

    internal static void RefreshWhenReady(EntityBehavior behavior)
    {
        if (!RequiresRefresh(behavior.entity))
        {
            RemoveDeferred(behavior);
            return;
        }
        Type type = behavior.GetType();
        object? models = AccessTools.Field(type, "ModelSystem")?.GetValue(behavior);
        bool modelsLoaded = models?.GetType().GetProperty("ModelsLoaded")?.GetValue(models) as bool? == true;
        bool initialized = type.GetProperty("Initialized")?.GetValue(behavior) as bool? == true;
        // PML explicitly waits for its definitions before ActuallyInitialize. Its load event
        // can reach Core before a player's initializer, and saved skinModel is not CurrentModelCode yet.
        if (!modelsLoaded || !initialized)
        {
            if (!deferred.ContainsKey(behavior))
            {
                EventInfo ready = type.GetEvent("OnActuallyInitialize")
                    ?? throw new MissingMemberException(type.FullName, "OnActuallyInitialize");
                Action handler = () => RefreshWhenReady(behavior);
                deferred.Add(behavior, (ready, handler));
                ready.AddEventHandler(behavior, handler);
            }
            return;
        }
        // Once PML claims readiness, an invalid table is a genuine error, not a startup race.
        var definitions = models!.GetType().GetProperty("CustomModels")?.GetValue(models) as IDictionary;
        string code = type.GetProperty("CurrentModelCode")?.GetValue(behavior) as string ?? "seraph";
        if (definitions == null || (!definitions.Contains(code) && !definitions.Contains("seraph")))
            throw new InvalidOperationException($"[AnimalicaCore] PML reports initialized models but neither '{code}' nor its Seraph fallback is defined.");
        RemoveDeferred(behavior);
        AccessTools.Method(type, "UpdateEntityProperties").Invoke(behavior, null);
    }

    private static void RemoveDeferred(EntityBehavior behavior)
    {
        if (deferred.Remove(behavior, out var entry)) entry.Event.RemoveEventHandler(behavior, entry.Handler);
    }

    internal static bool RequiresRefresh(Entity entity)
    {
        if (adjusted.TryGetValue(entity, out _)) return true;
        if (entity is not EntityPlayer) return false;
        var core = entity.Api.ModLoader.GetModSystem<AnimalicaCoreSystem>();
        var config = core?.CollisionBoundsConfig;
        return config != null && (config.CapCollisionToSeraph || config.RaiseCollisionToSeraph)
            && core!.IsAnimalicaModelCode(entity.WatchedAttributes.GetString("skinModel", "seraph"));
    }

    private static void BeforeUpdate(EntityBehavior __instance, out float __state)
    {
        Entity entity = __instance.entity;
        float height = entity.Properties.CollisionBoxSize.Y;
        // Preserve the current native posture fraction through model/size recalculation.
        __state = height > 0 && entity.CollisionBox != null && entity.CollisionBox.YSize > 0
            ? Math.Clamp(entity.CollisionBox.YSize / height, 0f, 1f) : 1f;
    }

    private static void AfterUpdate(EntityBehavior __instance, float __state)
    {
        Entity entity = __instance.entity;
        if (entity is not EntityPlayer || !entity.Alive) return;
        var core = entity.Api.ModLoader.GetModSystem<AnimalicaCoreSystem>();
        var config = core?.CollisionBoundsConfig;
        bool enabled = config != null && (config.CapCollisionToSeraph || config.RaiseCollisionToSeraph)
            && core!.IsAnimalicaModelCode(entity.WatchedAttributes.GetString("skinModel", "seraph"));
        if (!enabled && !adjusted.TryGetValue(entity, out _)) return;

        Vec2f natural = entity.Properties.CollisionBoxSize;
        // Registered entity types are engine-owned templates; each live entity has a clone.
        Vec2f? baseline = entity.World.GetEntityType(new AssetLocation("game:player"))?.CollisionBoxSize;
        Vec2f desired = enabled && baseline != null
            ? Clamp(natural, baseline, config!.CapCollisionToSeraph, config.RaiseCollisionToSeraph)
            : natural.Clone();
        entity.Properties.CollisionBoxSize = desired;
        entity.SetCollisionBox(desired.X, desired.Y * __state);

        // PML's ledge/sneak probe must use the same effective dimensions as movement.
        var physics = entity.GetBehavior<EntityBehaviorPlayerPhysics>();
        if (physics != null)
        {
            var probe = new Cuboidf(-desired.X / 2, 0, -desired.X / 2,
                desired.X / 2, desired.Y, desired.X / 2).OmniNotDownGrowBy(-desired.X / 10);
            probe.Y2 /= 2;
            AccessTools.Field(typeof(EntityBehaviorControlledPhysics), "sneakTestCollisionbox")?.SetValue(physics, probe);
        }
        if (enabled) adjusted.GetValue(entity, _ => new object());
        else adjusted.Remove(entity);
    }

    internal static Vec2f Clamp(Vec2f natural, Vec2f baseline, bool cap, bool raise)
    {
        float Dimension(float value, float reference)
        {
            if (!float.IsFinite(value) || value <= 0 || !float.IsFinite(reference) || reference <= 0) return value;
            if (cap) value = Math.Min(value, reference);
            if (raise) value = Math.Max(value, reference);
            return value;
        }
        return new Vec2f(Dimension(natural.X, baseline.X), Dimension(natural.Y, baseline.Y));
    }
}
