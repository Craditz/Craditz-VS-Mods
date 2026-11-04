using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Druidry;

internal sealed class DruidPlayerModelAdapter
{
    private const string ModelSystemTypeName = "PlayerModelLib.CustomModelsSystem";
    private readonly ICoreAPI api;
    private static readonly ConcurrentDictionary<(Type Type, string Name), (PropertyInfo? Property, FieldInfo? Field)> memberAccess = new();
    private static readonly ConcurrentDictionary<(Type Player, Type Behavior), MethodInfo[]> behaviorAccess = new();

    public DruidPlayerModelAdapter(ICoreAPI api)
    {
        this.api = api;
    }

    public DruidModelAvailability GetModelAvailability(string modelCode)
    {
        if (!TryGetModel(modelCode, out object? model, out _))
        {
            return DruidModelAvailability.Missing;
        }

        return GetBoolean(model, "Enabled", true)
            ? DruidModelAvailability.Available
            : DruidModelAvailability.DisabledForPicker;
    }

    public bool IsModelAvailable(string modelCode)
    {
        return GetModelAvailability(modelCode) != DruidModelAvailability.Missing;
    }

    public bool ApplyWildshape(EntityPlayer player, DruidFormDefinition form, IReadOnlyDictionary<string, string> appearance, out string error)
    {
        if (!TryGetModel(form.ModelCode, out object? model, out string resolvedModelCode))
        {
            error = $"The {form.Name} spirit is unavailable because {form.PackName} is not loaded.";
            return false;
        }

        float size = GetFiniteFloat(model, "ModelSizeFactor", 1f);
        if (TryGetSizeRange(model!, out float minimum, out float maximum))
        {
            size = Math.Clamp(size, minimum, maximum);
        }

        string[] traits = ReadStringCollection(GetMemberValue(model, "ExtraTraits"));
        var appliedParts = new TreeAttribute();
        foreach ((string key, string value) in appearance)
        {
            if (!string.IsNullOrWhiteSpace(key) && value != null) appliedParts.SetString(key, value);
        }
        var skinTree = new TreeAttribute();
        skinTree.SetAttribute("appliedParts", appliedParts);

        SyncedTreeAttribute watched = player.WatchedAttributes;
        watched.SetAttribute("skinConfig", skinTree);
        watched.SetString("skinModel", resolvedModelCode);
        watched.SetFloat("entitySize", size);
        watched.SetStringArray("extraTraits", traits);
        watched.SetString("voicetype", appearance.TryGetValue("voicetype", out string? voice) ? voice : string.Empty);
        watched.SetString("voicepitch", appearance.TryGetValue("voicepitch", out string? pitch) ? pitch : string.Empty);
        MarkAppearanceDirty(watched);
        RefreshPlayerProperties(player, resolvedModelCode, size);

        error = string.Empty;
        return true;
    }

    public bool RestoreTrueForm(EntityPlayer player, ITreeAttribute trueForm, out string error)
    {
        string modelCode = trueForm.GetString("modelCode", string.Empty);
        if (string.IsNullOrWhiteSpace(modelCode)
            || !TryGetModel(modelCode, out _, out string resolvedModelCode))
        {
            error = "Your recorded true form is no longer available. Select a non-Animalica model and use /druid trueform.";
            return false;
        }

        float size = trueForm.GetFloat("entitySize", 1f);
        if (!float.IsFinite(size) || size <= 0f) size = 1f;

        SyncedTreeAttribute watched = player.WatchedAttributes;
        ITreeAttribute? savedSkin = trueForm.GetTreeAttribute("skinConfig");
        if (savedSkin != null) watched.SetAttribute("skinConfig", savedSkin.Clone());
        else watched.RemoveAttribute("skinConfig");

        watched.SetString("skinModel", resolvedModelCode);
        watched.SetFloat("entitySize", size);
        watched.SetString("voicetype", trueForm.GetString("voicetype", string.Empty));
        watched.SetString("voicepitch", trueForm.GetString("voicepitch", string.Empty));
        string[] savedTraits = (trueForm["extraTraits"] as StringArrayAttribute)?.value ?? Array.Empty<string>();
        watched.SetStringArray("extraTraits", savedTraits);
        MarkAppearanceDirty(watched);
        RefreshPlayerProperties(player, resolvedModelCode, size);

        error = string.Empty;
        return true;
    }

    public static ITreeAttribute CaptureTrueForm(EntityPlayer player)
    {
        SyncedTreeAttribute watched = player.WatchedAttributes;
        var result = new TreeAttribute();
        result.SetString("modelCode", watched.GetString("skinModel", "seraph"));
        result.SetFloat("entitySize", watched.GetFloat("entitySize", 1f));
        result.SetString("voicetype", watched.GetString("voicetype", string.Empty));
        result.SetString("voicepitch", watched.GetString("voicepitch", string.Empty));
        result.SetStringArray("extraTraits", watched.GetStringArray("extraTraits", Array.Empty<string>()));
        ITreeAttribute? skin = watched.GetTreeAttribute("skinConfig");
        if (skin != null) result.SetAttribute("skinConfig", skin.Clone());
        return result;
    }

    private bool TryGetModel(string modelCode, out object? model, out string resolvedModelCode)
    {
        ModSystem? system = api.ModLoader.GetModSystem(ModelSystemTypeName);
        object? value = GetMemberValue(system, "CustomModels");
        if (value is IDictionary models)
        {
            // Normal configured codes are exact dictionary keys. Do not cache
            // model values: PML can reload or replace entries in the same registry.
            if (models.Contains(modelCode) && models[modelCode] is object exactModel)
            {
                model = exactModel;
                resolvedModelCode = modelCode;
                return true;
            }
            string shortCode = GetShortModelCode(modelCode);
            string? aliasKey = null;
            object? aliasModel = null;
            foreach (DictionaryEntry entry in models)
            {
                if (entry.Key is string key
                    && string.Equals(key, modelCode, StringComparison.OrdinalIgnoreCase))
                {
                    model = entry.Value;
                    resolvedModelCode = key;
                    return model != null;
                }
                if (aliasKey == null && entry.Key is string candidate
                    && string.Equals(GetShortModelCode(candidate), shortCode, StringComparison.OrdinalIgnoreCase))
                {
                    aliasModel = entry.Value;
                    aliasKey = candidate;
                }
            }
            if (aliasKey != null)
            {
                model = aliasModel;
                resolvedModelCode = aliasKey;
                return model != null;
            }
        }

        model = null;
        resolvedModelCode = string.Empty;
        return false;
    }

    private static string GetShortModelCode(string modelCode)
    {
        int namespaceSeparator = modelCode.LastIndexOf(':');
        return namespaceSeparator >= 0 ? modelCode[(namespaceSeparator + 1)..] : modelCode;
    }

    private static void MarkAppearanceDirty(SyncedTreeAttribute watched)
    {
        watched.MarkPathDirty("skinConfig");
        watched.MarkPathDirty("skinModel");
        watched.MarkPathDirty("entitySize");
        watched.MarkPathDirty("extraTraits");
        watched.MarkPathDirty("voicetype");
        watched.MarkPathDirty("voicepitch");
    }

    private static void RefreshPlayerProperties(EntityPlayer player, string modelCode, float size)
    {
        Type? behaviorType = Type.GetType("PlayerModelLib.PlayerSkinBehavior, PlayerModelLib", throwOnError: false);
        MethodInfo? getBehavior = behaviorType == null ? null : behaviorAccess.GetOrAdd((player.GetType(), behaviorType), key =>
            key.Player.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.Name == "GetBehavior" && method.IsGenericMethodDefinition
                    && method.GetParameters().Length == 0)
                .Take(1).Select(method => method.MakeGenericMethod(key.Behavior)).ToArray()).FirstOrDefault();
        object? behavior = behaviorType == null || getBehavior == null
            ? null
            : getBehavior.Invoke(player, null);
        if (behavior == null) return;

        Type type = behavior.GetType();
        type.GetMethod("ApplyTraitAttributesWithModelTraits", BindingFlags.Public | BindingFlags.Instance)
            ?.Invoke(behavior, new object[] { modelCode });
        PropertyInfo? currentSize = type.GetProperty("CurrentSize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (currentSize?.GetSetMethod(true) != null) currentSize.SetValue(behavior, size);
        type.GetMethod("UpdateEntityProperties", BindingFlags.Public | BindingFlags.Instance)?.Invoke(behavior, null);
    }

    private static bool TryGetSizeRange(object model, out float minimum, out float maximum)
    {
        object? range = GetMemberValue(model, "SizeRange");
        minimum = GetFiniteFloat(range, "X", float.NaN);
        maximum = GetFiniteFloat(range, "Y", float.NaN);
        if (!float.IsFinite(minimum) || !float.IsFinite(maximum) || minimum <= 0f || maximum <= 0f)
        {
            return false;
        }
        if (maximum < minimum) (minimum, maximum) = (maximum, minimum);
        return true;
    }

    private static string[] ReadStringCollection(object? value)
    {
        if (value is string[] array) return array.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        if (value is IEnumerable enumerable)
        {
            return enumerable.Cast<object?>()
                .Select(item => item?.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray();
        }
        return Array.Empty<string>();
    }

    private static float GetFiniteFloat(object? value, string name, float fallback)
    {
        object? member = GetMemberValue(value, name);
        float result = member switch
        {
            float single => single,
            double doubleValue => (float)doubleValue,
            _ => fallback
        };
        return float.IsFinite(result) ? result : fallback;
    }

    private static bool GetBoolean(object? value, string name, bool fallback)
    {
        object? member = GetMemberValue(value, name);
        return member is bool boolean ? boolean : fallback;
    }

    private static object? GetMemberValue(object? value, string name)
    {
        if (value == null) return null;
        var members = memberAccess.GetOrAdd((value.GetType(), name), key =>
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            return (key.Type.GetProperty(key.Name, flags), key.Type.GetField(key.Name, flags));
        });
        return members.Property?.GetValue(value) ?? members.Field?.GetValue(value);
    }
}

internal enum DruidModelAvailability
{
    Missing,
    Available,
    DisabledForPicker
}
