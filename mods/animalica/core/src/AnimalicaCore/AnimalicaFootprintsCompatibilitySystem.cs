using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace AnimalicaCore;

public sealed class AnimalicaFootprintsCompatibilitySystem : ModSystem
{
    private const string HarmonyId = "craditz.animalicacore.footprints";
    private static ILogger? logger;
    private static int mappedLogCount;
    private Harmony? harmony;

    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return forSide == EnumAppSide.Client;
    }

    public override double ExecuteOrder()
    {
        return 0.061;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        if (!api.ModLoader.IsModEnabled("footprints"))
        {
            return;
        }

        logger = api.Logger;
        Type? texturesManager = AccessTools.TypeByName("Footprints.src.managers.rendering.TexturesManager");
        MethodInfo? target = texturesManager == null
            ? null
            : AccessTools.Method(texturesManager, "GetPlayerTexture", [typeof(EntityPlayer)]);
        // Footprints 1.2.6 replaced its texture enum with data-driven string IDs.
        // Keep the legacy enum patch for 1.2.5 and earlier.
        MethodInfo? postfix = null;
        if (target?.ReturnType == typeof(string))
        {
            postfix = AccessTools.Method(typeof(AnimalicaFootprintsCompatibilitySystem), nameof(GetPlayerTextureStringPostfix));
        }
        else if (target?.ReturnType.IsEnum == true)
        {
            postfix = AccessTools.Method(typeof(AnimalicaFootprintsCompatibilitySystem), nameof(GetPlayerTexturePostfix))
                ?.MakeGenericMethod(target.ReturnType);
        }

        if (target == null || postfix == null)
        {
            api.Logger.Warning("[AnimalicaCore] Footprints is installed, but its player texture selector could not be found.");
            return;
        }

        harmony = new Harmony(HarmonyId);
        harmony.Patch(target, postfix: new HarmonyMethod(postfix));
        api.Logger.Notification("[AnimalicaCore] Optional Footprints compatibility enabled for Animalica player models.");
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
        logger = null;
        mappedLogCount = 0;
    }

    private static void GetPlayerTexturePostfix<TTexture>(EntityPlayer player, ref TTexture __result)
        where TTexture : struct, Enum
    {
        string? skinModel = player.WatchedAttributes?.GetString("skinModel", null);
        if (!TryGetAnimalicaTextureName(skinModel, out string textureName)
            || !Enum.TryParse(textureName, true, out TTexture texture))
        {
            return;
        }

        __result = texture;
        if (mappedLogCount < 10)
        {
            mappedLogCount++;
            logger?.Notification("[AnimalicaCore] Mapped skinModel '{0}' to Footprints texture '{1}'.", skinModel, texture);
        }
    }

    private static void GetPlayerTextureStringPostfix(EntityPlayer player, ref string __result)
    {
        string? skinModel = player.WatchedAttributes?.GetString("skinModel", null);
        if (!TryGetAnimalicaTextureName(skinModel, out string textureName))
        {
            return;
        }

        __result = textureName;
        if (mappedLogCount < 10)
        {
            mappedLogCount++;
            logger?.Notification("[AnimalicaCore] Mapped skinModel '{0}' to Footprints texture '{1}'.", skinModel, textureName);
        }
    }

    private static bool TryGetAnimalicaTextureName(string? skinModel, out string texture)
    {
        texture = string.Empty;
        if (string.IsNullOrWhiteSpace(skinModel))
        {
            return false;
        }

        string model = skinModel;

        if (ContainsAny(model, "feralfox", "foxsocks"))
        {
            texture = "fox";
            return true;
        }

        if (model.Contains("feralwolf", StringComparison.OrdinalIgnoreCase))
        {
            texture = "wolf";
            return true;
        }

        if (ContainsAny(model, "feralhyena", "spottedhyena"))
        {
            texture = "hyena";
            return true;
        }

        if (ContainsAny(model, "feralbear", "blackbear", "brownbear", "polarbear", "sunbear", "giantpanda"))
        {
            texture = "bear";
            return true;
        }

        if (model.Contains("feralraccoon", StringComparison.OrdinalIgnoreCase))
        {
            texture = "raccoon";
            return true;
        }

        if (ContainsAny(model, "draconis", "cuprocaudus"))
        {
            texture = "bear";
            return true;
        }

        if (ContainsAny(model, "equus", "ferus", "horse"))
        {
            texture = "deer";
            return true;
        }

        if (ContainsAny(model, "cats-", "pantherinae", "lion", "jaguar", "leopard", "tiger", "cloudedleopard", "snowleopard"))
        {
            texture = "wolf";
            return true;
        }

        if (ContainsAny(model, "deer-elk", "elk"))
        {
            texture = "elk";
            return true;
        }

        if (ContainsAny(model, "deer-moose", "moose"))
        {
            texture = "moose";
            return true;
        }

        if (model.Contains("gazelle", StringComparison.OrdinalIgnoreCase))
        {
            texture = "gazelle";
            return true;
        }

        if (model.Contains("goat", StringComparison.OrdinalIgnoreCase))
        {
            texture = "goat";
            return true;
        }

        if (model.Contains("pig", StringComparison.OrdinalIgnoreCase))
        {
            texture = "pig";
            return true;
        }

        if (model.Contains("sheep", StringComparison.OrdinalIgnoreCase))
        {
            texture = "sheep";
            return true;
        }

        if (ContainsAny(model, "feraldeer", "deer-", "caribou", "pudu"))
        {
            texture = "deer";
            return true;
        }

        if (model.Contains("chicken", StringComparison.OrdinalIgnoreCase))
        {
            texture = "chicken";
            return true;
        }

        if (model.Contains("hare", StringComparison.OrdinalIgnoreCase))
        {
            texture = "hare";
            return true;
        }

        return false;
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
