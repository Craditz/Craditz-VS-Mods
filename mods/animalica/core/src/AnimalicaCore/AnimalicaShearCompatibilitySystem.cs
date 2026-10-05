using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Client;
using Vintagestory.API.Server;

namespace AnimalicaCore;

/// <summary>
/// Adds the released ShearLib behavior to eligible Animalica player entities when
/// ShearLib is installed. The behavior itself remains owned by ShearLib.
/// </summary>
internal sealed class AnimalicaShearCompatibilitySystem : ModSystem
{
    private const string BehaviorCode = "shearable";
    private const string SkinModelAttribute = "skinModel";

    private static readonly IReadOnlyDictionary<string, (double Hours, int Min, int Max, int Bonus, double Scratch, string Drop, string? DropSecond, double DropSecondPercentage)> Configs =
        new Dictionary<string, (double, int, int, int, double, string, string?, double)>(StringComparer.Ordinal)
        {
            ["goat-angora"] = (26, 6, 12, 4, 0.35, "wool:fleece-angora-white", null, 0),
            ["goat-ibexalp"] = (42, 4, 8, 2, 0.35, "wool:fleece-generic-lightbrown", "wool:fleece-generic-brown", 0.30),
            ["goat-ibexnub"] = (42, 3, 5, 2, 0.35, "wool:fleece-generic-lightbrown", "wool:fleece-generic-brown", 0.20),
            ["goat-markhor"] = (28, 4, 8, 2, 0.35, "wool:fleece-generic-lightbrown", null, 0),
            ["goat-mountain"] = (24, 7, 14, 4, 0.35, "wool:fleece-generic-plain", null, 0),
            ["goat-muskox"] = (28, 10, 20, 4, 0.35, "wool:fleece-muskox-brown", null, 0),
            ["goat-nubian"] = (48, 3, 5, 2, 0.35, "wool:fleece-generic-lightbrown", "wool:fleece-generic-plain", 0.10),
            ["goat-sirohi"] = (52, 3, 4, 2, 0.35, "wool:fleece-generic-redbrown", "wool:fleece-generic-yellow", 0.30),
            ["goat-takingold"] = (26, 8, 15, 4, 0.35, "wool:fleece-generic-yellow", null, 0),
            ["goat-valais"] = (42, 3, 6, 2, 0.35, "wool:fleece-generic-black", "wool:fleece-generic-white", 0.45),
            ["sheep-bighorn"] = (32, 6, 7, 4, 0.65, "wool:fleece-generic-gray", "wool:fleece-generic-plain", 0.15),
            ["sheep-mouflon"] = (52, 2, 3, 2, 0.65, "wool:fleece-generic-lightbrown", "wool:fleece-generic-redbrown", 0.30)
        };

    private ICoreServerAPI? serverApi;
    private ICoreClientAPI? clientApi;
    private readonly HashSet<long> listenerEntityIds = new();
    private readonly HashSet<long> cleaningEntityIds = new();
    private readonly Dictionary<long, EntityBehavior> ownedBehaviors = new();
    private readonly Dictionary<long, EntityPlayer> ownedPlayers = new();
    private readonly Dictionary<long, string> ownedModels = new();
    private bool disposed;

    public override void StartServerSide(ICoreServerAPI api)
    {
        disposed = false;
        serverApi = api;
        api.Event.OnEntitySpawn += AttachToPlayer;
        api.Event.OnEntityLoaded += AttachToPlayer;
        api.Event.OnEntityDespawn += CleanupPlayer;
        api.Event.PlayerNowPlaying += AttachToServerPlayer;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        disposed = false;
        clientApi = api;
        api.Event.OnEntitySpawn += AttachToPlayer;
        api.Event.OnEntityLoaded += AttachToPlayer;
        api.Event.OnEntityDespawn += CleanupPlayer;
    }

    public override void Dispose()
    {
        disposed = true;
        if (serverApi != null)
        {
            serverApi.Event.OnEntitySpawn -= AttachToPlayer;
            serverApi.Event.OnEntityLoaded -= AttachToPlayer;
            serverApi.Event.OnEntityDespawn -= CleanupPlayer;
            serverApi.Event.PlayerNowPlaying -= AttachToServerPlayer;
        }
        if (clientApi != null)
        {
            clientApi.Event.OnEntitySpawn -= AttachToPlayer;
            clientApi.Event.OnEntityLoaded -= AttachToPlayer;
            clientApi.Event.OnEntityDespawn -= CleanupPlayer;
        }
        foreach ((long entityId, EntityBehavior behavior) in ownedBehaviors.ToArray())
        {
            if (ownedPlayers.TryGetValue(entityId, out EntityPlayer? player))
            {
                RemoveOwnedBehavior(player, behavior);
            }
        }

        serverApi = null;
        clientApi = null;
        listenerEntityIds.Clear();
        cleaningEntityIds.Clear();
        ownedBehaviors.Clear();
        ownedPlayers.Clear();
        ownedModels.Clear();
    }

    private void AttachToPlayer(Entity entity)
    {
        if (entity is not EntityPlayer player || entity.Api == null || entity.SidedProperties == null)
        {
            return;
        }

        if (listenerEntityIds.Add(player.EntityId))
        {
            player.WatchedAttributes.RegisterModifiedListener(SkinModelAttribute, () =>
            {
                if (!disposed)
                {
                    RefreshPlayer(player);
                }
            });
        }

        RefreshPlayer(player);
    }

    private void RefreshPlayer(EntityPlayer player)
    {
        if (player.Api == null || player.SidedProperties == null || cleaningEntityIds.Contains(player.EntityId))
        {
            return;
        }

        string modelCode = player.WatchedAttributes.GetString(SkinModelAttribute, string.Empty) ?? string.Empty;
        bool eligible = Configs.TryGetValue(modelCode, out var config)
            && player.Api.ModLoader.IsModEnabled("wool")
            && player.World.ClassRegistry.GetEntityBehaviorClass(BehaviorCode) != null
            && HasWoolDrops(player, config);

        EntityBehavior? current = player.GetBehavior(BehaviorCode);
        bool owned = current != null
            && ownedBehaviors.TryGetValue(player.EntityId, out EntityBehavior? tracked)
            && ReferenceEquals(current, tracked);
        if (!eligible)
        {
            if (owned && current != null)
            {
                RemoveOwnedBehavior(player, current);
            }
            ownedBehaviors.Remove(player.EntityId);
            ownedModels.Remove(player.EntityId);
            return;
        }

        if (current != null && (!owned
            || (ownedModels.TryGetValue(player.EntityId, out string? ownedModel)
                && string.Equals(ownedModel, modelCode, StringComparison.Ordinal))))
        {
            return;
        }

        if (current != null && owned)
        {
            RemoveOwnedBehavior(player, current);
            current = null;
        }

        try
        {
            EntityBehavior behavior = player.World.ClassRegistry.CreateEntityBehavior(player, BehaviorCode);
            behavior.FromBytes(false);
            behavior.Initialize(player.Properties, new JsonObject(BuildAttributes(config)));
            if (player.WatchedAttributes.GetAttribute("generation") == null)
            {
                player.WatchedAttributes.SetInt("generation", 1);
            }
            player.AddBehavior(behavior);
            ownedBehaviors[player.EntityId] = behavior;
            ownedPlayers[player.EntityId] = player;
            ownedModels[player.EntityId] = modelCode;
        }
        catch (Exception exception)
        {
            player.Api.Logger.Warning("[AnimalicaCore] Could not attach optional ShearLib behavior to player model {0}: {1}", modelCode, exception.Message);
        }
    }

    private void CleanupPlayer(Entity entity, EntityDespawnData reasonData)
    {
        if (entity is EntityPlayer player)
        {
            listenerEntityIds.Remove(player.EntityId);
            if (ownedBehaviors.TryGetValue(player.EntityId, out EntityBehavior? behavior))
            {
                RemoveOwnedBehavior(player, behavior);
            }
            ownedModels.Remove(player.EntityId);
        }
    }

    private void RemoveOwnedBehavior(EntityPlayer player, EntityBehavior behavior)
    {
        if (!ownedBehaviors.TryGetValue(player.EntityId, out EntityBehavior? tracked)
            || !ReferenceEquals(tracked, behavior)
            || !cleaningEntityIds.Add(player.EntityId))
        {
            return;
        }

        ownedBehaviors.Remove(player.EntityId);
        ownedPlayers.Remove(player.EntityId);
        ownedModels.Remove(player.EntityId);
        try
        {
            if (ReferenceEquals(player.GetBehavior(BehaviorCode), behavior))
            {
                player.RemoveBehavior(behavior);
            }
        }
        finally
        {
            cleaningEntityIds.Remove(player.EntityId);
        }
    }

    private static bool HasWoolDrops(EntityPlayer player, (double Hours, int Min, int Max, int Bonus, double Scratch, string Drop, string? DropSecond, double DropSecondPercentage) config)
    {
        return player.World.GetItem(new AssetLocation(config.Drop)) != null
            && (config.DropSecond == null || player.World.GetItem(new AssetLocation(config.DropSecond)) != null);
    }

    private void AttachToServerPlayer(IServerPlayer player)
    {
        if (player?.Entity != null)
        {
            AttachToPlayer(player.Entity);
        }
    }

    private static JObject BuildAttributes((double Hours, int Min, int Max, int Bonus, double Scratch, string Drop, string? DropSecond, double DropSecondPercentage) config)
    {
        return new JObject
        {
            ["hoursPerUnit"] = config.Hours,
            ["hoursPerUnitGenerationalReduction"] = 0.5,
            ["hoursPerUnitMaxReduction"] = 8.0,
            ["maxGenBonus"] = 8,
            ["generationalMaxGrowthBonus"] = 2,
            ["generationalScratchChanceReduction"] = 0.05,
            ["minQuantity"] = config.Min,
            ["maxQuantity"] = config.Max,
            ["shearsBonus"] = config.Bonus,
            ["minGen"] = 1,
            ["scratchChance"] = config.Scratch,
            ["drop"] = config.Drop,
            ["dropSecond"] = config.DropSecond ?? config.Drop,
            ["dropSecondPercentage"] = config.DropSecondPercentage,
            ["shearSound"] = "game:sounds/player/scrape.ogg"
        };
    }
}
