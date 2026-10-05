using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace FeralKinship
{
internal sealed class KinshipTargetFilter
{
    private readonly EntityAgent animal;
    private readonly string? animalRace;
    private readonly int damageMemoryMs;
    private readonly Dictionary<long, long> recentAttackers = new();

    public KinshipTargetFilter(EntityAgent animal, JsonObject taskConfig)
    {
        this.animal = animal;
        animalRace = FeralRaceResolver.GetAnimalRace(animal);
        damageMemoryMs = Math.Max(0, taskConfig["kinshipDamageMemoryMs"].AsInt(10000));
    }

    public bool ShouldIgnore(Entity candidate)
    {
        EntityPlayer? player = candidate as EntityPlayer;
        if (player == null || string.IsNullOrEmpty(animalRace))
        {
            return false;
        }

        string? playerRace = FeralRaceResolver.GetRace(player);
        if (!string.Equals(playerRace, animalRace, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        long nowMs = animal.World.ElapsedMilliseconds;
        if (!recentAttackers.TryGetValue(player.EntityId, out long expiresAtMs))
        {
            return true;
        }

        if (nowMs <= expiresAtMs)
        {
            return false;
        }

        recentAttackers.Remove(player.EntityId);
        return true;
    }

    public void OnEntityHurt(DamageSource source)
    {
        EntityPlayer? player = source.GetCauseEntity() as EntityPlayer;
        if (player == null || string.IsNullOrEmpty(animalRace))
        {
            return;
        }

        string? playerRace = FeralRaceResolver.GetRace(player);
        if (string.Equals(playerRace, animalRace, StringComparison.OrdinalIgnoreCase))
        {
            long nowMs = animal.World.ElapsedMilliseconds;
            recentAttackers[player.EntityId] = nowMs + damageMemoryMs;

            // This collection is normally tiny. Opportunistic cleanup prevents
            // stale multiplayer attackers from accumulating on long-lived mobs.
            if (recentAttackers.Count > 8)
            {
                List<long> expiredIds = new();
                foreach (KeyValuePair<long, long> entry in recentAttackers)
                {
                    if (entry.Value < nowMs)
                    {
                        expiredIds.Add(entry.Key);
                    }
                }

                foreach (long entityId in expiredIds)
                {
                    recentAttackers.Remove(entityId);
                }
            }
        }
    }
}
}
