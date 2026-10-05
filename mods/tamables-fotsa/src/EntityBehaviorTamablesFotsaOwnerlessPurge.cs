#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace TamablesFotsa;

/// <summary>
/// Removes invalid dedicated tamed variants which have no owner. Older
/// Tamables:FOTSA releases accidentally allowed these internal variants to
/// spawn naturally. The delayed, one-shot check also leaves enough time for
/// PetAI conversion, growth, and reproduction to copy or assign ownership.
/// </summary>
internal sealed class EntityBehaviorTamablesFotsaOwnerlessPurge : EntityBehavior
{
    public const string BehaviorCode = "tamablesfotsaownerlesspurge";
    private const int OwnershipGracePeriodMilliseconds = 5000;

    private long callbackId;

    public EntityBehaviorTamablesFotsaOwnerlessPurge(Entity entity) : base(entity)
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
            || !entity.Code.Domain.Equals("tamablesfotsa", StringComparison.OrdinalIgnoreCase)
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

        string domesticationLevel = entity.WatchedAttributes
            .GetTreeAttribute("domesticationstatus")?
            .GetString("domesticationLevel", "WILD") ?? "WILD";

        // Ownerless internally-spawned wild variants are invalid, but a saved
        // entity that still carries an explicit DOMESTICATED state may be a
        // real pet whose owner attribute was lost. Preserve that animal rather
        // than converting uncertain ownership data into permanent removal.
        if (domesticationLevel.Equals("DOMESTICATED", StringComparison.OrdinalIgnoreCase))
        {
            entity.World.Logger.Warning(
                "[tamablesfotsa] Preserving ownerless domesticated variant: code={0}, entityId={1}, tameState={2}, owner=<none>, reason=ownerless-purge-check, action=preserve.",
                entity.Code,
                entity.EntityId,
                domesticationLevel
            );
            return;
        }

        entity.World.Logger.Warning(
            "[tamablesfotsa] Ownerless tamed-variant purge: code={0}, entityId={1}, tameState={2}, owner=<none>, reason={3}, path=ownerless-purge.",
            entity.Code,
            entity.EntityId,
            domesticationLevel,
            EnumDespawnReason.Removed
        );

        // Removed is a drop-free despawn and therefore cannot turn a cleanup
        // pass over a crowded old save into an item/entity storm.
        entity.Die(EnumDespawnReason.Removed);
    }
}
