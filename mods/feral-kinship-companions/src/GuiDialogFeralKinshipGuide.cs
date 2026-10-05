#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace FeralKinshipCompanions;

public sealed class GuiDialogFeralKinshipGuide : GuiDialog
{
    private const double DialogWidth = 820;
    private const double DialogHeight = 720;

    public GuiDialogFeralKinshipGuide(
        ICoreClientAPI capi,
        FeralKinshipCompanionSystem system,
        BrambleStatePacket state) : base(capi)
    {
        Surface = new GuiElementFeralKinshipGuideSurface(
            capi,
            ElementBounds.Fixed(0, 0, DialogWidth, DialogHeight),
            system,
            state,
            () => TryClose());
    }

    private GuiElementFeralKinshipGuideSurface Surface { get; }
    public override string ToggleKeyCombinationCode => null;

    public override void OnGuiOpened()
    {
        ElementBounds contentBounds = ElementBounds.Fixed(0, 0, DialogWidth, DialogHeight).WithFixedPadding(0);
        contentBounds.BothSizing = ElementSizing.Fixed;
        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
        SingleComposer = capi.Gui.CreateCompo("feralkinship-bramble", dialogBounds)
            .AddShadedDialogBG(contentBounds)
            .AddDialogTitleBar("Bramble", () => TryClose())
            .BeginChildElements(contentBounds)
            .AddInteractiveElement(Surface, "bramble-surface")
            .EndChildElements()
            .Compose();
        base.OnGuiOpened();
    }

    public override void OnGuiClosed()
    {
        Surface.ResolveClosedOffer();
        base.OnGuiClosed();
    }
}

internal sealed class GuiElementFeralKinshipGuideSurface : GuiElementFeralKinshipScaledSurface
{
    private enum GuidePage { Offer, Species, Home, Next, Taming, Topics, Topic, About, Hints, Treatment, Dismissal }

    private static readonly (string Id, string Label)[] GuideTopics =
    {
        ("interaction", "Interaction"), ("follow", "Follow"), ("at_ease", "At Ease"),
        ("rest", "Rest"), ("return_home", "Return Home"), ("combat_style", "Combat and risk"),
        ("requests", "Requests"), ("hunger", "Hunger"), ("juveniles", "Juveniles"),
        ("talents", "Talents"), ("camps_dens", "Camps and dens"), ("pack", "Pack status"),
        ("duties", "Duties"), ("work_carts", "Work Carts"), ("expeditions", "Expeditions"),
        ("wounded", "Wounded"), ("mortal", "Mortally wounded"), ("bramble_difference", "How Bramble differs")
    };

    protected override double DesignWidth => 820;
    protected override double DesignHeight => 720;
    private static double[] TextWhite => FoxGuiTheme.Text;
    private static double[] TextMuted => FoxGuiTheme.Muted;
    private static double[] TextGold => FoxGuiTheme.Accent;

    private readonly FeralKinshipCompanionSystem system;
    private readonly Action close;
    private readonly BrambleStatePacket state;
    private GuidePage page;
    private string selectedTopic = string.Empty;
    private int tamingPage;
    private int textureId;
    private FeralKinshipCompanionSystem.GuideAdviceClient currentAdvice;
    private bool offerResolved;

    public GuiElementFeralKinshipGuideSurface(
        ICoreClientAPI capi,
        ElementBounds bounds,
        FeralKinshipCompanionSystem system,
        BrambleStatePacket state,
        Action close) : base(capi, bounds)
    {
        this.system = system;
        this.state = state;
        this.close = close;
        page = state.ShowOnboarding
            ? GuidePage.Offer
            : state.DismissalPending
                ? GuidePage.Dismissal
            : state.OnboardingState == BrambleOnboardingState.Accepted && !state.SpeciesChosen
                ? GuidePage.Species
                : GuidePage.Home;
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

    public override void Dispose()
    {
        if (textureId > 0) api.Render.GLDeleteTexture(textureId);
        textureId = 0;
        base.Dispose();
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        args.Handled = true;
        if (args.Button != EnumMouseButton.Left) return;
        double x = CanvasMouseX(args.X);
        double y = CanvasMouseY(args.Y);
        if (HandleFontScaleClick(x, y)) { Redraw(); return; }
        HandleClick(x, y);
    }

    public void ResolveClosedOffer()
    {
        if (page == GuidePage.Offer && !offerResolved)
        {
            offerResolved = true;
            system.SendBrambleChoice(BrambleRequestPacket.OnboardingChoice, "dismiss");
        }
    }

    private void Redraw()
    {
        Bounds.CalcWorldBounds();
        ImageSurface image = new(Format.Argb32, Math.Max(1, Bounds.OuterWidthInt), Math.Max(1, Bounds.OuterHeightInt));
        Context ctx = new(image);
        try
        {
            ScaleCanvas(ctx);
            Draw(ctx, (int)CanvasWidth, (int)CanvasHeight);
            generateTexture(image, ref textureId);
        }
        finally { ctx.Dispose(); image.Dispose(); }
    }

    private void Draw(Context ctx, int width, int height)
    {
        FoxGuiTheme.DrawJournal(ctx, width, height, FoxGuiSurfaceKind.Companion);
        FoxGuiTheme.DrawWindowControls(ctx, api, width);
        DrawText(ctx, "Bramble", 28, 42, 27, TextWhite);
        DrawText(ctx, state.Follow ? "Following" : "Staying", 148, 42, 15, TextMuted);
        switch (page)
        {
            case GuidePage.Offer: DrawOffer(ctx, width, height); break;
            case GuidePage.Species: DrawSpecies(ctx, width, height); break;
            case GuidePage.Next: DrawNext(ctx, width, height); break;
            case GuidePage.Taming: DrawTaming(ctx, width, height); break;
            case GuidePage.Topics: DrawTopics(ctx, width, height); break;
            case GuidePage.Topic: DrawTopic(ctx, width, height); break;
            case GuidePage.About: DrawAbout(ctx, width, height); break;
            case GuidePage.Hints: DrawHints(ctx, width, height); break;
            case GuidePage.Treatment: DrawTreatment(ctx, width, height); break;
            case GuidePage.Dismissal: DrawDismissal(ctx, width, height); break;
            default: DrawHome(ctx, width, height); break;
        }
    }

    private void DrawOffer(Context ctx, int width, int height)
    {
        DrawText(ctx, "Something at the firelight", 30, 92, 22, TextGold);
        DrawWrapped(ctx, system.BrambleLine("bramble.onboarding.first_offer.01"), 30, 140, width - 60, 22, TextWhite, 6);
        DrawWrapped(ctx, system.BrambleLine("bramble.onboarding.first_offer.02"), 30, 285, width - 60, 22, TextGold, 3);
        DrawButton(ctx, "Accept help", 30, 400, 230, 46, true);
        DrawButton(ctx, "Not right now", 295, 400, 230, 46, true);
        DrawButton(ctx, "Ask later", 560, 400, 230, 46, true);
        DrawWrapped(ctx, "Your answer is saved for this player in this world. Bramble remains available if you decline.", 30, 505, width - 60, 18, TextMuted, 3);
    }

    private void DrawSpecies(Context ctx, int width, int height)
    {
        DrawText(ctx, "Choose the first animal to learn", 30, 84, 22, TextGold);
        DrawWrapped(ctx, string.IsNullOrWhiteSpace(state.ResponseLine) ? system.BrambleLine("bramble.onboarding.accept.01") : state.ResponseLine,
            30, 122, width - 60, 19, TextWhite, 3);
        for (int i = 0; i < FeralKinshipCompanionSystem.BrambleTutorialSpeciesIds.Length; i++)
        {
            int col = i % 3;
            int row = i / 3;
            string id = FeralKinshipCompanionSystem.BrambleTutorialSpeciesIds[i];
            DrawButton(ctx, FeralKinshipCompanionSystem.GetBrambleSpeciesDisplayName(id), 30 + col * 260, 210 + row * 66, 230, 42, true);
        }
        DrawWrapped(ctx, "This changes Bramble's visible form, not his name, ownership, memories, or helper state.", 30, 520, width - 60, 18, TextMuted, 3);
        DrawFooter(ctx, width, height, false);
    }

    private void DrawHome(Context ctx, int width, int height)
    {
        DrawWrapped(ctx,
            string.IsNullOrWhiteSpace(state.ResponseLine)
                ? "I am here to help you understand the others. Ask what to do next, or choose a chapter."
                : state.ResponseLine,
            30, 92, width - 60, 20, TextWhite, 4);
        DrawButton(ctx, "What should I do next?", 30, 205, 360, 44, true);
        DrawButton(ctx, state.FirstTamingChapter ? "First-taming guide" : "Companion guide", 430, 205, 360, 44, true);
        DrawButton(ctx, "About Bramble", 30, 270, 360, 44, true);
        DrawButton(ctx, "Hint preferences", 430, 270, 360, 44, true);
        DrawButton(ctx, "Choose Bramble's form", 30, 335, 360, 44, true);
        DrawButton(ctx, "Refresh recommendation", 430, 335, 360, 44, true);
        DrawButton(ctx, "Let Bramble move on", 30, 400, 360, 44, true);
        DrawWrapped(ctx, system.BrambleLine("bramble.guide.bramble_difference.01"), 30, 490, width - 60, 19, TextGold, 3);
        DrawWrapped(ctx, "Right-click Bramble with an empty hand for help. Sneak and right-click to toggle Follow and Stay.", 30, 580, width - 60, 18, TextMuted, 3);
        DrawFooter(ctx, width, height, true);
    }

    private void DrawDismissal(Context ctx, int width, int height)
    {
        DrawText(ctx, "A gentle farewell", 30, 92, 22, TextGold);
        DrawWrapped(ctx,
            string.IsNullOrWhiteSpace(state.ResponseLine)
                ? "I think I can manage from here. Bramble will move on and will not return in this world."
                : state.ResponseLine,
            30, 140, width - 60, 21, TextWhite, 5);
        DrawWrapped(ctx, "This is permanent. You can keep Bramble here, or let him go.", 30, 290, width - 60, 18, TextMuted, 3);
        DrawButton(ctx, "Keep Bramble", 30, 390, 360, 46, true);
        DrawButton(ctx, "Let Bramble move on", 430, 390, 360, 46, true);
        DrawFooter(ctx, width, height, false);
    }

    private void DrawNext(Context ctx, int width, int height)
    {
        currentAdvice = system.GetGuideAdviceClient();
        DrawText(ctx, currentAdvice.Heading, 30, 88, 22, TextGold);
        DrawWrapped(ctx, currentAdvice.Body, 30, 128, width - 60, 20, TextWhite, 5);
        for (int i = 0; i < Math.Min(3, currentAdvice.Steps.Length); i++) DrawStep(ctx, (i + 1).ToString(), currentAdvice.Steps[i], 30, 260 + i * 65);
        if (state.NearbyWildTarget && currentAdvice.Id is "no_companion" or "nearby_target")
            DrawWrapped(ctx, system.BrambleLine("bramble.next.nearby_target.01").Replace("{species}", FeralKinshipCompanionSystem.GetBrambleSpeciesDisplayName(state.SpeciesId)), 30, 480, width - 60, 18, TextGold, 3);
        if (currentAdvice.HasAction) DrawButton(ctx, currentAdvice.ActionLabel, 30, 565, 260, 38, true);
        DrawFooter(ctx, width, height, false);
    }

    private void DrawTaming(Context ctx, int width, int height)
    {
        if (!state.FirstTamingChapter)
        {
            page = GuidePage.Topics;
            DrawTopics(ctx, width, height);
            return;
        }
        string species = FeralKinshipCompanionSystem.GetBrambleSpeciesDisplayName(state.SpeciesId);
        DrawText(ctx, "First taming: " + species, 30, 84, 22, TextGold);
        if (tamingPage == 0)
        {
            string[] ids = { "bramble.taming.common.start.01", "bramble.taming.common.food.01", "bramble.taming.common.patience.01", "bramble.taming.common.after.01" };
            double y = 126;
            foreach (string id in ids) { DrawWrapped(ctx, system.BrambleLine(id), 30, y, width - 60, 18, TextWhite, 3); y += 82; }
            string relationId = state.PlayerSpeciesRelationship switch
            {
                "same" => "bramble.taming.same_species.01", "different" => "bramble.taming.different_species.01", _ => "bramble.taming.unknown_species.01"
            };
            DrawWrapped(ctx, system.BrambleLine(relationId), 30, 480, width - 60, 18, TextGold, 3);
            DrawButton(ctx, "Species details", 30, 590, 230, 36, true);
        }
        else
        {
            IReadOnlyList<BrambleDialogueEntry> lines = system.BrambleLines("bramble.taming." + state.SpeciesId + ".");
            if (state.SpeciesId == "wolf")
            {
                lines = lines.Where(entry => entry.Id.EndsWith(".03", StringComparison.Ordinal)
                    || state.PlayerSpeciesRelationship == "same" && entry.Id.EndsWith(".01", StringComparison.Ordinal)
                    || state.PlayerSpeciesRelationship == "different" && entry.Id.EndsWith(".02", StringComparison.Ordinal)).ToList();
            }
            double y = 128;
            foreach (BrambleDialogueEntry entry in lines) { DrawWrapped(ctx, entry.Line, 30, y, width - 60, 19, TextWhite, 4); y += 100; }
            DrawText(ctx, "Accepted taming foods (live rules)", 30, 450, 17, TextGold);
            DrawWrapped(ctx, state.AcceptedFoods.Count == 0 ? "No accepted-food data is currently loaded." : string.Join("  •  ", state.AcceptedFoods), 30, 482, width - 60, 18, TextMuted, 5);
            if (state.NearbyWildTarget) DrawWrapped(ctx, system.BrambleLine("bramble.next.nearby_target.01").Replace("{species}", species), 30, 580, width - 60, 18, TextGold, 2);
            DrawButton(ctx, "Common approach", 560, 620, 230, 34, true);
        }
        DrawFooter(ctx, width, height, false);
    }

    private void DrawTopics(Context ctx, int width, int height)
    {
        DrawText(ctx, "Companion guide", 30, 84, 22, TextGold);
        DrawWrapped(ctx, "The first-taming chapter retires after your first ordinary Companion. These topics describe the universal systems.", 30, 118, width - 60, 18, TextMuted, 3);
        for (int i = 0; i < GuideTopics.Length; i++)
        {
            int col = i % 2;
            int row = i / 2;
            DrawButton(ctx, GuideTopics[i].Label, 30 + col * 395, 182 + row * 47, 365, 34, true);
        }
        DrawFooter(ctx, width, height, false);
    }

    private void DrawTopic(Context ctx, int width, int height)
    {
        var topic = GuideTopics.FirstOrDefault(item => item.Id == selectedTopic);
        DrawText(ctx, string.IsNullOrWhiteSpace(topic.Label) ? "Companion guide" : topic.Label, 30, 90, 23, TextGold);
        DrawWrapped(ctx, "Right-click an ordinary Companion with an empty hand to open its character card and use its commands. Bramble has this help menu instead; crouch and right-click him to toggle Follow and Stay.", 30, 122, width - 60, 18, TextMuted, 4);
        DrawWrapped(ctx, system.BrambleLine("bramble.guide." + selectedTopic + ".01"), 30, 180, width - 60, 23, TextWhite, 8);
        if (selectedTopic == "wounded" || selectedTopic == "mortal") DrawButton(ctx, "Treatment help", 30, 520, 240, 40, true);
        DrawFooter(ctx, width, height, false);
    }

    private void DrawAbout(Context ctx, int width, int height)
    {
        DrawText(ctx, "About Bramble", 30, 84, 22, TextGold);
        DrawText(ctx, "What I am", 30, 126, 16, TextGold);
        DrawWrapped(ctx,
            system.BrambleLine("bramble.self.what_are_you.01") + " " + system.BrambleLine("bramble.self.what_are_you.02"),
            30, 150, width - 60, 18, TextWhite, 2);

        DrawText(ctx, "My job", 30, 212, 16, TextGold);
        DrawWrapped(ctx,
            system.BrambleLine("bramble.self.why_no_work.01") + " " + system.BrambleLine("bramble.self.why_no_work.02")
                + " I explain the Companion systems; I do not perform their duties.",
            30, 236, width - 60, 18, TextWhite, 3);

        DrawText(ctx, "What I do not do", 30, 316, 16, TextGold);
        DrawWrapped(ctx, system.BrambleLine("bramble.self.why_no_fight.01"), 30, 340, width - 60, 18, TextWhite, 3);

        DrawText(ctx, "A few boundaries", 30, 410, 16, TextGold);
        DrawWrapped(ctx,
            system.BrambleLine("bramble.self.why_no_pack.01") + " " + system.BrambleLine("bramble.self.why_no_pack.02")
                + " Rename Bramble? " + system.BrambleLine("bramble.self.rename.01"),
            30, 434, width - 60, 18, TextWhite, 3);

        DrawText(ctx, "Follow or stay", 30, 510, 16, TextGold);
        DrawWrapped(ctx,
            "Stay: " + system.BrambleLine("bramble.ambient.stay.01") + " Follow again: " + system.BrambleLine("bramble.ambient.resume.01"),
            30, 534, width - 60, 18, TextWhite, 2);
        DrawFooter(ctx, width, height, false);
    }

    private void DrawHints(Context ctx, int width, int height)
    {
        DrawText(ctx, "Unsolicited hints", 30, 84, 22, TextGold);
        DrawWrapped(ctx, "Bramble is quiet by default. Occasional is the recommended choice if you want contextual interruptions.", 30, 125, width - 60, 20, TextWhite, 4);
        DrawButton(ctx, "Quiet" + (state.HintMode == "quiet" ? "  • selected" : ""), 30, 240, 230, 44, true);
        DrawButton(ctx, "Occasional  • recommended" + (state.HintMode == "occasional" ? "  • selected" : ""), 295, 240, 230, 44, true);
        DrawButton(ctx, "Frequent" + (state.HintMode == "frequent" ? "  • selected" : ""), 560, 240, 230, 44, true);
        DrawWrapped(ctx, system.BrambleLine("bramble.hints.quiet.01"), 30, 345, width - 60, 18, TextMuted, 3);
        DrawWrapped(ctx, system.BrambleLine("bramble.hints.occasional.01"), 30, 420, width - 60, 18, TextGold, 3);
        DrawWrapped(ctx, system.BrambleLine("bramble.hints.frequent.01"), 30, 495, width - 60, 18, TextMuted, 3);
        DrawFooter(ctx, width, height, false);
    }

    private void DrawTreatment(Context ctx, int width, int height)
    {
        DrawText(ctx, "Treatment help", 30, 84, 22, TextGold);
        DrawWrapped(ctx, system.BrambleLine("bramble.guide.mortal.01"), 30, 130, width - 60, 21, TextGold, 3);
        DrawWrapped(ctx, system.BrambleLine("bramble.guide.wounded.01"), 30, 205, width - 60, 20, TextWhite, 3);
        DrawStep(ctx, "1", "Hold a valid vanilla healing item such as the appropriate poultice.", 30, 300);
        DrawStep(ctx, "2", "Hold the healing item, then Ctrl + right-click the wounded Companion to begin treatment.", 30, 370);
        DrawStep(ctx, "3", "Keep the Companion safe until the recovering state finishes.", 30, 440);
        DrawWrapped(ctx, "Wounded interaction deliberately does not open the ordinary social window, so this action stays treatment-focused.", 30, 545, width - 60, 18, TextMuted, 4);
        DrawFooter(ctx, width, height, false);
    }

    private void HandleClick(double x, double y)
    {
        if (page == GuidePage.Offer)
        {
            if (y is >= 400 and <= 446)
            {
                offerResolved = true;
                string choice = x < 275 ? "accept" : x < 545 ? "not-now" : "ask-later";
                system.SendBrambleChoice(BrambleRequestPacket.OnboardingChoice, choice);
            }
            return;
        }
        if (page == GuidePage.Species)
        {
            if (y >= 210 && y <= 450)
            {
                int col = (int)((x - 30) / 260);
                int row = (int)((y - 210) / 66);
                int index = row * 3 + col;
                if (col is >= 0 and < 3 && index >= 0 && index < FeralKinshipCompanionSystem.BrambleTutorialSpeciesIds.Length)
                    system.SendBrambleChoice(BrambleRequestPacket.SelectSpecies, FeralKinshipCompanionSystem.BrambleTutorialSpeciesIds[index]);
            }
            else HandleFooter(x, y);
            return;
        }
        if (HandleFooter(x, y)) return;

        if (page == GuidePage.Dismissal)
        {
            if (y is >= 390 and <= 436)
            {
                if (x < 410) system.SendBrambleChoice(BrambleRequestPacket.CancelDismissal);
                else system.SendBrambleChoice(BrambleRequestPacket.ConfirmDismissal, "confirm");
            }
            return;
        }

        if (page == GuidePage.Home)
        {
            if (y is >= 205 and <= 249) page = x < 410 ? GuidePage.Next : state.FirstTamingChapter ? GuidePage.Taming : GuidePage.Topics;
            else if (y is >= 270 and <= 314) page = x < 410 ? GuidePage.About : GuidePage.Hints;
            else if (y is >= 335 and <= 379)
            {
                if (x < 410) page = GuidePage.Species;
                else { system.SendBrambleChoice(BrambleRequestPacket.Refresh); return; }
            }
            else if (y is >= 400 and <= 444)
            {
                system.SendBrambleChoice(BrambleRequestPacket.BeginDismissal);
                return;
            }
        }
        else if (page == GuidePage.Next && currentAdvice != null && currentAdvice.HasAction && y is >= 565 and <= 603)
        {
            if (currentAdvice.Action == FeralKinshipCompanionSystem.GuideAdviceAction.TreatmentHelp) page = GuidePage.Treatment;
            else if (currentAdvice.Action == FeralKinshipCompanionSystem.GuideAdviceAction.OpenTamingGuide) page = GuidePage.Taming;
            else system.TryFollowGuideAdvice(currentAdvice);
        }
        else if (page == GuidePage.Taming)
        {
            if (tamingPage == 0 && y is >= 590 and <= 626) tamingPage = 1;
            else if (tamingPage == 1 && x >= 560 && y is >= 620 and <= 654) tamingPage = 0;
        }
        else if (page == GuidePage.Topics && y >= 182 && y <= 605)
        {
            int col = x < 410 ? 0 : 1;
            int row = (int)((y - 182) / 47);
            int index = row * 2 + col;
            if (index >= 0 && index < GuideTopics.Length) { selectedTopic = GuideTopics[index].Id; page = GuidePage.Topic; }
        }
        else if (page == GuidePage.Topic && (selectedTopic == "wounded" || selectedTopic == "mortal") && y is >= 520 and <= 560)
            page = GuidePage.Treatment;
        else if (page == GuidePage.Hints && y is >= 240 and <= 284)
        {
            string mode = x < 275 ? "quiet" : x < 545 ? "occasional" : "frequent";
            system.SendBrambleChoice(BrambleRequestPacket.SetHintMode, mode);
            return;
        }
        FoxGuiTheme.PlayNavigation(api);
        Redraw();
    }

    private bool HandleFooter(double x, double y)
    {
        if (y < CanvasHeight - 48 || y > CanvasHeight - 18) return false;
        if (x <= 180)
        {
            page = page == GuidePage.Topic ? GuidePage.Topics : GuidePage.Home;
            FoxGuiTheme.PlayNavigation(api);
            Redraw();
        }
        else if (x >= CanvasWidth - 160) close();
        return true;
    }

    private void DrawFooter(Context ctx, int width, int height, bool home)
    {
        if (!home) DrawButton(ctx, "Back", 30, height - 48, 120, 30, true);
        DrawButton(ctx, "Close", width - 150, height - 48, 120, 30, true);
    }

    private static void DrawStep(Context ctx, string number, string text, double x, double y)
    {
        FoxGuiTheme.DrawPanel(ctx, x, y, 44, 44, true);
        DrawText(ctx, number, x + 16, y + 29, 17, TextGold);
        DrawWrapped(ctx, text, x + 62, y + 27, 680, 18, TextWhite, 2);
    }

    private static void DrawButton(Context ctx, string label, double x, double y, double width, double height, bool enabled)
    {
        FoxGuiTheme.DrawButtonSurface(ctx, x, y, width, height, enabled);
        DrawText(ctx, label, x + 14, y + height / 2 + 6, 15, enabled ? TextWhite : TextMuted);
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

    private static void DrawWrapped(Context ctx, string text, double x, double baseline, double maxWidth, double lineHeight, double[] color, int maxLines)
    {
        string[] words = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string line = string.Empty;
        int drawn = 0;
        foreach (string word in words)
        {
            if (drawn >= maxLines) break;
            string candidate = string.IsNullOrEmpty(line) ? word : line + " " + word;
            if (FeralKinshipCompanionUiSettings.GetTextWidth(candidate, 14) <= maxWidth) { line = candidate; continue; }
            if (!string.IsNullOrEmpty(line))
            {
                DrawText(ctx, line, x, baseline + drawn * lineHeight * FeralKinshipCompanionUiSettings.TextScale, 14, color);
                drawn++;
            }
            line = word;
        }
        if (drawn < maxLines && !string.IsNullOrEmpty(line)) DrawText(ctx, line, x, baseline + drawn * lineHeight * FeralKinshipCompanionUiSettings.TextScale, 14, color);
    }
}
