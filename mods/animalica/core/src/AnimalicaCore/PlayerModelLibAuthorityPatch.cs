using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace AnimalicaCore;

internal static class PlayerModelLibAuthorityPatch
{
    private const string ModelSystemTypeName = "PlayerModelLib.CustomModelsSystem";
    private static ICoreServerAPI? serverApi;
    private static System.Func<string, bool>? isManagedModel;

    public static void Start(ICoreServerAPI api, Harmony harmony, System.Func<string, bool> managedModelPredicate)
    {
        serverApi = api;
        isManagedModel = managedModelPredicate;

        Type? systemType = AccessTools.TypeByName(ModelSystemTypeName);
        MethodInfo? modelHandler = systemType == null
            ? null
            : AccessTools.Method(systemType, "HandleChangePlayerModelPacket");
        MethodInfo? sizeHandler = systemType == null
            ? null
            : AccessTools.Method(systemType, "HandleChangePlayerModelSizePacket");
        if (modelHandler == null || sizeHandler == null)
        {
            api.Logger.Error("[AnimalicaCore] Could not install Player Model Lib server-authority validation.");
            return;
        }

        harmony.Patch(
            modelHandler,
            prefix: new HarmonyMethod(typeof(PlayerModelLibAuthorityPatch), nameof(ValidateModelChange)),
            postfix: new HarmonyMethod(typeof(PlayerModelLibAuthorityPatch), nameof(ClampAfterModelChange))
        );
        harmony.Patch(
            sizeHandler,
            prefix: new HarmonyMethod(typeof(PlayerModelLibAuthorityPatch), nameof(ValidateSizeChange))
        );
        api.Logger.Notification("[AnimalicaCore] Player Model Lib model and size requests are server-validated.");
    }

    public static void EnforceCurrentPlayer(ICoreServerAPI? api, IServerPlayer player)
    {
        if (api == null)
        {
            return;
        }

        object? system = api.ModLoader.GetModSystem(ModelSystemTypeName);
        ClampCurrentSize(system, player);
        RefreshPlayerProperties(player.Entity);
    }

    public static void Stop()
    {
        serverApi = null;
        isManagedModel = null;
    }

    private static bool ValidateModelChange(object __instance, IPlayer player, object packet)
    {
        string modelCode = GetMemberValue<string>(packet, "ModelCode") ?? string.Empty;
        if (TryGetModel(__instance, modelCode, out _))
        {
            return true;
        }

        serverApi?.Logger.Warning(
            "[AnimalicaCore] Rejected unknown player model '{0}' requested by {1}.",
            modelCode,
            player.PlayerName
        );
        return false;
    }

    private static void ClampAfterModelChange(object __instance, IPlayer player)
    {
        ClampCurrentSize(__instance, player);
    }

    private static void ValidateSizeChange(object __instance, IPlayer player, object packet)
    {
        string modelCode = player.Entity.WatchedAttributes.GetString("skinModel", "seraph");
        if (isManagedModel?.Invoke(modelCode) != true
            || !TryGetModel(__instance, modelCode, out object? model)
            || !TryGetSizePolicy(model!, out float minimum, out float maximum, out float defaultSize))
        {
            return;
        }

        float requested = GetMemberValue<float>(packet, "EntitySize");
        float clamped = ClampFinite(requested, minimum, maximum, defaultSize);
        if (Math.Abs(requested - clamped) > 0.0001f || !float.IsFinite(requested))
        {
            SetMemberValue(packet, "EntitySize", clamped);
            serverApi?.Logger.Warning(
                "[AnimalicaCore] Clamped out-of-policy size {0} to {1} for {2} using model {3}.",
                requested,
                clamped,
                player.PlayerName,
                modelCode
            );
        }
    }

    private static void ClampCurrentSize(object? system, IPlayer player)
    {
        string modelCode = player.Entity.WatchedAttributes.GetString("skinModel", "seraph");
        if (system == null
            || isManagedModel?.Invoke(modelCode) != true
            || !TryGetModel(system, modelCode, out object? model)
            || !TryGetSizePolicy(model!, out float minimum, out float maximum, out float defaultSize))
        {
            return;
        }

        float current = player.Entity.WatchedAttributes.GetFloat("entitySize", defaultSize);
        float clamped = ClampFinite(current, minimum, maximum, defaultSize);
        if (float.IsFinite(current) && Math.Abs(current - clamped) <= 0.0001f)
        {
            return;
        }

        player.Entity.WatchedAttributes.SetFloat("entitySize", clamped);
        player.Entity.WatchedAttributes.MarkPathDirty("entitySize");
        RefreshPlayerProperties(player.Entity);
    }

    private static bool TryGetModel(object system, string modelCode, out object? model)
    {
        object? value = system.GetType()
            .GetProperty("CustomModels", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(system);
        if (value is IDictionary models && models.Contains(modelCode))
        {
            model = models[modelCode];
            return model != null;
        }

        model = null;
        return false;
    }

    private static bool TryGetSizePolicy(object model, out float minimum, out float maximum, out float defaultSize)
    {
        object? range = GetMemberValue(model, "SizeRange");
        minimum = GetMemberValue<float>(range, "X");
        maximum = GetMemberValue<float>(range, "Y");
        defaultSize = GetMemberValue<float>(model, "ModelSizeFactor");
        if (!float.IsFinite(minimum)
            || !float.IsFinite(maximum)
            || minimum <= 0f
            || maximum <= 0f)
        {
            return false;
        }

        if (maximum < minimum)
        {
            (minimum, maximum) = (maximum, minimum);
        }

        defaultSize = ClampFinite(defaultSize, minimum, maximum, 1f);
        return true;
    }

    private static void RefreshPlayerProperties(Entity entity)
    {
        string modelCode = entity.WatchedAttributes.GetString("skinModel", "seraph");
        Type? behaviorType = AccessTools.TypeByName("PlayerModelLib.PlayerSkinBehavior");
        MethodInfo? getBehavior = entity.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "GetBehavior"
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 0);
        object? behavior = behaviorType == null || getBehavior == null
            ? null
            : getBehavior.MakeGenericMethod(behaviorType).Invoke(entity, null);
        if (behavior == null)
        {
            return;
        }

        Type type = behavior.GetType();
        type.GetMethod("ApplyTraitAttributesWithModelTraits", BindingFlags.Public | BindingFlags.Instance)
            ?.Invoke(behavior, new object[] { modelCode });
        PreserveWatchedEntitySize(entity, behavior, type);
        type.GetMethod("UpdateEntityProperties", BindingFlags.Public | BindingFlags.Instance)
            ?.Invoke(behavior, null);
    }

    private static void PreserveWatchedEntitySize(Entity entity, object behavior, Type behaviorType)
    {
        if (!entity.WatchedAttributes.HasAttribute("entitySize"))
        {
            return;
        }

        float entitySize = entity.WatchedAttributes.GetFloat("entitySize", float.NaN);
        if (!float.IsFinite(entitySize) || entitySize <= 0f)
        {
            return;
        }

        PropertyInfo? currentSize = behaviorType.GetProperty(
            "CurrentSize",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        );
        if (currentSize?.GetSetMethod(true) == null)
        {
            return;
        }

        currentSize.SetValue(behavior, entitySize);
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

    private static void SetMemberValue(object value, string memberName, object memberValue)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        Type type = value.GetType();
        PropertyInfo? property = type.GetProperty(memberName, flags);
        if (property?.SetMethod != null)
        {
            property.SetValue(value, memberValue);
            return;
        }

        type.GetField(memberName, flags)?.SetValue(value, memberValue);
    }

    private static float ClampFinite(float value, float minimum, float maximum, float fallback)
    {
        if (!float.IsFinite(value))
        {
            value = fallback;
        }

        return Math.Clamp(value, minimum, maximum);
    }
}
