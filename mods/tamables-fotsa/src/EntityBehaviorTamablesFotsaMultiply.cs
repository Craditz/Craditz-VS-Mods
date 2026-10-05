#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace TamablesFotsa;

internal sealed class EntityBehaviorTamablesFotsaMultiply : EntityBehaviorMultiply
{
    public const string BehaviorCode = "tamablesfotsamultiply";

    public EntityBehaviorTamablesFotsaMultiply(Entity entity) : base(entity)
    {
    }

    protected override void GiveBirth(float quantity)
    {
        AssetLocation[] offspringCodes = TamablesFotsaSystem.GetRegisteredTamedOffspringCodes(entity);
        if (offspringCodes.Length == 0)
        {
            entity.World.Logger.Error(
                "[tamablesfotsa] No registered tamed offspring types found for {0}; falling back to the source birth list.",
                entity.Code
            );
            base.GiveBirth(quantity);
            return;
        }

        Random random = entity.World.Rand;
        int generation = entity.WatchedAttributes.GetInt("generation", 0);
        while (quantity >= 1f || random.NextDouble() < quantity)
        {
            quantity--;
            AssetLocation offspringCode = offspringCodes[random.Next(offspringCodes.Length)];
            EntityProperties? offspringType = entity.World.GetEntityType(offspringCode);
            if (offspringType == null)
            {
                entity.World.Logger.Error(
                    "[tamablesfotsa] Registered offspring type {0} could not be resolved during birth from {1}.",
                    offspringCode,
                    entity.Code
                );
                continue;
            }

            Entity offspring = entity.World.ClassRegistry.CreateEntity(offspringType);
            offspring.Pos.SetFrom(entity.Pos);
            offspring.Pos.Motion.X += (random.NextDouble() - 0.5d) / 20d;
            offspring.Pos.Motion.Z += (random.NextDouble() - 0.5d) / 20d;
            offspring.Attributes.SetString("origin", "reproduction");
            offspring.WatchedAttributes.SetInt("generation", generation + 1);
            entity.World.SpawnEntity(offspring);
        }
    }
}
