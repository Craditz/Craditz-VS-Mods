#nullable disable

using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace FeralKinshipCompanions;

public sealed class GuiDialogFeralKinshipWhistle : GuiDialog
{
    private const double DialogWidth = 700;
    private const double DialogHeight = 760;
    private readonly FeralKinshipCompanionSystem system;
    private GuiElementFeralKinshipWhistleSurface surface;

    public GuiDialogFeralKinshipWhistle(ICoreClientAPI capi, FeralKinshipCompanionSystem system)
        : base(capi)
    {
        this.system = system;
    }

    public override string ToggleKeyCombinationCode => null;

    public override void OnGuiOpened()
    {
        ComposeDialog();
        base.OnGuiOpened();
    }

    private void ComposeDialog()
    {
        ElementBounds contentBounds = ElementBounds.Fixed(0, 0, DialogWidth, DialogHeight)
            .WithFixedPadding(0);
        contentBounds.BothSizing = ElementSizing.Fixed;
        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
        GuiComposer composer = capi.Gui
            .CreateCompo("feralkinship-whistle", dialogBounds)
            .AddShadedDialogBG(contentBounds)
            .AddDialogTitleBar("Companion whistle", () => TryClose())
            .BeginChildElements(contentBounds);

        surface = new GuiElementFeralKinshipWhistleSurface(
            capi,
            ElementBounds.Fixed(0, 0, DialogWidth, DialogHeight),
            system
        );
        composer.AddInteractiveElement(surface, "whistle-surface");
        SingleComposer = composer.Compose();
    }
}

internal sealed class GuiElementFeralKinshipWhistleSurface : GuiElementFeralKinshipScaledSurface
{
    protected override double DesignWidth => 700;
    protected override double DesignHeight => 760;
    private static double[] TextWhite => FoxGuiTheme.Text;
    private static double[] TextMuted => FoxGuiTheme.Muted;
    private static double[] TextGold => FoxGuiTheme.Accent;
    private static double[] TextButton => FoxGuiTheme.ButtonColor;

    private readonly FeralKinshipCompanionSystem system;
    private int textureId;
    private string hoveredCommand = string.Empty;
    private string activeTab = "commands";
    private int dutyMask = CompanionDuty.AllExceptSnowballsMask;
    private bool dutyMaskInitialized;

    public GuiElementFeralKinshipWhistleSurface(
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
        double x = CanvasMouseX(api.Input.MouseX);
        double y = CanvasMouseY(api.Input.MouseY);
        string command = activeTab == "commands" ? FindCommand(x, y) : string.Empty;
        if (!string.Equals(command, hoveredCommand, StringComparison.Ordinal))
        {
            hoveredCommand = command;
            Redraw();
        }

        if (textureId > 0)
        {
            Render2DTexture(textureId, Bounds);
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

        if (y >= 140 && y <= 174)
        {
            if (x >= 18 && x < 341) activeTab = "commands";
            else if (x >= 359 && x < 682) activeTab = "duties";
            Redraw();
            return;
        }

        if (activeTab == "duties")
        {
            if (y >= 400 && y <= 438 && x >= 18 && x <= 173)
            {
                dutyMask = CompanionDuty.AllExceptSnowballsMask;
                Redraw();
                return;
            }
            if (y >= 400 && y <= 438 && x >= 187 && x <= 342)
            {
                dutyMask = 0;
                Redraw();
                return;
            }

            int bit = FindDutyOption(x, y);
            if (bit != 0)
            {
                dutyMask ^= bit;
                Redraw();
                return;
            }

            if (y >= 690 && y <= 728 && x >= 18 && x <= 340)
            {
                system.SelectWhistleCommand($"duty:custom-{dutyMask}");
                system.CloseWhistleGui();
                return;
            }
            if (y >= 690 && y <= 728 && x >= 360 && x <= 560)
            {
                system.ClearWhistleCommand();
                system.CloseWhistleGui();
                return;
            }
            return;
        }

        string command = FindCommand(x, y);
        if (CompanionWhistleCommand.IsValid(command))
        {
            system.SelectWhistleCommand(command);
            system.CloseWhistleGui();
        }
        else if (y >= 690 && y <= 728 && x >= 18 && x <= 220)
        {
            system.ClearWhistleCommand();
            system.CloseWhistleGui();
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
        DrawText(ctx, "Companion whistle", 24, 34, 25, TextWhite);
        DrawText(ctx, "Choose a command, then blow the whistle to apply it nearby.", 24, 58, 15, TextMuted);

        string selected = system.GetClientWhistleCommand();
        FoxGuiTheme.DrawPanel(ctx, 18, 72, width - 36, 58, !string.IsNullOrWhiteSpace(selected));
        DrawText(ctx,
            CompanionWhistleCommand.IsValid(selected)
                ? $"Selected command: {CompanionWhistleCommand.DisplayName(selected)}"
                : "No Companions command selected",
            32,
            99,
            17,
            CompanionWhistleCommand.IsValid(selected) ? TextGold : TextWhite);
        DrawText(ctx, "Owned PetAI pets receive the closest matching command; risk settings only affect Companions.", 32, 119, 12, TextMuted);

        DrawChoiceButton(ctx, "Usual commands", 18, 140, 323, 34, activeTab == "commands");
        DrawChoiceButton(ctx, "Duties", 359, 140, 323, 34, activeTab == "duties");
        if (activeTab == "duties") DrawDuties(ctx, width);
        else DrawUsualCommands(ctx, width, selected);
    }

    private void DrawUsualCommands(Context ctx, int width, string selected)
    {
        DrawText(ctx, "Activity", 24, 198, 16, TextGold);
        DrawChoiceButton(ctx, "Follow", 18, 210, 157, 34, selected == "activity:follow" || hoveredCommand == "activity:follow");
        DrawChoiceButton(ctx, "At Ease", 187, 210, 157, 34, selected == "activity:atease" || hoveredCommand == "activity:atease");
        DrawChoiceButton(ctx, "Rest", 356, 210, 157, 34, selected == "activity:rest" || hoveredCommand == "activity:rest");
        DrawChoiceButton(ctx, "Return Home", 525, 210, 157, 34, selected == "activity:returnhome" || hoveredCommand == "activity:returnhome");
        DrawText(ctx, "Combat style", 24, 270, 16, TextGold);
        DrawChoiceButton(ctx, "Passive", 18, 282, 210, 34, selected == "combat:passive" || hoveredCommand == "combat:passive");
        DrawChoiceButton(ctx, "Defensive", 245, 282, 210, 34, selected == "combat:defensive" || hoveredCommand == "combat:defensive");
        DrawChoiceButton(ctx, "Protect", 472, 282, 210, 34, selected == "combat:protect" || hoveredCommand == "combat:protect");
        DrawChoiceButton(ctx, "Assist", 18, 322, 210, 34, selected == "combat:assist" || hoveredCommand == "combat:assist");
        DrawChoiceButton(ctx, "Aggressive", 245, 322, 210, 34, selected == "combat:aggressive" || hoveredCommand == "combat:aggressive");
        DrawChoiceButton(ctx, "Flee", 472, 322, 210, 34, selected == "combat:flee" || hoveredCommand == "combat:flee");
        DrawChoiceButton(ctx, "Attack target", 18, 362, 210, 34, selected == "target:attack" || hoveredCommand == "target:attack");
        DrawText(ctx, "Risk tolerance", 24, 420, 16, TextGold);
        DrawChoiceButton(ctx, "Cautious", 18, 432, 210, 34, selected == "risk:cautious" || hoveredCommand == "risk:cautious");
        DrawChoiceButton(ctx, "Steady", 245, 432, 210, 34, selected == "risk:steady" || hoveredCommand == "risk:steady");
        DrawChoiceButton(ctx, "Fearless", 472, 432, 210, 34, selected == "risk:fearless" || hoveredCommand == "risk:fearless");
        DrawText(ctx, "Utility", 24, 500, 16, TextGold);
        DrawChoiceButton(ctx, "Drop held items", 18, 512, 210, 34, selected == "utility:drop-held-items" || hoveredCommand == "utility:drop-held-items");
        FoxGuiTheme.DrawPanel(ctx, 18, 556, width - 36, 104, false);
        DrawWrapped(ctx, "Nearby commands reach owned Companions within 15 blocks horizontally and 5 blocks vertically. Select a duty from the Duties tab to apply several settings together.", 32, 580, width - 64, 17, TextMuted, 2);
        string hoverDetails = string.IsNullOrWhiteSpace(hoveredCommand) ? "Hover over a command to see what it does." : GetCommandDescription(hoveredCommand);
        DrawWrapped(ctx, hoverDetails, 32, 620, width - 64, 17, TextGold, 2);
        DrawText(ctx, "Select a command to close this menu, then right-click the whistle.", 32, 676, 13, TextWhite);
        DrawButton(ctx, "Clear selection", 18, 690, 202, 38, string.IsNullOrWhiteSpace(selected));
        DrawText(ctx, "Clearing returns the whistle to PetAI's normal command behavior.", 236, 714, 13, TextMuted);
    }

    private void DrawDuties(Context ctx, int width)
    {
        if (!dutyMaskInitialized)
        {
            string selected = system.GetClientWhistleCommand();
            dutyMask = CompanionWhistleCommand.TryGetDutyMask(selected, out int selectedMask)
                ? selectedMask
                : CompanionDuty.AllExceptSnowballsMask;
            dutyMaskInitialized = true;
        }

        DrawText(ctx, "Select duties to apply to nearby Companions", 24, 198, 16, TextGold);
        DrawCheckbox(ctx, "Dropped items", 32, 216, CompanionDuty.IsSet(dutyMask, CompanionDuty.GroundDroppedItemsBit));
        DrawCheckbox(ctx, "Cattails", 32, 246, CompanionDuty.IsSet(dutyMask, CompanionDuty.GroundCattailsBit));
        DrawCheckbox(ctx, "Flint", 32, 276, CompanionDuty.IsSet(dutyMask, CompanionDuty.GroundFlintBit));
        DrawCheckbox(ctx, "Sticks", 32, 306, CompanionDuty.IsSet(dutyMask, CompanionDuty.GroundSticksBit));
        DrawCheckbox(ctx, "Boulders", 240, 216, CompanionDuty.IsSet(dutyMask, CompanionDuty.GroundBouldersBit));
        DrawCheckbox(ctx, "Rocks", 240, 246, CompanionDuty.IsSet(dutyMask, CompanionDuty.GroundRocksBit));
        DrawCheckbox(ctx, "Mow grass", 240, 276, CompanionDuty.IsSet(dutyMask, CompanionDuty.MowLawnBit));
        DrawCheckbox(ctx, "Crops", 240, 306, CompanionDuty.IsSet(dutyMask, CompanionDuty.FinishedCropsBit));
        DrawCheckbox(ctx, "Berries", 448, 216, CompanionDuty.IsSet(dutyMask, CompanionDuty.FinishedBerriesBit));
        DrawCheckbox(ctx, "Mushrooms", 448, 246, CompanionDuty.IsSet(dutyMask, CompanionDuty.FinishedMushroomsBit));
        DrawCheckbox(ctx, "Flowers", 448, 276, CompanionDuty.IsSet(dutyMask, CompanionDuty.FlowerRemovalBit));
        DrawCheckbox(ctx, "Snow", 448, 306, CompanionDuty.IsSet(dutyMask, CompanionDuty.SnowShovelingBit));
        DrawCheckbox(ctx, "Collect snowballs", 32, 350, CompanionDuty.IsSet(dutyMask, CompanionDuty.SnowballCollectionBit));
        DrawCheckbox(ctx, "Sort storage", 240, 350, CompanionDuty.IsSet(dutyMask, CompanionDuty.StorageSortingBit));
        DrawCheckbox(ctx, "Charcoal", 448, 350, CompanionDuty.IsSet(dutyMask, CompanionDuty.CharcoalShovelingBit));
        DrawButton(ctx, "Enable all", 18, 400, 155, 38, false);
        DrawButton(ctx, "Disable all", 187, 400, 155, 38, false);
        FoxGuiTheme.DrawPanel(ctx, 18, 454, width - 36, 120, false);
        DrawWrapped(ctx, "Enable all selects every normal duty, including storage sorting. Snowballs stay optional.", 32, 482, width - 64, 18, TextMuted, 3);
        DrawWrapped(ctx, "The whistle reaches owned adult Companions within 15 blocks horizontally and 5 blocks vertically.", 32, 538, width - 64, 17, TextGold, 2);
        DrawButton(ctx, "Apply selected duties", 18, 690, 322, 38, false);
        DrawButton(ctx, "Clear selection", 360, 690, 202, 38, false);
    }

    private static void DrawCheckbox(Context ctx, string label, double x, double y, bool checkedValue)
    {
        FoxGuiTheme.DrawChoiceSurface(ctx, x, y, 22, 22, checkedValue);
        if (checkedValue)
        {
            ctx.SetSourceRGBA(TextGold[0], TextGold[1], TextGold[2], TextGold[3]);
            ctx.Rectangle(x + 5, y + 5, 12, 12);
            ctx.Fill();
        }
        DrawText(ctx, label, x + 30, y + 17, 14, checkedValue ? TextWhite : TextMuted);
    }

    private static int FindDutyOption(double x, double y)
    {
        (double X, double Y, int Bit)[] options =
        {
            (32, 216, CompanionDuty.GroundDroppedItemsBit), (32, 246, CompanionDuty.GroundCattailsBit),
            (32, 276, CompanionDuty.GroundFlintBit), (32, 306, CompanionDuty.GroundSticksBit),
            (240, 216, CompanionDuty.GroundBouldersBit), (240, 246, CompanionDuty.GroundRocksBit),
            (240, 276, CompanionDuty.MowLawnBit), (240, 306, CompanionDuty.FinishedCropsBit),
            (448, 216, CompanionDuty.FinishedBerriesBit), (448, 246, CompanionDuty.FinishedMushroomsBit),
            (448, 276, CompanionDuty.FlowerRemovalBit), (448, 306, CompanionDuty.SnowShovelingBit),
            (32, 350, CompanionDuty.SnowballCollectionBit),
            (240, 350, CompanionDuty.StorageSortingBit),
            (448, 350, CompanionDuty.CharcoalShovelingBit)
        };
        foreach ((double X, double Y, int Bit) option in options)
        {
            if (x >= option.X && x <= option.X + 190 && y >= option.Y && y <= option.Y + 24) return option.Bit;
        }
        return 0;
    }

    private static string GetCommandDescription(string command)
    {
        return command switch
        {
            "activity:follow" => "Stay close and keep pace with you.",
            "activity:atease" => "Wander, socialize, play, and rest normally.",
            "activity:rest" => "Return home and recover until fully healed.",
            "activity:returnhome" => "Return home; the companion knows the way regardless of distance.",
            "combat:passive" => "Never starts fights.",
            "combat:defensive" => "Fights only after this companion is attacked.",
            "combat:protect" => "Defends you when you are attacked.",
            "combat:assist" => "Helps fight what you attack.",
            "combat:aggressive" => "Looks for known hostile creatures nearby.",
            "combat:flee" => "Avoids threats and flees from danger.",
            "target:attack" => "Orders nearby Companions to attack the creature you are looking at.",
            "risk:cautious" => "Retreats home below 40% health.",
            "risk:steady" => "Retreats home below 20% health.",
            "risk:fearless" => "Never retreats automatically.",
            "utility:drop-held-items" => "Drops each nearby Companion's carried cargo and holds them still for 10 seconds.",
            "duty:cleanup" => "Enable Ground Cleanup and disable the other background duties.",
            "duty:mow" => "Enable Mow the Lawn and disable the other background duties.",
            "duty:finished" => "Enable Gather Finished Products and disable the other background duties.",
            "duty:flowers" => "Enable Flower Removal and disable the other background duties.",
            "duty:snow" => "Enable Snow Shoveling and disable the other background duties.",
            "duty:charcoal" => "Enable Charcoal Shoveling and disable the other background duties.",
            "duty:all" => "Enable all normal duties, including storage sorting; snowball collection remains optional.",
            "duty:none" => "Disable all background duties.",
            var custom when CompanionWhistleCommand.IsValid(custom)
                && CompanionWhistleCommand.IsDuty(custom) => "Apply the selected duty checkboxes to nearby adult Companions.",
            _ => string.Empty
        };
    }

    private static string FindCommand(double x, double y)
    {
        if (y >= 210 && y < 244)
        {
            if (x >= 18 && x < 175) return "activity:follow";
            if (x >= 187 && x < 344) return "activity:atease";
            if (x >= 356 && x < 513) return "activity:rest";
            if (x >= 525 && x < 682) return "activity:returnhome";
        }
        else if (y >= 282 && y < 316)
        {
            if (x >= 18 && x < 228) return "combat:passive";
            if (x >= 245 && x < 455) return "combat:defensive";
            if (x >= 472 && x < 682) return "combat:protect";
        }
        else if (y >= 322 && y < 356)
        {
            if (x >= 18 && x < 228) return "combat:assist";
            if (x >= 245 && x < 455) return "combat:aggressive";
            if (x >= 472 && x < 682) return "combat:flee";
        }
        else if (y >= 362 && y < 396)
        {
            if (x >= 18 && x < 228) return "target:attack";
        }
        else if (y >= 432 && y < 466)
        {
            if (x >= 18 && x < 228) return "risk:cautious";
            if (x >= 245 && x < 455) return "risk:steady";
            if (x >= 472 && x < 682) return "risk:fearless";
        }
        else if (y >= 512 && y < 546 && x >= 18 && x < 228)
        {
            return "utility:drop-held-items";
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
        DrawText(ctx, label, x + 12, y + height / 2 + 6, 15, selected ? TextGold : TextWhite);
    }

    private static void DrawButton(Context ctx, string label, double x, double y, double width, double height, bool disabled)
    {
        FoxGuiTheme.DrawButtonSurface(ctx, x, y, width, height, !disabled);
        DrawText(ctx, label, x + 12, y + height / 2 + 6, 15, disabled ? TextMuted : TextWhite);
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
