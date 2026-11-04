using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace Druidry;

internal sealed class DruidAppearanceCatalog
{
    private const string PresetAssetPath = "config/animalica/skinpresets";
    private readonly Dictionary<string, Dictionary<string, string>> defaultsByModel =
        new(StringComparer.OrdinalIgnoreCase);

    public void Load(ICoreAPI api)
    {
        defaultsByModel.Clear();
        foreach (IAsset asset in api.Assets.GetMany(PresetAssetPath, null, true)
                     .OrderBy(value => value.Location.ToString(), StringComparer.Ordinal))
        {
            DruidPresetDocument? document;
            try
            {
                document = JsonConvert.DeserializeObject<DruidPresetDocument>(asset.ToText());
            }
            catch (Exception exception)
            {
                api.Logger.Warning("[Druidry] Could not read Animalica appearance presets from {0}: {1}", asset.Location, exception.Message);
                continue;
            }

            if (document?.Selectors == null) continue;
            foreach ((string partCode, DruidPresetSelector selector) in document.Selectors)
            {
                if (selector.ModelCodes == null || selector.Presets == null || selector.Presets.Count == 0)
                {
                    continue;
                }

                Dictionary<string, string>? preset = null;
                if (!string.IsNullOrWhiteSpace(selector.DefaultPreset))
                {
                    selector.Presets.TryGetValue(selector.DefaultPreset, out preset);
                }
                preset ??= selector.Presets
                    .OrderBy(value => value.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(value => value.Value)
                    .FirstOrDefault();
                if (preset == null) continue;

                foreach (string modelCode in selector.ModelCodes.Where(value => !string.IsNullOrWhiteSpace(value)))
                {
                    var selection = new Dictionary<string, string>(preset, StringComparer.Ordinal);
                    if (selector.DefaultParts != null)
                    {
                        foreach ((string key, string value) in selector.DefaultParts) selection[key] = value;
                    }
                    selection[partCode] = selector.ResetVariant ?? "custom";
                    defaultsByModel[modelCode] = selection;
                }
            }
        }

        api.Logger.Notification("[Druidry] Loaded default appearances for {0} Animalica model(s).", defaultsByModel.Count);
    }

    public bool TryGet(string modelCode, out IReadOnlyDictionary<string, string> appearance)
    {
        if (defaultsByModel.TryGetValue(modelCode, out Dictionary<string, string>? value))
        {
            appearance = value;
            return true;
        }

        appearance = null!;
        return false;
    }
}

internal sealed class DruidPresetDocument
{
    [JsonProperty("selectors")]
    public Dictionary<string, DruidPresetSelector>? Selectors { get; set; }
}

internal sealed class DruidPresetSelector
{
    [JsonProperty("modelCodes")]
    public List<string>? ModelCodes { get; set; }

    [JsonProperty("defaultPreset")]
    public string? DefaultPreset { get; set; }

    [JsonProperty("resetVariant")]
    public string? ResetVariant { get; set; }

    [JsonProperty("defaultParts")]
    public Dictionary<string, string>? DefaultParts { get; set; }

    [JsonProperty("presets")]
    public Dictionary<string, Dictionary<string, string>>? Presets { get; set; }
}
