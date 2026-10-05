#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace FeralKinshipCompanions;

public sealed class GuiDialogFeralKinshipStorageRouting : GuiDialog
{
    private const double DialogWidth = 640;
    private const double DialogHeight = 700;

    private readonly FeralKinshipCompanionSystem system;
    private FoxStorageRoutingStatePacket state;
    private GuiElementFeralKinshipStorageRoutingSurface surface;

    public GuiDialogFeralKinshipStorageRouting(
        ICoreClientAPI capi,
        FeralKinshipCompanionSystem system,
        FoxStorageRoutingStatePacket state)
        : base(capi)
    {
        this.system = system;
        this.state = state;
    }

    public override string ToggleKeyCombinationCode => null;

    public bool IsFor(FoxStorageRoutingStatePacket packet) => state != null && packet != null
        && state.X == packet.X
        && state.Y == packet.Y
        && state.Z == packet.Z
        && state.Dimension == packet.Dimension;

    public override void OnGuiOpened()
    {
        ElementBounds content = ElementBounds.Fixed(0, 0, DialogWidth, DialogHeight).WithFixedPadding(0);
        content.BothSizing = ElementSizing.Fixed;
        ElementBounds dialog = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
        GuiComposer composer = capi.Gui
            .CreateCompo("feralkinship-storage-routing", dialog)
            .AddShadedDialogBG(content)
            .AddDialogTitleBar("Pack Ledger", () => TryClose())
            .BeginChildElements(content);

        surface = new GuiElementFeralKinshipStorageRoutingSurface(
            capi,
            ElementBounds.Fixed(0, 0, DialogWidth, DialogHeight),
            system);
        composer.AddInteractiveElement(surface, "storage-routing-surface");
        SingleComposer = composer.Compose();
        base.OnGuiOpened();
        surface.ApplyState(state);
    }

    public void ApplyState(FoxStorageRoutingStatePacket packet)
    {
        state = packet;
        surface?.ApplyState(packet);
    }
}

internal sealed class GuiElementFeralKinshipStorageRoutingSurface : GuiElementFeralKinshipScaledSurface
{
    protected override double DesignWidth => 640;
    protected override double DesignHeight => 700;

    private const double ListTop = 188;
    private const double ListBottom = 524;
    private const double RowHeight = 48;
    private const double ColumnWidth = 202;
    private const double AdvancedListTop = 210;
    private const double AdvancedRowHeight = 43;
    private const int AdvancedVisibleRows = 7;

    private readonly FeralKinshipCompanionSystem system;
    // General is the one-click fallback above the grid. Keeping it out of
    // the category grid avoids presenting two controls for the same rule.
    private readonly IReadOnlyList<(FoxStorageRouting.Category Category, string Name)> options =
        FoxStorageRouting.Options.Where(option => option.Category != FoxStorageRouting.Category.General).ToArray();
    private FoxStorageRoutingStatePacket state;
    private int hoveredIndex = -1;
    private int hoveredItemIndex = -1;
    private int advancedPage;
    private int textureId;
    private bool helpOpen;
    private bool helpHovered;

    private static readonly double[] White = FoxGuiTheme.Text;
    private static readonly double[] Muted = FoxGuiTheme.Muted;
    private static readonly double[] Gold = FoxGuiTheme.Accent;
    private static readonly double[] Panel = FoxGuiTheme.PanelColor;
    private static readonly double[] Selected = FoxGuiTheme.SelectedPanelColor;

    public GuiElementFeralKinshipStorageRoutingSurface(
        ICoreClientAPI capi,
        ElementBounds bounds,
        FeralKinshipCompanionSystem system)
        : base(capi, bounds)
    {
        this.system = system;
    }

    public void ApplyState(FoxStorageRoutingStatePacket packet)
    {
        state = packet;
        if (packet.AdvancedMode) advancedPage = Math.Min(advancedPage, AdvancedPageCount - 1);
        else advancedPage = 0;
        Redraw();
    }

    public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
    {
        Bounds.CalcWorldBounds();
        Redraw();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        double currentX = CanvasMouseX(api.Input.MouseX);
        double currentY = CanvasMouseY(api.Input.MouseY);
        bool nextHelpHovered = FoxGuiTheme.IsHelpHovered(currentX, currentY, CanvasWidth);
        if (nextHelpHovered != helpHovered)
        {
            helpHovered = nextHelpHovered;
            Redraw();
        }

        int nextHovered = state?.AdvancedMode == true ? -1 : FindCategoryIndex(currentX, currentY);
        if (nextHovered != hoveredIndex)
        {
            hoveredIndex = nextHovered;
            Redraw();
        }

        int nextHoveredItem = state?.AdvancedMode == true
            ? FindAdvancedItemIndex(currentX, currentY)
            : -1;
        if (nextHoveredItem != hoveredItemIndex)
        {
            hoveredItemIndex = nextHoveredItem;
            Redraw();
        }

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

        double helpX = FoxGuiTheme.HelpButtonX(CanvasWidth);
        if (x >= helpX && x <= helpX + 34 && y >= 5 && y <= 38)
        {
            FoxGuiTheme.PlayNavigation(api);
            helpOpen = !helpOpen;
            Redraw();
            return;
        }

        if (helpOpen)
        {
            if (x >= CanvasWidth - 150
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

        if (state == null || !state.Supported)
        {
            return;
        }

        if (y >= 126 && y <= 162)
        {
            if (x >= 350 && x <= 440)
            {
                FoxGuiTheme.PlayChoice(api);
                system.SendStorageRoutingAction(
                    state,
                    state.AdvancedMode
                        ? FoxStorageRoutingRequestPacket.UseCategories
                        : FoxStorageRoutingRequestPacket.EnableAdvanced);
            }
            else if (x >= 445 && x <= 525)
            {
                FoxGuiTheme.PlayChoice(api);
                system.SendStorageRoutingAction(state, FoxStorageRoutingRequestPacket.SetMask,
                    (int)FoxStorageRouting.Category.General);
            }
            else if (x >= 530 && x <= 620)
            {
                FoxGuiTheme.PlayChoice(api);
                system.SendStorageRoutingAction(state, FoxStorageRoutingRequestPacket.Clear);
            }
            return;
        }

        if (state.AdvancedMode)
        {
            if (x >= 460 && x <= 620 && y >= 172 && y <= 204)
            {
                FoxGuiTheme.PlayChoice(api);
                system.SendStorageRoutingAction(state, FoxStorageRoutingRequestPacket.AddCurrentItems);
                return;
            }

            if (y >= 518 && y <= 548)
            {
                if (x >= 20 && x <= 120 && advancedPage > 0)
                {
                    advancedPage--;
                    Redraw();
                }
                else if (x >= 500 && x <= 620 && advancedPage < AdvancedPageCount - 1)
                {
                    advancedPage++;
                    Redraw();
                }
                return;
            }

            int itemIndex = FindAdvancedItemIndex(x, y);
            if (itemIndex >= 0 && itemIndex < state.AdvancedItems.Count)
            {
                FoxStorageRoutingItemRulePacket item = state.AdvancedItems[itemIndex];
                FoxGuiTheme.PlayChoice(api);
                system.SendStorageRoutingAction(
                    state,
                    FoxStorageRoutingRequestPacket.ToggleExactItem,
                    itemCode: item.ItemCode);
            }
            return;
        }

        int index = FindCategoryIndex(x, y);
        if (index < 0 || index >= options.Count)
        {
            return;
        }

        FoxStorageRouting.Category selected = options[index].Category;
        FoxStorageRouting.Category current = (FoxStorageRouting.Category)state.CategoryMask;
        int nextMask;
        if (selected == FoxStorageRouting.Category.General)
        {
            nextMask = (int)FoxStorageRouting.Category.General;
        }
        else
        {
            if ((current & FoxStorageRouting.Category.General) != 0)
            {
                current = FoxStorageRouting.Category.None;
            }

            FoxStorageRouting.Category next = current ^ selected;
            nextMask = next == FoxStorageRouting.Category.None
                ? 0
                : (int)next;
        }

        FoxGuiTheme.PlayChoice(api);
        system.SendStorageRoutingAction(
            state,
            nextMask == 0 ? FoxStorageRoutingRequestPacket.Clear : FoxStorageRoutingRequestPacket.SetMask,
            nextMask);
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

    private int FindCategoryIndex(double x, double y)
    {
        if (state?.AdvancedMode == true) return -1;
        if (x < 20 || x >= 20 + ColumnWidth * 3 || y < ListTop || y >= ListBottom)
        {
            return -1;
        }

        int column = (int)Math.Floor((x - 20) / ColumnWidth);
        int row = (int)Math.Floor((y - ListTop) / RowHeight);
        int index = row * 3 + column;
        return index >= 0 && index < options.Count ? index : -1;
    }

    private int FindAdvancedItemIndex(double x, double y)
    {
        if (state?.AdvancedMode != true
            || x < 20 || x >= 620
            || y < AdvancedListTop || y >= AdvancedListTop + AdvancedRowHeight * AdvancedVisibleRows)
        {
            return -1;
        }

        int row = (int)Math.Floor((y - AdvancedListTop) / AdvancedRowHeight);
        int index = advancedPage * AdvancedVisibleRows + row;
        return index >= 0 && index < state.AdvancedItems.Count ? index : -1;
    }

    private int AdvancedPageCount => Math.Max(1,
        (state?.AdvancedItems?.Count ?? 0) / AdvancedVisibleRows
        + ((state?.AdvancedItems?.Count ?? 0) % AdvancedVisibleRows == 0 ? 0 : 1));

    private void Redraw()
    {
        Bounds.CalcWorldBounds();
        using ImageSurface image = new(Format.Argb32, Math.Max(1, Bounds.OuterWidthInt), Math.Max(1, Bounds.OuterHeightInt));
        using Context ctx = new(image);
        ScaleCanvas(ctx);
        FoxGuiTheme.DrawJournal(ctx, (int)CanvasWidth, (int)CanvasHeight, FoxGuiSurfaceKind.Pack);

        if (helpOpen)
        {
            DrawHelp(ctx);
            FoxGuiTheme.DrawWindowControls(ctx, api, CanvasWidth);
            FoxGuiTheme.DrawHelpGlyph(ctx, CanvasWidth, helpHovered);
            generateTexture(image, ref textureId);
            return;
        }

        FoxGuiTheme.DrawWindowControls(ctx, api, CanvasWidth);
        FoxGuiTheme.DrawHelpGlyph(ctx, CanvasWidth, helpHovered);

        DrawText(ctx, "Configure storage", 20, 40, 22, White);
        DrawWrapped(ctx,
            "Choose what the pack should put here. Use broad categories for quick setup, or Advanced mode to refine by exact item.",
            20, 66, CanvasWidth - 40, 16, Muted, 3);

        FoxGuiTheme.DrawPanel(ctx, 20, 112, 600, 54, true);
        string target = string.IsNullOrWhiteSpace(state?.StorageName) ? "storage container" : state.StorageName;
        DrawText(ctx,
            FeralKinshipCompanionUiSettings.TrimTextToWidth($"Target: {target}", 300, 15),
            34, 135, 15, White);
        DrawText(ctx,
            FeralKinshipCompanionUiSettings.TrimTextToWidth($"Rules: {CurrentRules()}", 300, 14),
            34, 155, 14, Gold);

        DrawButton(ctx, 350, 126, 90, 36, state?.AdvancedMode == true ? "Categories" : "Advanced", state?.Supported == true);
        DrawButton(ctx, 445, 126, 80, 36, "General", state?.Supported == true);
        DrawButton(ctx, 530, 126, 90, 36, "Remove", state?.Supported == true);

        if (state?.AdvancedMode == true)
        {
            DrawAdvancedRules(ctx);
        }
        else
        {
            DrawText(ctx, "Categories", 20, 180, 17, Gold);
            for (int i = 0; i < options.Count; i++)
            {
                int column = i % 3;
                int row = i / 3;
                double x = 20 + column * ColumnWidth;
                double y = ListTop + row * RowHeight;
                bool selected = IsSelected(options[i].Category);
                bool hovered = hoveredIndex == i;
                DrawCategoryButton(ctx, x, y, ColumnWidth - 6, RowHeight - 5, options[i].Name, selected, hovered);
            }

            FoxGuiTheme.DrawPanel(ctx, 20, 540, 600, 68, hoveredIndex >= 0);
            string description = hoveredIndex >= 0
                ? DescriptionFor(options[hoveredIndex].Category)
                : "Hover over a category to see what it includes.";
            DrawWrapped(ctx, description, 34, 566, 570, 16, hoveredIndex >= 0 ? White : Muted, 3);
        }

        if (!string.IsNullOrWhiteSpace(state?.Message))
        {
            DrawWrapped(ctx, state.Message, 20, 640, 600, 15, Gold, 3);
        }

        if (state?.Supported != true)
        {
            FoxGuiTheme.DrawPanel(ctx, 20, 210, 600, 120, true);
            DrawWrapped(ctx, state?.Message ?? "This container cannot be configured.", 40, 250, 560, 19, White, 4);
        }

        generateTexture(image, ref textureId);
    }

    private void DrawHelp(Context ctx)
    {
        FoxGuiTheme.DrawOverlay(ctx, (int)CanvasWidth, (int)CanvasHeight, Panel);
        DrawText(ctx, Lang.Get("feralkinshipcompanions:help-storage-title"), 30, 45, 23, White);

        DrawText(ctx, Lang.Get("feralkinshipcompanions:help-storage-target-heading"), 30, 80, 16, Gold);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-storage-target-text"), 30, 103,
            CanvasWidth - 60, 17, White, 3);

        DrawText(ctx, Lang.Get("feralkinshipcompanions:help-storage-category-heading"), 30, 170, 16, Gold);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-storage-category-text"), 30, 193,
            CanvasWidth - 60, 17, Muted, 4);

        DrawText(ctx, Lang.Get("feralkinshipcompanions:help-storage-priority-heading"), 30, 275, 16, Gold);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-storage-priority-text"), 30, 298,
            CanvasWidth - 60, 17, Muted, 5);

        DrawText(ctx, Lang.Get("feralkinshipcompanions:help-storage-general-heading"), 30, 395, 16, Gold);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-storage-general-text"), 30, 418,
            CanvasWidth - 60, 17, Muted, 4);

        DrawText(ctx, Lang.Get("feralkinshipcompanions:help-storage-food-heading"), 30, 500, 16, Gold);
        DrawWrapped(ctx, Lang.Get("feralkinshipcompanions:help-storage-food-text"), 30, 523,
            CanvasWidth - 60, 17, Muted, 3);
        DrawButton(ctx, CanvasWidth - 150, CanvasHeight - 52, 120, 30,
            Lang.Get("feralkinshipcompanions:help-close"), true);
    }

    private string CurrentRules()
    {
        if (state == null || !state.Supported)
        {
            return "Unavailable";
        }

        if (state.AdvancedMode)
        {
            int included = state.AdvancedItems?.Count(item => item.Included) ?? 0;
            return $"Advanced: {included} exact item type(s) allowed";
        }

        return state.CategoryMask == 0
            ? "Unconfigured"
            : FoxStorageRouting.GetDisplayName(state.CategoryMask);
    }

    private bool IsSelected(FoxStorageRouting.Category category)
    {
        return state != null && (state.CategoryMask & (int)category) != 0;
    }

    private void DrawCategoryButton(Context ctx, double x, double y, double width, double height,
        string label, bool selected, bool hovered)
    {
        double[] fill = selected || hovered ? Selected : Panel;
        FoxGuiTheme.DrawPanel(ctx, x, y, width, height, selected || hovered);
        if (selected)
        {
            DrawRect(ctx, x, y, 4, height, Gold);
        }
        DrawText(ctx, selected ? "●" : "○", x + 12, y + 28, 15, selected ? Gold : Muted);
        DrawText(ctx, label, x + 34, y + 28, 14, selected ? Gold : White);
    }

    private void DrawAdvancedRules(Context ctx)
    {
        DrawText(ctx, "Exact item rules", 20, 194, 17, Gold);
        DrawButton(ctx, 460, 174, 160, 30, "Add current contents", state?.Supported == true);

        List<FoxStorageRoutingItemRulePacket> items = state?.AdvancedItems
            ?? new List<FoxStorageRoutingItemRulePacket>();
        int first = advancedPage * AdvancedVisibleRows;
        for (int row = 0; row < AdvancedVisibleRows; row++)
        {
            int index = first + row;
            if (index >= items.Count) break;

            FoxStorageRoutingItemRulePacket item = items[index];
            double y = AdvancedListTop + row * AdvancedRowHeight;
            bool hovered = hoveredItemIndex == index;
            FoxGuiTheme.DrawPanel(ctx, 20, y, 600, AdvancedRowHeight - 3, item.Included || hovered);
            if (item.Included) DrawRect(ctx, 20, y, 4, AdvancedRowHeight - 3, Gold);

            double[] ruleColor = item.Included ? Gold : item.Excluded ? Muted : White;
            DrawText(ctx, item.Included ? "●" : "○", 32, y + 19, 15, ruleColor);
            DrawText(ctx, Shorten(item.DisplayName, 42), 54, y + 17, 14, item.Included ? Gold : White);

            string details = item.CategoryName;
            if (item.PresentInStorage) details += $" · in container ×{item.QuantityInStorage}";
            else details += " · saved rule";
            details += $" · {Shorten(item.ItemCode, 34)}";
            DrawText(ctx, details, 54, y + 34, 10, Muted);

            string status = item.Included ? "Allowed" : item.Excluded ? "Excluded" : "Blocked";
            DrawText(ctx, status, 520, y + 25, 12, ruleColor);
        }

        if (items.Count == 0)
        {
            FoxGuiTheme.DrawPanel(ctx, 20, AdvancedListTop, 600, 74, true);
            DrawWrapped(ctx,
                "No sample items found. Put items in this container, then use Add current contents.",
                36, AdvancedListTop + 29, 560, 16, White, 2);
        }

        int pageCount = AdvancedPageCount;
        DrawButton(ctx, 20, 518, 100, 30, "Previous", pageCount > 1 && advancedPage > 0);
        DrawText(ctx, $"Page {advancedPage + 1} / {pageCount} · {items.Count} tracked types", 210, 538, 13, Muted);
        DrawButton(ctx, 500, 518, 120, 30, "Next", pageCount > 1 && advancedPage < pageCount - 1);

        FoxGuiTheme.DrawPanel(ctx, 20, 555, 600, 68, hoveredItemIndex >= 0);
        DrawWrapped(ctx,
            "Advanced mode accepts only checked exact collectible types. New types stay blocked until added. Category labels are hints; item-family rules are not part of this first pass.",
            34, 580, 570, 16, Muted, 3);
    }

    private static string Shorten(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value ?? string.Empty;
        return "…" + value.Substring(value.Length - Math.Max(1, maxLength - 1));
    }

    private void DrawButton(Context ctx, double x, double y, double width, double height, string label, bool enabled)
    {
        FoxGuiTheme.DrawPanel(ctx, x, y, width, height, false);
        ctx.SetSourceRGBA(Gold[0], Gold[1], Gold[2], enabled ? Gold[3] : 0.35);
        ctx.Rectangle(x, y, width, height);
        ctx.Stroke();
        double labelWidth = FeralKinshipCompanionUiSettings.GetTextWidth(label, 15);
        DrawText(ctx, label, x + Math.Max(8, (width - labelWidth) / 2), y + 24, 15, enabled ? White : Muted);
    }

    private static string DescriptionFor(FoxStorageRouting.Category category) => category switch
    {
        FoxStorageRouting.Category.General => "Accepts any item that fits. This is the default behavior of Pack Collection Boxes.",
        FoxStorageRouting.Category.Food => "Anything the player can eat, including modded food, wine, and other edible items.",
        FoxStorageRouting.Category.Seeds => "Planting seeds and other seed items.",
        FoxStorageRouting.Category.Logs => "Whole logs and other direct tree-log drops.",
        FoxStorageRouting.Category.Sticks => "Sticks gathered from trees and the ground.",
        FoxStorageRouting.Category.LeavesAndBranches => "Leaves, branchy leaves, and similar tree debris.",
        FoxStorageRouting.Category.Flowers => "Flowers and other flower-like forage.",
        FoxStorageRouting.Category.GrassAndReeds => "Grass, ferns, reeds, cattails, and similar plants.",
        FoxStorageRouting.Category.Ore => "Ore-bearing items produced by mining.",
        FoxStorageRouting.Category.Stone => "Rocks, stone, gravel, cobblestone, and boulder drops.",
        FoxStorageRouting.Category.Flint => "Flint pieces.",
        FoxStorageRouting.Category.Clay => "Clay items and clay gathered by digging.",
        FoxStorageRouting.Category.RuinsSalvage => "Items identified as ruins, ancient salvage, or other recovered remnants.",
        FoxStorageRouting.Category.SoilAndSand => "Dirt, soil, sand, and related digging materials.",
        FoxStorageRouting.Category.FiberAndTextiles => "Flax, fiber, hemp, twine, rope, cloth, and linen.",
        FoxStorageRouting.Category.AnimalMaterials => "Hides, pelts, leather, bones, feathers, horns, antlers, and fangs.",
        FoxStorageRouting.Category.MetalAndScrap => "Ingots, nuggets, metal parts, plates, wire, and scrap.",
        FoxStorageRouting.Category.Fuel => "Coal, charcoal, firewood, peat, and similar fuel.",
        FoxStorageRouting.Category.Tools => "Axes, pickaxes, shovels, hoes, chisels, hammers, saws, and knives.",
        FoxStorageRouting.Category.PotteryAndVessels => "Pottery, ceramic items, crocks, jugs, urns, and vessels.",
        FoxStorageRouting.Category.WoodProducts => "Planks, boards, lumber, fences, and other processed wood.",
        _ => "Items in this category are reserved for future pack duties."
    };

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
        string[] words = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string line = string.Empty;
        int drawn = 0;
        foreach (string word in words)
        {
            if (drawn >= maxLines) break;
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
}
