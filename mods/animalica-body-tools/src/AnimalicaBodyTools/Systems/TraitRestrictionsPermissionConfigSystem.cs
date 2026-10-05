using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnimalicaBodyTools.Config;
using Vintagestory.API.Common;

namespace AnimalicaBodyTools.Systems;

/// <summary>
/// Builds Trait Restrictions' item lists from the collectibles that actually loaded.
/// This runs in AssetsFinalize before Trait Restrictions (order 0.31) resolves its rules.
/// </summary>
internal static class TraitRestrictionsPermissionConfigSystem
{
    private const string TraitAssetDomain = "animalicabodytools";
    private const string TraitAssetPath = "config/traits.json";
    private const string AnimalBodyToolsTrait = "animalica-animal-bodytools";

    private static readonly HashSet<string> BlockedToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Knife",
        "Axe",
        "Shovel",
        "Pickaxe",
        "Spear",
        "Bow",
        "Sword",
        "Falx",
        "Hoe",
        "Scythe",
        "Shears",
        "Saw",
        "ProspectingPick",
        "Propick"
    };

    private static readonly HashSet<string> AllowedToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Hammer",
        "Chisel"
    };

    public static void RefreshResolvedPermissions(ICoreAPI api)
    {
        ModSystem permissions = api.ModLoader.GetModSystem("TraitRestrictions.TraitItemPermissionsSystem")
            ?? throw new InvalidOperationException("Trait Restrictions permission system is unavailable.");

        Apply(api);
        // Trait Restrictions 1.0.0 rebuilds all resolved rule/owner caches here.
        // Reload all current assets so other mods' permissions and foods survive.
        permissions.AssetsFinalize(api);
        api.Logger.Notification(
            "[animalicabodytools] Refreshed Trait Restrictions for server mode {0}.",
            AnimalicaBodyToolsPolicy.CurrentMode);
    }

    public static void Apply(ICoreAPI api)
    {
        List<string> bodyToolCodes = new();
        List<string> wearableBodyToolCodes = new();
        List<string> restrictedToolCodes = new();

        foreach (CollectibleObject collectible in api.World.Collectibles)
        {
            string? code = collectible.Code?.ToString();
            if (string.IsNullOrWhiteSpace(code)) continue;

            bool isBodyTool = collectible.Attributes?["animalicaBodyTool"].AsBool(false) == true;
            bool isWearableBodyTool = collectible.Attributes?["animalicaWearableBodyTool"].AsBool(false) == true;
            if (isBodyTool || isWearableBodyTool)
            {
                bodyToolCodes.Add(code);
            }

            if (isWearableBodyTool)
            {
                wearableBodyToolCodes.Add(code);
            }

            if (isBodyTool || isWearableBodyTool) continue;

            string? toolName = collectible.Tool?.ToString();
            if (string.IsNullOrWhiteSpace(toolName)
                || !BlockedToolNames.Contains(toolName)
                || AllowedToolNames.Contains(toolName))
            {
                continue;
            }

            restrictedToolCodes.Add(code);
        }

        bodyToolCodes = bodyToolCodes.Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToList();
        wearableBodyToolCodes = wearableBodyToolCodes.Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToList();
        restrictedToolCodes = restrictedToolCodes.Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToList();

        if (bodyToolCodes.Count == 0 || wearableBodyToolCodes.Count == 0 || restrictedToolCodes.Count == 0)
        {
            throw new InvalidOperationException(
                $"Could not build Trait Restrictions rules: body tools={bodyToolCodes.Count}, " +
                $"wearable body tools={wearableBodyToolCodes.Count}, humanoid tools={restrictedToolCodes.Count}.");
        }

        IAsset asset = api.Assets.Get(new AssetLocation(TraitAssetDomain, TraitAssetPath));
        JsonNode? parsed = JsonNode.Parse(Encoding.UTF8.GetString(asset.Data));
        if (parsed is not JsonArray traits)
        {
            throw new InvalidOperationException("Animalica Body Tools config/traits.json must contain a trait array.");
        }

        BodyToolRestrictionMode mode = AnimalicaBodyToolsPolicy.CurrentMode;
        JsonObject animalBodyToolsTrait = FindOrAddTrait(traits, AnimalBodyToolsTrait);
        SetStringArray(
            animalBodyToolsTrait,
            "ExclusiveItems",
            mode == BodyToolRestrictionMode.Disabled ? Array.Empty<string>() : bodyToolCodes);
        SetStringArray(
            animalBodyToolsTrait,
            "ExclusiveWearables",
            mode == BodyToolRestrictionMode.Disabled ? Array.Empty<string>() : wearableBodyToolCodes);

        IEnumerable<string> restrictedTools = mode == BodyToolRestrictionMode.Default
            ? restrictedToolCodes
            : Array.Empty<string>();
        SetStringArray(animalBodyToolsTrait, "DisallowedInteract", restrictedTools);
        SetStringArray(animalBodyToolsTrait, "DisallowedAttack", restrictedTools);

        asset.Data = Encoding.UTF8.GetBytes(traits.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        asset.IsPatched = true;

        api.Logger.Notification(
            "[animalicabodytools] Prepared Trait Restrictions on {0} for {1} body tools ({2} wearable), {3} humanoid tools, mode {4}.",
            AnimalBodyToolsTrait,
            mode == BodyToolRestrictionMode.Disabled ? 0 : bodyToolCodes.Count,
            mode == BodyToolRestrictionMode.Disabled ? 0 : wearableBodyToolCodes.Count,
            mode == BodyToolRestrictionMode.Default ? restrictedToolCodes.Count : 0,
            mode);
    }

    private static JsonObject FindOrAddTrait(JsonArray traits, string code)
    {
        foreach (JsonNode? node in traits)
        {
            if (node is JsonObject trait
                && string.Equals(trait["code"]?.GetValue<string>(), code, StringComparison.Ordinal))
            {
                return trait;
            }
        }

        JsonObject created = new()
        {
            ["code"] = code,
            ["type"] = "positive",
            ["attributes"] = new JsonObject()
        };
        traits.Add(created);
        return created;
    }

    private static void SetStringArray(JsonObject target, string property, IEnumerable<string> values)
    {
        JsonArray array = new();
        foreach (string value in values) array.Add(value);
        target[property] = array;
    }
}
