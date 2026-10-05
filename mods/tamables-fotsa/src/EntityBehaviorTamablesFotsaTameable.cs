#nullable enable

using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TamablesFotsa;

/// <summary>
/// PetAI's food behavior calls EntityBehaviorTameable.OnInteract directly,
/// so the stage check must live in the tameable behavior itself.
/// </summary>
internal sealed class EntityBehaviorTamablesFotsaTameable : PetAI.EntityBehaviorTameable
{
    public const string BehaviorCode = "tamablesfotsatameable";
    private string? tameEntityCode;
    private long replacementCallbackId;
    private bool hasLoggedBlockedReplacement;

    public EntityBehaviorTamablesFotsaTameable(Entity entity) : base(entity)
    {
    }

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);
        tameEntityCode = attributes["tameEntityCode"].AsString();
    }

    public override void OnInteract(
        EntityAgent byEntity,
        ItemSlot itemslot,
        Vec3d hitPosition,
        EnumInteractMode mode,
        ref EnumHandling handled)
    {
        if (TamablesFotsaSystem.ShouldBlockTamingInteraction(entity, itemslot, mode))
        {
            handled = EnumHandling.PreventSubsequent;
            return;
        }

        base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
        PreventPendingSourceDespawn();
    }

    public override WorldInteraction[] GetInteractionHelp(
        IClientWorldAccessor world,
        EntitySelection es,
        IClientPlayer player,
        ref EnumHandling handled)
    {
        // PetAI advertises every configured treat without checking whether
        // this wild animal is at the configured taming stage. Keep feeding
        // help for in-progress and completed pets, but do not promise an
        // interaction that the server intentionally rejects for wild adults.
        if (TamablesFotsaSystem.IsWrongTamingStageForWildEntity(entity))
        {
            return Array.Empty<WorldInteraction>();
        }

        return base.GetInteractionHelp(world, es, player, ref handled);
    }

    public override void OnEntityLoaded()
    {
        base.OnEntityLoaded();

        string domesticationLevel = entity.WatchedAttributes
            .GetTreeAttribute("domesticationstatus")?
            .GetString("domesticationLevel", "WILD") ?? "WILD";
        bool isTamedVariant = entity.Code.Domain.Equals("tamablesfotsa", StringComparison.OrdinalIgnoreCase)
            && entity.Code.Path.StartsWith("tame-", StringComparison.OrdinalIgnoreCase);
        if (entity.Api.Side != EnumAppSide.Server
            || isTamedVariant
            || !domesticationLevel.Equals("DOMESTICATED", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        PreventPendingSourceDespawn();
        string owner = entity.WatchedAttributes
            .GetTreeAttribute("domesticationstatus")?
            .GetString("owner", string.Empty) ?? string.Empty;
        entity.World.Logger.Warning(
            "[tamablesfotsa] Resuming completed tame replacement after load: code={0}, entityId={1}, tameState={2}, owner={3}, replacement={4}.",
            entity.Code,
            entity.EntityId,
            domesticationLevel,
            string.IsNullOrWhiteSpace(owner) ? "<none>" : owner,
            string.IsNullOrWhiteSpace(tameEntityCode) ? "none" : tameEntityCode
        );
        ScheduleTamedVariantReplacement();
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        string domesticationLevel = entity.WatchedAttributes
            .GetTreeAttribute("domesticationstatus")?
            .GetString("domesticationLevel", "WILD") ?? "WILD";
        bool isTamedVariant = entity.Code.Domain.Equals("tamablesfotsa", StringComparison.OrdinalIgnoreCase)
            && entity.Code.Path.StartsWith("tame-", StringComparison.OrdinalIgnoreCase);

        // Chunk unloads and server disconnects are expected lifecycle events.
        // Record only actual tamed/taming removals so runtime reports can tell
        // death, expiry, and ownerless cleanup apart without per-tick logging.
        if (entity.Api.Side == EnumAppSide.Server
            && despawn.Reason != EnumDespawnReason.Unload
            && despawn.Reason != EnumDespawnReason.Disconnect
            && (isTamedVariant || !domesticationLevel.Equals("WILD", StringComparison.OrdinalIgnoreCase)))
        {
            string owner = entity.WatchedAttributes
                .GetTreeAttribute("domesticationstatus")?
                .GetString("owner", string.Empty) ?? string.Empty;
            string replacement = GetReplacementTarget();
            string damageType = despawn.DamageSourceForDeath?.Type.ToString() ?? "none";

            entity.World.Logger.Notification(
                "[tamablesfotsa] Tamed lifecycle removal: code={0}, entityId={1}, tameState={2}, owner={3}, reason={4}, damageType={5}, replacement={6}.",
                entity.Code,
                entity.EntityId,
                domesticationLevel,
                string.IsNullOrWhiteSpace(owner) ? "<none>" : owner,
                despawn.Reason,
                damageType,
                replacement
            );
        }

        if (replacementCallbackId != 0)
        {
            entity.World.UnregisterCallback(replacementCallbackId);
            replacementCallbackId = 0;
        }

        base.OnEntityDespawn(despawn);
    }

    public override string PropertyName() => "tameable";

    private string GetReplacementTarget()
    {
        if (!string.IsNullOrWhiteSpace(tameEntityCode))
        {
            return tameEntityCode;
        }

        AssetLocation[]? adultCodes = entity.GetBehavior<EntityBehaviorGrow>()?.AdultEntityCodes;
        return adultCodes is { Length: > 0 }
            ? string.Join(",", adultCodes)
            : "none";
    }

    private void ScheduleTamedVariantReplacement()
    {
        if (replacementCallbackId != 0
            || string.IsNullOrWhiteSpace(tameEntityCode)
            || entity.Api.Side != EnumAppSide.Server)
        {
            return;
        }

        replacementCallbackId = entity.World.RegisterCallback(TryRestoreTamedVariant, 1000);
    }

    private void TryRestoreTamedVariant(float deltaTime)
    {
        replacementCallbackId = 0;
        if (!entity.Alive
            || entity.Api.Side != EnumAppSide.Server
            || string.IsNullOrWhiteSpace(tameEntityCode)
            || entity.Code.Domain.Equals("tamablesfotsa", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (DomesticationLevel != PetAI.DomesticationLevel.DOMESTICATED)
        {
            return;
        }

        EntityProperties? tameType = entity.World.GetEntityType(new AssetLocation(tameEntityCode));
        if (tameType == null)
        {
            entity.World.Logger.Error(
                "[tamablesfotsa] Cannot resume tame replacement for {0}#{1}: destination {2} is not registered.",
                entity.Code,
                entity.EntityId,
                tameEntityCode
            );
            return;
        }

        if (entity.World.CollisionTester.IsColliding(
            entity.World.BlockAccessor,
            tameType.SpawnCollisionBox,
            entity.Pos.XYZ,
            false))
        {
            if (!hasLoggedBlockedReplacement)
            {
                entity.World.Logger.Warning(
                    "[tamablesfotsa] Completed tame replacement is blocked by terrain; retaining source entity and retrying: code={0}, entityId={1}, owner={2}, replacement={3}.",
                    entity.Code,
                    entity.EntityId,
                    string.IsNullOrWhiteSpace(OwnerId) ? "<none>" : OwnerId,
                    tameEntityCode
                );
                hasLoggedBlockedReplacement = true;
            }

            // PetAI retries this same blocked conversion once per second.
            // Repeat after reload because its pending callback is canceled on
            // chunk unload and is not part of the serialized entity state.
            ScheduleTamedVariantReplacement();
            return;
        }

        Entity tameEntity = entity.World.ClassRegistry.CreateEntity(tameType);
        tameEntity.Pos.SetFrom(entity.Pos);

        entity.Die(EnumDespawnReason.Expire, null);
        entity.World.SpawnEntity(tameEntity);

        if (tameEntity.GetBehavior<PetAI.EntityBehaviorTameable>() is { } tameable)
        {
            tameable.DomesticationStatus = DomesticationStatus;
        }

        if (entity.WatchedAttributes.HasAttribute("grow"))
        {
            tameEntity.WatchedAttributes.SetAttribute("grow", entity.WatchedAttributes.GetAttribute("grow"));
        }

        tameEntity.GetBehavior<EntityBehaviorNameTag>()?.SetName(
            entity.GetBehavior<EntityBehaviorNameTag>()?.DisplayName
        );
        tameEntity.WatchedAttributes.SetInt(
            "textureIndex",
            entity.WatchedAttributes.GetInt("textureIndex", 0)
        );

        entity.World.Logger.Warning(
            "[tamablesfotsa] Restored completed tame replacement: source={0}#{1}, tame={2}#{3}, owner={4}.",
            entity.Code,
            entity.EntityId,
            tameEntity.Code,
            tameEntity.EntityId,
            string.IsNullOrWhiteSpace(OwnerId) ? "<none>" : OwnerId
        );
    }

    private void PreventPendingSourceDespawn()
    {
        if (!entity.Alive
            || entity.Api.Side != EnumAppSide.Server
            || string.IsNullOrWhiteSpace(tameEntityCode)
            || entity.Code.Domain.Equals("tamablesfotsa", StringComparison.OrdinalIgnoreCase)
            || DomesticationLevel != PetAI.DomesticationLevel.DOMESTICATED)
        {
            return;
        }

        // PetAI can keep the source type alive while a blocked replacement is
        // retried, but Bovinae's source type still has timed wild despawn. The
        // source is not being made permanently loaded; stop only that timer
        // until it can become its dedicated tame variant.
        EntityBehaviorDespawn? despawn = entity.GetBehavior<EntityBehaviorDespawn>();
        if (despawn != null)
        {
            if (despawn.DespawnSeconds < float.MaxValue)
            {
                despawn.DespawnSeconds = float.MaxValue;
            }

            if (!entity.WatchedAttributes.HasAttribute("despawnTotalDays")
                || entity.WatchedAttributes.GetDouble("despawnTotalDays") < double.MaxValue)
            {
                entity.WatchedAttributes.SetDouble("despawnTotalDays", double.MaxValue);
                entity.WatchedAttributes.MarkPathDirty("despawnTotalDays");
            }
        }
    }
}
