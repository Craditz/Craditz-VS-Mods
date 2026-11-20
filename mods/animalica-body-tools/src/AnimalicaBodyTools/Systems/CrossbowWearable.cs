using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace AnimalicaBodyTools.Systems;

public sealed class CrossbowWearable : CollectibleBehaviorWearable
{
    public CrossbowWearable(CollectibleObject collectible) : base(collectible) { }

    protected override MeshData genFullBodyMesh(ICoreClientAPI api, ItemStack stack, ITexPositionSource textures)
    {
        // Draw only the crossbow in inventory. The worn model still follows UpperTorso.
        var shapePath = stack.Item.Shape.Base.CopyWithPathPrefixAndAppendixOnce("shapes/", ".json");
        var shape = Shape.TryGet(api, shapePath);
        api.Tesselator.TesselateShape(stack.Item, shape, out var mesh);
        // Move the torso-local model to the inventory tile centre.
        return mesh.Translate((8f - .75f) / 16f, (8f - 6.85f) / 16f, (8f - 1.85f) / 16f);
    }
}
