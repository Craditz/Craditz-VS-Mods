using System;
using System.Text;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using AnimalicaBodyTools.Util;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace AnimalicaBodyTools.Systems;

public sealed class HuntingToolSystem : ModSystem
{
    private BolasRenderer? renderer;
    public override double ExecuteOrder() => 1.02;
    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass("AnimalicaHuntingTool", typeof(ItemHuntingTool));
        api.RegisterItemClass("AnimalicaPoisonDart", typeof(ItemPoisonDart));
        api.RegisterEntity("AnimalicaBombBolt", typeof(EntityBombBolt));
        api.RegisterEntity("AnimalicaUtilityShot", typeof(EntityUtilityShot));
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api is not ICoreServerAPI server) return;
        var template = api.Assets.Get(new AssetLocation("animalicabodytools:config/ranged/dart-poison.json")).ToObject<GridRecipe>();
        foreach (var block in api.World.Blocks)
        {
            if (block?.Code?.Domain != "game" || !block.Code.PathStartsWith("mushroom-")
                || !block.Code.Path.EndsWith("-normal", StringComparison.Ordinal)
                || !(block.NutritionProps?.Health < 0)) continue;
            var recipe = template.Clone();
            recipe.Name = new AssetLocation("animalicabodytools:dart-poison-" + block.Code.Path);
            recipe.Ingredients!["M"].Code = block.Code;
            recipe.Output!.Attributes = new JsonObject(new JObject {
                [ItemPoisonDart.SourceKey] = block.Code.ToString(),
                [ItemPoisonDart.HealthKey] = block.NutritionProps!.Health
            });
            if (!api.World.Config.GetBool("classExclusiveRecipes", true)) recipe.RequiresTrait = null;
            recipe.OnParsed(api.World);
            if (recipe.Resolve(api.World, recipe.Name.ToString())) server.RegisterCraftingRecipe(recipe);
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        api.Event.OnEntityLoaded += Restore;
        api.Event.OnEntitySpawn += Restore;
    }
    private static void Restore(Entity entity)
    {
        if (entity is EntityAgent && entity.Attributes.HasAttribute(HuntingEffects.StateKey)) HuntingEffects.Ensure(entity);
    }
    public override void StartClientSide(ICoreClientAPI api) => renderer = new BolasRenderer(api);
    public override void Dispose() => renderer?.Dispose();
}

public sealed class ItemPoisonDart : Item
{
    internal const string SourceKey = "abtDartMushroom", HealthKey = "abtDartFoodHealth";
    internal const int Pulses = 4;
    // Darts crafted before potency was recorded dealt four HP in total.
    internal static float Total(ItemStack dart) => -2f * (float)dart.Attributes.GetDecimal(HealthKey, -2);

    public override void GetHeldItemInfo(ItemSlot slot, StringBuilder description, IWorldAccessor world, bool withDebugInfo)
    {
        if (slot.Itemstack is not {} dart) return;
        base.GetHeldItemInfo(slot, description, world, withDebugInfo);
        var source = dart.Attributes.GetString(SourceKey);
        if (source != null && world.GetBlock(new AssetLocation(source)) is {} block)
        {
            description.AppendLine(Lang.Get("animalicabodytools:dart-source", new ItemStack(block).GetName(),
                dart.Attributes.GetDecimal(HealthKey).ToString("0.###")));
        }
        var total = Total(dart);
        description.AppendLine(Lang.Get("animalicabodytools:dart-potency", total.ToString("0.###"), (total/Pulses).ToString("0.###")));
    }
}

public sealed class ItemHuntingTool : Item
{
    private static ItemSlot? FindDart(EntityAgent shooter)
    {
        ItemSlot? found = null;
        shooter.WalkInventory(slot =>
        {
            if (slot is ItemSlotCreative || slot.Itemstack is not { StackSize: > 0 } stack
                || stack.Collectible.Code is not { Domain: "animalicabodytools", Path: "dart-blank" or "dart-poison"
                    or "dart-trail" or "dart-slow" or "dart-berserk" or "dart-tranq" }) return true;

            found = slot;
            return false;
        });
        return found;
    }

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel,
        EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        if (byEntity is not EntityPlayer shooter || !shooter.Alive || !AnimalBodyToolTraitAccessUtil.HolderHasTraitFast(shooter)) return;
        handling = EnumHandHandling.PreventDefault;
        if (!firstEvent || shooter.Player is not IServerPlayer player || player.WorldData.CurrentGameMode == EnumGameMode.Spectator
            || shooter.IsActivityRunning("abtHuntingShot")) return;
        bool pipe = Code.Path == "blowpipe";
        var ammo = pipe ? FindDart(shooter) : slot;
        if (ammo?.Itemstack == null)
        {
            player.SendLocalisedMessage(GlobalConstants.InfoLogChatGroup,"animalicabodytools:blowpipe-empty");
            return;
        }
        var world = shooter.World;
        var type = world.GetEntityType(new AssetLocation(Code.Domain,"shot-"+ammo.Itemstack.Collectible.Code.Path));
        if (type == null || world.ClassRegistry.CreateEntity(type) is not EntityUtilityShot shot) return;
        var origin = shooter.Pos.XYZ.Add(0,shooter.LocalEyePos.Y,0);
        var direction = origin.AheadCopy(1, shooter.Pos.Pitch, shooter.Pos.Yaw) - origin;
        shot.World = world;
        shot.Pos.SetPosWithDimension(origin);
        shot.Pos.Motion.Set(direction * (pipe ? .80 : .46));
        shot.FiredBy = shooter;
        shot.ProjectileStack = ammo.Itemstack.Clone();
        shot.ProjectileStack.StackSize = 1;
        ((IProjectile)shot).PreInitialize();
        world.SpawnPriorityEntity(shot);
        ammo.TakeOut(1);
        ammo.MarkDirty();
        if (pipe) DamageItem(world, shooter, slot);
        shooter.SetActivityRunning("abtHuntingShot", pipe ? 650 : 450);
    }
}

public sealed class EntityUtilityShot : EntityProjectile
{
    private bool settled;
    public override bool CanCollect(Entity byEntity) => false;

    public override void SetRotationFromMotion()
    {
        base.SetRotationFromMotion();
        if (Code.Path.StartsWith("shot-bolas-",StringComparison.Ordinal))
            Pos.Roll = (World.ElapsedMilliseconds-msLaunch)*.015f;
    }
    protected override bool CanHitTarget(Entity target) => target is EntityAgent and not EntityPlayer
        && target.Alive && target != FiredBy && base.CanHitTarget(target);

    protected override void ImpactOnEntity(Entity target)
    {
        if (!CanHitTarget(target)) return;
        Settle(CanDealDamage(target) ? target : null);
    }

    protected override void IsColliding(EntityPos pos, double speed)
    {
        base.IsColliding(pos,speed);
        Settle(null);
    }

    private void Settle(Entity? target)
    {
        if (settled || !Alive || World.Side != EnumAppSide.Server) return;
        settled = true;
        var stack = ProjectileStack;
        if (stack != null && stack.ResolveBlockOrItem(World))
        {
            var code = stack.Collectible.Code.Path;
            if (code.StartsWith("bolas-",StringComparison.Ordinal))
            {
                bool intact;
                if (code == "bolas-stone") intact = World.Rand.NextDouble() >= .35;
                else
                {
                    int left = stack.Collectible.GetRemainingDurability(stack)-1;
                    intact = left > 0;
                    if (intact) stack.Collectible.SetDurability(stack,left);
                }
                if (target != null && !target.WatchedAttributes.HasAttribute(HuntingEffects.Bound))
                    HuntingEffects.Ensure(target).Bind(stack,intact);
                else if (intact) World.SpawnItemEntity(stack, Pos.XYZ);
            }
            else if (code is "dart-blank" or "dart-trail" or "dart-poison" or "dart-slow" or "dart-berserk" or "dart-tranq")
            {
                if (target != null && code == "dart-blank")
                    target.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Player, SourceEntity = this,
                        CauseEntity = FiredBy, Type = EnumDamageType.PiercingAttack, KnockbackStrength = 0 }, 1f);
                else if (target != null) HuntingEffects.Ensure(target).Mark(stack, FiredBy?.EntityId ?? 0);
                if (World.Rand.NextDouble() >= 0.75)
                    World.SpawnItemEntity(stack, target?.Pos.XYZ.Add(0,.12,0) ?? Pos.XYZ);
            }
        }
        Die();
    }
}
