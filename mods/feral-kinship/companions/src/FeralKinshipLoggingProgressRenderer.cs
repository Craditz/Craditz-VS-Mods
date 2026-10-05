#nullable enable

using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

/// <summary>
/// Draws a small bar over each fox currently logging. The server keeps the
/// progress on each worker's watched attributes, while the client uses the
/// worker's live position so the indicator does not jump between tree parts.
/// </summary>
internal sealed class FeralKinshipLoggingProgressRenderer : IRenderer
{
    private const float MaxDisplayDistance = 64f;
    private const float BarWidth = 52f;
    private const float BarHeight = 7f;
    private const long DiscoveryIntervalMs = 250;
    private readonly ICoreClientAPI api;
    private readonly LoadedTexture whiteTexture;
    private readonly List<long> activeLoggingEntityIds = new();
    private long nextDiscoveryAtMs;

    public FeralKinshipLoggingProgressRenderer(ICoreClientAPI api)
    {
        this.api = api;
        whiteTexture = new IconUtil(api).GenTexture(1, 1, (ctx, surface) =>
        {
            ctx.SetSourceRGBA(1, 1, 1, 1);
            ctx.Rectangle(0, 0, surface.Width, surface.Height);
            ctx.Fill();
        });
    }

    public double RenderOrder => 0.55;

    public int RenderRange => (int)MaxDisplayDistance;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.Ortho
            || api.Render.FrameWidth <= 0
            || api.Render.FrameHeight <= 0
            || api.World.Player?.Entity == null)
        {
            return;
        }

        long nowMs = api.World.ElapsedMilliseconds;
        if (nowMs >= nextDiscoveryAtMs)
        {
            nextDiscoveryAtMs = nowMs + DiscoveryIntervalMs;
            activeLoggingEntityIds.Clear();
            foreach (Entity entity in api.World.LoadedEntities.Values)
            {
                ITreeAttribute? display = entity.WatchedAttributes
                    .GetTreeAttribute(FeralKinshipCompanionSystem.LoggingDisplayKey);
                if (display?.GetBool(
                        FeralKinshipCompanionSystem.LoggingDisplayActiveKey,
                        false) == true)
                {
                    activeLoggingEntityIds.Add(entity.EntityId);
                }
            }
        }

        foreach (long entityId in activeLoggingEntityIds)
        {
            Entity? entity = api.World.GetEntityById(entityId);
            ITreeAttribute? display = entity?.WatchedAttributes
                .GetTreeAttribute(FeralKinshipCompanionSystem.LoggingDisplayKey);
            if (entity == null
                || display?.GetBool(
                    FeralKinshipCompanionSystem.LoggingDisplayActiveKey,
                    false) != true)
            {
                continue;
            }
            int dimension = display.GetInt(
                FeralKinshipCompanionSystem.LoggingDisplayDimensionKey,
                entity.Pos.Dimension);
            if (dimension != api.World.Player.Entity.Pos.Dimension) continue;

            if (api.World.Player.Entity.Pos.SquareDistanceTo(entity.Pos)
                > MaxDisplayDistance * MaxDisplayDistance)
            {
                continue;
            }

            int progress = Math.Max(
                0,
                display.GetInt(
                    FeralKinshipCompanionSystem.LoggingDisplayProgressKey,
                    0));
            int total = Math.Max(
                1,
                display.GetInt(
                    FeralKinshipCompanionSystem.LoggingDisplayTotalKey,
                    1));
            DrawBar(entity, progress, total);
        }
    }

    public void Dispose()
    {
        activeLoggingEntityIds.Clear();
        whiteTexture.Dispose();
    }

    private void DrawBar(Entity entity, int progressValue, int totalValue)
    {
        Vec3d worldPosition = new(
            entity.Pos.X,
            entity.Pos.Y + entity.SelectionBox.Y2 + 0.3d,
            entity.Pos.Z);
        Vec3d projected = MatrixToolsd.Project(
            worldPosition,
            api.Render.PerspectiveProjectionMat,
            api.Render.PerspectiveViewMat,
            api.Render.FrameWidth,
            api.Render.FrameHeight);
        if (projected.Z < 0) return;

        double left = projected.X - BarWidth / 2d;
        double top = api.Render.FrameHeight - projected.Y - 10d;
        if (left > api.Render.FrameWidth
            || left + BarWidth < 0
            || top > api.Render.FrameHeight
            || top + BarHeight < 0)
        {
            return;
        }

        double progress = Math.Clamp(progressValue / (double)totalValue, 0d, 1d);
        api.Render.Render2DTexture(
            whiteTexture.TextureId,
            (float)left - 2f,
            (float)top - 2f,
            BarWidth + 4f,
            BarHeight + 4f,
            100f,
            new Vec4f(0.02f, 0.015f, 0.01f, 0.88f));
        api.Render.Render2DTexture(
            whiteTexture.TextureId,
            (float)left,
            (float)top,
            BarWidth,
            BarHeight,
            100f,
            new Vec4f(0.16f, 0.09f, 0.03f, 0.92f));
        if (progress <= 0d) return;

        api.Render.Render2DTexture(
            whiteTexture.TextureId,
            (float)left,
            (float)top,
            (float)(BarWidth * progress),
            BarHeight,
            100f,
            new Vec4f(0.95f, 0.55f, 0.08f, 1f));
    }

}
