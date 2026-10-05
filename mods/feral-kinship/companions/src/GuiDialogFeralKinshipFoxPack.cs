#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Cairo;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace FeralKinshipCompanions;

public sealed class GuiDialogFeralKinshipFoxPack : GuiDialog
{
    private const double DialogWidth = 1180;
    private const double DialogHeight = 860;
    private const double Padding = 0;

    private readonly FeralKinshipCompanionSystem system;
    private readonly long sourceEntityId;
    private GuiElementFeralKinshipFoxPackSurface surface;
    private FoxPackStatePacket state;

    public GuiDialogFeralKinshipFoxPack(
        ICoreClientAPI capi,
        FeralKinshipCompanionSystem system,
        long sourceEntityId)
        : base(capi)
    {
        this.system = system;
        this.sourceEntityId = sourceEntityId;
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
        system.SendFoxSocialAction(sourceEntityId, FoxSocialRequestAction.ClosePack);
        base.OnGuiClosed();
    }

    public void ApplyState(FoxPackStatePacket packet)
    {
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
            .CreateCompo("feralkinship-fox-pack", dialogBounds)
            .AddShadedDialogBG(contentBounds)
            .AddDialogTitleBar("Feral Kinship Companion Pack", () => TryClose())
            .BeginChildElements(contentBounds);

        surface = new GuiElementFeralKinshipFoxPackSurface(
            capi,
            ElementBounds.Fixed(0, 0, DialogWidth, DialogHeight),
            system,
            sourceEntityId
        );
        composer.AddInteractiveElement(surface, "pack-surface");
        SingleComposer = composer.Compose();
    }
}

internal sealed partial class GuiElementFeralKinshipFoxPackSurface : GuiElementFeralKinshipScaledSurface
{
    protected override double DesignWidth => 1180;
    protected override double DesignHeight => 860;
    private const string RosterTab = "roster";
    private const string ExpeditionsTab = "expeditions";
    private const string ScavengeTab = "scavenge";
    private const string RoutesTab = "routes";
    private const string PackTalentsTab = "packtalents";
    private const string LastTripTab = "lasttrip";
    private const string CacheTab = "cache";
    private const string ArchivedTab = "archived";

    private const double TabY = 56;
    private const double TabHeight = 36;
    private const double CompanionTabX = 904;
    private const double CompanionTabWidth = 256;
    private const double ContentY = 150;
    private const double FooterY = 804;
    private const double RosterRowHeight = 128;
    private const double RosterViewportHeight = 586;
    private const double PartyRowHeight = 37;
    private static readonly (string Label, string Id, double X, double Width)[] CartTabs =
    {
        ("Roster", RosterTab, 18, 184),
        ("Expeditions", ExpeditionsTab, 210, 184),
        ("Pack talents", PackTalentsTab, 402, 184),
        ("Pack cache", CacheTab, 594, 184),
        ("Archived", ArchivedTab, 786, 184),
        ("Reports", LastTripTab, 978, 184)
    };

    private readonly FeralKinshipCompanionSystem system;
    private readonly long sourceEntityId;
    private readonly bool cartAccess;
    private bool redrawPending = true;
    private long nextTimerRefreshMs;
    private readonly CompanionAnimalArt animalArt;
    private readonly CompanionSceneArt sceneArt;
    private readonly HashSet<string> selectedFoxIds = new(StringComparer.Ordinal);
    private FoxPackStatePacket state;
    private readonly List<DummySlot> cacheItemSlots = new();
    private readonly HashSet<string> selectedCacheKeys = new(StringComparer.Ordinal);
    private string selectedCacheKey = string.Empty;
    private string activeTab = RosterTab;
    private string activePackTalentGroup = PackTalentCatalog.Groups[0].Id;
    private string selectedPackTalentId = string.Empty;
    private string hoveredPackTalentId = string.Empty;
    private string hoveredPackTalentGroupId = string.Empty;
    private string selectedExpeditionType = string.Empty;
    private string expeditionCategory = "hunt";
    private bool expeditionPartyPlannerOpen;
    private bool scavengeRuinRoutes;
    private long selectedScavengeSiteId;
    private string selectedScavengeFocus = FoxScavengeSites.Useful;
    private string selectedScoutDuration = FoxScavengeSites.ShortScout;
    private bool scavengeIntelOpen;
    private int scavengeIntelPage;
    private string selectedMissingFoxId = string.Empty;
    private string purchaseRouteId = string.Empty;
    private string selectedRouteId = string.Empty;
    private bool prepareSelected;
    private bool guideOpen;
    private bool guideDismissalNotified;
    private double rosterScrollOffset;
    private string rosterFilter = "All";
    private string selectedRosterFoxId = string.Empty;
    private string archivedFilter = "All";
    private string selectedArchivedFoxId = string.Empty;
    private double archivedScrollOffset;
    private double expeditionScrollOffset;
    private double activeExpeditionScrollOffset;
    private double cacheScrollOffset;
    private double lastTripScrollOffset;
    private double reportListScrollOffset;
    private long selectedReportId;
    private long confirmDeleteReportId;
    private bool lastTripDetailsOpen;
    private long expeditionSnapshotReceivedMs;
    private int lastRenderedRemainingSecond = -1;
    private bool helpOpen;
    private bool helpHovered;
    private int textureId;

    public GuiElementFeralKinshipFoxPackSurface(
        ICoreClientAPI capi,
        ElementBounds bounds,
        FeralKinshipCompanionSystem system,
        long sourceEntityId)
        : base(capi, bounds)
    {
        this.system = system;
        this.sourceEntityId = sourceEntityId;
        animalArt = new CompanionAnimalArt(capi);
        sceneArt = new CompanionSceneArt(capi);
        cartAccess = sourceEntityId == 0;
        guideOpen = cartAccess
            ? system.ShouldShowCompanionPackCartGuide()
            : system.ShouldShowCompanionPackGuide();
    }

    public void NotifyGuideClosed()
    {
        if (guideOpen)
        {
            DismissGuide();
        }
    }

    public void ApplyState(FoxPackStatePacket packet)
    {
        state = packet;
        cacheItemSlots.Clear();
        foreach (FoxPackLootItemPacket item in packet?.LootItems ?? new List<FoxPackLootItemPacket>())
        {
            try
            {
                ItemStack stack = item.StackBytes?.Length > 0 ? new ItemStack(item.StackBytes) : null;
                cacheItemSlots.Add(stack != null && stack.ResolveBlockOrItem(api.World)
                    ? new DummySlot(stack) : null);
            }
            catch
            {
                cacheItemSlots.Add(null);
            }
        }
        HashSet<string> currentCacheKeys = (packet?.LootItems ?? new List<FoxPackLootItemPacket>())
            .Select((item, index) => CacheSelectionKey(index, item.StackBytes))
            .ToHashSet(StringComparer.Ordinal);
        selectedCacheKeys.RemoveWhere(key => !currentCacheKeys.Contains(key));
        if (!currentCacheKeys.Contains(selectedCacheKey)) selectedCacheKey = string.Empty;
        expeditionSnapshotReceivedMs = api.ElapsedMilliseconds;
        lastRenderedRemainingSecond = -1;
        if (packet?.Members != null)
        {
            selectedFoxIds.RemoveWhere(foxId => !packet.Members.Any(member =>
                string.Equals(member.FoxId, foxId, StringComparison.Ordinal)
                && IsExpeditionEligible(member)));
            if (!packet.Members.Any(member =>
                    string.Equals(member.FoxId, selectedMissingFoxId, StringComparison.Ordinal)
                    && IsRescueTarget(member))
                || IsSearchTargetActive(packet, selectedMissingFoxId))
            {
                selectedMissingFoxId = string.Empty;
            }
        }
        List<FoxExpeditionSummaryPacket> reports = GetExpeditionReports(packet);
        if (reports.Count == 0)
        {
            selectedReportId = 0;
            confirmDeleteReportId = 0;
        }
        else if (selectedReportId == 0 || reports.All(report => report.ExpeditionId != selectedReportId))
        {
            selectedReportId = reports[0].ExpeditionId;
        }
        if (confirmDeleteReportId != 0 && reports.All(report => report.ExpeditionId != confirmDeleteReportId))
        {
            confirmDeleteReportId = 0;
        }
        ClampScrollOffsets();
        Redraw();
    }

    public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
    {
        Bounds.CalcWorldBounds();
        RenderSurface();
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

        if (api.ElapsedMilliseconds >= nextTimerRefreshMs)
        {
            nextTimerRefreshMs = api.ElapsedMilliseconds + 100;
            List<FoxActiveExpeditionPacket> activeExpeditions = GetActiveExpeditions(state);
            int remainingUnit = activeExpeditions.Aggregate(17, (hash, active) => unchecked(
                hash * 31 + (int)Math.Ceiling(GetDisplayedExpeditionRemainingHours(active) * 60d)));
            foreach (FoxPackMemberPacket member in state?.Members ?? new List<FoxPackMemberPacket>())
            {
                if (!member.BackpackDeliveryActive
                    || !string.Equals(member.BackpackDeliveryPhase, "wait-return", StringComparison.Ordinal)
                    || !member.BackpackReturnScheduled) continue;
                remainingUnit = unchecked(
                    remainingUnit * 31 + (int)Math.Ceiling(GetDisplayedBackpackReturnSeconds(member)));
            }
            if (remainingUnit != lastRenderedRemainingSecond)
            {
                lastRenderedRemainingSecond = remainingUnit;
                Redraw();
            }
        }
        if (redrawPending) RenderSurface();
        if (textureId > 0)
        {
            Render2DTexture(textureId, Bounds);
        }
        if (activeTab == CacheTab && !guideOpen && !helpOpen && string.IsNullOrWhiteSpace(purchaseRouteId))
        {
            double top = ContentY + 158;
            for (int index = 0; index < cacheItemSlots.Count; index++)
            {
                if (cacheItemSlots[index] == null) continue;
                double itemY = top + index * 58 - cacheScrollOffset;
                if (itemY < ContentY + 158 || itemY > ContentY + 545) continue;
                api.Render.RenderItemstackToGui(cacheItemSlots[index],
                    Bounds.renderX + 330 * CanvasScaleX,
                    Bounds.renderY + (itemY + 29) * CanvasScaleY,
                    460, (float)(42 * Math.Min(CanvasScaleX, CanvasScaleY)), -1,
                    showStackSize: false);
            }
            int detailIndex = FindSelectedCacheIndex(state);
            if (detailIndex >= 0 && detailIndex < cacheItemSlots.Count && cacheItemSlots[detailIndex] != null)
                api.Render.RenderItemstackToGui(cacheItemSlots[detailIndex],
                    Bounds.renderX + 1018 * CanvasScaleX,
                    Bounds.renderY + (ContentY + 213) * CanvasScaleY,
                    460, (float)(92 * Math.Min(CanvasScaleX, CanvasScaleY)), -1,
                    showStackSize: false);
        }
    }

    public override void Dispose()
    {
        animalArt.Dispose();
        sceneArt.Dispose();
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
        if (!string.Equals(activeTab, PackTalentsTab, StringComparison.Ordinal))
        {
            if (!string.IsNullOrEmpty(hoveredPackTalentId)
                || !string.IsNullOrEmpty(hoveredPackTalentGroupId))
            {
                hoveredPackTalentId = string.Empty;
                hoveredPackTalentGroupId = string.Empty;
                Redraw();
            }
            return;
        }

        double mouseX = CanvasMouseX(args.X);
        double mouseY = CanvasMouseY(args.Y);
        PackTalentDefinition hovered = FindHoveredPackTalent(mouseX, mouseY, (int)CanvasWidth);
        string hoveredId = hovered?.Id ?? string.Empty;
        string hoveredGroupId = FindHoveredPackTalentGroup(mouseX, mouseY, (int)CanvasWidth)?.Id ?? string.Empty;
        if (string.Equals(hoveredId, hoveredPackTalentId, StringComparison.Ordinal)
            && string.Equals(hoveredGroupId, hoveredPackTalentGroupId, StringComparison.Ordinal))
        {
            return;
        }

        hoveredPackTalentId = hoveredId;
        hoveredPackTalentGroupId = hoveredGroupId;
        Redraw();
    }

    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        double amount = args.deltaPrecise != 0f ? args.deltaPrecise : args.delta;
        if (Math.Abs(amount) < 0.01)
        {
            return;
        }

        if (activeTab == RosterTab)
        {
            rosterScrollOffset = Math.Max(0d, rosterScrollOffset - amount * 48d);
        }
        else if (activeTab == ArchivedTab)
        {
            archivedScrollOffset = Math.Max(0d, archivedScrollOffset - amount * 48d);
        }
        else if (activeTab == ExpeditionsTab)
        {
            expeditionScrollOffset = Math.Max(0d, expeditionScrollOffset - amount * 48d);
        }
        else if (activeTab == ScavengeTab)
        {
            if (scavengePage == "sites" || CanvasMouseX(api.Input.MouseX) > 396
                || CanvasMouseY(api.Input.MouseY) < ContentY + 151
                || CanvasMouseY(api.Input.MouseY) > ContentY + 487) return;
            scavengePartyScrollOffset = Math.Max(0d,
                scavengePartyScrollOffset - amount * 36d);
        }
        else if (activeTab == CacheTab)
        {
            cacheScrollOffset = Math.Max(0d, cacheScrollOffset - amount * 48d);
        }
        else if (activeTab == PackTalentsTab)
        {
            packTalentScrollOffset = Math.Max(0d, packTalentScrollOffset - amount * 48d);
        }
        else if (activeTab == LastTripTab)
        {
            if (lastTripDetailsOpen) lastTripScrollOffset = Math.Max(0d, lastTripScrollOffset - amount * 48d);
            else reportListScrollOffset = Math.Max(0d, reportListScrollOffset - amount * 48d);
        }
        else
        {
            return;
        }

        ClampScrollOffsets();
        Redraw();
        args.SetHandled();
    }

    private void Redraw()
    {
        // Packets, hover changes and countdowns may all arrive before one frame.
        redrawPending = true;
    }

    private void RenderSurface()
    {
        redrawPending = false;
        Bounds.CalcWorldBounds();
        int width = Math.Max(1, Bounds.OuterWidthInt);
        int height = Math.Max(1, Bounds.OuterHeightInt);
        ImageSurface surface = new ImageSurface(Format.Argb32, width, height);
        Context ctx = new Context(surface);
        try
        {
            ScaleCanvas(ctx);
            Draw(ctx, (int)CanvasWidth, (int)CanvasHeight);
            generateTexture(surface, ref textureId);
        }
        finally
        {
            ctx.Dispose();
            surface.Dispose();
        }
    }

    private void Draw(Context ctx, int width, int height)
    {
        FoxGuiTheme.DrawJournal(ctx, width, height, FoxGuiSurfaceKind.Pack);

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

        FoxPackStatePacket current = state;
        int activeCount = current?.PackSize ?? 0;
        int archivedCount = current?.ArchivedMembers?.Count ?? 0;
        int packPoints = current?.PackPoints ?? 0;
        int activeExpeditions = GetActiveExpeditions(current).Count;
        DrawText(ctx, $"Pack members: {activeCount}   |   Pack points: {packPoints}   |   Active expeditions: {activeExpeditions}/{Math.Max(1, current?.ExpeditionCapacity ?? 1)}", 14, 28, 24, TextWhite);
        FoxGuiTheme.DrawSectionRule(ctx, 14, 39, width - 84);

        if (cartAccess)
        {
            foreach (var tab in CartTabs)
                DrawTab(ctx, tab.Label, tab.Id, tab.X, TabY, tab.Width);
            if (activeTab is ExpeditionsTab or ScavengeTab or RoutesTab)
            {
                DrawChoiceButton(ctx, "Available expeditions", 18, 102, 370, 34, true, activeTab == ExpeditionsTab);
                DrawChoiceButton(ctx, "Scavenging", 396, 102, 370, 34, true, activeTab == ScavengeTab);
                DrawChoiceButton(ctx, "Unlock new expeditions", 774, 102, 388, 34, true, activeTab == RoutesTab);
            }
        }
        else
        {
            DrawTab(ctx, "Roster", RosterTab, 18, TabY, 282);
            DrawTab(ctx, "Pack cache", CacheTab, 310, TabY, 282);
            DrawTab(ctx, "Archived", ArchivedTab, 602, TabY, 282);
            DrawChoiceButton(ctx, "Back to overview", CompanionTabX, TabY, CompanionTabWidth, TabHeight, true, false);
        }

        if (current == null)
        {
            FoxGuiTheme.DrawPanel(ctx, 14, ContentY + 4, width - 38, 82, false);
            DrawText(ctx, "Waiting for pack data...", 28, ContentY + 32, 18, TextWhite);
            DrawWrapped(ctx,
                "The pack cart or a companion may be outside interaction range, or still answering. Move closer and give it a moment.",
                28,
                ContentY + 57,
                width - 66,
                15,
                TextMuted,
                2,
                13);
            FoxGuiTheme.DrawWindowControls(ctx, api, width);
            FoxGuiTheme.DrawHelpGlyph(ctx, width, helpHovered);
            return;
        }

        switch (activeTab)
        {
            case ExpeditionsTab:
                if (expeditionPartyPlannerOpen) DrawExpeditionPartyPlanner(ctx, current, width);
                else DrawExpeditionBrowser(ctx, current, width);
                break;
            case ScavengeTab:
                DrawScavengeMenu(ctx, current, width);
                break;
            case RoutesTab:
                DrawRoutesOverview(ctx, current, width);
                break;
            case PackTalentsTab:
                DrawPackTalentsOverview(ctx, current, width);
                break;
            case LastTripTab:
                DrawReportsOverview(ctx, current, width);
                break;
            case CacheTab:
                DrawCacheOverview(ctx, current, width);
                break;
            case ArchivedTab:
                DrawArchiveOverview(ctx, current, width);
                break;
            default:
                DrawRosterOverview(ctx, current, width);
                break;
        }

        if (!string.IsNullOrWhiteSpace(current.Message))
        {
            FoxGuiTheme.DrawPanel(ctx, 10, height - 48, width - 30, 40, true);
            DrawWrapped(ctx, current.Message, 20, height - 30,
                width - 50, 15, TextGold, 2, 13);
        }

        if (!string.IsNullOrWhiteSpace(purchaseRouteId))
        {
            DrawRoutePurchaseOverlay(ctx, width, height, current);
        }

        FoxGuiTheme.DrawWindowControls(ctx, api, width);
        FoxGuiTheme.DrawHelpGlyph(ctx, width, helpHovered);
    }

    private void DrawRoster(Context ctx, FoxPackStatePacket current, int width)
    {
        DrawText(ctx, "Active roster", 14, ContentY, 18, TextGold);
        DrawText(ctx, "Health, temperament, points, cargo, and current availability at a glance.", 150, ContentY, 14, TextMuted);
        List<FoxPackMemberPacket> members = current.Members ?? new List<FoxPackMemberPacket>();
        Dictionary<string, string> labels = BuildMemberLabels(members);
        double y = ContentY + 14 - rosterScrollOffset;
        foreach (FoxPackMemberPacket member in members)
        {
            DrawMemberRow(ctx, member, labels[member.FoxId], 12, y, width - 32, canArchive: true);
            y += RosterRowHeight;
        }
        if (members.Count * RosterRowHeight > RosterViewportHeight)
        {
            DrawScrollbar(ctx, width - 18, ContentY + 18, members.Count * RosterRowHeight, rosterScrollOffset, RosterViewportHeight);
        }
        if (members.Count == 0)
        {
            DrawRect(ctx, 14, ContentY + 24, width - 38, 84, TextPanel);
            DrawText(ctx, "This pack has no active companions yet.", 28, ContentY + 57, 17, TextWhite);
            DrawText(ctx, "Tamed adult animals appear here after joining your companion pack.", 28, ContentY + 81, 13, TextMuted);
        }
    }

    private void DrawArchived(Context ctx, FoxPackStatePacket current, int width)
    {
        DrawText(ctx, "Archived records", 14, ContentY, 18, TextGold);
        DrawText(ctx, "Names, points, and history kept after death.", 180, ContentY, 14, TextMuted);
        List<FoxPackMemberPacket> members = current.ArchivedMembers ?? new List<FoxPackMemberPacket>();
        Dictionary<string, string> labels = BuildMemberLabels(members);
        double y = ContentY + 14 - archivedScrollOffset;
        foreach (FoxPackMemberPacket member in members)
        {
            DrawMemberRow(ctx, member, labels[member.FoxId], 12, y, width - 32, canArchive: false, canUnarchive: member.EntityLoaded);
            y += RosterRowHeight;
        }
        if (members.Count * RosterRowHeight > RosterViewportHeight)
        {
            DrawScrollbar(ctx, width - 18, ContentY + 18, members.Count * RosterRowHeight, archivedScrollOffset, RosterViewportHeight);
        }
        if (members.Count == 0)
        {
            DrawRect(ctx, 14, ContentY + 24, width - 38, 84, TextPanel);
            DrawText(ctx, "No companion records have been archived.", 28, ContentY + 57, 17, TextWhite);
            DrawText(ctx, "A dead companion remains on the active roster until you archive it.", 28, ContentY + 81, 13, TextMuted);
        }
    }

    private static Dictionary<string, string> BuildMemberLabels(IEnumerable<FoxPackMemberPacket> members)
    {
        List<FoxPackMemberPacket> list = members?.ToList() ?? new List<FoxPackMemberPacket>();
        Dictionary<string, int> totals = list
            .GroupBy(member => CompanionDisplayName(member.Name), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Dictionary<string, int> seen = new(StringComparer.Ordinal);
        Dictionary<string, string> labels = new(StringComparer.Ordinal);
        foreach (FoxPackMemberPacket member in list)
        {
            string baseName = CompanionDisplayName(member.Name);
            int ordinal = seen.TryGetValue(baseName, out int prior) ? prior + 1 : 1;
            seen[baseName] = ordinal;
            labels[member.FoxId] = totals[baseName] > 1 ? $"{baseName} #{ordinal}" : baseName;
        }
        return labels;
    }

    private static Dictionary<string, string> BuildSummaryLabels(IEnumerable<FoxExpeditionMemberSummaryPacket> members)
    {
        List<FoxExpeditionMemberSummaryPacket> list = members?.ToList()
            ?? new List<FoxExpeditionMemberSummaryPacket>();
        Dictionary<string, int> totals = list
            .GroupBy(member => CompanionDisplayName(member.Name), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Dictionary<string, int> seen = new(StringComparer.Ordinal);
        Dictionary<string, string> labels = new(StringComparer.Ordinal);
        foreach (FoxExpeditionMemberSummaryPacket member in list)
        {
            string baseName = CompanionDisplayName(member.Name);
            int ordinal = seen.TryGetValue(baseName, out int prior) ? prior + 1 : 1;
            seen[baseName] = ordinal;
            labels[member.FoxId] = totals[baseName] > 1 ? $"{baseName} #{ordinal}" : baseName;
        }
        return labels;
    }

    private static string CompanionDisplayName(string name) =>
        string.IsNullOrWhiteSpace(name) ? "Unnamed companion" : name.Trim();

    private void DrawMemberRow(Context ctx, FoxPackMemberPacket member, string displayName, double x, double y, double rowWidth, bool canArchive, bool canUnarchive = false)
    {
        if (y + RosterRowHeight < ContentY + 8 || y > FooterY - 8)
        {
            return;
        }

        bool dead = string.Equals(member.Status, "Dead", StringComparison.OrdinalIgnoreCase);
        bool archived = string.Equals(member.Status, "Archived", StringComparison.OrdinalIgnoreCase);
        double[] fill = dead ? TextRedPanel : archived ? TextMutedPanel : TextPanel;
        DrawRect(ctx, x, y, rowWidth, RosterRowHeight - 5, fill);
        DrawRect(ctx, x + 8, y + 12, 10, 10, dead ? TextRed : StatusColor(member.Status));

        DrawText(ctx, Trim(displayName, 36), x + 28, y + 20, 17, TextWhite);
        string displayedStatus = string.Equals(member.Status, "Dead", StringComparison.OrdinalIgnoreCase)
            ? member.IsJuvenile ? "Dead — child" : FriendlyStatus(member.Status)
            : !IsOrdinaryRosterStatus(member.Status)
                ? FriendlyStatus(member.Status) + (member.IsJuvenile ? " — child" : string.Empty)
            : member.IsJuvenile
                ? Lang.Get("feralkinshipcompanions:child-stage")
            : member.PregnancyActive
                ? Lang.Get("feralkinshipcompanions:pack-pregnant-status", (int)(member.ExpeditionStrengthFactor * 100f))
            : !string.IsNullOrWhiteSpace(member.CarriedItem)
            ? $"Carrying {member.CarriedItem}"
            : member.WaitingForCartCargo ? "Collecting cart cargo" : FriendlyStatus(member.Status);
        DrawText(ctx, Trim(displayedStatus, 32), x + rowWidth - 225, y + 20, 14, dead ? TextRed : TextGold);

        string health = member.MaxHealth > 0f
            ? $"Health {member.CurrentHealth:0.#}/{member.MaxHealth:0.#}"
            : "Health unknown";
        DrawText(ctx, $"{health}   |   Mood: {Trim(member.Mood, 18)}   |   Personality: {Trim(member.Personality, 18)}", x + 28, y + 45, 14, TextWhite);
        string progression = member.IsJuvenile
            ? $"Child · adulthood EXP pending · Lifetime EXP {member.LifetimeExperience}"
            : $"Level {member.Level} · {member.CurrentLevelExperience}/{member.RequiredLevelExperience} EXP · Lifetime {member.LifetimeExperience}";
        DrawText(ctx, $"{progression}   |   Requests {member.RequestsCompleted}/{member.RequestsGenerated}   |   Points {member.Points}/{member.LifetimePoints}", x + 28, y + 67, 14, TextMuted);
        DrawText(ctx, Trim(FormatBackpackRosterStatus(member), 112), x + 28, y + 89, 13,
            member.BackpackDeliveryActive ? TextGold : TextMuted);
        DrawText(ctx,
            Trim(string.IsNullOrWhiteSpace(member.BackpackLastDeliverySummary)
                ? "Last trip: none yet."
                : member.BackpackLastDeliverySummary, 112),
            x + 28, y + 110, 13, TextMuted);

        bool archivable = canArchive && IsArchivableStatus(member.Status);
        bool unloadedWithLocation = archivable
            && string.Equals(member.Status, "Not currently loaded", StringComparison.OrdinalIgnoreCase)
            && member.HasLastKnownPosition;
        if (unloadedWithLocation)
        {
            DrawButton(ctx, "Archive", x + rowWidth - 210, y + 34, 72, 27, true);
            DrawButton(ctx, "Locate", x + rowWidth - 130, y + 34, 112, 27, true);
        }
        else if (archivable)
        {
            DrawButton(ctx, "Archive", x + rowWidth - 130, y + 34, 112, 27, true);
        }
        else if (canUnarchive)
        {
            DrawButton(ctx, "Restore", x + rowWidth - 130, y + 34, 112, 27, true);
        }
    }

    private string FormatBackpackRosterStatus(FoxPackMemberPacket member)
    {
        if (!member.BackpackEquipped)
        {
            return "Backpack: none equipped";
        }

        string name = string.IsNullOrWhiteSpace(member.BackpackName)
            ? "equipped bag"
            : Trim(member.BackpackName, 32);
        string result = $"Backpack: {name} · {member.BackpackOccupiedSlots}/{member.BackpackTotalSlots} slots used";
        if (!member.BackpackDeliveryActive) return result + " · Ready";

        string phase = member.BackpackDeliveryPhase switch
        {
            "home" => "Heading home",
            "unload" => "Unloading at camp",
            "wait-return" when !member.BackpackReturnScheduled => "Waiting for owner before return",
            "wait-return" when GetDisplayedBackpackReturnSeconds(member) > 0f =>
                $"Returning in {FormatBackpackCountdown(GetDisplayedBackpackReturnSeconds(member))}",
            "wait-return" => "Waiting for a safe return",
            _ => "Delivery in progress"
        };
        return result + " · " + phase;
    }

    private void DrawExpeditions(Context ctx, FoxPackStatePacket current, int width)
    {
        DrawText(ctx, "Expedition party", 14, ContentY, 18, TextGold);
        DrawText(ctx, "Select the party on the left; plan and depart from the cart on the right.", 174, ContentY, 14, TextMuted);

        double leftWidth = 520;
        List<FoxPackMemberPacket> members = current.Members ?? new List<FoxPackMemberPacket>();
        Dictionary<string, string> labels = BuildMemberLabels(members);
        double partyY = ContentY + 24 - expeditionScrollOffset;
        for (int index = 0; index < members.Count; index++)
        {
            int column = index % 2;
            int row = index / 2;
            double x = 12 + column * 267;
            double y = partyY + row * PartyRowHeight;
            DrawPartyRow(ctx, members[index], labels[members[index].FoxId], x, y, 252);
        }
        if (members.Count > 28)
        {
            DrawScrollbar(ctx, leftWidth - 18, ContentY + 24, Math.Ceiling(members.Count / 2d) * PartyRowHeight, expeditionScrollOffset);
        }

        double rightX = leftWidth + 12;
        double rightWidth = width - rightX - 14;
        DrawText(ctx, "Expedition type", rightX, ContentY + 28, 14, TextGold);
        if (expeditionCategory == "scavenge")
        {
            DrawChoiceButton(ctx, "Sites", rightX + rightWidth - 206, ContentY + 8,
                98, 24, true, !scavengeRuinRoutes);
            DrawChoiceButton(ctx, "Ruin routes", rightX + rightWidth - 102, ContentY + 8,
                102, 24, true, scavengeRuinRoutes);
        }
        HashSet<string> unlocked = (current.UnlockedExpeditionTypes ?? new List<string>())
            .ToHashSet(StringComparer.Ordinal);
        string[] categories = { "hunt", "forage", "utility" };
        double categoryWidth = (rightWidth - 12) / 3d;
        for (int index = 0; index < categories.Length; index++)
        {
            string category = categories[index];
            DrawChoiceButton(ctx, char.ToUpperInvariant(category[0]) + category[1..],
                rightX + index * (categoryWidth + 6), ContentY + 38, categoryWidth, 29,
                true, expeditionCategory == category);
        }
        List<FoxExpeditionDefinition> visibleMissions = GetCategoryMissions();
        for (int index = 0; index < visibleMissions.Count; index++)
        {
            FoxExpeditionDefinition mission = visibleMissions[index];
            int column = index % 3;
            int row = index / 3;
            double buttonWidth = (rightWidth - 12) / 3d;
            double x = rightX + column * (buttonWidth + 6);
            double y = ContentY + 76 + row * 35;
            bool isUnlocked = mission.DefaultUnlocked || unlocked.Contains(mission.Id);
            if (isUnlocked)
            {
                DrawChoiceButton(
                    ctx,
                    mission.Name,
                    x,
                    y,
                    buttonWidth,
                    29,
                    true,
                    string.Equals(selectedExpeditionType, mission.Id, StringComparison.Ordinal)
                );
            }
            else
            {
                DrawLockedMissionButton(ctx, mission.Name, x, y, buttonWidth, 29);
            }
        }
        if (expeditionCategory == "scavenge" && !scavengeRuinRoutes)
            DrawScavengeControls(ctx, current, rightX, rightWidth);

        float selectedStrength = GetSelectedStrength(members);
        float recruitmentChanceBonus = GetSelectedRecruitmentChanceBonus(members);
        FoxExpeditionDefinition selectedDefinition = FoxExpeditionCatalog.Get(selectedExpeditionType);
        float target = selectedDefinition?.TargetStrength ?? 0f;
        List<FoxActiveExpeditionPacket> activeExpeditions = GetActiveExpeditions(current);
        int expeditionCapacity = Math.Max(1, current.ExpeditionCapacity);
        bool slotsFull = activeExpeditions.Count >= expeditionCapacity;
        bool hasLoot = current.LootItems != null && current.LootItems.Count > 0;
        bool hasPendingReward = current.RecruitmentReady;
        bool routeUnlocked = selectedDefinition != null
            && (selectedDefinition.DefaultUnlocked || unlocked.Contains(selectedDefinition.Id));
        bool prerequisitesMet = selectedDefinition != null
            && AreRoutePrerequisitesMet(selectedDefinition, unlocked);
        bool hasMia = members.Any(IsRescueTarget);
        bool hasSelectedMia = members.Any(member =>
            string.Equals(member.FoxId, selectedMissingFoxId, StringComparison.Ordinal)
            && IsRescueTarget(member))
            && !IsSearchTargetActive(current, selectedMissingFoxId);
        int selectedCount = members.Count(member =>
            selectedFoxIds.Contains(member.FoxId) && IsExpeditionEligible(member));
        int preparationCost = selectedDefinition?.PreparationCost ?? 0;
        bool canAffordPreparation = !prepareSelected || current.PackPoints >= preparationCost;
        bool canStart = routeUnlocked
            && !slotsFull && !hasLoot && !hasPendingReward
            && selectedDefinition != null
            && selectedCount >= selectedDefinition.MinimumFoxes
            && selectedCount <= selectedDefinition.MaximumFoxes
            && prerequisitesMet
            && canAffordPreparation
            && (selectedExpeditionType != FoxExpeditionType.SearchLost || hasSelectedMia)
            && IsSelectedScavengeDestinationValid(current);
        int percent = target <= 0f ? 0 : (int)Math.Min(100f, selectedStrength / target * 100f);

        DrawRect(ctx, rightX, ContentY + 218, rightWidth, 113, TextPanel);
        if (selectedDefinition == null)
        {
            DrawText(ctx, "No expedition selected", rightX + 12, ContentY + 244, 16, TextWhite);
            DrawWrapped(ctx,
                "Choose a mission above to see its requirements, danger, travel time, and party limits.",
                rightX + 12, ContentY + 273, rightWidth - 24, 17, TextMuted, 3, 13);
        }
        else
        {
            DrawText(ctx, $"{selectedDefinition?.TierLabel ?? "Utility"}   |   Party {selectedCount}/{selectedDefinition?.MinimumFoxes ?? 0}–{selectedDefinition?.MaximumFoxes ?? 0}", rightX + 12, ContentY + 240, 15, selectedDefinition != null && selectedCount > selectedDefinition.MaximumFoxes ? TextRed : TextWhite);
            DrawText(ctx, $"Strength {selectedStrength:0.#}/{target:0.#} ({percent}% safe strength)", rightX + 12, ContentY + 261, 14, TextWhite);
            if (selectedExpeditionType == FoxExpeditionType.Recruitment)
            {
                int recruitmentChance = (int)Math.Round(Math.Clamp(
                    GetRecruitmentBaseChance(selectedStrength, target) + recruitmentChanceBonus,
                    0f,
                    1f
                ) * 100f);
                DrawText(ctx, $"Recruitment chance: {recruitmentChance}%   |   Travel {FormatExpeditionHours(selectedDefinition.BaseDurationHours)}", rightX + 12, ContentY + 282, 14, TextGold);
            }
            else if (selectedExpeditionType == FoxExpeditionType.SearchLost)
            {
                FoxPackMemberPacket missing = members.FirstOrDefault(member =>
                    string.Equals(member.FoxId, selectedMissingFoxId, StringComparison.Ordinal)
                    && IsRescueTarget(member));
                DrawText(ctx,
                    missing == null
                        ? hasMia ? "Target: select a recoverable companion on the left." : "Target: no recoverable companion is recorded."
                        : $"Target: {Trim(labels[missing.FoxId], 18)}   |   Travel {FormatExpeditionHours(selectedDefinition.BaseDurationHours)}",
                    rightX + 12,
                    ContentY + 282,
                    13,
                    missing == null ? TextMuted : TextRed);
            }
            else if (selectedExpeditionType == FoxExpeditionType.Scout)
            {
                FoxScavengeSitePacket site = SelectedScavengeSite(current);
                DrawWrapped(ctx,
                    $"{(site == null ? "Scout for a new site" : "Rescout " + site.Label)} · {selectedScoutDuration} ({FormatExpeditionHours(FoxScavengeSites.ScoutHours(selectedScoutDuration))}).",
                    rightX + 12, ContentY + 280, rightWidth - 24, 16, TextMuted, 3, 13);
            }
            else if (selectedExpeditionType == FoxExpeditionType.Scavenge && selectedScavengeSiteId > 0)
            {
                FoxScavengeSitePacket site = SelectedScavengeSite(current);
                DrawWrapped(ctx,
                    site == null ? "Select a discovered site." :
                        $"{site.Label} · {selectedScavengeFocus}. {string.Join(" ", (site.Clues ?? new List<string>()).TakeLast(1))} {site.LayoutHint} {site.DangerHint}",
                    rightX + 12, ContentY + 280, rightWidth - 24, 16, TextMuted, 3, 13);
            }
            else
            {
                DrawWrapped(ctx,
                    $"Travel {FormatExpeditionHours(selectedDefinition?.BaseDurationHours ?? 0f)}. {selectedDefinition?.RiskLabel ?? "Unknown"} danger. {selectedDefinition?.Description ?? string.Empty}",
                    rightX + 12, ContentY + 280, rightWidth - 24, 16, TextMuted, 3, 13);
            }
            DrawText(ctx,
                !IsSelectedScavengeDestinationValid(current)
                    ? selectedExpeditionType == FoxExpeditionType.Scout
                        ? "Scout new is full, or the selected site already has a party."
                        : "Select a site that is not barren or occupied."
                    : GetDepartureGuidance(
                    current,
                    selectedDefinition,
                    routeUnlocked,
                    prerequisitesMet,
                    selectedCount,
                    hasLoot,
                    hasPendingReward,
                    hasSelectedMia,
                    canAffordPreparation),
                rightX + 12,
                ContentY + 329,
                13,
                canStart ? TextMuted : TextGold);
        }

        string preparationUnit = preparationCost == 1 ? "pack point" : "pack points";
        string prepareLabel = selectedDefinition == null
            ? "Select a mission first"
            : prepareSelected
            ? $"Prepared: {preparationCost} {preparationUnit}, 20% safer"
            : $"Prepare expedition ({preparationCost} {preparationUnit})";
        DrawChoiceButton(ctx, prepareLabel, rightX, ContentY + 338, rightWidth, 29,
            selectedDefinition != null && !slotsFull && preparationCost > 0 && current.PackPoints >= preparationCost,
            prepareSelected);
        DrawButton(ctx, "Select healthy", rightX, ContentY + 374, 156, 31, selectedDefinition != null && !slotsFull && !hasLoot && !hasPendingReward);
        DrawButton(ctx, "Clear party", rightX + 164, ContentY + 374, 164, 31, selectedFoxIds.Count > 0);
        DrawButton(ctx, "Start expedition", rightX, ContentY + 412, 196, 34, canStart);
        double noteY = ContentY + 460;
        DrawText(ctx, $"Active expeditions: {activeExpeditions.Count}/{expeditionCapacity}", rightX, noteY, 15,
            slotsFull ? TextGold : TextWhite);
        double activeStartY = noteY + 12;
        const double activeRowHeight = 48d;
        const double activeViewportHeight = 116d;
        if (activeExpeditions.Count == 0)
        {
            DrawText(ctx, "No parties underway.", rightX, activeStartY + 28, 13, TextMuted);
        }
        for (int index = 0; index < activeExpeditions.Count; index++)
        {
            FoxActiveExpeditionPacket active = activeExpeditions[index];
            double activeY = activeStartY + index * activeRowHeight - activeExpeditionScrollOffset;
            if (activeY + activeRowHeight < activeStartY || activeY > activeStartY + activeViewportHeight) continue;
            DrawRect(ctx, rightX, activeY + 5, rightWidth, activeRowHeight - 6, TextPanel);
            DrawText(ctx,
                $"{ExpeditionName(active.Type)} — {active.SelectedFoxIds.Count} companion{(active.SelectedFoxIds.Count == 1 ? string.Empty : "s")}",
                rightX + 10, activeY + 23, 13, TextWhite);
            string remaining = active.RunningLate
                ? "Running late"
                : $"{FormatExpeditionHours(GetDisplayedExpeditionRemainingHours(active))} remaining";
            DrawText(ctx, $"{active.ProgressPercent:0}% safe strength · {remaining}",
                rightX + 10, activeY + 40, 11, active.RunningLate ? TextGold : TextMuted);
        }
        if (activeExpeditions.Count * activeRowHeight > activeViewportHeight)
        {
            DrawScrollbar(ctx, width - 18, activeStartY, activeExpeditions.Count * activeRowHeight,
                activeExpeditionScrollOffset, activeViewportHeight);
        }
        if (scavengeIntelOpen && expeditionCategory == "scavenge")
            DrawScavengeIntel(ctx, current, rightX, rightWidth);
    }

    private void DrawScavengeIntel(Context ctx, FoxPackStatePacket current, double rightX, double rightWidth)
    {
        FoxScavengeSitePacket site = SelectedScavengeSite(current);
        if (site == null) { scavengeIntelOpen = false; return; }
        double top = ContentY + 218;
        DrawRect(ctx, rightX, top, rightWidth, FooterY - top, TextPanel);
        DrawText(ctx, $"Field notes — {site.Label}", rightX + 12, top + 27, 17, TextGold);
        int pages = Math.Max(1, (int)Math.Ceiling((site.Clues?.Count ?? 0) / 5d));
        scavengeIntelPage = Math.Clamp(scavengeIntelPage, 0, pages - 1);
        DrawButton(ctx, "Previous", rightX + 12, top + 37, 96, 27, scavengeIntelPage > 0);
        DrawButton(ctx, $"{scavengeIntelPage + 1}/{pages}  Next", rightX + 114, top + 37, 102, 27,
            scavengeIntelPage < pages - 1);
        DrawButton(ctx, "Close", rightX + rightWidth - 94, top + 37, 82, 27, true);
        List<string> clues = site.Clues ?? new List<string>();
        for (int i = 0; i < 5; i++)
        {
            int clueIndex = scavengeIntelPage * 5 + i;
            if (clueIndex >= clues.Count) break;
            DrawWrapped(ctx, $"• {clues[clueIndex]}", rightX + 14, top + 82 + i * 40,
                rightWidth - 28, 15, TextWhite, 2, 12);
        }
        DrawWrapped(ctx, site.LayoutHint + " " + site.DangerHint + " " + site.ConditionHint,
            rightX + 14, top + 296, rightWidth - 28, 15, TextMuted, 5, 12);
    }

    private List<FoxExpeditionDefinition> GetCategoryMissions()
    {
        return FoxExpeditionCatalog.All.Where(mission => expeditionCategory switch
        {
            "hunt" => mission.Id is FoxExpeditionType.Hunt or FoxExpeditionType.GreatHunt
                or FoxExpeditionType.ApexHunt,
            "forage" => mission.Id is FoxExpeditionType.Forage or FoxExpeditionType.DistantForage
                or FoxExpeditionType.PrimevalReach,
            "scavenge" => scavengeRuinRoutes
                && mission.Id is FoxExpeditionType.RuinDelve or FoxExpeditionType.ResonantDepths,
            _ => mission.Id is not FoxExpeditionType.Hunt and not FoxExpeditionType.GreatHunt
                and not FoxExpeditionType.ApexHunt and not FoxExpeditionType.Forage
                and not FoxExpeditionType.DistantForage and not FoxExpeditionType.PrimevalReach
                and not FoxExpeditionType.Scout and not FoxExpeditionType.Scavenge
                and not FoxExpeditionType.RuinDelve and not FoxExpeditionType.ResonantDepths
        }).ToList();
    }

    private FoxScavengeSitePacket SelectedScavengeSite(FoxPackStatePacket current) =>
        current.ScavengeSites?.FirstOrDefault(site => site.SiteId == selectedScavengeSiteId);

    private bool IsSelectedScavengeDestinationValid(FoxPackStatePacket current)
    {
        if (selectedExpeditionType == FoxExpeditionType.Scout)
            return selectedScavengeSiteId == 0
                ? (current.ScavengeSites?.Count ?? 0) < FoxScavengeSites.MaximumRememberedSites
                : SelectedScavengeSite(current) is { Busy: false };
        if (selectedExpeditionType == FoxExpeditionType.Scavenge)
            return SelectedScavengeSite(current) is { Busy: false, Barren: false };
        return true;
    }

    private void DrawScavengeControls(Context ctx, FoxPackStatePacket current, double rightX, double rightWidth)
    {
        List<FoxScavengeSitePacket> sites = current.ScavengeSites ?? new List<FoxScavengeSitePacket>();
        double third = (rightWidth - 12) / 3d;
        for (int i = 0; i < 3; i++)
        {
            FoxScavengeSitePacket site = i < sites.Count ? sites[i] : null;
            DrawChoiceButton(ctx, site == null ? "No lead" :
                    Trim(site.Label, site.Barren || site.Busy ? 10 : 17)
                    + (site.Barren ? " · barren" : site.Busy ? " · away" : ""),
                rightX + i * (third + 6), ContentY + 76, third, 29,
                site != null, site != null && selectedScavengeSiteId == site.SiteId);
        }
        string[] durations = { FoxScavengeSites.ShortScout, FoxScavengeSites.MediumScout, FoxScavengeSites.LongScout };
        for (int i = 0; i < durations.Length; i++)
            DrawChoiceButton(ctx, $"{durations[i]} {FormatExpeditionHours(FoxScavengeSites.ScoutHours(durations[i]))}",
                rightX + i * (third + 6), ContentY + 110, third, 28,
                true, selectedScoutDuration == durations[i]);
        DrawChoiceButton(ctx, "Scout new", rightX, ContentY + 143, third, 29,
            sites.Count < FoxScavengeSites.MaximumRememberedSites,
            selectedExpeditionType == FoxExpeditionType.Scout && selectedScavengeSiteId == 0);
        FoxScavengeSitePacket selected = SelectedScavengeSite(current);
        DrawChoiceButton(ctx, "Scout site", rightX + third + 6, ContentY + 143, third, 29,
            selected != null && !selected.Busy,
            selectedExpeditionType == FoxExpeditionType.Scout && selectedScavengeSiteId > 0);
        DrawButton(ctx, "Abandon", rightX + 2 * (third + 6), ContentY + 143, third, 29,
            selected != null && !selected.Busy);
        string[] focuses = { FoxScavengeSites.Useful, FoxScavengeSites.Furniture,
            FoxScavengeSites.Mixed, FoxScavengeSites.Walls };
        string[] labels = { "Useful", "Furniture", "Both", "Walls" };
        double quarter = (rightWidth - 18) / 4d;
        for (int i = 0; i < focuses.Length; i++)
            DrawChoiceButton(ctx, labels[i], rightX + i * (quarter + 6), ContentY + 182,
                quarter, 29, selected != null && !selected.Busy && !selected.Barren,
                selectedExpeditionType == FoxExpeditionType.Scavenge && selectedScavengeFocus == focuses[i]);
    }

    private void DrawRoutes(Context ctx, FoxPackStatePacket current, int width)
    {
        DrawText(ctx, "Pack routes", 14, ContentY, 18, TextGold);
        DrawText(ctx, "Permanent destinations bought with shared pack points.", 130, ContentY, 14, TextMuted);
        HashSet<string> unlocked = (current.UnlockedExpeditionTypes ?? new List<string>())
            .ToHashSet(StringComparer.Ordinal);
        double cardWidth = (width - 50) / 2d;
        const double cardHeight = 102d;
        const double cardGap = 8d;
        for (int index = 0; index < FoxExpeditionCatalog.Unlockable.Count; index++)
        {
            FoxExpeditionDefinition route = FoxExpeditionCatalog.Unlockable[index];
            int column = index % 2;
            int row = index / 2;
            double x = 14 + column * (cardWidth + 8);
            double y = ContentY + 22 + row * (cardHeight + cardGap);
            bool isUnlocked = unlocked.Contains(route.Id);
            bool prerequisitesMet = AreRoutePrerequisitesMet(route, unlocked);
            DrawRect(ctx, x, y, cardWidth, cardHeight, isUnlocked ? TextSelectedPanel : TextPanel);
            DrawText(ctx, $"{route.Name} — {route.TierLabel}", x + 14, y + 22, 16, TextWhite);
            DrawText(ctx,
                $"Party {route.MinimumFoxes}–{route.MaximumFoxes}   |   Safe {route.TargetStrength:0.#}   |   {FormatExpeditionHours(route.BaseDurationHours)}",
                x + 14, y + 43, 13, TextGold);
            DrawWrapped(ctx, route.Description, x + 14, y + 62, cardWidth - 164, 15, TextMuted, 2, 12);
            string label = isUnlocked ? "Purchased" : $"Buy — {route.UnlockCost} points";
            DrawButton(ctx, label, x + cardWidth - 145, y + 54, 132, 34,
                !isUnlocked && prerequisitesMet);
            if (!prerequisitesMet)
            {
                DrawText(ctx, $"Requires {Trim(GetRoutePrerequisiteNames(route), 58)}", x + 14, y + 94, 12, TextRed);
            }
        }
        if (current.PatrolPrepared)
        {
            double noteY = ContentY + 22 + 4 * (cardHeight + cardGap) + 8;
            DrawWrapped(ctx,
                "Pack patrol benefit ready: the next non-patrol expedition receives 10% additional safety.",
                18, noteY, width - 36, 16, TextGold, 2, 13);
        }
    }

    private void DrawRoutePurchaseOverlay(Context ctx, int width, int height, FoxPackStatePacket current)
    {
        FoxExpeditionDefinition route = FoxExpeditionCatalog.Get(purchaseRouteId);
        if (route == null || route.DefaultUnlocked)
        {
            purchaseRouteId = string.Empty;
            return;
        }

        bool alreadyPurchased = (current.UnlockedExpeditionTypes ?? new List<string>())
            .Contains(route.Id, StringComparer.Ordinal);
        if (alreadyPurchased)
        {
            purchaseRouteId = string.Empty;
            return;
        }

        double popupWidth = 520;
        double popupHeight = 226;
        double popupX = (width - popupWidth) / 2d;
        double popupY = (height - popupHeight) / 2d;
        DrawRect(ctx, 0, 0, width, height, TextButtonDisabled);
        DrawRect(ctx, popupX, popupY, popupWidth, popupHeight, TextPanel);
        DrawRect(ctx, popupX + 6, popupY + 6, popupWidth - 12, popupHeight - 12, TextSelectedPanel);
        DrawText(ctx, $"Permanently unlock {route.Name}?", popupX + 24, popupY + 38, 21, TextWhite);
        DrawText(ctx,
            $"Price: {route.UnlockCost} pack points   |   Available: {current.PackPoints}",
            popupX + 24, popupY + 68, 16,
            current.PackPoints >= route.UnlockCost ? TextGold : TextRed);
        DrawWrapped(ctx, route.Description, popupX + 24, popupY + 96,
            popupWidth - 48, 18, TextMuted, 3, 14);

        HashSet<string> unlocked = (current.UnlockedExpeditionTypes ?? new List<string>())
            .ToHashSet(StringComparer.Ordinal);
        bool prerequisitesMet = AreRoutePrerequisitesMet(route, unlocked);
        bool canAfford = current.PackPoints >= route.UnlockCost;
        bool canPurchase = canAfford && prerequisitesMet;
        if (canPurchase)
        {
            DrawButton(ctx, "Cancel", popupX + 24, popupY + 168, 210, 36, true);
            DrawButton(ctx, $"Purchase for {route.UnlockCost}", popupX + 286, popupY + 168, 210, 36, true);
        }
        else if (!prerequisitesMet)
        {
            DrawText(ctx, $"Buy first: {GetRoutePrerequisiteNames(route)}.",
                popupX + 24, popupY + 156, 14, TextRed);
            DrawButton(ctx, "Close", popupX + 155, popupY + 174, 210, 34, true);
        }
        else
        {
            int shortfall = route.UnlockCost - current.PackPoints;
            DrawText(ctx, $"The pack needs {shortfall} more {(shortfall == 1 ? "point" : "points")} to buy this route.",
                popupX + 24, popupY + 156, 14, TextRed);
            DrawButton(ctx, "Close", popupX + 155, popupY + 174, 210, 34, true);
        }
    }

    private void DrawPackTalents(Context ctx, FoxPackStatePacket current, int width, int height)
    {
        DrawText(ctx, "Pack talents", 14, ContentY, 18, TextGold);
        DrawText(ctx, "Shared by the whole pack. Unlock one-rank talents with pack points.", 132, ContentY, 14, TextMuted);

        const double groupTabY = ContentY + 28;
        const double groupTabHeight = 31;
        const double groupTabGap = 6;
        double groupTabWidth = (width - 28 - groupTabGap * (PackTalentCatalog.Groups.Count - 1))
            / PackTalentCatalog.Groups.Count;
        for (int index = 0; index < PackTalentCatalog.Groups.Count; index++)
        {
            PackTalentGroup group = PackTalentCatalog.Groups[index];
            double x = 14 + index * (groupTabWidth + groupTabGap);
            double[] accent = GetPackTalentAccent(group.Id);
            DrawChoiceButton(ctx, group.Name, x, groupTabY, groupTabWidth, groupTabHeight,
                true,
                string.Equals(activePackTalentGroup, group.Id, StringComparison.Ordinal)
                    || string.Equals(hoveredPackTalentGroupId, group.Id, StringComparison.Ordinal),
                accent);
        }

        PackTalentGroup selectedGroup = PackTalentCatalog.Get(activePackTalentGroup);
        double[] groupAccent = GetPackTalentAccent(selectedGroup.Id);
        const double boardX = 14;
        double boardY = ContentY + 72;
        double boardWidth = width - 38;
        const double boardHeight = 528;
        DrawRect(ctx, boardX, boardY, boardWidth, boardHeight, TextPanel);

        DrawText(ctx, selectedGroup.Name, boardX + 18, boardY + 25, 16, groupAccent);
        DrawText(ctx, "Pack-wide talents. Permanent repeatable upgrades show their current rank.",
            boardX + 170, boardY + 25, 12, TextMuted);

        const double nodeWidth = 146;
        const double nodeHeight = 110;
        const double columnGap = 5;
        const double rowGap = 8;
        const double nodeStartX = boardX + 18;
        double nodeStartY = boardY + 42;

        foreach (PackTalentDefinition talent in selectedGroup.Talents)
        {
            if (string.IsNullOrWhiteSpace(talent.ParentId))
            {
                continue;
            }

            PackTalentDefinition parent = selectedGroup.Talents.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, talent.ParentId, StringComparison.Ordinal));
            if (parent == null)
            {
                continue;
            }

            double parentX = nodeStartX + parent.Column * (nodeWidth + columnGap);
            double parentY = nodeStartY + parent.Row * (nodeHeight + rowGap);
            double childX = nodeStartX + talent.Column * (nodeWidth + columnGap);
            double childY = nodeStartY + talent.Row * (nodeHeight + rowGap);
            double parentMidY = parentY + nodeHeight / 2d;
            double childMidY = childY + nodeHeight / 2d;
            double bendX = (parentX + nodeWidth + childX) / 2d;

            ctx.SetSourceRGBA(groupAccent[0], groupAccent[1], groupAccent[2],
                string.Equals(hoveredPackTalentId, talent.Id, StringComparison.Ordinal) ? 0.85 : 0.42);
            ctx.LineWidth = 2;
            ctx.MoveTo(parentX + nodeWidth, parentMidY);
            ctx.LineTo(bendX, parentMidY);
            ctx.LineTo(bendX, childMidY);
            ctx.LineTo(childX, childMidY);
            ctx.Stroke();
        }

        foreach (PackTalentDefinition talent in selectedGroup.Talents)
        {
            double x = nodeStartX + talent.Column * (nodeWidth + columnGap);
            double y = nodeStartY + talent.Row * (nodeHeight + rowGap);
            bool root = string.IsNullOrWhiteSpace(talent.ParentId);
            bool hovered = string.Equals(hoveredPackTalentId, talent.Id, StringComparison.Ordinal);
            bool unlocked = IsPackTalentUnlocked(current, talent.Id);
            int rank = GetPackTalentRank(current, talent);
            int cost = talent.Repeatable ? rank + 1 : talent.UnlockCost;
            bool prerequisiteMet = root || IsPackTalentUnlocked(current, talent.ParentId);
            bool canBuy = talent.Implemented && (talent.Repeatable || !unlocked) && prerequisiteMet
                && current.PackPoints >= cost;
            double[] cardColor = unlocked ? TextAvailable : talent.Implemented
                ? TextCard
                : TextNyiPanel;
            DrawRect(ctx, x, y, nodeWidth, nodeHeight, cardColor);
            if (hovered)
            {
                DrawRect(ctx, x, y, 5, nodeHeight, groupAccent);
            }
            ctx.SetSourceRGBA(groupAccent[0], groupAccent[1], groupAccent[2], hovered ? 1 : root ? 0.8 : 0.42);
            ctx.LineWidth = hovered ? 2 : 1;
            ctx.Rectangle(x + 0.5, y + 0.5, nodeWidth - 1, nodeHeight - 1);
            ctx.Stroke();

            DrawRect(ctx, x + 7, y + 7, 42, 42, TextNyiPanel);
            DrawPackTalentGlyph(ctx, x + 7, y + 7, groupAccent);
            DrawWrapped(ctx, talent.Name, x + 55, y + 19, nodeWidth - 62, 14, TextWhite, 3, 14);
            if (!talent.Implemented)
            {
                DrawText(ctx, "Prototype", x + 7, y + 62, 13, groupAccent);
                DrawText(ctx, "Layout only", x + 7, y + 78, 11, TextMuted);
                DrawButton(ctx, "Coming later", x + 7, y + 83, nodeWidth - 14, 18, false);
            }
            else if (unlocked && !talent.Repeatable)
            {
                DrawText(ctx, "Unlocked", x + 7, y + 64, 13, TextGreen);
                DrawText(ctx, "Pack-wide", x + 7, y + 79, 11, TextMuted);
                DrawButton(ctx, "Unlocked", x + 7, y + 83, nodeWidth - 14, 18, false);
            }
            else
            {
                DrawText(ctx, talent.Repeatable ? $"Rank {rank} · Cost {cost}" : $"Cost: {cost}", x + 7, y + 64, 13, canBuy ? groupAccent : TextMuted);
                DrawText(ctx, prerequisiteMet ? (talent.Repeatable ? "Permanent" : "Single rank") : "Needs prerequisite", x + 7, y + 79, 11, TextMuted);
                DrawButton(ctx, talent.Repeatable ? "Increase" : "Unlock", x + 7, y + 83, nodeWidth - 14, 18, canBuy);
            }
        }

        PackTalentDefinition hoveredTalent = selectedGroup.Talents.FirstOrDefault(talent =>
            string.Equals(talent.Id, hoveredPackTalentId, StringComparison.Ordinal));
        if (hoveredTalent != null)
        {
            DrawPackTalentTooltip(ctx, current, hoveredTalent, selectedGroup, width, height,
                nodeStartX, nodeStartY, nodeWidth, nodeHeight, columnGap, rowGap);
        }
    }

    private static bool IsPackTalentUnlocked(FoxPackStatePacket current, string id)
    {
        if (PackTalentCatalog.GetTalent(id)?.Repeatable == true)
        {
            return GetRepeatablePackTalentRank(current, id) > 0;
        }
        return current.UnlockedPackTalents?.Contains(id, StringComparer.Ordinal) == true;
    }

    private static int GetPackTalentRank(FoxPackStatePacket current, PackTalentDefinition talent)
    {
        return talent.Repeatable
            ? GetRepeatablePackTalentRank(current, talent.Id)
            : IsPackTalentUnlocked(current, talent.Id) ? 1 : 0;
    }

    private static int GetRepeatablePackTalentRank(FoxPackStatePacket current, string id)
    {
        return string.Equals(id, "far-reaching-pack", StringComparison.Ordinal)
            ? Math.Max(0, current.PermanentRangeRank)
            : string.Equals(id, "many-trails", StringComparison.Ordinal)
                ? Math.Max(0, current.PermanentExpeditionCapacityRank)
                : 0;
    }

    private PackTalentDefinition FindHoveredPackTalent(double x, double y, int width)
    {
        PackTalentGroup group = PackTalentCatalog.Get(activePackTalentGroup);
        const double boardX = 14;
        double boardY = ContentY + 72;
        const double nodeWidth = 146;
        const double nodeHeight = 110;
        const double columnGap = 5;
        const double rowGap = 8;
        const double nodeStartX = boardX + 18;
        double nodeStartY = boardY + 42;
        foreach (PackTalentDefinition talent in group.Talents)
        {
            double nodeX = nodeStartX + talent.Column * (nodeWidth + columnGap);
            double nodeY = nodeStartY + talent.Row * (nodeHeight + rowGap);
            if (x >= nodeX && x <= nodeX + nodeWidth
                && y >= nodeY && y <= nodeY + nodeHeight)
            {
                return talent;
            }
        }
        return null;
    }

    private static PackTalentGroup FindHoveredPackTalentGroup(double x, double y, int width)
    {
        const double groupTabY = ContentY + 28;
        const double groupTabHeight = 31;
        const double groupTabGap = 6;
        double groupTabWidth = (width - 28 - groupTabGap * (PackTalentCatalog.Groups.Count - 1))
            / PackTalentCatalog.Groups.Count;
        if (y < groupTabY || y > groupTabY + groupTabHeight)
        {
            return null;
        }

        for (int index = 0; index < PackTalentCatalog.Groups.Count; index++)
        {
            double tabX = 14 + index * (groupTabWidth + groupTabGap);
            if (x >= tabX && x <= tabX + groupTabWidth)
            {
                return PackTalentCatalog.Groups[index];
            }
        }
        return null;
    }

    private static double[] GetPackTalentAccent(string groupId)
    {
        return groupId switch
        {
            "physique" => FoxGuiTheme.CombatAccent,
            "cohesion" => FoxGuiTheme.SocialAccent,
            "routine" => FoxGuiTheme.SurvivalAccent,
            "labor" => FoxGuiTheme.CommandsAccent,
            "expeditions" => FoxGuiTheme.ReturnAccent,
            "den-life" => FoxGuiTheme.SocialAccent,
            "work-cart" => FoxGuiTheme.OverviewAccent,
            _ => FoxGuiTheme.Accent
        };
    }

    private static void DrawPackTalentGlyph(Context ctx, double x, double y, double[] accent)
    {
        ctx.SetSourceRGBA(accent[0], accent[1], accent[2], 0.86);
        ctx.Arc(x + 21, y + 26, 8, 0, Math.PI * 2);
        ctx.Fill();
        ctx.Arc(x + 10, y + 13, 4, 0, Math.PI * 2);
        ctx.Arc(x + 20, y + 8, 4, 0, Math.PI * 2);
        ctx.Arc(x + 31, y + 13, 4, 0, Math.PI * 2);
        ctx.Fill();
    }

    private void DrawPackTalentTooltip(
        Context ctx,
        FoxPackStatePacket current,
        PackTalentDefinition talent,
        PackTalentGroup group,
        int width,
        int height,
        double nodeStartX,
        double nodeStartY,
        double nodeWidth,
        double nodeHeight,
        double columnGap,
        double rowGap)
    {
        double cardX = nodeStartX + talent.Column * (nodeWidth + columnGap);
        double cardY = nodeStartY + talent.Row * (nodeHeight + rowGap);
        double tooltipWidth = 402;
        double tooltipHeight = talent.Repeatable ? 286 : 230;
        double x = cardX + nodeWidth + 12;
        if (x + tooltipWidth > width - 8)
        {
            x = Math.Max(8, cardX - tooltipWidth - 12);
        }
        double y = Math.Clamp(cardY, 8, height - tooltipHeight - 8);
        double[] accent = GetPackTalentAccent(group.Id);
        DrawRect(ctx, x + 5, y + 5, tooltipWidth, tooltipHeight, FoxGuiTheme.TooltipShadow);
        DrawRect(ctx, x - 2, y - 2, tooltipWidth + 4, tooltipHeight + 4, accent);
        DrawRect(ctx, x, y, tooltipWidth, tooltipHeight, FoxGuiTheme.TooltipBackground);
        DrawWrapped(ctx, talent.Name, x + 14, y + 25, tooltipWidth - 132, 16, TextWhite, 2, 17);
        bool unlocked = IsPackTalentUnlocked(current, talent.Id);
        int rank = GetPackTalentRank(current, talent);
        FoxGuiTheme.DrawBadge(ctx, x + tooltipWidth - 108, y + 10, 94, 23, talent.Implemented);
        DrawText(ctx, !talent.Implemented ? "Prototype" : unlocked ? "Unlocked" : "Available",
            x + tooltipWidth - 98, y + 27, 11, talent.Implemented ? TextGreen : TextGold);

        DrawText(ctx, "Effect", x + 14, y + 70, 13, accent);
        bool prerequisiteMet = string.IsNullOrWhiteSpace(talent.ParentId)
            || IsPackTalentUnlocked(current, talent.ParentId);
        string effect = talent.Implemented
            ? talent.Repeatable
                ? BuildRepeatableTalentDescription(talent, rank)
                : talent.Description
            : "A pack-wide talent placeholder. Its effect and purchase rules will be defined after this board's layout is approved.";
        DrawWrapped(ctx,
            effect,
            x + 14, y + 91, tooltipWidth - 28, 16, TextWhite, talent.Repeatable ? 6 : 3, 13);
        double availabilityRuleY = talent.Repeatable ? y + 194 : y + 142;
        FoxGuiTheme.DrawSectionRule(ctx, x + 14, availabilityRuleY, tooltipWidth - 28);
        DrawText(ctx, "Availability", x + 14, availabilityRuleY + 22, 13, accent);
        string availability = !talent.Implemented
            ? "Proof-of-concept only. No purchase or gameplay effect is active yet."
            : talent.Repeatable
            ? $"Next rank costs {GetNextRepeatableTalentCost(rank)} pack points. Permanent: it cannot be refunded or removed by respec."
            : unlocked
            ? "Unlocked for the whole pack. This talent has no additional ranks."
            : prerequisiteMet
            ? $"Costs {talent.UnlockCost} pack points."
            : $"Unlock {PackTalentCatalog.GetTalent(talent.ParentId)?.Name ?? talent.ParentId} first.";
        DrawWrapped(ctx,
            availability,
            x + 14, availabilityRuleY + 43, tooltipWidth - 28, 16, TextMuted, talent.Repeatable ? 3 : 2, 13);
    }

    private void DrawLastTrip(Context ctx, FoxPackStatePacket current, int width)
    {
        List<FoxExpeditionSummaryPacket> reports = GetExpeditionReports(current);
        FoxExpeditionSummaryPacket report = GetSelectedExpeditionReport(current);
        DrawText(ctx, "Expedition reports", 14, ContentY, 18, TextGold);
        if (report == null || string.IsNullOrWhiteSpace(report.Type))
        {
            DrawRect(ctx, 14, ContentY + 24, width - 38, 92, TextPanel);
            DrawText(ctx, "No completed expedition has been recorded for this pack yet.",
                28, ContentY + 62, 16, TextWhite);
            return;
        }

        List<FoxExpeditionMemberSummaryPacket> members = report.Members
            ?? new List<FoxExpeditionMemberSummaryPacket>();
        Dictionary<string, string> labels = BuildSummaryLabels(members);
        int reportIndex = Math.Max(0, reports.FindIndex(candidate => candidate.ExpeditionId == report.ExpeditionId));
        DrawButton(ctx, confirmDeleteReportId == report.ExpeditionId ? "CONFIRM DELETE" : "Delete report",
            width - 688, ContentY - 8, 180, 30, true);
        DrawButton(ctx, "Previous", width - 480, ContentY - 8, 94, 30, reportIndex < reports.Count - 1);
        DrawButton(ctx, "Next", width - 378, ContentY - 8, 72, 30, reportIndex > 0);
        DrawText(ctx, $"{reportIndex + 1} of {reports.Count}", width - 294, ContentY + 12, 12, TextMuted);
        DrawButton(ctx, lastTripDetailsOpen ? "Read field report" : "Open exact ledger",
            width - 210, ContentY - 8, 182, 30, true);
        if (!lastTripDetailsOpen)
        {
            DrawLastTripNarrative(ctx, report, members, labels, width);
            return;
        }
        DrawRect(ctx, 14, ContentY + 22, width - 38, 232, TextPanel);
        DrawText(ctx,
            $"{report.Name}   |   Party: {members.Count}   |   Strength {report.ExpeditionStrength:0.#}/{report.TargetStrength:0.#}",
            28, ContentY + 48, 18, TextWhite);

        string travel = $"Travel: {FormatExpeditionHours(report.PlannedDurationHours)}";
        if (report.BaseDurationHours > 0f
            && Math.Abs(report.BaseDurationHours - report.PlannedDurationHours) > 0.01f)
        {
            travel += $" after perks (base {FormatExpeditionHours(report.BaseDurationHours)})";
        }
        travel += report.RanLate
            ? $"   |   Ran late by {FormatExpeditionHours(report.LateDurationHours)}"
            : "   |   On time";
        DrawText(ctx, travel, 28, ContentY + 71, 14, report.RanLate ? TextGold : TextMuted);

        string protection = report.Prepared && report.PatrolProtected
            ? "Protection: prepared expedition and secured trails"
            : report.Prepared
                ? "Protection: prepared expedition"
                : report.PatrolProtected
                    ? "Protection: secured trails"
                    : "Protection: none";
        DrawText(ctx, protection, 28, ContentY + 93, 14, TextMuted);
        DrawWrapped(ctx, $"Outcome: {FriendlyReportGrammar(report.Result)}", 28, ContentY + 116,
            width - 66, 16, TextWhite, 3, 13);

        List<ExpeditionStoryEventRecord> storyEvents = report.Story?.Events
            ?? new List<ExpeditionStoryEventRecord>();
        string events = storyEvents.Count == 0
            ? "Field notes: none recorded"
            : "Field notes: " + string.Join("; ", storyEvents.Take(4).Select(StoryEventSummary));
        DrawWrapped(ctx, events, 28, ContentY + 166, width - 66, 15, TextMuted, 2, 13);

        string loot = report.LootItems == null || report.LootItems.Count == 0
            ? "Brought back: no item cargo"
            : "Brought back: " + string.Join(", ", report.LootItems.Select(item => $"{item.Count}x {item.Name}"));
        DrawWrapped(ctx, loot, 28, ContentY + 207, width - 66, 16, TextGold, 2, 13);

        DrawText(ctx, "Party details", 14, ContentY + 270, 17, TextGold);
        double rowStartY = ContentY + 280;
        const double rowHeight = 88d;
        const double viewportHeight = 236d;
        for (int index = 0; index < members.Count; index++)
        {
            FoxExpeditionMemberSummaryPacket member = members[index];
            double y = rowStartY + index * rowHeight - lastTripScrollOffset;
            if (y + rowHeight < rowStartY || y > rowStartY + viewportHeight)
            {
                continue;
            }

            DrawRect(ctx, 14, y, width - 38, rowHeight - 4, TextPanel);
            DrawText(ctx, $"{Trim(labels[member.FoxId], 36)} — {member.Outcome}",
                28, y + 20, 15, TextWhite);
            string perks = member.Perks == null || member.Perks.Count == 0
                ? "No expedition perks"
                : string.Join(", ", member.Perks);
            string strengthBreakdown = member.TotalStrength > 0f
                ? $"Strength {member.TotalStrength:0.##} = condition {member.ConditionStrength:0.##} + health {member.HealthStrength:0.##} + speed {member.MovementStrength:0.##} + perks {member.PerkStrength:0.##}"
                : $"Recorded strength {member.BaseStrength:0.##} (older report without a breakdown)";
            DrawWrapped(ctx, strengthBreakdown, 28, y + 40, width - 66, 12, TextMuted, 2, 12);
            DrawWrapped(ctx, perks, 28, y + 68, width - 66, 12, TextGold, 2, 12);
        }
        if (members.Count * rowHeight > viewportHeight)
        {
            DrawScrollbar(ctx, width - 18, rowStartY, members.Count * rowHeight,
                lastTripScrollOffset, viewportHeight);
        }
    }

    private void DrawLastTripNarrative(Context ctx, FoxExpeditionSummaryPacket report,
        List<FoxExpeditionMemberSummaryPacket> members,
        IReadOnlyDictionary<string, string> labels,
        int width)
    {
        double panelY = ContentY + 43;
        FoxGuiTheme.DrawPanel(ctx, 14, panelY, width - 38, 450, true);
        DrawText(ctx, $"Field report — {report.Name}", 32, panelY + 32, 23, TextWhite);
        FoxGuiTheme.DrawSectionRule(ctx, 32, panelY + 43, width - 92);

        FoxExpeditionMemberSummaryPacket lead = members.FirstOrDefault(member =>
            member.Perks?.Any(perk => perk.Contains("Lead the Way", StringComparison.OrdinalIgnoreCase)
                || perk.Contains("Packwise", StringComparison.OrdinalIgnoreCase)) == true)
            ?? members.FirstOrDefault();
        string leadName = lead == null ? "The party" : labels[lead.FoxId];
        string opening = lead == null
            ? $"A party of {members.Count} set out for {report.Name.ToLowerInvariant()}."
            : $"{leadName} took point as {members.Count} companion{(members.Count == 1 ? string.Empty : "s")} set out for {report.Name.ToLowerInvariant()}.";
        if (lead != null && !string.IsNullOrWhiteSpace(lead.Personality))
        {
            opening += $" True to a {lead.Personality.ToLowerInvariant()} temperament, the lead was unmistakably their own.";
        }
        DrawWrapped(ctx, opening, 32, panelY + 74, width - 74, 19, TextWhite, 4, 15);

        string timing = report.RanLate
            ? $"The route should have taken {FormatExpeditionHours(report.PlannedDurationHours)}, but the party ran late by {FormatExpeditionHours(report.LateDurationHours)} before finding the cart again."
            : $"The party made the planned {FormatExpeditionHours(report.PlannedDurationHours)} journey and returned on time.";
        string safeguards = report.Prepared && report.PatrolProtected ? " They left prepared and used trails secured by the last patrol."
            : report.Prepared ? " They left with expedition preparations in place."
            : report.PatrolProtected ? " They made use of trails secured by the last patrol."
            : " They travelled without extra preparation or patrol protection.";
        DrawWrapped(ctx, timing + safeguards, 32, panelY + 157, width - 74, 19, TextMuted, 4, 14);

        List<FoxExpeditionMemberSummaryPacket> injured = members.Where(m => m.Outcome.Contains("injured", StringComparison.OrdinalIgnoreCase)).ToList();
        List<FoxExpeditionMemberSummaryPacket> missing = members.Where(m => m.Outcome.Contains("MIA", StringComparison.OrdinalIgnoreCase)).ToList();
        List<FoxExpeditionMemberSummaryPacket> mortal = members.Where(m => m.Outcome.Contains("mortal", StringComparison.OrdinalIgnoreCase)).ToList();
        string homecoming = missing.Count + injured.Count + mortal.Count == 0
            ? "Every companion came back on their feet."
            : "The return was not clean. "
                + (injured.Count > 0 ? $"{Names(injured, labels)} returned hurt. " : string.Empty)
                + (mortal.Count > 0 ? $"{Names(mortal, labels)} had to be carried home. " : string.Empty)
                + (missing.Count > 0 ? $"{Names(missing, labels)} did not return with the party. " : string.Empty);
        List<ExpeditionStoryEventRecord> storyEvents = report.Story?.Events
            ?? new List<ExpeditionStoryEventRecord>();
        string notable = storyEvents.Count == 0
            ? string.Empty
            : "Field notes: " + string.Join(" ", storyEvents.Take(3).Select(StoryEventNarrative)) + " ";
        DrawWrapped(ctx, notable + homecoming + " " + FriendlyReportGrammar(report.Result),
            32, panelY + 246, width - 74, 19, TextWhite, 5, 14);

        string cargo = report.LootItems == null || report.LootItems.Count == 0
            ? "The pack cart came home empty."
            : "The cart held " + string.Join(", ", report.LootItems.OrderByDescending(item => item.Count).Take(5)
                .Select(item => $"{item.Count} {item.Name}"))
                + (report.LootItems.Count > 5 ? ", and several smaller finds." : ".");
        DrawText(ctx, "Quartermaster's note", 32, panelY + 354, 16, TextGold);
        DrawWrapped(ctx, cargo, 32, panelY + 378, width - 74, 18, TextMuted, 4, 14);
        DrawText(ctx, "Open the exact ledger for every companion, talent, strength figure, and cargo stack.",
            32, panelY + 434, 12, TextGold);
    }

    private static string Names(
        List<FoxExpeditionMemberSummaryPacket> members,
        IReadOnlyDictionary<string, string> labels) =>
        string.Join(members.Count == 2 ? " and " : ", ", members.Select(member => labels[member.FoxId]));

    private static string StoryEventSummary(ExpeditionStoryEventRecord storyEvent) =>
        Lang.Get($"feralkinshipcompanions:expedition-event-{storyEvent.Id}-title");

    private static string StoryEventNarrative(ExpeditionStoryEventRecord storyEvent)
    {
        string title = StoryEventSummary(storyEvent);
        string description = Lang.Get($"feralkinshipcompanions:expedition-event-{storyEvent.Id}-description");
        return $"{title}: {description}";
    }

    private void DrawPartyRow(Context ctx, FoxPackMemberPacket member, string displayName, double x, double y, double width)
    {
        if (y + PartyRowHeight < ContentY + 18 || y > FooterY - 8)
        {
            return;
        }

        bool courierBusy = !string.IsNullOrWhiteSpace(member.CarriedItem) || member.WaitingForCartCargo;
        bool eligible = IsExpeditionEligible(member);
        bool missingTarget = selectedExpeditionType == FoxExpeditionType.SearchLost
            && IsRescueTarget(member)
            && string.Equals(member.FoxId, selectedMissingFoxId, StringComparison.Ordinal);
        bool selected = eligible && selectedFoxIds.Contains(member.FoxId);
        DrawRect(ctx, x, y, width, PartyRowHeight - 4, selected || missingTarget ? TextSelectedPanel : TextPanel);
        DrawRect(ctx, x + 8, y + 9, 18, 18, selected || missingTarget ? TextGold : TextCheckbox);
        if (selected || missingTarget)
        {
            DrawText(ctx, missingTarget ? "◎" : "✓", x + 10, y + 25, 16, TextDark);
        }

        string health = member.MaxHealth > 0f ? $"{member.CurrentHealth:0.#}/{member.MaxHealth:0.#}" : "?/?";
        float baseStrength = member.ExpeditionCoreStrength > 0f
            ? member.ExpeditionCoreStrength
            : member.MaxHealth > 0f && member.CurrentHealth < member.MaxHealth * 0.5f ? 0.5f : 1f;
        string contribution = eligible
            ? $"{baseStrength:0.#} strength"
            : courierBusy
                ? !string.IsNullOrWhiteSpace(member.CarriedItem) ? "carrying cargo" : "collecting cargo"
            : IsRescueTarget(member)
                ? "search target"
                : "unavailable";
        if (eligible && member.ExpeditionStrengthBonus > 0f)
        {
            contribution += $" (+{member.ExpeditionStrengthBonus:0.#} perks)";
        }
        if (eligible && member.RecruitmentChanceBonus > 0f)
        {
            contribution += $" (+{member.RecruitmentChanceBonus * 100f:0}% recruit)";
        }
        if (eligible && member.ExpeditionInjuryRiskReduction > 0f)
        {
            contribution += $" (+{member.ExpeditionInjuryRiskReduction * 100f:0}% safety)";
        }
        if (member.PregnancyActive)
        {
            contribution += " " + Lang.Get("feralkinshipcompanions:pack-pregnancy-strength-note", (int)(member.ExpeditionStrengthFactor * 100f));
        }
        else if (member.IsJuvenile)
        {
            contribution = Lang.Get("feralkinshipcompanions:child-expedition-locked");
        }
        DrawText(ctx, Trim(displayName, 24), x + 34, y + 17, 14, eligible ? TextWhite : TextMuted);
        DrawText(ctx, $"{health} · {contribution}", x + 34, y + 32, 12, eligible ? TextMuted : TextRed);
    }

    private void DrawCache(Context ctx, FoxPackStatePacket current, int width)
    {
        DrawText(ctx, "Expedition cache", 14, ContentY, 18, TextGold);
        DrawText(ctx, cartAccess
            ? current.CargoUnloadingActive
                ? "Unloading is active; companions will keep cycling between cart and box."
                : "Rewards remain saved here until you claim them or have companions deliver them."
            : "Rewards remain saved here until claimed. Clear the cache before another expedition.", 160, ContentY, 14, TextMuted);
        List<FoxPackLootItemPacket> loot = current.LootItems ?? new List<FoxPackLootItemPacket>();
        double y = ContentY + 30;
        if (current.RecruitmentReady)
        {
            DrawRect(ctx, 14, y, width - 38, 88, TextSelectedPanel);
            DrawText(ctx, current.PendingRecruitmentCount > 1
                ? $"Recruitment results ready: {current.PendingRecruitmentCount}"
                : "Recruitment result ready", 28, y + 24, 17, TextWhite);
            DrawText(ctx, $"Claiming adds a tamed {current.RecruitmentType.ToLowerInvariant()} to your pack nearby.", 28, y + 45, 14, TextMuted);
            DrawButton(ctx, current.PendingRecruitmentCount > 1 ? "Claim next companion" : "Claim companion", 28, y + 53, 190, 28, true);
            y += 108;
        }

        if (loot.Count == 0 && !current.RecruitmentReady)
        {
            DrawRect(ctx, 14, y, width - 38, 84, TextPanel);
            DrawText(ctx, "The cache is empty. New expeditions may begin.", 28, y + 32, 17, TextWhite);
            DrawText(ctx, "Nothing is waiting to be claimed or delivered.", 28, y + 57, 13, TextMuted);
        }
        else
        {
            double itemStartY = y;
            double itemViewportHeight = Math.Max(80d, 490d - itemStartY);
            foreach (FoxPackLootItemPacket item in loot)
            {
                double itemY = y - cacheScrollOffset;
                if (itemY >= ContentY + 18 && itemY <= 492)
                {
                    DrawRect(ctx, 14, itemY + 2, width - 38, 42, TextPanel);
                    DrawText(ctx, $"{item.Count}× {Trim(item.Name, 86)}", 76, itemY + 29, 16, TextWhite);
                }
                y += 48;
            }
            if (loot.Count * 48d > itemViewportHeight)
            {
                DrawScrollbar(ctx, width - 18, itemStartY, loot.Count * 48d, cacheScrollOffset, itemViewportHeight);
            }
            DrawButton(ctx, "Claim all items", 14, 520, 230, 35, true);
            if (cartAccess) DrawButton(ctx,
                current.CargoUnloadingActive ? "Stop unloading" : "Empty cart with companions",
                254, 520, 250, 35, true);
        }

        int activeExpeditionCount = GetActiveExpeditions(current).Count;
        if (activeExpeditionCount > 0)
        {
            DrawText(ctx, $"Expeditions underway: {activeExpeditionCount}/{Math.Max(1, current.ExpeditionCapacity)}", 14, 582, 16, TextGold);
        }
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

        FoxPackStatePacket current = state;
        if (current == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(purchaseRouteId))
        {
            HandleRoutePurchaseOverlayClick(current, x, y);
            return;
        }

        if (!cartAccess
            && x >= CompanionTabX
            && x <= CompanionTabX + CompanionTabWidth
            && y >= TabY
            && y <= TabY + TabHeight)
        {
            FoxGuiTheme.PlayNavigation(api);
            system.TryOpenFoxSocialGui(sourceEntityId);
            return;
        }

        if (y >= TabY && y <= TabY + TabHeight)
        {
            if (cartAccess)
            {
                foreach (var tab in CartTabs)
                    if (x >= tab.X && x < tab.X + tab.Width)
                    {
                        SelectTab(tab.Id);
                        break;
                    }
            }
            else
            {
                if (x >= 18 && x < 300) SelectTab(RosterTab);
                else if (x >= 310 && x < 592) SelectTab(CacheTab);
                else if (x >= 602 && x < 884) SelectTab(ArchivedTab);
            }
            Redraw();
            return;
        }
        if (cartAccess && activeTab is ExpeditionsTab or ScavengeTab or RoutesTab
            && y >= 102 && y <= 136)
        {
            if (x >= 18 && x < 388) SelectTab(ExpeditionsTab);
            else if (x >= 396 && x < 766) SelectTab(ScavengeTab);
            else if (x >= 774 && x < 1162) SelectTab(RoutesTab);
            Redraw();
            return;
        }

        switch (activeTab)
        {
            case RosterTab:
                HandleRosterOverviewClick(current, x, y);
                break;
            case ArchivedTab:
                HandleArchiveOverviewClick(current, x, y);
                break;
            case CacheTab:
                HandleCacheOverviewClick(current, x, y);
                break;
            case ExpeditionsTab:
                if (expeditionPartyPlannerOpen) HandleExpeditionPartyPlannerClick(current, x, y);
                else HandleExpeditionBrowserClick(current, x, y);
                break;
            case ScavengeTab:
                HandleScavengeMenuClick(current, x, y);
                break;
            case RoutesTab:
                HandleRoutesOverviewClick(current, x, y);
                break;
            case PackTalentsTab:
                HandlePackTalentsOverviewClick(current, x, y);
                break;
            case LastTripTab:
                HandleReportsOverviewClick(current, x, y);
                break;
        }
    }

    private void HandlePackTalentClick(double x, double y, int width)
    {
        const double groupTabY = ContentY + 28;
        const double groupTabHeight = 31;
        const double groupTabGap = 6;
        double groupTabWidth = (width - 28 - groupTabGap * (PackTalentCatalog.Groups.Count - 1))
            / PackTalentCatalog.Groups.Count;
        if (y >= groupTabY && y <= groupTabY + groupTabHeight)
        {
            for (int index = 0; index < PackTalentCatalog.Groups.Count; index++)
            {
                double tabX = 14 + index * (groupTabWidth + groupTabGap);
                if (x >= tabX && x <= tabX + groupTabWidth)
                {
                    activePackTalentGroup = PackTalentCatalog.Groups[index].Id;
                    FoxGuiTheme.PlayNavigation(api);
                    Redraw();
                    return;
                }
            }
        }

        PackTalentDefinition talent = FindHoveredPackTalent(x, y, width);
        if (talent == null || !talent.Implemented || state == null
            || (!talent.Repeatable && IsPackTalentUnlocked(state, talent.Id))
            || (!string.IsNullOrWhiteSpace(talent.ParentId)
                && !IsPackTalentUnlocked(state, talent.ParentId))
            || state.PackPoints < (talent.Repeatable ? GetNextRepeatableTalentCost(GetPackTalentRank(state, talent)) : talent.UnlockCost))
        {
            return;
        }

        FoxGuiTheme.PlayAction(api);
        system.SendFoxSocialAction(
            sourceEntityId,
            FoxSocialRequestAction.UnlockPackTalent,
            talent.Id);
    }

    private static string BuildRepeatableTalentDescription(PackTalentDefinition talent, int rank)
    {
        if (string.Equals(talent.Id, "many-trails", StringComparison.Ordinal))
        {
            int currentCapacity = rank >= int.MaxValue - FoxPackRepository.BaseExpeditionCapacity
                ? int.MaxValue
                : FoxPackRepository.BaseExpeditionCapacity + rank;
            int nextCapacity = currentCapacity == int.MaxValue ? int.MaxValue : currentCapacity + 1;
            return $"Permanent pack upgrade; cannot be refunded or removed by respec. Current: {currentCapacity} active expedition slots. Next: {nextCapacity}. Existing parties keep running independently; launching is blocked while cart rewards remain unclaimed.";
        }
        int currentHome = (int)Math.Round(FeralKinshipCompanionSystem.BaseCompanionCampRadius * (1d + rank * 0.10d));
        int currentCart = (int)Math.Round(FeralKinshipCompanionSystem.BaseWorkCartRadius * (1d + rank * 0.10d));
        int nextHome = (int)Math.Round(FeralKinshipCompanionSystem.BaseCompanionCampRadius * (1d + (rank + 1) * 0.10d));
        int nextCart = (int)Math.Round(FeralKinshipCompanionSystem.BaseWorkCartRadius * (1d + (rank + 1) * 0.10d));
        return $"Permanent pack upgrade; cannot be refunded or removed by respec. Current: home ~{currentHome} blocks, Work Cart ~{currentCart} blocks. Next: home ~{nextHome}, Work Cart ~{nextCart}. Work Cart range is horizontal; its vertical cylinder remains unlimited.";
    }

    private static int GetNextRepeatableTalentCost(int rank)
    {
        return rank >= int.MaxValue ? int.MaxValue : Math.Max(1, rank + 1);
    }

    private void DrawHelpOverlay(Context ctx, int width, int height)
    {
        FoxGuiTheme.DrawOverlay(ctx, width, height, TextButtonDisabled);
        DrawText(ctx, Lang.Get("feralkinshipcompanions:help-pack-title"), 30, 45, 23, TextWhite);
        DrawText(ctx, Lang.Get("feralkinshipcompanions:help-pack-roster-heading"), 30, 80, 16, TextGold);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-pack-roster-text"), 30, 103, width - 60, 17, TextWhite, 4, 14);

        DrawText(ctx, Lang.Get("feralkinshipcompanions:help-pack-cart-heading"), 30, 180, 16, TextGold);
        DrawWrapped(ctx, cartAccess
            ? Lang.Get("feralkinshipcompanions:help-pack-cart-active-text")
            : Lang.Get("feralkinshipcompanions:help-pack-cart-status-text"),
            30, 203, width - 60, 17, TextMuted, 4, 14);

        DrawText(ctx, Lang.Get("feralkinshipcompanions:help-pack-routes-heading"), 30, 280, 16, TextGold);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-pack-routes-text"), 30, 303, width - 60, 17, TextMuted, 4, 14);

        DrawText(ctx, Lang.Get("feralkinshipcompanions:help-pack-cache-heading"), 30, 380, 16, TextGold);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-pack-cache-text"), 30, 403, width - 60, 17, TextMuted, 4, 14);

        DrawText(ctx, Lang.Get("feralkinshipcompanions:help-pack-reports-heading"), 30, 480, 16, TextGold);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-pack-reports-text"), 30, 503, width - 60, 17, TextMuted, 4, 14);
        DrawText(ctx, Lang.Get("feralkinshipcompanions:help-pack-scroll-note"), 30, 580, 13, TextGold);
        DrawButton(ctx, Lang.Get("feralkinshipcompanions:tutorial-bram-notes"), width - 310, height - 52, 150, 30, true);
        DrawButton(ctx, Lang.Get("feralkinshipcompanions:help-close"), width - 150, height - 52, 120, 30, true);
    }

    private void DrawGuideOverlay(Context ctx, int width, int height)
    {
        FoxGuiTheme.DrawOverlay(ctx, width, height, TextButtonDisabled);
        string titleKey = cartAccess ? "guide-pack-cart-title" : "guide-pack-title";
        string bylineKey = cartAccess ? "guide-pack-cart-byline" : "guide-pack-byline";
        string textKey = cartAccess ? "guide-pack-cart-text" : "guide-pack-text";
        DrawText(ctx, Lang.Get("feralkinshipcompanions:" + titleKey), 30, 45, 23, TextWhite);
        DrawText(ctx, Lang.Get("feralkinshipcompanions:" + bylineKey), 30, 73, 15, TextGold);
        if (cartAccess)
        {
            DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:" + textKey), 30, 110, width - 60, 18, TextWhite, 10, 15);
        }
        else
        {
            DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:" + textKey), 30, 110, width - 60, 18, TextWhite, 4, 15);
            DrawText(ctx, Lang.Get("feralkinshipcompanions:guide-pack-cart-importance-heading"), 30, 205, 17, TextGold);
            DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:guide-pack-cart-importance-text"), 30, 230, width - 60, 18, TextWhite, 5, 15);
        }
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
            if (cartAccess)
            {
                system.MarkCompanionPackCartGuideSeen();
            }
            else
            {
                system.MarkCompanionPackGuideSeen();
            }
        }
    }

    private void HandleRosterClick(FoxPackStatePacket current, double x, double y)
    {
        if (x < 12 || x > CanvasWidth - 20 || y < ContentY + 8 || y > FooterY)
        {
            return;
        }

        int index = (int)Math.Floor((y - (ContentY + 14) + rosterScrollOffset) / RosterRowHeight);
        if (index < 0 || index >= (current.Members?.Count ?? 0))
        {
            return;
        }

        FoxPackMemberPacket member = current.Members[index];
        bool archivable = IsArchivableStatus(member.Status);
        bool unloadedWithLocation = string.Equals(member.Status, "Not currently loaded", StringComparison.OrdinalIgnoreCase)
            && member.HasLastKnownPosition;
        if (archivable && unloadedWithLocation
            && x >= CanvasWidth - 230
            && x < CanvasWidth - 150)
        {
            FoxGuiTheme.PlayAction(api);
            system.SendFoxSocialAction(sourceEntityId, FoxSocialRequestAction.ArchivePackFox, member.FoxId);
        }
        else if (archivable && unloadedWithLocation && x >= CanvasWidth - 150)
        {
            FoxGuiTheme.PlayAction(api);
            system.SendFoxSocialAction(sourceEntityId, FoxSocialRequestAction.LocatePackFox, member.FoxId);
        }
        else if (archivable && x >= CanvasWidth - 160)
        {
            FoxGuiTheme.PlayAction(api);
            system.SendFoxSocialAction(sourceEntityId, FoxSocialRequestAction.ArchivePackFox, member.FoxId);
        }
    }

    private static bool IsArchivableStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status)
            || string.Equals(status, "Present", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Available", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Returned healthy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Returned injured", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Unowned", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.Equals(status, "Running late", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(status, "Awaiting safe return", StringComparison.OrdinalIgnoreCase)
            && !status.StartsWith("Away —", StringComparison.OrdinalIgnoreCase)
            && !status.StartsWith("Leaving —", StringComparison.OrdinalIgnoreCase);
    }

    private void HandleArchivedClick(FoxPackStatePacket current, double x, double y)
    {
        if (x < 12 || x > CanvasWidth - 20 || y < ContentY + 8 || y > FooterY)
        {
            return;
        }

        int index = (int)Math.Floor((y - (ContentY + 14) + archivedScrollOffset) / RosterRowHeight);
        if (index < 0 || index >= (current.ArchivedMembers?.Count ?? 0))
        {
            return;
        }

        FoxPackMemberPacket member = current.ArchivedMembers[index];
        if (member.EntityLoaded && x >= CanvasWidth - 160)
        {
            FoxGuiTheme.PlayAction(api);
            system.SendFoxSocialAction(sourceEntityId, FoxSocialRequestAction.UnarchivePackFox, member.FoxId);
        }
    }

    private void HandleExpeditionClick(FoxPackStatePacket current, double x, double y)
    {
        double leftWidth = 520;
        double partyY = ContentY + 24 - expeditionScrollOffset;
        if (x >= 12 && x <= leftWidth && y >= partyY && y <= FooterY)
        {
            int column = x < 279 ? 0 : 1;
            int row = (int)Math.Floor((y - partyY) / PartyRowHeight);
            int index = row * 2 + column;
            if (index >= 0 && index < (current.Members?.Count ?? 0))
            {
                FoxPackMemberPacket member = current.Members[index];
                if (selectedExpeditionType == FoxExpeditionType.SearchLost
                    && IsRescueTarget(member)
                    && !IsSearchTargetActive(current, member.FoxId))
                {
                    FoxGuiTheme.PlayChoice(api);
                    selectedMissingFoxId = string.Equals(selectedMissingFoxId, member.FoxId, StringComparison.Ordinal)
                        ? string.Empty
                        : member.FoxId;
                    Redraw();
                }
                else if (IsExpeditionEligible(member))
                {
                    FoxGuiTheme.PlayChoice(api);
                    if (!selectedFoxIds.Add(member.FoxId)) selectedFoxIds.Remove(member.FoxId);
                    Redraw();
                }
                else
                {
                    FoxGuiTheme.PlayUnavailable(api);
                }
            }
            return;
        }

        double rightX = leftWidth + 12;
        double rightWidth = CanvasWidth - rightX - 14;
        if (expeditionCategory == "scavenge" && y >= ContentY + 8 && y <= ContentY + 32)
        {
            if (x >= rightX + rightWidth - 206 && x < rightX + rightWidth)
            {
                scavengeRuinRoutes = x >= rightX + rightWidth - 102;
                selectedExpeditionType = scavengeRuinRoutes ? string.Empty : FoxExpeditionType.Scout;
                scavengeIntelOpen = false;
                prepareSelected = false;
                FoxGuiTheme.PlayNavigation(api);
                Redraw();
                return;
            }
        }
        if (scavengeIntelOpen)
        {
            double top = ContentY + 218;
            if (x >= rightX && x < rightX + rightWidth && y >= top && y < FooterY)
            {
                int pages = Math.Max(1, (int)Math.Ceiling(
                    (SelectedScavengeSite(current)?.Clues?.Count ?? 0) / 5d));
                if (y >= top + 37 && y <= top + 64)
                {
                    if (x >= rightX + 12 && x < rightX + 108)
                        scavengeIntelPage = Math.Max(0, scavengeIntelPage - 1);
                    else if (x >= rightX + 114 && x < rightX + 216)
                        scavengeIntelPage = Math.Min(pages - 1, scavengeIntelPage + 1);
                    else if (x >= rightX + rightWidth - 94) scavengeIntelOpen = false;
                }
                FoxGuiTheme.PlayChoice(api);
                Redraw();
                return;
            }
            scavengeIntelOpen = false;
        }
        string[] categories = { "hunt", "forage", "utility" };
        double categoryWidth = (rightWidth - 12) / 3d;
        for (int index = 0; index < categories.Length; index++)
        {
            double buttonX = rightX + index * (categoryWidth + 6);
            if (x >= buttonX && x < buttonX + categoryWidth
                && y >= ContentY + 38 && y <= ContentY + 67)
            {
                FoxGuiTheme.PlayNavigation(api);
                expeditionCategory = categories[index];
                scavengeIntelOpen = false;
                if (expeditionCategory == "scavenge") selectedExpeditionType = FoxExpeditionType.Scout;
                else selectedExpeditionType = string.Empty;
                if (expeditionCategory == "scavenge") scavengeRuinRoutes = false;
                prepareSelected = false;
                Redraw();
                return;
            }
        }
        if (expeditionCategory == "scavenge" && !scavengeRuinRoutes
            && HandleScavengeControlClick(current, x, y, rightX, rightWidth))
        {
            Redraw();
            return;
        }
        HashSet<string> unlocked = (current.UnlockedExpeditionTypes ?? new List<string>())
            .ToHashSet(StringComparer.Ordinal);
        List<FoxExpeditionDefinition> visibleMissions = GetCategoryMissions();
        for (int index = 0; index < visibleMissions.Count; index++)
        {
            FoxExpeditionDefinition mission = visibleMissions[index];
            double buttonWidth = (rightWidth - 12) / 3d;
            int column = index % 3;
            int row = index / 3;
            double buttonX = rightX + column * (buttonWidth + 6);
            double buttonY = ContentY + 76 + row * 35;
            if (x >= buttonX && x < buttonX + buttonWidth && y >= buttonY && y <= buttonY + 29)
            {
                if (mission.DefaultUnlocked || unlocked.Contains(mission.Id))
                {
                    FoxGuiTheme.PlayChoice(api);
                    selectedExpeditionType = mission.Id;
                    prepareSelected = false;
                    Redraw();
                }
                else
                {
                    FoxGuiTheme.PlayNavigation(api);
                    purchaseRouteId = mission.Id;
                    Redraw();
                }
                return;
            }
        }

        bool slotsFull = GetActiveExpeditions(current).Count >= Math.Max(1, current.ExpeditionCapacity);
        bool hasLoot = current.LootItems != null && current.LootItems.Count > 0;
        bool hasPendingReward = current.RecruitmentReady;
        FoxExpeditionDefinition selectedDefinition = FoxExpeditionCatalog.Get(selectedExpeditionType);
        if (y >= ContentY + 338 && y <= ContentY + 367
            && x >= rightX && x < rightX + 328
            && !slotsFull
            && selectedDefinition != null
            && selectedDefinition.PreparationCost > 0
            && current.PackPoints >= selectedDefinition.PreparationCost)
        {
            FoxGuiTheme.PlayChoice(api);
            prepareSelected = !prepareSelected;
            Redraw();
            return;
        }
        if (y >= ContentY + 338 && y <= ContentY + 367
            && x >= rightX && x < rightX + 328)
        {
            FoxGuiTheme.PlayUnavailable(api);
            return;
        }

        if (y >= ContentY + 374 && y <= ContentY + 405)
        {
            if (x >= rightX && x < rightX + 156 && selectedDefinition != null && !slotsFull && !hasLoot && !hasPendingReward)
            {
                FoxGuiTheme.PlayAction(api);
                SelectHealthy(current);
            }
            else if (x >= rightX + 164 && x < rightX + 328 && selectedFoxIds.Count > 0)
            {
                FoxGuiTheme.PlayAction(api);
                selectedFoxIds.Clear();
            }
            else if (x >= rightX && x < rightX + 328)
            {
                FoxGuiTheme.PlayUnavailable(api);
            }
            Redraw();
            return;
        }

        if (y >= ContentY + 412 && y <= ContentY + 446)
        {
            if (x >= rightX && x < rightX + 196)
            {
                bool hasMia = (current.Members ?? new List<FoxPackMemberPacket>())
                    .Any(member => string.Equals(member.FoxId, selectedMissingFoxId, StringComparison.Ordinal)
                        && IsRescueTarget(member))
                    && !IsSearchTargetActive(current, selectedMissingFoxId);
                int selectedCount = (current.Members ?? new List<FoxPackMemberPacket>())
                    .Count(member => selectedFoxIds.Contains(member.FoxId)
                        && IsExpeditionEligible(member));
                bool routeUnlocked = selectedDefinition != null
                    && (selectedDefinition.DefaultUnlocked || unlocked.Contains(selectedDefinition.Id));
                bool prerequisitesMet = selectedDefinition != null
                    && AreRoutePrerequisitesMet(selectedDefinition, unlocked);
                bool canStart = routeUnlocked
                    && !slotsFull && !hasLoot
                    && selectedDefinition != null
                    && selectedCount >= selectedDefinition.MinimumFoxes
                    && selectedCount <= selectedDefinition.MaximumFoxes
                    && prerequisitesMet
                    && !hasPendingReward
                    && (!prepareSelected || current.PackPoints >= selectedDefinition.PreparationCost)
                    && (selectedExpeditionType != FoxExpeditionType.SearchLost || hasMia)
                    && IsSelectedScavengeDestinationValid(current);
                if (canStart)
                {
                    FoxGuiTheme.PlayAction(api);
                    system.SendFoxSocialAction(
                        sourceEntityId,
                        FoxSocialRequestAction.StartPackExpedition,
                        selectedExpeditionType,
                        selectedFoxIds,
                        selectedMissingFoxId,
                        prepareSelected,
                        scavengeSiteId: selectedScavengeSiteId,
                        scavengeFocus: selectedScavengeFocus,
                        scoutDuration: selectedScoutDuration
                    );
                }
                else
                {
                    FoxGuiTheme.PlayUnavailable(api);
                }
            }
            return;
        }
    }

    private bool HandleScavengeControlClick(
        FoxPackStatePacket current, double x, double y, double rightX, double rightWidth)
    {
        List<FoxScavengeSitePacket> sites = current.ScavengeSites ?? new List<FoxScavengeSitePacket>();
        double third = (rightWidth - 12) / 3d;
        if (y >= ContentY + 76 && y <= ContentY + 105)
        {
            for (int i = 0; i < 3; i++)
                if (x >= rightX + i * (third + 6) && x < rightX + i * (third + 6) + third)
                {
                    if (i < sites.Count)
                    {
                        bool again = selectedScavengeSiteId == sites[i].SiteId;
                        selectedScavengeSiteId = sites[i].SiteId;
                        scavengeIntelOpen = again && !scavengeIntelOpen;
                        scavengeIntelPage = 0;
                        FoxGuiTheme.PlayChoice(api);
                    }
                    else FoxGuiTheme.PlayUnavailable(api);
                    return true;
                }
        }
        if (y >= ContentY + 110 && y <= ContentY + 138)
        {
            string[] durations = { FoxScavengeSites.ShortScout, FoxScavengeSites.MediumScout,
                FoxScavengeSites.LongScout };
            for (int i = 0; i < 3; i++)
                if (x >= rightX + i * (third + 6) && x < rightX + i * (third + 6) + third)
                {
                    selectedScoutDuration = durations[i];
                    FoxGuiTheme.PlayChoice(api);
                    return true;
                }
        }
        if (y >= ContentY + 143 && y <= ContentY + 172)
        {
            FoxScavengeSitePacket site = SelectedScavengeSite(current);
            if (x >= rightX && x < rightX + third)
            {
                if (sites.Count < FoxScavengeSites.MaximumRememberedSites)
                {
                    selectedScavengeSiteId = 0;
                    selectedExpeditionType = FoxExpeditionType.Scout;
                    FoxGuiTheme.PlayChoice(api);
                }
                else FoxGuiTheme.PlayUnavailable(api);
                return true;
            }
            if (x >= rightX + third + 6 && x < rightX + 2 * third + 6)
            {
                if (site != null && !site.Busy)
                {
                    selectedExpeditionType = FoxExpeditionType.Scout;
                    FoxGuiTheme.PlayChoice(api);
                }
                else FoxGuiTheme.PlayUnavailable(api);
                return true;
            }
            if (x >= rightX + 2 * (third + 6) && x < rightX + 3 * third + 12)
            {
                if (site != null && !site.Busy)
                {
                    system.SendFoxSocialAction(sourceEntityId,
                        FoxSocialRequestAction.AbandonScavengeSite,
                        scavengeSiteId: site.SiteId);
                    selectedScavengeSiteId = 0;
                    selectedExpeditionType = FoxExpeditionType.Scout;
                    FoxGuiTheme.PlayAction(api);
                }
                else FoxGuiTheme.PlayUnavailable(api);
                return true;
            }
        }
        if (y >= ContentY + 182 && y <= ContentY + 211)
        {
            double quarter = (rightWidth - 18) / 4d;
            string[] focuses = { FoxScavengeSites.Useful, FoxScavengeSites.Furniture,
                FoxScavengeSites.Mixed, FoxScavengeSites.Walls };
            for (int i = 0; i < focuses.Length; i++)
                if (x >= rightX + i * (quarter + 6) && x < rightX + i * (quarter + 6) + quarter)
                {
                    FoxScavengeSitePacket site = SelectedScavengeSite(current);
                    if (site != null && !site.Busy && !site.Barren)
                    {
                        selectedScavengeFocus = focuses[i];
                        selectedExpeditionType = FoxExpeditionType.Scavenge;
                        FoxGuiTheme.PlayChoice(api);
                    }
                    else FoxGuiTheme.PlayUnavailable(api);
                    return true;
                }
        }
        return false;
    }

    private void HandleRoutesClick(FoxPackStatePacket current, double x, double y)
    {
        if (!cartAccess)
        {
            return;
        }

        HashSet<string> unlocked = (current.UnlockedExpeditionTypes ?? new List<string>())
            .ToHashSet(StringComparer.Ordinal);
        double cardWidth = (CanvasWidth - 50) / 2d;
        const double cardHeight = 102d;
        const double cardGap = 8d;
        for (int index = 0; index < FoxExpeditionCatalog.Unlockable.Count; index++)
        {
            FoxExpeditionDefinition route = FoxExpeditionCatalog.Unlockable[index];
            int column = index % 2;
            int row = index / 2;
            double cardX = 14 + column * (cardWidth + 8);
            double cardY = ContentY + 22 + row * (cardHeight + cardGap);
            if (x >= cardX + cardWidth - 145
                && x <= cardX + cardWidth - 13
                && y >= cardY + 54
                && y <= cardY + 88
                && !unlocked.Contains(route.Id)
                && AreRoutePrerequisitesMet(route, unlocked))
            {
                FoxGuiTheme.PlayNavigation(api);
                purchaseRouteId = route.Id;
                Redraw();
                return;
            }
        }
    }

    private void HandleRoutePurchaseOverlayClick(FoxPackStatePacket current, double x, double y)
    {
        FoxExpeditionDefinition route = FoxExpeditionCatalog.Get(purchaseRouteId);
        if (route == null)
        {
            purchaseRouteId = string.Empty;
            Redraw();
            return;
        }

        double popupWidth = 520;
        double popupHeight = 226;
        double popupX = (CanvasWidth - popupWidth) / 2d;
        double popupY = (CanvasHeight - popupHeight) / 2d;
        HashSet<string> unlocked = (current.UnlockedExpeditionTypes ?? new List<string>())
            .ToHashSet(StringComparer.Ordinal);
        bool canAfford = current.PackPoints >= route.UnlockCost;
        bool canPurchase = canAfford && AreRoutePrerequisitesMet(route, unlocked);
        if (canPurchase
            && x >= popupX + 286 && x <= popupX + 496
            && y >= popupY + 168 && y <= popupY + 204)
        {
            FoxGuiTheme.PlayAction(api);
            string routeId = purchaseRouteId;
            purchaseRouteId = string.Empty;
            system.SendFoxSocialAction(sourceEntityId, FoxSocialRequestAction.UnlockExpeditionRoute, routeId);
            Redraw();
            return;
        }

        bool cancelClicked = canPurchase
            ? x >= popupX + 24 && x <= popupX + 234
                && y >= popupY + 168 && y <= popupY + 204
            : x >= popupX + 155 && x <= popupX + 365
                && y >= popupY + 174 && y <= popupY + 208;
        if (cancelClicked)
        {
            FoxGuiTheme.PlayNavigation(api);
            purchaseRouteId = string.Empty;
            Redraw();
        }
    }

    private void SelectHealthy(FoxPackStatePacket current)
    {
        selectedFoxIds.Clear();
        int maximumFoxes = FoxExpeditionCatalog.Get(selectedExpeditionType)?.MaximumFoxes ?? int.MaxValue;
        foreach (FoxPackMemberPacket member in current.Members ?? new List<FoxPackMemberPacket>())
        {
            if (selectedFoxIds.Count >= maximumFoxes)
            {
                break;
            }
            if (IsExpeditionEligible(member)
                && (member.MaxHealth <= 0f || member.CurrentHealth >= member.MaxHealth * 0.5f))
            {
                selectedFoxIds.Add(member.FoxId);
            }
        }
    }

    private bool IsCacheClaimButtonClicked(FoxPackStatePacket current, double x, double y)
    {
        if (current.LootItems == null || current.LootItems.Count == 0)
        {
            return false;
        }

        double buttonY = 520;
        return x >= 14 && x <= 244 && y >= buttonY && y <= buttonY + 35;
    }

    private bool IsCacheStorageButtonClicked(FoxPackStatePacket current, double x, double y)
    {
        if (!cartAccess || (!current.CargoUnloadingActive
            && (current.LootItems == null || current.LootItems.Count == 0))) return false;
        const double buttonY = 520;
        return x >= 254 && x <= 504 && y >= buttonY && y <= buttonY + 35;
    }

    private bool IsRecruitmentClaimButtonClicked(FoxPackStatePacket current, double x, double y)
    {
        return current.RecruitmentReady
            && x >= 28
            && x <= 218
            && y >= ContentY + 30 + 53
            && y <= ContentY + 30 + 81;
    }

    private float GetSelectedStrength(IReadOnlyList<FoxPackMemberPacket> members)
    {
        float strength = 0f;
        foreach (FoxPackMemberPacket member in members)
        {
            if (selectedFoxIds.Contains(member.FoxId) && IsExpeditionEligible(member))
            {
                strength += GetMemberStrength(member);
            }
        }
        return strength;
    }

    private static bool AreRoutePrerequisitesMet(
        FoxExpeditionDefinition definition,
        IReadOnlySet<string> unlocked)
    {
        return definition.RequiredRouteIds == null
            || definition.RequiredRouteIds.All(unlocked.Contains);
    }

    private static string GetRoutePrerequisiteNames(FoxExpeditionDefinition definition)
    {
        if (definition.RequiredRouteIds == null || definition.RequiredRouteIds.Count == 0)
        {
            return "none";
        }

        return string.Join(", ", definition.RequiredRouteIds.Select(routeId =>
            FoxExpeditionCatalog.Get(routeId)?.Name ?? routeId));
    }

    private static string GetDepartureGuidance(
        FoxPackStatePacket current,
        FoxExpeditionDefinition definition,
        bool routeUnlocked,
        bool prerequisitesMet,
        int selectedCount,
        bool hasLoot,
        bool hasPendingReward,
        bool hasSelectedMia,
        bool canAffordPreparation)
    {
        if (definition == null) return "Select a mission before assembling the party.";
        if (!routeUnlocked) return "Route locked — select it above to review the permanent unlock.";
        if (!prerequisitesMet) return $"Unlock first: {GetRoutePrerequisiteNames(definition)}.";
        if (hasLoot || hasPendingReward) return "Claim the saved cart reward before starting another expedition.";
        if (GetActiveExpeditions(current).Count >= Math.Max(1, current.ExpeditionCapacity))
            return "Every active expedition slot is in use. Wait for a party to return or buy another Many Trails rank.";
        if (selectedCount < definition.MinimumFoxes)
        {
            int needed = definition.MinimumFoxes - selectedCount;
            return $"Select {needed} more eligible companion{(needed == 1 ? string.Empty : "s")} to meet the minimum party size.";
        }
        if (selectedCount > definition.MaximumFoxes)
        {
            int excess = selectedCount - definition.MaximumFoxes;
            return $"Remove {excess} companion{(excess == 1 ? string.Empty : "s")}; this route has a {definition.MaximumFoxes}-companion limit.";
        }
        if (string.Equals(definition.Id, FoxExpeditionType.SearchLost, StringComparison.Ordinal)
            && !hasSelectedMia)
        {
            return "Select the recoverable companion this party should search for.";
        }
        if (!canAffordPreparation)
        {
            return $"The selected preparation needs {definition.PreparationCost} pack points.";
        }
        if (current.PatrolPrepared && !string.Equals(definition.Id, FoxExpeditionType.PackPatrol, StringComparison.Ordinal))
        {
            return "Ready to depart. Secured trails will add protection to this expedition.";
        }
        return "Ready to depart. Parties below safe strength accept greater danger.";
    }

    private float GetSelectedRecruitmentChanceBonus(IReadOnlyList<FoxPackMemberPacket> members)
    {
        float bonus = 0f;
        foreach (FoxPackMemberPacket member in members)
        {
            if (selectedFoxIds.Contains(member.FoxId) && IsExpeditionEligible(member))
            {
                bonus += member.RecruitmentChanceBonus;
            }
        }
        return Math.Clamp(bonus, 0f, 0.50f);
    }

    private static float GetRecruitmentBaseChance(float expeditionStrength, float targetStrength)
    {
        if (targetStrength <= 0f)
        {
            return 0f;
        }

        return Math.Clamp(
            Math.Min(0.60f, expeditionStrength / targetStrength * 0.80f),
            0f,
            0.60f
        );
    }

    private static List<FoxActiveExpeditionPacket> GetActiveExpeditions(FoxPackStatePacket current)
    {
        if (current?.ActiveExpeditions?.Count > 0)
        {
            return current.ActiveExpeditions;
        }
        if (current == null || string.IsNullOrWhiteSpace(current.ExpeditionType))
        {
            return new List<FoxActiveExpeditionPacket>();
        }
        return new List<FoxActiveExpeditionPacket>
        {
            new()
            {
                Type = current.ExpeditionType,
                RemainingSeconds = current.ExpeditionRemainingSeconds,
                SelectedFoxIds = current.ExpeditionSelectedFoxIds ?? new List<string>(),
                Strength = current.ExpeditionStrength,
                TargetStrength = current.ExpeditionTargetStrength,
                ProgressPercent = current.ExpeditionProgressPercent,
                RecruitmentChanceBonus = current.ExpeditionRecruitmentChanceBonus,
                TargetFoxId = current.ExpeditionTargetFoxId,
                Prepared = current.ExpeditionPrepared,
                PreparationRiskReduction = current.ExpeditionPreparationRiskReduction,
                PatrolRiskReduction = current.ExpeditionPatrolRiskReduction,
                CompletesTotalHours = current.ExpeditionCompletesTotalHours,
                BaseDurationHours = current.ExpeditionBaseDurationHours,
                LateDurationHours = current.ExpeditionLateDurationHours,
                RunningLate = current.ExpeditionLateDurationHours > 0f
            }
        };
    }

    private static bool IsSearchTargetActive(FoxPackStatePacket current, string foxId)
    {
        return !string.IsNullOrWhiteSpace(foxId)
            && GetActiveExpeditions(current).Any(expedition =>
                string.Equals(expedition.Type, FoxExpeditionType.SearchLost, StringComparison.Ordinal)
                && string.Equals(expedition.TargetFoxId, foxId, StringComparison.Ordinal));
    }

    private static List<FoxExpeditionSummaryPacket> GetExpeditionReports(FoxPackStatePacket current)
    {
        if (current?.ExpeditionReports?.Count > 0)
        {
            return current.ExpeditionReports;
        }
        return current?.LastExpedition == null
            ? new List<FoxExpeditionSummaryPacket>()
            : new List<FoxExpeditionSummaryPacket> { current.LastExpedition };
    }

    private FoxExpeditionSummaryPacket GetSelectedExpeditionReport(FoxPackStatePacket current)
    {
        List<FoxExpeditionSummaryPacket> reports = GetExpeditionReports(current);
        return reports.FirstOrDefault(report => report.ExpeditionId == selectedReportId)
            ?? reports.FirstOrDefault();
    }

    private void SelectAdjacentReport(int indexDelta)
    {
        List<FoxExpeditionSummaryPacket> reports = GetExpeditionReports(state);
        if (reports.Count == 0) return;
        int index = reports.FindIndex(report => report.ExpeditionId == selectedReportId);
        index = Math.Clamp(index < 0 ? 0 : index + indexDelta, 0, reports.Count - 1);
        if (reports[index].ExpeditionId == selectedReportId) return;
        selectedReportId = reports[index].ExpeditionId;
        confirmDeleteReportId = 0;
        lastTripScrollOffset = 0d;
        FoxGuiTheme.PlayNavigation(api);
        Redraw();
    }

    private void ClampScrollOffsets()
    {
        int activeCount = state?.Members?.Count ?? 0;
        rosterScrollOffset = Math.Min(rosterScrollOffset,
            Math.Max(0d, VisibleRoster(state).Count * OverviewRowHeight - OverviewListHeight));
        archivedScrollOffset = Math.Min(archivedScrollOffset,
            Math.Max(0d, VisibleArchive(state).Count * OverviewRowHeight - OverviewListHeight));
        expeditionScrollOffset = Math.Min(expeditionScrollOffset, expeditionPartyPlannerOpen
            ? Math.Max(0d, activeCount * PlannerRowHeight - PlannerRowsHeight)
            : Math.Max(0d, GetCategoryMissions().Count * MissionRowHeight - MissionRowsHeight));
        scavengePartyScrollOffset = Math.Min(scavengePartyScrollOffset,
            Math.Max(0d, activeCount * ScavengePartyRowHeight - ScavengePartyViewportHeight));
        int activeExpeditionCount = GetActiveExpeditions(state).Count;
        activeExpeditionScrollOffset = Math.Min(activeExpeditionScrollOffset,
            activeExpeditionCount * 48d > 116d
                ? Math.Max(0d, activeExpeditionCount * 48d - 116d)
                : 0d);
        int lootCount = state?.LootItems?.Count ?? 0;
        double cacheViewportHeight = CacheRowsHeight;
        cacheScrollOffset = Math.Min(cacheScrollOffset, lootCount * CacheRowHeight > cacheViewportHeight
            ? Math.Max(0d, lootCount * CacheRowHeight - cacheViewportHeight)
            : 0d);
        packTalentScrollOffset = Math.Min(packTalentScrollOffset,
            Math.Max(0d, PackTalentContentHeight(PackTalentCatalog.Get(activePackTalentGroup)) - TalentViewportHeight));
        reportListScrollOffset = Math.Min(reportListScrollOffset,
            Math.Max(0d, GetExpeditionReports(state).Count * ReportRowHeight - ReportRowsHeight));
        int lastTripMembers = GetSelectedExpeditionReport(state)?.Members?.Count ?? 0;
        lastTripScrollOffset = Math.Min(lastTripScrollOffset, lastTripMembers * 88d > 236d
            ? Math.Max(0d, lastTripMembers * 88d - 236d)
            : 0d);
    }

    private float GetDisplayedExpeditionRemainingSeconds(FoxPackStatePacket current)
    {
        if (current == null || current.ExpeditionRemainingSeconds <= 0f)
        {
            return 0f;
        }

        float elapsed = Math.Max(0f, (api.ElapsedMilliseconds - expeditionSnapshotReceivedMs) / 1000f);
        return Math.Max(0f, current.ExpeditionRemainingSeconds - elapsed);
    }

    private float GetDisplayedBackpackReturnSeconds(FoxPackMemberPacket member)
    {
        if (member == null || member.BackpackReturnRemainingSeconds <= 0f)
        {
            return 0f;
        }

        float elapsed = Math.Max(0f, (api.ElapsedMilliseconds - expeditionSnapshotReceivedMs) / 1000f);
        return Math.Max(0f, member.BackpackReturnRemainingSeconds - elapsed);
    }

    private static string FormatBackpackCountdown(float remainingSeconds)
    {
        int seconds = Math.Max(1, (int)Math.Ceiling(remainingSeconds));
        TimeSpan duration = TimeSpan.FromSeconds(seconds);
        if (duration.Days > 0) return $"{duration.Days}d {duration.Hours}h {duration.Minutes}m {duration.Seconds}s";
        if (duration.Hours > 0) return $"{duration.Hours}h {duration.Minutes}m {duration.Seconds}s";
        if (duration.Minutes > 0) return $"{duration.Minutes}m {duration.Seconds}s";
        return $"{duration.Seconds}s";
    }

    private double GetDisplayedExpeditionRemainingHours(FoxPackStatePacket current)
    {
        if (current == null || current.ExpeditionCompletesTotalHours <= 0d)
        {
            return 0d;
        }

        return Math.Max(0d, current.ExpeditionCompletesTotalHours - api.World.Calendar.TotalHours);
    }

    private double GetDisplayedExpeditionRemainingHours(FoxActiveExpeditionPacket expedition)
    {
        if (expedition == null) return 0d;
        if (expedition.CompletesTotalHours > 0d)
        {
            return Math.Max(0d, expedition.CompletesTotalHours - api.World.Calendar.TotalHours);
        }
        float elapsed = Math.Max(0f, (api.ElapsedMilliseconds - expeditionSnapshotReceivedMs) / 1000f);
        return Math.Max(0f, expedition.RemainingSeconds - elapsed) / 3600d;
    }

    private static string FormatExpeditionHours(double hours)
    {
        hours = Math.Max(0d, hours);
        int wholeHours = (int)Math.Floor(hours);
        int minutes = (int)Math.Round((hours - wholeHours) * 60d);
        if (minutes >= 60)
        {
            wholeHours++;
            minutes = 0;
        }
        if (wholeHours <= 0)
        {
            return $"{Math.Max(1, minutes)}m";
        }
        return minutes > 0 ? $"{wholeHours}h {minutes}m" : $"{wholeHours}h";
    }

    private static bool IsSelectableStatus(string status)
    {
        return status is "Present" or "Returned healthy" or "Returned injured";
    }

    private static bool IsOrdinaryRosterStatus(string status)
    {
        return status is "Present" or "Available" or "Returned healthy" or "Returned injured" or "Unowned";
    }

    private static bool IsCourierBusy(FoxPackMemberPacket member)
    {
        return !string.IsNullOrWhiteSpace(member.CarriedItem) || member.WaitingForCartCargo;
    }

    private static bool IsExpeditionEligible(FoxPackMemberPacket member) =>
        !member.IsJuvenile
        && IsSelectableStatus(member.Status);

    private static bool IsRescueTarget(FoxPackMemberPacket member) =>
        member.RescueRecoverable
        && (string.Equals(member.Status, "MIA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(member.Status, "Not currently loaded", StringComparison.OrdinalIgnoreCase));

    private static string FriendlyStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status)) return "Status unknown";
        if (string.Equals(status, "Not currently loaded", StringComparison.OrdinalIgnoreCase))
        {
            return "Unloaded — location saved";
        }
        if (string.Equals(status, "Returned healthy", StringComparison.OrdinalIgnoreCase))
        {
            return "Returned safely";
        }
        if (string.Equals(status, "Mortally wounded", StringComparison.OrdinalIgnoreCase))
        {
            return "Mortally wounded — needs treatment";
        }
        return status;
    }

    private static string FriendlyReportGrammar(string text)
    {
        return Regex.Replace(text ?? string.Empty, @"\b(\d+) item\(s\)", match =>
        {
            return int.TryParse(match.Groups[1].Value, out int count) && count == 1
                ? "1 item"
                : $"{match.Groups[1].Value} items";
        }, RegexOptions.IgnoreCase);
    }

    private static string ExpeditionName(string type)
    {
        return FoxExpeditionCatalog.Get(type)?.Name ?? type;
    }

    private void SelectTab(string tab)
    {
        activeTab = tab;
        if (!string.Equals(tab, PackTalentsTab, StringComparison.Ordinal))
        {
            hoveredPackTalentId = string.Empty;
            hoveredPackTalentGroupId = string.Empty;
        }
        FoxGuiTheme.PlayNavigation(api);
    }

    private void DrawTab(Context ctx, string label, string tab, double x, double y, double width)
    {
        bool selected = activeTab == tab || tab == ExpeditionsTab && activeTab is ScavengeTab or RoutesTab;
        FoxGuiTheme.DrawTabSurface(ctx, x, y, width, TabHeight, selected, false, TextGold);
        DrawText(ctx, Trim(label, width <= 165 ? 20 : 42), x + 10, y + TabHeight / 2 + 6, width < 125 ? 13 : 14,
            selected ? TextGold : TextWhite);
    }

    private static void DrawScrollbar(Context ctx, double x, double y, double contentHeight, double offset)
    {
        DrawScrollbar(ctx, x, y, contentHeight, offset, 500d);
    }

    private static void DrawScrollbar(Context ctx, double x, double y, double contentHeight, double offset, double trackHeight)
    {
        if (contentHeight <= trackHeight)
        {
            return;
        }

        double thumbHeight = Math.Max(36d, trackHeight * trackHeight / contentHeight);
        double maxOffset = contentHeight - trackHeight;
        double thumbY = y + (maxOffset <= 0d ? 0d : offset / maxOffset * (trackHeight - thumbHeight));
        DrawRect(ctx, x, y, 8, trackHeight, TextScrollbarTrack);
        DrawRect(ctx, x, thumbY, 8, thumbHeight, TextScrollbarThumb);
    }

    private static void DrawButton(Context ctx, string label, double x, double y, double width, double height, bool enabled)
    {
        FoxGuiTheme.DrawButtonSurface(ctx, x, y, width, height, enabled);
        DrawText(ctx, label, x + 10, y + height / 2 + 6, 15, enabled ? TextWhite : TextMuted);
    }

    private static void DrawLockedMissionButton(Context ctx, string label, double x, double y, double width, double height)
    {
        DrawRect(ctx, x, y, width, height, TextLockedPanel);
        ctx.SetSourceRGBA(TextLockedBorder[0], TextLockedBorder[1], TextLockedBorder[2], TextLockedBorder[3]);
        ctx.LineWidth = 1;
        ctx.Rectangle(x + 0.5, y + 0.5, width - 1, height - 1);
        ctx.Stroke();
        DrawLock(ctx, x + 9, y + 7);
        DrawText(ctx, Trim(label, width <= 165 ? 17 : 30), x + 27, y + height / 2 + 5, 13, TextLocked);
    }

    private static void DrawLock(Context ctx, double x, double y)
    {
        ctx.SetSourceRGBA(TextLocked[0], TextLocked[1], TextLocked[2], TextLocked[3]);
        ctx.LineWidth = 1.5;
        ctx.Arc(x + 6, y + 6, 4, Math.PI, 0);
        ctx.Stroke();
        DrawRect(ctx, x + 1.5, y + 6, 9, 8, TextLocked);
        DrawRect(ctx, x + 5, y + 9, 2, 4, TextLockedPanel);
    }

    private static void DrawChoiceButton(
        Context ctx,
        string label,
        double x,
        double y,
        double width,
        double height,
        bool enabled,
        bool selected,
        double[] accent = null)
    {
        accent ??= TextGold;
        FoxGuiTheme.DrawChoiceSurface(ctx, x, y, width, height, selected && enabled);
        if (selected && enabled)
        {
            DrawRect(ctx, x, y, 4, height, accent);
        }
        double fontSize = width < 125 ? 13 : 14;
        DrawText(ctx, Trim(label, width <= 165 ? 20 : 42), x + 10, y + height / 2 + 6, fontSize,
            !enabled ? TextMuted : selected ? accent : TextWhite);
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

    private static void DrawWrapped(
        Context ctx,
        string text,
        double x,
        double baseline,
        double maxWidth,
        double lineHeight,
        double[] color,
        int maxLines,
        double fontSize)
    {
        string[] words = (text ?? string.Empty)
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        List<string> lines = new();
        string line = string.Empty;
        foreach (string word in words)
        {
            string candidate = string.IsNullOrEmpty(line) ? word : line + " " + word;
            if (FeralKinshipCompanionUiSettings.GetTextWidth(candidate, fontSize) > maxWidth
                && !string.IsNullOrEmpty(line))
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = candidate;
            }
        }
        if (!string.IsNullOrEmpty(line))
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

    private static string Trim(string value, int maxLength)
    {
        value ??= string.Empty;
        if (value.Length <= maxLength)
        {
            return value;
        }
        return value.Substring(0, Math.Max(0, maxLength - 1)) + "…";
    }

    private static double[] StatusColor(string status)
    {
        if (status == "MIA") return TextRed;
        if (status.StartsWith("Away", StringComparison.OrdinalIgnoreCase)) return TextGold;
        if (status.StartsWith("Leaving", StringComparison.OrdinalIgnoreCase)) return TextGold;
        if (status == "Running late") return TextGold;
        if (status == "Returned injured" || status == "Mortally wounded") return TextOrange;
        return TextGreen;
    }

    private static double[] TextWhite => FoxGuiTheme.Text;
    private static double[] TextMuted => FoxGuiTheme.Muted;
    private static double[] TextGold => FoxGuiTheme.Accent;
    private static double[] TextDark => FoxGuiTheme.DarkText;
    private static double[] TextRed => FoxGuiTheme.Danger;
    private static double[] TextOrange => FoxGuiTheme.Warning;
    private static double[] TextGreen => FoxGuiTheme.Success;
    private static double[] TextPanel => FoxGuiTheme.PanelColor;
    private static double[] TextCard => FoxGuiTheme.PanelColor;
    private static double[] TextAvailable => FoxGuiTheme.SelectedPanelColor;
    private static double[] TextNyiPanel => FoxGuiTheme.FuturePanel;
    private static double[] TextSelectedPanel => FoxGuiTheme.SelectedPanelColor;
    private static double[] TextRedPanel => FoxGuiTheme.DangerPanelColor;
    private static double[] TextMutedPanel => FoxGuiTheme.MutedPanelColor;
    private static double[] TextCheckbox => FoxGuiTheme.CheckboxColor;
    private static double[] TextButton => FoxGuiTheme.ButtonColor;
    private static double[] TextButtonDisabled => FoxGuiTheme.ButtonDisabledColor;
    private static double[] TextLocked => FoxGuiTheme.LockedText;
    private static double[] TextLockedPanel => FoxGuiTheme.LockedPanel;
    private static double[] TextLockedBorder => FoxGuiTheme.LockedBorder;
    private static double[] TextScrollbarTrack => FoxGuiTheme.ScrollTrack;
    private static double[] TextScrollbarThumb => FoxGuiTheme.ScrollThumb;
}
