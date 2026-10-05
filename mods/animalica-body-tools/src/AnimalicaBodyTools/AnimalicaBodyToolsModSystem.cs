using System;
using AnimalicaBodyTools.Config;
using AnimalicaBodyTools.HarmonyPatches;
using AnimalicaBodyTools.Items;
using AnimalicaBodyTools.Rendering;
using AnimalicaBodyTools.Systems;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using VintageStoryConfigMigration;

namespace AnimalicaBodyTools;

/// <summary>
/// Animalica body tools:
/// - Trait Restrictions-backed permissions on the legacy Animal Body Tools trait
/// - small crafting and virtual-wearable bridges
/// - action-scoped wearable empty-hand virtual tools
/// </summary>
public sealed class AnimalicaBodyToolsModSystem : ModSystem
{
    private const string ConfigFileName = "AnimalicaBodyTools.json";
    private const string NetworkChannelName = "animalicabodytools:config";

    private AnimalicaBodyToolsConfig config = new();
    private ICoreClientAPI? clientApi;
    private IClientNetworkChannel? clientChannel;
    private ICoreServerAPI? serverApi;
    private IServerNetworkChannel? serverChannel;
    private HarmonyPatchBootstrap? harmonyPatchBootstrap;
    private bool permissionAssetsFinalized;
    private WearableAxeBreakBridgeSystem? wearableAxeBreakBridgeSystem;
    private WearableScytheBreakBridgeSystem? wearableScytheBreakBridgeSystem;
    private WearableProspectingPickBreakBridgeSystem? wearableProspectingPickBreakBridgeSystem;
    private WearableMiningBreakBridgeSystem? wearableMiningBreakBridgeSystem;
    private WearableCultivatingClawsServerSystem? wearableCultivatingClawsServerSystem;

    public override void Start(ICoreAPI api)
    {
        config = LoadConfig(api);
        string normalizedMode = AnimalicaBodyToolsPolicy.Apply(config.RestrictionMode, config.DiagnosticLogging);
        if (!string.Equals(config.RestrictionMode, normalizedMode, StringComparison.Ordinal))
        {
            config.RestrictionMode = normalizedMode;
            ConfigDefaults.StorePreservingUnknown(api, ConfigFileName, config);
        }

        api.Logger.Notification(
            "[animalicabodytools] Restriction mode: {0}. Options are Default, Light, Disabled.",
            normalizedMode
        );

        api.RegisterItemClass("animalicabodytools.specieslockedbodytool", typeof(ItemSpeciesLockedBodyTool));
        api.RegisterItemClass("animalicabodytools.specieslockedclaws", typeof(ItemSpeciesLockedClaws));
        api.RegisterItemClass("animalicabodytools.specieslockedrendingclaws", typeof(ItemSpeciesLockedRendingClaws));
        api.RegisterItemClass("animalicabodytools.specieslockedscythe", typeof(ItemSpeciesLockedScythe));
        api.RegisterItemClass("animalicabodytools.specieslockedprospectingpick", typeof(ItemSpeciesLockedProspectingPick));
        api.RegisterItemClass("animalicabodytools.specieslockedcultivatingclaws", typeof(ItemSpeciesLockedCultivatingClaws));
        api.RegisterCollectibleBehaviorClass(
            "animalicabodytools.readablebodytoolicon",
            typeof(CollectibleBehaviorReadableBodyToolIcon)
        );

        WearableToolResolver.Configure(api);
        MaterialDisplayNameResolver.Configure(api);

        harmonyPatchBootstrap = new HarmonyPatchBootstrap(api);
        harmonyPatchBootstrap.Start();
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        clientApi = api;
        api.Event.LevelFinalize += ApplyClientMaterialStats;
        clientChannel = api.Network.RegisterChannel(NetworkChannelName)
            .RegisterMessageType<AnimalicaBodyToolsConfigPacket>()
            .RegisterMessageType<CultivatingClawsPacket>();
        clientChannel.SetMessageHandler<AnimalicaBodyToolsConfigPacket>(OnServerConfigReceived);
        WearableCultivatingClawsClientAction.Configure(api, clientChannel);
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        AnimalClassPowerSystem.Apply(api);
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        TraitRestrictionsPermissionConfigSystem.Apply(api);
        permissionAssetsFinalized = true;
        CompatibilityModelPatchSystem.Apply(api);
        if (api.Side == EnumAppSide.Server) MaterialToolStatSystem.Apply(api);
    }

    private void ApplyClientMaterialStats()
    {
        if (clientApi != null) MaterialToolStatSystem.Apply(clientApi);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        serverApi = api;
        serverChannel = api.Network.RegisterChannel(NetworkChannelName)
            .RegisterMessageType<AnimalicaBodyToolsConfigPacket>()
            .RegisterMessageType<CultivatingClawsPacket>();
        wearableCultivatingClawsServerSystem = new WearableCultivatingClawsServerSystem(api);
        serverChannel.SetMessageHandler<CultivatingClawsPacket>(wearableCultivatingClawsServerSystem.HandlePacket);
        api.Event.PlayerNowPlaying += SendServerConfig;

        wearableAxeBreakBridgeSystem = new WearableAxeBreakBridgeSystem(api);
        wearableAxeBreakBridgeSystem.Start();

        wearableScytheBreakBridgeSystem = new WearableScytheBreakBridgeSystem(api);
        wearableScytheBreakBridgeSystem.Start();

        wearableProspectingPickBreakBridgeSystem = new WearableProspectingPickBreakBridgeSystem(api);
        wearableProspectingPickBreakBridgeSystem.Start();

        // Register after the specialized bridges so ordinary pickaxe breaks
        // are handled here while their custom multi-break paths stay intact.
        wearableMiningBreakBridgeSystem = new WearableMiningBreakBridgeSystem(api);
        wearableMiningBreakBridgeSystem.Start();
    }

    public override void Dispose()
    {
        if (clientApi != null) clientApi.Event.LevelFinalize -= ApplyClientMaterialStats;
        if (serverApi != null)
        {
            serverApi.Event.PlayerNowPlaying -= SendServerConfig;
        }

        clientApi = null;
        permissionAssetsFinalized = false;
        clientChannel = null;
        WearableCultivatingClawsClientAction.Reset();
        serverApi = null;
        serverChannel = null;
        wearableCultivatingClawsServerSystem?.Stop();
        wearableProspectingPickBreakBridgeSystem?.Stop();
        wearableScytheBreakBridgeSystem?.Stop();
        wearableAxeBreakBridgeSystem?.Stop();
        wearableMiningBreakBridgeSystem?.Stop();
        harmonyPatchBootstrap?.Dispose();
        MaterialDisplayNameResolver.Reset();
        base.Dispose();
    }

    private void SendServerConfig(IServerPlayer player)
    {
        serverChannel?.SendPacket(new AnimalicaBodyToolsConfigPacket
        {
            RestrictionMode = config.RestrictionMode,
            DiagnosticLogging = config.DiagnosticLogging
        }, player);
    }

    private void OnServerConfigReceived(AnimalicaBodyToolsConfigPacket packet)
    {
        string mode = AnimalicaBodyToolsPolicy.Apply(packet.RestrictionMode, packet.DiagnosticLogging);
        // A packet received before AssetsFinalize is picked up by the normal
        // initialization pass. Otherwise replace the already resolved local rules.
        if (clientApi != null && permissionAssetsFinalized)
        {
            try
            {
                TraitRestrictionsPermissionConfigSystem.RefreshResolvedPermissions(clientApi);
            }
            catch (Exception exception)
            {
                clientApi.Logger.Error(
                    "[animalicabodytools] Could not refresh Trait Restrictions after server mode {0}; client permissions may be stale. {1}",
                    mode, exception);
            }
        }
        clientApi?.Logger.Notification("[animalicabodytools] Applied server restriction mode: {0}.", mode);
    }

    private static AnimalicaBodyToolsConfig LoadConfig(ICoreAPI api)
    {
        try
        {
            AnimalicaBodyToolsConfig loaded = ConfigDefaults.LoadAndUpdate(
                api,
                ConfigFileName,
                () => new AnimalicaBodyToolsConfig());
            if (loaded.RefreshDescriptions())
            {
                ConfigDefaults.StorePreservingUnknown(api, ConfigFileName, loaded);
                return loaded;
            }

            return loaded;
        }
        catch (Exception exception)
        {
            api.Logger.Warning(
                "[animalicabodytools] Could not read {0}; using Default restrictions. {1}",
                ConfigFileName,
                exception.Message
            );
            return new AnimalicaBodyToolsConfig();
        }
    }
}
