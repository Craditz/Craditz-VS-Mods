using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VintageStoryConfigMigration;

/// <summary>
/// Helpers for evolving user-owned Vintage Story JSON mod configs without
/// dropping properties that are newer, custom, or unknown to the current DLL.
/// </summary>
public static class ConfigDefaults
{
    /// <summary>
    /// Loads a typed config and adds any missing properties from the resulting
    /// object. Existing values and unknown properties are left untouched.
    /// </summary>
    public static T LoadAndUpdate<T>(ICoreAPI api, string fileName, Func<T> createDefaults)
        where T : class
    {
        JsonObject? raw = api.LoadModConfig(fileName);
        T? loaded = raw == null ? null : api.LoadModConfig<T>(fileName);
        T config = loaded ?? createDefaults();
        EnsureDefaults(api, fileName, raw, config);
        return config;
    }

    /// <summary>
    /// Adds missing object properties from <paramref name="defaults"/> while
    /// retaining all existing and unknown properties. Arrays are treated as
    /// user-owned values and are never merged element-by-element.
    /// </summary>
    public static bool EnsureDefaults(ICoreAPI api, string fileName, object defaults)
    {
        return EnsureDefaults(api, fileName, api.LoadModConfig(fileName), defaults);
    }

    /// <summary>
    /// Same as <see cref="EnsureDefaults(ICoreAPI, string, object)"/>, using a
    /// raw object that has already been loaded by the caller.
    /// </summary>
    public static bool EnsureDefaults(ICoreAPI api, string fileName, JsonObject? raw, object defaults)
    {
        JObject? existing = GetObject(raw);
        JObject desired = JObject.FromObject(defaults);

        if (existing == null)
        {
            if (raw != null)
            {
                // Do not overwrite a malformed/non-object user file. The
                // caller can continue with typed defaults and report the
                // load error without destroying the original contents.
                return false;
            }

            api.StoreModConfig(new JsonObject(desired), fileName);
            return true;
        }

        JObject merged = (JObject)existing.DeepClone();
        bool changed = AddMissingProperties(merged, desired);
        if (changed)
        {
            api.StoreModConfig(new JsonObject(merged), fileName);
        }

        return changed;
    }

    /// <summary>
    /// Stores a typed config while preserving unknown properties from the raw
    /// file. Known properties are updated from <paramref name="config"/>, so
    /// intentional normalization or UI changes are persisted too.
    /// </summary>
    public static void StorePreservingUnknown(
        ICoreAPI api,
        string fileName,
        object config,
        JsonObject? raw = null)
    {
        raw ??= api.LoadModConfig(fileName);
        JObject? existing = GetObject(raw);
        JObject current = JObject.FromObject(config);

        if (existing == null)
        {
            if (raw != null)
            {
                // Do not replace a malformed/non-object user file while
                // attempting to persist a normalized runtime object.
                return;
            }

            api.StoreModConfig(new JsonObject(current), fileName);
            return;
        }

        JObject merged = (JObject)existing.DeepClone();
        foreach (JProperty property in current.Properties())
        {
            JProperty? existingProperty = FindProperty(merged, property.Name);
            if (existingProperty == null)
            {
                merged.Add(property.Name, property.Value.DeepClone());
            }
            else
            {
                existingProperty.Value = property.Value.DeepClone();
            }
        }

        api.StoreModConfig(new JsonObject(merged), fileName);
    }

    private static JObject? GetObject(JsonObject? raw)
    {
        return raw?.Token is JObject objectToken ? objectToken : null;
    }

    private static bool AddMissingProperties(JObject target, JObject defaults)
    {
        bool changed = false;
        foreach (JProperty defaultProperty in defaults.Properties())
        {
            JProperty? existingProperty = FindProperty(target, defaultProperty.Name);
            if (existingProperty == null)
            {
                target.Add(defaultProperty.Name, defaultProperty.Value.DeepClone());
                changed = true;
                continue;
            }

            if (existingProperty.Value is JObject existingObject
                && defaultProperty.Value is JObject defaultObject)
            {
                changed |= AddMissingProperties(existingObject, defaultObject);
            }
        }

        return changed;
    }

    private static JProperty? FindProperty(JObject objectToken, string name)
    {
        return objectToken.Properties().FirstOrDefault(property =>
            string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
