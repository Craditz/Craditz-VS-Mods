using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace ScentTrails;

public sealed class AnimalicaActiveAbilitySystem : ModSystem
{
    public const string NetworkChannelName = "animalicaabilities:active";

    private const string PounceHotkeyCode = "animalicaabilities-pounce";
    private const string AdrenalineHotkeyCode = "animalicaabilities-adrenaline";
    private const string AdrenalineWalkSpeedStat = "animalicaabilities-adrenaline-walkspeed";
    private const string AdrenalineDamageStat = "animalicaabilities-adrenaline-damage";

    private const double PounceRange = 7.0;
    private const double PounceLandingDistance = 1.2;
    private const double PounceArcHeight = 1.0;
    private const int PounceFlightMilliseconds = 500;
    private const int PounceCooldownMilliseconds = 3000;
    private const int AdrenalineDurationMilliseconds = 2500;
    private const int AdrenalineCooldownMilliseconds = 10000;
    private const float AdrenalineWalkSpeedBonus = 0.4f;
    private const float AdrenalineDamageBonus = 0.25f;
    private const float AdrenalineDamageReduction = 0.2f;

    private readonly Dictionary<long, ServerAbilityState> serverStates = new();
    private readonly Dictionary<long, ServerAbilityState> activePounces = new();
    private readonly Dictionary<long, ServerAbilityState> activeAdrenaline = new();
    private readonly List<ServerAbilityState> pounceUpdates = new();
    private readonly List<ServerAbilityState> adrenalineUpdates = new();
    private ICoreServerAPI? serverApi;
    private IServerNetworkChannel? serverChannel;
    private ICoreClientAPI? clientApi;
    private IClientNetworkChannel? clientChannel;
    private long serverTickListenerId;
    private long pounceTickListenerId;
    private AnimalicaPounceRenderer? pounceRenderer;
    private ClientPounceMotion? clientPounceMotion;
    private long clientPounceCooldownUntil;
    private long clientAdrenalineCooldownUntil;
    private long nextClientRequestAt;

    public override void StartServerSide(ICoreServerAPI api)
    {
        serverApi = api;
        serverChannel = api.Network.RegisterChannel(NetworkChannelName)
            .RegisterMessageType<AnimalicaActiveAbilityRequestPacket>()
            .RegisterMessageType<AnimalicaActiveAbilityResultPacket>()
            .RegisterMessageType<AnimalicaActiveAbilityPounceMotionPacket>()
            .SetMessageHandler<AnimalicaActiveAbilityRequestPacket>(OnAbilityRequested);
        serverTickListenerId = api.Event.RegisterGameTickListener(ServerTick, 50);
        pounceTickListenerId = api.Event.RegisterGameTickListener(PounceTick, 20);
        api.Event.PlayerDisconnect += OnPlayerDisconnect;
        api.Event.OnEntityDespawn += OnEntityDespawn;
        api.Logger.Notification(
            "[AnimalicaAbilities] Active abilities loaded. Pounce range {0:0.#}, adrenaline duration {1:0.#}s.",
            PounceRange,
            AdrenalineDurationMilliseconds / 1000d
        );
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        clientApi = api;
        clientChannel = api.Network.RegisterChannel(NetworkChannelName)
            .RegisterMessageType<AnimalicaActiveAbilityRequestPacket>()
            .RegisterMessageType<AnimalicaActiveAbilityResultPacket>()
            .RegisterMessageType<AnimalicaActiveAbilityPounceMotionPacket>()
            .SetMessageHandler<AnimalicaActiveAbilityResultPacket>(OnAbilityResult)
            .SetMessageHandler<AnimalicaActiveAbilityPounceMotionPacket>(OnPounceMotion);

        pounceRenderer = new AnimalicaPounceRenderer(this);
        api.Event.RegisterRenderer(pounceRenderer, EnumRenderStage.Before, "animalicaabilities-pounce");

        api.Input.RegisterHotKey(
            PounceHotkeyCode,
            Lang.Get("animalicaabilities:hotkey-pounce"),
            GlKeys.R,
            HotkeyType.CharacterControls
        );
        api.Input.SetHotKeyHandler(PounceHotkeyCode, OnPounceHotkey);

        api.Input.RegisterHotKey(
            AdrenalineHotkeyCode,
            Lang.Get("animalicaabilities:hotkey-adrenaline"),
            GlKeys.B,
            HotkeyType.CharacterControls
        );
        api.Input.SetHotKeyHandler(AdrenalineHotkeyCode, OnAdrenalineHotkey);
    }

    public override void Dispose()
    {
        if (serverApi != null && serverTickListenerId != 0)
        {
            serverApi.Event.UnregisterGameTickListener(serverTickListenerId);
        }
        if (serverApi != null && pounceTickListenerId != 0)
        {
            serverApi.Event.UnregisterGameTickListener(pounceTickListenerId);
        }

        if (serverApi != null)
        {
            serverApi.Event.PlayerDisconnect -= OnPlayerDisconnect;
            serverApi.Event.OnEntityDespawn -= OnEntityDespawn;
        }
        foreach (ServerAbilityState state in serverStates.Values)
        {
            EndPouncePhysics(state.Entity, state);
            SetAdrenalineStats(state.Entity, false);
        }
        serverStates.Clear();
        activePounces.Clear();
        activeAdrenaline.Clear();
        pounceUpdates.Clear();
        adrenalineUpdates.Clear();
        if (clientApi != null && pounceRenderer != null)
        {
            clientApi.Event.UnregisterRenderer(pounceRenderer, EnumRenderStage.Before);
        }

        serverApi = null;
        serverChannel = null;
        clientApi = null;
        clientChannel = null;
        pounceRenderer = null;
        clientPounceMotion = null;
        serverTickListenerId = 0;
        pounceTickListenerId = 0;
        clientPounceCooldownUntil = 0;
        clientAdrenalineCooldownUntil = 0;
        nextClientRequestAt = 0;
    }

    public AnimalicaActiveAbility EffectiveActiveAbility
    {
        get => AnimalicaAbilityResolver.Resolve(clientApi?.World.Player?.Entity).ActiveAbility;
    }

    public bool HasActiveAbility => EffectiveActiveAbility != AnimalicaActiveAbility.None;

    public bool IsPounceReady => clientApi != null && clientApi.World.ElapsedMilliseconds >= clientPounceCooldownUntil;

    public bool IsAdrenalineReady => clientApi != null && clientApi.World.ElapsedMilliseconds >= clientAdrenalineCooldownUntil;

    public bool UseActiveAbility()
    {
        return EffectiveActiveAbility switch
        {
            AnimalicaActiveAbility.Pounce => RequestPounce(),
            AnimalicaActiveAbility.Adrenaline => RequestAdrenaline(),
            _ => false
        };
    }

    private bool RequestPounce()
    {
        if (clientApi == null || clientChannel?.Connected != true || !IsPounceReady || !CanSendRequest())
        {
            return false;
        }

        EntityPlayer? player = clientApi.World.Player?.Entity;
        Entity? target = player == null ? null : FindPounceTarget(player);
        if (target == null || player == null)
        {
            clientApi.ShowChatMessage(Lang.Get("animalicaabilities:pounce-no-target"));
            return true;
        }

        clientChannel.SendPacket(new AnimalicaActiveAbilityRequestPacket
        {
            Ability = AnimalicaActiveAbility.Pounce,
            TargetEntityId = target.EntityId
        });
        nextClientRequestAt = clientApi.World.ElapsedMilliseconds + 250;
        return true;
    }

    private Entity? FindPounceTarget(EntityPlayer player)
    {
        Vec3d source = player.Pos.XYZ.AddCopy(player.LocalEyePos);
        BlockSelection? blockSelection = null;
        EntitySelection? entitySelection = null;
        EntityFilter entityFilter = entity =>
            entity.Alive
            && entity.EntityId != player.EntityId
            && entity is EntityAgent
            && entity is not EntityPlayer;

        clientApi!.World.RayTraceForSelection(
            source,
            player.Pos.Pitch,
            player.Pos.Yaw,
            (float)PounceRange,
            ref blockSelection,
            ref entitySelection,
            efilter: entityFilter
        );

        Entity? target = entitySelection?.Entity;
        if (IsUsablePounceTarget(player, target))
        {
            return target;
        }

        target = clientApi!.World.Player?.CurrentEntitySelection?.Entity;
        if (IsUsablePounceTarget(player, target))
        {
            return target;
        }

        return null;
    }

    private static bool IsUsablePounceTarget(EntityPlayer player, Entity? target)
    {
        return target is EntityAgent
            && target is not EntityPlayer
            && target.EntityId != player.EntityId
            && target.Alive
            && target.Pos.Dimension == player.Pos.Dimension
            && target.Pos.XYZ.DistanceTo(player.Pos.XYZ) <= PounceRange;
    }

    private bool RequestAdrenaline()
    {
        if (clientApi == null || clientChannel?.Connected != true || !IsAdrenalineReady || !CanSendRequest())
        {
            return false;
        }

        clientChannel.SendPacket(new AnimalicaActiveAbilityRequestPacket
        {
            Ability = AnimalicaActiveAbility.Adrenaline
        });
        nextClientRequestAt = clientApi.World.ElapsedMilliseconds + 250;
        return true;
    }

    private bool CanSendRequest()
    {
        return clientApi != null && clientApi.World.ElapsedMilliseconds >= nextClientRequestAt;
    }

    private bool OnPounceHotkey(KeyCombination combination)
    {
        return EffectiveActiveAbility == AnimalicaActiveAbility.Pounce && RequestPounce();
    }

    private bool OnAdrenalineHotkey(KeyCombination combination)
    {
        return EffectiveActiveAbility == AnimalicaActiveAbility.Adrenaline && RequestAdrenaline();
    }

    private void OnAbilityResult(AnimalicaActiveAbilityResultPacket packet)
    {
        if (clientApi == null)
        {
            return;
        }

        if (!packet.Success)
        {
            if (!string.IsNullOrWhiteSpace(packet.MessageKey))
            {
                clientApi.ShowChatMessage(Lang.Get(packet.MessageKey));
            }

            return;
        }

        long cooldownUntil = clientApi.World.ElapsedMilliseconds + packet.CooldownMilliseconds;
        if (packet.Ability == AnimalicaActiveAbility.Pounce)
        {
            clientPounceCooldownUntil = cooldownUntil;
        }
        else if (packet.Ability == AnimalicaActiveAbility.Adrenaline)
        {
            clientAdrenalineCooldownUntil = cooldownUntil;
        }

        if (!string.IsNullOrWhiteSpace(packet.MessageKey))
        {
            clientApi.ShowChatMessage(Lang.Get(packet.MessageKey));
        }
    }

    private void OnAbilityRequested(IServerPlayer player, AnimalicaActiveAbilityRequestPacket packet)
    {
        if (serverApi == null || serverChannel == null || player.Entity == null || packet.ProtocolVersion != 1)
        {
            return;
        }

        AnimalicaAbilityProfile profile = AnimalicaAbilityResolver.Resolve(player.Entity);
        if (!IsServerAbilityAllowed(profile, packet.Ability))
        {
            SendFailure(player, packet.Ability, "animalicaabilities:active-unavailable");
            return;
        }

        ServerAbilityState state = GetServerState(player);
        long now = serverApi.World.ElapsedMilliseconds;
        if (packet.Ability == AnimalicaActiveAbility.Pounce)
        {
            HandlePounceRequest(player, state, packet.TargetEntityId, now);
            return;
        }

        if (packet.Ability == AnimalicaActiveAbility.Adrenaline)
        {
            HandleAdrenalineRequest(player, state, now);
        }
    }

    private bool IsServerAbilityAllowed(AnimalicaAbilityProfile profile, AnimalicaActiveAbility ability)
    {
        if (ability is not (AnimalicaActiveAbility.Pounce or AnimalicaActiveAbility.Adrenaline))
        {
            return false;
        }

        return profile.ActiveAbility == ability;
    }

    private void HandlePounceRequest(IServerPlayer player, ServerAbilityState state, long targetEntityId, long now)
    {
        if (state.PounceFlight != null || now < state.PounceCooldownUntil)
        {
            SendFailure(player, AnimalicaActiveAbility.Pounce, "animalicaabilities:active-cooldown");
            return;
        }

        Entity? target = serverApi?.World.GetEntityById(targetEntityId);
        if (!IsValidPounceTarget(player.Entity, target))
        {
            SendFailure(player, AnimalicaActiveAbility.Pounce, "animalicaabilities:pounce-invalid-target");
            return;
        }

        state.PounceCooldownUntil = now + PounceCooldownMilliseconds;
        Vec3d startPosition = new Vec3d(player.Entity.Pos.X, player.Entity.Pos.Y, player.Entity.Pos.Z);
        Vec3d landingPosition = CalculatePounceLanding(player.Entity, target!);
        state.PounceFlight = new PounceFlight(
            targetEntityId,
            now,
            startPosition,
            landingPosition
        );
        state.LastPounceUpdateAt = 0;
        activePounces[player.Entity.EntityId] = state;
        InstallPouncePhysicsCallback(player, state);
        SendPounceMotion(player, state.PounceFlight);
        SendSuccess(player, AnimalicaActiveAbility.Pounce, PounceCooldownMilliseconds, "animalicaabilities:pounce-started");
    }

    private void HandleAdrenalineRequest(IServerPlayer player, ServerAbilityState state, long now)
    {
        if (now < state.AdrenalineCooldownUntil || now < state.AdrenalineUntil)
        {
            SendFailure(player, AnimalicaActiveAbility.Adrenaline, "animalicaabilities:active-cooldown");
            return;
        }

        state.AdrenalineUntil = now + AdrenalineDurationMilliseconds;
        state.AdrenalineCooldownUntil = now + AdrenalineCooldownMilliseconds;
        activeAdrenaline[player.Entity.EntityId] = state;
        EnsureBehavior(player.Entity);
        SetAdrenalineStats(player.Entity, true);
        SendSuccess(player, AnimalicaActiveAbility.Adrenaline, AdrenalineCooldownMilliseconds, "animalicaabilities:adrenaline-started");
    }

    private void ServerTick(float dt)
    {
        if (serverApi == null)
        {
            return;
        }

        long now = serverApi.World.ElapsedMilliseconds;
        adrenalineUpdates.Clear();
        adrenalineUpdates.AddRange(activeAdrenaline.Values);
        foreach (ServerAbilityState state in adrenalineUpdates)
        {
            EntityPlayer entity = state.Entity;
            if (!ReferenceEquals(state.Player.Entity, entity))
            {
                RemoveServerState(entity);
                continue;
            }
            if (state.AdrenalineUntil > 0 && now >= state.AdrenalineUntil)
            {
                state.AdrenalineUntil = 0;
                SetAdrenalineStats(entity, false);
                activeAdrenaline.Remove(entity.EntityId);
            }

        }
    }

    private void PounceTick(float dt)
    {
        if (serverApi == null)
        {
            return;
        }

        long now = serverApi.World.ElapsedMilliseconds;
        pounceUpdates.Clear();
        pounceUpdates.AddRange(activePounces.Values);
        foreach (ServerAbilityState state in pounceUpdates)
        {
            if (!ReferenceEquals(state.Player.Entity, state.Entity))
            {
                RemoveServerState(state.Entity);
                continue;
            }
            UpdatePounceIfDue(state.Player, state, now);
        }
    }

    private void UpdatePounceIfDue(IServerPlayer player, ServerAbilityState state, long now)
    {
        if (state.PounceFlight == null
            || (state.LastPounceUpdateAt != 0 && now - state.LastPounceUpdateAt < 15))
        {
            return;
        }

        state.LastPounceUpdateAt = now;
        UpdatePounce(player, state, now);
        if (state.PounceFlight == null) activePounces.Remove(state.Entity.EntityId);
    }

    private void UpdatePounce(IServerPlayer player, ServerAbilityState state, long now)
    {
        if (serverApi == null || player.Entity == null || state.PounceFlight == null)
        {
            return;
        }

        PounceFlight flight = state.PounceFlight;
        Entity? target = serverApi.World.GetEntityById(flight.TargetEntityId);
        if (!IsValidPounceTarget(player.Entity, target))
        {
            player.Entity.Pos.Motion.Set(0, 0, 0);
            EndPouncePhysics(player.Entity, state);
            state.PounceFlight = null;
            SendPounceMotionCancelled(player);
            return;
        }

        Vec3d direction = HorizontalDirection(flight.StartPosition, flight.LandingPosition);
        Vec3d landing = flight.LandingPosition;
        double progress = Math.Clamp(
            (now - flight.StartAtMilliseconds) / (double)PounceFlightMilliseconds,
            0,
            1
        );

        if (progress < 1)
        {
            return;
        }

        player.Entity.Pos.SetPos(landing.X, landing.Y, landing.Z);
        player.Entity.PositionBeforeFalling.Set(landing.X, landing.Y, landing.Z);
        player.Entity.Pos.Motion.Set(0, 0, 0);
        SendPouncePosition(player);
        SendPounceMotionCancelled(player);
        EndPouncePhysics(player.Entity, state);
        state.PounceFlight = null;

        if (target is EntityAgent targetAgent && targetAgent.Alive)
        {
            targetAgent.ReceiveDamage(new DamageSource
            {
                Source = EnumDamageSource.Player,
                SourceEntity = player.Entity,
                CauseEntity = player.Entity,
                SourcePos = player.Entity.Pos.XYZ,
                Type = EnumDamageType.BluntAttack,
                KnockbackStrength = 1.2f
            }, 3f);
        }
    }

    private void SendPouncePosition(IServerPlayer player)
    {
        if (serverApi == null || player.Entity == null)
        {
            return;
        }

        serverApi.Network.SendEntityPacket(
            player,
            player.Entity.EntityId,
            (int)EntityAgent.EntityServerPacketId.Teleport,
            SerializerUtil.Serialize(player.Entity.Pos.XYZ)
        );
    }

    private void SendPounceMotion(IServerPlayer player, PounceFlight flight)
    {
        if (serverChannel == null)
        {
            return;
        }

        Vec3d offset = flight.LandingPosition.SubCopy(flight.StartPosition);
        serverChannel.SendPacket(new AnimalicaActiveAbilityPounceMotionPacket
        {
            OffsetX = offset.X,
            OffsetY = offset.Y,
            OffsetZ = offset.Z,
            DurationMilliseconds = PounceFlightMilliseconds
        }, player);
    }

    private void SendPounceMotionCancelled(IServerPlayer player)
    {
        serverChannel?.SendPacket(new AnimalicaActiveAbilityPounceMotionPacket
        {
            Cancelled = true
        }, player);
    }

    private void InstallPouncePhysicsCallback(IServerPlayer player, ServerAbilityState state)
    {
        if (player.Entity == null || state.PounceAfterPhysicsTick != null)
        {
            return;
        }

        Action? previousCallback = player.Entity.AfterPhysicsTick;
        Action callback = () =>
        {
            previousCallback?.Invoke();
            UpdatePounceIfDue(player, state, serverApi?.World.ElapsedMilliseconds ?? long.MaxValue);
        };

        state.PreviousPounceAfterPhysicsTick = previousCallback;
        state.PounceAfterPhysicsTick = callback;
        player.Entity.AfterPhysicsTick = callback;
    }

    private static void EndPouncePhysics(EntityPlayer entity, ServerAbilityState state)
    {
        if (state.PounceAfterPhysicsTick != null
            && ReferenceEquals(entity.AfterPhysicsTick, state.PounceAfterPhysicsTick))
        {
            entity.AfterPhysicsTick = state.PreviousPounceAfterPhysicsTick;
        }

        state.PounceAfterPhysicsTick = null;
        state.PreviousPounceAfterPhysicsTick = null;
    }

    private bool IsValidPounceTarget(EntityPlayer player, Entity? target)
    {
        if (target == null || target.EntityId == player.EntityId || target is EntityPlayer || !target.Alive)
        {
            return false;
        }

        return target.Pos.Dimension == player.Pos.Dimension
            && target.Pos.XYZ.DistanceTo(player.Pos.XYZ) <= PounceRange
            && target is EntityAgent;
    }

    private Vec3d CalculatePounceLanding(EntityPlayer player, Entity target)
    {
        Vec3d playerPosition = new Vec3d(player.Pos.X, player.Pos.Y, player.Pos.Z);
        Vec3d targetPosition = new Vec3d(target.Pos.X, target.Pos.Y, target.Pos.Z);
        Vec3d direction = HorizontalDirection(playerPosition, targetPosition);
        double landingOffset = Math.Max(
            PounceLandingDistance,
            (target.SelectionBox.XSize + player.SelectionBox.XSize) * 0.5 + 0.25
        );
        Vec3d desiredLanding = targetPosition.SubCopy(direction * landingOffset);

        // Never choose a landing point below the player's takeoff level. This keeps a
        // target in a one-block hole from pulling the player down into the hole.
        desiredLanding.Y = Math.Max(desiredLanding.Y, playerPosition.Y);

        if (serverApi == null)
        {
            return desiredLanding;
        }

        // Search away from the target and then upward for the first position where
        // the player's collision box is clear. The server decides this position so
        // the client animation cannot finish inside a block and get pushed
        // underground by normal physics. A support check is intentionally not used:
        // an airborne pounce must be allowed to finish in open air and fall normally.
        for (int distanceStep = 0; distanceStep <= 8; distanceStep++)
        {
            Vec3d candidate = desiredLanding.AddCopy(direction * (distanceStep * 0.35));
            for (int riseStep = 0; riseStep <= 12; riseStep++)
            {
                candidate.Y = desiredLanding.Y + riseStep * 0.25;
                if (IsSafePounceLanding(player, candidate))
                {
                    return candidate;
                }
            }
        }

        // The takeoff point is already known to be safe. If the target is boxed in,
        // finish safely in place rather than teleporting the player into terrain.
        return playerPosition;
    }

    private bool IsSafePounceLanding(EntityPlayer player, Vec3d position)
    {
        CollisionTester collisionTester = serverApi!.World.CollisionTester;
        IBlockAccessor blockAccessor = serverApi.World.BlockAccessor;
        return !collisionTester.IsColliding(blockAccessor, player.CollisionBox, position, false);
    }

    private void OnPounceMotion(AnimalicaActiveAbilityPounceMotionPacket packet)
    {
        if (clientApi == null)
        {
            return;
        }

        if (packet.Cancelled)
        {
            clientPounceMotion = null;
            return;
        }

        EntityPlayer? player = clientApi.World.Player?.Entity;
        if (player == null
            || packet.DurationMilliseconds <= 0
            || !double.IsFinite(packet.OffsetX)
            || !double.IsFinite(packet.OffsetY)
            || !double.IsFinite(packet.OffsetZ))
        {
            return;
        }

        Vec3d offset = new Vec3d(packet.OffsetX, packet.OffsetY, packet.OffsetZ);
        if (offset.Length() > PounceRange + 4)
        {
            clientPounceMotion = null;
            return;
        }

        Vec3d startPosition = new Vec3d(player.Pos.X, player.Pos.Y, player.Pos.Z);
        clientPounceMotion = new ClientPounceMotion(
            startPosition,
            startPosition.AddCopy(offset),
            packet.DurationMilliseconds
        );
    }

    internal void UpdateClientPounce(float deltaTime)
    {
        if (clientApi == null || clientPounceMotion == null || clientApi.IsGamePaused)
        {
            return;
        }

        EntityPlayer? player = clientApi.World.Player?.Entity;
        if (player == null || !player.Alive)
        {
            clientPounceMotion = null;
            return;
        }

        ClientPounceMotion motion = clientPounceMotion;
        player.Controls.StopAllMovement();
        motion.ElapsedMilliseconds = Math.Min(
            motion.DurationMilliseconds,
            motion.ElapsedMilliseconds + Math.Max(0, deltaTime) * 1000
        );

        double progress = motion.ElapsedMilliseconds / motion.DurationMilliseconds;
        double arcProgress = progress * progress * (3 - 2 * progress);
        double x = motion.StartPosition.X
            + (motion.LandingPosition.X - motion.StartPosition.X) * arcProgress;
        double y = motion.StartPosition.Y
            + (motion.LandingPosition.Y - motion.StartPosition.Y) * arcProgress
            + Math.Sin(progress * Math.PI) * PounceArcHeight;
        double z = motion.StartPosition.Z
            + (motion.LandingPosition.Z - motion.StartPosition.Z) * arcProgress;

        player.Pos.SetPos(x, y, z);
        player.PositionBeforeFalling.Set(x, y, z);
        player.Pos.Motion.Set(0, 0, 0);
        player.Pos.Yaw = (float)Math.Atan2(
            motion.LandingPosition.X - motion.StartPosition.X,
            motion.LandingPosition.Z - motion.StartPosition.Z
        );

        if (progress >= 1)
        {
            clientPounceMotion = null;
        }
    }

    private static Vec3d HorizontalDirection(Vec3d from, Vec3d to)
    {
        Vec3d direction = to.SubCopy(from);
        direction.Y = 0;
        double length = Math.Sqrt(direction.X * direction.X + direction.Z * direction.Z);
        if (length < 0.001)
        {
            return new Vec3d(0, 0, 1);
        }

        return new Vec3d(direction.X / length, 0, direction.Z / length);
    }

    private void EnsureBehavior(EntityPlayer entity)
    {
        EntitySidedProperties? sidedProperties = entity.SidedProperties;
        if (sidedProperties?.Behaviors == null)
        {
            return;
        }

        EntityBehavior? behavior = entity.GetBehavior<AnimalicaActiveAbilityBehavior>();
        if (behavior == null)
        {
            behavior = new AnimalicaActiveAbilityBehavior(entity, this);
            entity.AddBehavior(behavior);
        }

        List<EntityBehavior> behaviors = sidedProperties.Behaviors;
        int behaviorIndex = behaviors.IndexOf(behavior);
        if (behaviorIndex > 0)
        {
            behaviors.RemoveAt(behaviorIndex);
            behaviors.Insert(0, behavior);
            entity.CacheServerBehaviors();
        }
    }

    private void SetAdrenalineStats(EntityPlayer entity, bool enabled)
    {
        if (enabled)
        {
            entity.Stats.Set("walkspeed", AdrenalineWalkSpeedStat, AdrenalineWalkSpeedBonus);
            entity.Stats.Set("meleeWeaponsDamage", AdrenalineDamageStat, AdrenalineDamageBonus);
        }
        else
        {
            entity.Stats.Remove("walkspeed", AdrenalineWalkSpeedStat);
            entity.Stats.Remove("meleeWeaponsDamage", AdrenalineDamageStat);
        }
    }

    public bool IsAdrenalineActive(EntityPlayer entity)
    {
        return serverApi != null
            && serverStates.TryGetValue(entity.EntityId, out ServerAbilityState? state)
            && state.AdrenalineUntil > serverApi.World.ElapsedMilliseconds;
    }

    private void OnPlayerDisconnect(IServerPlayer player)
    {
        if (player.Entity != null) RemoveServerState(player.Entity);
    }

    private void OnEntityDespawn(Entity entity, EntityDespawnData reason)
    {
        if (entity is EntityPlayer player) RemoveServerState(player);
    }

    private void RemoveServerState(EntityPlayer entity)
    {
        if (!serverStates.TryGetValue(entity.EntityId, out ServerAbilityState? state) ||
            !ReferenceEquals(state.Entity, entity)) return;
        EndPouncePhysics(entity, state);
        state.PounceFlight = null;
        SetAdrenalineStats(entity, false);
        serverStates.Remove(entity.EntityId);
        activePounces.Remove(entity.EntityId);
        activeAdrenaline.Remove(entity.EntityId);
    }

    private ServerAbilityState GetServerState(IServerPlayer player)
    {
        long entityId = player.Entity.EntityId;
        if (serverStates.TryGetValue(entityId, out ServerAbilityState? old) &&
            !ReferenceEquals(old.Entity, player.Entity)) RemoveServerState(old.Entity);
        if (!serverStates.TryGetValue(entityId, out ServerAbilityState? state))
        {
            state = new ServerAbilityState(player);
            serverStates[entityId] = state;
        }

        return state;
    }

    private void SendSuccess(IServerPlayer player, AnimalicaActiveAbility ability, int cooldown, string messageKey)
    {
        serverChannel?.SendPacket(new AnimalicaActiveAbilityResultPacket
        {
            Success = true,
            Ability = ability,
            CooldownMilliseconds = cooldown,
            MessageKey = messageKey
        }, player);
    }

    private void SendFailure(IServerPlayer player, AnimalicaActiveAbility ability, string messageKey)
    {
        serverChannel?.SendPacket(new AnimalicaActiveAbilityResultPacket
        {
            Success = false,
            Ability = ability,
            MessageKey = messageKey
        }, player);
    }

    private sealed class ServerAbilityState
    {
        public readonly IServerPlayer Player;
        public readonly EntityPlayer Entity;
        public ServerAbilityState(IServerPlayer player)
        {
            Player = player;
            Entity = player.Entity;
        }
        public long PounceCooldownUntil;
        public long AdrenalineCooldownUntil;
        public long AdrenalineUntil;
        public PounceFlight? PounceFlight;
        public long LastPounceUpdateAt;
        public Action? PreviousPounceAfterPhysicsTick;
        public Action? PounceAfterPhysicsTick;
    }

    private sealed class PounceFlight
    {
        public PounceFlight(
            long targetEntityId,
            long startAtMilliseconds,
            Vec3d startPosition,
            Vec3d landingPosition
        )
        {
            TargetEntityId = targetEntityId;
            StartAtMilliseconds = startAtMilliseconds;
            StartPosition = startPosition;
            LandingPosition = landingPosition;
        }

        public long TargetEntityId { get; }
        public long StartAtMilliseconds { get; }
        public Vec3d StartPosition { get; }
        public Vec3d LandingPosition { get; }
    }

    private sealed class ClientPounceMotion
    {
        public ClientPounceMotion(Vec3d startPosition, Vec3d landingPosition, int durationMilliseconds)
        {
            StartPosition = startPosition;
            LandingPosition = landingPosition;
            DurationMilliseconds = durationMilliseconds;
        }

        public Vec3d StartPosition { get; }
        public Vec3d LandingPosition { get; }
        public int DurationMilliseconds { get; }
        public double ElapsedMilliseconds { get; set; }
    }

}

public sealed class AnimalicaActiveAbilityBehavior : EntityBehavior
{
    private readonly AnimalicaActiveAbilitySystem system;

    public AnimalicaActiveAbilityBehavior(Entity entity, AnimalicaActiveAbilitySystem system)
        : base(entity)
    {
        this.system = system;
    }

    public override string PropertyName() => "animalicaabilities-active";

    public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
    {
        if (damageSource.Type != EnumDamageType.Heal
            && entity is EntityPlayer player
            && system.IsAdrenalineActive(player))
        {
            damage *= 1f - 0.2f;
        }
    }
}
