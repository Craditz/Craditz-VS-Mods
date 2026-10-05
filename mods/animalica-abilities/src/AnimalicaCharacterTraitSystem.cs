using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ScentTrails;

/// <summary>
/// Adds the sensory traits to PlayerModelLib race definitions. Animalica Core
/// then carries those ExtraTraits into the character creator and player data
/// as normal race traits.
/// </summary>
public sealed class AnimalicaRaceTraitSystem : ModSystem
{
    public override double ExecuteOrder() => 0.05;

    public override void AssetsLoaded(ICoreAPI api)
    {
        int changedModels = 0;
        int changedFiles = 0;

        foreach (IAsset asset in api.Assets.GetManyInCategory("config", "customplayermodels/", null, true))
        {
            JObject root;
            try
            {
                root = JObject.Parse(asset.ToText());
            }
            catch (JsonException)
            {
                continue;
            }

            bool changedFile = false;
            foreach (JProperty property in root.Properties())
            {
                if (property.Value is not JObject model)
                {
                    continue;
                }

                AnimalicaAbilityProfile profile = AnimalicaAbilityResolver.ResolveModel(property.Name);
                if (IsUnknownProfile(profile) || !LooksLikeCustomModelDefinition(model))
                {
                    continue;
                }

                string? sensoryTrait = GetSensoryTraitCode(profile);
                string? activeTrait = GetActiveTraitCode(profile);
                JArray? existingTraitsToken = model["ExtraTraits"] as JArray;
                if (existingTraitsToken == null && sensoryTrait == null && activeTrait == null)
                {
                    continue;
                }

                JArray existingTraits = existingTraitsToken ?? new JArray();
                JArray desiredTraits = new();

                foreach (JToken trait in existingTraits)
                {
                    string? code = trait.Value<string>();
                    if (code != null && !IsSensoryTrait(code) && !IsActiveTrait(code))
                    {
                        desiredTraits.Add(code);
                    }
                }

                if (sensoryTrait != null)
                {
                    desiredTraits.Add(sensoryTrait);
                }

                if (activeTrait != null)
                {
                    desiredTraits.Add(activeTrait);
                }

                if (!JToken.DeepEquals(existingTraits, desiredTraits))
                {
                    model["ExtraTraits"] = desiredTraits;
                    changedFile = true;
                    changedModels++;
                }
            }

            if (changedFile)
            {
                asset.Data = Encoding.UTF8.GetBytes(root.ToString(Formatting.Indented));
                asset.IsPatched = true;
                changedFiles++;
            }
        }

        api.Logger.Notification(
            "[AnimalicaAbilities] Added race sensory traits to {0} model entries across {1} model files.",
            changedModels,
            changedFiles
        );
    }

    private static bool IsUnknownProfile(AnimalicaAbilityProfile profile)
    {
        return profile.Race.Equals("unknown", StringComparison.Ordinal);
    }

    private static bool LooksLikeCustomModelDefinition(JObject model)
    {
        return model["ShapePath"]?.Type == JTokenType.String
            && (model["Group"]?.Type == JTokenType.String
                || model["BaseShapeCode"]?.Type == JTokenType.String);
    }

    private static string? GetSensoryTraitCode(AnimalicaAbilityProfile profile)
    {
        if (profile.HasScent && profile.HasNightSight)
        {
            return AnimalicaAbilityResolver.BothTraitCode;
        }

        if (profile.HasScent)
        {
            return AnimalicaAbilityResolver.ScentTraitCode;
        }

        if (profile.HasNightSight)
        {
            return AnimalicaAbilityResolver.NightSightTraitCode;
        }

        return null;
    }

    private static bool IsSensoryTrait(string code)
    {
        return code.Equals(AnimalicaAbilityResolver.ScentTraitCode, StringComparison.Ordinal)
            || code.Equals(AnimalicaAbilityResolver.NightSightTraitCode, StringComparison.Ordinal)
            || code.Equals(AnimalicaAbilityResolver.BothTraitCode, StringComparison.Ordinal);
    }

    private static string? GetActiveTraitCode(AnimalicaAbilityProfile profile)
    {
        return profile.ActiveAbility switch
        {
            AnimalicaActiveAbility.Pounce => AnimalicaAbilityResolver.PounceTraitCode,
            AnimalicaActiveAbility.Adrenaline => AnimalicaAbilityResolver.AdrenalineTraitCode,
            _ => null
        };
    }

    private static bool IsActiveTrait(string code)
    {
        return code.Equals(AnimalicaAbilityResolver.PounceTraitCode, StringComparison.Ordinal)
            || code.Equals(AnimalicaAbilityResolver.AdrenalineTraitCode, StringComparison.Ordinal);
    }
}
