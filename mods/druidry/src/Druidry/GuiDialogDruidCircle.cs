using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Druidry;

public sealed class GuiDialogDruidCircle : GuiDialog
{
    private const double DialogWidth = 960;
    private const double DialogHeight = 720;
    private readonly DruidrySystem system;
    private GuiElementDruidCircleSurface? surface;
    private string requestedTab = "forms";

    public GuiDialogDruidCircle(ICoreClientAPI capi, DruidrySystem system) : base(capi)
    {
        this.system = system;
    }

    public override string ToggleKeyCombinationCode => DruidrySystem.MenuHotkeyCode;

    public override void OnGuiOpened()
    {
        ComposeDialog();
        system.RequestState();
        base.OnGuiOpened();
    }

    public void OpenForms()
    {
        requestedTab = "forms";
        surface?.SetTab(requestedTab);
    }

    public void OpenRites()
    {
        requestedTab = "rites";
        surface?.SetTab(requestedTab);
    }

    public void OnStateUpdated()
    {
        surface?.Invalidate();
    }

    private void ComposeDialog()
    {
        ElementBounds contentBounds = ElementBounds.Fixed(0, 0, DialogWidth, DialogHeight).WithFixedPadding(0);
        contentBounds.BothSizing = ElementSizing.Fixed;
        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
        GuiComposer composer = capi.Gui
            .CreateCompo("druidry-circle", dialogBounds)
            .AddShadedDialogBG(contentBounds)
            .AddDialogTitleBar("Circle of Forms", () => TryClose())
            .BeginChildElements(contentBounds);
        surface = new GuiElementDruidCircleSurface(
            capi,
            ElementBounds.Fixed(0, 0, DialogWidth, DialogHeight),
            system,
            requestedTab
        );
        composer.AddInteractiveElement(surface, "circle-surface");
        SingleComposer = composer.Compose();
    }
}

internal sealed class GuiElementDruidCircleSurface : GuiElementDruidScaledSurface
{
    protected override double DesignWidth => 960;
    protected override double DesignHeight => 720;
    private const double CardStartX = 22;
    private const double CardStartY = 150;
    private const double CardWidth = 198;
    private const double CardHeight = 72;
    private const double CardGapX = 10;
    private const double CardGapY = 9;
    private readonly DruidrySystem system;
    private int textureId;
    private string activeTab;
    private string selectedCode;
    private string hoveredCode = string.Empty;
    private bool actionHovered;

    public GuiElementDruidCircleSurface(ICoreClientAPI capi, ElementBounds bounds, DruidrySystem system, string initialTab)
        : base(capi, bounds)
    {
        this.system = system;
        activeTab = initialTab;
        selectedCode = string.IsNullOrEmpty(system.ClientState.ActiveForm) ? DruidForms.All[0].Code : system.ClientState.ActiveForm;
    }

    public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
    {
        Bounds.CalcWorldBounds();
        Redraw();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        double x = CanvasMouseX(api.Input.MouseX);
        double y = CanvasMouseY(api.Input.MouseY);
        string hovered = FindForm(x, y);
        bool nextActionHovered = x >= 690 && x <= 930 && y >= 625 && y <= 672;
        if (!string.Equals(hovered, hoveredCode, StringComparison.Ordinal) || nextActionHovered != actionHovered)
        {
            hoveredCode = hovered;
            actionHovered = nextActionHovered;
            Redraw();
        }
        if (textureId > 0) Render2DTexture(textureId, Bounds);
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        args.Handled = true;
        if (args.Button != EnumMouseButton.Left) return;
        double x = CanvasMouseX(args.X);
        double y = CanvasMouseY(args.Y);
        if (y >= 96 && y <= 132)
        {
            if (x >= 22 && x <= 222) SetTab("forms");
            else if (x >= 232 && x <= 432) SetTab("rites");
            return;
        }

        string formCode = FindForm(x, y);
        if (!string.IsNullOrEmpty(formCode))
        {
            selectedCode = formCode;
            Redraw();
            return;
        }

        if (x >= 690 && x <= 930 && y >= 625 && y <= 672 && activeTab == "forms")
        {
            ActivateSelection();
        }
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

    public void SetTab(string tab)
    {
        activeTab = tab == "rites" ? "rites" : "forms";
        Redraw();
    }

    public void Invalidate()
    {
        if (!DruidForms.TryGet(selectedCode, out _)) selectedCode = DruidForms.All[0].Code;
        Redraw();
    }

    private void ActivateSelection()
    {
        DruidClientState state = system.ClientState;
        if (!DruidForms.TryGet(selectedCode, out DruidFormDefinition form)
            || !state.AvailableForms.Contains(form.Code)
            || !state.UnlockedForms.Contains(form.Code))
        {
            return;
        }
        system.RequestShift(string.Equals(state.ActiveForm, form.Code, StringComparison.OrdinalIgnoreCase) ? string.Empty : form.Code);
    }

    private void Redraw()
    {
        Bounds.CalcWorldBounds();
        int width = Math.Max(1, Bounds.OuterWidthInt);
        int height = Math.Max(1, Bounds.OuterHeightInt);
        using var image = new ImageSurface(Format.Argb32, width, height);
        using var ctx = new Context(image);
        ScaleCanvas(ctx);
        Draw(ctx);
        generateTexture(image, ref textureId);
    }

    private void Draw(Context ctx)
    {
        DruidGuiTheme.DrawBackground(ctx, CanvasWidth, CanvasHeight);
        DrawText(ctx, "THE CIRCLE OF FORMS", 24, 40, 27, DruidGuiTheme.Ink);
        DrawText(ctx, "A living memory of every spirit that has answered you", 25, 66, 15, DruidGuiTheme.Muted);
        DrawTab(ctx, "Wildshapes", 22, activeTab == "forms");
        DrawTab(ctx, "Rites & offerings", 232, activeTab == "rites");

        int unlockedCount = system.ClientState.UnlockedForms.Count;
        DrawText(ctx, $"Circle attunement  {unlockedCount}/{DruidForms.All.Count}", 744, 112, 15, DruidGuiTheme.Gold);
        DrawCards(ctx);
        if (activeTab == "rites") DrawRitesDetail(ctx);
        else DrawFormDetail(ctx);
    }

    private void DrawCards(Context ctx)
    {
        DruidClientState state = system.ClientState;
        for (int index = 0; index < DruidForms.All.Count; index++)
        {
            DruidFormDefinition form = DruidForms.All[index];
            int column = index % 3;
            int row = index / 3;
            double x = CardStartX + column * (CardWidth + CardGapX);
            double y = CardStartY + row * (CardHeight + CardGapY);
            bool available = state.AvailableForms.Contains(form.Code);
            bool unlocked = state.UnlockedForms.Contains(form.Code);
            bool active = string.Equals(state.ActiveForm, form.Code, StringComparison.OrdinalIgnoreCase);
            bool selected = string.Equals(selectedCode, form.Code, StringComparison.OrdinalIgnoreCase)
                || string.Equals(hoveredCode, form.Code, StringComparison.OrdinalIgnoreCase);
            DruidGuiTheme.DrawPanel(ctx, x, y, CardWidth, CardHeight, selected || active, available);
            double[] color = !available ? DruidGuiTheme.Locked : active ? DruidGuiTheme.Gold : unlocked ? DruidGuiTheme.Moss : DruidGuiTheme.Muted;
            DruidGuiTheme.DrawGlyph(ctx, form.Glyph, x + 33, y + 36, 35, color);
            DrawText(ctx, form.Name, x + 61, y + 29, 16, available ? DruidGuiTheme.Ink : DruidGuiTheme.Locked);
            string status = !available ? "PACK MISSING" : active ? "ACTIVE SHAPE" : unlocked ? "KNOWN" : "LOCKED RITE";
            DrawText(ctx, status, x + 61, y + 52, 11, color);
        }
    }

    private void DrawFormDetail(Context ctx)
    {
        if (!DruidForms.TryGet(selectedCode, out DruidFormDefinition form)) return;
        DruidClientState state = system.ClientState;
        bool available = state.AvailableForms.Contains(form.Code);
        bool unlocked = state.UnlockedForms.Contains(form.Code);
        bool active = string.Equals(state.ActiveForm, form.Code, StringComparison.OrdinalIgnoreCase);
        DruidGuiTheme.DrawPanel(ctx, 650, 150, 288, 444, true, available);
        DruidGuiTheme.DrawGlyph(ctx, form.Glyph, 794, 213, 72, available ? DruidGuiTheme.Gold : DruidGuiTheme.Locked);
        DrawCentered(ctx, form.Name.ToUpperInvariant(), 794, 280, 23, DruidGuiTheme.Ink);
        DrawCentered(ctx, form.Title, 794, 307, 15, DruidGuiTheme.Gold);
        DrawWrapped(ctx, form.Lore, 674, 344, 240, 19, 14, DruidGuiTheme.Muted, 4);
        DrawText(ctx, "GIFTS", 674, 431, 12, DruidGuiTheme.Moss);
        DrawWrapped(ctx, form.Gifts, 674, 452, 240, 18, 13, DruidGuiTheme.Ink, 3);
        DrawText(ctx, "BURDEN", 674, 514, 12, DruidGuiTheme.Ember);
        DrawWrapped(ctx, form.Burden, 674, 535, 240, 18, 13, DruidGuiTheme.Ink, 3);
        if (!available)
        {
            DrawWrapped(ctx, "Load " + form.PackName + " to make this spirit available.", 674, 581, 240, 17, 12, DruidGuiTheme.Ember, 2);
        }

        bool enabled = available && unlocked;
        DruidGuiTheme.DrawButton(ctx, 690, 625, 240, 47, enabled, actionHovered);
        string label = !available ? "Required pack missing" : !unlocked ? "Complete the rite first" : active ? "Return to true form" : "Assume this shape";
        DrawCentered(ctx, label, 810, 655, 15, enabled ? DruidGuiTheme.Ink : DruidGuiTheme.Locked);
        DrawText(ctx, state.HasTrueForm ? "True form recorded" : "True form records on first shift", 24, 688, 13, state.HasTrueForm ? DruidGuiTheme.Moss : DruidGuiTheme.Gold);
        if (state.CooldownMilliseconds > 0)
            DrawText(ctx, "Shape settling: a moment remains", 650, 688, 13, DruidGuiTheme.Muted);
    }

    private void DrawRitesDetail(Context ctx)
    {
        if (!DruidForms.TryGet(selectedCode, out DruidFormDefinition form)) return;
        bool unlocked = system.ClientState.UnlockedForms.Contains(form.Code);
        bool available = system.ClientState.AvailableForms.Contains(form.Code);
        DruidGuiTheme.DrawPanel(ctx, 650, 150, 288, 522, true, available);
        DruidGuiTheme.DrawGlyph(ctx, form.Glyph, 794, 211, 68, unlocked ? DruidGuiTheme.Moss : DruidGuiTheme.Gold);
        DrawCentered(ctx, unlocked ? "RITE REMEMBERED" : "RITE OF " + form.Name.ToUpperInvariant(), 794, 274, 17, unlocked ? DruidGuiTheme.Moss : DruidGuiTheme.Gold);
        DrawText(ctx, "CRAFT THE SPIRIT TOKEN", 674, 320, 12, DruidGuiTheme.Gold);
        DrawWrapped(ctx, form.TokenOffering, 674, 344, 240, 18, 14, DruidGuiTheme.Ink, 3);
        DrawText(ctx, "PERFORM THE RITE", 674, 415, 12, DruidGuiTheme.Gold);
        DrawWrapped(ctx, "Place a Ritual Altar beneath open sky. Hold the matching token and offer it to the altar with right-click.", 674, 439, 240, 19, 14, DruidGuiTheme.Ink, 5);
        DrawText(ctx, "THE BOND", 674, 548, 12, DruidGuiTheme.Gold);
        DrawWrapped(ctx, "The unlock belongs to this character and survives death and logout. The token is consumed unless you are in Creative mode.", 674, 572, 240, 18, 13, DruidGuiTheme.Muted, 5);
        DrawText(ctx, available ? form.PackName : "Missing: " + form.PackName, 674, 650, 12, available ? DruidGuiTheme.Moss : DruidGuiTheme.Ember);
    }

    private void DrawTab(Context ctx, string label, double x, bool selected)
    {
        DruidGuiTheme.DrawButton(ctx, x, 96, 200, 36, true, selected);
        DrawCentered(ctx, label, x + 100, 120, 15, selected ? DruidGuiTheme.Gold : DruidGuiTheme.Ink);
    }

    private static string FindForm(double x, double y)
    {
        for (int index = 0; index < DruidForms.All.Count; index++)
        {
            int column = index % 3;
            int row = index / 3;
            double cardX = CardStartX + column * (CardWidth + CardGapX);
            double cardY = CardStartY + row * (CardHeight + CardGapY);
            if (x >= cardX && x <= cardX + CardWidth && y >= cardY && y <= cardY + CardHeight)
                return DruidForms.All[index].Code;
        }
        return string.Empty;
    }

    private static void DrawCentered(Context ctx, string text, double centerX, double baseline, double size, double[] color)
    {
        using CairoFont font = CairoFont.WhiteSmallText().WithFontSize((float)size);
        font.Color = color;
        font.SetupContext(ctx);
        TextExtents extents = ctx.TextExtents(text ?? string.Empty);
        ctx.MoveTo(centerX - extents.Width / 2, baseline);
        ctx.ShowText(text ?? string.Empty);
    }

    private static void DrawWrapped(Context ctx, string text, double x, double baseline, double maxWidth, double lineHeight, double size, double[] color, int maxLines)
    {
        string[] words = (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string line = string.Empty;
        int count = 0;
        using CairoFont font = CairoFont.WhiteSmallText().WithFontSize((float)size);
        font.Color = color;
        font.SetupContext(ctx);
        foreach (string word in words)
        {
            string candidate = line.Length == 0 ? word : line + " " + word;
            if (ctx.TextExtents(candidate).Width > maxWidth && line.Length > 0)
            {
                if (count >= maxLines) return;
                ctx.MoveTo(x, baseline + count * lineHeight);
                ctx.ShowText(line);
                count++;
                line = word;
            }
            else line = candidate;
        }
        if (line.Length > 0 && count < maxLines)
        {
            ctx.MoveTo(x, baseline + count * lineHeight);
            ctx.ShowText(line);
        }
    }

    private static void DrawText(Context ctx, string text, double x, double baseline, double size, double[] color)
    {
        using CairoFont font = CairoFont.WhiteSmallText().WithFontSize((float)size);
        font.Color = color;
        font.SetupContext(ctx);
        ctx.MoveTo(x, baseline);
        ctx.ShowText(text ?? string.Empty);
    }
}
