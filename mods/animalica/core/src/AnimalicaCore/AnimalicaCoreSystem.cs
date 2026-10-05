using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VintageStoryConfigMigration;

namespace AnimalicaCore;

public sealed class AnimalicaCoreSystem : ModSystem
{
    private const string ConfigFileName = "AnimalicaCore.json";
    private const string NetworkChannelName = "animalicacore:config";
    private readonly Dictionary<string, AnimalicaModelBaseline> modelBaselines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, double>> coreTraitBaselines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, Dictionary<string, double>>> balancePresets = new(StringComparer.Ordinal);
    private readonly HashSet<string> coreTraitCodes = new(StringComparer.Ordinal);
    private readonly AnimalicaTraitRestrictionsFoodBridge foodBridge = new();
    private AnimalicaCoreConfig config = new();
    private ICoreClientAPI? clientApi;
    private IClientNetworkChannel? clientChannel;
    private ICoreServerAPI? serverApi;
    private IServerNetworkChannel? serverChannel;
    private Harmony? authorityHarmony;
    private Harmony? swimmingDepthHarmony;
    private object? clientModelsSystem;
    private EventInfo? clientModelsLoadedEvent;
    private Action? clientModelsLoadedHandler;
    private AnimalicaCoreConfig? receivedServerConfig;
    private static AnimalicaCoreSystem? activeInstance;

    internal bool AnimalFoodNutritionEnabled => serverApi != null
        ? config.EnableAnimalFoodNutrition
        : receivedServerConfig?.EnableAnimalFoodNutrition ?? config.EnableAnimalFoodNutrition;

    internal bool IsAnimalicaModelCode(string modelCode) => modelBaselines.ContainsKey(modelCode);

    internal static bool IsKnownAnimalicaModelCode(string? modelCode)
    {
        if (string.IsNullOrWhiteSpace(modelCode) || activeInstance == null)
        {
            return false;
        }

        if (activeInstance.modelBaselines.ContainsKey(modelCode))
        {
            return true;
        }

        // PlayerModelLib may store the selected model without its asset domain.
        return modelCode.IndexOf(':') < 0 && activeInstance.modelBaselines.Keys.Any(
            known => known.EndsWith(":" + modelCode, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool MuzzleModeEnabled => activeInstance?.config.MuzzleMode == true;
    internal static bool OldSitModeEnabled => activeInstance?.config.OldSit == true;

    public override double ExecuteOrder()
    {
        return 0.06;
    }

    public override void Start(ICoreAPI api)
    {
        activeInstance = this;
        api.RegisterEntityBehaviorClass(AnimalicaNaturalProtectionBehavior.BehaviorCode, typeof(AnimalicaNaturalProtectionBehavior));

        config = LoadConfig(api);
        string balanceMode = NormalizeAnimalBalanceMode(config.AnimalBalanceMode);
        string classPowerMode = NormalizeAnimalClassPowerMode(config.AnimalClassPowerMode);
        bool configChanged = false;
        if (!string.Equals(config.AnimalBalanceMode, balanceMode, StringComparison.Ordinal))
        {
            config.AnimalBalanceMode = balanceMode;
            configChanged = true;
        }

        if (!string.Equals(config.AnimalClassPowerMode, classPowerMode, StringComparison.Ordinal))
        {
            config.AnimalClassPowerMode = classPowerMode;
            configChanged = true;
        }

        if (configChanged)
        {
            ConfigDefaults.StorePreservingUnknown(api, ConfigFileName, config);
        }

        api.Logger.Notification("[AnimalicaCore] Animal balance mode: {0}. Options are Balanced, Distinct, Wild.", balanceMode);
        api.Logger.Notification("[AnimalicaCore] Animal class power mode: {0}. Options are Balanced, Heroic. This setting is used by Animalica Body Tools when installed.", classPowerMode);
        api.Logger.Notification("[AnimalicaCore] Default swimming depth: {0}.", config.UseDefaultSwimmingDepth ? "enabled" : "disabled");
        api.Logger.Notification("[AnimalicaCore] Approved animal-food nutrition: {0}.", config.EnableAnimalFoodNutrition ? "enabled" : "disabled");
        api.Logger.Notification("[AnimalicaCore] Muzzle mode: {0}; old sit mode: {1}.", config.MuzzleMode ? "enabled" : "disabled", config.OldSit ? "enabled" : "disabled");

        swimmingDepthHarmony = new Harmony("animalicacore.swimmingdepth");
        AnimalicaSwimmingDepthPatch.Start(swimmingDepthHarmony, ShouldUseDefaultSwimmingDepth);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        clientApi = api;
        clientChannel = api.Network.RegisterChannel(NetworkChannelName)
            .RegisterMessageType<AnimalicaConfigPacket>()
            .RegisterMessageType<AnimalicaConfigRequestPacket>()
            .SetMessageHandler<AnimalicaConfigPacket>(OnServerConfigReceived);
        SubscribeToPlayerModelsLoaded(api);
        api.Event.PlayerJoin += RequestServerConfig;
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        serverApi = api;
        serverChannel = api.Network.RegisterChannel(NetworkChannelName)
            .RegisterMessageType<AnimalicaConfigPacket>()
            .RegisterMessageType<AnimalicaConfigRequestPacket>()
            .SetMessageHandler<AnimalicaConfigRequestPacket>(OnServerConfigRequested);
        api.Event.PlayerJoin += SendServerConfig;
        api.Event.PlayerNowPlaying += SendServerConfig;

        authorityHarmony = new Harmony("animalicacore.serverauthority");
        PlayerModelLibAuthorityPatch.Start(api, authorityHarmony, modelBaselines.ContainsKey);
    }

    public override void Dispose()
    {
        if (ReferenceEquals(activeInstance, this))
        {
            activeInstance = null;
        }

        if (serverApi != null)
        {
            serverApi.Event.PlayerJoin -= SendServerConfig;
            serverApi.Event.PlayerNowPlaying -= SendServerConfig;
        }

        if (clientApi != null)
        {
            clientApi.Event.PlayerJoin -= RequestServerConfig;
        }

        if (clientModelsSystem != null && clientModelsLoadedEvent != null && clientModelsLoadedHandler != null)
        {
            clientModelsLoadedEvent.RemoveEventHandler(clientModelsSystem, clientModelsLoadedHandler);
        }

        authorityHarmony?.UnpatchAll("animalicacore.serverauthority");
        authorityHarmony = null;
        swimmingDepthHarmony?.UnpatchAll("animalicacore.swimmingdepth");
        AnimalicaSwimmingDepthPatch.Stop();
        swimmingDepthHarmony = null;
        PlayerModelLibAuthorityPatch.Stop();

        clientApi = null;
        clientChannel = null;
        clientModelsSystem = null;
        clientModelsLoadedEvent = null;
        clientModelsLoadedHandler = null;
        receivedServerConfig = null;
        serverApi = null;
        serverChannel = null;
        modelBaselines.Clear();
        coreTraitBaselines.Clear();
        balancePresets.Clear();
        coreTraitCodes.Clear();
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        foodBridge.Prepare(api);
        CaptureCoreTraitBaselines(api);
        LoadBalancePresets(api);
        RegisterAllPresetAttributeTranslations(api);

        string balanceMode = NormalizeAnimalBalanceMode(config.AnimalBalanceMode);
        int patchedTraits = ApplyAnimalBalanceMode(api, balanceMode);

        AnimalicaSizeSettings sizeSettings = AnimalicaSizePolicy.Normalize(config);
        bool shouldOverrideSizes = AnimalicaSizePolicy.ShouldOverridePackDefaults(config);

        int traitChangedFiles = 0;
        int traitChangedModels = 0;
        int sizeChangedFiles = 0;
        int sizeChangedModels = 0;

        foreach (IAsset asset in api.Assets.GetMany("config/customplayermodels", null, true))
        {
            string text = asset.ToText();
            if (string.IsNullOrWhiteSpace(text) || text.IndexOf("animalica", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            JObject root;
            try
            {
                root = JObject.Parse(text);
            }
            catch (Exception exception)
            {
                api.Logger.Warning("[AnimalicaCore] Could not read playable model config {0}: {1}", asset.Location, exception.Message);
                continue;
            }

            bool changed = false;
            bool traitsChangedInFile = false;
            bool sizesChangedInFile = false;
            foreach (JProperty property in root.Properties())
            {
                if (property.Value is not JObject model || !IsAnimalicaModel(model))
                {
                    continue;
                }

                JToken? originalTraits = model["ExtraTraits"]?.DeepClone();
                foodBridge.CombineFoodTraits(model);
                CaptureModelBaseline($"{asset.Location.Domain}:{property.Name}", model);
                ApplyRaceTraitSetting(model, config.EnableRaceTraits, config.EnableAnimalFoodNutrition);
                if (!JToken.DeepEquals(originalTraits, model["ExtraTraits"]))
                {
                    changed = true;
                    traitsChangedInFile = true;
                    traitChangedModels++;
                }

                if (shouldOverrideSizes && ApplySizeSettings(model, sizeSettings))
                {
                    changed = true;
                    sizesChangedInFile = true;
                    sizeChangedModels++;
                }
            }

            if (!changed)
            {
                continue;
            }

            asset.Data = Encoding.UTF8.GetBytes(root.ToString(Formatting.Indented));
            asset.IsPatched = true;
            if (traitsChangedInFile)
            {
                traitChangedFiles++;
            }

            if (sizesChangedInFile)
            {
                sizeChangedFiles++;
            }

        }

        if (config.EnableRaceTraits)
        {
            api.Logger.Notification(
                "[AnimalicaCore] Race traits are enabled for {0} Animalica model entries.",
                modelBaselines.Count
            );
        }
        else
        {
            api.Logger.Notification(
                "[AnimalicaCore] Race traits are disabled by config. Removed traits from {0} Animalica model entries across {1} config file(s).",
                traitChangedModels,
                traitChangedFiles
            );
        }

        if (shouldOverrideSizes)
        {
            api.Logger.Notification(
                "[AnimalicaCore] Size overrides are enabled. Applied min {0:0.###}, max {1:0.###}, default {2:0.###} to {3} Animalica model entries across {4} config file(s).",
                sizeSettings.Minimum,
                sizeSettings.Maximum,
                sizeSettings.Default,
                sizeChangedModels,
                sizeChangedFiles
            );
        }
        else
        {
            api.Logger.Notification(config.EnableSizeOverrides
                ? "[AnimalicaCore] Pack-default size settings are already baked into model assets."
                : "[AnimalicaCore] Size overrides are disabled.");
        }

        LogAnimalBalanceMode(api, balanceMode, patchedTraits);
    }

    private void SendServerConfig(IServerPlayer player)
    {
        PlayerModelLibAuthorityPatch.EnforceCurrentPlayer(serverApi, player);
        serverChannel?.SendPacket(AnimalicaConfigPacket.FromConfig(config), player);
    }

    private void OnServerConfigRequested(IServerPlayer player, AnimalicaConfigRequestPacket packet)
    {
        if (packet.ProtocolVersion == 1)
        {
            SendServerConfig(player);
        }
    }

    private void RequestServerConfig(IClientPlayer player)
    {
        if (clientApi?.World.Player?.PlayerUID == player.PlayerUID && clientChannel?.Connected == true)
        {
            clientChannel.SendPacket(new AnimalicaConfigRequestPacket());
        }
    }

    private void OnServerConfigReceived(AnimalicaConfigPacket packet)
    {
        if (clientApi == null)
        {
            return;
        }

        AnimalicaCoreConfig serverConfig = packet.ToConfig();
        serverConfig.KeepSeraphEyeHeight = config.KeepSeraphEyeHeight;
        serverConfig.AnimalBalanceMode = NormalizeAnimalBalanceMode(serverConfig.AnimalBalanceMode);
        serverConfig.AnimalClassPowerMode = NormalizeAnimalClassPowerMode(serverConfig.AnimalClassPowerMode);
        if (receivedServerConfig != null && HaveSameServerSettings(receivedServerConfig, serverConfig))
        {
            return;
        }

        receivedServerConfig = serverConfig;
        int changedTraits = ApplyRuntimeAnimalBalanceMode(clientApi, serverConfig.AnimalBalanceMode);
        int changedModels = ApplyRuntimeModelPolicy(clientApi, serverConfig);
        int eyeHeightModels = ApplyLocalEyeHeightPolicy(clientApi);
        config = serverConfig;
        RefreshPlayerTraitStats(clientApi);
        if (eyeHeightModels > 0)
        {
            AnimalicaRuntimeConfigApplier.RefreshLocalPlayerModel(clientApi);
        }
        clientApi.Logger.Notification(
            "[AnimalicaCore] Applied server-authoritative settings to {0} loaded Animalica model(s) and {1} trait definition(s). Traits: {2}; size overrides: {3}; balance: {4}; class power: {5}; local Seraph eye height: {6}.",
            changedModels,
            changedTraits,
            config.EnableRaceTraits ? "enabled" : "disabled",
            config.EnableSizeOverrides ? "enabled" : "disabled",
            config.AnimalBalanceMode,
            config.AnimalClassPowerMode,
            config.KeepSeraphEyeHeight ? $"enabled for {eyeHeightModels} model(s)" : "disabled"
        );
    }

    private void SubscribeToPlayerModelsLoaded(ICoreClientAPI api)
    {
        if (clientModelsLoadedHandler != null)
        {
            return;
        }

        object? modelsSystem = api.ModLoader.GetModSystem("PlayerModelLib.CustomModelsSystem");
        EventInfo? loadedEvent = modelsSystem?.GetType()
            .GetEvent("OnCustomModelsLoaded", BindingFlags.Public | BindingFlags.Instance);
        if (modelsSystem == null || loadedEvent == null)
        {
            api.Logger.Warning("[AnimalicaCore] Could not subscribe to Player Model Lib's model-loaded event.");
            return;
        }

        Action handler = OnPlayerModelsLoaded;
        loadedEvent.AddEventHandler(modelsSystem, handler);
        clientModelsSystem = modelsSystem;
        clientModelsLoadedEvent = loadedEvent;
        clientModelsLoadedHandler = handler;

        bool modelsAlreadyLoaded = modelsSystem.GetType()
            .GetProperty("ModelsLoaded", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(modelsSystem) as bool? == true;
        if (modelsAlreadyLoaded)
        {
            OnPlayerModelsLoaded();
        }
    }

    private void OnPlayerModelsLoaded()
    {
        if (clientApi == null || receivedServerConfig == null)
        {
            return;
        }

        int changedModels = ApplyRuntimeModelPolicy(clientApi, receivedServerConfig);
        int eyeHeightModels = ApplyLocalEyeHeightPolicy(clientApi);
        RefreshPlayerTraitStats(clientApi);
        if (eyeHeightModels > 0)
        {
            AnimalicaRuntimeConfigApplier.RefreshLocalPlayerModel(clientApi);
        }
        clientApi.Logger.Notification(
            "[AnimalicaCore] Reapplied server-authoritative traits and sizes to {0} Animalica model(s) after Player Model Lib finished loading. Local Seraph eye height: {1}.",
            changedModels,
            config.KeepSeraphEyeHeight ? $"enabled for {eyeHeightModels} model(s)" : "disabled"
        );
    }

    private int ApplyRuntimeModelPolicy(ICoreClientAPI api, AnimalicaCoreConfig serverConfig)
    {
        return AnimalicaRuntimeConfigApplier.Apply(
            api,
            serverConfig,
            modelBaselines,
            coreTraitCodes
        );
    }

    private int ApplyLocalEyeHeightPolicy(ICoreClientAPI api)
    {
        return config.KeepSeraphEyeHeight
            ? AnimalicaRuntimeConfigApplier.ApplySeraphEyeHeight(api, modelBaselines)
            : 0;
    }

    private bool ShouldUseDefaultSwimmingDepth(Entity entity)
    {
        if (!config.UseDefaultSwimmingDepth || entity is not EntityPlayer)
        {
            return false;
        }

        string modelCode = entity.WatchedAttributes.GetString("skinModel", "seraph");
        return modelBaselines.ContainsKey(modelCode);
    }

    private static AnimalicaCoreConfig LoadConfig(ICoreAPI api)
    {
        try
        {
            AnimalicaCoreConfig loaded = ConfigDefaults.LoadAndUpdate(
                api,
                ConfigFileName,
                () => new AnimalicaCoreConfig());
            const string oldFoodDescription = "Server-authoritative switch for Core's approved normally-inedible animal foods. When disabled, those items use their vanilla non-edible behavior instead of the incompatible 1-satiety fallback.";
            if (string.Equals(loaded.EnableAnimalFoodNutritionDescription, oldFoodDescription, StringComparison.Ordinal))
            {
                loaded.EnableAnimalFoodNutritionDescription = new AnimalicaCoreConfig().EnableAnimalFoodNutritionDescription;
                ConfigDefaults.StorePreservingUnknown(api, ConfigFileName, loaded);
            }

            return loaded;
        }
        catch (Exception exception)
        {
            api.Logger.Warning("[AnimalicaCore] Could not read {0}; using defaults. {1}", ConfigFileName, exception.Message);
            return new AnimalicaCoreConfig();
        }
    }

    internal static bool IsAnimalicaModel(JObject model)
    {
        string group = model.Value<string>("Group") ?? string.Empty;
        if (group.StartsWith("animalica", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (model["AddTags"] is not JArray tags)
        {
            return false;
        }

        foreach (JToken tag in tags)
        {
            if (string.Equals(tag.Value<string>(), "animalica", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void CaptureModelBaseline(string modelCode, JObject model)
    {
        string[] traits = model["ExtraTraits"] is JArray traitArray
            ? traitArray.Values<string>()
                .Where(value => value != null && coreTraitCodes.Contains(value))
                .Cast<string>()
                .ToArray()
            : Array.Empty<string>();
        JArray sizeRange = model["SizeRange"] as JArray ?? new JArray(0.5, 3.0);
        double minimum = sizeRange.Count > 0 ? sizeRange[0]!.Value<double>() : 0.5;
        double maximum = sizeRange.Count > 1 ? sizeRange[1]!.Value<double>() : 3.0;
        double defaultSize = model.Value<double?>("ModelSizeFactor") ?? 1.0;
        modelBaselines.TryAdd(modelCode, new AnimalicaModelBaseline(traits, minimum, maximum, defaultSize));
    }

    private bool ApplyRaceTraitSetting(JObject model, bool enabled, bool foodEnabled)
    {
        if (model["ExtraTraits"] is not JArray traits)
        {
            return false;
        }

        JArray desired = new();
        foreach (JToken trait in traits)
        {
            string? code = trait.Value<string>();
            if (code != null
                && (enabled || !coreTraitCodes.Contains(code))
                && (foodEnabled || !AnimalicaTraitRestrictionsFoodBridge.IsFoodAccessTrait(code)))
            {
                desired.Add(code);
            }
        }

        if (JToken.DeepEquals(traits, desired))
        {
            return false;
        }

        model["ExtraTraits"] = desired;
        return true;
    }

    private static bool ApplySizeSettings(JObject model, AnimalicaSizeSettings settings)
    {
        bool changed = false;
        JArray sizeRange = new(settings.Minimum, settings.Maximum);
        if (!JToken.DeepEquals(model["SizeRange"], sizeRange))
        {
            model["SizeRange"] = sizeRange;
            changed = true;
        }

        JValue defaultSize = new(settings.Default);
        if (!JToken.DeepEquals(model["ModelSizeFactor"], defaultSize))
        {
            model["ModelSizeFactor"] = defaultSize;
            changed = true;
        }

        return changed;
    }

    private static string NormalizeAnimalBalanceMode(string? mode)
    {
        if (string.Equals(mode, "Distinct", StringComparison.OrdinalIgnoreCase))
        {
            return "Distinct";
        }

        if (string.Equals(mode, "Wild", StringComparison.OrdinalIgnoreCase))
        {
            return "Wild";
        }

        return "Balanced";
    }

    private static string NormalizeAnimalClassPowerMode(string? mode)
    {
        if (string.Equals(mode, "Heroic", StringComparison.OrdinalIgnoreCase))
        {
            return "Heroic";
        }

        return "Balanced";
    }

    private static bool HaveSameServerSettings(AnimalicaCoreConfig left, AnimalicaCoreConfig right)
    {
        return left.EnableRaceTraits == right.EnableRaceTraits
            && left.EnableSizeOverrides == right.EnableSizeOverrides
            && left.SizeMinimum.Equals(right.SizeMinimum)
            && left.SizeMaximum.Equals(right.SizeMaximum)
            && left.DefaultSize.Equals(right.DefaultSize)
            && left.UseDefaultSwimmingDepth == right.UseDefaultSwimmingDepth
            && string.Equals(left.AnimalBalanceMode, right.AnimalBalanceMode, StringComparison.Ordinal)
            && string.Equals(left.AnimalClassPowerMode, right.AnimalClassPowerMode, StringComparison.Ordinal)
            && left.EnableAnimalFoodNutrition == right.EnableAnimalFoodNutrition
            && left.MuzzleMode == right.MuzzleMode
            && left.OldSit == right.OldSit
            && left.KeepSeraphEyeHeight == right.KeepSeraphEyeHeight;
    }

    private void CaptureCoreTraitBaselines(ICoreAPI api)
    {
        coreTraitBaselines.Clear();
        coreTraitCodes.Clear();
        IAsset? traitAsset = api.Assets.TryGet(new AssetLocation("animalicacore:config/traits.json"), true);
        if (traitAsset?.Data == null || traitAsset.Data.Length == 0)
        {
            return;
        }

        try
        {
            foreach (JObject trait in JArray.Parse(Encoding.UTF8.GetString(traitAsset.Data)).OfType<JObject>())
            {
                string code = trait.Value<string>("code") ?? string.Empty;
                if (string.IsNullOrEmpty(code) || trait["attributes"] is not JObject attributes)
                {
                    continue;
                }

                coreTraitCodes.Add(code);
                coreTraitBaselines[code] = attributes.Properties().ToDictionary(
                    property => property.Name,
                    property => property.Value.Value<double>(),
                    StringComparer.Ordinal
                );
            }
        }
        catch (Exception exception)
        {
            api.Logger.Warning("[AnimalicaCore] Could not capture baseline Core traits: {0}", exception.Message);
        }
    }

    private void LoadBalancePresets(ICoreAPI api)
    {
        balancePresets.Clear();
        foreach (string mode in new[] { "Distinct", "Wild" })
        {
            string path = $"animalicacore:config/trait-presets/{mode.ToLowerInvariant()}.json";
            try
            {
                IAsset? asset = api.Assets.TryGet(new AssetLocation(path), true);
                Dictionary<string, Dictionary<string, double>>? preset = asset == null
                    ? null
                    : JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, double>>>(asset.ToText());
                if (preset != null)
                {
                    balancePresets[mode] = preset;
                }
            }
            catch (Exception exception)
            {
                api.Logger.Warning("[AnimalicaCore] Could not read trait preset {0}: {1}", path, exception.Message);
            }
        }
    }

    private Dictionary<string, double> GetDesiredCoreTraitAttributes(string code, string balanceMode)
    {
        if (!string.Equals(balanceMode, "Balanced", StringComparison.Ordinal)
            && balancePresets.TryGetValue(balanceMode, out Dictionary<string, Dictionary<string, double>>? preset)
            && preset.TryGetValue(code, out Dictionary<string, double>? attributes))
        {
            return attributes;
        }

        return coreTraitBaselines[code];
    }

    private int ApplyAnimalBalanceMode(ICoreAPI api, string balanceMode)
    {
        IAsset? traitAsset = api.Assets.TryGet(new AssetLocation("animalicacore:config/traits.json"), true);
        if (traitAsset?.Data == null || traitAsset.Data.Length == 0)
        {
            return 0;
        }

        JArray traits;
        try
        {
            traits = JArray.Parse(Encoding.UTF8.GetString(traitAsset.Data));
        }
        catch (Exception exception)
        {
            api.Logger.Warning("[AnimalicaCore] Could not read animalicacore:config/traits.json for animalBalanceMode patching: {0}", exception.Message);
            return 0;
        }

        int patched = 0;
        foreach (JObject trait in traits.OfType<JObject>())
        {
            string code = trait.Value<string>("code") ?? string.Empty;
            if (!coreTraitBaselines.ContainsKey(code))
            {
                continue;
            }

            Dictionary<string, double> attributes = GetDesiredCoreTraitAttributes(code, balanceMode);
            trait["attributes"] = ToJObject(attributes);
            patched++;
        }

        if (patched > 0)
        {
            traitAsset.Data = Encoding.UTF8.GetBytes(traits.ToString(Formatting.Indented));
            traitAsset.IsPatched = true;
            PatchTraitLang(api, balanceMode, true);
        }

        return patched;
    }

    private int ApplyRuntimeAnimalBalanceMode(ICoreClientAPI api, string balanceMode)
    {
        try
        {
            CharacterSystem characterSystem = api.ModLoader.GetModSystem<CharacterSystem>();
            int changed = 0;
            foreach (string code in coreTraitBaselines.Keys)
            {
                if (!characterSystem.TraitsByCode.TryGetValue(code, out Trait? trait))
                {
                    continue;
                }

                trait.Attributes = new Dictionary<string, double>(
                    GetDesiredCoreTraitAttributes(code, balanceMode),
                    StringComparer.Ordinal
                );
                changed++;
            }

            return changed;
        }
        catch (Exception exception)
        {
            api.Logger.Warning("[AnimalicaCore] Could not apply synchronized trait definitions: {0}", exception.Message);
            return 0;
        }
    }

    private void RegisterAllPresetAttributeTranslations(ICoreAPI api)
    {
        PatchTraitLang(api, "Balanced", false);
        foreach (string mode in balancePresets.Keys)
        {
            PatchTraitLang(api, mode, false);
        }
    }

    private void PatchTraitLang(ICoreAPI api, string balanceMode, bool updateDescriptions)
    {
        IAsset? langAsset = api.Assets.TryGet(new AssetLocation("animalicacore:lang/en.json"), true);
        if (langAsset?.Data == null || langAsset.Data.Length == 0)
        {
            return;
        }

        JObject lang;
        try
        {
            lang = JObject.Parse(Encoding.UTF8.GetString(langAsset.Data));
        }
        catch (Exception exception)
        {
            api.Logger.Warning("[AnimalicaCore] Could not read animalicacore:lang/en.json for animalBalanceMode patching: {0}", exception.Message);
            return;
        }

        foreach (string code in coreTraitBaselines.Keys)
        {
            Dictionary<string, double> attributes = GetDesiredCoreTraitAttributes(code, balanceMode);
            if (updateDescriptions)
            {
                string description = BuildTraitDescription(attributes);
                if (!string.IsNullOrEmpty(description))
                {
                    lang[$"game:traitdesc-{code}"] = description;
                    lang[$"traitdesc-{code}"] = description;
                }
            }

            foreach ((string attribute, double value) in attributes)
            {
                string key = $"charattribute-{attribute}-{FormatKeyNumber(value)}";
                string text = FormatAttributeDescription(attribute, value);
                lang[key] = text;
                lang[$"game:{key}"] = text;
            }
        }

        langAsset.Data = Encoding.UTF8.GetBytes(lang.ToString(Formatting.Indented));
        langAsset.IsPatched = true;
    }

    private static void RefreshPlayerTraitStats(ICoreClientAPI api)
    {
        EntityPlayer? entity = api.World.Player?.Entity;
        if (entity == null)
        {
            return;
        }

        string modelCode = entity.WatchedAttributes.GetString("skinModel", "seraph");
        Type? behaviorType = AccessTools.TypeByName("PlayerModelLib.PlayerSkinBehavior");
        MethodInfo? getBehavior = entity.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "GetBehavior"
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 0);
        object? behavior = behaviorType == null || getBehavior == null
            ? null
            : getBehavior.MakeGenericMethod(behaviorType).Invoke(entity, null);
        behavior?.GetType()
            .GetMethod("ApplyTraitAttributesWithModelTraits", BindingFlags.Public | BindingFlags.Instance)
            ?.Invoke(behavior, new object[] { modelCode });
    }

    private static void LogAnimalBalanceMode(ICoreAPI api, string balanceMode, int patchedTraits)
    {
        if (patchedTraits > 0)
        {
            api.Logger.Notification("[AnimalicaCore] Animal balance mode {0}: patched {1} Core trait definition(s).", balanceMode, patchedTraits);
            return;
        }

        api.Logger.Notification("[AnimalicaCore] Animal balance mode {0}: using baseline Core trait definitions.", balanceMode);
    }

    private static JObject ToJObject(Dictionary<string, double> attributes)
    {
        JObject result = new();
        foreach ((string attribute, double value) in attributes)
        {
            result[attribute] = value;
        }

        return result;
    }

    private static string BuildTraitDescription(Dictionary<string, double> attributes)
    {
        return string.Join(", ", AttributeDisplayOrder
            .Where(attributes.ContainsKey)
            .Concat(attributes.Keys.Where(key => !AttributeDisplayOrder.Contains(key)))
            .Select(key => FormatAttributeDescription(key, attributes[key]))
            .Where(text => !string.IsNullOrEmpty(text)));
    }

    private static string FormatAttributeDescription(string attribute, double value)
    {
        return attribute switch
        {
            "maxhealthExtraPoints" => FormatHealth(value),
            "animalicaNaturalProtection" => $"+{FormatDisplayNumber(value)} flat physical protection",
            "animalicaNaturalDamageReduction" => FormatPercent(value, "physical damage reduction"),
            "warmthBonus" => FormatWarmth(value),
            "sprintSpeed" => FormatPercent(value, "sprint speed"),
            "walkspeed" => FormatPercent(value, "walk speed"),
            "sneakSpeed" => FormatPercent(value, "sneak speed"),
            "backwardSpeed" => FormatPercent(value, "backward speed"),
            "swimSpeed" => FormatPercent(value, "swim speed"),
            "jumpHeightMul" => FormatPercent(value, "jump height"),
            "fallDamageFactor" => FormatPercent(value, "fall damage"),
            "frostDamageFactor" => FormatPercent(value, "frost damage"),
            "animalSeekingRange" => FormatPercent(value, "animal detection range"),
            "hungerrate" => FormatPercent(value, "hunger rate"),
            "meleeWeaponsDamage" => FormatPercent(value, "bite damage"),
            "proteinNutritionFactor" => FormatPercent(value, "protein nutrition"),
            "fruitNutritionFactor" => FormatPercent(value, "fruit nutrition"),
            "vegetableNutritionFactor" => FormatPercent(value, "vegetable nutrition"),
            "grainNutritionFactor" => FormatPercent(value, "grain nutrition"),
            "dairyNutritionFactor" => FormatPercent(value, "dairy nutrition"),
            "animalLootDropRate" => FormatPercent(value, "animal loot"),
            "animalHarvestingTime" => FormatPercent(-value, "animal harvesting speed"),
            "forageDropRate" => FormatPercent(value, "loot from foraging"),
            "vesselContentsDropRate" => FormatPercent(value, "loot from cracked vessels"),
            "rustyGearDropRate" => FormatPercent(value, "rusty gear drop rate"),
            "oreDropRate" => FormatPercent(value, "ore drop rate"),
            "miningSpeedMul" => FormatPercent(value, "mining speed"),
            _ => FormatPercent(value, attribute)
        };
    }

    private static string FormatHealth(double value)
    {
        int points = (int)Math.Round(value);
        string sign = points > 0 ? "+" : string.Empty;
        string pointWord = Math.Abs(points) == 1 ? "point" : "points";
        return $"{sign}{points} health {pointWord}";
    }

    private static string FormatWarmth(double value)
    {
        string sign = value > 0 ? "+" : string.Empty;
        return $"{sign}{FormatDisplayNumber(value)}°C bonus warmth";
    }

    private static string FormatPercent(double value, string label)
    {
        double percent = value * 100;
        string sign = percent > 0 ? "+" : string.Empty;
        return $"{sign}{FormatDisplayNumber(percent)}% {label}";
    }

    private static string FormatDisplayNumber(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string FormatKeyNumber(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static readonly string[] AttributeDisplayOrder =
    [
        "maxhealthExtraPoints",
        "animalicaNaturalDamageReduction",
        "animalicaNaturalProtection",
        "warmthBonus",
        "frostDamageFactor",
        "walkspeed",
        "sprintSpeed",
        "sneakSpeed",
        "backwardSpeed",
        "swimSpeed",
        "jumpHeightMul",
        "fallDamageFactor",
        "animalSeekingRange",
        "meleeWeaponsDamage",
        "proteinNutritionFactor",
        "fruitNutritionFactor",
        "vegetableNutritionFactor",
        "grainNutritionFactor",
        "dairyNutritionFactor",
        "forageDropRate",
        "vesselContentsDropRate",
        "rustyGearDropRate",
        "animalLootDropRate",
        "animalHarvestingTime",
        "oreDropRate",
        "miningSpeedMul",
        "hungerrate"
    ];

}
