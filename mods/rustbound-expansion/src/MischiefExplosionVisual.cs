using rustboundmagic.src.system.modsystems;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace RustboundExpansion;

// Rustbound's actual Corpse Explosion shader and shared quad, without creating
// its projectile entity or invoking any server explosion/damage/corpse cleanup.
internal sealed class MischiefExplosionVisual : IRenderer, IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly List<(Vec3d At, long Start)> bursts = new();
    private readonly Matrixf matrix = new();
    private bool disposed;
    internal MischiefExplosionVisual(ICoreClientAPI api)
    { this.api=api; api.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "rbx-mischief-explosion"); }
    public double RenderOrder => .4;
    public int RenderRange => 100;
    internal int Count => bursts.Count;
    internal void Add(Vec3d at)
    {
        if (disposed) return;
        if (bursts.Count >= 32) bursts.RemoveAt(0);
        bursts.Add((at.Clone(), api.World.ElapsedMilliseconds));
    }
    internal void Clear() => bursts.Clear();
    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        long now = api.World.ElapsedMilliseconds;
        bursts.RemoveAll(b => now - b.Start >= 1150);
        if (disposed || bursts.Count == 0 || api.World.Player?.Entity == null || api.IsGamePaused) return;
        var shader = ModSystemShaderRegistryRM.ShaderEntitySpellCorpseExplosion1;
        var quad = ModSystemShaderRegistryRM.SharedProjectileQuadMesh;
        if (shader == null || quad == null) return;
        var r=api.Render; var previous=r.CurrentActiveShader; previous?.Stop();
        try
        {
            r.GLDepthMask(false); shader.Use();
            shader.Uniform("projectileSize",5f); shader.Uniform("projectilePrimaryColor",new Vec3f(.3f,.2f,.1f));
            shader.Uniform("layerSeed",.17f); shader.Uniform("layerOpacity",1f);
            shader.Uniform("rgbaFogIn",r.FogColor); shader.Uniform("fogMinIn",0f); shader.Uniform("fogDensityIn",0f);
            shader.Uniform("rgbaAmbientIn",r.AmbientColor); shader.Uniform("rgbaLightIn",new Vec4f(1,1,1,1));
            shader.BindTexture2D("primaryFb",r.FrameBuffers[0].ColorTextureIds[0],0);
            shader.BindTexture2D("depthTex",r.FrameBuffers[0].DepthTextureId,1);
            shader.UniformMatrix("projectionMatrix",r.CurrentProjectionMatrix);
            shader.Uniform("counterSmooth",200f+(float)Math.Sin(api.InWorldEllapsedMilliseconds/24000d)*10);
            shader.Uniform("invFrameSize",new Vec2f(1f/Math.Max(1,r.FrameWidth),1f/Math.Max(1,r.FrameHeight)));
            var viewer=api.World.Player.Entity.Pos;
            foreach (var b in bursts)
            {
                float age=(now-b.Start)/1000f;
                shader.Uniform("u_time",age); shader.Uniform("counter",age); shader.Uniform("effectProgress",age/1.15f);
                float x=(float)(b.At.X-viewer.X),y=(float)(b.At.Y-viewer.Y),z=(float)(b.At.Z-viewer.Z);
                matrix.Identity().Translate(x,y+.25f,z).ReverseMul(r.CameraMatrixOriginf);
                var m=matrix.Values; m[0]=m[5]=m[10]=1; m[1]=m[2]=m[4]=m[6]=m[8]=m[9]=0;
                matrix.RotateZ(age*4);
                shader.UniformMatrix("modelViewMatrix",matrix.Values); shader.Uniform("worldPos",new Vec4f(x,y,z,0));
                r.RenderMesh(quad);
            }
        }
        finally { shader.Stop(); r.GLDepthMask(true); previous?.Use(); }
    }
    public void Dispose()
    {
        if (disposed) return; disposed=true; Clear();
        api.Event.UnregisterRenderer(this,EnumRenderStage.AfterBlit);
    }
}
