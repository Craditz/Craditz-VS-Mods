using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using AnimalicaBodyTools.Config;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace AnimalicaBodyTools.Util;

/// <summary>
/// Keeps Body Tools' virtual wearable and crafting bridges aligned with the
/// single legacy trait used by Trait Restrictions and Body Tools recipes.
/// </summary>
internal static class AnimalBodyToolTraitAccessUtil
{
    private const string AnimalBodyToolsTrait = "animalica-animal-bodytools";

    public static bool HolderHasTraitFast(EntityAgent byEntity)
    {
        return HolderHasTrait(byEntity, out _);
    }

    public static bool HolderHasTrait(EntityAgent byEntity, out string reason)
    {
        if (!AnimalicaBodyToolsPolicy.RestrictAnimalToolUse)
        {
            reason = "restrictions disabled";
            return true;
        }

        if (HasTrait(byEntity))
        {
            reason = "found animalica-animal-bodytools";
            return true;
        }

        reason = "missing animalica-animal-bodytools";
        return false;
    }

    private static bool HasTrait(EntityAgent byEntity)
    {
        string[]? watchedTraits = byEntity.WatchedAttributes?.GetStringArray("extraTraits", Array.Empty<string>());
        if (watchedTraits?.Contains(AnimalBodyToolsTrait, StringComparer.Ordinal) == true)
        {
            return true;
        }

        try
        {
            string modelCode = byEntity.WatchedAttributes?.GetString("skinModel", "seraph") ?? "seraph";
            object? modelSystem = byEntity.Api?.ModLoader.GetModSystem("PlayerModelLib.CustomModelsSystem");
            object? modelCollection = modelSystem?.GetType()
                .GetProperty("CustomModels", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(modelSystem);
            if (modelCollection is not IDictionary models || !models.Contains(modelCode))
            {
                return false;
            }

            object? currentModel = models[modelCode];
            string[]? modelTraits = currentModel?.GetType()
                .GetProperty("ExtraTraits", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(currentModel) as string[];
            return modelTraits?.Contains(AnimalBodyToolsTrait, StringComparer.Ordinal) == true;
        }
        catch
        {
            return false;
        }
    }
}
