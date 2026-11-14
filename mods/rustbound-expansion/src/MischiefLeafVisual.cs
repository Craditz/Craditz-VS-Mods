using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace RustboundExpansion;

// Client-only textured falling leaves. No item entities or inventory drops.
internal sealed class MischiefLeafVisual : IRenderer, IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly List<(Entity Target,long Start,int Dimension)> leaves=new();
    private readonly Matrixf matrix=new();
    private MeshRef? quad;
    private LoadedTexture? texture;
    private IShaderProgram? shader;
    private bool disposed, failed;
    internal MischiefLeafVisual(ICoreClientAPI api)
    {
        this.api=api; api.Event.RegisterRenderer(this,EnumRenderStage.AfterOIT,"rbx-mischief-leaf");
        api.Event.ReloadShader+=Reload;
    }
    public double RenderOrder=>.97;
    public int RenderRange=>64;
    internal int Count=>leaves.Count;
    internal void Add(Entity target)
    {
        if(disposed)return;
        if(leaves.Count>=64)leaves.RemoveAt(0);
        leaves.Add((target,api.World.ElapsedMilliseconds,target.Pos.Dimension));
    }
    private bool Reload()
    {
        shader?.Dispose();shader=null;quad?.Dispose();quad=null;texture?.Dispose();texture=null;failed=false;
        // Compile lazily so reloads before entering a world do not allocate.
        return true;
    }
    private bool Prepare()
    {
        if(failed)return false;
        if(shader!=null)return true;
        shader=api.Shader.NewShaderProgram();shader.AssetDomain="rustboundexpansion";
        shader.VertexShader=api.Shader.NewShader(EnumShaderType.VertexShader);
        shader.FragmentShader=api.Shader.NewShader(EnumShaderType.FragmentShader);
        api.Shader.RegisterFileShaderProgram("mischiefleaf",shader);
        if(!shader.Compile()){shader.Dispose();shader=null;failed=true;return false;}
        texture=new(api);api.Render.GetOrLoadTexture(new("rustboundexpansion:textures/particle/conjuredleaf.png"),ref texture);
        quad=api.Render.UploadMesh(Quad());return true;
    }
    internal static MeshData Quad()
    {
        var mesh=new MeshData(4,12,false,true,false,false){VerticesCount=4,IndicesCount=12};
        new float[]{-.18f,-.18f,0,.18f,-.18f,0,.18f,.18f,0,-.18f,.18f,0}.CopyTo(mesh.xyz,0);
        new float[]{0,1,1,1,1,0,0,0}.CopyTo(mesh.Uv,0);
        new int[]{0,1,2,0,2,3,2,1,0,3,2,0}.CopyTo(mesh.Indices,0);
        return mesh;
    }
    internal static Vec3d Position(Entity target,float age)
    {
        // Player level pitch is PI; using yaw with an assumed zero pitch reverses
        // its real view direction. Native look also handles creature conventions.
        var look=target.Pos.GetViewVector();
        double length=Math.Sqrt(look.X*look.X+look.Z*look.Z);
        if(length<.001)
        {
            look=EntityPos.GetViewVector(target is EntityPlayer ? GameMath.PI : 0,target.Pos.Yaw);
            length=Math.Sqrt(look.X*look.X+look.Z*look.Z);
        }
        double x=look.X/length,z=look.Z/length,sway=Math.Sin(age*4)*.12;
        var eye=target.LocalEyePos;
        return target.Pos.XYZ.AddCopy(eye.X+x*.3+z*sway,Math.Max(.6,eye.Y)+.8-age*.6,eye.Z+z*.3-x*sway);
    }
    internal Vec3d PositionAt(int index)=>Position(leaves[index].Target,(api.World.ElapsedMilliseconds-leaves[index].Start)/1000f);
    internal void Prune()=>leaves.RemoveAll(l=>api.World.ElapsedMilliseconds-l.Start>=2600 || !l.Target.Alive || l.Target.ShouldDespawn
        || l.Target.Pos.Dimension!=l.Dimension || !l.Target.WatchedAttributes.GetBool(MemeCatalog.Key("leaf"))
        || !api.World.LoadedEntities.TryGetValue(l.Target.EntityId,out var current) || !ReferenceEquals(current,l.Target));
    public void OnRenderFrame(float dt,EnumRenderStage stage)
    {
        Prune();var viewer=api.World.Player?.Entity;
        if(disposed||leaves.Count==0||viewer==null||api.IsGamePaused||!Prepare())return;
        var r=api.Render;var previous=r.CurrentActiveShader;previous?.Stop();
        try
        {
            r.GlToggleBlend(true);r.GLDepthMask(false);shader!.Use();
            shader.BindTexture2D("leafTex",texture!.TextureId,0);shader.UniformMatrix("projectionMatrix",r.CurrentProjectionMatrix);
            foreach(var leaf in leaves)
            {
                float age=(api.World.ElapsedMilliseconds-leaf.Start)/1000f;
                var at=Position(leaf.Target,age);
                var camera=viewer.CameraPos;
                matrix.Set(r.CameraMatrixOriginf).Translate(at.X-camera.X,at.Y-camera.Y,at.Z-camera.Z)
                    .RotateY(-viewer.Pos.Yaw).RotateZ((float)Math.Sin(age*5)*.45f);
                shader.UniformMatrix("modelViewMatrix",matrix.Values);shader.Uniform("opacity",Math.Clamp((2.6f-age)*3,0,1));
                r.RenderMesh(quad!);
            }
        }
        finally{shader!.Stop();r.GLDepthMask(true);previous?.Use();}
    }
    internal void Clear()
    {leaves.Clear();quad?.Dispose();quad=null;texture?.Dispose();texture=null;shader?.Dispose();shader=null;failed=false;}
    public void Dispose()
    {
        if(disposed)return;disposed=true;Clear();api.Event.ReloadShader-=Reload;
        api.Event.UnregisterRenderer(this,EnumRenderStage.AfterOIT);
    }
}
