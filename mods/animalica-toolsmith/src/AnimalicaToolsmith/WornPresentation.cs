using System.Reflection;
using HarmonyLib;
using PlayerModelLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AnimalicaToolsmith;

/// <summary>CPU shapes follow the entity's animated joints; item meshes keep their accepted GUI pose.</summary>
public sealed class WornPresentation(ICoreClientAPI api,System.Func<AssetLocation,Shape>? loadShape = null) : IDisposable
{
    private readonly HashSet<string> reported = new();

    public void Start(Harmony harmony)
    {
        WearablesTesselatorBehavior.BeforeWearableShapeAttached += PlayerShape;
        MethodInfo native = typeof(EntityBehaviorContainer).GetMethods(BindingFlags.Public|BindingFlags.Static)
            .Single(m => m.Name == "addGearToShape");
        harmony.Patch(native,prefix:new HarmonyMethod(typeof(WornPresentation),nameof(NativeShape)) { priority=Priority.First });
    }

    public void Dispose() => WearablesTesselatorBehavior.BeforeWearableShapeAttached -= PlayerShape;

    private void PlayerShape(WearablesTesselatorBehavior behavior,IInventory inventory,ItemSlot slot,
        ref Shape entityShape,ref string[] willDeleteElements,ref Shape? attachableShape,ref CompositeShape? composite)
    {
        if (slot.Empty || AnimalPresentation.Parts(slot.Itemstack!) is not { Bundle:false } parts) return;
        try
        {
            Shape shape = Build(parts,entityShape,behavior.PlayerEntity.WatchedAttributes.GetString("skinModel",""));
            if (shape.Elements.Length == 0) return;
            attachableShape=shape;composite=null;
            Report(behavior.PlayerEntity,parts,shape);
        }
        catch(Exception error) { api.Logger.Error("[animalicatoolsmith] Worn presentation failed; retaining native attachment: {0}",error); }
    }

    // Native containers are used by Player Model NPCs and ordinary dressed entities.
    public static bool NativeShape(ICoreAPI api,Entity optionalTargetEntity,ITextureAtlasAPI targetAtlas,
        Shape entityShape,ItemStack stack,IAttachableToEntity iatta,string slotCode,string shapePathForLogging,
        IDictionary<string,CompositeTexture> collectedTextures,ref Shape __result)
    {
        if (api is not ICoreClientAPI client || optionalTargetEntity == null ||
            AnimalPresentation.Parts(stack) is not { Bundle:false } parts) return true;
        WornPresentation? presenter=client.ModLoader.GetModSystem<IntegrationSystem>().Worn;
        if(presenter == null) return true;
        try
        {
            Shape shape=presenter.Build(parts,entityShape,optionalTargetEntity.WatchedAttributes.GetString("skinModel",""));
            if(shape.Elements.Length == 0) return true;
            string prefix=(iatta.GetTexturePrefixCode(stack) ?? "")+"ats-";
            shape.SubclassForStepParenting(prefix,0);
            shape.ResolveReferences(api.Logger,"Animalica Toolsmith attachment");
            entityShape.StepParentShape(shape,"Animalica Toolsmith components",shapePathForLogging,api.Logger,
                (key,location) => EntityBehaviorContainer.addTexture(client,key,location,collectedTextures,prefix,targetAtlas));
            presenter.Report(optionalTargetEntity,parts,shape);
            __result=entityShape;return false;
        }
        catch(Exception error)
        {
            api.Logger.Error("[animalicatoolsmith] NPC presentation failed; retaining native attachment: {0}",error);
            return true;
        }
    }

    private void Report(Entity entity,AnimalVisualParts parts,Shape shape)
    {
        string model=entity.WatchedAttributes.GetString("skinModel","unknown");
        lock(reported)
            if(reported.Add(model+"|"+parts.CacheKey))
                api.Logger.Debug("[animalicatoolsmith] Attached components: model={0}, head={1}, fitting={2}, binding={3}, limbs={4}",
                    model,parts.Head.Collectible.Code,parts.Fitting.Collectible.Code,parts.Binding?.Collectible.Code,
                    string.Join(",",shape.Elements.Select(e=>e.StepParentName)));
    }

    public Shape Build(AnimalVisualParts parts,Shape body,string modelCode="")
    {
        // This callback can run inside entity tessellation. The engine's shared
        // tessellator is mutable and cannot safely be entered recursively.
        // Measurements need only CPU positions, so use an isolated local worker.
        var measurementTess=new Vintagestory.Client.NoObf.ShapeTesselator(null!,null!,null!,null!);
        var model=new Shape { Elements=Array.Empty<ShapeElement>(),Textures=new(),TextureSizes=new() };
        Item fitting=AnimalPresentation.ResolveItem(api.World,parts.Fitting)!;
        Item head=AnimalPresentation.ResolveItem(api.World,parts.Head)!;
        Shape seat=Load(fitting);Shape blade=Load(head);
        foreach(var e in All(blade.Elements))
            if(e.FacesResolved != null) for(int n=0;n<e.FacesResolved.Length;n++)
                if(e.FacesResolved[n]?.Texture.TrimStart('#') is "handle" or "string" or "wood") e.FacesResolved[n]=null!;
        // CPU tessellation measures the actual loaded head; dummy UVs need no atlas/GPU access.
        measurementTess.TesselateShape("Animalica head placement",blade,out MeshData measurement,new MeasurementTexture());
        var steps=new List<HeadPoseStep>();
        AnimalPresentation.PrepareHead(measurement,head.Code,Array.Empty<TextureAtlasPosition>(),false,steps.Add);
        AddPart(model,seat,fitting,"seat");AddPart(model,blade,head,"head");
        ShapeElement bladeRoot=Wrap("head geometry",blade.Elements);
        foreach(HeadPoseStep step in steps) bladeRoot=Transform(bladeRoot,step);
        var components=new List<ShapeElement> { Wrap("fitting geometry",seat.Elements),bladeRoot };
        if(parts.Binding != null && AnimalPresentation.ResolveItem(api.World,parts.Binding) is { } binding)
        {
            Shape fastening=Read(new AssetLocation(IntegrationSystem.Domain,"shapes/item/fastening.json"));
            var texture=binding.Textures.GetValueOrDefault("metal") ?? binding.Textures.GetValueOrDefault("material") ?? binding.Textures.Values.First();
            fastening.Textures=new() { ["binding"]=texture.Base };
            AddPart(model,fastening,null,"fastening");components.Add(Wrap("binding geometry",fastening.Elements));
        }
        var anchors=new List<ShapeElement>();
        bool hand=parts.Head.Collectible.Code.Path.StartsWith("knifeblade-",StringComparison.Ordinal)
            || parts.Head.Collectible.Code.Path.StartsWith("shovelhead-",StringComparison.Ordinal)
            || parts.Head.Collectible.Code.Path.StartsWith("scythehead-",StringComparison.Ordinal)
            || parts.Head.Collectible.Code.Path.StartsWith("hoehead-",StringComparison.Ordinal);
        // Item-space Y is the cuff's axis. Turning around it reverses the blade
        // along the leg while retaining its projection out from the cuff.
        ShapeElement assembly=Wrap("components",components.ToArray());
        assembly=Transform(assembly,new(Vec3f.Zero,Vec3f.Zero,new(-.5f,-7.5f/16,-.5f)));
        assembly=Transform(assembly,new(Vec3f.Zero,new(0,GameMath.PI,0),Vec3f.Zero));
        assembly=Transform(assembly,new(Vec3f.Zero,new(GameMath.PIHALF,0,0),Vec3f.Zero));
        var measuringShape=new Shape { Elements=new[]{assembly},Textures=model.Textures,TextureSizes=model.TextureSizes };
        measuringShape.ResolveReferences(api.Logger,"Animalica low mount measurement");
        measurementTess.TesselateShape("Animalica low mount",measuringShape,out MeshData assemblyMesh,new MeasurementTexture());
        float bottom=Enumerable.Range(0,assemblyMesh.VerticesCount).Min(n=>assemblyMesh.xyz[n*3+1]);
        foreach(string joint in AnchorNames(body,modelCode,hand))
        {
            ShapeElement? limb=body.GetElementByName(joint);
            if(limb?.From == null || limb.To == null) continue;
            if(limb.To[1]-limb.From[1] <= .05 || Math.Max(limb.To[0]-limb.From[0],limb.To[2]-limb.From[2]) <= .05) continue;
            ShapeElement root=CloneElement(assembly);
            // Animalica's right limb is on the negative-Z side of its rig.
            // Face the head out from that limb instead of between the legs.
            if(!hand) root=Transform(root,new(Vec3f.Zero,new(0,GameMath.PI,0),Vec3f.Zero));
            float width=(float)Math.Max(limb.To[0]-limb.From[0],limb.To[2]-limb.From[2]);
            float scale=Math.Clamp(width/6f,.05f,2f);
            root=Transform(root,new(Vec3f.Zero,Vec3f.Zero,Vec3f.Zero,scale));
            // Hoofed rigs continue below LowerArm through pastern/hoof children.
            // Measure those in the joint's own frame instead of treating its
            // rectangular helper box as the end of the visible limb.
            ShapeElement localLimb=CloneElement(limb);
            localLimb.From=new double[3];localLimb.To=Enumerable.Range(0,3).Select(i=>limb.To[i]-limb.From[i]).ToArray();
            localLimb.RotationOrigin=new double[3];localLimb.RotationX=localLimb.RotationY=localLimb.RotationZ=0;
            foreach(var element in All(new[]{localLimb}))
            {
                element.Children=element.Children?.Where(e=>e.Name?.StartsWith("ats",StringComparison.Ordinal)!=true).ToArray();
                element.ClimateColorMap=null;element.SeasonColorMap=null;
            }
            var limbShape=new Shape { Elements=new[]{localLimb} };
            limbShape.ResolveReferences(api.Logger,"Animalica visible limb base");
            measurementTess.TesselateShape("Animalica visible limb base",limbShape,out MeshData limbMesh,new MeasurementTexture());
            float limbBottom=limbMesh.VerticesCount>0 ? Enumerable.Range(0,limbMesh.VerticesCount).Min(n=>limbMesh.xyz[n*3+1]) : 0;
            var footVertices=Enumerable.Range(0,limbMesh.VerticesCount).Where(n=>limbMesh.xyz[n*3+1]<=limbBottom+.03f).ToArray();
            float centerX=footVertices.Length>0 ? (footVertices.Min(n=>limbMesh.xyz[n*3])+footVertices.Max(n=>limbMesh.xyz[n*3]))/2 : (float)(limb.To[0]-limb.From[0])/32;
            float centerZ=footVertices.Length>0 ? (footVertices.Min(n=>limbMesh.xyz[n*3+2])+footVertices.Max(n=>limbMesh.xyz[n*3+2]))/2 : (float)(limb.To[2]-limb.From[2])/32;
            // Actual head geometry determines the lowest safe mounting height.
            // Both sockets sit at the limb base; their side keeps them separate.
            float clearance=Math.Clamp(width*.05f,.05f,.2f)/16;
            root=Transform(root,new(Vec3f.Zero,Vec3f.Zero,new(
                centerX,
                limbBottom-bottom*scale+clearance,
                centerZ)));
            root.StepParentName=joint;anchors.Add(root);
        }
        model.Elements=anchors.ToArray();
        int index=0;foreach(var element in All(model.Elements)) element.Name="ats-worn-"+(index++);
        model.ResolveReferences(api.Logger,"Animalica component wearable");
        return model;
    }

    public static string[] AnchorNames(Shape body,string modelCode,bool hand=false)
    {
        string side=hand ? "L" : "R";
        bool usableArm=body.GetElementByName("LowerArm"+side) is { From:not null,To:not null } arm && arm.To[1]-arm.From[1]>.05;
        return modelCode.Split(':').Last().StartsWith("chicken-",StringComparison.Ordinal) || !usableArm
            ? new[]{"LowerFoot"+side} : new[]{"LowerArm"+side};
    }

    private Shape Load(Item item)
    {
        Shape shape=Read(item.Shape.Base.CopyWithPathPrefixAndAppendixOnce("shapes/",".json"));
        if(shape == null) throw new InvalidOperationException("Missing component shape: "+item.Shape.Base);
        if(item.Shape.rotateX != 0 || item.Shape.rotateY != 0 || item.Shape.rotateZ != 0)
            shape.Elements=new[]{Transform(Wrap("source pose",shape.Elements),new(new(.5f,.5f,.5f),
                new(item.Shape.rotateX*GameMath.DEG2RAD,item.Shape.rotateY*GameMath.DEG2RAD,item.Shape.rotateZ*GameMath.DEG2RAD),Vec3f.Zero))};
        return shape;
    }

    private Shape Read(AssetLocation location)
    {
        Shape shape=loadShape?.Invoke(location) ?? Shape.TryGet(api,location);
        Shape copy=shape.Clone();copy.Elements=shape.Elements.Select(CloneElement).ToArray();return copy;
    }

    // Native ShapeElement.Clone shares face objects. Prefixing a left attachment
    // must never rewrite its right twin, another NPC, or a cached source shape.
    public static ShapeElement CloneElement(ShapeElement source)
    {
        ShapeElement copy=source.Clone();
        foreach(var element in All(new[]{copy}))
        {
            // PML may remove a hidden limb's face array while retaining its
            // animated children. Native CPU tessellation expects six entries.
            element.FacesResolved ??= new ShapeElementFace[6];
            for(int n=0;n<element.FacesResolved.Length;n++)
                if(element.FacesResolved[n] is { } f) element.FacesResolved[n]=new ShapeElementFace
                {
                    Texture=f.Texture,Uv=(float[])f.Uv.Clone(),Enabled=f.Enabled,Glow=f.Glow,Rotation=f.Rotation,
                    ReflectiveMode=f.ReflectiveMode,WindMode=(sbyte[]?)f.WindMode?.Clone(),WindData=(sbyte[]?)f.WindData?.Clone()
                };
        }
        return copy;
    }

    private static void AddPart(Shape target,Shape part,Item? item,string prefix)
    {
        var textures=part.Textures?.ToDictionary(p=>p.Key,p=>p.Value) ?? new Dictionary<string,AssetLocation>();
        if(item?.Textures != null) foreach(var pair in item.Textures) textures[pair.Key]=pair.Value.Base;
        foreach(var pair in textures)
        {
            string name="ats"+prefix+"-"+pair.Key;
            target.Textures[name]=pair.Value;
            target.TextureSizes[name]=part.TextureSizes?.GetValueOrDefault(pair.Key) ?? new[]{part.TextureWidth,part.TextureHeight};
        }
        foreach(var element in All(part.Elements))
        {
            element.Name="ats"+prefix+"-"+element.Name;
            if(element.FacesResolved != null) foreach(var face in element.FacesResolved)
                if(face != null)
                {
                    string key=face.Texture.TrimStart('#');
                    // Some Toolsmith zero-volume origin helpers refer to unused '#0'.
                    if(!textures.ContainsKey(key) && element.From!.SequenceEqual(element.To!)) key=textures.Keys.First();
                    face.Texture="ats"+prefix+"-"+key;
                }
        }
    }

    public static IEnumerable<ShapeElement> All(IEnumerable<ShapeElement> roots)
    {
        foreach(var root in roots) { yield return root;foreach(var child in All(root.Children ?? Array.Empty<ShapeElement>())) yield return child; }
    }
    private static ShapeElement Wrap(string name,ShapeElement[] children) => new()
    { Name=name,From=new double[3],To=new double[3],Children=children,RotationOrigin=new double[3] };

    public static ShapeElement Transform(ShapeElement element,HeadPoseStep step)
    {
        ShapeElement wrapper=Wrap("pose",new[]{element});
        wrapper.From=new[]{(double)step.Translation.X*16,step.Translation.Y*16,step.Translation.Z*16};
        wrapper.To=(double[])wrapper.From.Clone();
        wrapper.RotationOrigin=new[]{(double)step.Pivot.X*16,step.Pivot.Y*16,step.Pivot.Z*16};
        wrapper.RotationX=step.Rotation.X*GameMath.RAD2DEG;wrapper.RotationY=step.Rotation.Y*GameMath.RAD2DEG;wrapper.RotationZ=step.Rotation.Z*GameMath.RAD2DEG;
        wrapper.ScaleX=wrapper.ScaleY=wrapper.ScaleZ=step.Scale;return wrapper;
    }

    private sealed class MeasurementTexture : ITexPositionSource
    {
        public Size2i AtlasSize => new(16,16);
        public TextureAtlasPosition this[string code] => new() { x1=0,y1=0,x2=1,y2=1,atlasTextureId=1 };
    }
}
