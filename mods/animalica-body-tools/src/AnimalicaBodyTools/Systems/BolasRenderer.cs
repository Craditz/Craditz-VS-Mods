using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace AnimalicaBodyTools.Systems;

internal sealed class BolasRenderer : IRenderer
{
    private readonly ICoreClientAPI api;
    private readonly Dictionary<string,MultiTextureMeshRef> meshes = new();
    private readonly Matrixf matrix = new();
    private Entity[] animals = Array.Empty<Entity>();
    private readonly long listener;
    private Shape? shape;
    public double RenderOrder => .5;
    public int RenderRange => 48;

    internal BolasRenderer(ICoreClientAPI api)
    {
        this.api = api;
        listener = api.Event.RegisterGameTickListener(_ => {
            if (api.World.Player?.Entity is {} viewer)
                animals = api.World.GetEntitiesAround(viewer.Pos.XYZ,RenderRange,RenderRange,
                    entity => entity.Alive && entity.WatchedAttributes.HasAttribute(HuntingEffects.Bound));
        },200);
        api.Event.RegisterRenderer(this,EnumRenderStage.Opaque,"animalica-bolas");
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (animals.Length == 0 || api.World.Player?.Entity is not {} viewer) return;
        var render = api.Render;
        var previous = render.CurrentActiveShader;
        previous?.Stop();
        var pos = viewer.Pos;
        var shader = render.PreparedStandardShader((int)pos.X,(int)pos.InternalY,(int)pos.Z);
        try
        {
            shader.ViewMatrix = render.CameraMatrixOriginf;
            shader.ProjectionMatrix = render.CurrentProjectionMatrix;
            foreach (var animal in animals)
            {
                var code = animal.WatchedAttributes.GetString(HuntingEffects.Bound);
                if (code == null || !animal.Alive || animal.ShouldDespawn || animal.Pos.Dimension != pos.Dimension) continue;
                if (!meshes.TryGetValue(code,out var mesh))
                {
                    var item = api.World.GetItem(new AssetLocation("animalicabodytools",code));
                    if (item == null) continue;
                    shape ??= api.Assets.Get(new AssetLocation("animalicabodytools:shapes/item/ranged/entangled-bolas.json")).ToObject<Shape>();
                    api.Tesselator.TesselateShape(item,shape,out var data);
                    meshes[code] = mesh = render.UploadMultiTextureMesh(data);
                }
                var box = animal.SelectionBox;
                var at = animal.Pos.XYZ;
                var camera = viewer.CameraPos;
                float size = Math.Max(.25f,box.XSize);
                shader.RgbaLightIn = api.World.BlockAccessor.GetLightRGBs((int)at.X,(int)at.Y,(int)at.Z);
                shader.ModelMatrix = matrix.Identity().Translate(at.X-camera.X, at.Y-camera.Y+box.YSize*.22, at.Z-camera.Z)
                    .RotateY(animal.Pos.Yaw).Scale(size*1.22f,size*.85f,Math.Max(size,box.ZSize)*1.25f)
                    .Translate(-.5f,-.5f,-.5f).Values;
                render.RenderMultiTextureMesh(mesh,"tex");
            }
        }
        finally
        {
            shader.Stop();
            previous?.Use();
        }
    }

    public void Dispose()
    {
        api.Event.UnregisterRenderer(this,EnumRenderStage.Opaque);
        api.Event.UnregisterGameTickListener(listener);
        foreach (var mesh in meshes.Values) mesh.Dispose();
    }
}
