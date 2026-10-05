#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace FeralKinshipCompanions;

public sealed class GuiDialogFeralKinshipFoxPerks : GuiDialog
{
    private const double DialogWidth = 1280;
    private const double DialogHeight = 940;
    private const double Padding = 0;

    private readonly FeralKinshipCompanionSystem system;
    private readonly long targetEntityId;
    private GuiElementFeralKinshipFoxPerksSurface surface;
    private FoxPerkStatePacket state;

    public GuiDialogFeralKinshipFoxPerks(ICoreClientAPI capi, FeralKinshipCompanionSystem system, long targetEntityId)
        : base(capi)
    {
        this.system = system;
        this.targetEntityId = targetEntityId;
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
        surface?.NotifyGuideClosed();
        system.SendFoxSocialAction(targetEntityId, FoxSocialRequestAction.ClosePerks);
        base.OnGuiClosed();
    }

    public void ApplyState(FoxPerkStatePacket packet)
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
            .CreateCompo("feralkinship-fox-perks", dialogBounds)
            .AddShadedDialogBG(contentBounds)
            .AddDialogTitleBar("Feral Kinship Companion - Talents", () => TryClose())
            .BeginChildElements(contentBounds);

        surface = new GuiElementFeralKinshipFoxPerksSurface(
            capi,
            ElementBounds.Fixed(0, 0, DialogWidth, DialogHeight),
            system,
            targetEntityId
        );
        composer.AddInteractiveElement(surface, "fox-perks-surface");
        SingleComposer = composer.Compose();
    }
}

internal sealed class GuiElementFeralKinshipFoxPerksSurface : GuiElementFeralKinshipScaledSurface
{
    protected override double DesignWidth => 1280;
    protected override double DesignHeight => 940;
    private const double TabY = 96;
    private const double TabHeight = 36;
    private const double NodeTop = 260;
    private const double NodeViewportTop = 242;
    private const double NodeViewportHeight = 618;
    private const double NodeWidth = 205;
    private const double NodeHeight = 140;
    private const double NodeSpacingX = 217;
    private const double NodeSpacingY = 148;
    private static double[] TextWhite => FoxGuiTheme.Text;
    private static double[] TextMuted => FoxGuiTheme.Muted;
    private static double[] TextGold => FoxGuiTheme.Accent;
    private static double[] TextCombatAccent => FoxGuiTheme.CombatAccent;
    private static double[] TextSurvivalAccent => FoxGuiTheme.SurvivalAccent;
    private static double[] TextSocialAccent => FoxGuiTheme.SocialAccent;
    private static double[] TextReturnAccent => FoxGuiTheme.ReturnAccent;
    private static double[] TextPanel => FoxGuiTheme.PanelColor;
    private static double[] TextCard => FoxGuiTheme.PanelColor;
    private static double[] TextAvailable => FoxGuiTheme.SelectedPanelColor;
    private static double[] TextLocked => FoxGuiTheme.LockedPanel;
    private static double[] TooltipShadow => FoxGuiTheme.TooltipShadow;
    private static double[] TooltipBackground => FoxGuiTheme.TooltipBackground;
    private static double[] TextImplemented => FoxGuiTheme.ImplementedPanel;
    private static double[] TextNyi => FoxGuiTheme.FuturePanel;
    private static readonly double[] IconImplementedOverlay = { 0.12, 0.75, 0.20, 0.28 };
    private static readonly double[] IconTestingOverlay = { 0.95, 0.62, 0.12, 0.28 };
    private static readonly double[] IconNyiOverlay = { 0.90, 0.08, 0.05, 0.45 };

    private readonly FeralKinshipCompanionSystem system;
    private readonly long targetEntityId;
    private FoxPerkStatePacket state;
    private string currentTree = FoxPerkTreeId.Combat;
    private string hoveredTree = string.Empty;
    private string hoveredPerkId = string.Empty;
    private string selectedPerkId = string.Empty;
    private double nodeScrollOffset;
    private bool guideOpen;
    private bool guideDismissalNotified;
    private bool helpOpen;
    private bool helpHovered;
    private int textureId;

    public GuiElementFeralKinshipFoxPerksSurface(
        ICoreClientAPI capi,
        ElementBounds bounds,
        FeralKinshipCompanionSystem system,
        long targetEntityId)
        : base(capi, bounds)
    {
        this.system = system;
        this.targetEntityId = targetEntityId;
        guideOpen = system.ShouldShowCompanionTalentGuide();
    }

    public void NotifyGuideClosed()
    {
        if (guideOpen)
        {
            DismissGuide();
        }
    }

    public void ApplyState(FoxPerkStatePacket packet)
    {
        state = packet;
        ClampNodeScroll();
        Redraw();
    }

    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        if (guideOpen || helpOpen || state == null) return;
        double x = CanvasMouseX(api.Input.MouseX);
        double y = CanvasMouseY(api.Input.MouseY);
        if (x < 270 || x > 950 || y < NodeViewportTop || y > NodeViewportTop + NodeViewportHeight) return;
        double amount = args.deltaPrecise != 0f ? args.deltaPrecise : args.delta;
        if (Math.Abs(amount) < .01d) return;
        nodeScrollOffset = Math.Max(0, nodeScrollOffset - amount * 52);
        ClampNodeScroll();
        hoveredPerkId = string.Empty;
        Redraw();
        args.SetHandled();
    }

    private List<(FoxPerkDefinition Definition, double X, double Y)> GetPerkCards()
    {
        var cards = new List<(FoxPerkDefinition, double, double)>();
        double cursor = NodeTop;
        foreach (IGrouping<int, FoxPerkDefinition> tier in FoxPerkCatalog
            .ForTree(currentTree, state?.SpeciesId ?? string.Empty)
            .OrderBy(definition => definition.Row).ThenBy(definition => definition.Column)
            .GroupBy(definition => definition.Row))
        {
            double cardTop = cursor + 25;
            int index = 0;
            foreach (FoxPerkDefinition definition in tier)
            {
                cards.Add((definition, 284 + index % 3 * NodeSpacingX,
                    cardTop + index / 3 * NodeSpacingY));
                index++;
            }
            cursor = cardTop + Math.Max(1, (index + 2) / 3) * NodeSpacingY + 12;
        }
        return cards;
    }

    private double NodeContentHeight()
    {
        List<(FoxPerkDefinition Definition, double X, double Y)> cards = GetPerkCards();
        return cards.Count == 0 ? 0 : cards.Max(card => card.Y + NodeHeight) - NodeViewportTop + 14;
    }

    private void ClampNodeScroll() => nodeScrollOffset = Math.Min(nodeScrollOffset,
        Math.Max(0, NodeContentHeight() - NodeViewportHeight));

    public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
    {
        Bounds.CalcWorldBounds();
        Redraw();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        bool hovered = FoxGuiTheme.IsHelpHovered(
            CanvasMouseX(api.Input.MouseX),
            CanvasMouseY(api.Input.MouseY),
            CanvasWidth);
        if (hovered != helpHovered)
        {
            helpHovered = hovered;
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

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        string tree = FindHoveredTree(CanvasMouseX(args.X), CanvasMouseY(args.Y));
        string hovered = FindHoveredPerk(CanvasMouseX(args.X), CanvasMouseY(args.Y))?.Id ?? string.Empty;
        if (string.Equals(hovered, hoveredPerkId, StringComparison.Ordinal)
            && string.Equals(tree, hoveredTree, StringComparison.Ordinal))
        {
            return;
        }

        hoveredTree = tree;
        hoveredPerkId = hovered;
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
        FoxGuiTheme.DrawJournal(ctx, width, height, FoxGuiSurfaceKind.Talents);
        if (guideOpen)
        {
            DrawGuideOverlay(ctx, width, height);
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

        FoxPerkStatePacket current = state;
        if (current == null)
        {
            FoxGuiTheme.DrawPanel(ctx, 18, 18, width - 36, 72, false);
            DrawText(ctx, "Waiting for talent data...", 32, 51, 20, TextWhite);
            DrawWrapped(ctx,
                "The companion may be outside interaction range, or still answering. Move closer and give it a moment.",
                32,
                76,
                width - 64,
                15,
                TextMuted,
                2,
                13);
            FoxGuiTheme.DrawWindowControls(ctx, api, width);
            FoxGuiTheme.DrawHelpGlyph(ctx, width, helpHovered);
            return;
        }

        string displayName = string.IsNullOrWhiteSpace(current.Name) ? "Unnamed companion" : current.Name;
        DrawText(ctx, $"{Trim(displayName, 30)}'s talents", 18, 32, 25, TextWhite);
        DrawText(ctx, $"{Trim(current.SpeciesDisplayName, 24)}  |  Personality: {Trim(current.Personality, 22)}  |  Mood: {Trim(current.Mood, 20)}",
            18, 60, 15, TextMuted);
        DrawText(ctx, $"{CompanionActivityMode.DisplayName(current.ActivityMode)} · {CompanionCombatStyle.DisplayName(current.CombatStyle)}  |  Level {current.Level} · {current.CurrentLevelExperience}/{current.RequiredLevelExperience} EXP",
            18, 85, 14, TextGold);
        DrawButton(ctx, "Back to overview", width - 192, 92, 170, 40, true, TextReturnAccent);
        FoxGuiTheme.DrawSectionRule(ctx, 18, 143, width - 36);

        DrawRect(ctx, 18, 158, 240, 714, TextPanel);
        DrawRect(ctx, 270, 158, 680, 714, TextPanel);
        DrawRect(ctx, 962, 158, 300, 714, TextPanel);
        DrawText(ctx, "Talent categories", 30, 190, 18, TextGold);
        string[] treeIds = { FoxPerkTreeId.Combat, FoxPerkTreeId.Survival, FoxPerkTreeId.Social };
        string[] treeNames = { "Combat", "Survival", "Social" };
        int[] investments = { current.CombatInvestment, current.MobilityInvestment, current.SocialInvestment };
        for (int i = 0; i < treeIds.Length; i++)
        {
            int maximum = FoxPerkCatalog.ForTree(treeIds[i], current.SpeciesId).Sum(definition => definition.MaxRank);
            DrawChoiceButton(ctx, $"{treeNames[i]}  {investments[i]}/{maximum}",
                30, 211 + i * 68, 216, 59, currentTree == treeIds[i],
                hoveredTree == treeIds[i], GetTreeAccent(treeIds[i]));
        }
        FoxGuiTheme.DrawSectionRule(ctx, 30, 423, 216);
        DrawText(ctx, "Talent summary", 30, 453, 18, TextGold);
        DrawText(ctx, $"Available points: {current.AvailablePoints}", 30, 487, 15, TextWhite);
        DrawText(ctx, $"Spent points: {current.SpentPoints}", 30, 512, 15, TextWhite);
        DrawText(ctx, $"Combat: {current.CombatInvestment}", 30, 549, 13, TextMuted);
        DrawText(ctx, $"Survival: {current.MobilityInvestment}", 30, 572, 13, TextMuted);
        DrawText(ctx, $"Social: {current.SocialInvestment}", 30, 595, 13, TextMuted);
        DrawWrapped(ctx, "Talents make this companion stronger and unlock new abilities.",
            30, 640, 212, 19, TextMuted, 4, 13);
        DrawButton(ctx, "Respec", 30, 740, 216, 40, current.SpentPoints > 0, TextGold);
        DrawWrapped(ctx, current.SpentPoints > 0 ? "Returns spent points under the existing reset rules."
            : "Spend points to enable respec.", 30, 804, 212, 18,
            current.SpentPoints > 0 ? TextMuted : TextGold, 3, 12);

        DrawText(ctx, treeNames[Array.IndexOf(treeIds, currentTree)] + " talents", 284, 190, 21,
            GetTreeAccent(currentTree));
        DrawWrapped(ctx, GetTreeSubtitle(currentTree), 284, 215, 650, 18, TextMuted, 2, 13);
        if (NodeContentHeight() > NodeViewportHeight)
            DrawText(ctx, "Scroll for more ↓", 804, 190, 12, TextMuted);
        ITreeAttribute perks = BuildPerkTree(current);
        List<FoxPerkDefinition> definitions = FoxPerkCatalog
            .ForTree(currentTree, current.SpeciesId)
            .ToList();
        FoxPerkDefinition selected = definitions.FirstOrDefault(definition => definition.Id == selectedPerkId)
            ?? definitions.FirstOrDefault();
        if (selected != null && selectedPerkId != selected.Id) selectedPerkId = selected.Id;
        if (definitions.Count == 0)
        {
            FoxGuiTheme.DrawPanel(ctx, 284, NodeTop, 650, 96, false);
            DrawText(ctx, "No talents are available in this branch yet.", 300, NodeTop + 34, 18, TextWhite);
            DrawWrapped(ctx,
                "This species has no entries here yet. Try another branch or return later.",
                300,
                NodeTop + 61,
                620,
                16,
                TextMuted,
                2,
                13);
        }
        ctx.Save();
        ctx.Rectangle(278, NodeViewportTop, 658, NodeViewportHeight);
        ctx.Clip();
        int lastTier = -1;
        foreach (var card in GetPerkCards())
        {
            double y = card.Y - nodeScrollOffset;
            if (card.Definition.Row != lastTier)
            {
                string tierName = card.Definition.Row switch
                {
                    0 => "Tier 1 — Foundations",
                    1 => "Tier 2 — Training",
                    2 => "Tier 3 — Mastery",
                    _ => $"Tier {card.Definition.Row + 1}"
                };
                DrawText(ctx, tierName, 284, y - 9, 15, GetTreeAccent(currentTree));
                lastTier = card.Definition.Row;
            }
            if (y + NodeHeight < NodeViewportTop || y > NodeViewportTop + NodeViewportHeight) continue;
            DrawPerkCard(ctx, card.Definition, perks, current.AvailablePoints, card.X, y);
        }
        ctx.Restore();
        double contentHeight = NodeContentHeight();
        if (contentHeight > NodeViewportHeight)
        {
            DrawRect(ctx, 939, NodeViewportTop, 5, NodeViewportHeight, TextLocked);
            double thumbHeight = Math.Max(42, NodeViewportHeight * NodeViewportHeight / contentHeight);
            double thumbY = NodeViewportTop + nodeScrollOffset / (contentHeight - NodeViewportHeight)
                * (NodeViewportHeight - thumbHeight);
            DrawRect(ctx, 939, thumbY, 5, thumbHeight, GetTreeAccent(currentTree));
        }
        DrawSelectedPerkDetails(ctx, selected, perks, current);

        if (!string.IsNullOrWhiteSpace(current.Message))
        {
            FoxGuiTheme.DrawPanel(ctx, 12, height - 48, width - 32, 38, true);
            DrawWrapped(ctx, current.Message, 22, height - 30, width - 52, 15, GetTreeAccent(currentTree), 2, 13);
        }

        FoxGuiTheme.DrawWindowControls(ctx, api, width);
        FoxGuiTheme.DrawHelpGlyph(ctx, width, helpHovered);
    }

    private void DrawPerkCard(Context ctx, FoxPerkDefinition definition, ITreeAttribute perks,
        int availablePoints, double x, double y)
    {
        int rank = FoxPerkCatalog.GetRank(perks, definition);
        bool canBuy = FoxPerkCatalog.CanBuy(perks, definition, availablePoints, out string reason);
        bool maxed = rank >= definition.MaxRank;
        bool buttonEnabled = canBuy && definition.IsImplemented;
        string buttonLabel = maxed
            ? "Max rank"
            : !definition.IsImplemented
                ? "Coming later"
                : canBuy
                    ? "Rank up"
                    : availablePoints <= 0
                        ? "No points"
                        : "Locked";
        ctx.Save();
        ctx.Rectangle(x, y, NodeWidth, NodeHeight);
        ctx.Clip();
        DrawRect(ctx, x, y, NodeWidth, NodeHeight,
            selectedPerkId == definition.Id ? TextAvailable : TextCard);
        DrawRect(ctx, x + 8, y + 8, 46, 46, definition.IsImplemented ? TextImplemented : TextNyi);
        DrawIcon(ctx, definition, x + 8, y + 8, 46);
        DrawRect(
            ctx,
            x + 8,
            y + 8,
            46,
            46,
            !definition.IsImplemented
                ? IconNyiOverlay
                : definition.NeedsTesting ? IconTestingOverlay : IconImplementedOverlay
        );
        DrawWrapped(ctx, definition.Name, x + 61, y + 25, NodeWidth - 69, 16, TextWhite, 2, 13);
        DrawText(ctx, GetRankText(definition, rank), x + 9, y + 74, 13, TextWhite);
        string cardStatus = maxed
            ? "Maximum rank"
            : canBuy
                ? $"Cost: {FormatPoints(FoxPerkCatalog.GetRankCost(definition, rank))}"
                : CompactRequirementText(reason);
        DrawText(ctx, Trim(cardStatus, 27), x + 9, y + 94, 11, canBuy || maxed ? TextMuted : TextGold);
        DrawButton(ctx, buttonLabel, x + 9, y + 105, NodeWidth - 18, 28, buttonEnabled);
        ctx.Restore();
    }

    private void DrawIcon(Context ctx, FoxPerkDefinition definition, double x, double y, double size)
    {
        ImageSurface icon = GuiElement.getImageSurfaceFromAsset(
            api,
            new AssetLocation("feralkinshipcompanions", definition.IconPath)
        );
        try
        {
            double scaleX = size / Math.Max(1, icon.Width);
            double scaleY = size / Math.Max(1, icon.Height);
            ctx.Save();
            ctx.Rectangle(x, y, size, size);
            ctx.Clip();
            ctx.Translate(x, y);
            ctx.Scale(scaleX, scaleY);
            ctx.SetSourceSurface(icon, 0, 0);
            ctx.Paint();
            ctx.Restore();
        }
        finally
        {
            icon.Dispose();
        }
    }

    private void DrawSelectedPerkDetails(Context ctx, FoxPerkDefinition definition,
        ITreeAttribute perks, FoxPerkStatePacket current)
    {
        DrawText(ctx, "Talent details", 976, 190, 19, TextGold);
        FoxGuiTheme.DrawSectionRule(ctx, 976, 200, 272);
        if (definition == null)
        {
            DrawWrapped(ctx, "Choose a talent to see its effects and requirements.",
                976, 236, 270, 20, TextMuted, 4, 14);
            return;
        }
        int rank = FoxPerkCatalog.GetRank(perks, definition);
        bool canBuy = FoxPerkCatalog.CanBuy(perks, definition, current.AvailablePoints,
            out string reason) && definition.IsImplemented;
        DrawRect(ctx, 978, 218, 66, 66, definition.IsImplemented ? TextImplemented : TextNyi);
        DrawIcon(ctx, definition, 981, 221, 60);
        DrawWrapped(ctx, definition.Name, 1052, 239, 195, 20, TextWhite, 2, 17);
        DrawText(ctx, $"{currentTree} · Tier {definition.Row + 1}", 1052, 279, 12, TextMuted);
        DrawText(ctx, $"Current rank: {rank}/{definition.MaxRank}", 978, 319, 15, TextWhite);
        DrawText(ctx, rank >= definition.MaxRank ? "Maximum rank" : $"Next rank: {rank + 1}/{definition.MaxRank}",
            978, 344, 15, TextWhite);
        if (rank < definition.MaxRank)
            DrawText(ctx, $"Cost: {FormatPoints(FoxPerkCatalog.GetRankCost(definition, rank))}",
                978, 369, 15, TextGold);
        FoxGuiTheme.DrawSectionRule(ctx, 978, 390, 268);
        DrawText(ctx, "What changes", 978, 419, 17, TextGold);
        DrawWrapped(ctx, definition.Description, 978, 446, 266, 21,
            TextWhite, 7, 14);
        if (!string.IsNullOrWhiteSpace(definition.UsageRequirement))
        {
            DrawText(ctx, "Applies when", 978, 590, 15, TextGold);
            DrawWrapped(ctx, definition.UsageRequirement, 978, 613, 266, 18,
                TextMuted, 3, 12);
        }
        DrawText(ctx, "Requirements", 978, 684, 17, TextGold);
        string availability = !definition.IsImplemented ? "Coming later"
            : rank >= definition.MaxRank ? "Maximum rank reached"
            : canBuy ? "Ready to unlock"
            : FullRequirementText(reason);
        DrawWrapped(ctx, availability, 978, 712, 266, 19,
            canBuy ? TextWhite : TextGold, 3, 13);
        DrawButton(ctx, rank >= definition.MaxRank ? "Max rank"
            : $"Rank up — {FormatPoints(FoxPerkCatalog.GetRankCost(definition, rank))}",
            978, 784, 268, 45, canBuy, GetTreeAccent(currentTree));
        DrawText(ctx, "Points here belong only to this companion.", 978, 852, 11, TextMuted);
    }

    private void DrawTooltip(Context ctx, FoxPerkDefinition definition, int width, int height)
    {
        double cardX = 274 + definition.Column * NodeSpacingX;
        double cardY = NodeTop + definition.Row * NodeSpacingY;
        bool hasUsageRequirement = !string.IsNullOrWhiteSpace(definition.UsageRequirement);
        double tooltipWidth = 402;
        double tooltipHeight = hasUsageRequirement ? 286 : 230;
        double x = cardX + NodeWidth + 12;
        if (x + tooltipWidth > width - 8)
        {
            x = Math.Max(8, cardX - tooltipWidth - 12);
        }

        double y = Math.Clamp(cardY, 8, height - tooltipHeight - 8);
        DrawRect(ctx, x + 5, y + 5, tooltipWidth, tooltipHeight, TooltipShadow);
        DrawRect(ctx, x - 2, y - 2, tooltipWidth + 4, tooltipHeight + 4, TextGold);
        DrawRect(ctx, x, y, tooltipWidth, tooltipHeight, TooltipBackground);
        DrawText(ctx, definition.Name, x + 14, y + 25, 17, TextWhite);
        string developmentState = !definition.IsImplemented
            ? "Coming later"
            : definition.NeedsTesting ? "Testing" : "Ready";
        FoxGuiTheme.DrawBadge(ctx, x + tooltipWidth - 108, y + 10, 94, 23,
            definition.IsImplemented && !definition.NeedsTesting);
        DrawText(ctx, developmentState, x + tooltipWidth - 98, y + 27, 11,
            definition.IsImplemented ? TextWhite : TextGold);

        DrawText(ctx, "Effect", x + 14, y + 52, 13, TextGold);
        DrawWrapped(ctx, definition.Description, x + 14, y + 73,
            tooltipWidth - 28, 16, TextWhite, 4, 13);
        FoxGuiTheme.DrawSectionRule(ctx, x + 14, y + 138, tooltipWidth - 28);

        double availabilityY;
        if (hasUsageRequirement)
        {
            DrawText(ctx, "Applies when", x + 14, y + 159, 13, TextGold);
            DrawWrapped(ctx, definition.UsageRequirement, x + 14, y + 180,
                tooltipWidth - 28, 16, TextWhite, 2, 12);
            DrawText(ctx, "Availability", x + 14, y + 220, 13, TextGold);
            availabilityY = y + 241;
        }
        else
        {
            DrawText(ctx, "Availability", x + 14, y + 160, 13, TextGold);
            availabilityY = y + 181;
        }

        ITreeAttribute perks = state == null ? new TreeAttribute() : BuildPerkTree(state);
        int rank = FoxPerkCatalog.GetRank(perks, definition);
        string reason = state == null ? "Waiting for companion data" : string.Empty;
        bool canBuy = state != null
            && FoxPerkCatalog.CanBuy(perks, definition, state.AvailablePoints, out reason);
        string availability = rank >= definition.MaxRank
            ? "Maximum rank reached"
            : canBuy
                ? $"Ready — next rank costs {FormatPoints(FoxPerkCatalog.GetRankCost(definition, rank))}"
                : FullRequirementText(reason);
        DrawWrapped(ctx, availability, x + 14, availabilityY,
            tooltipWidth - 28, 15, canBuy ? TextWhite : TextGold, 2, 12);
        DrawText(ctx,
            $"Current rank: {rank}/{definition.MaxRank}   |   Tree gate: {definition.TreeInvestmentRequired} invested",
            x + 14, y + tooltipHeight - 14, 11, TextMuted);
    }

    private FoxPerkDefinition FindHoveredPerk(double x, double y)
    {
        if (x < 278 || x > 936 || y < NodeViewportTop || y > NodeViewportTop + NodeViewportHeight)
            return null;
        foreach (var card in GetPerkCards())
        {
            double cardY = card.Y - nodeScrollOffset;
            if (x >= card.X && x <= card.X + NodeWidth && y >= cardY && y <= cardY + NodeHeight)
            {
                return card.Definition;
            }
        }

        return null;
    }

    private void HandleClick(double x, double y)
    {
        double helpX = FoxGuiTheme.HelpButtonX(CanvasWidth);
        if (x >= helpX && x <= helpX + 34 && y >= 8 && y <= 38)
        {
            FoxGuiTheme.PlayNavigation(api);
            if (guideOpen)
            {
                DismissGuide();
                helpOpen = true;
            }
            else
            {
                helpOpen = !helpOpen;
            }
            Redraw();
            return;
        }

        if (guideOpen)
        {
            if (x >= CanvasWidth - 150
                && x <= CanvasWidth - 30
                && y >= CanvasHeight - 52
                && y <= CanvasHeight - 22)
            {
                FoxGuiTheme.PlayAction(api);
                DismissGuide();
                Redraw();
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
                guideOpen = true;
                guideDismissalNotified = true;
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

        if (state == null)
        {
            return;
        }

        if (x >= CanvasWidth - 192 && x <= CanvasWidth - 22 && y >= 92 && y <= 132)
        {
            FoxGuiTheme.PlayNavigation(api);
            system.TryOpenFoxSocialGui(targetEntityId);
            Redraw();
            return;
        }
        if (x >= 30 && x <= 246 && y >= 211 && y < 211 + 3 * 68)
        {
            int index = (int)((y - 211) / 68);
            currentTree = new[] { FoxPerkTreeId.Combat, FoxPerkTreeId.Survival,
                FoxPerkTreeId.Social }[Math.Clamp(index, 0, 2)];
            selectedPerkId = string.Empty;
            nodeScrollOffset = 0;
            FoxGuiTheme.PlayNavigation(api);
            Redraw();
            return;
        }
        if (x >= 30 && x <= 246 && y >= 740 && y <= 780 && state.SpentPoints > 0)
        {
            FoxGuiTheme.PlayAction(api);
            system.SendFoxSocialAction(targetEntityId, FoxSocialRequestAction.ResetPerks);
            return;
        }

        FoxPerkDefinition selected = FoxPerkCatalog.Get(selectedPerkId);
        if (x >= 978 && x <= 1246 && y >= 784 && y <= 829 && selected != null)
        {
            TryBuySelectedPerk(selected);
            return;
        }

        if (x < 278 || x > 936 || y < NodeViewportTop || y > NodeViewportTop + NodeViewportHeight)
            return;
        foreach (var card in GetPerkCards())
        {
            double cardY = card.Y - nodeScrollOffset;
            if (x < card.X || x > card.X + NodeWidth || y < cardY || y > cardY + NodeHeight)
            {
                continue;
            }
            selectedPerkId = card.Definition.Id;
            if (y >= cardY + 105 && y <= cardY + 133) TryBuySelectedPerk(card.Definition);
            else { FoxGuiTheme.PlayChoice(api); Redraw(); }
            return;
        }
    }

    private void TryBuySelectedPerk(FoxPerkDefinition definition)
    {
        if (state == null) return;
        ITreeAttribute perks = BuildPerkTree(state);
        if (definition.IsImplemented && FoxPerkCatalog.CanBuy(perks, definition,
            state.AvailablePoints, out _))
        {
            FoxGuiTheme.PlayAction(api);
            system.SendFoxSocialAction(targetEntityId, FoxSocialRequestAction.BuyPerk, definition.Id);
        }
        else FoxGuiTheme.PlayUnavailable(api);
        Redraw();
    }

    private void DrawHelpOverlay(Context ctx, int width, int height)
    {
        FoxGuiTheme.DrawOverlay(ctx, width, height, TextLocked);
        DrawText(ctx, Lang.Get("feralkinshipcompanions:help-talent-title"), 30, 45, 23, TextWhite);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-talent-points-text"), 30, 78, width - 60, 17, TextWhite, 2, 14);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-talent-branches-text"), 30, 116, width - 60, 17, TextMuted, 4, 14);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-talent-rank-text"), 30, 184, width - 60, 17, TextMuted, 3, 14);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-talent-respec-text"), 30, 235, width - 60, 17, TextMuted, 2, 14);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-talent-hover-text"), 30, 286, width - 60, 17, TextMuted, 4, 14);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-talent-saved-text"), 30, 354, width - 60, 17, TextMuted, 3, 14);
        DrawButton(ctx, Lang.Get("feralkinshipcompanions:tutorial-bram-notes"), width - 310, height - 52, 150, 30, true);
        DrawButton(ctx, Lang.Get("feralkinshipcompanions:help-close"), width - 150, height - 52, 120, 30, true);
    }

    private void DrawGuideOverlay(Context ctx, int width, int height)
    {
        FoxGuiTheme.DrawOverlay(ctx, width, height, TextLocked);
        DrawText(ctx, Lang.Get("feralkinshipcompanions:guide-talent-title"), 30, 45, 23, TextWhite);
        DrawText(ctx, Lang.Get("feralkinshipcompanions:guide-talent-byline"), 30, 73, 15, TextGold);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:guide-talent-text"), 30, 110, width - 60, 18, TextWhite, 6, 15);
        FoxGuiTheme.DrawPanel(ctx, 30, height - 142, width - 60, 62, true);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:guide-open-handbook"), 44, height - 112, width - 88, 18, TextGold, 2, 14);
        DrawButton(ctx, Lang.Get("feralkinshipcompanions:guide-continue"), width - 150, height - 52, 120, 30, true);
    }

    private void DismissGuide()
    {
        if (!guideOpen)
        {
            return;
        }

        guideOpen = false;
        if (!guideDismissalNotified)
        {
            guideDismissalNotified = true;
            system.MarkCompanionTalentGuideSeen();
        }
    }

    private static ITreeAttribute BuildPerkTree(FoxPerkStatePacket packet)
    {
        ITreeAttribute perks = new TreeAttribute();
        foreach (FoxPerkRankEntry entry in packet.Ranks ?? new List<FoxPerkRankEntry>())
        {
            if (FoxPerkCatalog.Get(entry.Id) != null)
            {
                perks.SetInt(entry.Id, entry.Rank);
            }
        }
        return perks;
    }

    private static string GetRankText(FoxPerkDefinition definition, int rank)
    {
        return $"Rank {rank}/{definition.MaxRank}";
    }

    private static string CompactRequirementText(string reason)
    {
        string text = FullRequirementText(reason);
        const string investedSuffix = " points invested in this tree";
        if (text.StartsWith("Requires ", StringComparison.Ordinal)
            && text.EndsWith(investedSuffix, StringComparison.Ordinal))
        {
            return "Tree gate: " + text.Substring(9, text.Length - 9 - investedSuffix.Length);
        }
        if (text.StartsWith("Needs ", StringComparison.Ordinal)) return Trim(text, 21);
        if (text.StartsWith("Requires ", StringComparison.Ordinal)) return Trim("Needs " + text.Substring(9), 21);
        if (text.StartsWith("Exclusive with ", StringComparison.Ordinal)) return Trim("Exclusive: " + text.Substring(15), 21);
        return Trim(text, 21);
    }

    private static string FullRequirementText(string reason)
    {
        return (reason ?? string.Empty).Trim().TrimEnd('.');
    }

    private static string FormatPoints(int points)
    {
        return $"{points} point{(points == 1 ? string.Empty : "s")}";
    }

    private static string GetTreeSubtitle(string treeId)
    {
        return treeId switch
        {
            FoxPerkTreeId.Combat => "Combat — resilience first, then future fighting power.",
            FoxPerkTreeId.Survival => "Survival — movement, hazards, awareness, and escape.",
            _ => "Social / Economy — requests, pack actions, and one economic specialization."
        };
    }

    private static void DrawWrapped(Context ctx, string text, double x, double baseline, double maxWidth, double lineHeight, double[] color, int maxLines, double fontSize = 12)
    {
        string[] words = (text ?? string.Empty)
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        List<string> lines = new();
        string line = string.Empty;
        foreach (string word in words)
        {
            string next = string.IsNullOrEmpty(line) ? word : line + " " + word;
            if (FeralKinshipCompanionUiSettings.GetTextWidth(next, fontSize) > maxWidth
                && line.Length > 0)
            {
                lines.Add(line);
                line = word;
            }
            else line = next;
        }
        if (line.Length > 0)
        {
            lines.Add(line);
        }
        for (int index = 0; index < Math.Min(maxLines, lines.Count); index++)
        {
            string rendered = lines[index];
            if (index == maxLines - 1 && lines.Count > maxLines)
            {
                rendered = FeralKinshipCompanionUiSettings.TrimTextToWidth(rendered + "…", maxWidth, fontSize);
            }
            DrawText(ctx, rendered, x, baseline + index * lineHeight * FeralKinshipCompanionUiSettings.TextScale, fontSize, color);
        }
    }

    private static void DrawButton(Context ctx, string label, double x, double y, double width, double height, bool active, double[] accent = null)
    {
        FoxGuiTheme.DrawButtonSurface(ctx, x, y, width, height, active);
        DrawText(ctx, label, x + 10, y + height / 2 + 6, 15, active ? accent ?? TextWhite : TextMuted);
    }

    private static void DrawChoiceButton(Context ctx, string label, double x, double y, double width, double height, bool selected, bool hovered, double[] accent)
    {
        bool highlighted = selected || hovered;
        FoxGuiTheme.DrawTabSurface(ctx, x, y, width, height, selected, hovered, accent);
        if (highlighted)
        {
            DrawRect(ctx, x, y, 4, height, accent);
        }
        DrawText(ctx, label, x + 10, y + height / 2 + 6, 15, highlighted ? accent : TextWhite);
    }

    private string FindHoveredTree(double x, double y)
    {
        if (y < 211 || y >= 211 + 3 * 68 || x < 30 || x > 246)
        {
            return string.Empty;
        }
        return new[] { FoxPerkTreeId.Combat, FoxPerkTreeId.Survival,
            FoxPerkTreeId.Social }[(int)((y - 211) / 68)];
    }

    private static double[] GetTreeAccent(string treeId)
    {
        return treeId switch
        {
            FoxPerkTreeId.Combat => TextCombatAccent,
            FoxPerkTreeId.Survival => TextSurvivalAccent,
            _ => TextSocialAccent
        };
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
