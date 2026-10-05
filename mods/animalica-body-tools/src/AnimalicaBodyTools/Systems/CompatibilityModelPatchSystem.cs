using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnimalicaBodyTools.Config;
using Vintagestory.API.Common;

namespace AnimalicaBodyTools.Systems;

public static class CompatibilityModelPatchSystem
{
    private const string AnimalBodyToolsTrait = "animalica-animal-bodytools";
    private static readonly HashSet<string> ObsoletePermissionTraits =
    [
        "animalica-bodytools-animal-access",
        "animalica-bodytools-no-humanoid-tools"
    ];
    private static readonly string[] AnimalClasses =
    [
        "animalica-animal-wanderer",
        "animalica-animal-hunter",
        "animalica-animal-stalker",
        "animalica-animal-brute",
        "animalica-animal-guardian",
        "animalica-animal-runner",
        "animalica-animal-mountaineer",
        "animalica-animal-miner",
        "animalica-animal-digger",
        "animalica-animal-forager",
        "animalica-animal-fieldhand",
        "animalica-animal-scavenger"
    ];

    public static void Apply(ICoreAPI api)
    {
        int patchedModels = 0;
        int patchedFiles = 0;
        foreach (IAsset asset in api.Assets.GetMany("config/customplayermodels", null, true))
        {
            if (asset.Data == null || asset.Data.Length == 0) continue;

            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(Encoding.UTF8.GetString(asset.Data));
            }
            catch (JsonException ex)
            {
                api.Logger.Warning("[animalicabodytools] Could not parse playable model config {0}: {1}", asset.Location, ex.Message);
                continue;
            }

            if (parsed is not JsonObject root) continue;

            int patchedInFile = 0;
            foreach (KeyValuePair<string, JsonNode?> property in root)
            {
                if (property.Value is not JsonObject model) continue;
                bool isAnimalicaModel = IsAnimalicaModel(model);
                bool removedObsoleteTraits = RemoveObsoletePermissionTraits(model);
                if (!isAnimalicaModel && !removedObsoleteTraits) continue;

                if (isAnimalicaModel)
                {
                    AddUniqueStrings(model, "ExtraTraits", [AnimalBodyToolsTrait]);
                    SetStringArray(model, "AvailableClasses", AnimalClasses);
                    SetStringArray(model, "ExclusiveClasses", AnimalClasses);
                }

                patchedInFile++;
            }

            if (patchedInFile == 0) continue;
            asset.Data = Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            asset.IsPatched = true;
            patchedFiles++;
            patchedModels += patchedInFile;
        }

        if (patchedModels > 0)
        {
            api.Logger.Notification("[animalicabodytools] Added the legacy body-tool trait to {0} Animalica model(s) across {1} file(s).", patchedModels, patchedFiles);
        }
    }

    private static bool IsAnimalicaModel(JsonObject model)
    {
        string group = model["Group"]?.GetValue<string>() ?? string.Empty;
        if (group.StartsWith("animalica", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (model["AddTags"] is not JsonArray tags)
        {
            return false;
        }

        foreach (JsonNode? tag in tags)
        {
            if (string.Equals(tag?.GetValue<string>(), "animalica", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddUniqueStrings(JsonObject model, string propertyName, IEnumerable<string> values)
    {
        JsonArray array = model[propertyName] as JsonArray ?? [];
        HashSet<string> existing = array.Select(node => node?.GetValue<string>()).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (string value in values)
        {
            if (existing.Add(value)) array.Add(value);
        }
        model[propertyName] = array;
    }

    private static void SetStringArray(JsonObject model, string propertyName, IEnumerable<string> values)
    {
        JsonArray array = [];
        foreach (string value in values) array.Add(value);
        model[propertyName] = array;
    }

    private static bool RemoveObsoletePermissionTraits(JsonObject model)
    {
        if (model["ExtraTraits"] is not JsonArray traits) return false;
        bool removed = false;
        for (int index = traits.Count - 1; index >= 0; index--)
        {
            string? trait = traits[index]?.GetValue<string>();
            if (trait != null && ObsoletePermissionTraits.Contains(trait))
            {
                traits.RemoveAt(index);
                removed = true;
            }
        }
        return removed;
    }
}
