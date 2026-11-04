using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace AnimalicaEmotes;

public sealed class AnimalEmoteBehavior : EntityBehavior
{
    private ICoreAPI? api;

    public AnimalEmoteBehavior(Entity entity) : base(entity)
    {
    }

    public override string PropertyName() => "animalicaemotes";

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);
        api = entity.Api;

        if (api.Side != EnumAppSide.Server)
        {
            return;
        }

        entity.WatchedAttributes.RegisterModifiedListener("mountedOn", StopServerPose);
        entity.WatchedAttributes.RegisterModifiedListener("skinModel", StopServerPose);
    }

    public override void OnGameTick(float deltaTime)
    {
        if (api?.Side != EnumAppSide.Server || entity is not EntityPlayer player)
        {
            return;
        }

        if (!AnimalicaEmotesSystem.HasActivePose(player))
        {
            return;
        }

        EntityControls? controls = player.ServerControls;
        bool acted = player.MountedOn != null
            || player.Swimming
            || controls == null
            || controls.TriesToMove
            || controls.Jump
            || controls.Sneak
            || controls.Sprint
            || controls.FloorSitting
            || controls.LeftMouseDown
            || controls.RightMouseDown;

        if (acted)
        {
            AnimalicaEmotesSystem.SetActivePose(player, string.Empty);
        }
    }

    public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
    {
        base.OnEntityReceiveDamage(damageSource, ref damage);

        if (api?.Side == EnumAppSide.Server
            && damageSource.Type != EnumDamageType.Heal
            && entity is EntityPlayer player)
        {
            AnimalicaEmotesSystem.SetActivePose(player, string.Empty);
        }
    }

    public override void OnEntityDeath(DamageSource damageSource)
    {
        base.OnEntityDeath(damageSource);
        StopServerPose();
    }

    public override void OnEntityDespawn(EntityDespawnData despawnData)
    {
        base.OnEntityDespawn(despawnData);
    }

    private void StopServerPose()
    {
        if (api?.Side == EnumAppSide.Server && entity is EntityPlayer player)
        {
            AnimalicaEmotesSystem.SetActivePose(player, string.Empty);
        }
    }

}
