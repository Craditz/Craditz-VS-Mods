using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;
using VintageStoryConfigMigration;

namespace AnimalicaCore;

/// <summary>
/// Gives Animalica first-person held items the vanilla Seraph's animation
/// motion while leaving the selected model, eye height, collision, and
/// third-person renderer untouched.
/// </summary>
public sealed class AnimalicaSeraphFirstPersonSystem : ModSystem
{
    public const string PositioningHotkeyCode = "animalicacore-firstperson-positioning-f9";

    private const string HarmonyId = "craditz.animalicacore.seraphfirstperson";
    private const string CoreHandsHarmonyId = "craditz.animalicacore.firstpersonhands";
    private const string ConfigFileName = "AnimalicaFirstPerson.json";
    private const string LegacyConfigFileName = "AnimalicaFirstPersonSeraphTest.json";
    private const string LegacyModId = "animalicafirstpersonseraphtest";
    private static readonly FieldInfo? EntityField = AccessTools.Field(typeof(EntityRenderer), "entity");
    private static readonly FieldInfo? RenderModeField = AccessTools.Field(typeof(EntityPlayerShapeRenderer), "renderMode");
    private static readonly MethodInfo? RenderItemMethod = AccessTools.Method(typeof(EntityShapeRenderer), "RenderItem");
    private static readonly string[] AnimalModelTokens =
    {
        "feralfox", "foxsocks", "feralwolf", "spottedhyena", "feralhyena",
        "blackbear", "brownbear", "polarbear", "giantpanda", "sunbear",
        "feraldeer-caribou", "deer-", "gazelle-thomson", "goat-", "pig-", "sheep-",
        "feralfish", "chicken-", "hare-", "feralraccoon", "cats-",
        "pantherinae-", "sirenia-", "caninae-", "capreolinae-",
        "casuariidae-", "machairodontinae-", "vombatidae-", "elephantidae-",
        "felinae-", "spheniscidae-", "dinornithidae-", "manidae-",
        "rhinocerotidae-", "viverridae-", "bovinae-", "chelonioidea-",
        "thylacinidae-", "iniidae-", "cervinae-", "meiolaniidae-",
        "dinosaur-", "hieronymus-", "feverstone-", "jimothy-",
        "draconis-", "cuprocaudus", "equus-", "ferus-",
        "feraldrifter", "feralshiver", "feralbowtorn", "ferallocust"
    };

    private static AnimalicaSeraphFirstPersonSystem? activeInstance;
    private readonly HashSet<string> discoveredAnimalicaModels =
        new(StringComparer.OrdinalIgnoreCase);
    private Harmony? harmony;
    private ICoreClientAPI? clientApi;
    private AnimalicaFirstPersonConfig config = new();
    private AnimalicaSeraphPoseDriver? poseDriver;
    private GuiDialogAnimalicaFirstPerson? positionDialog;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        if (api.ModLoader.IsModEnabled(LegacyModId))
        {
            api.Logger.Warning(
                "[AnimalicaCore] The retired {0} add-on is still installed. Core's integrated Seraph first-person driver is disabled for this session to prevent duplicate patches; remove the old add-on.",
                LegacyModId
            );
            return;
        }

        clientApi = api;
        config = LoadConfig(api);
        NormalizeConfig();
        ConfigDefaults.StorePreservingUnknown(api, ConfigFileName, config);
        if (!config.Enabled)
        {
            api.Logger.Notification(
                "[AnimalicaCore] Seraph first-person animations are disabled; optional animation render hooks were not installed."
            );
            return;
        }

        DiscoverAnimalicaModels(api);
        poseDriver = new AnimalicaSeraphPoseDriver(api, config);
        positionDialog = new GuiDialogAnimalicaFirstPerson(api, this);
        api.Input.RegisterHotKey(
            PositioningHotkeyCode,
            "Animalica first-person positioning",
            GlKeys.F9,
            HotkeyType.GUIOrOtherControls
        );
        api.Input.SetHotKeyHandler(PositioningHotkeyCode, OnTogglePositioningHotkey);
        activeInstance = this;

        MethodInfo? renderPlayerTarget = AccessTools.Method(
            typeof(EntityPlayerShapeRenderer),
            nameof(EntityPlayerShapeRenderer.DoRender3DOpaque),
            new[] { typeof(float), typeof(bool) }
        );
        MethodInfo? renderPlayerPrefix = AccessTools.Method(
            typeof(AnimalicaSeraphFirstPersonSystem),
            nameof(BeforeRenderPlayerOpaque)
        );
        MethodInfo? renderItemPrefix = AccessTools.Method(
            typeof(AnimalicaSeraphFirstPersonSystem),
            nameof(BeforeRenderItem)
        );

        if (renderPlayerTarget == null
            || renderPlayerPrefix == null
            || renderItemPrefix == null
            || RenderItemMethod == null
            || EntityField == null
            || RenderModeField == null)
        {
            api.Logger.Warning("[AnimalicaCore] Could not install the integrated Seraph first-person patches.");
            return;
        }

        HarmonyMethod itemPatch = new(renderItemPrefix)
        {
            after = new[] { CoreHandsHarmonyId }
        };

        harmony = new Harmony(HarmonyId);
        harmony.Patch(renderPlayerTarget, prefix: new HarmonyMethod(renderPlayerPrefix));
        harmony.Patch(RenderItemMethod, prefix: itemPatch);
        api.Logger.Notification(
            "[AnimalicaCore] Seraph first-person animations enabled in config: {0}; discovered Animalica models: {1}; parent yaw: {2} degrees; main-hand offset: ({3}, {4}, {5}); offhand offset: ({6}, {7}, {8}); press F9 to configure; config: {9}.",
            config.Enabled,
            discoveredAnimalicaModels.Count,
            config.SeraphYawDegrees,
            config.MainHandOffsetX,
            config.MainHandOffsetY,
            config.MainHandOffsetZ,
            config.OffHandOffsetX,
            config.OffHandOffsetY,
            config.OffHandOffsetZ,
            ConfigFileName
        );
    }

    public override void Dispose()
    {
        positionDialog?.TryClose();
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
        positionDialog = null;
        poseDriver = null;
        clientApi = null;
        if (ReferenceEquals(activeInstance, this))
        {
            activeInstance = null;
        }
    }

    internal AnimalicaFirstPersonConfig Config => config;

    internal void SetPositionStep(float value)
    {
        config.PositionStep = value;
        ApplyAndSaveConfig();
    }

    internal void SetHandOffsets(bool offHand, float x, float y, float z)
    {
        if (offHand)
        {
            config.OffHandOffsetX = x;
            config.OffHandOffsetY = y;
            config.OffHandOffsetZ = z;
        }
        else
        {
            config.MainHandOffsetX = x;
            config.MainHandOffsetY = y;
            config.MainHandOffsetZ = z;
        }

        ApplyAndSaveConfig();
    }

    internal void AdjustHand(bool offHand, float deltaX, float deltaY, float deltaZ)
    {
        if (offHand)
        {
            config.OffHandOffsetX += deltaX;
            config.OffHandOffsetY += deltaY;
            config.OffHandOffsetZ += deltaZ;
        }
        else
        {
            config.MainHandOffsetX += deltaX;
            config.MainHandOffsetY += deltaY;
            config.MainHandOffsetZ += deltaZ;
        }

        ApplyAndSaveConfig();
    }

    internal void ResetHand(bool offHand)
    {
        SetHandOffsets(
            offHand,
            offHand
                ? AnimalicaFirstPersonConfig.DefaultOffHandOffsetX
                : AnimalicaFirstPersonConfig.DefaultMainHandOffsetX,
            offHand
                ? AnimalicaFirstPersonConfig.DefaultOffHandOffsetY
                : AnimalicaFirstPersonConfig.DefaultMainHandOffsetY,
            offHand
                ? AnimalicaFirstPersonConfig.DefaultOffHandOffsetZ
                : AnimalicaFirstPersonConfig.DefaultMainHandOffsetZ
        );
    }

    private bool OnTogglePositioningHotkey(KeyCombination combination)
    {
        if (positionDialog == null || !config.Enabled)
        {
            return false;
        }

        if (positionDialog.IsOpened())
        {
            positionDialog.TryClose();
        }
        else
        {
            positionDialog.TryOpen();
        }

        return true;
    }

    private void ApplyAndSaveConfig()
    {
        NormalizeConfig();
        poseDriver?.UpdateTransforms(config);

        if (clientApi == null)
        {
            return;
        }

        try
        {
            ConfigDefaults.StorePreservingUnknown(clientApi, ConfigFileName, config);
        }
        catch (Exception exception)
        {
            clientApi.Logger.Warning(
                "[AnimalicaCore] Could not save {0}. {1}",
                ConfigFileName,
                exception.Message
            );
        }
    }

    private void NormalizeConfig()
    {
        config.Description = AnimalicaFirstPersonConfig.DefaultDescription;
        config.SeraphYawDegrees = Sanitize(config.SeraphYawDegrees, -90f, -360f, 360f);
        config.MainHandOffsetX = Sanitize(config.MainHandOffsetX, AnimalicaFirstPersonConfig.DefaultMainHandOffsetX, -5f, 5f);
        config.MainHandOffsetY = Sanitize(config.MainHandOffsetY, AnimalicaFirstPersonConfig.DefaultMainHandOffsetY, -5f, 5f);
        config.MainHandOffsetZ = Sanitize(config.MainHandOffsetZ, AnimalicaFirstPersonConfig.DefaultMainHandOffsetZ, -5f, 5f);
        config.OffHandOffsetX = Sanitize(config.OffHandOffsetX, AnimalicaFirstPersonConfig.DefaultOffHandOffsetX, -5f, 5f);
        config.OffHandOffsetY = Sanitize(config.OffHandOffsetY, AnimalicaFirstPersonConfig.DefaultOffHandOffsetY, -5f, 5f);
        config.OffHandOffsetZ = Sanitize(config.OffHandOffsetZ, AnimalicaFirstPersonConfig.DefaultOffHandOffsetZ, -5f, 5f);
        config.PositionStep = Sanitize(config.PositionStep, AnimalicaFirstPersonConfig.DefaultPositionStep, 0.001f, 1f);
    }

    private static float Sanitize(float value, float fallback, float minimum, float maximum)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return fallback;
        }

        return Math.Clamp(value, minimum, maximum);
    }

    private static void BeforeRenderPlayerOpaque(
        EntityPlayerShapeRenderer __instance,
        float dt,
        bool isShadowPass
    )
    {
        AnimalicaSeraphFirstPersonSystem? system = activeInstance;
        if (system == null
            || !system.config.Enabled
            || isShadowPass
            || RenderModeField?.GetValue(__instance) is not RenderMode.FirstPerson
            || EntityField?.GetValue(__instance) is not Entity entity)
        {
            return;
        }

        if (system.IsAnimalicaModel(entity))
        {
            system.poseDriver?.Update(entity, dt);
        }
    }

    private static void BeforeRenderItem(
        EntityShapeRenderer __instance,
        bool isShadowPass,
        ref AttachmentPointAndPose apap
    )
    {
        AnimalicaSeraphFirstPersonSystem? system = activeInstance;
        if (system == null
            || !system.config.Enabled
            || isShadowPass
            || __instance is not EntityPlayerShapeRenderer playerRenderer
            || RenderModeField?.GetValue(playerRenderer) is not RenderMode.FirstPerson
            || EntityField?.GetValue(__instance) is not Entity entity
            || !system.IsAnimalicaModel(entity))
        {
            return;
        }

        bool leftHand = string.Equals(apap.AttachPoint?.Code, "LeftHand", StringComparison.Ordinal);
        if (system.poseDriver?.TryGetHandPose(
            leftHand,
            __instance.ModelMat,
            out float[] proxyPose
        ) == true)
        {
            apap.AnimModelMatrix = proxyPose;
        }
    }

    private bool IsAnimalicaModel(Entity entity)
    {
        string model = entity.WatchedAttributes?.GetString("skinModel", string.Empty) ?? string.Empty;
        if (discoveredAnimalicaModels.Contains(model))
        {
            return true;
        }

        int separator = model.IndexOf(':');
        if (separator >= 0
            && separator + 1 < model.Length
            && discoveredAnimalicaModels.Contains(model[(separator + 1)..]))
        {
            return true;
        }

        foreach (string token in AnimalModelTokens)
        {
            if (model.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private void DiscoverAnimalicaModels(ICoreAPI api)
    {
        discoveredAnimalicaModels.Clear();
        foreach (IAsset asset in api.Assets.GetMany("config/customplayermodels", null, true))
        {
            string text = asset.ToText();
            if (string.IsNullOrWhiteSpace(text)
                || text.IndexOf("animalica", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            try
            {
                JObject root = JObject.Parse(text);
                foreach (JProperty property in root.Properties())
                {
                    if (property.Value is not JObject model || !IsAnimalicaDefinition(model))
                    {
                        continue;
                    }

                    discoveredAnimalicaModels.Add(property.Name);
                    discoveredAnimalicaModels.Add($"{asset.Location.Domain}:{property.Name}");
                }
            }
            catch (Exception exception)
            {
                api.Logger.Warning(
                    "[AnimalicaCore] Could not inspect playable model config {0} for first-person animation support. {1}",
                    asset.Location,
                    exception.Message
                );
            }
        }
    }

    private static bool IsAnimalicaDefinition(JObject model)
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

    private static AnimalicaFirstPersonConfig LoadConfig(ICoreClientAPI api)
    {
        try
        {
            var raw = api.LoadModConfig(ConfigFileName);
            if (raw != null)
            {
                AnimalicaFirstPersonConfig loaded =
                    api.LoadModConfig<AnimalicaFirstPersonConfig>(ConfigFileName)
                    ?? new AnimalicaFirstPersonConfig();
                ConfigDefaults.EnsureDefaults(api, ConfigFileName, raw, loaded);
                return loaded;
            }

            AnimalicaFirstPersonConfig? legacy =
                api.LoadModConfig<AnimalicaFirstPersonConfig>(LegacyConfigFileName);
            if (legacy != null)
            {
                api.Logger.Notification(
                    "[AnimalicaCore] Imported accepted first-person settings from {0} into {1}.",
                    LegacyConfigFileName,
                    ConfigFileName
                );
                return legacy;
            }

            return new AnimalicaFirstPersonConfig();
        }
        catch (Exception exception)
        {
            api.Logger.Warning(
                "[AnimalicaCore] Could not read {0} or its legacy predecessor; using defaults. {1}",
                ConfigFileName,
                exception.Message
            );
            return new AnimalicaFirstPersonConfig();
        }
    }
}
