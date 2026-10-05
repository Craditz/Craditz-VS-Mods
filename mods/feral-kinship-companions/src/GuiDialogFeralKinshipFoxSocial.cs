#nullable disable

using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace FeralKinshipCompanions;

public sealed class GuiDialogFeralKinshipFoxSocial : GuiDialog
{
    private const double DialogWidth = 1020;
    private const double DialogHeight = 820;
    private const double Padding = 0;

    private readonly FeralKinshipCompanionSystem system;
    private readonly long targetEntityId;
    private GuiElementFeralKinshipFoxSocialSurface surface;
    private FoxSocialStatePacket state;

    private readonly string initialTab;

    public GuiDialogFeralKinshipFoxSocial(ICoreClientAPI capi, FeralKinshipCompanionSystem system, long targetEntityId, string initialTab = "overview")
        : base(capi)
    {
        this.system = system;
        this.targetEntityId = targetEntityId;
        this.initialTab = initialTab;
    }

    public override string ToggleKeyCombinationCode => null;

    public override void OnGuiOpened()
    {
        ComposeDialog();
        base.OnGuiOpened();
        surface?.ApplyState(state);
    }

    public override void OnGuiClosed()
    {
        surface?.NotifyDialogClosed();
        system.SendFoxSocialAction(targetEntityId, FoxSocialRequestAction.CloseView);
        base.OnGuiClosed();
    }

    public void ApplyState(FoxSocialStatePacket packet)
    {
        if (packet == null || packet.TargetEntityId != targetEntityId)
        {
            return;
        }

        state = packet;
        surface?.ApplyState(packet);
    }

    private void ComposeDialog()
    {
        ElementBounds contentBounds = ElementBounds.Fixed(0, 0, DialogWidth, DialogHeight)
            .WithFixedPadding(Padding);
        contentBounds.BothSizing = ElementSizing.Fixed;
        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
        GuiComposer composer = capi.Gui
            .CreateCompo("feralkinship-fox-social", dialogBounds)
            .AddShadedDialogBG(contentBounds)
            .AddDialogTitleBar("Feral Kinship Companion", () => TryClose())
            .BeginChildElements(contentBounds);

        surface = new GuiElementFeralKinshipFoxSocialSurface(
            capi,
            ElementBounds.Fixed(0, 0, DialogWidth, DialogHeight),
            system,
            targetEntityId,
            initialTab
        );
        composer.AddInteractiveElement(surface, "fox-social-surface");
        SingleComposer = composer.Compose();
    }
}

internal sealed class GuiElementFeralKinshipFoxSocialSurface : GuiElementFeralKinshipScaledSurface
{
    protected override double DesignWidth => 1020;
    protected override double DesignHeight => 820;
    private const double TabY = 88;
    private const double TabHeight = 36;
    private const double ButtonY = 762;
    private const double ButtonHeight = 34;
    private const double TextSize = 15;
    private static double[] TextWhite => FoxGuiTheme.Text;
    private static double[] TextMuted => FoxGuiTheme.Muted;
    private static double[] TextGold => FoxGuiTheme.Accent;
    private static double[] TextPanel => FoxGuiTheme.PanelColor;
    private static double[] TextButton => FoxGuiTheme.ButtonColor;
    private static double[] TextButtonDisabled => FoxGuiTheme.ButtonDisabledColor;
    private static double[] TextOverviewAccent => FoxGuiTheme.OverviewAccent;
    private static double[] TextCommandsAccent => FoxGuiTheme.CommandsAccent;
    private static double[] TextSocialAccent => FoxGuiTheme.SocialAccent;
    private static double[] TextTalentsAccent => FoxGuiTheme.TalentsAccent;

    private readonly FeralKinshipCompanionSystem system;
    private readonly long targetEntityId;
    private FoxSocialStatePacket state;
    private readonly CompanionAnimalArt animalArt;
    private long stateReceivedAtMs;
    private long lastLocalRedrawAtMs;
    private bool helpOpen;
    private bool helpHovered;
    private bool tabHovered;
    private string activeTab = "overview";
    private string hoveredTabId = string.Empty;
    private string hoveredCommandId = string.Empty;
    private bool tutorialOpen;
    private bool tutorialDismissalNotified;
    private string activeDutyTab = "overview";
    private double bonusesScroll;
    private double bonusesMaxScroll;
    private bool portraitPickerOpen;
    private string[] portraitFiles = Array.Empty<string>();
    private int portraitPage;
    private string portraitMessage = string.Empty;
    private int textureId;

    public GuiElementFeralKinshipFoxSocialSurface(
        ICoreClientAPI capi,
        ElementBounds bounds,
        FeralKinshipCompanionSystem system,
        long targetEntityId,
        string initialTab = "overview")
        : base(capi, bounds)
    {
        this.system = system;
        this.targetEntityId = targetEntityId;
        animalArt = new CompanionAnimalArt(capi);
        activeTab = initialTab is "commands" or "duties" or "social" or "bonuses" ? initialTab : "overview";
        tutorialOpen = system.ShouldShowCompanionTutorial();
    }

    public void ApplyState(FoxSocialStatePacket packet)
    {
        state = packet;
        stateReceivedAtMs = api.World.ElapsedMilliseconds;
        Redraw();
    }

    public void NotifyDialogClosed()
    {
        if (tutorialOpen)
        {
            DismissTutorial();
        }
    }

    public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
    {
        Bounds.CalcWorldBounds();
        Redraw();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        double mouseX = CanvasMouseX(api.Input.MouseX);
        double mouseY = CanvasMouseY(api.Input.MouseY);
        bool hovered = FoxGuiTheme.IsHelpHovered(
            mouseX,
            mouseY,
            CanvasWidth);
        string tab = FindHoveredTab(mouseX, mouseY, state?.IsJuvenile == true);
        string command = FindHoveredCommand(mouseX, mouseY);
        bool needsRedraw = false;
        if (hovered != helpHovered)
        {
            helpHovered = hovered;
            needsRedraw = true;
        }
        if (!string.Equals(tab, hoveredTabId, StringComparison.Ordinal))
        {
            hoveredTabId = tab;
            tabHovered = !string.IsNullOrEmpty(tab);
            needsRedraw = true;
        }
        if (!string.Equals(command, hoveredCommandId, StringComparison.Ordinal))
        {
            hoveredCommandId = command;
            needsRedraw = true;
        }
        if (needsRedraw)
        {
            Redraw();
        }

        long now = api.World.ElapsedMilliseconds;
        if (state != null && now - lastLocalRedrawAtMs >= 500)
        {
            lastLocalRedrawAtMs = now;
            Redraw();
        }

        if (textureId > 0)
        {
            Render2DTexture(textureId, Bounds);
        }
    }

    public override void Dispose()
    {
        animalArt.Dispose();
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

        HandleClick(x, y);
    }

    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        if (portraitPickerOpen)
        {
            double pageDelta = args.deltaPrecise != 0f ? args.deltaPrecise : args.delta;
            int maxPage = Math.Max(0, (portraitFiles.Length - 1) / 8);
            portraitPage = Math.Clamp(portraitPage + (pageDelta < 0 ? 1 : -1), 0, maxPage);
            args.SetHandled();
            Redraw();
            return;
        }
        if (activeTab != "bonuses" || helpOpen || tutorialOpen) return;
        double x = CanvasMouseX(api.Input.MouseX);
        double y = CanvasMouseY(api.Input.MouseY);
        if (x < 18 || x > CanvasWidth - 18 || y < 136 || y > 732) return;
        double amount = args.deltaPrecise != 0f ? args.deltaPrecise : args.delta;
        bonusesScroll = Math.Clamp(bonusesScroll - amount * 45, 0, bonusesMaxScroll);
        args.SetHandled();
        Redraw();
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
        FoxGuiTheme.DrawJournal(ctx, width, height, FoxGuiSurfaceKind.Companion);
        if (portraitPickerOpen)
        {
            DrawPortraitPicker(ctx, width);
            FoxGuiTheme.DrawWindowControls(ctx, api, width);
            return;
        }
        if (tutorialOpen)
        {
            DrawTutorialOverlay(ctx, width, height);
            FoxGuiTheme.DrawWindowControls(ctx, api, width);
            FoxGuiTheme.DrawHelpGlyph(ctx, width, helpHovered);
            return;
        }
        if (helpOpen)
        {
            DrawHelpOverlay(ctx, width, height);
            FoxGuiTheme.DrawWindowControls(ctx, api, width);
            FoxGuiTheme.DrawHelpGlyph(ctx, width, helpHovered);
            return;
        }

        FoxSocialStatePacket current = state;
        if (current == null)
        {
            FoxGuiTheme.DrawPanel(ctx, 18, 18, width - 36, 82, false);
            DrawText(ctx, "Waiting for companion data...", 32, 51, 20, TextWhite);
            DrawWrapped(ctx,
                "The companion may be outside interaction range, or still answering. Move closer and give it a moment.",
                32,
                77,
                width - 64,
                15,
                TextMuted,
                2);
            FoxGuiTheme.DrawWindowControls(ctx, api, width);
            FoxGuiTheme.DrawHelpGlyph(ctx, width, helpHovered);
            return;
        }

        float elapsed = Math.Max(0f, (api.World.ElapsedMilliseconds - stateReceivedAtMs) / 1000f);
        float requestCooldown = Math.Max(0f, current.RequestCooldownSeconds - elapsed);
        float cancelCooldown = Math.Max(0f, current.CancelCooldownSeconds - elapsed);
        bool active = !string.Equals(current.ActiveRequest, "None", StringComparison.OrdinalIgnoreCase);
        bool canAsk = !active && requestCooldown <= 0.01f;

        string displayName = string.IsNullOrWhiteSpace(current.Name) ? "Unnamed companion" : current.Name.Trim();
        DrawText(ctx, Trim(displayName, 34), 18, 34, 27, TextWhite);
        string lifeStage = current.IsJuvenile
            ? "Child · adulthood EXP pending"
            : current.PregnancyActive ? Lang.Get("feralkinshipcompanions:pregnancy-stage") : string.Empty;
        DrawText(ctx, $"Personality: {Trim(current.Personality, 24)}   |   Mood: {Trim(current.Mood, 24)}", 18, 58, TextSize, TextMuted);
        if (!string.IsNullOrWhiteSpace(lifeStage))
        {
            // Keep the longer pregnancy text on its own header line so it
            // cannot collide with the activity/combat summary on the right.
            DrawText(ctx, lifeStage, 18, 79, 13, TextGold);
        }
        DrawText(ctx,
            $"{CompanionActivityMode.DisplayName(current.ActivityMode)} · {CompanionCombatStyle.DisplayName(current.CombatStyle)}",
            476, 58, 13, TextGold);
        if (!current.IsJuvenile)
        {
            DrawText(ctx,
                $"Level {current.Level} · {current.CurrentLevelExperience}/{current.RequiredLevelExperience} EXP",
                476, 79, 13, TextGold);
        }
        DrawExperienceBar(ctx, current, 18, 82, width - 36, 4);

        if (current.IsJuvenile && string.Equals(activeTab, "duties", StringComparison.Ordinal))
        {
            activeTab = "overview";
        }
        DrawTabs(ctx, width, current.IsJuvenile);
        double[] accent = GetTabAccent(activeTab);
        if (string.Equals(activeTab, "commands", StringComparison.Ordinal))
        {
            DrawCommands(ctx, width, current, accent);
        }
        else if (string.Equals(activeTab, "duties", StringComparison.Ordinal))
        {
            DrawDuties(ctx, width, current, accent);
        }
        else if (string.Equals(activeTab, "social", StringComparison.Ordinal))
        {
            DrawSocial(ctx, width, current, active, requestCooldown, cancelCooldown, accent);
        }
        else if (activeTab == "bonuses")
        {
            DrawBonuses(ctx, width, current, accent);
        }
        else
        {
            DrawOverview(ctx, width, current, active, requestCooldown, cancelCooldown, accent);
        }

        DrawActionButtons(ctx, width, current, accent);

        FoxGuiTheme.DrawWindowControls(ctx, api, width);
        FoxGuiTheme.DrawHelpGlyph(ctx, width, helpHovered);
    }

    private void DrawPortraitPicker(Context ctx, int width)
    {
        DrawText(ctx, "Choose a portrait", 24, 39, 26, TextWhite);
        DrawText(ctx, "PNG files in this client's portrait folder. Choose one for this companion.", 24, 67, 14, TextMuted);
        DrawWrapped(ctx, animalArt.PortraitFolder, 24, 91, width - 48, 17, TextGold, 2);
        string assigned = animalArt.AssignedImage(state?.FoxId) ?? "Built-in portrait";
        DrawText(ctx, "Current: " + Trim(assigned, 80), 24, 131, 14, TextGold);
        if (!string.IsNullOrEmpty(portraitMessage))
            DrawText(ctx, Trim(portraitMessage, 90), 24, 153, 13, TextMuted);

        if (portraitFiles.Length == 0)
        {
            FoxGuiTheme.DrawPanel(ctx, 24, 176, width - 48, 476, false);
            DrawWrapped(ctx, "No PNG files found. Copy PNGs into the folder above, then press Refresh.",
                46, 214, width - 92, 24, TextWhite, 3);
        }
        else
        {
            int first = portraitPage * 8;
            for (int slot = 0; slot < 8 && first + slot < portraitFiles.Length; slot++)
            {
                string name = portraitFiles[first + slot];
                double x = slot % 2 == 0 ? 24 : 516;
                double y = 176 + slot / 2 * 124;
                bool selected = string.Equals(assigned, name, StringComparison.OrdinalIgnoreCase);
                FoxGuiTheme.DrawPanel(ctx, x, y, 480, 116, selected);
                animalArt.DrawCustomPreview(ctx, name, x + 8, y + 8, 100);
                DrawWrapped(ctx, name, x + 122, y + 33, 342, 20,
                    selected ? TextGold : TextWhite, 2);
                DrawText(ctx, selected ? "Selected" : "Click to use", x + 122, y + 95, 12,
                    selected ? TextGold : TextMuted);
            }
        }
        int maxPage = Math.Max(0, (portraitFiles.Length - 1) / 8);
        DrawText(ctx, $"Page {portraitPage + 1} / {maxPage + 1}  ·  {portraitFiles.Length} PNGs", 24, 696, 13, TextMuted);
        DrawButton(ctx, "Built-in art", 24, 724, 196, 36, true);
        DrawButton(ctx, "Refresh", 232, 724, 154, 36, true);
        DrawButton(ctx, "Previous", 398, 724, 154, 36, portraitPage > 0);
        DrawButton(ctx, "Next", 564, 724, 154, 36, portraitPage < maxPage);
        DrawButton(ctx, "Back", 730, 724, 274, 36, true);
    }

    private void HandlePortraitPickerClick(double x, double y)
    {
        if (y >= 724 && y <= 760)
        {
            if (x >= 24 && x <= 220)
            {
                portraitMessage = animalArt.ChooseImage(state?.FoxId, null) ? "Built-in art selected." : "Could not save the choice.";
            }
            else if (x >= 232 && x <= 386)
            {
                animalArt.RefreshCustomImages();
                portraitFiles = animalArt.ListCustomImages();
                portraitPage = Math.Clamp(portraitPage, 0, Math.Max(0, (portraitFiles.Length - 1) / 8));
                portraitMessage = "Folder refreshed.";
            }
            else if (x >= 398 && x <= 552) portraitPage = Math.Max(0, portraitPage - 1);
            else if (x >= 564 && x <= 718) portraitPage = Math.Min(Math.Max(0, (portraitFiles.Length - 1) / 8), portraitPage + 1);
            else if (x >= 730 && x <= 1004) portraitPickerOpen = false;
            else return;
            FoxGuiTheme.PlayNavigation(api);
            Redraw();
            return;
        }
        for (int slot = 0; slot < 8; slot++)
        {
            int index = portraitPage * 8 + slot;
            if (index >= portraitFiles.Length) break;
            double left = slot % 2 == 0 ? 24 : 516;
            double top = 176 + slot / 2 * 124;
            if (x < left || x > left + 480 || y < top || y > top + 116) continue;
            portraitMessage = animalArt.ChooseImage(state?.FoxId, portraitFiles[index])
                ? "Portrait selected for this companion." : "Could not load or save that PNG. Check the game log.";
            FoxGuiTheme.PlayChoice(api);
            Redraw();
            return;
        }
    }

    private void DrawTabs(Context ctx, int width, bool juvenile)
    {
        string[] ids = ProfileTabs(juvenile);
        double gap = 8;
        double tabWidth = (width - 36 - gap * (ids.Length - 1)) / ids.Length;
        for (int index = 0; index < ids.Length; index++)
        {
            double x = 18 + index * (tabWidth + gap);
            bool selected = string.Equals(activeTab, ids[index], StringComparison.Ordinal);
            bool hovered = string.Equals(hoveredTabId, ids[index], StringComparison.Ordinal);
            DrawTabButton(ctx, char.ToUpperInvariant(ids[index][0]) + ids[index].Substring(1), x, TabY, tabWidth, TabHeight, selected, hovered, GetTabAccent(ids[index]));
        }
    }

    internal static string[] ProfileTabs(bool juvenile) => juvenile
        ? new[] { "overview", "commands", "social", "bonuses" }
        : new[] { "overview", "commands", "duties", "social", "bonuses" };

    private void DrawBonuses(Context ctx, int width, FoxSocialStatePacket current, double[] accent)
    {
        FoxGuiTheme.DrawPanel(ctx, 18, 136, width - 36, 596, false);
        double scale = FeralKinshipCompanionUiSettings.TextScale;
        double y = 152 + TextSize * scale - bonusesScroll;
        ctx.Save();
        ctx.Rectangle(30, 148, width - 60, 572);
        ctx.Clip();
        void Paragraph(string text, double[] color)
        {
            int lines = DrawWrapped(ctx, text, 32, y, width - 80, 21, color, int.MaxValue);
            y += lines * 21 * scale + 12;
        }
        var trait = CompanionSpeciesTraits.Get(current.SpeciesId);
        if (trait == null)
        {
            Paragraph("Innate expedition bonuses", accent);
            Paragraph("This species has no innate expedition bonus or species party synergy in this preview.", TextWhite);
        }
        else
        {
            int training = Math.Clamp(current.SpeciesTraining, 0, 2);
            Paragraph($"{current.SpeciesDisplayName} innate: {trait.Name}", accent);
            Paragraph("Free from the start. Applies to expeditions. " + (current.IsJuvenile
                ? "Available when this companion reaches adulthood."
                : "No talent purchase is needed to use the base bonus."), TextMuted);
            Paragraph($"Base: 15% | Training: 20% | Mastery: 25%. Current: {(int)Math.Round(CompanionSpeciesTraits.Affinity(training) * 100f)}% ({(training == 2 ? "Mastery" : training == 1 ? "Training" : "untrained")}).", TextGold);
            foreach (string effect in CompanionSpeciesTraits.InnateDetails(trait.Species, training))
                Paragraph(effect, TextWhite);
            if (trait.Species != "wolf")
                Paragraph("Shared effects: add the relevant animals' percentages, then divide by EVERY companion in the party. One untrained animal in a party of two contributes 7.5%; two contribute 15%.", TextMuted);
            Paragraph($"Party synergy: {trait.Synergy}", accent);
            Paragraph($"Requires this species to make up more than half the party. No synergy for a solo companion or a tie. {trait.SynergyDescription}", TextWhite);
            Paragraph("This is a separate party bonus. It needs no talent points and applies once per expedition on a matching route or encounter.", TextMuted);
        }
        ctx.Restore();
        bonusesMaxScroll = Math.Max(0, y + bonusesScroll - 720);
        bonusesScroll = Math.Min(bonusesScroll, bonusesMaxScroll);
        if (bonusesMaxScroll > 0)
            DrawText(ctx, "Scroll here to read all bonuses", 32, 749, 11, TextMuted);
    }

    private void DrawOverview(Context ctx, int width, FoxSocialStatePacket current, bool active, float requestCooldown, float cancelCooldown, double[] accent)
    {
        FoxGuiTheme.DrawPanel(ctx, 18, 124, width - 36, 100, false);
        DrawText(ctx, "Pack record", 30, 148, 16, accent);
        DrawButton(ctx, "Rename", width - 170, 132, 140, 27, true);
        DrawText(ctx, $"Requests: {current.RequestsCompleted} fulfilled / {current.RequestsGenerated} asked", 30, 172, TextSize, TextWhite);
        DrawText(ctx, current.IsJuvenile
            ? Lang.Get("feralkinshipcompanions:child-banked-points", current.BankedTalentPoints)
            : $"Companion points available: {current.Points}   ·   Lifetime EXP: {current.LifetimeExperience}", 30, 195, TextSize, TextMuted);
        DrawFoodBar(ctx, current, width - 358, 178, 328, 13);
        if (current.IsJuvenile)
        {
            DrawText(ctx, Lang.Get("feralkinshipcompanions:child-health", current.CurrentHealth, current.MaxHealth), width - 250, 195, 13, TextGold);
        }
        if (!string.IsNullOrWhiteSpace(current.CarriedItem))
        {
            DrawText(ctx, $"Cargo secured: {Trim(current.CarriedItem, 40)}", 30, 218, 13, accent);
            DrawButton(ctx, "Retrieve held item", width - 184, 166, 154, 27, true);
        }
        else
        {
            DrawText(ctx, current.IsJuvenile
                ? Lang.Get("feralkinshipcompanions:child-parents", ParentLabel(current.MotherName), ParentLabel(current.FatherName))
                : "Not carrying collection cargo.", 30, 218, 13, TextMuted);
        }

        FoxGuiTheme.DrawPanel(ctx, 18, 236, 226, 266, false);
        animalArt.DrawBody(ctx, current.SpeciesId, current.AppearanceCode, 30, 248, 202, targetEntityId, current.FoxId);
        DrawButton(ctx, "Portrait...", 30, 459, 202, 30, true);

        FoxGuiTheme.DrawPanel(ctx, 256, 236, width - 274, 142, false);
        DrawText(ctx, "Temperament", 268, 262, 16, accent);
        DrawWrapped(ctx, current.PersonalitySummary, 268, 288, width - 300, 18, TextWhite, 2);
        DrawText(ctx, "Right now", 268, 334, 16, accent);
        DrawWrapped(ctx, current.CurrentThought, 268, 360, width - 300, 18, TextMuted, 2);

        FoxGuiTheme.DrawPanel(ctx, 256, 390, width - 274, 112, false);
        DrawText(ctx, "Current direction", 268, 416, 16, accent);
        DrawText(ctx, $"{CompanionActivityMode.DisplayName(current.ActivityMode)} · {CompanionCombatStyle.DisplayName(current.CombatStyle)}", 268, 444, TextSize, TextWhite);
        DrawText(ctx, $"Risk tolerance: {CompanionRiskTolerance.DisplayName(current.RiskTolerance)}", 268, 468, 13, TextMuted);
        var speciesTrait = CompanionSpeciesTraits.Get(current.SpeciesId);
        DrawText(ctx, speciesTrait == null ? "Use Commands to change how this companion behaves."
            : speciesTrait.Name + " - see Bonuses for effects and party synergy.", 268, 490, 11, TextMuted);

        DrawRequestPanel(ctx, width, current, active, requestCooldown, cancelCooldown, 518, 126, accent);
    }

    private void DrawCommands(Context ctx, int width, FoxSocialStatePacket current, double[] accent)
    {
        FoxGuiTheme.DrawPanel(ctx, 12, 124, width - 24, 154, false);
        DrawText(ctx, "Activity", 30, 146, 16, accent);
        DrawChoiceButton(ctx, "Follow", 18, 158, 190, 34, Is(current.ActivityMode, CompanionActivityMode.Follow), accent);
        DrawChoiceButton(ctx, "At Ease", 218, 158, 190, 34, Is(current.ActivityMode, CompanionActivityMode.AtEase), accent);
        DrawChoiceButton(ctx, "Rest", 418, 158, 190, 34, Is(current.ActivityMode, CompanionActivityMode.Rest), accent);
        DrawChoiceButton(ctx, "Return Home", 618, 158, 190, 34, Is(current.ActivityMode, CompanionActivityMode.ReturnHome), accent);

        bool following = Is(current.ActivityMode, CompanionActivityMode.Follow);
        string previewActivity = hoveredCommandId.StartsWith("activity:", StringComparison.Ordinal)
            ? hoveredCommandId.Substring("activity:".Length)
            : current.ActivityMode;
        bool showFollowControls = following
            && string.Equals(previewActivity, current.ActivityMode, StringComparison.Ordinal);
        DrawText(ctx, showFollowControls ? "Follow range" : "Mode details", 18, 244, 15, showFollowControls ? TextWhite : TextMuted);
        if (showFollowControls)
        {
            DrawChoiceButton(ctx, "Close", 170, 222, 200, 32, Is(current.FollowDistance, CompanionFollowDistance.Close), accent);
            DrawChoiceButton(ctx, "Normal", 380, 222, 200, 32, Is(current.FollowDistance, CompanionFollowDistance.Normal), accent);
            DrawChoiceButton(ctx, "Hang Back", 590, 222, 218, 32, Is(current.FollowDistance, CompanionFollowDistance.Back), accent);
        }
        else
        {
            DrawWrapped(ctx, GetActivitySummary(previewActivity), 170, 244, 630, 14, TextMuted, 2);
        }

        if (current.IsJuvenile)
        {
            FoxGuiTheme.DrawPanel(ctx, 18, 290, width - 36, 220, false);
            DrawText(ctx, Lang.Get("feralkinshipcompanions:child-command-heading"), 30, 318, 16, accent);
            DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:child-command-text"), 30, 350, width - 60, 18, TextWhite, 5);
            DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:child-source-ai-note"), 30, 462, width - 60, 13, TextMuted, 2);
            return;
        }

        FoxGuiTheme.DrawPanel(ctx, 12, 290, width - 24, 220, false);
        DrawText(ctx, "Combat style", 30, 314, 16, accent);
        DrawChoiceButton(ctx, "Passive", 18, 328, 260, 32, Is(current.CombatStyle, CompanionCombatStyle.Passive), accent);
        DrawChoiceButton(ctx, "Defensive", 292, 328, 260, 32, Is(current.CombatStyle, CompanionCombatStyle.Defensive), accent);
        DrawChoiceButton(ctx, "Protect", 566, 328, 242, 32, Is(current.CombatStyle, CompanionCombatStyle.Protect), accent);
        DrawChoiceButton(ctx, "Assist", 18, 370, 260, 32, Is(current.CombatStyle, CompanionCombatStyle.Assist), accent);
        DrawChoiceButton(ctx, "Aggressive", 292, 370, 260, 32, Is(current.CombatStyle, CompanionCombatStyle.Aggressive), accent);
        DrawChoiceButton(ctx, "Flee", 566, 370, 242, 32, Is(current.CombatStyle, CompanionCombatStyle.Flee), accent);
        DrawWrapped(ctx, GetCombatStyleSummary(hoveredCommandId.StartsWith("combat:", StringComparison.Ordinal)
            ? hoveredCommandId.Substring("combat:".Length)
            : current.CombatStyle), 30, 422, 774, 12, TextMuted, 2);

        DrawText(ctx, "Risk tolerance", 30, 454, 15, TextWhite);
        DrawChoiceButton(ctx, "Cautious", 170, 462, 200, 32, Is(current.RiskTolerance, CompanionRiskTolerance.Cautious), accent);
        DrawChoiceButton(ctx, "Steady", 380, 462, 200, 32, Is(current.RiskTolerance, CompanionRiskTolerance.Steady), accent);
        DrawChoiceButton(ctx, "Fearless", 590, 462, 218, 32, Is(current.RiskTolerance, CompanionRiskTolerance.Fearless), accent);
        DrawWrapped(ctx, GetRiskToleranceSummary(hoveredCommandId.StartsWith("risk:", StringComparison.Ordinal)
            ? hoveredCommandId.Substring("risk:".Length)
            : current.RiskTolerance), 30, 506, 774, 12, TextMuted, 2);

        FoxGuiTheme.DrawPanel(ctx, 18, 530, width - 36, 104, false);
        DrawText(ctx, "Commands change behavior immediately and remain saved with this companion.", 30, 558, 14, TextWhite);
        DrawWrapped(ctx, "Follow range affects how closely this companion travels. Combat style chooses when it fights; risk tolerance chooses when it retreats.", 30, 586, 774, 16, TextMuted, 3);
        FoxGuiTheme.DrawPanel(ctx, 822, 124, 180, 510, false);
        animalArt.DrawBody(ctx, current.SpeciesId, current.AppearanceCode, 834, 137, 156, targetEntityId, current.FoxId);
        DrawText(ctx, "Current orders", 834, 326, 15, accent);
        DrawWrapped(ctx, CompanionActivityMode.DisplayName(current.ActivityMode),
            834, 354, 156, 17, TextWhite, 2);
        DrawText(ctx, "Combat", 834, 410, 15, accent);
        DrawWrapped(ctx, CompanionCombatStyle.DisplayName(current.CombatStyle),
            834, 438, 156, 17, TextWhite, 2);
        DrawText(ctx, "Risk", 834, 494, 15, accent);
        DrawWrapped(ctx, CompanionRiskTolerance.DisplayName(current.RiskTolerance),
            834, 522, 156, 17, TextWhite, 2);
    }

    private void DrawSocial(Context ctx, int width, FoxSocialStatePacket current, bool active, float requestCooldown, float cancelCooldown, double[] accent)
    {
        FoxGuiTheme.DrawPanel(ctx, 18, 124, width - 36, 168, false);
        DrawText(ctx, "Personality & mood", 30, 150, 16, accent);
        DrawText(ctx, $"Personality: {Trim(current.Personality, 30)}", 30, 180, TextSize, TextWhite);
        DrawText(ctx, $"Mood: {Trim(current.Mood, 30)}", 30, 204, TextSize, TextMuted);
        DrawWrapped(ctx, current.PersonalitySummary, 30, 234, width - 60, 18, TextWhite, 2);
        DrawWrapped(ctx, current.CurrentThought, 30, 270, width - 60, 18, TextMuted, 2);

        DrawRequestPanel(ctx, width, current, active, requestCooldown, cancelCooldown, 310, 154, accent);

        FoxGuiTheme.DrawPanel(ctx, 18, 480, width - 36, 184, false);
        if (current.IsJuvenile)
        {
            DrawText(ctx, Lang.Get("feralkinshipcompanions:child-family-heading"), 30, 506, 16, accent);
            DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:child-parents", ParentLabel(current.MotherName), ParentLabel(current.FatherName)), 30, 538, width - 60, 15, TextWhite, 2);
            DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:child-social-text"), 30, 576, width - 60, 15, TextMuted, 2);
        }
        else if (current.BreedingSupported)
        {
            DrawText(ctx, Lang.Get("feralkinshipcompanions:breeding-heading"), 30, 506, 16, accent);
            string stateText = current.PregnancyActive
                ? Lang.Get("feralkinshipcompanions:pregnancy-visible", current.PregnancyRemainingHours, (int)(current.ExpeditionStrengthFactor * 100f))
                : current.BreedingEnabled
                    ? Lang.Get("feralkinshipcompanions:breeding-opted-in")
                    : Lang.Get("feralkinshipcompanions:breeding-opted-out");
            if (!string.IsNullOrWhiteSpace(current.BreedingPartnerName))
            {
                stateText += " " + Lang.Get("feralkinshipcompanions:breeding-bonded", current.BreedingPartnerName);
            }
            // Give the family status the full panel width. The old layout
            // reserved the button's column beside this text, which made
            // localized pregnancy and bond messages truncate early.
            DrawWrapped(ctx, stateText, 30, 534, width - 60, 15, TextWhite, 3);
            DrawWrapped(ctx, current.BreedingFeedback, 30, 588, width - 60, 14, TextMuted, 2);
            DrawButton(ctx, current.BreedingEnabled
                ? Lang.Get("feralkinshipcompanions:breeding-disable-button")
                : Lang.Get("feralkinshipcompanions:breeding-enable-button"), width - 220, 628, 190, 31, true);
        }
        else
        {
            DrawText(ctx, "Social life", 30, 506, 16, accent);
            DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:breeding-unsupported"), 30, 536, width - 60, 17, TextWhite, 3);
        }
    }

    private void DrawDuties(Context ctx, int width, FoxSocialStatePacket current, double[] accent)
    {
        FoxGuiTheme.DrawPanel(ctx, 18, 124, width - 36, 72, false);
        DrawText(ctx, "Background duties", 30, 149, 16, accent);
        DrawWrapped(ctx,
            "Choose a job tab, then enable only the work you want this companion to do while At Ease.",
            30, 173, width - 60, 13, TextMuted, 2);

        string[] ids = { "overview", "ground", "harvest", "yard", "charcoal", "mowing", "storage" };
        string[] labels = { "Overview", "Ground", "Harvest", "Yard", "Charcoal", "Mowing", "Storage" };
        double gap = 7;
        double tabWidth = (width - 36 - gap * (ids.Length - 1)) / ids.Length;
        for (int index = 0; index < ids.Length; index++)
        {
            double x = 18 + index * (tabWidth + gap);
            DrawTabButton(ctx, labels[index], x, 202, tabWidth, 32,
                string.Equals(activeDutyTab, ids[index], StringComparison.Ordinal), false, accent);
        }

        FoxGuiTheme.DrawPanel(ctx, 18, 244, width - 36, 390, false);
        switch (activeDutyTab)
        {
            case "ground":
                DrawText(ctx, "Ground Cleanup", 32, 268, 18, accent);
                string groundDescription = "Choose exactly which ground clutter this companion may collect. Priority remains cattails, flint, sticks, boulders, then rocks.";
                DrawWrapped(ctx, groundDescription, 32, 294, width - 64, 14, TextMuted, 8, false);
                double groundShift = DutyTextShift(groundDescription, width);
                DrawCheckbox(ctx, "Enable Ground Cleanup", 32, 330 + groundShift, current.GroundCleanupEnabled, accent);
                DrawCheckbox(ctx, "Dropped items", 32, 378 + groundShift, current.GroundDroppedItemsEnabled, accent);
                DrawCheckbox(ctx, "Cattails", 32, 422 + groundShift, current.GroundCattailsEnabled, accent);
                DrawCheckbox(ctx, "Flint", 32, 466 + groundShift, current.GroundFlintEnabled, accent);
                DrawCheckbox(ctx, "Sticks", 430, 378 + groundShift, current.GroundSticksEnabled, accent);
                DrawCheckbox(ctx, "Boulders", 430, 422 + groundShift, current.GroundBouldersEnabled, accent);
                DrawCheckbox(ctx, "Rocks", 430, 466 + groundShift, current.GroundRocksEnabled, accent);
                break;
            case "harvest":
                DrawText(ctx, "Gather Finished Products", 32, 268, 18, accent);
                string harvestDescription = "Mature crops, ripe berries, and ready mushrooms are separate choices. Player storage remains unrestricted.";
                DrawWrapped(ctx, harvestDescription, 32, 294, width - 64, 14, TextMuted, 8, false);
                double harvestShift = DutyTextShift(harvestDescription, width);
                DrawCheckbox(ctx, "Enable Finished Product Gathering", 32, 330 + harvestShift, current.FinishedProductsEnabled, accent);
                DrawCheckbox(ctx, "Crops", 32, 386 + harvestShift, current.FinishedCropsEnabled, accent);
                DrawCheckbox(ctx, "Berries", 32, 432 + harvestShift, current.FinishedBerriesEnabled, accent);
                DrawCheckbox(ctx, "Mushrooms", 32, 478 + harvestShift, current.FinishedMushroomsEnabled, accent);
                break;
            case "yard":
                DrawText(ctx, "Yard Work", 32, 268, 18, accent);
                string yardDescription = "Flowers are removed only when selected. Snow shoveling also powers the snow escape behavior; snowballs remain optional.";
                DrawWrapped(ctx, yardDescription, 32, 294, width - 64, 14, TextMuted, 8, false);
                double yardShift = DutyTextShift(yardDescription, width);
                DrawCheckbox(ctx, "Flower removal", 32, 350 + yardShift, current.FlowerRemovalEnabled, accent);
                DrawCheckbox(ctx, "Snow shoveling", 32, 400 + yardShift, current.SnowShovelingEnabled, accent);
                DrawCheckbox(ctx, "Collect snowballs", 32, 450 + yardShift, current.SnowballCollectionEnabled, accent);
                break;
            case "mowing":
                DrawText(ctx, "Mow the Lawn", 32, 268, 18, accent);
                string mowingDescription = "Cuts only ordinary naturally-spawned tall grass. Bushes, berries, crops, flowers, and placed vegetation remain untouched.";
                DrawWrapped(ctx, mowingDescription, 32, 294, width - 64, 14, TextMuted, 8, false);
                double mowingShift = DutyTextShift(mowingDescription, width);
                DrawCheckbox(ctx, "Enable Mow the Lawn", 32, 350 + mowingShift, current.MowLawnEnabled, accent);
                break;
            case "charcoal":
                DrawText(ctx, "Charcoal Shoveling", 32, 268, 18, accent);
                string charcoalDescription = "Clears one layer at a time from completed charcoal piles and carries the charcoal to configured storage. Unfinished charcoal pits are ignored.";
                DrawWrapped(ctx, charcoalDescription, 32, 294, width - 64, 14, TextMuted, 8, false);
                double charcoalShift = DutyTextShift(charcoalDescription, width);
                DrawCheckbox(ctx, "Enable Charcoal Shoveling", 32, 350 + charcoalShift, current.CharcoalShovelingEnabled, accent);
                break;
            case "storage":
                DrawText(ctx, "Sort storage", 32, 268, 18, accent);
                string storageDescription = "Moves items already stored in the wrong configured container into the matching storage box. New pickups always use normal routing.";
                DrawWrapped(ctx, storageDescription, 32, 294, width - 64, 14, TextMuted, 8, false);
                double storageShift = DutyTextShift(storageDescription, width);
                DrawCheckbox(ctx, "Enable Sort storage", 32, 350 + storageShift, current.GeneralStorageSortingEnabled, accent);
                break;
            default:
                DrawText(ctx, "Duty overview", 32, 268, 18, accent);
                string overviewDescription = "Duties run only while this companion is At Ease. Commands, rest, combat, injury, and expeditions always take priority.";
                DrawWrapped(ctx, overviewDescription, 32, 294, width - 64, 14, TextMuted, 8, false);
                double overviewShift = DutyTextShift(overviewDescription, width);
                DrawDutySummary(ctx, "Ground Cleanup", current.GroundCleanupEnabled, 338 + overviewShift, accent);
                DrawDutySummary(ctx, "Finished Products", current.FinishedProductsEnabled, 382 + overviewShift, accent);
                DrawDutySummary(ctx, "Flower Removal", current.FlowerRemovalEnabled, 426 + overviewShift, accent);
                DrawDutySummary(ctx, "Snow Shoveling", current.SnowShovelingEnabled, 470 + overviewShift, accent);
                DrawDutySummary(ctx, "Charcoal Shoveling", current.CharcoalShovelingEnabled, 514 + overviewShift, accent);
                DrawDutySummary(ctx, "Mow the Lawn", current.MowLawnEnabled, 558 + overviewShift, accent);
                DrawDutySummary(ctx, "Sort storage", current.GeneralStorageSortingEnabled, 602 + overviewShift, accent);
                break;
        }

        FoxGuiTheme.DrawPanel(ctx, 18, 644, width - 36, 70, current.GroundCleanupEnabled || current.MowLawnEnabled
            || current.FinishedProductsEnabled || current.FlowerRemovalEnabled || current.SnowShovelingEnabled
            || current.CharcoalShovelingEnabled);
        DrawText(ctx, "Occasional idle work", 32, 670, 16, accent);
        DrawWrapped(ctx, "The companion will choose among its checked jobs according to the existing priority system.", 32, 696, width - 64, 13, TextMuted, 2);
    }

    private static void DrawDutySummary(Context ctx, string label, bool enabled, double y, double[] accent)
    {
        DrawCheckbox(ctx, label, 32, y, enabled, accent);
        DrawText(ctx, enabled ? "Enabled" : "Disabled", 420, y + 17, 13, enabled ? accent : TextMuted);
    }

    private static void DrawCheckbox(Context ctx, string label, double x, double y, bool checkedValue, double[] accent)
    {
        FoxGuiTheme.DrawChoiceSurface(ctx, x, y, 24, 24, checkedValue);
        if (checkedValue)
        {
            DrawRect(ctx, x + 5, y + 5, 14, 14, accent);
        }
        DrawText(ctx, label, x + 36, y + 18, 15, checkedValue ? TextWhite : TextMuted);
    }

    private void DrawRequestPanel(Context ctx, int width, FoxSocialStatePacket current, bool active, float requestCooldown, float cancelCooldown, double y, double height, double[] accent)
    {
        FoxGuiTheme.DrawPanel(ctx, 18, y, width - 36, height, active);
        DrawText(ctx, active ? "Current request" : "No current request", 30, y + 26, 16, accent);
        DrawWrapped(ctx, active ? current.ActiveRequest : "Check in when this companion is ready to ask something of you.", 30, y + 52, width - 60, 18, TextWhite, 2);
        if (active)
        {
            DrawButton(ctx, "Cancel request", width - 182, y + height - 52, 152, 28, cancelCooldown <= 0.01f);
        }
        DrawText(ctx, active
            ? FormatProgress(current, Math.Max(0f, (api.World.ElapsedMilliseconds - stateReceivedAtMs) / 1000f)) + $"   |   Cancel: {FormatCooldown(cancelCooldown)}"
            : $"Next check-in: {FormatCooldown(requestCooldown)}", 30, y + height - 18, 13, TextMuted);
    }

    private void DrawActionButtons(Context ctx, int width, FoxSocialStatePacket current, double[] accent)
    {
        string[] labels = current.IsJuvenile ? new[] { "Pack status", "Check in", "Close" }
            : current.HasThreatSurvey ? new[] { "Pack status", "Check in", "Talents", "Close", "Threat survey" }
            : new[] { "Pack status", "Check in", "Talents", "Close" };
        double gap = 10;
        double buttonWidth = (width - 36 - gap * (labels.Length - 1)) / labels.Length;
        for (int i = 0; i < labels.Length; i++)
            DrawButton(ctx, labels[i], 18 + i * (buttonWidth + gap), ButtonY,
                buttonWidth, ButtonHeight, true);
    }

    private static void DrawTabButton(Context ctx, string label, double x, double y, double width, double height, bool selected, bool hovered, double[] accent)
    {
        FoxGuiTheme.DrawTabSurface(ctx, x, y, width, height, selected, hovered, accent);
        if (selected || hovered)
        {
            DrawRect(ctx, x, y, 4, height, accent);
        }
        DrawText(ctx, label, x + 14, y + height / 2 + 6, 15, selected || hovered ? accent : TextWhite);
    }

    private void HandleClick(double x, double y)
    {
        if (portraitPickerOpen)
        {
            HandlePortraitPickerClick(x, y);
            return;
        }
        double helpX = FoxGuiTheme.HelpButtonX(CanvasWidth);
        if (x >= helpX && x <= helpX + 34 && y >= 8 && y <= 38)
        {
            FoxGuiTheme.PlayNavigation(api);
            if (tutorialOpen)
            {
                DismissTutorial();
                helpOpen = true;
            }
            else
            {
                helpOpen = !helpOpen;
            }
            Redraw();
            return;
        }

        if (tutorialOpen)
        {
            if (y >= CanvasHeight - 52 && y <= CanvasHeight - 22)
            {
                if (x >= CanvasWidth - 292 && x < CanvasWidth - 142)
                {
                    FoxGuiTheme.PlayNavigation(api);
                    DismissTutorial();
                    helpOpen = true;
                    Redraw();
                }
                else if (x >= CanvasWidth - 142 && x <= CanvasWidth - 22)
                {
                    FoxGuiTheme.PlayAction(api);
                    DismissTutorial();
                    Redraw();
                }
            }
            return;
        }

        if (helpOpen)
        {
            if (x >= CanvasWidth - 310
                && x <= CanvasWidth - 160
                && y >= CanvasHeight - 52
                && y <= CanvasHeight - 22)
            {
                FoxGuiTheme.PlayNavigation(api);
                helpOpen = false;
                tutorialOpen = true;
                tutorialDismissalNotified = true;
                Redraw();
            }
            else if (x >= CanvasWidth - 150
                && x <= CanvasWidth - 30
                && y >= CanvasHeight - 52
                && y <= CanvasHeight - 22)
            {
                FoxGuiTheme.PlayNavigation(api);
                helpOpen = false;
                Redraw();
            }
            return;
        }

        string clickedTab = FindHoveredTab(x, y, state?.IsJuvenile == true);
        if (!string.IsNullOrEmpty(clickedTab))
        {
            FoxGuiTheme.PlayNavigation(api);
            activeTab = clickedTab;
            Redraw();
            return;
        }

        if (state == null)
        {
            return;
        }

        if (activeTab == "overview" && x >= 30 && x <= 232 && y >= 459 && y <= 489)
        {
            portraitFiles = animalArt.ListCustomImages();
            portraitPage = 0;
            portraitMessage = string.Empty;
            portraitPickerOpen = true;
            FoxGuiTheme.PlayNavigation(api);
            Redraw();
            return;
        }

        float elapsed = Math.Max(0f, (api.World.ElapsedMilliseconds - stateReceivedAtMs) / 1000f);
        float requestCooldown = Math.Max(0f, state.RequestCooldownSeconds - elapsed);
        float cancelCooldown = Math.Max(0f, state.CancelCooldownSeconds - elapsed);
        bool active = !string.Equals(state.ActiveRequest, "None", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(activeTab, "overview", StringComparison.Ordinal)
            && x >= CanvasWidth - 170 && x <= CanvasWidth - 30
            && y >= 132 && y <= 159)
        {
            FoxGuiTheme.PlayNavigation(api);
            system.RequestCompanionNameSuggestion(targetEntityId);
            return;
        }

        if (!state.IsJuvenile
            && state.BreedingSupported
            && string.Equals(activeTab, "social", StringComparison.Ordinal)
            && x >= CanvasWidth - 220 && x <= CanvasWidth - 30
            && y >= 628 && y <= 659)
        {
            FoxGuiTheme.PlayChoice(api);
            system.SendFoxSocialAction(targetEntityId, FoxSocialRequestAction.ToggleBreeding);
            return;
        }

        double requestPanelY = string.Equals(activeTab, "social", StringComparison.Ordinal) ? 310
            : string.Equals(activeTab, "overview", StringComparison.Ordinal) ? 518
            : -1;
        double requestPanelHeight = string.Equals(activeTab, "social", StringComparison.Ordinal) ? 154
            : string.Equals(activeTab, "overview", StringComparison.Ordinal) ? 126
            : 0;
        if (active && requestPanelHeight > 0
            && x >= CanvasWidth - 182 && x <= CanvasWidth - 30
            && y >= requestPanelY + requestPanelHeight - 52
            && y <= requestPanelY + requestPanelHeight - 24)
        {
            if (cancelCooldown <= 0.01f)
            {
                FoxGuiTheme.PlayAction(api);
                system.SendFoxSocialAction(targetEntityId, FoxSocialRequestAction.Cancel);
            }
            else
            {
                FoxGuiTheme.PlayUnavailable(api);
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(state.CarriedItem)
            && x >= CanvasWidth - 184 && x <= CanvasWidth - 30
            && y >= 166 && y <= 193
            && string.Equals(activeTab, "overview", StringComparison.Ordinal))
        {
            FoxGuiTheme.PlayAction(api);
            system.SendFoxSocialAction(targetEntityId, FoxSocialRequestAction.RetrieveHeldItem);
            return;
        }

        if (string.Equals(activeTab, "commands", StringComparison.Ordinal)
            && y >= 158 && y <= 192)
        {
            if (x >= 18 && x < 208) SendChoice(FoxSocialRequestAction.SetActivity, CompanionActivityMode.Follow);
            else if (x >= 218 && x < 408) SendChoice(FoxSocialRequestAction.SetActivity, CompanionActivityMode.AtEase);
            else if (x >= 418 && x < 608) SendChoice(FoxSocialRequestAction.SetActivity, CompanionActivityMode.Rest);
            else if (x >= 618 && x < 808) SendChoice(FoxSocialRequestAction.SetActivity, CompanionActivityMode.ReturnHome);
            return;
        }

        if (!state.IsJuvenile && string.Equals(activeTab, "duties", StringComparison.Ordinal))
        {
            string[] dutyTabs = { "overview", "ground", "harvest", "yard", "charcoal", "mowing", "storage" };
            double dutyGap = 7;
            double dutyTabWidth = (CanvasWidth - 36 - dutyGap * (dutyTabs.Length - 1)) / dutyTabs.Length;
            if (y >= 198 && y <= 238)
            {
                int tabIndex = (int)((x - 18) / (dutyTabWidth + dutyGap));
                if (tabIndex >= 0 && tabIndex < dutyTabs.Length)
                {
                    double tabStart = 18 + tabIndex * (dutyTabWidth + dutyGap);
                    if (x >= tabStart && x <= tabStart + dutyTabWidth)
                    {
                        activeDutyTab = dutyTabs[tabIndex];
                        FoxGuiTheme.PlayNavigation(api);
                        Redraw();
                        return;
                    }
                }
            }

            if (activeDutyTab == "ground")
            {
                double shift = DutyTextShift("Choose exactly which ground clutter this companion may collect. Priority remains cattails, flint, sticks, boulders, then rocks.", CanvasWidth);
                if (TryHandleGroundDutyClick(x, y, shift, state)) return;
                return;
            }
            if (activeDutyTab == "harvest")
            {
                double shift = DutyTextShift("Mature crops, ripe berries, and ready mushrooms are separate choices. Player storage remains unrestricted.", CanvasWidth);
                if (TryHandleDutyRowClick(x, y, shift, 320, 376,
                        () => ToggleDuty(FoxSocialRequestAction.SetFinishedProducts, "on", "off", state.FinishedProductsEnabled))
                    || TryHandleDutyRowClick(x, y, shift, 376, 422,
                        () => ToggleSubtask(CompanionDuty.FinishedCrops, state.FinishedCropsEnabled))
                    || TryHandleDutyRowClick(x, y, shift, 422, 468,
                        () => ToggleSubtask(CompanionDuty.FinishedBerries, state.FinishedBerriesEnabled))
                    || TryHandleDutyRowClick(x, y, shift, 468, 514,
                        () => ToggleSubtask(CompanionDuty.FinishedMushrooms, state.FinishedMushroomsEnabled))) return;
                return;
            }
            if (activeDutyTab == "yard")
            {
                double shift = DutyTextShift("Flowers are removed only when selected. Snow shoveling also powers the snow escape behavior; snowballs remain optional.", CanvasWidth);
                if (TryHandleDutyRowClick(x, y, shift, 334, 390,
                        () => ToggleDuty(FoxSocialRequestAction.SetFlowerRemoval, "on", "off", state.FlowerRemovalEnabled))
                    || TryHandleDutyRowClick(x, y, shift, 390, 440,
                        () => ToggleDuty(FoxSocialRequestAction.SetSnowShoveling, "on", "off", state.SnowShovelingEnabled))
                    || TryHandleDutyRowClick(x, y, shift, 440, 490,
                        () => ToggleDuty(FoxSocialRequestAction.SetSnowballCollection, "on", "off", state.SnowballCollectionEnabled))) return;
                return;
            }
            if (activeDutyTab == "charcoal")
            {
                double shift = DutyTextShift("Clears one layer at a time from completed charcoal piles and carries the charcoal to configured storage. Unfinished charcoal pits are ignored.", CanvasWidth);
                if (TryHandleDutyRowClick(x, y, shift, 334, 390,
                    () => ToggleDuty(FoxSocialRequestAction.SetCharcoalShoveling, "on", "off", state.CharcoalShovelingEnabled))) return;
                return;
            }
            double mowingShift = DutyTextShift("Cuts only ordinary naturally-spawned tall grass. Bushes, berries, crops, flowers, and placed vegetation remain untouched.", CanvasWidth);
            if (activeDutyTab == "mowing"
                && TryHandleDutyRowClick(x, y, mowingShift, 334, 390,
                    () => ToggleDuty(FoxSocialRequestAction.SetMowLawn, "on", "off", state.MowLawnEnabled)))
            {
                return;
            }
            if (activeDutyTab == "storage")
            {
                double shift = DutyTextShift("Moves items already stored in the wrong configured container into the matching storage box. New pickups always use normal routing.", CanvasWidth);
                if (TryHandleDutyRowClick(x, y, shift, 334, 390,
                    () => ToggleDuty(FoxSocialRequestAction.SetGeneralStorageSorting, "on", "off", state.GeneralStorageSortingEnabled)))
                {
                    return;
                }
            }
        }

        if (string.Equals(activeTab, "commands", StringComparison.Ordinal)
            && Is(state.ActivityMode, CompanionActivityMode.Follow) && y >= 222 && y <= 254)
        {
            if (x >= 170 && x < 370) SendChoice(FoxSocialRequestAction.SetFollowDistance, CompanionFollowDistance.Close);
            else if (x >= 380 && x < 580) SendChoice(FoxSocialRequestAction.SetFollowDistance, CompanionFollowDistance.Normal);
            else if (x >= 590 && x < 808) SendChoice(FoxSocialRequestAction.SetFollowDistance, CompanionFollowDistance.Back);
            return;
        }

        if (!state.IsJuvenile && string.Equals(activeTab, "commands", StringComparison.Ordinal)
            && y >= 328 && y <= 360)
        {
            if (x >= 18 && x < 278) SendChoice(FoxSocialRequestAction.SetCombatStyle, CompanionCombatStyle.Passive);
            else if (x >= 292 && x < 552) SendChoice(FoxSocialRequestAction.SetCombatStyle, CompanionCombatStyle.Defensive);
            else if (x >= 566 && x < 808) SendChoice(FoxSocialRequestAction.SetCombatStyle, CompanionCombatStyle.Protect);
            return;
        }
        if (!state.IsJuvenile && string.Equals(activeTab, "commands", StringComparison.Ordinal)
            && y >= 370 && y <= 402)
        {
            if (x >= 18 && x < 278) SendChoice(FoxSocialRequestAction.SetCombatStyle, CompanionCombatStyle.Assist);
            else if (x >= 292 && x < 552) SendChoice(FoxSocialRequestAction.SetCombatStyle, CompanionCombatStyle.Aggressive);
            else if (x >= 566 && x < 808) SendChoice(FoxSocialRequestAction.SetCombatStyle, CompanionCombatStyle.Flee);
            return;
        }

        if (!state.IsJuvenile && string.Equals(activeTab, "commands", StringComparison.Ordinal)
            && y >= 462 && y <= 494)
        {
            if (x >= 170 && x < 370) SendChoice(FoxSocialRequestAction.SetRiskTolerance, CompanionRiskTolerance.Cautious);
            else if (x >= 380 && x < 580) SendChoice(FoxSocialRequestAction.SetRiskTolerance, CompanionRiskTolerance.Steady);
            else if (x >= 590 && x < 808) SendChoice(FoxSocialRequestAction.SetRiskTolerance, CompanionRiskTolerance.Fearless);
            return;
        }

        if (y >= ButtonY && y <= ButtonY + ButtonHeight)
        {
            string[] actions = state.IsJuvenile ? new[] { "pack", "check", "close" }
                : state.HasThreatSurvey ? new[] { "pack", "check", "talents", "close", "survey" }
                : new[] { "pack", "check", "talents", "close" };
            double gap = 10;
            double buttonWidth = (CanvasWidth - 36 - gap * (actions.Length - 1)) / actions.Length;
            int index = (int)((x - 18) / (buttonWidth + gap));
            if (x < 18 || index < 0 || index >= actions.Length
                || x >= 18 + index * (buttonWidth + gap) + buttonWidth) return;
            switch (actions[index])
            {
                case "pack":
                    FoxGuiTheme.PlayNavigation(api);
                    system.TryOpenFoxPackGui(targetEntityId);
                    break;
                case "check":
                    if (!active && requestCooldown <= 0.01f)
                    {
                        FoxGuiTheme.PlayAction(api);
                        system.SendFoxSocialAction(targetEntityId, FoxSocialRequestAction.Generate);
                    }
                    else FoxGuiTheme.PlayUnavailable(api);
                    break;
                case "talents":
                    FoxGuiTheme.PlayNavigation(api);
                    system.TryOpenFoxPerksGui(targetEntityId);
                    break;
                case "close":
                    FoxGuiTheme.PlayNavigation(api);
                    system.CloseFoxSocialGui();
                    break;
                case "survey":
                    FoxGuiTheme.PlayAction(api);
                    system.SendFoxSocialAction(targetEntityId, FoxSocialRequestAction.ThreatSurvey);
                    break;
            }
            return;
        }
    }

    private void SendChoice(int action, string value)
    {
        FoxGuiTheme.PlayChoice(api);
        system.SendFoxSocialAction(targetEntityId, action, value);
    }

    private void ToggleDuty(int action, string onValue, string offValue, bool currentValue)
    {
        SendChoice(action, currentValue ? offValue : onValue);
    }

    private void ToggleSubtask(string option, bool currentValue)
    {
        SendChoice(FoxSocialRequestAction.SetDutySubtask, $"{option}:{(currentValue ? "off" : "on")}");
    }

    private bool TryHandleDutyRowClick(
        double x,
        double y,
        double shift,
        double rowTop,
        double rowBottom,
        Action toggle)
    {
        // The checkbox is the visual cue, but the whole row is interactive.
        // Using a generous row hitbox keeps duty controls usable when the
        // description reflows at a different font size or resolution.
        if (x < 18 || x > CanvasWidth - 18) return false;
        double adjustedY = y - shift;
        if (adjustedY < rowTop || adjustedY >= rowBottom) return false;
        toggle();
        return true;
    }

    private bool TryHandleGroundDutyClick(
        double x,
        double y,
        double shift,
        FoxSocialStatePacket current)
    {
        // Use the complete visible row, not only the 24px checkbox square.
        // This also leaves a small tolerance for GUI scaling and font-driven
        // description reflow while keeping the two columns distinct.
        double adjustedY = y - shift;
        if (adjustedY >= 320 && adjustedY < 366)
        {
            ToggleDuty(FoxSocialRequestAction.SetGroundCleanup, "on", "off", current.GroundCleanupEnabled);
            return true;
        }

        bool leftColumn = x >= 18 && x < 420;
        bool rightColumn = x >= 420 && x <= CanvasWidth - 18;
        if (!leftColumn && !rightColumn) return false;

        if (adjustedY >= 366 && adjustedY < 410)
        {
            if (leftColumn) ToggleSubtask(CompanionDuty.GroundDroppedItems, current.GroundDroppedItemsEnabled);
            else ToggleSubtask(CompanionDuty.GroundSticks, current.GroundSticksEnabled);
            return true;
        }
        if (adjustedY >= 410 && adjustedY < 454)
        {
            if (leftColumn) ToggleSubtask(CompanionDuty.GroundCattails, current.GroundCattailsEnabled);
            else ToggleSubtask(CompanionDuty.GroundBoulders, current.GroundBouldersEnabled);
            return true;
        }
        if (adjustedY >= 454 && adjustedY < 502)
        {
            if (leftColumn) ToggleSubtask(CompanionDuty.GroundFlint, current.GroundFlintEnabled);
            else ToggleSubtask(CompanionDuty.GroundRocks, current.GroundRocksEnabled);
            return true;
        }

        return false;
    }

    private static string ParentLabel(string name) => string.IsNullOrWhiteSpace(name)
        ? Lang.Get("feralkinshipcompanions:child-parent-unknown")
        : name.Trim();

    private static string FindHoveredCommand(double x, double y)
    {
        if (y >= 158 && y <= 192)
        {
            if (x >= 18 && x < 208) return "activity:" + CompanionActivityMode.Follow;
            if (x >= 218 && x < 408) return "activity:" + CompanionActivityMode.AtEase;
            if (x >= 418 && x < 608) return "activity:" + CompanionActivityMode.Rest;
            if (x >= 618 && x < 808) return "activity:" + CompanionActivityMode.ReturnHome;
        }
        else if (y >= 328 && y <= 360)
        {
            if (x >= 18 && x < 278) return "combat:" + CompanionCombatStyle.Passive;
            if (x >= 292 && x < 552) return "combat:" + CompanionCombatStyle.Defensive;
            if (x >= 566 && x < 808) return "combat:" + CompanionCombatStyle.Protect;
        }
        else if (y >= 370 && y <= 402)
        {
            if (x >= 18 && x < 278) return "combat:" + CompanionCombatStyle.Assist;
            if (x >= 292 && x < 552) return "combat:" + CompanionCombatStyle.Aggressive;
            if (x >= 566 && x < 808) return "combat:" + CompanionCombatStyle.Flee;
        }
        else if (y >= 462 && y <= 494)
        {
            if (x >= 170 && x < 370) return "risk:" + CompanionRiskTolerance.Cautious;
            if (x >= 380 && x < 580) return "risk:" + CompanionRiskTolerance.Steady;
            if (x >= 590 && x < 808) return "risk:" + CompanionRiskTolerance.Fearless;
        }

        return string.Empty;
    }

    private string FindHoveredTab(double x, double y, bool juvenile)
    {
        if (y < TabY || y > TabY + TabHeight || x < 18)
        {
            return string.Empty;
        }

        double gap = 8;
        string[] ids = ProfileTabs(juvenile);
        int tabCount = ids.Length;
        double tabWidth = (CanvasWidth - 36 - gap * (tabCount - 1)) / tabCount;
        int index = (int)((x - 18) / (tabWidth + gap));
        if (index < 0 || index >= tabCount)
        {
            return string.Empty;
        }

        double tabStart = 18 + index * (tabWidth + gap);
        if (x > tabStart + tabWidth)
        {
            return string.Empty;
        }

        return ids[index];
    }

    private static double[] GetTabAccent(string tab)
    {
        return tab switch
        {
            "commands" => TextCommandsAccent,
            "duties" => TextCommandsAccent,
            "social" => TextSocialAccent,
            "talents" => TextTalentsAccent,
            _ => TextOverviewAccent
        };
    }

    private void DrawHelpOverlay(Context ctx, int width, int height)
    {
        FoxGuiTheme.DrawOverlay(ctx, width, height, TextButtonDisabled);
        DrawText(ctx, Lang.Get("feralkinshipcompanions:help-companion-title"), 30, 45, 23, TextWhite);
        DrawText(ctx, Lang.Get("feralkinshipcompanions:help-companion-start-heading"), 30, 80, 16, TextGold);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-companion-start-text"), 30, 103, width - 60, 17, TextWhite, 3);

        DrawText(ctx, Lang.Get("feralkinshipcompanions:help-companion-requests-heading"), 30, 160, 16, TextGold);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-companion-requests-text"), 30, 183, width - 60, 17, TextMuted, 5);

        DrawText(ctx, Lang.Get("feralkinshipcompanions:help-companion-activity-heading"), 30, 275, 16, TextGold);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-companion-activity-text"), 30, 298, width - 60, 17, TextMuted, 5);

        DrawText(ctx, Lang.Get("feralkinshipcompanions:help-companion-combat-heading"), 30, 390, 16, TextGold);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-companion-combat-text"), 30, 413, width - 60, 17, TextMuted, 4);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-companion-wounded-text"), 30, 482, width - 60, 17, TextMuted, 3);

        DrawText(ctx, Lang.Get("feralkinshipcompanions:help-companion-pack-heading"), 30, 545, 16, TextGold);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-companion-pack-text"), 30, 568, width - 60, 17, TextMuted, 4);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-companion-personality-text"), 30, 637, width - 60, 17, TextMuted, 2);
        DrawButton(ctx, Lang.Get("feralkinshipcompanions:tutorial-bram-notes"), width - 310, height - 52, 150, 30, true);
        DrawButton(ctx, Lang.Get("feralkinshipcompanions:help-close"), width - 150, height - 52, 120, 30, true);
    }

    private void DrawTutorialOverlay(Context ctx, int width, int height)
    {
        FoxGuiTheme.DrawOverlay(ctx, width, height, TextButtonDisabled);
        DrawText(ctx, Lang.Get("feralkinshipcompanions:tutorial-packkeeper-title"), 30, 45, 23, TextWhite);
        DrawText(ctx, Lang.Get("feralkinshipcompanions:tutorial-packkeeper-byline"), 30, 73, 15, TextGold);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:tutorial-packkeeper-intro"), 30, 105, width - 60, 17, TextWhite, 3);

        DrawText(ctx, Lang.Get("feralkinshipcompanions:tutorial-start-heading"), 30, 167, 16, TextGold);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:tutorial-start-text"), 30, 190, width - 60, 17, TextMuted, 3);

        DrawText(ctx, Lang.Get("feralkinshipcompanions:tutorial-activity-heading"), 30, 255, 16, TextGold);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:tutorial-activity-text"), 30, 278, width - 60, 17, TextMuted, 3);

        DrawText(ctx, Lang.Get("feralkinshipcompanions:tutorial-combat-heading"), 30, 343, 16, TextGold);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:tutorial-combat-text"), 30, 366, width - 60, 17, TextMuted, 4);

        DrawText(ctx, Lang.Get("feralkinshipcompanions:tutorial-progress-heading"), 30, 431, 16, TextGold);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:tutorial-progress-text"), 30, 454, width - 60, 17, TextMuted, 3);

        FoxGuiTheme.DrawPanel(ctx, 30, 530, width - 60, 88, true);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:tutorial-packkeeper-last-word"), 44, 556, width - 88, 17, TextWhite, 3);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:tutorial-packkeeper-reminder"), 44, 598, width - 88, 17, TextGold, 2);

        DrawButton(ctx, Lang.Get("feralkinshipcompanions:tutorial-open-help"), width - 292, height - 52, 150, 30, true);
        DrawButton(ctx, Lang.Get("feralkinshipcompanions:tutorial-continue"), width - 142, height - 52, 120, 30, true);
    }

    private void DismissTutorial()
    {
        if (!tutorialOpen)
        {
            return;
        }

        tutorialOpen = false;
        if (!tutorialDismissalNotified)
        {
            tutorialDismissalNotified = true;
            system.MarkCompanionTutorialSeen();
            api.ShowChatMessage(Lang.Get("feralkinshipcompanions:tutorial-packkeeper-reminder"));
        }
    }

    private static string GetActivitySummary(string activityMode)
    {
        return activityMode switch
        {
            CompanionActivityMode.Follow => "Follow keeps this companion near you and uses the selected follow range.",
            CompanionActivityMode.AtEase => "At Ease allows wandering, socializing, play, and ordinary rest.",
            CompanionActivityMode.Rest => "Rest sends this companion to its assigned den without an immediate teleport.",
            CompanionActivityMode.ReturnHome => "Return Home knows the way home regardless of distance and safely teleports only as a fallback.",
            _ => "Choose Follow to set how closely this companion travels."
        };
    }

    private static string GetRiskToleranceSummary(string riskTolerance)
    {
        return riskTolerance switch
        {
            CompanionRiskTolerance.Cautious => "Retreats home below 40% health.",
            CompanionRiskTolerance.Steady => "Retreats home below 20% health.",
            CompanionRiskTolerance.Fearless => "Never retreats automatically.",
            _ => "Choose when this companion retreats."
        };
    }

    private static string GetCombatStyleSummary(string combatStyle)
    {
        return combatStyle switch
        {
            CompanionCombatStyle.Passive => "Passive: never fights, even when threatened.",
            CompanionCombatStyle.Defensive => "Defensive: fights only after this companion is attacked.",
            CompanionCombatStyle.Protect => "Protect: defends you and nearby owned companions.",
            CompanionCombatStyle.Assist => "Assist: joins fights you begin.",
            CompanionCombatStyle.Aggressive => "Aggressive: seeks known hostile creatures within its permitted combat range.",
            CompanionCombatStyle.Flee => "Flee: avoids nearby creatures that could attack it.",
            _ => "Choose when this companion should enter combat."
        };
    }

    private static string FormatCooldown(float seconds)
    {
        if (seconds <= 0.01f) return "Ready";
        int total = Math.Max(1, (int)Math.Ceiling(seconds));
        return $"{total / 60}:{total % 60:00}";
    }

    private static string FormatProgress(FoxSocialStatePacket current, float elapsed)
    {
        if (current.DurationSeconds <= 0f)
        {
            return "Progress: completes when the condition is met";
        }

        float progress = Math.Clamp(current.ProgressSeconds + elapsed, 0f, current.DurationSeconds);
        return $"Progress: {progress:0} / {current.DurationSeconds:0} seconds";
    }

    private static int DrawWrapped(Context ctx, string text, double x, double baseline, double maxWidth, double lineHeight, double[] color, int maxLines, bool appendEllipsis = true)
    {
        string[] words = (text ?? string.Empty)
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        System.Collections.Generic.List<string> lines = new();
        string line = string.Empty;
        foreach (string word in words)
        {
            string next = string.IsNullOrEmpty(line) ? word : line + " " + word;
            if (FeralKinshipCompanionUiSettings.GetTextWidth(next, TextSize) > maxWidth
                && !string.IsNullOrEmpty(line))
            {
                lines.Add(line);
                line = word;
            }
            else line = next;
        }
        if (!string.IsNullOrEmpty(line))
        {
            lines.Add(line);
        }
        for (int index = 0; index < Math.Min(maxLines, lines.Count); index++)
        {
            string rendered = lines[index];
            if (appendEllipsis && index == maxLines - 1 && lines.Count > maxLines)
            {
                rendered = FeralKinshipCompanionUiSettings.TrimTextToWidth(rendered + "…", maxWidth, TextSize);
            }
            DrawText(ctx, rendered, x, baseline + index * lineHeight * FeralKinshipCompanionUiSettings.TextScale, TextSize, color);
        }
        return Math.Min(maxLines, lines.Count);
    }

    private static double DutyTextShift(string text, double width)
    {
        string[] words = (text ?? string.Empty)
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string line = string.Empty;
        int lineCount = 0;
        foreach (string word in words)
        {
            string next = string.IsNullOrEmpty(line) ? word : line + " " + word;
            if (FeralKinshipCompanionUiSettings.GetTextWidth(next, 14) > width - 64
                && !string.IsNullOrEmpty(line))
            {
                lineCount++;
                line = word;
            }
            else
            {
                line = next;
            }
        }
        if (!string.IsNullOrEmpty(line))
        {
            lineCount++;
        }
        return Math.Max(0, lineCount - 2) * 14 * FeralKinshipCompanionUiSettings.TextScale;
    }

    private static void DrawButton(Context ctx, string label, double x, double y, double width, double height, bool enabled)
    {
        FoxGuiTheme.DrawButtonSurface(ctx, x, y, width, height, enabled);
        DrawText(ctx, label, x + 12, y + height / 2 + 6, 15, enabled ? TextWhite : TextMuted);
    }

    private static void DrawChoiceButton(Context ctx, string label, double x, double y, double width, double height, bool selected, double[] accent = null)
    {
        FoxGuiTheme.DrawChoiceSurface(ctx, x, y, width, height, selected);
        if (selected)
        {
            DrawRect(ctx, x, y, 4, height, accent ?? TextGold);
        }
        DrawText(ctx, label, x + 12, y + height / 2 + 6, 15, selected ? accent ?? TextGold : TextWhite);
    }

    private static bool Is(string value, string expected) => string.Equals(value, expected, StringComparison.Ordinal);

    private static void DrawFoodBar(
        Context ctx,
        FoxSocialStatePacket current,
        double x,
        double y,
        double width,
        double height)
    {
        float level = Math.Clamp(current.FoodLevel, 0f, 1f);
        string label = string.IsNullOrWhiteSpace(current.FoodState)
            ? FeralKinshipCompanionSystem.GetFoodStateLabel(level, current.FoodSystemEnabled)
            : current.FoodState;
        bool starving = current.FoodSystemEnabled
            && (level <= 0.001f || label.StartsWith("Starving", StringComparison.OrdinalIgnoreCase));
        DrawText(ctx, $"Food: {label}", x, y - 6, 13,
            current.FoodSystemEnabled ? TextWhite : TextMuted);
        DrawRect(ctx, x, y, width, height, new[] { 0.10d, 0.075d, 0.055d, 0.85d });
        double[] fill = !current.FoodSystemEnabled
            ? new[] { 0.42d, 0.42d, 0.42d, 0.90d }
            : starving
                ? new[] { 0.66d, 0.16d, 0.12d, 0.95d }
                : level < 0.25f
                    ? new[] { 0.82d, 0.38d, 0.10d, 0.95d }
                    : level < 0.65f
                        ? new[] { 0.78d, 0.64d, 0.16d, 0.95d }
                        : new[] { 0.30d, 0.62d, 0.30d, 0.95d };
        double displayedLevel = !current.FoodSystemEnabled ? 1d : starving ? 0d : level;
        DrawRect(ctx, x + 2, y + 2, Math.Max(0d, (width - 4) * displayedLevel), height - 4, fill);
    }

    private static void DrawExperienceBar(
        Context ctx,
        FoxSocialStatePacket current,
        double x,
        double y,
        double width,
        double height)
    {
        DrawRect(ctx, x, y, width, height, new[] { 0.10d, 0.075d, 0.055d, 0.85d });
        double progress = current.IsJuvenile || current.RequiredLevelExperience <= 0L
            ? 0d
            : Math.Clamp((double)current.CurrentLevelExperience / current.RequiredLevelExperience, 0d, 1d);
        DrawRect(ctx, x, y, width * progress, height, TextGold);
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

    private static string Trim(string value, int maxLength)
    {
        value ??= string.Empty;
        return value.Length <= maxLength ? value : value.Substring(0, Math.Max(0, maxLength - 1)) + "…";
    }
}
