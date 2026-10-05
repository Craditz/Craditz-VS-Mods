#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace FeralKinshipCompanions;

public sealed class GuiDialogFeralKinshipFoxBed : GuiDialog
{
    private readonly FeralKinshipCompanionSystem system;
    private FoxBedAssignmentStatePacket state;
    private GuiElementFeralKinshipFoxBedSurface surface;

    public GuiDialogFeralKinshipFoxBed(
        ICoreClientAPI capi,
        FeralKinshipCompanionSystem system,
        FoxBedAssignmentStatePacket state)
        : base(capi)
    {
        this.system = system;
        this.state = state;
    }

    public override string ToggleKeyCombinationCode => null;

    public bool IsFor(FoxBedAssignmentStatePacket packet)
    {
        return state != null && packet != null
            && state.X == packet.X
            && state.Y == packet.Y
            && state.Z == packet.Z
            && state.Dimension == packet.Dimension;
    }

    public override void OnGuiOpened()
    {
        ElementBounds content = ElementBounds.Fixed(0, 0, 520, 540).WithFixedPadding(0);
        content.BothSizing = ElementSizing.Fixed;
        ElementBounds dialog = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
        GuiComposer composer = capi.Gui
            .CreateCompo("feralkinship-fox-bed", dialog)
            .AddShadedDialogBG(content)
            .AddDialogTitleBar("Assign Companion Den", () => TryClose())
            .BeginChildElements(content);

        surface = new GuiElementFeralKinshipFoxBedSurface(
            capi,
            ElementBounds.Fixed(0, 0, 520, 540),
            system
        );
        composer.AddInteractiveElement(surface, "fox-bed-surface");
        SingleComposer = composer.Compose();
        base.OnGuiOpened();
        surface.ApplyState(state);
    }

    public void ApplyState(FoxBedAssignmentStatePacket packet)
    {
        state = packet;
        surface?.ApplyState(packet);
    }
}

internal sealed class GuiElementFeralKinshipFoxBedSurface : GuiElementFeralKinshipScaledSurface
{
    protected override double DesignWidth => 520;
    protected override double DesignHeight => 540;
    private const double RowHeight = 50;
    private const double ListTop = 80;
    private const double ListBottom = 452;

    private readonly FeralKinshipCompanionSystem system;
    private FoxBedAssignmentStatePacket state;
    private double scrollOffset;
    private int textureId;

    private static double[] White => FoxGuiTheme.Text;
    private static double[] Muted => FoxGuiTheme.Muted;
    private static double[] Gold => FoxGuiTheme.Accent;
    private static double[] Panel => FoxGuiTheme.PanelColor;
    private static double[] Selected => FoxGuiTheme.SelectedPanelColor;

    public GuiElementFeralKinshipFoxBedSurface(
        ICoreClientAPI capi,
        ElementBounds bounds,
        FeralKinshipCompanionSystem system)
        : base(capi, bounds)
    {
        this.system = system;
    }

    public void ApplyState(FoxBedAssignmentStatePacket packet)
    {
        state = packet;
        ClampScroll();
        Redraw();
    }

    public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
    {
        Bounds.CalcWorldBounds();
        Redraw();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (textureId > 0)
        {
            Render2DTexture(textureId, Bounds);
        }
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        args.Handled = true;
        if (args.Button != EnumMouseButton.Left)
        {
            return;
        }

        double x = CanvasMouseX(args.X);
        double y = CanvasMouseY(args.Y);
        if (HandleFontScaleClick(x, y))
        {
            Redraw();
            return;
        }

        if (state == null)
        {
            return;
        }

        if (y < ListTop || y > ListBottom)
        {
            return;
        }

        int index = (int)Math.Floor((y - ListTop + scrollOffset) / RowHeight);
        if (index == 0)
        {
            FoxGuiTheme.PlayChoice(api);
            system.SendFoxBedAssignment(state, string.Empty);
            return;
        }

        List<FoxBedCandidatePacket> candidates = state.Candidates ?? new List<FoxBedCandidatePacket>();
        int foxIndex = index - 1;
        if (foxIndex >= 0 && foxIndex < candidates.Count)
        {
            FoxGuiTheme.PlayChoice(api);
            FoxBedCandidatePacket candidate = candidates[foxIndex];
            system.SendFoxBedAssignment(state, candidate.FoxId, candidate.IsBramble);
        }
    }

    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        double amount = args.deltaPrecise != 0f ? args.deltaPrecise : args.delta;
        scrollOffset = Math.Max(0d, scrollOffset - amount * RowHeight);
        ClampScroll();
        Redraw();
        args.SetHandled();
    }

    public override void Dispose()
    {
        if (textureId > 0)
        {
            api.Render.GLDeleteTexture(textureId);
            textureId = 0;
        }
        base.Dispose();
    }

    private void ClampScroll()
    {
        int count = 1 + (state?.Candidates?.Count ?? 0);
        double viewport = ListBottom - ListTop;
        scrollOffset = Math.Clamp(scrollOffset, 0d, Math.Max(0d, count * RowHeight - viewport));
    }

    private void Redraw()
    {
        Bounds.CalcWorldBounds();
        int width = Math.Max(1, Bounds.OuterWidthInt);
        int height = Math.Max(1, Bounds.OuterHeightInt);
        using ImageSurface image = new(Format.Argb32, width, height);
        using Context ctx = new(image);

        ScaleCanvas(ctx);
        FoxGuiTheme.DrawJournal(ctx, (int)CanvasWidth, (int)CanvasHeight, FoxGuiSurfaceKind.Den);
        FoxGuiTheme.DrawWindowControls(ctx, api, CanvasWidth);
        DrawText(ctx, "Choose one companion for this personal den.", 10, 25, 18, White);
        DrawWrapped(ctx, "A companion can have one den. Selecting it here moves its assignment.", 10, 48,
            CanvasWidth - 20, 13, Muted, 2);
        FoxGuiTheme.DrawSectionRule(ctx, 10, 72, CanvasWidth - 30);

        if (state != null)
        {
            DrawRow(ctx, "No companion", "Leave this den unassigned", 0,
                string.IsNullOrWhiteSpace(state.AssignedFoxId));
            List<FoxBedCandidatePacket> candidates = state.Candidates ?? new List<FoxBedCandidatePacket>();
            Dictionary<string, int> nameTotals = candidates
                .GroupBy(candidate => DisplayName(candidate.Name), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            Dictionary<string, int> nameOrdinals = new(StringComparer.Ordinal);
            for (int index = 0; index < candidates.Count; index++)
            {
                FoxBedCandidatePacket fox = candidates[index];
                string name = DisplayName(fox.Name);
                int ordinal = nameOrdinals.TryGetValue(name, out int prior) ? prior + 1 : 1;
                nameOrdinals[name] = ordinal;
                if (nameTotals[name] > 1)
                {
                    name += $" #{ordinal}";
                }
                string note = fox.AssignedHere
                    ? "Assigned to this den"
                    : fox.AssignedElsewhere ? "Has another den — click to move" : fox.Status;
                if (fox.IsBramble && !fox.AssignedHere && !fox.AssignedElsewhere)
                {
                    note = "Guide companion — click to assign";
                }
                DrawRow(ctx, Trim(name, 34), note, index + 1, fox.AssignedHere);
            }

            int rowCount = 1 + candidates.Count;
            double viewport = ListBottom - ListTop;
            if (rowCount * RowHeight > viewport)
            {
                DrawScrollbar(ctx, 484, ListTop, viewport, rowCount * RowHeight, scrollOffset);
            }

            if (!string.IsNullOrWhiteSpace(state.Message))
            {
                FoxGuiTheme.DrawPanel(ctx, 8, 466, 472, 54, true);
                DrawWrapped(ctx, state.Message, 18, 491, 452, 14, Gold, 2);
            }
        }
        generateTexture(image, ref textureId);
    }

    private static string DisplayName(string name) =>
        string.IsNullOrWhiteSpace(name) ? "Unnamed companion" : name.Trim();

    private void DrawRow(Context ctx, string label, string note, int index, bool selected)
    {
        double y = ListTop + index * RowHeight - scrollOffset;
        if (y + RowHeight < ListTop || y > ListBottom)
        {
            return;
        }

        DrawRect(ctx, 8, y + 2, 472, RowHeight - 5, selected ? Selected : Panel);
        if (selected)
        {
            DrawRect(ctx, 8, y + 2, 4, RowHeight - 5, Gold);
        }
        DrawText(ctx, selected ? "●" : "○", 18, y + 28, 16, selected ? Gold : Muted);
        DrawText(ctx, label, 44, y + 21, 15, White);
        if (!string.IsNullOrWhiteSpace(note))
        {
            DrawText(ctx, note, 44, y + 39, 12, selected ? Gold : Muted);
        }
    }

    private static void DrawRect(Context ctx, double x, double y, double width, double height, double[] color)
    {
        ctx.SetSourceRGBA(color[0], color[1], color[2], color[3]);
        ctx.Rectangle(x, y, width, height);
        ctx.Fill();
    }

    private static void DrawText(Context ctx, string text, double x, double baseline, double size, double[] color)
    {
        CairoFont font = CairoFont.WhiteSmallText().WithFontSize((float)FeralKinshipCompanionUiSettings.ScaleFont(size));
        font.Color = color;
        font.SetupContext(ctx);
        ctx.MoveTo(x, baseline);
        ctx.ShowText(text ?? string.Empty);
        font.Dispose();
    }

    private static void DrawWrapped(Context ctx, string text, double x, double baseline, double maxWidth,
        double lineHeight, double[] color, int maxLines)
    {
        string normalized = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
        string[] words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string line = string.Empty;
        int drawn = 0;
        foreach (string word in words)
        {
            if (drawn >= maxLines)
            {
                break;
            }

            string candidate = string.IsNullOrEmpty(line) ? word : line + " " + word;
            if (FeralKinshipCompanionUiSettings.GetTextWidth(candidate, 14) <= maxWidth)
            {
                line = candidate;
                continue;
            }

            if (!string.IsNullOrEmpty(line))
            {
                DrawText(ctx, line, x, baseline + drawn * lineHeight * FeralKinshipCompanionUiSettings.TextScale, 14, color);
                drawn++;
            }
            line = word;
        }

        if (drawn < maxLines && !string.IsNullOrEmpty(line))
        {
            DrawText(ctx, line, x, baseline + drawn * lineHeight * FeralKinshipCompanionUiSettings.TextScale, 14, color);
        }
    }

    private static void DrawScrollbar(
        Context ctx,
        double x,
        double y,
        double trackHeight,
        double contentHeight,
        double offset)
    {
        double thumbHeight = Math.Max(34d, trackHeight * trackHeight / contentHeight);
        double maxOffset = contentHeight - trackHeight;
        double thumbY = y + (maxOffset <= 0d ? 0d : offset / maxOffset * (trackHeight - thumbHeight));
        DrawRect(ctx, x, y, 6, trackHeight, FoxGuiTheme.ScrollTrack);
        DrawRect(ctx, x, thumbY, 6, thumbHeight, FoxGuiTheme.ScrollThumb);
    }

    private static string Trim(string value, int maxLength)
    {
        value ??= string.Empty;
        return value.Length <= maxLength
            ? value
            : value.Substring(0, Math.Max(0, maxLength - 1)) + "…";
    }
}
