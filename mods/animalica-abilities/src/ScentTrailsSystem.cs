using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ScentTrails;

public sealed class ScentTrailsSystem : ModSystem
{
    private const string NetworkChannelName = "animalicaabilities:main";
    private const string ToggleScentHotkeyCode = "animalicaabilities-scent";
    public const string AbilitiesHotkeyCode = "animalicaabilities-abilities";

    private const float CarrierWindScale = 20f;
    private const float MoteWindScale = 5f;
    private readonly EmissionLayout emissionLayout = EmissionLayout.LatestOnly;
    private readonly ParticleRecipe particleRecipe = ParticleRecipe.CarrierGlowingMotes;

    private const int SamplingIntervalMs = 1000;
    private const double DepositDistance = 2.0;
    private const double DepositDistanceSquared = DepositDistance * DepositDistance;
    private const float SamplingRadius = 48f;
    private const float SamplingVerticalRange = 32f;
    private const int TrailLifetimeMs = 120_000;
    private const int MaxNodesPerTrail = 32;

    private const float RevealRadius = 48f;
    private const int MaxNodesPerResponse = 384;
    private const int ParticleEmissionIntervalMs = 50;
    private const int NodeRefreshIntervalMs = 2000;
    private const int MaxNodesPerEmission = 48;

    private readonly Dictionary<long, TrailState> trails = new();
    private readonly List<ClientScentNode> revealedNodes = new();
    private ClientScentNode[][] trailGroups = Array.Empty<ClientScentNode[]>();
    private readonly Dictionary<long, long> nextTrailEmissionAtMs = new();
    private readonly List<ScentCarrier> activeCarriers = new();

    private ICoreServerAPI? serverApi;
    private IServerNetworkChannel? serverChannel;
    private ICoreClientAPI? clientApi;
    private IClientNetworkChannel? clientChannel;
    private GuiDialogScentTrailsAbilities? abilitiesDialog;
    private long serverTickListenerId;
    private long clientEmissionListenerId;
    private long nextNodeRefreshAtMs;
    private bool scentViewEnabled;
    private AnimalicaAbilityProfile abilityProfile = AnimalicaAbilityProfile.None;
    private float scentOpacityMultiplier = 1f;
    private float scentSizeMultiplier = 1f;
    private float scentLightMultiplier = 1f;
    private bool needsInitialSeed;
    private bool reportedNoScent;

    public override void StartServerSide(ICoreServerAPI api)
    {
        serverApi = api;
        serverChannel = api.Network.RegisterChannel(NetworkChannelName)
            .RegisterMessageType<ScentRequestPacket>()
            .RegisterMessageType<ScentResponsePacket>()
            .SetMessageHandler<ScentRequestPacket>(OnScentRequested);

        serverTickListenerId = api.Event.RegisterGameTickListener(SampleNearbyCreatures, SamplingIntervalMs);
        api.Logger.Notification(
            "[AnimalicaAbilities] Global scent sampling enabled: radius {0}, lifetime {1}s, spacing {2:0.#} blocks.",
            SamplingRadius,
            TrailLifetimeMs / 1000,
            DepositDistance
        );
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        clientApi = api;
        RefreshAbilityProfile();
        abilitiesDialog = new GuiDialogScentTrailsAbilities(
            api,
            this,
            api.ModLoader.GetModSystem<AnimalicaNightVisionSystem>()
        );
        clientChannel = api.Network.RegisterChannel(NetworkChannelName)
            .RegisterMessageType<ScentRequestPacket>()
            .RegisterMessageType<ScentResponsePacket>()
            .SetMessageHandler<ScentResponsePacket>(OnScentResponse);

        api.Input.RegisterHotKey(
            ToggleScentHotkeyCode,
            Lang.Get("animalicaabilities:hotkey-scent"),
            GlKeys.K,
            HotkeyType.CharacterControls
        );
        api.Input.SetHotKeyHandler(ToggleScentHotkeyCode, OnToggleScentHotkey);

        api.Input.RegisterHotKey(
            AbilitiesHotkeyCode,
            Lang.Get("animalicaabilities:hotkey-abilities"),
            GlKeys.N,
            HotkeyType.GUIOrOtherControls
        );
        api.Input.SetHotKeyHandler(AbilitiesHotkeyCode, OnOpenAbilitiesHotkey);
        clientEmissionListenerId = api.Event.RegisterGameTickListener(EmitScentParticles, ParticleEmissionIntervalMs);
    }

    public override void Dispose()
    {
        if (serverApi != null && serverTickListenerId != 0)
        {
            serverApi.Event.UnregisterGameTickListener(serverTickListenerId);
        }

        if (clientApi != null)
        {
            if (clientEmissionListenerId != 0)
            {
                clientApi.Event.UnregisterGameTickListener(clientEmissionListenerId);
            }

        }
        abilitiesDialog?.TryClose();
        trails.Clear();
        revealedNodes.Clear();
        trailGroups = Array.Empty<ClientScentNode[]>();
        nextTrailEmissionAtMs.Clear();
        activeCarriers.Clear();
        serverApi = null;
        serverChannel = null;
        clientApi = null;
        clientChannel = null;
        serverTickListenerId = 0;
        clientEmissionListenerId = 0;
        scentViewEnabled = false;
        abilityProfile = AnimalicaAbilityProfile.None;
        scentOpacityMultiplier = 1f;
        scentSizeMultiplier = 1f;
        scentLightMultiplier = 1f;
        abilitiesDialog = null;
    }

    private void SampleNearbyCreatures(float dt)
    {
        if (serverApi == null)
        {
            return;
        }

        long nowMs = serverApi.World.ElapsedMilliseconds;
        HashSet<long> sampledEntityIds = new();

        foreach (IPlayer player in serverApi.World.AllOnlinePlayers)
        {
            if (player is not IServerPlayer serverPlayer
                || serverPlayer.ConnectionState != EnumClientState.Playing
                || serverPlayer.Entity == null)
            {
                continue;
            }

            Entity[] nearbyEntities = serverApi.World.GetEntitiesAround(
                serverPlayer.Entity.Pos.XYZ,
                SamplingRadius,
                SamplingVerticalRange,
                IsScentSource
            );

            foreach (Entity entity in nearbyEntities)
            {
                if (sampledEntityIds.Add(entity.EntityId))
                {
                    RecordEntityPosition(entity, nowMs);
                }
            }
        }

        PruneExpiredTrails(nowMs);
    }

    private static bool IsScentSource(Entity entity)
    {
        return entity is EntityAgent
            && entity is not EntityPlayer
            && !(entity.Code?.Path.StartsWith("butterfly-", StringComparison.OrdinalIgnoreCase) ?? false)
            && entity.Alive
            && entity.EntityId > 0;
    }

    private void RecordEntityPosition(Entity entity, long nowMs)
    {
        Vec3d position = entity.Pos.XYZ;
        int dimension = entity.Pos.Dimension;

        if (!trails.TryGetValue(entity.EntityId, out TrailState? trail))
        {
            trail = new TrailState(position, dimension, nowMs);
            trails[entity.EntityId] = trail;
            AddNode(trail, entity.EntityId, position, dimension, nowMs);
            return;
        }

        trail.LastSeenMs = nowMs;
        if (trail.Dimension == dimension
            && trail.LastDepositPosition.SquareDistanceTo(position) < DepositDistanceSquared)
        {
            return;
        }

        trail.LastDepositPosition.Set(position);
        trail.Dimension = dimension;
        AddNode(trail, entity.EntityId, position, dimension, nowMs);
    }

    private static void AddNode(TrailState trail, long trailId, Vec3d position, int dimension, long nowMs)
    {
        trail.Nodes.Enqueue(new ScentNode(
            position.X,
            position.Y + 0.08,
            position.Z,
            dimension,
            nowMs,
            trailId
        ));

        while (trail.Nodes.Count > MaxNodesPerTrail)
        {
            trail.Nodes.Dequeue();
        }
    }

    private void PruneExpiredTrails(long nowMs)
    {
        List<long>? emptyTrailIds = null;
        foreach ((long entityId, TrailState trail) in trails)
        {
            while (trail.Nodes.Count > 0 && nowMs - trail.Nodes.Peek().CreatedAtMs > TrailLifetimeMs)
            {
                trail.Nodes.Dequeue();
            }

            if (trail.Nodes.Count > 0 || nowMs - trail.LastSeenMs <= TrailLifetimeMs)
            {
                continue;
            }

            emptyTrailIds ??= new List<long>();
            emptyTrailIds.Add(entityId);
        }

        if (emptyTrailIds == null)
        {
            return;
        }

        foreach (long entityId in emptyTrailIds)
        {
            trails.Remove(entityId);
        }
    }

    private void OnScentRequested(IServerPlayer player, ScentRequestPacket packet)
    {
        if (serverApi == null
            || serverChannel == null
            || player.Entity == null
            || packet.ProtocolVersion != 1
            || !AnimalicaAbilityResolver.Resolve(player.Entity).HasScent)
        {
            return;
        }

        long nowMs = serverApi.World.ElapsedMilliseconds;
        PruneExpiredTrails(nowMs);

        Vec3d playerPosition = player.Entity.Pos.XYZ;
        int playerDimension = player.Entity.Pos.Dimension;
        double radiusSquared = RevealRadius * RevealRadius;

        ScentNodePacket[] nodes = trails.Values
            .SelectMany(trail => trail.Nodes)
            .Where(node => node.Dimension == playerDimension
                && SquareDistance(node.X, node.Y, node.Z, playerPosition) <= radiusSquared)
            .OrderByDescending(node => node.CreatedAtMs)
            .ThenBy(node => SquareDistance(node.X, node.Y, node.Z, playerPosition))
            .Take(MaxNodesPerResponse)
            .Select(node => new ScentNodePacket
            {
                X = node.X,
                Y = node.Y,
                Z = node.Z,
                AgeMilliseconds = (int)Math.Clamp(nowMs - node.CreatedAtMs, 0, TrailLifetimeMs),
                TrailId = node.TrailId
            })
            .ToArray();

        serverChannel.SendPacket(new ScentResponsePacket { Nodes = nodes }, player);
    }

    public bool ToggleScentView()
    {
        RefreshAbilityProfile();
        if (clientApi == null || !abilityProfile.HasScent || clientChannel?.Connected != true)
        {
            return false;
        }

        scentViewEnabled = !scentViewEnabled;
        if (scentViewEnabled)
        {
            needsInitialSeed = true;
            reportedNoScent = false;
            nextNodeRefreshAtMs = 0;
            clientApi.ShowChatMessage(Lang.Get("animalicaabilities:scent-view-on"));
            RequestScentNodes();
        }
        else
        {
            revealedNodes.Clear();
            trailGroups = Array.Empty<ClientScentNode[]>();
            nextTrailEmissionAtMs.Clear();
            activeCarriers.Clear();
            clientApi.ShowChatMessage(Lang.Get("animalicaabilities:scent-view-off"));
        }

        return true;
    }

    public bool ApplyScentProfile(ScentStrengthProfile profile)
    {
        RefreshAbilityProfile();
        if (!abilityProfile.HasScent)
        {
            return false;
        }

        (scentOpacityMultiplier, scentSizeMultiplier, scentLightMultiplier) = profile switch
        {
            ScentStrengthProfile.Weak => (0.6f, 0.8f, 0.65f),
            ScentStrengthProfile.Normal => (0.8f, 0.9f, 0.8f),
            ScentStrengthProfile.Strong => (1.0f, 1.0f, 1.0f),
            _ => (1.0f, 1.0f, 1.0f)
        };

        clientApi?.ShowChatMessage(Lang.Get(
            "animalicaabilities:abilities-strength-applied",
            Lang.Get(profile switch
            {
                ScentStrengthProfile.Weak => "animalicaabilities:abilities-strength-weak",
                ScentStrengthProfile.Strong => "animalicaabilities:abilities-strength-strong",
                _ => "animalicaabilities:abilities-strength-normal"
            })
        ));
        return true;
    }

    private bool OnToggleScentHotkey(KeyCombination combination)
    {
        return ToggleScentView();
    }

    private bool OnOpenAbilitiesHotkey(KeyCombination combination)
    {
        if (abilitiesDialog == null)
        {
            return false;
        }

        if (abilitiesDialog.IsOpened())
        {
            abilitiesDialog.TryClose();
        }
        else
        {
            abilitiesDialog.TryOpen();
        }

        return true;
    }

    public bool ScentViewEnabled => scentViewEnabled;

    public bool HasScentAbility
    {
        get
        {
            RefreshAbilityProfile();
            return abilityProfile.HasScent;
        }
    }

    public AnimalicaAbilityProfile AbilityProfile
    {
        get
        {
            RefreshAbilityProfile();
            return abilityProfile;
        }
    }

    private void RefreshAbilityProfile()
    {
        if (clientApi == null)
        {
            return;
        }

        AnimalicaAbilityProfile nextProfile = AnimalicaAbilityResolver.Resolve(clientApi.World.Player?.Entity);
        if (nextProfile.Race.Equals(abilityProfile.Race, StringComparison.OrdinalIgnoreCase)
            && nextProfile.Abilities == abilityProfile.Abilities)
        {
            return;
        }

        abilityProfile = nextProfile;
        if (!abilityProfile.HasScent && scentViewEnabled)
        {
            scentViewEnabled = false;
            revealedNodes.Clear();
            trailGroups = Array.Empty<ClientScentNode[]>();
            nextTrailEmissionAtMs.Clear();
            activeCarriers.Clear();
            clientApi.ShowChatMessage(Lang.Get("animalicaabilities:scent-unavailable"));
        }
    }

    private void RequestScentNodes()
    {
        if (!scentViewEnabled || !HasScentAbility || clientApi == null || clientChannel?.Connected != true)
        {
            return;
        }

        clientChannel.SendPacket(new ScentRequestPacket());
        nextNodeRefreshAtMs = clientApi.World.ElapsedMilliseconds + NodeRefreshIntervalMs;
    }

    private void OnScentResponse(ScentResponsePacket packet)
    {
        if (clientApi == null || !scentViewEnabled)
        {
            return;
        }

        revealedNodes.Clear();
        foreach (ScentNodePacket node in packet.Nodes ?? Array.Empty<ScentNodePacket>())
        {
            revealedNodes.Add(new ClientScentNode(
                new Vec3d(node.X, node.Y, node.Z),
                node.AgeMilliseconds,
                node.TrailId
            ));
        }

        // The response is immutable until the next refresh. Keep the same stable
        // ordering without grouping and sorting it again every 50 ms.
        trailGroups = revealedNodes
            .GroupBy(node => node.TrailId)
            .Select(group => group.OrderBy(node => node.AgeMilliseconds).ToArray())
            .OrderBy(group => group[0].AgeMilliseconds)
            .Take(MaxNodesPerEmission)
            .ToArray();
        HashSet<long> currentTrails = revealedNodes.Select(node => node.TrailId).ToHashSet();
        foreach (long trailId in nextTrailEmissionAtMs.Keys.ToArray())
        {
            if (!currentTrails.Contains(trailId)) nextTrailEmissionAtMs.Remove(trailId);
        }

        if (revealedNodes.Count == 0)
        {
            if (!reportedNoScent)
            {
                clientApi.TriggerIngameError(this, "no-scent", Lang.Get("animalicaabilities:no-scent"));
                reportedNoScent = true;
            }
            return;
        }

        reportedNoScent = false;
        if (needsInitialSeed)
        {
            needsInitialSeed = false;
            SeedCurrentPlume();
        }
    }

    private void EmitScentParticles(float dt)
    {
        RefreshAbilityProfile();
        if (!scentViewEnabled || !abilityProfile.HasScent || clientApi == null)
        {
            return;
        }

        UpdateCarriers(dt);

        if (clientApi.World.ElapsedMilliseconds >= nextNodeRefreshAtMs)
        {
            RequestScentNodes();
        }

        if (revealedNodes.Count == 0)
        {
            return;
        }

        SpawnDueTrailParticles();
    }

    private void SeedCurrentPlume()
    {
        if (clientApi == null || revealedNodes.Count == 0)
        {
            return;
        }

        long nowMs = clientApi.World.ElapsedMilliseconds;
        foreach (ClientScentNode[] trailNodes in GetTrailGroups())
        {
            SpawnGlowingMoteCarrier(trailNodes[0]);
            ScheduleNextEmission(trailNodes[0].TrailId, nowMs);
        }
    }

    private void SpawnDueTrailParticles()
    {
        if (clientApi == null)
        {
            return;
        }

        long nowMs = clientApi.World.ElapsedMilliseconds;
        foreach (ClientScentNode[] trailNodes in GetTrailGroups())
        {
            long trailId = trailNodes[0].TrailId;
            if (nextTrailEmissionAtMs.TryGetValue(trailId, out long nextMs) && nowMs < nextMs)
            {
                continue;
            }

            SpawnGlowingMoteCarrier(trailNodes[0]);
            ScheduleNextEmission(trailId, nowMs);
        }
    }

    private ClientScentNode[][] GetTrailGroups()
    {
        return trailGroups;
    }

    private void ScheduleNextEmission(long trailId, long nowMs)
    {
        if (clientApi == null)
        {
            return;
        }

        nextTrailEmissionAtMs[trailId] = nowMs
            + 1600
            + (long)(clientApi.World.Rand.NextDouble() * 1400);
    }

    private void SpawnGlowingMoteCarrier(ClientScentNode node)
    {
        SpawnParticleRecipe(node, 0f);
    }

    private void SpawnParticleRecipe(ClientScentNode node, float preseedSeconds)
    {
        switch (particleRecipe)
        {
            case ParticleRecipe.BrighteningStream:
                SpawnCarrier(
                    node,
                    preseedSeconds,
                    carrierWindScale: 10f,
                    carrierLifetime: 2.6f,
                    childWindScale: 1.5f,
                    childLifetime: 2.6f,
                    spawnInterval: 0.14f,
                    childSizeMin: 0.14f,
                    childSizeMax: 0.28f,
                    sizeGrowth: 0.2f,
                    childJitter: 0.025f,
                    childRiseMin: 0.015f,
                    childRiseMax: 0.05f,
                    childSpread: 0.03f,
                    lateralCarrierOffset: 0f,
                    ageColor: StreamAgeColor.Brighten,
                    palette: StreamPalette.Cyan
                );
                return;
            case ParticleRecipe.FadingStream:
                SpawnCarrier(
                    node,
                    preseedSeconds,
                    carrierWindScale: 10f,
                    carrierLifetime: 2.6f,
                    childWindScale: 1.5f,
                    childLifetime: 2.6f,
                    spawnInterval: 0.14f,
                    childSizeMin: 0.14f,
                    childSizeMax: 0.28f,
                    sizeGrowth: 0.2f,
                    childJitter: 0.025f,
                    childRiseMin: 0.015f,
                    childRiseMax: 0.05f,
                    childSpread: 0.03f,
                    lateralCarrierOffset: 0f,
                    ageColor: StreamAgeColor.Fade,
                    palette: StreamPalette.Cyan
                );
                return;
            case ParticleRecipe.BraidedStream:
                SpawnCarrier(
                    node,
                    preseedSeconds,
                    8f, 2.4f, 2f, 2.4f, 0.16f,
                    0.11f, 0.22f, 0.16f, 0.018f,
                    0.01f, 0.04f, 0.025f, -0.12f,
                    StreamAgeColor.Brighten, StreamPalette.Cyan
                );
                SpawnCarrier(
                    node,
                    preseedSeconds,
                    8f, 2.4f, 2f, 2.4f, 0.16f,
                    0.11f, 0.22f, 0.16f, 0.018f,
                    0.01f, 0.04f, 0.025f, 0.12f,
                    StreamAgeColor.Fade, StreamPalette.Violet
                );
                return;
            case ParticleRecipe.PuffChain:
                SpawnCarrier(
                    node,
                    preseedSeconds,
                    carrierWindScale: 6f,
                    carrierLifetime: 3f,
                    childWindScale: 0.8f,
                    childLifetime: 3.2f,
                    spawnInterval: 0.32f,
                    childSizeMin: 0.42f,
                    childSizeMax: 0.75f,
                    sizeGrowth: 0.55f,
                    childJitter: 0.06f,
                    childRiseMin: 0.03f,
                    childRiseMax: 0.1f,
                    childSpread: 0.08f,
                    lateralCarrierOffset: 0f,
                    ageColor: StreamAgeColor.Steady,
                    palette: StreamPalette.Cyan
                );
                return;
            case ParticleRecipe.CarrierSoftWisps:
                SpawnCarrier(
                    node,
                    preseedSeconds,
                    carrierWindScale: 10f,
                    carrierLifetime: 2.6f,
                    childWindScale: 1.5f,
                    childLifetime: 1.1f,
                    spawnInterval: 0.16f,
                    childSizeMin: 0.45f,
                    childSizeMax: 0.85f,
                    sizeGrowth: 0.45f,
                    childJitter: 0.015f,
                    childRiseMin: 0.025f,
                    childRiseMax: 0.075f,
                    childSpread: 0.12f,
                    lateralCarrierOffset: 0f,
                    ageColor: StreamAgeColor.Brighten,
                    palette: StreamPalette.Cyan,
                    childVisual: CarrierChildVisual.SoftWisp
                );
                return;
            case ParticleRecipe.CarrierGlowingMotes:
                SpawnCarrier(
                    node,
                    preseedSeconds,
                    carrierWindScale: 10f,
                    carrierLifetime: 2.6f,
                    childWindScale: 1.5f,
                    childLifetime: 1.35f,
                    spawnInterval: 0.14f,
                    childSizeMin: 0.22f,
                    childSizeMax: 0.42f,
                    sizeGrowth: 0f,
                    childJitter: 0.08f,
                    childRiseMin: -0.015f,
                    childRiseMax: 0.08f,
                    childSpread: 0.18f,
                    lateralCarrierOffset: 0f,
                    ageColor: StreamAgeColor.Brighten,
                    palette: StreamPalette.Cyan,
                    childVisual: CarrierChildVisual.GlowingMote
                );
                return;
            case ParticleRecipe.CarrierWindClouds:
                SpawnCarrier(
                    node,
                    preseedSeconds,
                    carrierWindScale: 10f,
                    carrierLifetime: 2.6f,
                    childWindScale: 1.5f,
                    childLifetime: 1.6f,
                    spawnInterval: 0.28f,
                    childSizeMin: 1.25f,
                    childSizeMax: 2.1f,
                    sizeGrowth: 0.6f,
                    childJitter: 0.01f,
                    childRiseMin: 0.015f,
                    childRiseMax: 0.05f,
                    childSpread: 0.2f,
                    lateralCarrierOffset: 0f,
                    ageColor: StreamAgeColor.Brighten,
                    palette: StreamPalette.Cyan,
                    childVisual: CarrierChildVisual.WindCloud
                );
                return;
            case ParticleRecipe.CarrierGroundBeads:
                SpawnCarrier(
                    node,
                    preseedSeconds,
                    carrierWindScale: 10f,
                    carrierLifetime: 2.6f,
                    childWindScale: 1.5f,
                    childLifetime: 1.4f,
                    spawnInterval: 0.22f,
                    childSizeMin: 3.5f,
                    childSizeMax: 5.5f,
                    sizeGrowth: 0f,
                    childJitter: 0.005f,
                    childRiseMin: 0.005f,
                    childRiseMax: 0.02f,
                    childSpread: 0.08f,
                    lateralCarrierOffset: 0f,
                    ageColor: StreamAgeColor.Brighten,
                    palette: StreamPalette.Cyan,
                    childVisual: CarrierChildVisual.GroundBead
                );
                return;
            default:
                SpawnPlumeParticle(node, preseedSeconds);
                return;
        }
    }

    private void SpawnCarrier(
        ClientScentNode node,
        float preseedSeconds,
        float carrierWindScale,
        float carrierLifetime,
        float childWindScale,
        float childLifetime,
        float spawnInterval,
        float childSizeMin,
        float childSizeMax,
        float sizeGrowth,
        float childJitter,
        float childRiseMin,
        float childRiseMax,
        float childSpread,
        float lateralCarrierOffset,
        StreamAgeColor ageColor,
        StreamPalette palette,
        CarrierChildVisual childVisual = CarrierChildVisual.StreamSquare
    )
    {
        if (clientApi == null)
        {
            return;
        }

        Vec3f wind = GlobalConstants.CurrentWindSpeedClient;
        float horizontalWindLength = MathF.Sqrt(wind.X * wind.X + wind.Z * wind.Z);
        float perpendicularX = horizontalWindLength > 0.0001f ? -wind.Z / horizontalWindLength : 1f;
        float perpendicularZ = horizontalWindLength > 0.0001f ? wind.X / horizontalWindLength : 0f;
        float selectedCarrierSpeed = CarrierWindScale;
        float selectedMoteSpeed = MoteWindScale;
        Vec3f carrierVelocity = new(
            wind.X * selectedCarrierSpeed + perpendicularX * lateralCarrierOffset,
            wind.Y * selectedCarrierSpeed + 0.025f,
            wind.Z * selectedCarrierSpeed + perpendicularZ * lateralCarrierOffset
        );
        Vec3d startPosition = node.Position.AddCopy(
            carrierVelocity.X * preseedSeconds,
            0.32 + carrierVelocity.Y * preseedSeconds,
            carrierVelocity.Z * preseedSeconds
        );

        StreamPalette effectivePalette = emissionLayout == EmissionLayout.SparseHistory
            ? StreamPalette.Amber
            : palette;
        activeCarriers.Add(new ScentCarrier(
            startPosition,
            carrierVelocity,
            carrierLifetime,
            selectedMoteSpeed,
            childLifetime,
            spawnInterval,
            childSizeMin,
            childSizeMax,
            sizeGrowth,
            childJitter,
            childRiseMin,
            childRiseMax,
            childSpread,
            ageColor,
            effectivePalette,
            childVisual
        ));
    }

    private void UpdateCarriers(float dt)
    {
        if (clientApi == null || activeCarriers.Count == 0)
        {
            return;
        }

        float step = GameMath.Clamp(dt, 0f, 0.1f);
        for (int index = activeCarriers.Count - 1; index >= 0; index--)
        {
            ScentCarrier carrier = activeCarriers[index];
            carrier.Age += step;
            carrier.TimeSinceChild += step;
            Vec3d nextPosition = carrier.Position.AddCopy(
                carrier.Velocity.X * step,
                carrier.Velocity.Y * step,
                carrier.Velocity.Z * step
            );

            if (CarrierHitsBlockingTerrain(carrier.Position, nextPosition))
            {
                activeCarriers.RemoveAt(index);
                continue;
            }

            carrier.Position.Set(nextPosition);

            while (carrier.TimeSinceChild >= carrier.ChildInterval && carrier.Age < carrier.Lifetime)
            {
                carrier.TimeSinceChild -= carrier.ChildInterval;
                SpawnCarrierChild(carrier);
            }

            if (carrier.Age >= carrier.Lifetime)
            {
                activeCarriers.RemoveAt(index);
            }
        }
    }

    private bool CarrierHitsBlockingTerrain(Vec3d from, Vec3d to)
    {
        if (clientApi == null)
        {
            return false;
        }

        AABBIntersectionTest intersectionTester = clientApi.World.InteresectionTester;
        intersectionTester.LoadRayAndPos(new Line3D
        {
            Start = new[] { from.X, from.Y, from.Z },
            End = new[] { to.X, to.Y, to.Z }
        });

        return intersectionTester.GetSelectedBlock(
            (float)from.DistanceTo(to),
            IsScentBlockingBlock,
            testCollide: true
        ) != null;
    }

    private bool IsScentBlockingBlock(BlockPos position, Block block)
    {
        if (clientApi == null
            || block.BlockMaterial is EnumBlockMaterial.Leaves or EnumBlockMaterial.Plant
            || block.Replaceable >= 3000)
        {
            return false;
        }

        Cuboidf[]? collisionBoxes = block.GetCollisionBoxes(clientApi.World.BlockAccessor, position);
        return collisionBoxes is { Length: > 0 };
    }

    private void SpawnCarrierChild(ScentCarrier carrier)
    {
        if (clientApi == null)
        {
            return;
        }

        Random random = clientApi.World.Rand;
        float ageFraction = GameMath.Clamp(carrier.Age / carrier.Lifetime, 0f, 1f);
        (int baseRed, int baseGreen, int baseBlue) = carrier.Palette switch
        {
            StreamPalette.Violet => (198, 145, 255),
            StreamPalette.Amber => (245, 174, 72),
            _ => (118, 215, 245)
        };

        float intensity;
        int alpha;
        switch (carrier.AgeColor)
        {
            case StreamAgeColor.Brighten:
                intensity = 0.45f + ageFraction * 0.65f;
                alpha = (int)(55 + ageFraction * 185);
                break;
            case StreamAgeColor.Fade:
                intensity = 1.05f - ageFraction * 0.55f;
                alpha = (int)(230 - ageFraction * 155);
                break;
            default:
                intensity = 0.85f;
                alpha = 145;
                break;
        }

        float opacityMultiplier = carrier.ChildVisual switch
        {
            CarrierChildVisual.SoftWisp => 0.55f,
            CarrierChildVisual.WindCloud => 0.22f,
            _ => 1f
        };
        alpha = GameMath.Clamp((int)(alpha * opacityMultiplier * scentOpacityMultiplier), 1, 255);

        int color = ColorUtil.ToRgba(
            alpha,
            GameMath.Clamp((int)(baseRed * intensity), 0, 255),
            GameMath.Clamp((int)(baseGreen * intensity), 0, 255),
            GameMath.Clamp((int)(baseBlue * intensity), 0, 255)
        );
        Vec3f wind = GlobalConstants.CurrentWindSpeedClient;
        Vec3f childVelocity = new(
            wind.X * carrier.ChildWindScale + ((float)random.NextDouble() * 2 - 1) * carrier.ChildJitter,
            wind.Y * carrier.ChildWindScale + carrier.ChildRiseMin
                + (float)random.NextDouble() * (carrier.ChildRiseMax - carrier.ChildRiseMin),
            wind.Z * carrier.ChildWindScale + ((float)random.NextDouble() * 2 - 1) * carrier.ChildJitter
        );
        Vec3d childPosition = carrier.Position.AddCopy(
            (random.NextDouble() * 2 - 1) * carrier.ChildSpread,
            random.NextDouble() * carrier.ChildSpread,
            (random.NextDouble() * 2 - 1) * carrier.ChildSpread
        );
        float baseSize = carrier.ChildSizeMin
            + (float)random.NextDouble() * (carrier.ChildSizeMax - carrier.ChildSizeMin);
        float size = baseSize * scentSizeMultiplier * (0.85f + ageFraction * 0.3f);

        EnumParticleModel particleModel = carrier.ChildVisual == CarrierChildVisual.GroundBead
            ? EnumParticleModel.Cube
            : EnumParticleModel.Quad;
        EvolvingNatFloat opacityEvolve = carrier.ChildVisual switch
        {
            CarrierChildVisual.SoftWisp or CarrierChildVisual.GlowingMote or CarrierChildVisual.WindCloud
                => EvolvingNatFloat.create(EnumTransformFunction.CLAMPEDPOSITIVESINUS, GameMath.PI),
            CarrierChildVisual.GroundBead
                => EvolvingNatFloat.create(EnumTransformFunction.QUADRATIC, -12f),
            _ => EvolvingNatFloat.create(EnumTransformFunction.QUADRATIC, -8f)
        };
        int vertexFlags = carrier.ChildVisual switch
        {
            CarrierChildVisual.SoftWisp => 120,
            CarrierChildVisual.WindCloud => 80,
            CarrierChildVisual.GlowingMote or CarrierChildVisual.GroundBead => 255,
            _ => 180
        };
        int lightEmission = carrier.ChildVisual is CarrierChildVisual.GlowingMote or CarrierChildVisual.GroundBead
            ? ColorUtil.ToRgba(
                255,
                (int)(80 * scentLightMultiplier),
                (int)(160 * scentLightMultiplier),
                (int)(185 * scentLightMultiplier)
            )
            : 0;

        SimpleParticleProperties particles = new(
            1f,
            1f,
            color,
            childPosition,
            childPosition,
            childVelocity,
            childVelocity.Clone(),
            carrier.ChildLifetime,
            0f,
            size,
            size,
            particleModel
        )
        {
            IgnoreUserConfig = true,
            WithTerrainCollision = true,
            VertexFlags = vertexFlags,
            LightEmission = lightEmission,
            RandomVelocityChange = carrier.ChildVisual == CarrierChildVisual.GlowingMote,
            OpacityEvolve = opacityEvolve,
            SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, carrier.SizeGrowth)
        };

        clientApi.World.SpawnParticles(particles);
    }

    private void SpawnPlumeParticle(ClientScentNode node, float preseedSeconds)
    {
        if (clientApi == null)
        {
            return;
        }

        PlumeProfile profile = BalancedProfile();
        Vec3f wind = GlobalConstants.CurrentWindSpeedClient;
        Vec3f baseVelocity = new(
            wind.X * profile.WindScale,
            wind.Y * profile.WindScale,
            wind.Z * profile.WindScale
        );

        float freshness = 1f - GameMath.Clamp(node.AgeMilliseconds / (float)TrailLifetimeMs, 0f, 1f);
        int alpha = (int)((profile.MinimumAlpha + freshness * profile.AdditionalAlpha) * scentOpacityMultiplier);
        bool historicalColor = emissionLayout == EmissionLayout.SparseHistory;
        int color = historicalColor
            ? ColorUtil.ToRgba(alpha, 245, 174, 72)
            : ColorUtil.ToRgba(alpha, 118, 215, 245);

        Vec3d center = node.Position.AddCopy(
            baseVelocity.X * preseedSeconds,
            0.28 + baseVelocity.Y * preseedSeconds,
            baseVelocity.Z * preseedSeconds
        );
        Vec3d minPosition = center.AddCopy(-profile.SpawnSpread, 0, -profile.SpawnSpread);
        Vec3d maxPosition = center.AddCopy(profile.SpawnSpread, profile.VerticalSpawnSpread, profile.SpawnSpread);
        Vec3f minVelocity = new(
            baseVelocity.X - profile.HorizontalJitter,
            baseVelocity.Y + profile.MinimumRise,
            baseVelocity.Z - profile.HorizontalJitter
        );
        Vec3f maxVelocity = new(
            baseVelocity.X + profile.HorizontalJitter,
            baseVelocity.Y + profile.MaximumRise,
            baseVelocity.Z + profile.HorizontalJitter
        );

        SimpleParticleProperties particles = new(
            1f,
            1f,
            color,
            minPosition,
            maxPosition,
            minVelocity,
            maxVelocity,
            profile.Lifetime,
            0f,
            profile.MinimumSize * scentSizeMultiplier,
            profile.MaximumSize * scentSizeMultiplier,
            profile.Model
        )
        {
            IgnoreUserConfig = true,
            WithTerrainCollision = false,
            VertexFlags = profile.VertexFlags,
            LightEmission = historicalColor
                ? ColorUtil.ToRgba(255, 150, 95, 35)
                : ColorUtil.ToRgba(255, 80, 145, 165),
            OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.QUADRATIC, -8f),
            SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, profile.SizeGrowth)
        };

        clientApi.World.SpawnParticles(particles);
    }

    private static PlumeProfile BalancedProfile()
    {
        return new PlumeProfile(
            WindScale: 5f,
            Lifetime: 3.4f,
            MinimumSize: 0.22f,
            MaximumSize: 0.42f,
            SpawnSpread: 0.09,
            VerticalSpawnSpread: 0.24,
            HorizontalJitter: 0.018f,
            MinimumRise: 0.012f,
            MaximumRise: 0.045f,
            MinimumAlpha: 70,
            AdditionalAlpha: 105,
            SizeGrowth: 0.3f,
            VertexFlags: 180,
            Model: EnumParticleModel.Quad
        );
    }

    private static string LayoutCode(EmissionLayout layout)
    {
        return layout switch
        {
            EmissionLayout.RecentCluster => "recent-cluster",
            EmissionLayout.SparseHistory => "sparse-history",
            _ => "latest-only"
        };
    }

    private static string RecipeCode(ParticleRecipe recipe)
    {
        return recipe switch
        {
            ParticleRecipe.BrighteningStream => "brightening-stream",
            ParticleRecipe.FadingStream => "fading-stream",
            ParticleRecipe.BraidedStream => "braided-stream",
            ParticleRecipe.PuffChain => "puff-chain",
            ParticleRecipe.CarrierSoftWisps => "carrier-soft-wisps",
            ParticleRecipe.CarrierGlowingMotes => "carrier-glowing-motes",
            ParticleRecipe.CarrierWindClouds => "carrier-wind-clouds",
            ParticleRecipe.CarrierGroundBeads => "carrier-ground-beads",
            _ => "baseline-plume"
        };
    }

    private static double SquareDistance(double x, double y, double z, Vec3d other)
    {
        double dx = x - other.X;
        double dy = y - other.Y;
        double dz = z - other.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    private sealed class TrailState
    {
        public TrailState(Vec3d position, int dimension, long nowMs)
        {
            LastDepositPosition = new Vec3d(position.X, position.Y, position.Z);
            Dimension = dimension;
            LastSeenMs = nowMs;
        }

        public Vec3d LastDepositPosition { get; }
        public int Dimension { get; set; }
        public long LastSeenMs { get; set; }
        public Queue<ScentNode> Nodes { get; } = new();
    }

    private readonly record struct ScentNode(
        double X,
        double Y,
        double Z,
        int Dimension,
        long CreatedAtMs,
        long TrailId
    );

    private readonly record struct ClientScentNode(Vec3d Position, int AgeMilliseconds, long TrailId);

    private readonly record struct PlumeProfile(
        float WindScale,
        float Lifetime,
        float MinimumSize,
        float MaximumSize,
        double SpawnSpread,
        double VerticalSpawnSpread,
        float HorizontalJitter,
        float MinimumRise,
        float MaximumRise,
        int MinimumAlpha,
        int AdditionalAlpha,
        float SizeGrowth,
        int VertexFlags,
        EnumParticleModel Model
    );

    private sealed class ScentCarrier
    {
        public ScentCarrier(
            Vec3d position,
            Vec3f velocity,
            float lifetime,
            float childWindScale,
            float childLifetime,
            float childInterval,
            float childSizeMin,
            float childSizeMax,
            float sizeGrowth,
            float childJitter,
            float childRiseMin,
            float childRiseMax,
            float childSpread,
            StreamAgeColor ageColor,
            StreamPalette palette,
            CarrierChildVisual childVisual
        )
        {
            Position = position;
            Velocity = velocity;
            Lifetime = lifetime;
            ChildWindScale = childWindScale;
            ChildLifetime = childLifetime;
            ChildInterval = childInterval;
            ChildSizeMin = childSizeMin;
            ChildSizeMax = childSizeMax;
            SizeGrowth = sizeGrowth;
            ChildJitter = childJitter;
            ChildRiseMin = childRiseMin;
            ChildRiseMax = childRiseMax;
            ChildSpread = childSpread;
            AgeColor = ageColor;
            Palette = palette;
            ChildVisual = childVisual;
        }

        public Vec3d Position { get; }
        public Vec3f Velocity { get; }
        public float Lifetime { get; }
        public float ChildWindScale { get; }
        public float ChildLifetime { get; }
        public float ChildInterval { get; }
        public float ChildSizeMin { get; }
        public float ChildSizeMax { get; }
        public float SizeGrowth { get; }
        public float ChildJitter { get; }
        public float ChildRiseMin { get; }
        public float ChildRiseMax { get; }
        public float ChildSpread { get; }
        public StreamAgeColor AgeColor { get; }
        public StreamPalette Palette { get; }
        public CarrierChildVisual ChildVisual { get; }
        public float Age { get; set; }
        public float TimeSinceChild { get; set; }
    }

    private enum StreamAgeColor
    {
        Brighten,
        Fade,
        Steady
    }

    private enum StreamPalette
    {
        Cyan,
        Violet,
        Amber
    }

    private enum CarrierChildVisual
    {
        StreamSquare,
        SoftWisp,
        GlowingMote,
        WindCloud,
        GroundBead
    }

    private enum EmissionLayout
    {
        LatestOnly,
        RecentCluster,
        SparseHistory
    }

    private enum ParticleRecipe
    {
        BaselinePlume,
        BrighteningStream,
        FadingStream,
        BraidedStream,
        PuffChain,
        CarrierSoftWisps,
        CarrierGlowingMotes,
        CarrierWindClouds,
        CarrierGroundBeads
    }
}
