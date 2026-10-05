using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace AnimalicaBodyTools.Util;

internal static class WearableShapeUtil
{
    private const string EquippedShapeAttribute = "animalicaEquippedShape";

    public static Shape? TryGetEquippedShape(
        CollectibleObject collectible,
        Entity forEntity,
        string texturePrefixCode)
    {
        string? shapeCode = collectible.Attributes?[EquippedShapeAttribute].AsString();
        if (string.IsNullOrWhiteSpace(shapeCode))
        {
            return null;
        }

        AssetLocation location = AssetLocation.Create(shapeCode, collectible.Code.Domain)
            .WithPathPrefixOnce("shapes/")
            .WithPathAppendixOnce(".json");

        Shape? shape = Shape.TryGet(forEntity.Api, location);
        shape?.SubclassForStepParenting(texturePrefixCode, 0f);
        return shape;
    }
}
