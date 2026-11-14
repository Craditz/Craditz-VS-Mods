using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace RustboundExpansion;

internal static class BreadcrumbBread
{
    internal const string VarietyKey="rustboundexpansion-breadcrumb-variety",SeedKey="rustboundexpansion-breadcrumb-seed";
    internal static readonly string[] Types=["spelt","rye","flax","rice","cassava","amaranth","sunflower"];
    internal const int Glow=6; // Native shader range is 0..255. No actual world-light sources.
    internal static AssetLocation Code(string variety)=>new("game:bread-"+variety+"-perfect");
    internal static string? Choose(IWorldAccessor world)
    {
        var available=Types.Where(t=>world.GetItem(Code(t))?.Code?.Equals(Code(t))==true).ToArray();
        return available.Length==0?null:available[world.Rand.Next(available.Length)];
    }
    internal static void Begin(EntityPlayer caster,string variety)
    {
        caster.WatchedAttributes.SetString(VarietyKey,variety);
        caster.WatchedAttributes.SetInt(SeedKey,caster.World.Rand.Next());
        caster.WatchedAttributes.MarkPathDirty(VarietyKey);caster.WatchedAttributes.MarkPathDirty(SeedKey);
    }
    internal static void Forget(Entity target)
    {
        foreach(var key in new[]{VarietyKey,SeedKey})
            if(target.WatchedAttributes.HasAttribute(key)){target.WatchedAttributes.RemoveAttribute(key);target.WatchedAttributes.MarkPathDirty(key);}
    }
    private static uint Mix(uint value)
    {
        unchecked {value^=value>>16;value*=0x7feb352d;value^=value>>15;value*=0x846ca68b;return value^(value>>16);}
    }
    internal readonly record struct Appearance(float Size,float Yaw,bool Whole);
    internal static Appearance Style(int seed,int index)
    {
        uint hash=Mix(unchecked((uint)seed+(uint)index*0x9e3779b9u));
        bool whole=hash%48==0;
        float variation=((hash>>8)&65535)/65535f;
        return new(whole?.64f+variation*.1f:.22f+variation*.05f,((hash>>16)&65535)/65535f*GameMath.TWOPI,whole);
    }
}

internal readonly record struct BreadBounds(float CenterX,float Bottom,float CenterZ,float Span)
{
    internal static BreadBounds From(float[] vertices,int count)
    {
        if(count<=0 || vertices.Length<count*3)throw new ArgumentException("Empty bread mesh");
        float minX=float.MaxValue,minY=float.MaxValue,minZ=float.MaxValue,maxX=float.MinValue,maxZ=float.MinValue;
        for(int i=0;i<count*3;i+=3)
        {
            float x=vertices[i],y=vertices[i+1],z=vertices[i+2];
            if(!float.IsFinite(x)||!float.IsFinite(y)||!float.IsFinite(z))throw new ArgumentException("Invalid bread vertex");
            minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);minZ=Math.Min(minZ,z);maxZ=Math.Max(maxZ,z);
        }
        float span=Math.Max(maxX-minX,maxZ-minZ);
        if(span<.00001f)throw new ArgumentException("Flat bread footprint");
        return new((minX+maxX)/2,minY,(minZ+maxZ)/2,span);
    }
    internal Matrixf Transform(Matrixf matrix,Vec3d at,Vec3d camera,BreadcrumbBread.Appearance style)
        =>matrix.Identity().Translate(at.X-camera.X,at.Y-camera.Y+.006,at.Z-camera.Z)
            .RotateY(style.Yaw).Scale(style.Size/Span,style.Size/Span,style.Size/Span).Translate(-CenterX,-Bottom,-CenterZ);
}

// Draw native bread meshes directly. These are neither item stacks in the world nor entities.
public sealed partial class BreadcrumbSystem
{
    internal string? BreadType;
    internal int BreadSeed;
    internal readonly List<int> Visible=[];
    private MultiTextureMeshRef? breadMesh;
    private BreadBounds breadBounds;
    private readonly Matrixf breadMatrix=new();
    private bool breadFailed;
    public double RenderOrder=>.5;
    public int RenderRange=>48;
    private void ClearBread()
    {
        Visible.Clear();BreadType=null;BreadSeed=0;breadFailed=false;
        breadMesh?.Dispose();breadMesh=null;breadBounds=default;
    }
    private bool PrepareBread()
    {
        if(breadMesh!=null)return true;
        if(client==null || BreadType==null || breadFailed)return false;
        try
        {
            var item=client.World.GetItem(BreadcrumbBread.Code(BreadType));
            if(item==null)throw new InvalidOperationException("Native bread item is unavailable");
            client.Tesselator.TesselateItem(item,out var mesh);
            breadBounds=BreadBounds.From(mesh.xyz,mesh.VerticesCount);
            breadMesh=client.Render.UploadMultiTextureMesh(mesh);
            return breadMesh!=null;
        }
        catch(Exception e){breadFailed=true;client.Logger.Warning("[Crooked Codex] Could not prepare breadcrumb bread: {0}",e.Message);return false;}
    }
    public void OnRenderFrame(float dt,EnumRenderStage stage)
    {
        if(client?.World.Player?.Entity is not {} viewer)return;
        if(!viewer.Alive || viewer.ShouldDespawn || viewer.Pos.Dimension!=dimension || !ExpansionRuntime.Active(viewer,"rbxbreadcrumbs1")
            || viewer.WatchedAttributes.GetString(BreadcrumbBread.VarietyKey,"")!=BreadType
            || viewer.WatchedAttributes.GetInt(BreadcrumbBread.SeedKey)!=BreadSeed)
        {Clear();return;}
        if(Visible.Count==0 || !PrepareBread())return;
        var render=client.Render;var previous=render.CurrentActiveShader;IStandardShaderProgram? shader=null;
        previous?.Stop();
        try
        {
            shader=render.PreparedStandardShader((int)Math.Floor(viewer.Pos.X),(int)Math.Floor(viewer.Pos.InternalY),(int)Math.Floor(viewer.Pos.Z));
            shader.ViewMatrix=render.CameraMatrixOriginf;shader.ProjectionMatrix=render.CurrentProjectionMatrix;
            shader.NormalShaded=1;shader.ExtraGlow=BreadcrumbBread.Glow;shader.DontWarpVertices=1;
            shader.RgbaTint=ColorUtil.WhiteArgbVec;shader.ExtraGodray=0;shader.SsaoAttn=0;shader.AlphaTest=.05f;shader.OverlayOpacity=0;shader.AddRenderFlags=0;
            var camera=viewer.CameraPos;var viewerPosition=viewer.Pos.XYZ;
            foreach(int index in Visible)
            {
                if(index>=Path.Points.Count)continue;
                var point=Path.Points[index].Position;
                if(point.SquareDistanceTo(viewerPosition)>48*48)continue;
                shader.RgbaLightIn=client.World.BlockAccessor.GetLightRGBs((int)Math.Floor(point.X),(int)Math.Floor(point.Y+.05),(int)Math.Floor(point.Z));
                shader.ModelMatrix=breadBounds.Transform(breadMatrix,point,camera,BreadcrumbBread.Style(BreadSeed,index)).Values;
                render.RenderMultiTextureMesh(breadMesh!,"tex");
            }
        }
        finally
        {
            if(shader!=null){shader.ExtraGlow=0;shader.DontWarpVertices=0;shader.Stop();}
            previous?.Use();
        }
    }
}
