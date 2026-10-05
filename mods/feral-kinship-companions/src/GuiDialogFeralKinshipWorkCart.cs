#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace FeralKinshipCompanions;

public sealed class GuiDialogFeralKinshipWorkCart : GuiDialog
{
    private readonly FeralKinshipCompanionSystem system;
    private FoxWorkCartAssignmentStatePacket state;
    private GuiElementFeralKinshipWorkCartSurface surface;

    public GuiDialogFeralKinshipWorkCart(ICoreClientAPI capi, FeralKinshipCompanionSystem system,
        FoxWorkCartAssignmentStatePacket state) : base(capi)
    {
        this.system = system;
        this.state = state;
    }

    public override string ToggleKeyCombinationCode => null;

    public bool IsFor(FoxWorkCartAssignmentStatePacket packet) => state != null && packet != null
        && state.X == packet.X && state.Y == packet.Y && state.Z == packet.Z && state.Dimension == packet.Dimension;

    public override void OnGuiOpened()
    {
        ElementBounds content = ElementBounds.Fixed(0, 0, 520, 580).WithFixedPadding(0);
        content.BothSizing = ElementSizing.Fixed;
        ElementBounds dialog = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
        GuiComposer composer = capi.Gui.CreateCompo("feralkinship-work-cart", dialog)
            .AddShadedDialogBG(content)
            .AddDialogTitleBar("Work Cart", () => TryClose())
            .BeginChildElements(content);
        surface = new GuiElementFeralKinshipWorkCartSurface(capi, ElementBounds.Fixed(0, 0, 520, 580), system);
        composer.AddInteractiveElement(surface, "work-cart-surface");
        SingleComposer = composer.Compose();
        base.OnGuiOpened();
        surface.ApplyState(state);
    }

    public void ApplyState(FoxWorkCartAssignmentStatePacket packet)
    {
        state = packet;
        surface?.ApplyState(packet);
    }
}

internal sealed class GuiElementFeralKinshipWorkCartSurface : GuiElementFeralKinshipScaledSurface
{
    protected override double DesignWidth => 520;
    protected override double DesignHeight => 580;
    private const double RowHeight = 50;
    private const double ListTop = 164;
    private const double ListBottom = 492;
    private readonly FeralKinshipCompanionSystem system;
    private FoxWorkCartAssignmentStatePacket state;
    private double scrollOffset;
    private string selectedTab = "assign";
    private int textureId;
    private static double[] White => FoxGuiTheme.Text;
    private static double[] Muted => FoxGuiTheme.Muted;
    private static double[] Gold => FoxGuiTheme.Accent;
    private static double[] Panel => FoxGuiTheme.PanelColor;
    private static double[] Selected => FoxGuiTheme.SelectedPanelColor;

    public GuiElementFeralKinshipWorkCartSurface(ICoreClientAPI capi, ElementBounds bounds,
        FeralKinshipCompanionSystem system) : base(capi, bounds) => this.system = system;

    public void ApplyState(FoxWorkCartAssignmentStatePacket packet)
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
        if (textureId > 0) Render2DTexture(textureId, Bounds);
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        args.Handled = true;
        if (args.Button != EnumMouseButton.Left || state == null) return;
        double x = CanvasMouseX(args.X);
        double y = CanvasMouseY(args.Y);
        if (HandleFontScaleClick(x, y)) { Redraw(); return; }
        if (y >= 38 && y <= 70)
        {
            string nextTab = x < CanvasWidth / 2d ? "assign" : "duties";
            if (!string.Equals(nextTab, selectedTab, StringComparison.Ordinal))
            {
                selectedTab = nextTab;
                scrollOffset = 0d;
                FoxGuiTheme.PlayChoice(api);
                Redraw();
            }
            return;
        }
        if (string.Equals(selectedTab, "duties", StringComparison.Ordinal))
        {
            if (y >= 150 && y <= 198
                && state.WorkCartKind is "generic" or "logging")
            {
                FoxGuiTheme.PlayChoice(api);
                system.SendFoxWorkCartLogging(state, !state.LoggingEnabled);
            }
            return;
        }
        if (y >= 120 && y <= 153)
        {
            FoxGuiTheme.PlayChoice(api);
            system.SendFoxWorkCartAssignment(state, string.Empty, true, true);
            return;
        }
        if (y < ListTop || y > ListBottom) return;
        int index = (int)Math.Floor((y - ListTop + scrollOffset) / RowHeight);
        List<FoxBedCandidatePacket> candidates = state.Candidates ?? new List<FoxBedCandidatePacket>();
        if (index >= 0 && index < candidates.Count)
        {
            FoxBedCandidatePacket candidate = candidates[index];
            FoxGuiTheme.PlayChoice(api);
            system.SendFoxWorkCartAssignment(state, candidate.FoxId, !candidate.AssignedHere);
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
        if (textureId > 0) { api.Render.GLDeleteTexture(textureId); textureId = 0; }
        base.Dispose();
    }

    private void ClampScroll()
    {
        int count = state?.Candidates?.Count ?? 0;
        double viewport = ListBottom - ListTop;
        scrollOffset = Math.Clamp(scrollOffset, 0d, Math.Max(0d, count * RowHeight - viewport));
    }

    private void Redraw()
    {
        Bounds.CalcWorldBounds();
        using ImageSurface image = new(Format.Argb32, Math.Max(1, Bounds.OuterWidthInt), Math.Max(1, Bounds.OuterHeightInt));
        using Context ctx = new(image);
        ScaleCanvas(ctx);
        FoxGuiTheme.DrawJournal(ctx, (int)CanvasWidth, (int)CanvasHeight, FoxGuiSurfaceKind.Den);
        FoxGuiTheme.DrawWindowControls(ctx, api, CanvasWidth);
        DrawText(ctx, WorkCartTitle(), 10, 25, 18, White);
        DrawTab(ctx, 10, 38, 248, 32, "Assignments", string.Equals(selectedTab, "assign", StringComparison.Ordinal));
        DrawTab(ctx, 262, 38, 248, 32, "Duties", string.Equals(selectedTab, "duties", StringComparison.Ordinal));
        if (state != null)
        {
            if (string.Equals(selectedTab, "duties", StringComparison.Ordinal))
            {
                DrawDuties(ctx);
                generateTexture(image, ref textureId);
                return;
            }

            DrawText(ctx, $"Assigned: {state.AssignedCount} | At cart: {state.WorkersAtCartCount}", 10, 92, 15, Gold);
            DrawWrapped(ctx, $"Horizontal radius: {state.Radius:0.#} blocks. Vertical distance does not matter.", 10, 108,
                CanvasWidth - 20, 12, Muted, 1);
            DrawButton(ctx, 10, 120, 500, 33, "Assign animals within radius", Gold);
            FoxGuiTheme.DrawSectionRule(ctx, 10, 157, CanvasWidth - 30);
            List<FoxBedCandidatePacket> candidates = state.Candidates ?? new List<FoxBedCandidatePacket>();
            Dictionary<string, int> totals = candidates.GroupBy(c => DisplayName(c.Name), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            Dictionary<string, int> ordinals = new(StringComparer.Ordinal);
            for (int i = 0; i < candidates.Count; i++)
            {
                FoxBedCandidatePacket fox = candidates[i];
                string name = DisplayName(fox.Name);
                int ordinal = ordinals.TryGetValue(name, out int prior) ? prior + 1 : 1;
                ordinals[name] = ordinal;
                if (totals[name] > 1) name += $" #{ordinal}";
                string note = fox.AssignedHere ? "Assigned to this Work Cart"
                    : fox.AssignedElsewhere ? "Has another home — click to move" : fox.Status;
                DrawRow(ctx, Trim(name, 34), note, i, fox.AssignedHere);
            }
            if (candidates.Count * RowHeight > ListBottom - ListTop)
                DrawScrollbar(ctx, 484, ListTop, ListBottom - ListTop, candidates.Count * RowHeight, scrollOffset);
            if (!string.IsNullOrWhiteSpace(state.Message))
            {
                FoxGuiTheme.DrawPanel(ctx, 8, 506, 472, 54, true);
                DrawWrapped(ctx, state.Message, 18, 531, 452, 14, Gold, 2);
            }
        }
        generateTexture(image, ref textureId);
    }

    private void DrawDuties(Context ctx)
    {
        DrawText(ctx, $"{WorkCartTitle()} duties", 10, 94, 17, Gold);
        DrawWrapped(ctx,
            "These jobs belong to this Work Cart and are shared by its assigned adult companions.",
            10, 116, CanvasWidth - 20, 15, Muted, 2);

        if (state.WorkCartKind is not ("generic" or "logging"))
        {
            FoxGuiTheme.DrawPanel(ctx, 10, 150, 500, 186, true);
            DrawText(ctx, "Not available yet", 24, 180, 16, Gold);
            DrawWrapped(ctx,
                state.WorkCartKind == "mining"
                    ? "Mining duties are planned for this cart, but are not implemented yet."
                    : "Quarrying duties are planned for this cart, but are not implemented yet.",
                24, 208, 470, 18, White, 4);
            if (!string.IsNullOrWhiteSpace(state.Message))
            {
                FoxGuiTheme.DrawPanel(ctx, 10, 506, 470, 54, true);
                DrawWrapped(ctx, state.Message, 20, 531, 450, 14, Gold, 2);
            }
            return;
        }

        DrawRect(ctx, 10, 150, 500, 48, state.LoggingEnabled ? Selected : Panel);
        ctx.SetSourceRGBA(Gold[0], Gold[1], Gold[2], Gold[3]);
        ctx.Rectangle(10, 150, 500, 48);
        ctx.Stroke();
        DrawText(ctx, state.LoggingEnabled ? "●" : "○", 22, 180, 17, state.LoggingEnabled ? Gold : Muted);
        DrawText(ctx, "Logging", 50, 179, 16, White);

        FoxGuiTheme.DrawPanel(ctx, 10, 214, 500, 186, true);
        DrawText(ctx, "Logging rules", 24, 242, 16, Gold);
        DrawWrapped(ctx,
            "Assigned adults fell ordinary trees inside the 27-block Work Cart radius. Fruit trees are ignored.",
            24, 266, 470, 18, White, 3);
        DrawWrapped(ctx,
            "A new tree is never started while logs, sticks, branches, or tree seeds remain on the ground. Enable Ground Cleanup on the assigned companions so they can carry that debris to a Pack Collection Box.",
            24, 322, 470, 18, Muted, 5);

        if (!string.IsNullOrWhiteSpace(state.Message))
        {
            FoxGuiTheme.DrawPanel(ctx, 10, 506, 470, 54, true);
            DrawWrapped(ctx, state.Message, 20, 531, 450, 14, Gold, 2);
        }
    }

    private void DrawRow(Context ctx, string label, string note, int index, bool selected)
    {
        double y = ListTop + index * RowHeight - scrollOffset;
        if (y + RowHeight < ListTop || y > ListBottom) return;
        DrawRect(ctx, 8, y + 2, 472, RowHeight - 5, selected ? Selected : Panel);
        if (selected) DrawRect(ctx, 8, y + 2, 4, RowHeight - 5, Gold);
        DrawText(ctx, selected ? "●" : "○", 18, y + 28, 16, selected ? Gold : Muted);
        DrawText(ctx, label, 44, y + 21, 15, White);
        DrawText(ctx, note, 44, y + 39, 12, selected ? Gold : Muted);
    }

    private void DrawButton(Context ctx, double x, double y, double width, double height, string label, double[] color)
    {
        DrawRect(ctx, x, y, width, height, Panel);
        ctx.SetSourceRGBA(color[0], color[1], color[2], color[3]);
        ctx.Rectangle(x, y, width, height); ctx.Stroke();
        DrawText(ctx, label, x + 16, y + 22, 15, White);
    }

    private void DrawTab(Context ctx, double x, double y, double width, double height, string label, bool selected)
    {
        DrawRect(ctx, x, y, width, height, selected ? Selected : Panel);
        ctx.SetSourceRGBA(Gold[0], Gold[1], Gold[2], selected ? Gold[3] : 0.45d);
        ctx.Rectangle(x, y, width, height);
        ctx.Stroke();
        if (selected) DrawRect(ctx, x, y + height - 3, width, 3, Gold);
        DrawText(ctx, label, x + 14, y + 22, 15, selected ? Gold : White);
    }

    private static void DrawRect(Context ctx, double x, double y, double width, double height, double[] color)
    { ctx.SetSourceRGBA(color[0], color[1], color[2], color[3]); ctx.Rectangle(x, y, width, height); ctx.Fill(); }

    private static void DrawText(Context ctx, string text, double x, double baseline, double size, double[] color)
    {
        CairoFont font = CairoFont.WhiteSmallText().WithFontSize((float)FeralKinshipCompanionUiSettings.ScaleFont(size));
        font.Color = color; font.SetupContext(ctx); ctx.MoveTo(x, baseline); ctx.ShowText(text ?? string.Empty); font.Dispose();
    }

    private static void DrawWrapped(Context ctx, string text, double x, double baseline, double maxWidth,
        double lineHeight, double[] color, int maxLines)
    {
        string[] words = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string line = string.Empty; int drawn = 0;
        foreach (string word in words)
        {
            if (drawn >= maxLines) break;
            string candidate = string.IsNullOrEmpty(line) ? word : line + " " + word;
            if (FeralKinshipCompanionUiSettings.GetTextWidth(candidate, 14) <= maxWidth) { line = candidate; continue; }
            if (!string.IsNullOrEmpty(line)) { DrawText(ctx, line, x, baseline + drawn * lineHeight * FeralKinshipCompanionUiSettings.TextScale, 14, color); drawn++; }
            line = word;
        }
        if (drawn < maxLines && !string.IsNullOrEmpty(line)) DrawText(ctx, line, x, baseline + drawn * lineHeight * FeralKinshipCompanionUiSettings.TextScale, 14, color);
    }

    private static void DrawScrollbar(Context ctx, double x, double y, double trackHeight, double contentHeight, double offset)
    {
        double thumbHeight = Math.Max(34d, trackHeight * trackHeight / contentHeight);
        double maxOffset = contentHeight - trackHeight;
        double thumbY = y + (maxOffset <= 0d ? 0d : offset / maxOffset * (trackHeight - thumbHeight));
        DrawRect(ctx, x, y, 6, trackHeight, FoxGuiTheme.ScrollTrack);
        DrawRect(ctx, x, thumbY, 6, thumbHeight, FoxGuiTheme.ScrollThumb);
    }

    private static string DisplayName(string name) => string.IsNullOrWhiteSpace(name) ? "Unnamed companion" : name.Trim();
    private static string Trim(string value, int maxLength) => value.Length <= maxLength ? value : value.Substring(0, maxLength - 1) + "…";

    private string WorkCartTitle() => state?.WorkCartKind switch
    {
        "logging" => "Logging Cart",
        "mining" => "Mining Cart",
        "quarrying" => "Quarrying Cart",
        _ => "Work Cart"
    };
}
