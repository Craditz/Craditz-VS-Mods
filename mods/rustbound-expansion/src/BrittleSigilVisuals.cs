using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace RustboundExpansion;

/// <summary>Observes server-synchronized marks; owns no gameplay effect state.</summary>
internal sealed class BrittleSigilVisuals : IDisposable
{
    private const string ActiveKey = "effectactive-" + ExpansionSystem.BrittleId;
    private readonly ICoreClientAPI api;
    private readonly Dictionary<Entity, Action> listeners = new();
    private readonly HashSet<Entity> marked = new();
    private readonly long tickId;

    internal BrittleSigilVisuals(ICoreClientAPI api)
    {
        this.api = api;
        api.Event.OnEntitySpawn += Watch;
        api.Event.OnEntityLoaded += Watch;
        api.Event.OnEntityDespawn += OnDespawn;
        api.Event.LevelFinalize += WatchLoaded;
        api.Event.LeaveWorld += Clear;
        tickId = api.Event.RegisterGameTickListener(Tick, 100);
        WatchLoaded();
    }

    private void WatchLoaded()
    {
        foreach (var entity in api.World.LoadedEntities.Values) Watch(entity);
    }

    private void Watch(Entity entity)
    {
        if (entity is not EntityAgent || listeners.ContainsKey(entity)) return;
        Action changed = () => Refresh(entity);
        listeners.Add(entity, changed);
        entity.WatchedAttributes.RegisterModifiedListener(ActiveKey, changed);
        entity.WatchedAttributes.RegisterModifiedListener(ExpansionSystem.BrittleBonusKey, changed);
        Refresh(entity);
    }

    private static bool IsMarked(Entity entity) => entity.Alive
        && entity.WatchedAttributes.GetBool(ActiveKey)
        && entity.WatchedAttributes.GetFloat(ExpansionSystem.BrittleBonusKey) is var bonus
        && float.IsFinite(bonus) && bonus > 0;

    private void Refresh(Entity entity)
    {
        if (IsMarked(entity)) marked.Add(entity);
        else marked.Remove(entity);
    }

    private void Tick(float dt)
    {
        var viewer = api.World.Player?.Entity;
        if (viewer == null) return;
        double phase = api.World.ElapsedMilliseconds / 1000.0 * 2.5;
        foreach (var entity in marked.ToArray())
        {
            // Recheck even without a notification (e.g. a full-tree reset).
            if (!IsMarked(entity)) { marked.Remove(entity); continue; }
            if (ControlEffects.Banished(entity)) continue;
            if (!api.World.LoadedEntities.TryGetValue(entity.EntityId, out var loaded)
                || !ReferenceEquals(entity, loaded)) { Forget(entity); continue; }
            if (entity.Pos.Dimension != viewer.Pos.Dimension
                || entity.Pos.SquareDistanceTo(viewer.Pos) > 64 * 64) continue;
            foreach (var particle in CreateParticles(entity, phase)) api.World.SpawnParticles(particle);
        }
    }

    // Very short-lived sparks are regenerated from the target's current client
    // position. There is no stationary world emitter left at the cast point.
    internal static IEnumerable<SimpleParticleProperties> CreateParticles(Entity entity, double phase)
    {
        float height = Math.Clamp(entity.SelectionBox?.YSize ?? 1.8f, 0.3f, 8f);
        float radius = Math.Clamp((entity.SelectionBox?.XSize ?? 0.6f) * 0.6f, 0.35f, 2f);
        for (int i = 0; i < 8; i++)
        {
            double angle = phase + i * Math.PI / 4;
            var position = entity.Pos.XYZ.AddCopy(Math.Cos(angle) * radius,
                height * (0.55 + 0.18 * Math.Sin(angle * 2)), Math.Sin(angle) * radius);
            yield return new SimpleParticleProperties(1, 1, ColorUtil.ToRgba(230, 194, 95, 255),
                position, position, new Vec3f(), new Vec3f(), 0.2f, 0, 0.6f, 0.9f, EnumParticleModel.Quad)
            {
                WithTerrainCollision = false,
                LightEmission = ColorUtil.ToRgba(255, 120, 40, 190),
                OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -4f)
            };
        }
    }

    private void OnDespawn(Entity entity, EntityDespawnData data) => Forget(entity);

    private void Forget(Entity entity)
    {
        if (listeners.Remove(entity, out var changed)) entity.WatchedAttributes.UnregisterListener(changed);
        marked.Remove(entity);
    }

    private void Clear()
    {
        foreach (var entity in listeners.Keys.ToArray()) Forget(entity);
    }

    public void Dispose()
    {
        api.Event.UnregisterGameTickListener(tickId);
        api.Event.OnEntitySpawn -= Watch;
        api.Event.OnEntityLoaded -= Watch;
        api.Event.OnEntityDespawn -= OnDespawn;
        api.Event.LevelFinalize -= WatchLoaded;
        api.Event.LeaveWorld -= Clear;
        Clear();
    }
}
