using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace AnimalicaBodyTools.Rendering;

/// <summary>
/// Uses a larger family-specific mesh in inventory and handbook views without
/// changing the shape PlayerModelLib attaches to an animal.
/// </summary>
public sealed class CollectibleBehaviorReadableBodyToolIcon : CollectibleBehavior
{
    private AssetLocation? inventoryShapeBase;
    private MultiTextureMeshRef? inventoryMeshRef;
    private bool missingShapeLogged;

    public CollectibleBehaviorReadableBodyToolIcon(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void Initialize(JsonObject properties)
    {
        string? shapeBase = properties["shape"]["base"].AsString();
        inventoryShapeBase = shapeBase == null
            ? null
            : AssetLocation.Create(shapeBase, collObj.Code.Domain);
        base.Initialize(properties);
    }

    public override void OnBeforeRender(
        ICoreClientAPI capi,
        ItemStack itemstack,
        EnumItemRenderTarget target,
        ref ItemRenderInfo renderinfo
    )
    {
        if (target != EnumItemRenderTarget.Gui || inventoryShapeBase == null)
        {
            return;
        }

        if (inventoryMeshRef == null)
        {
            AssetLocation shapePath = inventoryShapeBase.CopyWithPathPrefixAndAppendixOnce("shapes/", ".json");
            Shape? shape = Shape.TryGet(capi, shapePath);
            if (shape == null)
            {
                if (!missingShapeLogged)
                {
                    capi.Logger.Warning(
                        "[animalicabodytools] Readable inventory shape {0} for {1} was not found.",
                        shapePath,
                        collObj.Code
                    );
                    missingShapeLogged = true;
                }

                return;
            }

            capi.Tesselator.TesselateShape(collObj, shape, out MeshData meshData);
            inventoryMeshRef = capi.Render.UploadMultiTextureMesh(meshData);
        }

        renderinfo.ModelRef = inventoryMeshRef;
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        inventoryMeshRef?.Dispose();
        inventoryMeshRef = null;
        base.OnUnloaded(api);
    }
}
