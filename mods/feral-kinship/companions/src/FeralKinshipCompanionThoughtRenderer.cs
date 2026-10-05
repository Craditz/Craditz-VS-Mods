#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

/// <summary>
/// Displays short owner-visible companion thoughts above the fox that said
/// them. The server supplies the text, while the client keeps the bubble
/// attached to the fox rather than to the world position where it was sent.
/// </summary>
internal sealed class FeralKinshipCompanionThoughtRenderer : IRenderer
{
    private const float MaxDisplayDistance = 64f;
    private const int MaxQueuedThoughtsPerFox = 4;
    private const int MaxBubbleWidth = 246;
    private const int BubbleHorizontalPadding = 8;
    private const int BubbleVerticalPadding = 5;
    private const int SafeTextWidth = MaxBubbleWidth - (BubbleHorizontalPadding * 2) - 4;
    private const int DefaultDurationMs = 5000;

    private readonly ICoreClientAPI api;
    private readonly Dictionary<long, Queue<Thought>> queuedByEntity = new();
    private readonly Dictionary<long, Thought> activeByEntity = new();
    private int lastThemeRevision = -1;

    public FeralKinshipCompanionThoughtRenderer(ICoreClientAPI api)
    {
        this.api = api;
    }

    public double RenderOrder => 0.56;

    public int RenderRange => (int)MaxDisplayDistance;

    public void QueueThought(CompanionThoughtPacket packet)
    {
        if (string.IsNullOrWhiteSpace(packet.Text)) return;
        bool followIdle = packet.SemanticGroup == "idle.follow";
        if (followIdle && (activeByEntity.ContainsKey(packet.TargetEntityId)
            || activeByEntity.Values.Any(item => item.Priority > CompanionDialoguePriority.Low)
            || queuedByEntity.Values.Any(queue => queue.Any(item => item.Priority > CompanionDialoguePriority.Low)))) return;
        // Idle remarks never delay an actual warning or survive behind it in the queue.
        if (packet.Priority > CompanionDialoguePriority.Low)
        {
            foreach (long id in activeByEntity.Where(pair => pair.Value.SemanticGroup == "idle.follow")
                         .Select(pair => pair.Key).ToArray())
            {
                activeByEntity[id].Texture.Dispose();
                activeByEntity.Remove(id);
            }
        }

        int durationMs = Math.Clamp(
            packet.DurationMs <= 0 ? DefaultDurationMs : packet.DurationMs,
            1500,
            15000);
        long nowMs = api.World.ElapsedMilliseconds;
        Thought thought = new(
            packet.TargetEntityId,
            packet.Text.Trim(),
            durationMs,
            packet.Priority,
            packet.ExpiresAtUtcMs,
            packet.SemanticGroup);
        if (followIdle)
        {
            thought.FollowOwnerAnchor = new Vec3d(packet.FollowOwnerX, packet.FollowOwnerY, packet.FollowOwnerZ);
            thought.FollowOwnerDimension = packet.FollowOwnerDimension;
            if (!IsFollowContextValid(thought)) return;
        }
        thought.Texture = CreateTexture(thought.Text);
        if (activeByEntity.ContainsKey(packet.TargetEntityId))
        {
            Thought active = activeByEntity[packet.TargetEntityId];
            if (thought.Priority >= 90 && thought.Priority > active.Priority)
            {
                active.Texture.Dispose();
                activeByEntity[packet.TargetEntityId] = thought;
                thought.StartedAtMs = nowMs;
                return;
            }

            if (!queuedByEntity.TryGetValue(packet.TargetEntityId, out Queue<Thought>? queue))
            {
                queue = new Queue<Thought>();
                queuedByEntity[packet.TargetEntityId] = queue;
            }

            if (queue.Count >= MaxQueuedThoughtsPerFox)
            {
                Thought? lowest = queue.OrderBy(candidate => candidate.Priority).FirstOrDefault();
                if (lowest == null || lowest.Priority >= thought.Priority)
                {
                    thought.Texture.Dispose();
                    return;
                }
                queue = new Queue<Thought>(queue.Where(candidate => !ReferenceEquals(candidate, lowest)));
                lowest.Texture.Dispose();
                queuedByEntity[packet.TargetEntityId] = queue;
            }

            queue.Enqueue(thought);
            return;
        }

        activeByEntity[packet.TargetEntityId] = thought;
        thought.StartedAtMs = nowMs;
    }

    private bool IsFollowContextValid(Thought thought)
    {
        if (thought.SemanticGroup != "idle.follow") return true;
        Entity? owner = api.World.Player?.Entity;
        Entity? companion = api.World.GetEntityById(thought.EntityId);
        return owner != null && companion != null && thought.FollowOwnerAnchor != null
            && owner.Pos.Dimension == thought.FollowOwnerDimension
            && owner.Pos.XYZ.SquareDistanceTo(thought.FollowOwnerAnchor) <= 0.25 * 0.25
            && FeralKinshipCompanionSystem.IsFollowIdleBubbleContextValid(companion, owner);
    }

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
        long nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (lastThemeRevision != FoxGuiTheme.ThemeRevision)
        {
            RefreshTexturesForTheme();
            lastThemeRevision = FoxGuiTheme.ThemeRevision;
        }

        List<long>? expired = null;
        foreach ((long entityId, Thought thought) in activeByEntity)
        {
            if (!IsFollowContextValid(thought)
                || nowMs - thought.StartedAtMs >= thought.DurationMs
                || (thought.ExpiresAtUtcMs > 0 && nowUtcMs >= thought.ExpiresAtUtcMs))
            {
                thought.Texture.Dispose();
                expired ??= new List<long>();
                expired.Add(entityId);
                continue;
            }

            Entity? entity = api.World.GetEntityById(entityId);
            if (entity == null
                || FeralKinshipCompanionSystem.IsFoxAwayFromWorld(entity)
                || entity.Pos.Dimension != api.World.Player.Entity.Pos.Dimension
                || api.World.Player.Entity.Pos.SquareDistanceTo(entity.Pos)
                    > MaxDisplayDistance * MaxDisplayDistance)
            {
                continue;
            }

            DrawThought(entity, thought);
        }

        if (expired == null) return;
        foreach (long entityId in expired)
        {
            activeByEntity.Remove(entityId);
            if (queuedByEntity.TryGetValue(entityId, out Queue<Thought>? queue)
                && queue.Count > 0)
            {
                Thought next;
                do
                {
                    next = queue.Dequeue();
                    if (next.ExpiresAtUtcMs > 0 && nowUtcMs >= next.ExpiresAtUtcMs)
                    {
                        next.Texture.Dispose();
                    }
                }
                while (queue.Count > 0
                    && next.ExpiresAtUtcMs > 0
                    && nowUtcMs >= next.ExpiresAtUtcMs);

                if (next.ExpiresAtUtcMs > 0 && nowUtcMs >= next.ExpiresAtUtcMs)
                {
                    if (queue.Count == 0) queuedByEntity.Remove(entityId);
                    continue;
                }
                next.StartedAtMs = nowMs;
                activeByEntity[entityId] = next;
                if (queue.Count == 0) queuedByEntity.Remove(entityId);
            }
            else
            {
                queuedByEntity.Remove(entityId);
            }
        }
    }

    public void Dispose()
    {
        foreach (Thought thought in activeByEntity.Values)
        {
            thought.Texture.Dispose();
        }
        foreach (Queue<Thought> queue in queuedByEntity.Values)
        {
            foreach (Thought thought in queue)
            {
                thought.Texture.Dispose();
            }
        }
        activeByEntity.Clear();
        queuedByEntity.Clear();
    }

    private void DrawThought(Entity entity, Thought thought)
    {
        Vec3d worldPosition = new(
            entity.Pos.X,
            entity.Pos.Y + entity.SelectionBox.Y2 + 0.35d,
            entity.Pos.Z);
        Vec3d projected = MatrixToolsd.Project(
            worldPosition,
            api.Render.PerspectiveProjectionMat,
            api.Render.PerspectiveViewMat,
            api.Render.FrameWidth,
            api.Render.FrameHeight);
        if (projected.Z < 0) return;

        double left = projected.X - thought.Texture.Width / 2d;
        double top = api.Render.FrameHeight - projected.Y - thought.Texture.Height - 18d;
        if (left > api.Render.FrameWidth
            || left + thought.Texture.Width < 0
            || top > api.Render.FrameHeight
            || top + thought.Texture.Height < 0)
        {
            return;
        }

        api.Render.Render2DTexture(
            thought.Texture.TextureId,
            (float)left,
            (float)top,
            thought.Texture.Width,
            thought.Texture.Height,
            100f,
            new Vec4f(1f, 1f, 1f, 1f));
    }

    private LoadedTexture CreateTexture(string text)
    {
        TextBackground background = new()
        {
            HorPadding = BubbleHorizontalPadding,
            VerPadding = BubbleVerticalPadding,
            Radius = 4,
            FillColor = WithAlpha(FoxGuiTheme.TooltipBackground, 0.60),
            BorderColor = WithAlpha(FoxGuiTheme.Accent, 0.70),
            BorderWidth = 1.5
        };
        CairoFont font = CairoFont.WhiteSmallishText()
            .WithColor(FoxGuiTheme.Text);
        try
        {
            // The API's max-width overload gives its line breaker the full
            // texture width, including horizontal padding. Pre-wrap against
            // the true inner width so the last word cannot run into the edge.
            string wrappedText = WrapText(font, text, SafeTextWidth);
            int lineCount = Math.Max(1, wrappedText.Split('\n').Length);
            int height = (int)Math.Ceiling(
                font.GetFontExtents().Height * font.LineHeightMultiplier * lineCount
                + BubbleVerticalPadding * 2 + 1);
            return api.Gui.TextTexture.GenTextTexture(
                wrappedText,
                font,
                MaxBubbleWidth,
                height,
                background);
        }
        finally
        {
            font.Dispose();
        }
    }

    private void RefreshTexturesForTheme()
    {
        foreach (Thought thought in activeByEntity.Values)
        {
            thought.Texture?.Dispose();
            thought.Texture = CreateTexture(thought.Text);
        }
        foreach (Queue<Thought> queue in queuedByEntity.Values)
        {
            foreach (Thought thought in queue)
            {
                thought.Texture?.Dispose();
                thought.Texture = CreateTexture(thought.Text);
            }
        }
    }

    private static double[] WithAlpha(double[] color, double alpha) =>
        new[] { color[0], color[1], color[2], alpha };

    private static string WrapText(CairoFont font, string text, double maxWidth)
    {
        TextDrawUtil textUtil = new();
        TextLine[] lines = textUtil.Lineize(font, text, maxWidth);
        return string.Join("\n", lines.Select(line => line.Text.TrimEnd()));
    }

    private sealed class Thought
    {
        public Thought(
            long entityId,
            string text,
            int durationMs,
            int priority,
            long expiresAtUtcMs,
            string semanticGroup)
        {
            EntityId = entityId;
            Text = text;
            DurationMs = durationMs;
            Priority = priority;
            ExpiresAtUtcMs = expiresAtUtcMs;
            SemanticGroup = semanticGroup;
        }

        public long EntityId { get; }
        public string Text { get; }
        public LoadedTexture Texture { get; set; } = null!;
        public int DurationMs { get; }
        public int Priority { get; }
        public long ExpiresAtUtcMs { get; }
        public string SemanticGroup { get; }
        public long StartedAtMs { get; set; }
        public Vec3d? FollowOwnerAnchor { get; set; }
        public int FollowOwnerDimension { get; set; }
    }
}
