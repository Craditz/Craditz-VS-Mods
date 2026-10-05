using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace AnimalicaCore;

/// <summary>
/// Food Shelves 3.0.5 applies its full storage whitelist on the server, but
/// only loads the vegetable-basket restriction on clients. Mirror its
/// meat-freezer code whitelist after multiplayer item definitions arrive so
/// the client's freezer interaction sees the same compatibility attributes.
/// </summary>
public sealed class AnimalicaFoodShelvesCompatibilitySystem : ModSystem
{
    private const string RestrictionAssetCode = "foodshelves:config/restrictions/coolers/meatfreezer.json";
    private const string FreezerAttribute = "fsmeatfreezer";

    private bool applied;

    public override void StartClientSide(ICoreClientAPI api)
    {
        if (!api.ModLoader.IsModEnabled("foodshelves"))
        {
            return;
        }

        api.Event.BlockTexturesLoaded += () =>
        {
            if (applied)
            {
                return;
            }

            applied = true;
            ApplyClientWhitelist(api);
        };
    }

    private static void ApplyClientWhitelist(ICoreClientAPI api)
    {
        IAsset? asset = api.Assets.TryGet(new AssetLocation(RestrictionAssetCode));
        if (asset == null)
        {
            api.Logger.Warning(
                "[AnimalicaCore] Food Shelves is enabled, but its meat-freezer restriction asset was not found: {0}.",
                RestrictionAssetCode
            );
            return;
        }

        AnimalicaFoodShelvesRestriction? restriction;
        try
        {
            restriction = asset.ToObject<AnimalicaFoodShelvesRestriction>();
        }
        catch (Exception exception)
        {
            api.Logger.Warning(
                "[AnimalicaCore] Could not read Food Shelves meat-freezer restrictions: {0}",
                exception.Message
            );
            return;
        }

        string[]? allowedCodes = restriction?.CollectibleCodes;
        if (allowedCodes == null || allowedCodes.Length == 0)
        {
            api.Logger.Warning(
                "[AnimalicaCore] Food Shelves meat-freezer restrictions contain no collectible codes."
            );
            return;
        }

        string[] blacklistedCodes = restriction?.BlacklistedCodes ?? Array.Empty<string>();

        int matched = 0;
        int newlyMarked = 0;
        int alreadyMarked = 0;
        var matchedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (CollectibleObject collectible in api.World.Collectibles)
        {
            string? code = collectible.Code?.ToString();
            if (code == null
                || WildcardUtil.Match(blacklistedCodes, code)
                || !WildcardUtil.Match(allowedCodes, code))
            {
                continue;
            }

            matched++;
            matchedCodes.Add(code);

            JObject attributes = collectible.Attributes?.Token as JObject ?? new JObject();
            JProperty? marker = attributes.Properties().FirstOrDefault(property =>
                string.Equals(property.Name, FreezerAttribute, StringComparison.OrdinalIgnoreCase)
            );
            if (marker != null
                && marker.Value.Type == JTokenType.Boolean
                && marker.Value.Value<bool>())
            {
                alreadyMarked++;
                continue;
            }

            if (marker == null)
            {
                attributes[FreezerAttribute] = true;
            }
            else
            {
                marker.Value = true;
            }

            collectible.Attributes = new JsonObject(attributes);
            newlyMarked++;
        }

        var unmatchedCodes = allowedCodes
            .Where(code => !matchedCodes.Any(collectibleCode =>
                WildcardUtil.Match(new[] { code }, collectibleCode)))
            .ToArray();

        api.Logger.Notification(
            "[AnimalicaCore] Food Shelves client meat-freezer whitelist after BlockTexturesLoaded: " +
            "matched={0}, newlyMarked={1}, alreadyMarked={2}, matchedCodes=[{3}], unmatchedWhitelistCodes=[{4}].",
            matched,
            newlyMarked,
            alreadyMarked,
            string.Join(", ", matchedCodes.OrderBy(code => code, StringComparer.OrdinalIgnoreCase)),
            string.Join(", ", unmatchedCodes)
        );

        if (matched == 0)
        {
            api.Logger.Warning(
                "[AnimalicaCore] Food Shelves meat-freezer whitelist matched no client collectibles."
            );
        }
    }

    private sealed class AnimalicaFoodShelvesRestriction
    {
        [JsonProperty("CollectibleCodes")]
        public string[]? CollectibleCodes { get; set; }

        [JsonProperty("BlacklistedCodes")]
        public string[]? BlacklistedCodes { get; set; }
    }
}
