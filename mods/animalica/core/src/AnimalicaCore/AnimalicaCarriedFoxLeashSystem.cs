using System;
using AnimalicaShared;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AnimalicaCore;

/// <summary>
/// Draws a cosmetic leash for Seraph-led Animalica passengers.
/// Gameplay remains Carried's.
/// </summary>
public sealed class AnimalicaCarriedFoxLeashSystem : ModSystem, IRenderer
{
    private const int Segments = 12;
    private const string RenderId = "animalicacore:foxhandholdleash";

    private ICoreClientAPI? api;
    private MeshRef? ropeMesh;
    private MeshData? updateMesh;
    private bool registered;
    private bool renderFailed;
    private LoadedPlayerIndex? loadedPlayers;

    public double RenderOrder => 0.91;
    public int RenderRange => 48;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI clientApi)
    {
        if (!clientApi.ModLoader.IsModEnabled("carried"))
        {
            return;
        }

        api = clientApi;
        loadedPlayers = new LoadedPlayerIndex(clientApi);
        MeshData initialMesh = new(Segments + 1, Segments + 1, false, false, true, true);
        initialMesh.SetMode(EnumDrawMode.LineStrip);
        for (int index = 0; index <= Segments; index++)
        {
            initialMesh.AddVertexSkipTex(0, 0, 0, ColorUtil.WhiteArgb);
            // The shader reads these mesh bytes as RGBA. Writing an ARGB int
            // through AddVertexSkipTex swaps red and blue on little-endian hosts.
            int colorOffset = index * 4;
            initialMesh.Rgba[colorOffset] = 148;
            initialMesh.Rgba[colorOffset + 1] = 101;
            initialMesh.Rgba[colorOffset + 2] = 57;
            initialMesh.Rgba[colorOffset + 3] = 255;
            initialMesh.AddIndex(index);
        }

        ropeMesh = clientApi.Render.UploadMesh(initialMesh);
        updateMesh = new MeshData(false)
        {
            xyz = new float[(Segments + 1) * 3],
            VerticesCount = Segments + 1
        };
        clientApi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, RenderId);
        registered = true;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.Opaque || api?.World?.Player?.Entity == null ||
            ropeMesh == null || updateMesh == null || renderFailed)
        {
            return;
        }

        try
        {
            foreach (EntityPlayer carrier in loadedPlayers!.Snapshot)
            {
                string style = carrier.WatchedAttributes.GetString("carryTypeCode", string.Empty);
                if (!carrier.WatchedAttributes.GetBool("carrying", false) ||
                    (style != "animalica-foxwalk" && style != "animalica-walk") ||
                    !IsModel(carrier, "seraph"))
                {
                    continue;
                }

                long passengerId = carrier.WatchedAttributes.GetLong("carryTargetId", 0);
                Entity? passenger = passengerId == 0 ? null : api.World.GetEntityById(passengerId);
                if (passenger == null ||
                    !(style == "animalica-foxwalk" && IsModel(passenger, "feralfox") ||
                      style == "animalica-walk" && AnimalicaCoreSystem.IsKnownAnimalicaModelCode(
                          passenger.WatchedAttributes.GetString("skinModel", string.Empty))) ||
                    !TryAttachmentPosition(carrier, "LeftHand", out Vec3f hand) ||
                    !(TryAttachmentPosition(passenger, "rope", out Vec3f neck) ||
                      TryBonePivotPosition(passenger, "Neck", out neck)))
                {
                    continue;
                }

                DrawLeash(carrier, hand, neck);
            }
        }
        catch (Exception exception)
        {
            renderFailed = true;
            api.Logger.Warning("[AnimalicaCore] Disabled Animalica walking leash after render error: {0}", exception);
        }
    }

    private void DrawLeash(Entity carrier, Vec3f hand, Vec3f neck)
    {
        if (api == null || ropeMesh == null || updateMesh?.xyz == null)
        {
            return;
        }

        float dx = neck.X - hand.X;
        float dy = neck.Y - hand.Y;
        float dz = neck.Z - hand.Z;
        float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (distance < 0.08f || distance > 5f)
        {
            return;
        }

        float horizontal = MathF.Sqrt(dx * dx + dz * dz);
        float normalX = horizontal > 0.001f ? dz / horizontal : 1f;
        float normalZ = horizontal > 0.001f ? -dx / horizontal : 0f;
        double motionX = carrier.Pos.Motion.X;
        double motionZ = carrier.Pos.Motion.Z;
        float movement = MathF.Min(1f, (float)Math.Sqrt(motionX * motionX + motionZ * motionZ) * 8f);
        float sag = MathF.Min(0.25f, 0.07f + distance * 0.09f);
        float sway = 0.015f + movement * 0.055f;
        float phase = api.ElapsedMilliseconds * 0.003f + carrier.EntityId * 0.37f;

        for (int index = 0; index <= Segments; index++)
        {
            float t = index / (float)Segments;
            float middle = MathF.Sin(MathF.PI * t);
            float wave = middle * MathF.Sin(phase - t * 2.7f) * sway;
            int offset = index * 3;
            updateMesh.xyz[offset] = hand.X + dx * t + normalX * wave;
            updateMesh.xyz[offset + 1] = hand.Y + dy * t - middle * sag;
            updateMesh.xyz[offset + 2] = hand.Z + dz * t + normalZ * wave;
        }

        api.Render.UpdateMesh(ropeMesh, updateMesh);
        IShaderProgram shader = api.Shader.GetProgram((int)EnumShaderProgram.Autocamera);
        shader.Use();
        api.Render.GlPushMatrix();
        try
        {
            api.Render.LineWidth = 3f;
            api.Render.BindTexture2d(0);
            // EntityShapeRenderer.ModelMat already contains camera-relative positions.
            api.Render.GlLoadMatrix(api.Render.CameraMatrixOrigin);
            shader.UniformMatrix("projectionMatrix", api.Render.CurrentProjectionMatrix);
            shader.UniformMatrix("modelViewMatrix", api.Render.CurrentModelviewMatrix);
            api.Render.RenderMesh(ropeMesh);
        }
        finally
        {
            api.Render.GlPopMatrix();
            shader.Stop();
        }
    }

    private static bool TryAttachmentPosition(Entity entity, string code, out Vec3f position)
    {
        position = new Vec3f();
        if (entity.Properties?.Client?.Renderer is not EntityShapeRenderer renderer ||
            entity.AnimManager?.Animator?.GetAttachmentPointPose(code) is not AttachmentPointAndPose pose ||
            pose.AnimModelMatrix == null || pose.AnimModelMatrix.Length < 16 ||
            pose.AttachPoint == null)
        {
            return false;
        }

        AttachmentPoint attachment = pose.AttachPoint;
        Matrixf matrix = new Matrixf()
            .Set(renderer.ModelMat)
            .Mul(pose.AnimModelMatrix)
            .Translate(attachment.PosX / 16f, attachment.PosY / 16f, attachment.PosZ / 16f);
        Vec4f transformed = matrix.TransformVector(new Vec4f(0, 0, 0, 1));
        position = new Vec3f(transformed.X, transformed.Y, transformed.Z);
        return true;
    }

    private static bool TryBonePivotPosition(Entity entity, string bone, out Vec3f position)
    {
        position = new Vec3f();
        if (entity.Properties?.Client?.Renderer is not EntityShapeRenderer renderer ||
            entity.AnimManager?.Animator?.GetPosebyName(bone, StringComparison.InvariantCultureIgnoreCase) is not ElementPose pose ||
            pose.AnimModelMatrix == null || pose.AnimModelMatrix.Length < 16 ||
            pose.ForElement?.From == null || pose.ForElement.RotationOrigin == null ||
            pose.ForElement.From.Length < 3 || pose.ForElement.RotationOrigin.Length < 3)
        {
            return false;
        }

        ShapeElement element = pose.ForElement;
        Matrixf matrix = new Matrixf()
            .Set(renderer.ModelMat)
            .Mul(pose.AnimModelMatrix)
            .Translate(
                (float)(element.RotationOrigin[0] - element.From[0]) / 16f,
                (float)(element.RotationOrigin[1] - element.From[1]) / 16f,
                (float)(element.RotationOrigin[2] - element.From[2]) / 16f);
        Vec4f transformed = matrix.TransformVector(new Vec4f(0, 0, 0, 1));
        position = new Vec3f(transformed.X, transformed.Y, transformed.Z);
        return true;
    }

    private static bool IsModel(Entity entity, string model)
    {
        string code = entity.WatchedAttributes.GetString("skinModel", "seraph");
        return AnimalicaCarriedFoxSystem.IsModel(code, model);
    }

    public override void Dispose()
    {
        if (registered)
        {
            api?.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
            registered = false;
        }

        loadedPlayers?.Dispose();
        loadedPlayers = null;
        ropeMesh?.Dispose();
        ropeMesh = null;
        updateMesh = null;
        api = null;
    }
}
