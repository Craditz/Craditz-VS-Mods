using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace RustboundExpansion;

[ProtoContract]
public sealed class ArcVisualPacket
{
    [ProtoMember(1)] public double X1 { get; set; }
    [ProtoMember(2)] public double Y1 { get; set; }
    [ProtoMember(3)] public double Z1 { get; set; }
    [ProtoMember(4)] public double X2 { get; set; }
    [ProtoMember(5)] public double Y2 { get; set; }
    [ProtoMember(6)] public double Z2 { get; set; }
    [ProtoMember(7)] public int Dimension { get; set; }
    [ProtoMember(8)] public float Width { get; set; } = 4;
    [ProtoMember(9)] public bool HealingCircle { get; set; }
    public Vec3d From => new(X1, Y1, Z1);
    public Vec3d To => new(X2, Y2, Z2);
}

public sealed class ArcVisualSystem : ModSystem
{
    private const string Channel = "rustboundexpansion-arcs";
    private IServerNetworkChannel? channel;
    private ICoreServerAPI? server;
    private ArcRenderer? renderer;

    public override void Start(ICoreAPI api) => api.Network.RegisterChannel(Channel).RegisterMessageType<ArcVisualPacket>();
    public override void StartServerSide(ICoreServerAPI api)
    {
        server = api;
        channel = api.Network.GetChannel(Channel);
    }
    public override void StartClientSide(ICoreClientAPI api)
    {
        renderer = new ArcRenderer(api);
        api.Network.GetChannel(Channel).SetMessageHandler<ArcVisualPacket>(renderer.Receive);
    }
    internal static void Emit(IWorldAccessor world, Vec3d from, Vec3d to, int dimension)
        => Emit(world, from, to, dimension, 4);
    internal static void Emit(IWorldAccessor world, Vec3d from, Vec3d to, int dimension, float width)
    {
        try { world.Api?.ModLoader.GetModSystem<ArcVisualSystem>()?.Send(from, to, dimension, width); }
        catch (Exception error) { world.Logger?.Warning("[RustboundExpansion] Arc visual failed: {0}", error.Message); }
    }
    internal static void EmitCircle(IWorldAccessor world, Vec3d center, int dimension)
    {
        try { world.Api?.ModLoader.GetModSystem<ArcVisualSystem>()?.Send(center, center, dimension, 0, true); }
        catch (Exception error) { world.Logger?.Warning("[RustboundExpansion] Circle visual update failed: {0}", error.Message); }
    }
    private void Send(Vec3d from, Vec3d to, int dimension, float width, bool healingCircle = false)
    {
        if (server == null || channel == null) return;
        var recipients = server.World.AllOnlinePlayers.OfType<IServerPlayer>()
            .Where(p => p.Entity.Pos.Dimension == dimension &&
                (p.Entity.Pos.XYZ.SquareDistanceTo(from) <= 96 * 96 || p.Entity.Pos.XYZ.SquareDistanceTo(to) <= 96 * 96)).ToArray();
        if (recipients.Length == 0) return;
        channel.SendPacket(new ArcVisualPacket
        {
            X1 = from.X,
            Y1 = from.Y,
            Z1 = from.Z,
            X2 = to.X,
            Y2 = to.Y,
            Z2 = to.Z,
            Dimension = dimension,
            Width = width,
            HealingCircle = healingCircle
        }, recipients);
    }
    public override void Dispose() { renderer?.Dispose(); channel = null; server = null; }
}

internal sealed class ArcRenderer : IRenderer
{
    internal const int Capacity = 64;
    internal const double LifetimeMs = 300;
    private readonly ICoreClientAPI api;
    private readonly List<Bolt> bolts = new();
    private readonly Matrixf matrix = new();
    private bool disposed;
    private record Bolt(MeshRef Mesh, MeshRef? Core, Vec3d Origin, int Dimension, long Started, float Width)
    {
        internal void Dispose() { Mesh.Dispose(); Core?.Dispose(); }
    }
    public double RenderOrder => 0.95;
    public int RenderRange => 128;

    internal ArcRenderer(ICoreClientAPI api)
    {
        this.api = api;
        api.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "rustboundexpansion-arcs");
        api.Event.LeaveWorld += Clear;
    }
    internal void Receive(ArcVisualPacket packet)
    {
        if (disposed || api.World.Player?.Entity == null || packet.Dimension != api.World.Player.Entity.Pos.Dimension) return;
        if (packet.HealingCircle)
        {
            if (double.IsFinite(packet.X1) && double.IsFinite(packet.Y1) && double.IsFinite(packet.Z1)
                && api.World.Player.Entity.Pos.XYZ.SquareDistanceTo(packet.From) <= 96 * 96)
                NextVfx.Circle(api.World, packet.From, packet.Dimension);
            return;
        }
        var points = Points(packet.From, packet.To);
        if (points.Length == 0) return;
        if (float.IsFinite(packet.Width) && packet.Width >= 16)
        {
            // A world-space tube stays thick at distance and does not depend
            // on the graphics driver's supported OpenGL line width.
            if (bolts.Count >= Capacity) { bolts[0].Dispose(); bolts.RemoveAt(0); }
            bolts.Add(new Bolt(api.Render.UploadMesh(Tube(points, .4f)), api.Render.UploadMesh(Tube(points, .18f)),
                packet.From, packet.Dimension, api.World.ElapsedMilliseconds, 24));
            return;
        }
        // Upload once; the same connected mesh is reused for both color passes.
        var mesh = new MeshData(points.Length, 2 * (points.Length - 1), false, false, true, false);
        mesh.SetMode(EnumDrawMode.Lines);
        mesh.VerticesCount = points.Length;
        mesh.IndicesCount = 2 * (points.Length - 1);
        for (int i = 0; i < points.Length; i++)
        {
            mesh.xyz[3 * i] = (float)points[i].X;
            mesh.xyz[3 * i + 1] = (float)points[i].Y;
            mesh.xyz[3 * i + 2] = (float)points[i].Z;
            for (int c = 0; c < 4; c++) mesh.Rgba[4 * i + c] = 255;
            if (i == 0) continue;
            mesh.Indices[2 * (i - 1)] = i - 1;
            mesh.Indices[2 * (i - 1) + 1] = i;
        }
        if (bolts.Count >= Capacity) { bolts[0].Dispose(); bolts.RemoveAt(0); }
        bolts.Add(new Bolt(api.Render.UploadMesh(mesh), null, packet.From, packet.Dimension, api.World.ElapsedMilliseconds,
            float.IsFinite(packet.Width) ? Math.Clamp(packet.Width, 1.6f, 12) : 4));
    }

    internal static MeshData Tube(Vec3d[] points, float radius)
    {
        const int sides = 8;
        int vertices = points.Length * sides, indices = (points.Length - 1) * sides * 6 + 2 * (sides - 2) * 3;
        var mesh = new MeshData(vertices, indices, false, false, true, false);
        mesh.SetMode(EnumDrawMode.Triangles); mesh.VerticesCount = vertices; mesh.IndicesCount = indices;
        var direction = (points[^1] - points[0]).Normalize();
        var across = direction.Cross(Math.Abs(direction.Y) < .9 ? new Vec3d(0, 1, 0) : new Vec3d(1, 0, 0)).Normalize();
        var up = direction.Cross(across).Normalize();
        int index = 0;
        for (int i = 0; i < points.Length; i++)
            for (int j = 0; j < sides; j++)
            {
                var p = points[i] + across * (Math.Cos(j * Math.PI / 4) * radius) + up * (Math.Sin(j * Math.PI / 4) * radius);
                int vertex = i * sides + j;
                mesh.xyz[vertex * 3] = (float)p.X; mesh.xyz[vertex * 3 + 1] = (float)p.Y; mesh.xyz[vertex * 3 + 2] = (float)p.Z;
                for (int c = 0; c < 4; c++) mesh.Rgba[vertex * 4 + c] = 255;
                if (i == 0) continue;
                int a = (i - 1) * sides + j, b = (i - 1) * sides + (j + 1) % sides, d = i * sides + (j + 1) % sides;
                foreach (int value in new[] { a, b, vertex, b, d, vertex }) mesh.Indices[index++] = value;
            }
        for (int j = 1; j < sides - 1; j++)
        {
            int end = (points.Length - 1) * sides;
            foreach (int value in new[] { 0, j + 1, j, end, end + j, end + j + 1 }) mesh.Indices[index++] = value;
        }
        return mesh;
    }

    // Coordinates relative to the first endpoint retain precision far from spawn.
    internal static Vec3d[] Points(Vec3d from, Vec3d to)
    {
        var delta = to - from;
        double length = delta.Length();
        if (!double.IsFinite(length) || !double.IsFinite(from.X) || !double.IsFinite(from.Y) ||
            !double.IsFinite(from.Z) || length < 0.001 || length > 24) return Array.Empty<Vec3d>();
        int steps = Math.Clamp((int)Math.Ceiling(length * 2), 2, 48);
        var direction = delta / length;
        var across = direction.Cross(Math.Abs(direction.Y) < 0.9 ? new Vec3d(0, 1, 0) : new Vec3d(1, 0, 0)).Normalize();
        var up = direction.Cross(across).Normalize();
        var points = new Vec3d[steps + 1];
        points[0] = new Vec3d(); points[steps] = delta;
        for (int i = 1; i < steps; i++)
        {
            double t = (double)i / steps;
            points[i] = delta * t + across * (Math.Sin(i * 12.9898) * 0.15) + up * (Math.Cos(i * 7.233) * 0.12);
        }
        return points;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        long now = api.World.ElapsedMilliseconds;
        for (int i = bolts.Count - 1; i >= 0; i--)
            if (now - bolts[i].Started >= LifetimeMs) { bolts[i].Dispose(); bolts.RemoveAt(i); }
        if (bolts.Count == 0 || api.World.Player?.Entity == null) return;
        var render = api.Render;
        var previous = render.CurrentActiveShader;
        var shader = render.GetEngineShader(EnumShaderProgram.Wireframe);
        previous?.Stop();
        try
        {
            render.GLDepthMask(false);
            render.GlToggleBlend(true);
            shader.Use();
            shader.Uniform("origin", new Vec3f());
            shader.UniformMatrix("projectionMatrix", render.CurrentProjectionMatrix);
            var camera = api.World.Player.Entity.CameraPos;
            foreach (var bolt in bolts)
            {
                if (bolt.Dimension != api.World.Player.Entity.Pos.Dimension) continue;
                shader.UniformMatrix("modelViewMatrix", matrix.Set(render.CameraMatrixOriginf)
                    .Translate(bolt.Origin.X - camera.X, bolt.Origin.Y - camera.Y, bolt.Origin.Z - camera.Z).Values);
                float alpha = (float)(1 - (now - bolt.Started) / LifetimeMs);
                render.LineWidth = bolt.Width;
                shader.Uniform("colorIn", new Vec4f(0.25f, 0.6f, 1, alpha * 0.65f));
                render.RenderMesh(bolt.Mesh);
                render.LineWidth = Math.Max(1.6f, bolt.Width * .4f);
                shader.Uniform("colorIn", new Vec4f(0.85f, 0.95f, 1, alpha));
                render.RenderMesh(bolt.Core ?? bolt.Mesh);
            }
        }
        finally { shader.Stop(); render.LineWidth = 1.6f; render.GLDepthMask(true); previous?.Use(); }
    }
    private void Clear() { foreach (var bolt in bolts) bolt.Dispose(); bolts.Clear(); }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        api.Event.LeaveWorld -= Clear;
        api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        Clear();
    }
}
