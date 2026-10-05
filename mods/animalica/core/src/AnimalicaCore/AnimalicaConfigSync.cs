using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ProtoBuf;
using Vintagestory.API.Client;

namespace AnimalicaCore;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class AnimalicaConfigPacket
{
    public bool EnableRaceTraits { get; set; }
    public bool EnableSizeOverrides { get; set; }
    public double SizeMinimum { get; set; }
    public double SizeMaximum { get; set; }
    public double DefaultSize { get; set; }
    public bool UseDefaultSwimmingDepth { get; set; }
    public string AnimalBalanceMode { get; set; } = "Balanced";
    public string AnimalClassPowerMode { get; set; } = "Balanced";
    public bool EnableAnimalFoodNutrition { get; set; }
    public bool MuzzleMode { get; set; }
    public bool OldSit { get; set; }

    public static AnimalicaConfigPacket FromConfig(AnimalicaCoreConfig config)
    {
        return new AnimalicaConfigPacket
        {
            EnableRaceTraits = config.EnableRaceTraits,
            EnableSizeOverrides = config.EnableSizeOverrides,
            SizeMinimum = config.SizeMinimum,
            SizeMaximum = config.SizeMaximum,
            DefaultSize = config.DefaultSize,
            UseDefaultSwimmingDepth = config.UseDefaultSwimmingDepth,
            AnimalBalanceMode = config.AnimalBalanceMode,
            AnimalClassPowerMode = config.AnimalClassPowerMode,
            EnableAnimalFoodNutrition = config.EnableAnimalFoodNutrition,
            MuzzleMode = config.MuzzleMode,
            OldSit = config.OldSit
        };
    }

    public AnimalicaCoreConfig ToConfig()
    {
        return new AnimalicaCoreConfig
        {
            EnableRaceTraits = EnableRaceTraits,
            EnableSizeOverrides = EnableSizeOverrides,
            SizeMinimum = SizeMinimum,
            SizeMaximum = SizeMaximum,
            DefaultSize = DefaultSize,
            UseDefaultSwimmingDepth = UseDefaultSwimmingDepth,
            AnimalBalanceMode = AnimalBalanceMode,
            AnimalClassPowerMode = AnimalClassPowerMode,
            EnableAnimalFoodNutrition = EnableAnimalFoodNutrition,
            MuzzleMode = MuzzleMode,
            OldSit = OldSit
        };
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class AnimalicaConfigRequestPacket
{
    public int ProtocolVersion { get; set; } = 1;
}

internal readonly record struct AnimalicaModelBaseline(
    string[] CoreTraits,
    double SizeMinimum,
    double SizeMaximum,
    double ModelSizeFactor
);

internal static class AnimalicaRuntimeConfigApplier
{
    public static int ApplySeraphEyeHeight(
        ICoreClientAPI api,
        IReadOnlyDictionary<string, AnimalicaModelBaseline> baselines
    )
    {
        object? customModelsSystem = api.ModLoader.GetModSystem("PlayerModelLib.CustomModelsSystem");
        object? customModels = customModelsSystem?.GetType()
            .GetProperty("CustomModels", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(customModelsSystem);
        if (customModels is not IDictionary models)
        {
            return 0;
        }

        object? seraph = models.Contains("seraph")
            ? models["seraph"]
            : models.Contains("playermodellib:seraph")
                ? models["playermodellib:seraph"]
                : null;
        float seraphEyeHeight = GetMemberValue<float>(seraph, "EyeHeight");
        if (seraph == null || !float.IsFinite(seraphEyeHeight) || seraphEyeHeight <= 0f)
        {
            return 0;
        }

        int changed = 0;
        foreach (string modelCode in baselines.Keys)
        {
            if (!models.Contains(modelCode) || models[modelCode] == null)
            {
                continue;
            }

            object model = models[modelCode]!;
            bool modelChanged = false;
            modelChanged |= SetMemberValue(model, "EyeHeight", seraphEyeHeight);
            modelChanged |= SetMemberValue(model, "MinEyeHeight", seraphEyeHeight);
            modelChanged |= SetMemberValue(model, "MaxEyeHeight", seraphEyeHeight);
            if (modelChanged)
            {
                changed++;
            }
        }

        return changed;
    }

    public static void RefreshLocalPlayerModel(ICoreClientAPI api)
    {
        object? entity = api.World.Player?.GetType()
            .GetProperty("Entity", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(api.World.Player);
        Type? behaviorType = Type.GetType("PlayerModelLib.PlayerSkinBehavior, PlayerModelLib");
        MethodInfo? getBehavior = entity?.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "GetBehavior"
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 0);
        object? behavior = entity == null || behaviorType == null || getBehavior == null
            ? null
            : getBehavior.MakeGenericMethod(behaviorType).Invoke(entity, null);
        behavior?.GetType()
            .GetMethod("UpdateEntityProperties", BindingFlags.Public | BindingFlags.Instance)
            ?.Invoke(behavior, null);
    }

    public static int Apply(
        ICoreClientAPI api,
        AnimalicaCoreConfig config,
        IReadOnlyDictionary<string, AnimalicaModelBaseline> baselines,
        IReadOnlySet<string> coreTraitCodes
    )
    {
        object? customModelsSystem = api.ModLoader.GetModSystem("PlayerModelLib.CustomModelsSystem");
        object? customModels = customModelsSystem?.GetType()
            .GetProperty("CustomModels", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(customModelsSystem);

        if (customModels is not IDictionary models)
        {
            api.Logger.Warning("[AnimalicaCore] Could not apply synchronized settings to Player Model Lib's loaded models.");
            return 0;
        }

        AnimalicaSizeSettings sizeSettings = AnimalicaSizePolicy.Normalize(config);
        bool shouldOverrideSizes = AnimalicaSizePolicy.ShouldOverridePackDefaults(config);
        int changed = 0;
        foreach (DictionaryEntry entry in models)
        {
            string code = entry.Key?.ToString() ?? string.Empty;
            object? model = entry.Value;
            if (model == null || !baselines.TryGetValue(code, out AnimalicaModelBaseline baseline))
            {
                continue;
            }

            Type modelType = model.GetType();
            string[] currentTraits = GetMemberValue(model, "ExtraTraits") as string[] ?? Array.Empty<string>();
            List<string> desiredTraits = new(currentTraits.Length + baseline.CoreTraits.Length);
            foreach (string trait in currentTraits)
            {
                if (!coreTraitCodes.Contains(trait) && !desiredTraits.Contains(trait, StringComparer.Ordinal))
                {
                    desiredTraits.Add(trait);
                }
            }

            if (config.EnableRaceTraits)
            {
                foreach (string trait in baseline.CoreTraits)
                {
                    if (!config.EnableAnimalFoodNutrition
                        && AnimalicaTraitRestrictionsFoodBridge.IsFoodAccessTrait(trait))
                    {
                        continue;
                    }

                    if (!desiredTraits.Contains(trait, StringComparer.Ordinal))
                    {
                        desiredTraits.Add(trait);
                    }
                }
            }

            SetMemberValue(model, "ExtraTraits", desiredTraits.ToArray());

            if (shouldOverrideSizes)
            {
                SetVector2(model, "SizeRange", (float)sizeSettings.Minimum, (float)sizeSettings.Maximum);
                SetMemberValue(model, "ModelSizeFactor", (float)sizeSettings.Default);
            }
            else
            {
                SetVector2(model, "SizeRange", (float)baseline.SizeMinimum, (float)baseline.SizeMaximum);
                SetMemberValue(model, "ModelSizeFactor", (float)baseline.ModelSizeFactor);
            }

            changed++;
        }

        return changed;
    }

    private static void SetVector2(object model, string propertyName, float x, float y)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        Type modelType = model.GetType();
        PropertyInfo? property = modelType.GetProperty(propertyName, flags);
        FieldInfo? field = modelType.GetField(propertyName, flags);
        Type? vectorType = property?.PropertyType ?? field?.FieldType;
        ConstructorInfo? constructor = vectorType?.GetConstructor([typeof(float), typeof(float)]);
        if (constructor == null || (property == null && field == null))
        {
            return;
        }

        object value = constructor.Invoke([x, y]);
        if (property?.SetMethod != null)
        {
            property.SetValue(model, value);
        }
        else
        {
            field?.SetValue(model, value);
        }
    }

    private static T GetMemberValue<T>(object? value, string memberName)
    {
        object? result = GetMemberValue(value, memberName);
        return result is T typed ? typed : default!;
    }

    private static object? GetMemberValue(object? value, string memberName)
    {
        if (value == null)
        {
            return null;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        Type type = value.GetType();
        PropertyInfo? property = type.GetProperty(memberName, flags);
        if (property != null)
        {
            return property.GetValue(value);
        }

        return type.GetField(memberName, flags)?.GetValue(value);
    }

    private static bool SetMemberValue(object value, string memberName, object memberValue)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        Type type = value.GetType();
        PropertyInfo? property = type.GetProperty(memberName, flags);
        if (property?.SetMethod != null)
        {
            object? current = property.GetValue(value);
            if (Equals(current, memberValue))
            {
                return false;
            }

            property.SetValue(value, memberValue);
            return true;
        }

        FieldInfo? field = type.GetField(memberName, flags);
        if (field == null || Equals(field.GetValue(value), memberValue))
        {
            return false;
        }

        field.SetValue(value, memberValue);
        return true;
    }

}
