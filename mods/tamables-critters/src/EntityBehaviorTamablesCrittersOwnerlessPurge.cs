#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace TamablesCritters;

/// <summary>
/// Removes invalid dedicated tamed variants which have no owner. Older
/// Tamables:Critters releases accidentally allowed these internal variants to
/// spawn naturally. The delayed, one-shot check also leaves enough time for
/// PetAI conversion, growth, and reproduction to copy or assign ownership.
/// </summary>
internal sealed class EntityBehaviorTamablesCrittersOwnerlessPurge : EntityBehavior
{
    public const string BehaviorCode = "tamablescrittersownerlesspurge";
    private const int OwnershipGracePeriodMilliseconds = 5000;

    private long callbackId;

    public EntityBehaviorTamablesCrittersOwnerlessPurge(Entity entity) : base(entity)
    {
    }

    public override void OnEntitySpawn()
    {
        base.OnEntitySpawn();
        ScheduleOwnershipCheck();
    }

    public override void OnEntityLoaded()
    {
        base.OnEntityLoaded();
        ScheduleOwnershipCheck();
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        base.OnEntityDespawn(despawn);
        if (callbackId != 0)
        {
            entity.World.UnregisterCallback(callbackId);
            callbackId = 0;
        }
    }

    public override string PropertyName() => BehaviorCode;

    private void ScheduleOwnershipCheck()
    {
        if (entity.Api.Side != EnumAppSide.Server || callbackId != 0)
        {
            return;
        }

        callbackId = entity.World.RegisterCallback(PurgeIfOwnerless, OwnershipGracePeriodMilliseconds);
    }

    private void PurgeIfOwnerless(float deltaTime)
    {
        callbackId = 0;
        if (!entity.Alive
            || !entity.Code.Domain.Equals("tamablescritters", StringComparison.OrdinalIgnoreCase)
            || !entity.Code.Path.StartsWith("tame-", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string ownerUid = entity.WatchedAttributes
            .GetTreeAttribute("domesticationstatus")?
            .GetString("owner", string.Empty) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(ownerUid))
        {
            return;
        }

        // Removed is a drop-free despawn and therefore cannot turn a cleanup
        // pass over a crowded old save into an item/entity storm.
        entity.Die(EnumDespawnReason.Removed);
    }
}
