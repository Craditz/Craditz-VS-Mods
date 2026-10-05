#nullable disable

using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace FeralKinshipCompanions;

public sealed class GuiDialogFeralKinshipIdleTest : GuiDialog
{
    private const double DialogWidth = 560;
    internal const double DialogHeight = 660;
    private readonly FeralKinshipCompanionSystem system;
    private GuiElementFeralKinshipIdleTestSurface surface;

    public GuiDialogFeralKinshipIdleTest(ICoreClientAPI capi, FeralKinshipCompanionSystem system)
        : base(capi)
    {
        this.system = system;
    }

    public override string ToggleKeyCombinationCode => null;

    public override void OnGuiOpened()
    {
        ElementBounds contentBounds = ElementBounds.Fixed(0, 0, DialogWidth, DialogHeight)
            .WithFixedPadding(0);
        contentBounds.BothSizing = ElementSizing.Fixed;
        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
        GuiComposer composer = capi.Gui
            .CreateCompo("feralkinship-idle-test", dialogBounds)
            .AddShadedDialogBG(contentBounds)
            .AddDialogTitleBar("Companion idle test", () => TryClose())
            .BeginChildElements(contentBounds);

        surface = new GuiElementFeralKinshipIdleTestSurface(
            capi,
            ElementBounds.Fixed(0, 0, DialogWidth, DialogHeight),
            system
        );
        composer.AddInteractiveElement(surface, "idle-test-surface");
        SingleComposer = composer.Compose();
    }
}

internal sealed class GuiElementFeralKinshipIdleTestSurface : GuiElementFeralKinshipScaledSurface
{
    protected override double DesignWidth => 560;
    protected override double DesignHeight => GuiDialogFeralKinshipIdleTest.DialogHeight;
    private static double[] TextWhite => FoxGuiTheme.Text;
    private static double[] TextMuted => FoxGuiTheme.Muted;
    private static double[] TextGold => FoxGuiTheme.Accent;

    private readonly FeralKinshipCompanionSystem system;
    private int textureId;
    private string hoveredCommand = string.Empty;

    public GuiElementFeralKinshipIdleTestSurface(
        ICoreClientAPI capi,
        ElementBounds bounds,
        FeralKinshipCompanionSystem system)
        : base(capi, bounds)
    {
        this.system = system;
    }

    public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
    {
        Bounds.CalcWorldBounds();
        Redraw();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        string command = FindCommand(CanvasMouseX(api.Input.MouseX), CanvasMouseY(api.Input.MouseY));
        if (!string.Equals(command, hoveredCommand, StringComparison.Ordinal))
        {
            hoveredCommand = command;
            Redraw();
        }

        if (textureId > 0) Render2DTexture(textureId, Bounds);
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

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        args.Handled = true;
        if (args.Button != EnumMouseButton.Left) return;

        double x = CanvasMouseX(args.X);
        double y = CanvasMouseY(args.Y);
        if (HandleFontScaleClick(x, y))
        {
            Redraw();
            return;
        }

        string command = FindCommand(x, y);
        if (CompanionIdleTestCommand.IsValid(command))
        {
            system.SelectIdleTestCommand(command);
            system.CloseIdleTestGui();
        }
        else if (y >= 616 && y <= 648 && x >= 24 && x <= 185)
        {
            system.ClearIdleTestCommand();
            system.CloseIdleTestGui();
        }
    }

    private void Redraw()
    {
        Bounds.CalcWorldBounds();
        int width = Math.Max(1, Bounds.OuterWidthInt);
        int height = Math.Max(1, Bounds.OuterHeightInt);
        ImageSurface image = new ImageSurface(Format.Argb32, width, height);
        Context ctx = new Context(image);
        try
        {
            ScaleCanvas(ctx);
            Draw(ctx, (int)CanvasWidth, (int)CanvasHeight);
            generateTexture(image, ref textureId);
        }
        finally
        {
            ctx.Dispose();
            image.Dispose();
        }
    }

    private void Draw(Context ctx, int width, int height)
    {
        FoxGuiTheme.DrawJournal(ctx, width, height, FoxGuiSurfaceKind.Whistle);
        DrawText(ctx, "Companion idle test", 24, 34, 25, TextWhite);
        DrawText(ctx, "Select a harmless ambient behavior, then right-click one owned packmate.", 24, 58, 14, TextMuted);

        string selected = system.GetClientIdleTestCommand();
        FoxGuiTheme.DrawPanel(ctx, 24, 78, width - 48, 58, CompanionIdleTestCommand.IsValid(selected));
        DrawText(ctx,
            CompanionIdleTestCommand.IsValid(selected)
                ? $"Selected behavior: {CompanionIdleTestCommand.DisplayName(selected)}"
                : "No idle behavior selected",
            40,
            113,
            17,
            CompanionIdleTestCommand.IsValid(selected) ? TextGold : TextWhite);

        DrawText(ctx, "Test behaviors", 24, 162, 16, TextGold);
        DrawChoiceButton(ctx, "Paired rest", 24, 176, 244, 36,
            selected == CompanionIdleTestCommand.PairedRest
                || hoveredCommand == CompanionIdleTestCommand.PairedRest);
        DrawChoiceButton(ctx, "Paired sleep", 292, 176, 244, 36,
            selected == CompanionIdleTestCommand.PairedSleep
                || hoveredCommand == CompanionIdleTestCommand.PairedSleep);
        DrawChoiceButton(ctx, "Packmate play", 24, 220, 244, 36,
            selected == CompanionIdleTestCommand.PackmatePlay
                || hoveredCommand == CompanionIdleTestCommand.PackmatePlay);
        DrawChoiceButton(ctx, "Shadow walk", 292, 220, 244, 36,
            selected == CompanionIdleTestCommand.ShadowFollow
                || hoveredCommand == CompanionIdleTestCommand.ShadowFollow);
        DrawChoiceButton(ctx, "Visit pack space", 24, 264, 244, 36,
            selected == CompanionIdleTestCommand.PackSpaceVisit
                || hoveredCommand == CompanionIdleTestCommand.PackSpaceVisit);
        DrawChoiceButton(ctx, "Sit on pack cart", 292, 264, 244, 36,
            selected == CompanionIdleTestCommand.PackCartSit
                || hoveredCommand == CompanionIdleTestCommand.PackCartSit);

        DrawText(ctx, "Ambient life", 24, 310, 16, TextGold);
        DrawChoiceButton(ctx, "See you off", 24, 324, 244, 36,
            selected == CompanionIdleTestCommand.SeeOff
                || hoveredCommand == CompanionIdleTestCommand.SeeOff);
        DrawChoiceButton(ctx, "Packmate greeting", 292, 324, 244, 36,
            selected == CompanionIdleTestCommand.PackmateGreeting
                || hoveredCommand == CompanionIdleTestCommand.PackmateGreeting);
        DrawChoiceButton(ctx, "Scent investigation", 24, 368, 244, 36,
            selected == CompanionIdleTestCommand.ScentInvestigation
                || hoveredCommand == CompanionIdleTestCommand.ScentInvestigation);
        DrawChoiceButton(ctx, "Camp lookout", 292, 368, 244, 36,
            selected == CompanionIdleTestCommand.CampLookout
                || hoveredCommand == CompanionIdleTestCommand.CampLookout);

        DrawText(ctx, "Basic idles", 24, 414, 16, TextGold);
        DrawChoiceButton(ctx, "Sit", 24, 428, 244, 36,
            selected == CompanionIdleTestCommand.BasicSit
                || hoveredCommand == CompanionIdleTestCommand.BasicSit);
        DrawChoiceButton(ctx, "Sleep", 292, 428, 244, 36,
            selected == CompanionIdleTestCommand.BasicSleep
                || hoveredCommand == CompanionIdleTestCommand.BasicSleep);
        DrawChoiceButton(ctx, "Sniff", 24, 472, 244, 36,
            selected == CompanionIdleTestCommand.BasicSniff
                || hoveredCommand == CompanionIdleTestCommand.BasicSniff);
        DrawChoiceButton(ctx, "Stand idle", 292, 472, 244, 36,
            selected == CompanionIdleTestCommand.BasicIdle
                || hoveredCommand == CompanionIdleTestCommand.BasicIdle);
        DrawChoiceButton(ctx, "Look around", 24, 516, 244, 36,
            selected == CompanionIdleTestCommand.BasicLookAround
                || hoveredCommand == CompanionIdleTestCommand.BasicLookAround);
        DrawChoiceButton(ctx, "Short wander", 292, 516, 244, 36,
            selected == CompanionIdleTestCommand.BasicWander
                || hoveredCommand == CompanionIdleTestCommand.BasicWander);

        FoxGuiTheme.DrawPanel(ctx, 24, 562, width - 48, 48, false);
        string details = hoveredCommand switch
        {
            CompanionIdleTestCommand.BasicSit => "The selected companion uses its ordinary sit animation for a short test period.",
            CompanionIdleTestCommand.BasicSleep => "The selected companion uses its ordinary sleep animation for a short test period.",
            CompanionIdleTestCommand.BasicSniff => "The selected companion uses its ordinary sniff animation for a short test period.",
            CompanionIdleTestCommand.BasicIdle => "The selected companion uses its ordinary standing idle animation for a short test period.",
            CompanionIdleTestCommand.BasicLookAround => "The selected companion pauses and looks around using its ordinary idle pose.",
            CompanionIdleTestCommand.BasicWander => "The selected companion takes a short walk around the pack camp, then returns to normal idling.",
            CompanionIdleTestCommand.PairedRest => "The selected companion finds a nearby packmate. They sit together, then leave at different times.",
            CompanionIdleTestCommand.PairedSleep => "The selected companion finds a nearby packmate. They sleep together, then leave at different times.",
            CompanionIdleTestCommand.PackmatePlay => "The selected companion plays chase with a nearby packmate using ordinary running.",
            CompanionIdleTestCommand.ShadowFollow => "The selected companion walks toward you while you remain inside camp. It never runs and stops when the test timer ends.",
            CompanionIdleTestCommand.PackSpaceVisit => "The selected companion visits the nearest communal pack space and idles there briefly.",
            CompanionIdleTestCommand.PackCartSit => "The selected companion walks to the Pack Cart, climbs onto its top, and sits there briefly.",
            CompanionIdleTestCommand.SeeOff => "If you are outside camp, one or two companions walk to the camp edge, sit, and watch you briefly.",
            CompanionIdleTestCommand.PackmateGreeting => "Two nearby packmates approach, face each other briefly, then separate without sitting or sleeping.",
            CompanionIdleTestCommand.ScentInvestigation => "The selected companion visits several safe camp points and sniffs each one for five seconds.",
            CompanionIdleTestCommand.CampLookout => "The selected companion walks to the camp edge, sits, and slowly scans the surroundings.",
            _ => "The test item only starts behaviors while the companion is healthy, present, and At Ease."
        };
        DrawWrapped(ctx, details, 40, 582, width - 80, 15, TextMuted, 2);

        DrawButton(ctx, "Clear selection", 24, 616, 162, 32, !CompanionIdleTestCommand.IsValid(selected));
        DrawText(ctx, "This item is a temporary creative testing tool.", 208, 637, 13, TextMuted);
    }

    private static string FindCommand(double x, double y)
    {
        if (y >= 176 && y < 212)
        {
            if (x >= 24 && x < 268) return CompanionIdleTestCommand.PairedRest;
            if (x >= 292 && x < 536) return CompanionIdleTestCommand.PairedSleep;
        }
        if (y >= 220 && y < 256)
        {
            if (x >= 24 && x < 268) return CompanionIdleTestCommand.PackmatePlay;
            if (x >= 292 && x < 536) return CompanionIdleTestCommand.ShadowFollow;
        }
        if (y >= 264 && y < 300)
        {
            if (x >= 24 && x < 268) return CompanionIdleTestCommand.PackSpaceVisit;
            if (x >= 292 && x < 536) return CompanionIdleTestCommand.PackCartSit;
        }
        if (y >= 324 && y < 360)
        {
            if (x >= 24 && x < 268) return CompanionIdleTestCommand.SeeOff;
            if (x >= 292 && x < 536) return CompanionIdleTestCommand.PackmateGreeting;
        }
        if (y >= 368 && y < 404)
        {
            if (x >= 24 && x < 268) return CompanionIdleTestCommand.ScentInvestigation;
            if (x >= 292 && x < 536) return CompanionIdleTestCommand.CampLookout;
        }
        if (y >= 428 && y < 464)
        {
            if (x >= 24 && x < 268) return CompanionIdleTestCommand.BasicSit;
            if (x >= 292 && x < 536) return CompanionIdleTestCommand.BasicSleep;
        }
        if (y >= 472 && y < 508)
        {
            if (x >= 24 && x < 268) return CompanionIdleTestCommand.BasicSniff;
            if (x >= 292 && x < 536) return CompanionIdleTestCommand.BasicIdle;
        }
        if (y >= 516 && y < 552)
        {
            if (x >= 24 && x < 268) return CompanionIdleTestCommand.BasicLookAround;
            if (x >= 292 && x < 536) return CompanionIdleTestCommand.BasicWander;
        }
        return string.Empty;
    }

    private static void DrawChoiceButton(Context ctx, string label, double x, double y, double width, double height, bool selected)
    {
        FoxGuiTheme.DrawChoiceSurface(ctx, x, y, width, height, selected);
        if (selected)
        {
            ctx.SetSourceRGBA(TextGold[0], TextGold[1], TextGold[2], TextGold[3]);
            ctx.Rectangle(x, y, 4, height);
            ctx.Fill();
        }
        DrawText(ctx, label, x + 14, y + height / 2 + 6, 16, selected ? TextGold : TextWhite);
    }

    private static void DrawButton(Context ctx, string label, double x, double y, double width, double height, bool disabled)
    {
        FoxGuiTheme.DrawButtonSurface(ctx, x, y, width, height, !disabled);
        DrawText(ctx, label, x + 12, y + height / 2 + 6, 14, disabled ? TextMuted : TextWhite);
    }

    private static void DrawWrapped(Context ctx, string text, double x, double baseline, double maxWidth, double lineHeight, double[] color, int maxLines)
    {
        string[] words = text
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string line = string.Empty;
        int lineCount = 0;
        foreach (string word in words)
        {
            string next = string.IsNullOrEmpty(line) ? word : line + " " + word;
            if (FeralKinshipCompanionUiSettings.GetTextWidth(next, 14) > maxWidth
                && !string.IsNullOrEmpty(line))
            {
                if (lineCount++ >= maxLines) return;
                DrawText(ctx, line, x, baseline + (lineCount - 1) * lineHeight * FeralKinshipCompanionUiSettings.TextScale, 14, color);
                line = word;
            }
            else
            {
                line = next;
            }
        }
        if (!string.IsNullOrEmpty(line) && lineCount < maxLines)
        {
            DrawText(ctx, line, x, baseline + lineCount * lineHeight * FeralKinshipCompanionUiSettings.TextScale, 14, color);
        }
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
}
