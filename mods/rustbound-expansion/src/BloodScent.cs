using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace RustboundExpansion;

[ProtoContract]
public sealed class BloodScentPacket
{
    [ProtoMember(1)] public double[] Points {get;set;}=[];
    [ProtoMember(2)] public int Dimension {get;set;}
}

public sealed class BloodScentSystem : ModSystem,IRenderer
{
    private const string Channel="rustboundexpansion-bloodscent";
    private ICoreClientAPI? client;
    private ICoreServerAPI? server;
    private LoadedTexture? texture;
    private BloodScentPacket? pulse;
    private long received;
    public double RenderOrder=>.91;
    public int RenderRange=>512;
    public override void Start(ICoreAPI api)=>api.Network.RegisterChannel(Channel).RegisterMessageType<BloodScentPacket>();
    public override void StartServerSide(ICoreServerAPI api)=>server=api;
    public override void StartClientSide(ICoreClientAPI api)
    {
        client=api;
        api.Network.GetChannel(Channel).SetMessageHandler<BloodScentPacket>(p=>{pulse=p;received=api.World.ElapsedMilliseconds;});
        api.Event.RegisterRenderer(this,EnumRenderStage.Ortho,"rbx-bloodscent");
        api.Event.LeaveWorld+=Clear;
    }
    internal static void Emit(EntityPlayer caster,Entity[] targets)
    {
        if(caster.Player is not IServerPlayer recipient)return;
        var system=caster.World.Api?.ModLoader.GetModSystem<BloodScentSystem>();
        system?.server?.Network.GetChannel(Channel).SendPacket(new BloodScentPacket
        {Dimension=caster.Pos.Dimension,Points=targets.SelectMany(e=>{var c=SpellTargeting.BodyCenter(e);return new[]{c.X,c.Y,c.Z};}).ToArray()},recipient);
    }
    private void Clear(){pulse=null;}
    public void OnRenderFrame(float dt,EnumRenderStage stage)
    {
        if(client==null || pulse==null || client.World.Player?.Entity is not {} p || p.Pos.Dimension!=pulse.Dimension
            || !ExpansionRuntime.Active(p,BloodCatalog.Kind("bloodscent").Id))return;
        double age=(client.World.ElapsedMilliseconds-received)/1000.0;
        if(age<0||age>1.1)return;
        texture??=new(client);
        if(texture.TextureId==0)client.Render.GetOrLoadTexture(new("rustboundexpansion:textures/particle/bloodscent.png"),ref texture);
        for(int i=0;i+2<pulse.Points.Length && i<192;i+=3)
        {
            var point=new Vec3d(pulse.Points[i],pulse.Points[i+1],pulse.Points[i+2]);
            var projected=MatrixToolsd.Project(point,client.Render.PerspectiveProjectionMat,client.Render.PerspectiveViewMat,
                client.Render.FrameWidth,client.Render.FrameHeight);
            // MatrixToolsd.Project returns unnormalised clip Z, not a 0..1
            // depth value. Its sign excludes points behind the camera.
            if(projected.Z<0 || projected.X<0 || projected.Y<0 || projected.X>client.Render.FrameWidth || projected.Y>client.Render.FrameHeight)continue;
            float size=18+(float)age*18;
            client.Render.Render2DTexturePremultipliedAlpha(texture.TextureId,(float)projected.X-size/2,
                client.Render.FrameHeight-(float)projected.Y-size/2,size,size,50,new Vec4f(1,1,1,(float)Math.Max(0,1-age)));
        }
    }
    public override void Dispose()
    {
        if(client!=null){client.Event.UnregisterRenderer(this,EnumRenderStage.Ortho);client.Event.LeaveWorld-=Clear;}
        texture?.Dispose();Clear();
    }
}
