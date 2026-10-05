using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace FeralKinshipCompanions;

internal readonly record struct IdleSoundsConfigMigration(bool Enabled, string? NormalizedJson);

internal static class IdleSoundsConfigPolicy
{
    private const string CurrentKey = "IdleSounds";
    private const string LegacyKey = "EnableSourceIdleTaskSounds";

    internal static IdleSoundsConfigMigration Migrate(string? rawJson)
    {
        JsonObject config;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            config = new JsonObject();
        }
        else
        {
            try
            {
                if (JsonNode.Parse(rawJson) is not JsonObject parsed)
                {
                    return new IdleSoundsConfigMigration(true, null);
                }

                config = parsed;
            }
            catch
            {
                return new IdleSoundsConfigMigration(true, null);
            }
        }

        bool hasCurrentValue = TryReadBoolean(config, CurrentKey, out bool currentValue);
        bool hasLegacyValue = TryReadBoolean(config, LegacyKey, out bool legacyValue);
        bool enabled = hasCurrentValue ? currentValue : hasLegacyValue ? legacyValue : true;

        foreach (string key in config.Select(pair => pair.Key)
                     .Where(key => string.Equals(key, CurrentKey, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(key, LegacyKey, StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            config.Remove(key);
        }

        config[CurrentKey] = enabled;
        return new IdleSoundsConfigMigration(enabled, config.ToJsonString());
    }

    private static bool TryReadBoolean(JsonObject config, string key, out bool value)
    {
        value = false;
        foreach (KeyValuePair<string, JsonNode?> entry in config)
        {
            if (!string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return entry.Value is JsonValue jsonValue && jsonValue.TryGetValue(out value);
        }

        return false;
    }
}
