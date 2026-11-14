using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace RustboundExpansion;

[ProtoContract]
public sealed class MemeVisualPacket
{
    [ProtoMember(1)] public double X { get; set; }
    [ProtoMember(2)] public double Y { get; set; }
    [ProtoMember(3)] public double Z { get; set; }
    [ProtoMember(4)] public int Dimension { get; set; }
    [ProtoMember(5)] public string Kind { get; set; } = "";
}

public sealed class MemeVisualSystem : ModSystem
{
    private const string Channel = "rustboundexpansion-mischief";
    private ICoreServerAPI? server;
    private MemeRenderer? renderer;
    public override void Start(ICoreAPI api) => api.Network.RegisterChannel(Channel).RegisterMessageType<MemeVisualPacket>();
    public override void StartServerSide(ICoreServerAPI api) => server = api;
    public override void StartClientSide(ICoreClientAPI api)
    {
        renderer = new(api);
        api.Network.GetChannel(Channel).SetMessageHandler<MemeVisualPacket>(renderer.Receive);
    }
    internal static void Emit(IWorldAccessor world, Entity target, string kind, Vec3d? position = null)
    {
        var at = position ?? target.Pos.XYZ.AddCopy(0, Math.Max(.6, target.LocalEyePos.Y), 0);
        var packet = new MemeVisualPacket { X = at.X, Y = at.Y, Z = at.Z, Dimension = target.Pos.Dimension, Kind = kind };
        try { world.Api?.ModLoader.GetModSystem<MemeVisualSystem>()?.Send(packet); }
        catch (Exception e) { world.Logger?.Warning("[CrookedCodex] Mischief particles failed: {0}", e.Message); }
    }
    private void Send(MemeVisualPacket packet)
    {
        if (server == null) return;
        var recipients = server.World.AllOnlinePlayers.OfType<IServerPlayer>().Where(p => p.Entity.Pos.Dimension == packet.Dimension
            && p.Entity.Pos.XYZ.SquareDistanceTo(packet.X, packet.Y, packet.Z) <= 64 * 64).ToArray();
        if (recipients.Length > 0) server.Network.GetChannel(Channel).SendPacket(packet, recipients);
    }
    public override void Dispose() { renderer?.Dispose(); server = null; }
}

internal sealed class MemeRenderer : IRenderer
{
    private readonly ICoreClientAPI api;
    private readonly Dictionary<long, long> leafTimes = new();
    private readonly Dictionary<long, ILoadedSound> rains = new();
    private readonly Matrixf matrix = new();
    private readonly Dictionary<(int Level, int Seed), MeshRef> hats = new();
    private readonly MischiefLeafVisual leaves;
    private readonly MischiefExplosionVisual explosions;
    private MeshRef? cloud;
    private long tick;
    private bool disposed;
    internal MemeRenderer(ICoreClientAPI api)
    {
        this.api = api; tick = api.Event.RegisterGameTickListener(Tick, 200);
        api.Event.RegisterRenderer(this, EnumRenderStage.AfterOIT, "rustboundexpansion-mischief");
        leaves = new(api); explosions = new(api);
        api.Event.LeaveWorld += Clear;
    }
    public double RenderOrder => .96;
    public int RenderRange => 64;
    private Entity[] Nearby()
    {
        var viewer = api.World.Player?.Entity;
        if (viewer == null) return [];
        return api.World.LoadedEntities.Values.Where(e => e.Alive && !e.ShouldDespawn && e.Pos.Dimension == viewer.Pos.Dimension
            && e.Pos.XYZ.SquareDistanceTo(viewer.Pos.XYZ) <= 64 * 64).ToArray();
    }
    private void Tick(float dt)
    {
        if (disposed) return;
        var entities = Nearby(); long now = api.World.ElapsedMilliseconds;
        foreach (var e in entities)
        {
            double head = Math.Max(.6, e.LocalEyePos.Y);
            if (e.WatchedAttributes.GetBool(MemeCatalog.Key("weather")))
            {
                var top = e.Pos.XYZ.AddCopy(0, head + 1.2, 0);
                var rain = new SimpleParticleProperties(10, 10, Vfx.Color("water"), top.AddCopy(-.45, -.2, -.45), top.AddCopy(.45, -.1, .45),
                    new Vec3f(0, -4, 0), new Vec3f(0, -5, 0), .4f, 0, .15f, .35f, EnumParticleModel.Quad)
                { WithTerrainCollision = true, LightEmission = Vfx.Color("water") };
                api.World.SpawnParticles(rain);
                if (!rains.TryGetValue(e.EntityId, out var sound) && rains.Count < 8)
                {
                    sound = api.World.LoadSound(RainSound(top));
                    if (sound != null) { rains[e.EntityId] = sound; sound.Start(); }
                }
                sound?.SetPosition((float)top.X, (float)top.Y, (float)top.Z);
                if ((now / 200) % 50 == 0)
                    api.World.SpawnParticles(new SimpleParticleProperties(5, 5, Vfx.Color("lightning"), top, top.AddCopy(0, -.4, 0),
                        new Vec3f(), new Vec3f(), .15f, 0, .4f, .6f, EnumParticleModel.Quad) { LightEmission = Vfx.Color("lightning"), WithTerrainCollision = false });
            }
            if (e.WatchedAttributes.GetBool(MemeCatalog.Key("leaf")) && leafTimes.GetValueOrDefault(e.EntityId) <= now)
            {
                leafTimes[e.EntityId] = now + 1800;
                leaves.Add(e);
            }
        }
        var ids = entities.Where(e => e.WatchedAttributes.GetBool(MemeCatalog.Key("leaf"))).Select(e => e.EntityId).ToHashSet();
        foreach (var id in leafTimes.Keys.Where(id => !ids.Contains(id)).ToArray()) leafTimes.Remove(id);
        var weather = entities.Where(e => e.WatchedAttributes.GetBool(MemeCatalog.Key("weather"))).Select(e => e.EntityId).ToHashSet();
        foreach (var id in rains.Keys.Where(id => !weather.Contains(id)).ToArray())
        { rains[id].Stop(); rains[id].Dispose(); rains.Remove(id); }
    }
    internal static SoundParams RainSound(Vec3d at) => new(new("game:sounds/weather/tracks/rain-leafless.ogg"))
    { ShouldLoop = true, DisposeOnFinish = false, Volume = .07f, Range = 8, ReferenceDistance = 1,
        Position = new((float)at.X, (float)at.Y, (float)at.Z), SoundType = EnumSoundType.Weather };
    internal void Receive(MemeVisualPacket packet)
    {
        var viewer = api.World.Player?.Entity;
        if (disposed || viewer == null || packet.Dimension != viewer.Pos.Dimension || !double.IsFinite(packet.X) || !double.IsFinite(packet.Y)
            || !double.IsFinite(packet.Z) || viewer.Pos.XYZ.SquareDistanceTo(packet.X, packet.Y, packet.Z) > 64 * 64) return;
        var at = new Vec3d(packet.X, packet.Y, packet.Z);
        if (packet.Kind == "spite") { explosions.Add(at); return; }
        if (packet.Kind == "reveal")
        {
            // Client-only native flash supplies bolt, point lights and thunder.
            // Never call WeatherSystemServer.SpawnLightningFlash (damage/fire).
            var weather = api.ModLoader?.GetModSystem<WeatherSystemClient>();
            if (weather != null) weather.SpawnLightningFlash(at);
            else api.World.PlaySoundAt(new AssetLocation("game:sounds/weather/lightning-verynear.ogg"),at.X,at.Y,at.Z,null,false,32,.8f);
        }
        Burst(api.World, at, packet.Kind);
    }
    internal static void Burst(IWorldAccessor world, Vec3d at, string kind)
    {
        if (kind == "paper")
        {
            world.SpawnParticles(new SimpleParticleProperties(40, 40, ColorUtil.ToRgba(255, 245, 236, 211), at, at,
                new Vec3f(-1.5f, .5f, -1.5f), new Vec3f(1.5f, 2, 1.5f), 1.8f, .35f, .6f, 1.3f, EnumParticleModel.Quad)
            { WithTerrainCollision = false, OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -.45f) });
        }
        if (kind == "reveal")
        {
            int white = ColorUtil.ToRgba(220, 255, 245, 190);
            world.SpawnParticles(new SimpleParticleProperties(35, 35, ColorUtil.ToRgba(180, 180, 160, 195), at.AddCopy(-.4, -.6, -.4), at.AddCopy(.4, .2, .4),
                new Vec3f(-1, .4f, -1), new Vec3f(1, 1.5f, 1), 1.5f, 0, 1, 2, EnumParticleModel.Quad)
            { WithTerrainCollision = false, SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 1), OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -.6f) });
            world.SpawnParticles(new SimpleParticleProperties(80, 80, white, at.AddCopy(-.4, -.7, -.4), at.AddCopy(.4, 5, .4),
                new Vec3f(), new Vec3f(), 1.8f, 0, .7f, 1.2f, EnumParticleModel.Quad)
            { WithTerrainCollision = false, LightEmission = white, OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -.5f) });
        }
    }
    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (disposed || api.World.Player?.Entity == null) return;
        var entities = Nearby().Where(e => !ControlEffects.Banished(e) && !e.WatchedAttributes.GetBool(MemeCatalog.Key("entrance"))
            && (e.WatchedAttributes.GetBool(MemeCatalog.Key("hat")) || e.WatchedAttributes.GetBool(MemeCatalog.Key("weather")))).ToArray();
        if (entities.Length == 0) return;
        var render = api.Render; var previous = render.CurrentActiveShader; var shader = render.GetEngineShader(EnumShaderProgram.Wireframe);
        previous?.Stop();
        try
        {
            render.GlToggleBlend(true); render.GLDepthMask(false);
            shader.Use(); shader.Uniform("origin", new Vec3f()); shader.UniformMatrix("projectionMatrix", render.CurrentProjectionMatrix);
            foreach (var e in entities)
            {
                var camera = api.World.Player.Entity.CameraPos; var pos = e.Pos.XYZ; double head = Math.Max(.6, e.LocalEyePos.Y);
                void Draw(MeshRef mesh, double y, Vec4f color)
                {
                    shader.UniformMatrix("modelViewMatrix", matrix.Set(render.CameraMatrixOriginf).Translate(pos.X - camera.X, pos.Y + y - camera.Y, pos.Z - camera.Z).Values);
                    shader.Uniform("colorIn", color); render.RenderMesh(mesh);
                }
                if (e.WatchedAttributes.GetBool(MemeCatalog.Key("hat")))
                {
                    int level = Math.Clamp(e.WatchedAttributes.GetInt("rbx-meme-hatlevel", 1), 1, 8);
                    int seed = e.WatchedAttributes.GetInt("rbx-meme-hatseed");
                    var key = (level,seed);
                    if (!hats.TryGetValue(key,out var hat))
                    {
                        if (hats.Count >= 64) { var first=hats.First();first.Value.Dispose();hats.Remove(first.Key); }
                        hats[key] = hat = render.UploadMesh(Hat(level,seed));
                    }
                    Draw(hat, head + .15, new(1,1,1,1));
                }
                if (e.WatchedAttributes.GetBool(MemeCatalog.Key("weather")))
                { cloud ??= render.UploadMesh(Cloud()); Draw(cloud, head + 1.2, new(.72f, .74f, .8f, .4f)); }
            }
        }
        finally { shader.Stop(); render.GLDepthMask(true); previous?.Use(); }
    }
    internal static MeshData Hat(int levels, int seed = 0)
    {
        var vertices = new List<Vec3f>(); var indices = new List<int>();
        byte[][] palette = [[144,61,204],[49,155,215],[215,65,75],[67,180,114],[228,160,50],[205,91,182],[80,89,213],[220,224,211]];
        for (int layer = 0; layer < Math.Clamp(levels, 1, 8); layer++)
        {
            float baseY = layer * .5f;
            Box(vertices, indices, -.48f, baseY, -.48f, .48f, baseY + .07f, .48f);
            float bottom = baseY + .07f, radius = .34f, top = bottom + .55f;
            for (int side = 0; side < 8; side++)
            {
                double a = side * Math.PI / 4, b = (side + 1) * Math.PI / 4;
                int i = vertices.Count;
                vertices.Add(new((float)Math.Cos(a) * radius, bottom, (float)Math.Sin(a) * radius));
                vertices.Add(new(.1f, top, 0));
                vertices.Add(new((float)Math.Cos(b) * radius, bottom, (float)Math.Sin(b) * radius));
                indices.AddRange([i, i + 1, i + 2]);
            }
        }
        var mesh = Mesh(vertices, indices);
        for (int layer=0;layer<Math.Clamp(levels,1,8);layer++)
        {
            uint hash=unchecked((uint)seed + (uint)layer*2654435761u); hash ^= hash>>16; hash*=2246822519u; hash^=hash>>13;
            var color=palette[hash%palette.Length];
            for(int v=layer*32;v<(layer+1)*32;v++) for(int c=0;c<3;c++) mesh.Rgba[v*4+c]=color[c];
        }
        return mesh;
    }
    internal static MeshData Cloud()
    {
        var v = new List<Vec3f>(); var i = new List<int>();
        Box(v, i, -.6f, -.12f, -.25f, .6f, .12f, .25f);
        Box(v, i, -.4f, 0, -.35f, .3f, .35f, .35f);
        Box(v, i, -.05f, -.03f, -.25f, .48f, .25f, .28f);
        return Mesh(v, i);
    }
    private static void Box(List<Vec3f> v, List<int> i, float x1, float y1, float z1, float x2, float y2, float z2)
    {
        int start = v.Count;
        v.AddRange([new(x1,y1,z1),new(x2,y1,z1),new(x2,y2,z1),new(x1,y2,z1),new(x1,y1,z2),new(x2,y1,z2),new(x2,y2,z2),new(x1,y2,z2)]);
        foreach (int n in new[] {0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5}) i.Add(start+n);
    }
    private static MeshData Mesh(List<Vec3f> v, List<int> indices)
    {
        var mesh = new MeshData(v.Count, indices.Count, false, false, true, false);
        mesh.SetMode(EnumDrawMode.Triangles); mesh.VerticesCount = v.Count; mesh.IndicesCount = indices.Count;
        for (int n = 0; n < v.Count; n++) { mesh.xyz[n*3]=v[n].X; mesh.xyz[n*3+1]=v[n].Y; mesh.xyz[n*3+2]=v[n].Z; for (int c=0;c<4;c++) mesh.Rgba[n*4+c]=255; }
        indices.CopyTo(mesh.Indices); return mesh;
    }
    private void Clear()
    {
        foreach (var hat in hats.Values) hat.Dispose(); hats.Clear(); cloud?.Dispose(); cloud=null; leafTimes.Clear();
        foreach(var sound in rains.Values) {sound.Stop();sound.Dispose();} rains.Clear(); leaves.Clear(); explosions.Clear();
    }
    public void Dispose()
    {
        if (disposed) return; disposed=true; api.Event.UnregisterGameTickListener(tick);
        api.Event.UnregisterRenderer(this, EnumRenderStage.AfterOIT); api.Event.LeaveWorld-=Clear; Clear(); leaves.Dispose(); explosions.Dispose();
    }
}
