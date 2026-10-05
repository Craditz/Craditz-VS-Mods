using System;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace ScentTrails;

public sealed class AnimalicaNightVisionRenderer : IRenderer
{
    private readonly ICoreClientAPI capi;
    private readonly AnimalicaNightVisionTuning tuning;
    private MeshRef? quadRef;
    private float ambientStrength = 1f;
    private float targetAmbientStrength = 1f;
    private float ambientUpdateTimer;

    public AnimalicaNightVisionRenderer(ICoreClientAPI capi, AnimalicaNightVisionTuning tuning)
    {
        this.capi = capi;
        this.tuning = tuning;

        MeshData quadMesh = QuadMeshUtil.GetCustomQuadModelData(-1, -1, 0, 2, 2);
        quadMesh.Rgba = null;
        quadRef = capi.Render.UploadMesh(quadMesh);
    }

    public IShaderProgram? Shader { get; set; }

    public double RenderOrder => 0.1;

    public int RenderRange => 1;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.AfterBlit
            || !tuning.Enabled
            || Shader == null
            || quadRef == null)
        {
            return;
        }

        if (capi.World.Player?.Entity == null)
        {
            return;
        }

        UpdateAmbientStrength(deltaTime);

        IShaderProgram? currentShader = capi.Render.CurrentActiveShader;
        currentShader?.Stop();

        Shader.Use();
        Shader.BindTexture2D(
            "primaryFb",
            capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary].ColorTextureIds[0],
            0
        );
        Shader.Uniform("brightnessLift", tuning.BrightnessLift);
        Shader.Uniform("grayStrength", tuning.GrayscaleStrength);
        Shader.Uniform("darknessStart", tuning.DarknessStart);
        Shader.Uniform("darknessRange", tuning.DarknessRange);
        Shader.Uniform("darknessCurve", tuning.DarknessCurve);
        float effectiveAmbientStrength = 1f - (1f - ambientStrength) * tuning.EnvironmentalLightResponse;
        Shader.Uniform("ambientStrength", effectiveAmbientStrength);
        Shader.Uniform("highlightProtection", tuning.HighlightProtection);

        capi.Render.GLDisableDepthTest();
        capi.Render.GLDepthMask(false);
        capi.Render.GlToggleBlend(false);
        capi.Render.RenderMesh(quadRef);
        capi.Render.GlToggleBlend(true);
        capi.Render.GLDepthMask(true);
        capi.Render.GLEnableDepthTest();

        Shader.Stop();
        currentShader?.Use();
    }

    private void UpdateAmbientStrength(float deltaTime)
    {
        float safeDeltaTime = Math.Max(deltaTime, 0.001f);

        ambientUpdateTimer -= deltaTime;
        if (ambientUpdateTimer <= 0)
        {
            ambientUpdateTimer = 0.1f;

            BlockPos eyeBlockPos = capi.World.Player!.Entity.Pos.AsBlockPos.AddCopy(0, 1, 0);
            int worldLightLevel = capi.World.BlockAccessor.GetLightLevel(
                eyeBlockPos,
                EnumLightLevelType.MaxTimeOfDayLight
            );
            int heldLightLevel = GetHeldLightLevel();
            int effectiveLightLevel = Math.Max(worldLightLevel, heldLightLevel);

            // Keep the effect strong in genuinely dark surroundings, then taper
            // it away as the player's environment or held light approaches
            // daylight brightness.
            targetAmbientStrength = 1f - Math.Clamp((effectiveLightLevel - 2f) / 14f, 0f, 1f);
        }

        // Smooth both entering and leaving darkness so a light source never
        // causes an abrupt exposure jump.
        const float fadeSeconds = 0.5f;
        float blend = 1f - MathF.Exp(-safeDeltaTime / fadeSeconds);
        ambientStrength += (targetAmbientStrength - ambientStrength) * blend;
    }

    private int GetHeldLightLevel()
    {
        EntityPlayer player = capi.World.Player!.Entity;
        int rightLight = GetItemLightLevel(player.RightHandItemSlot?.Itemstack);
        int leftLight = GetItemLightLevel(player.LeftHandItemSlot?.Itemstack);
        return Math.Max(rightLight, leftLight);
    }

    private int GetItemLightLevel(ItemStack? stack)
    {
        if (stack?.Collectible == null)
        {
            return 0;
        }

        byte[]? lightHsv = stack.Collectible.GetLightHsv(capi.World.BlockAccessor, null, stack);
        return lightHsv != null && lightHsv.Length > 2 ? lightHsv[2] : 0;
    }

    public void Dispose()
    {
        if (quadRef != null)
        {
            capi.Render.DeleteMesh(quadRef);
            quadRef = null;
        }
    }
}
