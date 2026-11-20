using System;
using System.Collections.Generic;
using AnimalicaBodyTools.Util;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace AnimalicaBodyTools.Systems;

[ProtoContract]
public sealed class CrossbowAction { }

public sealed class RangedCrossbowSystem : ModSystem
{
    private const string Channel = "animalicabodytools:ranged";
    internal const string RemovedDuringLoad = "abtCrossbowRemovedDuringLoad";
    internal const string Loaded = "animalicaLoadedArrow";
    internal const string DrawPhase = "animalicaCrossbowDraw";
    internal const string ShotCounter = "animalicaCrossbowShots";
    internal const string SpeedFactor = "animalicaCrossbowMovement";
    private const string Hotkey = "animalica-crossbow-fire";
    private readonly Dictionary<string, Reload> loads = new();
    private Dictionary<string, string[]>? duckPatterns;
    private ICoreServerAPI server = null!;
    private long tickId;

    private sealed record Reload(ItemStack Weapon, ItemSlot AmmoSlot, ItemStack AmmoStack, long ReadyAt, Vec3d Origin, int Dimension);

    internal bool UsesDuck(ICoreAPI api, string? model, string pattern)
    {
        if (string.IsNullOrEmpty(model)) return false;
        duckPatterns ??= api.Assets.Get(new AssetLocation("animalicabodytools:config/ranged/duck-models.json"))
            .ToObject<Dictionary<string, string[]>>();
        return duckPatterns.TryGetValue(model, out var patterns) && Array.IndexOf(patterns, pattern) >= 0;
    }

    internal record struct BackFit(double Drop, double Pitch);
    private Dictionary<string, Dictionary<string, BackFit>>? backFits;

    internal bool TryGetBackFit(ICoreAPI api, string? model, string pattern, out BackFit fit)
    {
        fit = default;
        if (model == null) return false;
        backFits ??= api.Assets.Get(new AssetLocation("animalicabodytools:config/ranged/back-fits.json"))
            .ToObject<Dictionary<string, Dictionary<string, BackFit>>>();
        return backFits.TryGetValue(model, out var fits) && fits.TryGetValue(pattern, out fit);
    }

    public override void Start(ICoreAPI api)
    {
        api.RegisterCollectibleBehaviorClass("AnimalicaCrossbowWearable", typeof(CrossbowWearable));
        api.RegisterCollectibleBehaviorClass("AnimalicaLoadCrossbow", typeof(CrossbowAmmo));
        api.RegisterEntity("AnimalicaBallistaArrow", typeof(EntityBallistaArrow));
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Event.OnEntitySpawn += CrossbowAnimation.Attach;
        api.Event.OnEntityLoaded += CrossbowAnimation.Attach;
        var channel = api.Network.RegisterChannel(Channel).RegisterMessageType<CrossbowAction>();
        api.Input.RegisterHotKeyFirst(Hotkey, Lang.Get("animalicabodytools:crossbow-control"), (GlKeys)(KeyCombination.MouseStart + (int)EnumMouseButton.Middle),
            HotkeyType.CharacterControls);
        api.Input.SetHotKeyHandler(Hotkey, _ =>
        {
            if (!api.Input.MouseGrabbed || Equipped(api.World.Player)?.Itemstack?.Attributes.HasAttribute(Loaded) != true) return false;
            channel.SendPacket(new CrossbowAction());
            return true;
        });
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        server = api;
        api.Network.RegisterChannel(Channel).RegisterMessageType<CrossbowAction>()
            .SetMessageHandler<CrossbowAction>(OnAction);
        api.Event.PlayerNowPlaying += MoveLegacyCrossbow;
        api.Event.PlayerDisconnect += OnDisconnect;
        tickId = api.Event.RegisterGameTickListener(OnTick, 50);
    }

    private static void MoveLegacyCrossbow(IServerPlayer player)
    {
        player.Entity.WatchedAttributes.RemoveAttribute("animalicaCrossbowDucking");
        var inventory = player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);
        if (inventory == null || inventory.Count <= (int)EnumCharacterDressType.UpperBodyOver) return;
        var former = inventory[(int)EnumCharacterDressType.Shoulder];
        var outer = inventory[(int)EnumCharacterDressType.UpperBodyOver];
        if (outer?.Empty == true && former?.Itemstack?.ItemAttributes?["crossbowPattern"].Exists == true)
            former.TryPutInto(player.Entity.World, outer);
    }

    internal static ItemSlot? Equipped(IPlayer? player)
    {
        var inv = player?.InventoryManager?.GetOwnInventory(GlobalConstants.characterInvClassName);
        int index = (int)EnumCharacterDressType.UpperBodyOver;
        if (inv == null || inv.Count <= index) return null;
        var slot = inv[index];
        return slot?.Itemstack?.ItemAttributes?["crossbowPattern"].Exists == true ? slot : null;
    }

    private static bool CanOperate(IServerPlayer player) =>
        player.Entity.Alive && player.WorldData.CurrentGameMode != EnumGameMode.Spectator
        && AnimalBodyToolTraitAccessUtil.HolderHasTraitFast(player.Entity);

    private static string Pattern(ItemStack weapon) => weapon.ItemAttributes["crossbowPattern"].AsString("")!;

    private static bool Moving(EntityPlayer entity) =>
        entity.ServerControls.TriesToMove || entity.ServerControls.Jump || !entity.OnGround
        || entity.MountedOn != null || entity.Pos.Motion.HorizontalSquareDistanceTo(0, 0) > 0.000025;

    private static void Notify(IServerPlayer player, string key) =>
        player.SendLocalisedMessage(GlobalConstants.InfoLogChatGroup, "animalicabodytools:crossbow-" + key);

    private void OnAction(IServerPlayer player, CrossbowAction packet)
    {
        var slot = Equipped(player);
        if (slot?.Itemstack is not { } weapon || !CanOperate(player)) return;
        if (loads.ContainsKey(player.PlayerUID)) return;
        if (weapon.Attributes.GetItemstack(Loaded) is { } arrow)
            Fire(player, slot, weapon, arrow);
    }

    internal void StartReload(IServerPlayer player, ItemSlot ammo)
    {
        var weapon = Equipped(player)?.Itemstack;
        if (weapon == null || !CanOperate(player) || weapon.Attributes.HasAttribute(Loaded)
            || loads.ContainsKey(player.PlayerUID) || ammo != player.InventoryManager.ActiveHotbarSlot || ammo.Empty) return;
        if (Pattern(weapon) == "ballista" && Moving(player.Entity)) { Notify(player, "stand"); return; }

        weapon.TempAttributes.RemoveAttribute(RemovedDuringLoad);
        long duration = weapon.ItemAttributes["reloadMs"].AsInt();
        loads[player.PlayerUID] = new Reload(weapon, ammo, ammo.Itemstack!, server.World.ElapsedMilliseconds + duration,
            player.Entity.Pos.XYZ, player.Entity.Pos.Dimension);
        player.Entity.WatchedAttributes.SetInt(DrawPhase, 1);
        server.World.PlaySoundAt(new AssetLocation("game:sounds/bow-draw"), player.Entity);
        Notify(player, "loading");
    }

    internal void CancelReload(IServerPlayer player, ItemSlot ammo)
    {
        if (loads.TryGetValue(player.PlayerUID, out var load) && load.AmmoSlot == ammo)
        {
            loads.Remove(player.PlayerUID);
            Notify(player, "cancelled");
        }
    }

    private void OnTick(float dt)
    {
        foreach (var player in server.World.AllOnlinePlayers)
        {
            if (player is not IServerPlayer online || online.Entity == null) continue;
            UpdatePlayer(online);
        }
    }

    private void UpdatePlayer(IServerPlayer online)
    {
        CrossbowUnload.DeliverPending(online);
        var slot = Equipped(online);
        var weapon = slot?.Itemstack;
        string? pattern = weapon == null ? null : Pattern(weapon);
        if (loads.TryGetValue(online.PlayerUID, out var load))
        {
            bool cancelled = weapon != load.Weapon || load.Weapon.TempAttributes.GetBool(RemovedDuringLoad) || !CanOperate(online)
                || online.InventoryManager.ActiveHotbarSlot != load.AmmoSlot || load.AmmoSlot.Empty
                || load.AmmoSlot.Itemstack != load.AmmoStack || load.Dimension != online.Entity.Pos.Dimension;
            if (pattern == "ballista" && (Moving(online.Entity)
                || load.Origin.SquareDistanceTo(online.Entity.Pos.XYZ) > 0.0001)) cancelled = true;
            if (cancelled)
            {
                loads.Remove(online.PlayerUID);
                Notify(online, "cancelled");
            }
            else if (server.World.ElapsedMilliseconds >= load.ReadyAt)
            {
                loads.Remove(online.PlayerUID);
                load.Weapon.Attributes.SetItemstack(Loaded, load.AmmoSlot.TakeOut(1));
                load.AmmoSlot.MarkDirty();
                slot!.MarkDirty();
                server.World.PlaySoundAt(new AssetLocation("game:sounds/effect/latch"), online.Entity, null, false, 12);
                Notify(online, "ready");
            }
        }

        int phase = weapon?.Attributes.HasAttribute(Loaded) == true ? 11 : 0;
        if (loads.TryGetValue(online.PlayerUID, out var active))
        {
            int duration = active.Weapon.ItemAttributes["reloadMs"].AsInt();
            double elapsed = duration - (active.ReadyAt - server.World.ElapsedMilliseconds);
            phase = 1 + Math.Clamp((int)(10 * elapsed / duration), 0, 9);
        }
        var attrs = online.Entity.WatchedAttributes;
        if (attrs.GetInt(DrawPhase) != phase) attrs.SetInt(DrawPhase, phase);

        float factor = pattern switch
        {
            "ballista" => 0.9f,
            "medium" when loads.ContainsKey(online.PlayerUID) => 0.5f,
            _ => 1f
        };
        if (attrs.GetFloat(SpeedFactor, 1f) != factor) attrs.SetFloat(SpeedFactor, factor);
    }

    internal static float ShotDamage(float basis, float modifier) =>
        modifier > 0 ? basis * (1f + 0.20f * modifier) : basis + modifier;

    private void Fire(IServerPlayer player, ItemSlot slot, ItemStack weapon, ItemStack ammo)
    {
        if (!ammo.ResolveBlockOrItem(server.World)) return;
        var shooter = player.Entity;
        bool heavy = Pattern(weapon) == "ballista";
        string? bomb = ammo.ItemAttributes["boltBomb"].AsString();
        string entityCode = bomb != null ? "animalicabodytools:shot-bolt-" + bomb : heavy ? "animalicabodytools:ballistashot-" + ammo.Collectible.Variant["material"]
            : ammo.ItemAttributes["arrowEntityCode"].AsString("arrow-" + ammo.Collectible.Variant["material"])!;
        var type = server.World.GetEntityType(new AssetLocation(entityCode));
        if (type == null || server.World.ClassRegistry.CreateEntity(type) is not Entity entity
            || entity is not IProjectile projectile) { Notify(player, "invalid-ammo"); return; }

        var origin = shooter.Pos.XYZ.Add(0, shooter.LocalEyePos.Y, 0);
        var direction = origin.AheadCopy(1, shooter.Pos.Pitch, shooter.Pos.Yaw) - origin;
        entity.World = server.World;
        
        entity.Pos.SetPosWithDimension(origin);
        entity.Pos.Motion.Set(direction * weapon.ItemAttributes["shotSpeed"].AsFloat());
        projectile.FiredBy = shooter;
        projectile.WeaponStack = weapon.Clone();
        projectile.WeaponStack.Attributes.RemoveAttribute(Loaded);
        projectile.ProjectileStack = ammo;
        var arrow = bomb == null ? ammo.Item : server.World.GetItem(new AssetLocation("game", "arrow-" + ammo.Collectible.Variant["material"]));
        if (arrow == null) return;
        projectile.Damage = ShotDamage(weapon.ItemAttributes["damage"].AsFloat(), arrow.Attributes["damage"].AsFloat());
        projectile.DropOnImpactChance = 1f - ammo.ItemAttributes["breakChanceOnImpact"].AsFloat(0.5f);
        projectile.PreInitialize();
        server.World.SpawnPriorityEntity(entity);
        weapon.Attributes.RemoveAttribute(Loaded);
        shooter.WatchedAttributes.SetInt(DrawPhase, 0);
        shooter.WatchedAttributes.SetInt(ShotCounter, shooter.WatchedAttributes.GetInt(ShotCounter) + 1);
        weapon.Collectible.DamageItem(server.World, shooter, slot);
        slot.MarkDirty();
        if (heavy)
        {
            shooter.WatchedAttributes.SetFloat("onHurtDir", (float)Math.Atan2(-direction.X, -direction.Z));
            shooter.WatchedAttributes.SetDouble("kbdirX", -direction.X * 0.35);
            shooter.WatchedAttributes.SetDouble("kbdirY", 0.05);
            shooter.WatchedAttributes.SetDouble("kbdirZ", -direction.Z * 0.35);
            shooter.WatchedAttributes.SetInt("onHurtCounter", shooter.WatchedAttributes.GetInt("onHurtCounter") + 1);
            shooter.WatchedAttributes.SetFloat("onHurt", 0.01f);
        }
        server.World.PlaySoundAt(new AssetLocation("game:sounds/bow-release"), shooter);
    }

    private void OnDisconnect(IServerPlayer player)
    {
        loads.Remove(player.PlayerUID);
        player.Entity?.WatchedAttributes.RemoveAttribute(SpeedFactor);
        player.Entity?.WatchedAttributes.RemoveAttribute(DrawPhase);
    }

    public override void Dispose()
    {
        if (server != null)
        {
            server.Event.UnregisterGameTickListener(tickId);
            server.Event.PlayerNowPlaying -= MoveLegacyCrossbow;
            server.Event.PlayerDisconnect -= OnDisconnect;
        }
        loads.Clear();
        base.Dispose();
    }
}




