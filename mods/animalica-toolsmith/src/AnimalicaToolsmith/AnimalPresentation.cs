#if !LTO
using Toolsmith;
using Toolsmith.ToolTinkering;
using Toolsmith.Utils;
#endif
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace AnimalicaToolsmith;

public sealed record AnimalVisualParts(ItemStack Head, ItemStack Fitting, ItemStack? Binding, bool Bundle)
{
    // Condition/sharpness changes do not allocate a new GPU mesh each time.
    public string CacheKey => $"{Bundle}|{Head.Collectible.Code}|{Fitting.Collectible.Code}|{Binding?.Collectible.Code}";
}

public sealed record HeadPoseStep(Vec3f Pivot, Vec3f Rotation, Vec3f Translation, float Scale = 1);

/// <summary>Item presentation; WornPresentation supplies animated limb attachments.</summary>
public sealed class AnimalPresentation(ICoreClientAPI api) : IDisposable
{
    private readonly Dictionary<string, MultiTextureMeshRef> meshes = new();
    private readonly HashSet<string> failed = new();
    private readonly HashSet<string> rendered = new();
    private short atlasIteration = -1;
    // Value 1 is the deprecated HandFp target, still used by some renderers.
    public static bool Supports(EnumItemRenderTarget target) => (int)target == 1 || target is EnumItemRenderTarget.Gui or EnumItemRenderTarget.Ground or EnumItemRenderTarget.HandTp or EnumItemRenderTarget.HandTpOff;
    public static Item? ResolveItem(IWorldAccessor clientWorld, ItemStack component) => clientWorld.GetItem(component.Collectible.Code);

    public static AnimalVisualParts? Parts(ItemStack stack)
    {
#if LTO
        return IntegrationSystem.VisualParts(stack);
#else
        bool bundle = stack.Collectible.Code == new AssetLocation(IntegrationSystem.Domain, "partsbundle");
        if (!bundle && (!IntegrationSystem.Managed(stack) || !IntegrationSystem.IsWearable(stack.Collectible))) return null;
        if (!stack.Attributes.HasAttribute("tinkeredToolHead") || !stack.Attributes.HasAttribute("tinkeredToolHandle")) return null;
        ItemStack head = stack.GetToolhead(), fitting = stack.GetToolhandle();
        if (head?.Item == null || fitting?.Item == null || !IntegrationSystem.IsFitting(fitting.Collectible)) return null;
        return new(head, fitting, bundle ? null : stack.GetToolbinding(), bundle);
#endif
    }

    public void Apply(ItemStack stack, EnumItemRenderTarget target, ref ItemRenderInfo info)
    {
        if (!Supports(target) || Parts(stack) is not { } parts) return;
        short iteration = api.ItemTextureAtlas.UnknownTexturePosition.reloadIteration;
        if (iteration != atlasIteration) { Dispose(); atlasIteration = iteration; }
        if (!meshes.TryGetValue(parts.CacheKey, out MultiTextureMeshRef? mesh))
        {
            if (failed.Contains(parts.CacheKey)) return;
            try
            {
                MeshData data = Build(parts);
                mesh = api.Render.UploadMultiTextureMesh(data);
                meshes.Add(parts.CacheKey, mesh);
            }
            catch (Exception error)
            {
                failed.Add(parts.CacheKey);
                api.Logger.Error("[animalicatoolsmith] Presentation failed for {0}; retaining native mesh: {1}",parts.CacheKey,error);
                return;
            }
        }
        info.ModelRef = mesh;
        info.Transform = Transform(target, parts.Bundle);
        info.CullFaces = true;
        if(rendered.Add(target+"|"+parts.CacheKey))
            api.Logger.Debug("[animalicatoolsmith] Rendered components: target={0}, head={1}, fitting={2}, binding={3}",
                target,parts.Head.Collectible.Code,parts.Fitting.Collectible.Code,parts.Binding?.Collectible.Code);
    }

    public static ModelTransform Transform(EnumItemRenderTarget target, bool bundle) => target == EnumItemRenderTarget.Gui
        ? new ModelTransform { Origin = new(.5f,.5f,.5f), Rotation = new(145,-35,-8), Scale = bundle ? 1.25f : 1.65f, Rotate = false }
          : target == EnumItemRenderTarget.Ground
            ? new ModelTransform { Origin = new(.5f,0,.5f), Rotation = new(0,15,0), Scale = 1.3f }
            : HeldTransform(target);

    public static ModelTransform HeldTransform(EnumItemRenderTarget target)
    {
        // Native hand rendering translates by Origin before scaling. A GUI-style
        // .5 pivot therefore offsets a small animal's carried item by half a
        // block. Cancel that unscaled pivot; keep rotation around the cuff center
        // so PML's animated paw/mouth anchor supplies the actual carried position.
        const float scale=.25f;
        var center=new Vec3f(.5f,7.5f/16,.5f);
        return new ModelTransform
        {
            Origin=center,Translation=new(-center.X/scale,-center.Y/scale,-center.Z/scale),
            Rotation=target==EnumItemRenderTarget.HandTpOff ? new(90,180,0) : new(-90,0,0),Scale=scale
        };
    }

    private MeshData Build(AnimalVisualParts parts)
    {
        // Toolsmith resolves stored stacks through its static API, which can be
        // the server in singleplayer. GPU resources must use client collectibles.
        Item fittingItem = ResolveItem(api.World,parts.Fitting) ?? throw new InvalidOperationException("Client fitting item is missing.");
        Item headItem = ResolveItem(api.World,parts.Head) ?? throw new InvalidOperationException("Client head item is missing.");
        api.Tesselator.TesselateItem(fittingItem, out MeshData fitting);
        fitting = fitting.Clone();
        if (parts.Bundle) fitting.Translate(-.18f,0,0);
        api.Tesselator.TesselateItem(headItem, out MeshData head);
        head = head.Clone();
        // Native head shapes still contain transparent handle geometry. Measure
        // only the head's opaque texture regions so invisible handles cannot
        // shrink, offset or enlarge the displayed head.
        TextureAtlasPosition[] visible = headItem.Textures.Keys
            .Where(key => key is not ("handle" or "string" or "wood"))
            .Select(key => api.ItemTextureAtlas.GetPosition(headItem,key,true))
            .Where(pos => pos != null).ToArray();
        PrepareHead(head,headItem.Code,visible,parts.Bundle);
        var displayedBounds = Bounds(head,visible);
        api.Logger.Debug("[animalicatoolsmith] Presented head {0}: source={1}, bundle={2}, span=({3:0.###}, {4:0.###}, {5:0.###})",
            headItem.Code,headItem.Shape?.Base,parts.Bundle,
            displayedBounds.Max[0]-displayedBounds.Min[0],displayedBounds.Max[1]-displayedBounds.Min[1],displayedBounds.Max[2]-displayedBounds.Min[2]);
        fitting.AddMeshData(head);
        if (parts.Binding != null && ResolveItem(api.World,parts.Binding) is { } binding)
        {
            Shape shape = Shape.TryGet(api,new AssetLocation(IntegrationSystem.Domain,"shapes/item/fastening.json"));
            TextureAtlasPosition position = api.ItemTextureAtlas.GetPosition(binding,"metal",true)
                ?? api.ItemTextureAtlas.GetPosition(binding,"material",true)
                ?? api.ItemTextureAtlas.GetPosition(binding,null,true)
                ?? api.ItemTextureAtlas.UnknownTexturePosition;
            api.Tesselator.TesselateShape("Animalica fitting fastening",shape,out MeshData fastenings,new BindingTexture(api,position));
            fitting.AddMeshData(fastenings);
        }
        return fitting;
    }

    public static void PrepareHead(MeshData head, AssetLocation code, TextureAtlasPosition[] visible, bool bundle, Action<HeadPoseStep>? record = null)
    {
        void Turn(float x,float y,float z) { head.Rotate(Vec3f.Zero,x,y,z); record?.Invoke(new(Vec3f.Zero,new(x,y,z),Vec3f.Zero)); }
        void Move(float x,float y,float z) { head.Translate(x,y,z); record?.Invoke(new(Vec3f.Zero,Vec3f.Zero,new(x,y,z))); }
        var bounds = Bounds(head, visible);
        float[] span = Enumerable.Range(0,3).Select(i => bounds.Max[i]-bounds.Min[i]).ToArray();
        int thin = Array.IndexOf(span,span.Min());
        // Toolsmith's simple pick is almost equally wide and thick. Guessing
        // its thin axis rolls its curve sideways before our intended rotation.
        if (bundle || !IsPickHead(code))
        {
            if (thin == 2) Turn(GameMath.PIHALF,0,0);
            else if (thin == 0) Turn(0,0,GameMath.PIHALF);
        }
        bounds = Bounds(head,visible);
        if (bounds.Max[0]-bounds.Min[0] > bounds.Max[2]-bounds.Min[2]) Turn(0,GameMath.PIHALF,0);
        bounds = Bounds(head,visible);
        float longest = Enumerable.Range(0,3).Max(i => bounds.Max[i]-bounds.Min[i]);
        float scale = (bundle ? .65f : .88f) / Math.Max(longest,.01f);
        Move(-(bounds.Min[0]+bounds.Max[0])/2,-bounds.Min[1],-(bounds.Min[2]+bounds.Max[2])/2);
        head.Scale(Vec3f.Zero,scale,scale,scale);
        record?.Invoke(new(Vec3f.Zero,Vec3f.Zero,Vec3f.Zero,scale));
        if (!bundle) AdjustMountedHead(head, code, new Vec3f(0,(bounds.Max[1]-bounds.Min[1])*scale/2,0),record);
        Move(bundle ? .98f : .5f, bundle ? .28f : .84f, bundle ? .5f : .33f);
    }

    public static void AdjustMountedHead(MeshData head, AssetLocation code, Vec3f center, Action<HeadPoseStep>? record = null)
    {
        if (code.Domain != "game") return;
        if (IsPickHead(code))
        {
            // Measure the loaded head: Toolsmith replaces vanilla shapes, and
            // metal names alone cannot tell us which way their curve bends.
            // Roll around the head's existing length, keeping its original
            // alignment on the fitting. Do not yaw the head across the cuff.
            float roll = PickCurveOffset(head) > 0 ? -GameMath.PIHALF : GameMath.PIHALF;
            head.Rotate(center,0,0,roll);
            record?.Invoke(new(center,new(0,0,roll),Vec3f.Zero));
            head.Scale(center,1.2f,1.2f,1.2f);
            record?.Invoke(new(center,Vec3f.Zero,Vec3f.Zero,1.2f));
        }
        else if (code.Path.StartsWith("scythehead-",StringComparison.Ordinal))
        {
            head.Scale(center,1.6f,1.6f,1.6f);
            record?.Invoke(new(center,Vec3f.Zero,Vec3f.Zero,1.6f));
        }
        else if (code.Path.StartsWith("shovelhead-",StringComparison.Ordinal))
        {
            // Turn the scoop over around its blade length, retaining projection.
            head.Rotate(center,0,0,GameMath.PI);
            record?.Invoke(new(center,new(0,0,GameMath.PI),Vec3f.Zero));
            head.Translate(0,0,-.18f);
            record?.Invoke(new(Vec3f.Zero,Vec3f.Zero,new(0,0,-.18f)));
        }
    }

    public static bool IsPickHead(AssetLocation code) => code.Domain == "game" && code.Path.StartsWith("pickaxehead-",StringComparison.Ordinal);

    public static float PickCurveOffset(MeshData mesh, int transverseAxis = 0)
    {
        float low=float.MaxValue,high=float.MinValue;
        for (int n=0;n<mesh.VerticesCount;n++) { low=Math.Min(low,mesh.xyz[n*3+2]); high=Math.Max(high,mesh.xyz[n*3+2]); }
        float width=high-low,middle=(low+high)/2;
        double tips=0,seat=0;int tipCount=0,seatCount=0;
        for (int n=0;n<mesh.VerticesCount;n++)
        {
            float z=mesh.xyz[n*3+2],x=mesh.xyz[n*3+transverseAxis];
            if (z<=low+width*.15f || z>=high-width*.15f) { tips+=x;tipCount++; }
            if (Math.Abs(z-middle)<=width*.15f) { seat+=x;seatCount++; }
        }
        return tipCount>0 && seatCount>0 ? (float)(tips/tipCount-seat/seatCount) : 0;
    }

    private static (float[] Min,float[] Max) Bounds(MeshData mesh, TextureAtlasPosition[] visible)
    {
        var min = new[] {float.MaxValue,float.MaxValue,float.MaxValue};
        var max = new[] {float.MinValue,float.MinValue,float.MinValue};
        bool found = false;
        for (int n=0;n<mesh.VerticesCount;n++)
        {
            float u=mesh.Uv[n*2],v=mesh.Uv[n*2+1];
            if (visible.Length>0 && !visible.Any(pos => u>=pos.x1-.00001 && u<=pos.x2+.00001 && v>=pos.y1-.00001 && v<=pos.y2+.00001)) continue;
            found=true;
            for(int axis=0;axis<3;axis++) { float value=mesh.xyz[n*3+axis]; min[axis]=Math.Min(min[axis],value); max[axis]=Math.Max(max[axis],value); }
        }
        if (!found && visible.Length>0) return Bounds(mesh,Array.Empty<TextureAtlasPosition>());
        if (!found) throw new InvalidOperationException("Cannot present an empty native tool-head mesh.");
        return (min,max);
    }

    public void Dispose()
    {
        foreach(MultiTextureMeshRef mesh in meshes.Values) if (!mesh.Disposed) mesh.Dispose();
        meshes.Clear();
        failed.Clear();
    }

    private sealed class BindingTexture(ICoreClientAPI capi, TextureAtlasPosition position) : ITexPositionSource
    {
        public Size2i AtlasSize => capi.ItemTextureAtlas.Size;
        public TextureAtlasPosition this[string textureCode] => position;
    }
}
