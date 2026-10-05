#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace TamablesCritters;

internal sealed class EntityBehaviorTamablesCrittersMultiply : EntityBehaviorMultiply
{
    public const string BehaviorCode = "tamablescrittersmultiply";

    public EntityBehaviorTamablesCrittersMultiply(Entity entity) : base(entity) { }

    protected override void GiveBirth(float quantity)
    {
        if (SpawnEntityCodes == null || SpawnEntityCodes.Length == 0) return;

        Random random = entity.World.Rand;
        int generation = entity.WatchedAttributes.GetInt("generation", 0);
        while (quantity >= 1f || random.NextDouble() < quantity)
        {
            quantity--;
            AssetLocation offspringCode = SpawnEntityCodes[random.Next(SpawnEntityCodes.Length)];
            EntityProperties? offspringType = entity.World.GetEntityType(offspringCode);
            if (offspringType == null)
            {
                entity.World.Logger.Error(
                    "[tamablescritters] Registered offspring type {0} could not be resolved during birth from {1}.",
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
            CopyOwnership(entity, offspring);
            entity.World.SpawnEntity(offspring);
        }
    }

    internal static void CopyOwnership(Entity source, Entity destination)
    {
        ITreeAttribute? status = source.WatchedAttributes.GetTreeAttribute("domesticationstatus");
        if (status != null)
        {
            destination.WatchedAttributes.SetAttribute("domesticationstatus", status.Clone());
        }
        destination.WatchedAttributes.SetString(
            "activeCommand",
            source.WatchedAttributes.GetString("activeCommand", string.Empty)
        );
    }
}
