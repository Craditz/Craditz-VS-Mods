using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace AnimalicaShared;

/// <summary>Client player membership, updated only when entities enter or leave.</summary>
internal sealed class LoadedPlayerIndex : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly Dictionary<long, EntityPlayer> players = new();
    public EntityPlayer[] Snapshot { get; private set; } = Array.Empty<EntityPlayer>();

    public LoadedPlayerIndex(ICoreClientAPI api)
    {
        this.api = api;
        api.Event.OnEntitySpawn += Add;
        api.Event.OnEntityLoaded += Add;
        api.Event.OnEntityDespawn += Remove;
        // Include entities loaded before this system initialized.
        foreach (Entity entity in api.World.LoadedEntities.Values) Add(entity);
    }

    private void Add(Entity entity)
    {
        if (entity is not EntityPlayer player ||
            players.TryGetValue(entity.EntityId, out EntityPlayer? existing) && ReferenceEquals(existing, player)) return;
        players[entity.EntityId] = player;
        Snapshot = players.Values.ToArray();
    }

    private void Remove(Entity entity, EntityDespawnData reason)
    {
        // A delayed despawn must not remove a replacement entity with the same ID.
        if (!players.TryGetValue(entity.EntityId, out EntityPlayer? existing) || !ReferenceEquals(existing, entity)) return;
        players.Remove(entity.EntityId);
        Snapshot = players.Values.ToArray();
    }

    public void Dispose()
    {
        api.Event.OnEntitySpawn -= Add;
        api.Event.OnEntityLoaded -= Add;
        api.Event.OnEntityDespawn -= Remove;
        players.Clear();
        Snapshot = Array.Empty<EntityPlayer>();
    }
}
