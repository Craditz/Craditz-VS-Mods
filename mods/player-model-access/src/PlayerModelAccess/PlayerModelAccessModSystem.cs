using System;
using System.Collections.Generic;
using System.Linq;
using PlayerModelLib;
using Vintagestory.API.Common;

namespace PlayerModelAccess;

public sealed class PlayerModelAccessModSystem : ModSystem
{
    private const string ConfigFileName = "PlayerModelAccess.json";
    private const string DefaultModelCode = "seraph";

    private ICoreAPI? api;
    private CustomModelsSystem? modelSystem;
    private PlayerModelAccessConfig config = new();

    public override double ExecuteOrder() => 0.22;

    public override void Start(ICoreAPI api)
    {
        this.api = api;
        PlayerModelAccessConfig? loadedConfig = api.LoadModConfig<PlayerModelAccessConfig>(ConfigFileName);
        config = loadedConfig ?? new PlayerModelAccessConfig();

        if (loadedConfig is null)
        {
            api.StoreModConfig(config, ConfigFileName);
        }

        modelSystem = api.ModLoader.GetModSystem<CustomModelsSystem>(true);
        modelSystem.OnCustomModelsLoaded += ApplyModelAccess;

        if (modelSystem.ModelsLoaded)
        {
            ApplyModelAccess();
        }
    }

    public override void Dispose()
    {
        if (modelSystem is not null)
        {
            modelSystem.OnCustomModelsLoaded -= ApplyModelAccess;
        }

        modelSystem = null;
        api = null;
    }

    private void ApplyModelAccess()
    {
        if (api is null || modelSystem is null)
        {
            return;
        }

        HashSet<string> legacyDisabled = new(
            config.DisabledModels
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim()),
            StringComparer.OrdinalIgnoreCase);

        Dictionary<string, ModelAccessEntry> configuredEntries = new(StringComparer.OrdinalIgnoreCase);
        foreach (ModelAccessEntry entry in config.Models)
        {
            if (string.IsNullOrWhiteSpace(entry.InternalName))
            {
                continue;
            }

            string internalName = entry.InternalName.Trim();
            if (!configuredEntries.TryAdd(internalName, entry))
            {
                api.Logger.Warning("[playermodelaccess] Duplicate model entry in config: {0}; keeping the first entry.", internalName);
            }
        }

        int disabledCount = 0;
        int configuredCount = 0;
        List<ModelAccessEntry> generatedEntries = new();
        HashSet<string> availableModels = new(modelSystem.CustomModels.Keys, StringComparer.OrdinalIgnoreCase);

        foreach ((string modelCode, CustomModelData modelData) in modelSystem.CustomModels.OrderBy(entry => entry.Key))
        {
            bool isDefault = IsDefaultModel(modelCode);
            bool hasConfiguredEntry = configuredEntries.TryGetValue(modelCode, out ModelAccessEntry? configuredEntry);
            bool isLegacyDisabled = legacyDisabled.Contains(modelCode);
            bool enabled = hasConfiguredEntry ? configuredEntry!.Enabled : !isLegacyDisabled;

            if (hasConfiguredEntry || isLegacyDisabled)
            {
                configuredCount++;
            }

            modelData.Enabled = enabled;
            if (!enabled)
            {
                disabledCount++;
            }

            generatedEntries.Add(new ModelAccessEntry
            {
                InternalName = modelCode,
                Enabled = enabled
            });

            if (config.DiagnosticLogging) api.Logger.Notification(
                "[playermodelaccess] Model {0}: {1}{2}",
                modelCode,
                isDefault ? enabled ? "default seraph (enabled)" : "default seraph (disabled)" : enabled ? "enabled" : "disabled",
                string.Empty);
        }

        foreach (string configuredCode in configuredEntries.Keys.Concat(legacyDisabled).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!availableModels.Contains(configuredCode))
            {
                api.Logger.Warning("[playermodelaccess] Configured model was not found: {0}", configuredCode);
            }
        }

        config.Models = generatedEntries;
        config.DisabledModels.Clear();
        api.StoreModConfig(config, ConfigFileName);

        api.Logger.Notification(
            "[playermodelaccess] Enumerated {0} PML models; disabled {1}; configured entries found {2}. Wrote the model list to ModConfig/{3}. Models remain loaded. PML enablePlayerModel grants remain available.",
            modelSystem.CustomModels.Count,
            disabledCount,
            configuredCount,
            ConfigFileName);
    }

    private static bool IsDefaultModel(string modelCode)
    {
        return string.Equals(modelCode, DefaultModelCode, StringComparison.OrdinalIgnoreCase)
            || string.Equals(modelCode, "game:" + DefaultModelCode, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class PlayerModelAccessConfig
{
    public bool DiagnosticLogging { get; set; }
    public List<ModelAccessEntry> Models { get; set; } = new();

    // Kept only so configs from Player Model Access 0.1.0 can be migrated.
    public List<string> DisabledModels { get; set; } = new();
}

public sealed class ModelAccessEntry
{
    public string InternalName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
}
