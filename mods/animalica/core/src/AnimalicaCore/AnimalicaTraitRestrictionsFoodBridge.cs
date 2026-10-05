using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace AnimalicaCore;

// Trait Restrictions uses the first matching food override. Give each food
// module its own values, then replace overlapping modules on a model with one
// combined trait whose values are resolved from the same diet data.
internal sealed class AnimalicaTraitRestrictionsFoodBridge
{
    internal const string CombinedPrefix = "animalica-food-combined-";
    internal const string LegacyProfilePrefix = "animalica-food-profile-";
    private const string FoodTraitPrefix = "animalica-food-";

    private readonly Dictionary<string, string> combinedBySignature = new(StringComparer.Ordinal);
    private AnimalicaDietDocument? diets;

    public void Prepare(ICoreAPI api)
    {
        combinedBySignature.Clear();
        diets = api.Assets.TryGet(new AssetLocation("animalicacore:config/animalica/diets.json"))
            ?.ToObject<AnimalicaDietDocument>();
        if (diets == null)
        {
            api.Logger.Error("[AnimalicaCore] Cannot prepare Trait Restrictions food traits: diets.json is missing.");
            return;
        }

        foreach (AnimalicaFoodDefinition food in diets.Foods)
        {
            food.TryNormalize(api.Logger, out _);
        }

        var signatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (IAsset asset in api.Assets.GetMany("config/customplayermodels", null, true))
        {
            string source = asset.ToText();
            if (source.IndexOf("animalica", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            try
            {
                foreach (JObject model in JObject.Parse(source).Properties()
                    .Select(property => property.Value)
                    .OfType<JObject>()
                    .Where(AnimalicaCoreSystem.IsAnimalicaModel))
                {
                    string[] modules = GetFoodModules(model);
                    if (modules.Length > 1)
                    {
                        signatures.Add(GetSignature(modules));
                    }
                }
            }
            catch (JsonException exception)
            {
                api.Logger.Warning("[AnimalicaCore] Could not inspect food traits in {0}: {1}", asset.Location, exception.Message);
            }
        }

        IAsset? traitAsset = api.Assets.TryGet(new AssetLocation("animalicacore:config/traits.json"), true);
        if (traitAsset == null)
        {
            api.Logger.Error("[AnimalicaCore] Cannot prepare Trait Restrictions food traits: traits.json is missing.");
            return;
        }

        JArray traits;
        try
        {
            traits = JArray.Parse(traitAsset.ToText());
        }
        catch (JsonException exception)
        {
            api.Logger.Error("[AnimalicaCore] Cannot prepare Trait Restrictions food traits: {0}", exception.Message);
            return;
        }

        int individualCount = 0;
        foreach (string module in diets.TraitAffinities.Keys
            .Where(IsFoodModule)
            .OrderBy(code => code, StringComparer.Ordinal))
        {
            JObject? trait = traits.OfType<JObject>()
                .FirstOrDefault(entry => entry.Value<string>("code") == module);
            if (trait == null)
            {
                api.Logger.Warning("[AnimalicaCore] Food trait definition {0} is missing.", module);
                continue;
            }

            JObject allowed = BuildAllowedFood([module]);
            if (allowed.Properties().Any())
            {
                trait["AllowedFood"] = allowed;
                individualCount++;
            }
        }

        var codeByFoodRules = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string signature in signatures
            .OrderBy(value => value.Count(character => character == '\u001f'))
            .ThenBy(value => value, StringComparer.Ordinal))
        {
            string[] modules = signature.Split('\u001f');
            JObject allowed = BuildAllowedFood(modules);
            if (!allowed.Properties().Any())
            {
                continue;
            }

            string foodRules = GetFoodRulesKey(modules);
            if (codeByFoodRules.TryGetValue(foodRules, out string? existingCode))
            {
                combinedBySignature.Add(signature, existingCode);
                continue;
            }

            string code = CombinedPrefix + string.Join("--",
                modules.Select(module => module[FoodTraitPrefix.Length..]));
            codeByFoodRules.Add(foodRules, code);
            combinedBySignature.Add(signature, code);
            traits.Add(new JObject
            {
                ["code"] = code,
                ["type"] = "positive",
                ["attributes"] = new JObject(),
                ["AllowedFood"] = allowed
            });
        }

        traitAsset.Data = Encoding.UTF8.GetBytes(traits.ToString(Formatting.Indented));
        traitAsset.IsPatched = true;
        api.Logger.Notification(
            "[AnimalicaCore] Prepared {0} individual and {1} combined food trait(s) for Trait Restrictions.",
            individualCount,
            codeByFoodRules.Count
        );
    }

    public bool CombineFoodTraits(JObject model)
    {
        string[] modules = GetFoodModules(model);
        if (modules.Length < 2
            || !combinedBySignature.TryGetValue(GetSignature(modules), out string? combinedCode)
            || model["ExtraTraits"] is not JArray traits)
        {
            return false;
        }

        var moduleSet = new HashSet<string>(modules, StringComparer.Ordinal);
        JArray combinedTraits = new();
        bool inserted = false;
        foreach (JToken trait in traits)
        {
            if (trait.Value<string>() is string code && moduleSet.Contains(code))
            {
                if (!inserted)
                {
                    combinedTraits.Add(combinedCode);
                    inserted = true;
                }
            }
            else
            {
                combinedTraits.Add(trait.DeepClone());
            }
        }

        model["ExtraTraits"] = combinedTraits;
        return true;
    }

    internal static bool IsFoodAccessTrait(string code) =>
        code.StartsWith(FoodTraitPrefix, StringComparison.Ordinal);

    internal static bool IsGeneratedFoodTrait(string code) =>
        code.StartsWith(CombinedPrefix, StringComparison.Ordinal)
        || code.StartsWith(LegacyProfilePrefix, StringComparison.Ordinal);

    internal static IEnumerable<string> ExpandCombinedFoodTrait(string code)
    {
        if (!code.StartsWith(CombinedPrefix, StringComparison.Ordinal))
        {
            yield return code;
            yield break;
        }

        foreach (string component in code[CombinedPrefix.Length..].Split("--", StringSplitOptions.RemoveEmptyEntries))
        {
            yield return FoodTraitPrefix + component;
        }
    }

    private string[] GetFoodModules(JObject model)
    {
        if (diets == null || model["ExtraTraits"] is not JArray modelTraits)
        {
            return [];
        }

        return modelTraits.Values<string>()
            .Where(code => code != null && IsFoodModule(code) && diets.TraitAffinities.ContainsKey(code))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray()!;
    }

    private static bool IsFoodModule(string code) =>
        code.StartsWith(FoodTraitPrefix, StringComparison.Ordinal)
        && !IsGeneratedFoodTrait(code);

    private static string GetSignature(IEnumerable<string> modules) => string.Join("\u001f", modules);

    private JObject BuildAllowedFood(IEnumerable<string> modules)
    {
        var allowed = new JObject();
        if (diets == null)
        {
            return allowed;
        }

        foreach (AnimalicaFoodDefinition food in diets.Foods.Where(entry => entry.StackAttributeAtMost == null))
        {
            decimal affinity = GetAffinity(modules, food.Tag);

            if (affinity <= 0m)
            {
                continue;
            }

            allowed[food.Code] = new JObject
            {
                ["Satiety"] = Math.Round((decimal)food.BaseSatiety * affinity, 0, MidpointRounding.AwayFromZero),
                ["Health"] = 0,
                ["FoodCategory"] = food.FoodCategory
            };
        }

        return allowed;
    }

    private string GetFoodRulesKey(IEnumerable<string> modules)
    {
        var affinities = new JObject();
        foreach (AnimalicaFoodDefinition food in diets!.Foods)
        {
            affinities[food.Tag] = GetAffinity(modules, food.Tag);
        }

        return affinities.ToString(Formatting.None);
    }

    private decimal GetAffinity(IEnumerable<string> modules, string foodTag)
    {
        decimal affinity = 0m;
        if (diets == null)
        {
            return affinity;
        }

        foreach (string trait in modules)
        {
            if (diets.TraitAffinities.TryGetValue(trait, out Dictionary<string, string>? grants)
                && grants.TryGetValue(foodTag, out string? level)
                && diets.AffinityMultipliers.TryGetValue(level, out decimal multiplier))
            {
                affinity = Math.Max(affinity, multiplier);
            }
        }

        return affinity;
    }
}
