#nullable enable

using System;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

public sealed class GuiDialogFeralKinshipCompanionBackpack : GuiDialog
{
    private readonly FeralKinshipCompanionSystem system;
    private readonly Entity companion;
    private readonly InventoryGeneric equipment;
    private readonly InventoryGeneric contents;
    private readonly bool locked;
    private bool refreshing;

    public GuiDialogFeralKinshipCompanionBackpack(
        ICoreClientAPI capi,
        FeralKinshipCompanionSystem system,
        Entity companion,
        InventoryGeneric equipment,
        InventoryGeneric contents,
        string title,
        bool locked) : base(capi)
    {
        this.system = system;
        this.companion = companion;
        this.equipment = equipment;
        this.contents = contents;
        this.locked = locked;
        Compose(title);
    }

    public override string? ToggleKeyCombinationCode => null;
    public override bool UnregisterOnClose => true;

    private void Compose(string title)
    {
        const double dialogWidth = 620;
        const int columns = 4;
        int rows = Math.Max(1, (int)Math.Ceiling(contents.Count / (double)columns));
        double slotSize = GuiElementPassiveItemSlot.unscaledSlotSize;
        double slotPadding = GuiElementItemSlotGrid.unscaledSlotPadding;
        double slotStep = slotSize + slotPadding;

        ElementBounds equipmentGrid = ElementStdBounds
            .SlotGrid(EnumDialogArea.None, 30, 88, 1, 1)
            .FixedGrow(slotPadding * 2, slotPadding * 2);
        ElementBounds contentsGrid = ElementStdBounds
            .SlotGrid(EnumDialogArea.None, 30, 194, columns, rows)
            .FixedGrow(slotPadding * 2, slotPadding * 2);

        double contentBottom = contents.Count > 0
            ? 194 + rows * slotStep + 22
            : 250;
        bool hasCargo = contents.Any(slot => !slot.Empty);
        string status = locked
            ? "Delivery in progress. The backpack is locked."
            : equipment[0].Empty
                ? "Place any backpack here. Its inventory will appear below."
                : !hasCargo
                    ? "Backpack is empty. Add cargo before starting a delivery."
                    : string.Empty;
        double statusHeight = string.IsNullOrEmpty(status) ? 0 : 58;
        double buttonTop = contentBottom + statusHeight + 12;
        double dialogHeight = equipment[0].Empty
            ? contentBottom + statusHeight + 18
            : buttonTop + 64;
        ElementBounds contentBounds = ElementBounds.Fixed(0, 0, dialogWidth, dialogHeight)
            .WithFixedPadding(0);
        contentBounds.BothSizing = ElementSizing.Fixed;
        ElementBounds dialog = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle);

        GuiComposer composer = capi.Gui
            .CreateCompo("feralkinship-companion-backpack-" + companion.EntityId, dialog)
            .AddShadedDialogBG(contentBounds)
            .AddDialogTitleBar(title, () => TryClose())
            .BeginChildElements(contentBounds);

        var surface = new GuiElementFeralKinshipCompanionBackpackSurface(
            capi,
            ElementBounds.Fixed(0, 0, dialogWidth, dialogHeight),
            title,
            status,
            equipment[0].Empty,
            contents.Count,
            contentBottom,
            buttonTop,
            locked || !hasCargo,
            StartDelivery);
        composer
            .AddInteractiveElement(surface, "backpack-theme-surface")
            .AddItemSlotGrid(
                equipment,
                packet => capi.Network.SendEntityPacketWithOffset(
                    companion.EntityId,
                    EntityBehaviorFeralKinshipFoxSocial.BackpackEquipmentPacketOffset,
                    packet),
                1,
                equipmentGrid,
                "backpack-equipment");

        if (contents.Count > 0)
        {
            composer
                .AddItemSlotGrid(
                    contents,
                    packet => capi.Network.SendEntityPacketWithOffset(
                        companion.EntityId,
                        EntityBehaviorFeralKinshipFoxSocial.BackpackContentsPacketOffset,
                        packet),
                    columns,
                    contentsGrid,
                    "backpack-contents");
        }

        SingleComposer = composer.Compose();
    }

    private bool StartDelivery(bool returnToPlayer)
    {
        if (locked || !contents.Any(slot => !slot.Empty)) return false;
        system.SendCompanionBackpackAction(
            companion.EntityId,
            returnToPlayer
                ? CompanionBackpackAction.UnloadAndReturn
                : CompanionBackpackAction.UnloadAtHome);
        TryClose();
        return true;
    }

    public override void OnGuiClosed()
    {
        if (!refreshing)
        {
            system.SendCompanionBackpackAction(companion.EntityId, CompanionBackpackAction.Close);
            capi.World.Player.InventoryManager.CloseInventoryAndSync(equipment);
            capi.World.Player.InventoryManager.CloseInventoryAndSync(contents);
        }
        SingleComposer?.GetSlotGrid("backpack-equipment")?.OnGuiClosed(capi);
        SingleComposer?.GetSlotGrid("backpack-contents")?.OnGuiClosed(capi);
        base.OnGuiClosed();
    }

    internal void CloseForRefresh()
    {
        refreshing = true;
        capi.World.Player.InventoryManager.CloseInventory(equipment);
        capi.World.Player.InventoryManager.CloseInventory(contents);
        TryClose();
    }

    public override void OnFinalizeFrame(float dt)
    {
        base.OnFinalizeFrame(dt);
        if (!companion.Alive
            || capi.World.Player.Entity.Pos.SquareDistanceTo(companion.Pos.XYZ) > 8 * 8)
        {
            capi.Event.EnqueueMainThreadTask(() => TryClose(), "close-companion-backpack");
        }
    }
}

internal sealed class GuiElementFeralKinshipCompanionBackpackSurface
    : GuiElementFeralKinshipScaledSurface
{
    private const double EquipmentPanelY = 54;
    private const double EquipmentPanelHeight = 102;
    private const double ContentsPanelY = 166;
    private const double HomeButtonX = 18;
    private const double ReturnButtonX = 316;
    private const double ButtonWidth = 286;
    private const double ButtonHeight = 46;

    private readonly double designHeight;
    private readonly string title;
    private readonly string status;
    private readonly bool backpackEmpty;
    private readonly int contentSlotCount;
    private readonly double contentBottom;
    private readonly double buttonTop;
    private readonly bool deliveryDisabled;
    private readonly System.Func<bool, bool> startDelivery;
    private int textureId;

    protected override double DesignWidth => 620;
    protected override double DesignHeight => designHeight;

    internal GuiElementFeralKinshipCompanionBackpackSurface(
        ICoreClientAPI capi,
        ElementBounds bounds,
        string title,
        string status,
        bool backpackEmpty,
        int contentSlotCount,
        double contentBottom,
        double buttonTop,
        bool deliveryDisabled,
        System.Func<bool, bool> startDelivery)
        : base(capi, bounds)
    {
        designHeight = bounds.fixedHeight;
        this.title = title;
        this.status = status;
        this.backpackEmpty = backpackEmpty;
        this.contentSlotCount = contentSlotCount;
        this.contentBottom = contentBottom;
        this.buttonTop = buttonTop;
        this.deliveryDisabled = deliveryDisabled;
        this.startDelivery = startDelivery;
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
        if (args.Button != EnumMouseButton.Left) return;

        double x = CanvasMouseX(args.X);
        double y = CanvasMouseY(args.Y);
        if (HandleFontScaleClick(x, y))
        {
            args.Handled = true;
            Redraw();
            return;
        }

        if (backpackEmpty || y < buttonTop || y > buttonTop + ButtonHeight) return;
        bool home = x >= HomeButtonX && x <= HomeButtonX + ButtonWidth;
        bool returnToPlayer = x >= ReturnButtonX && x <= ReturnButtonX + ButtonWidth;
        if (!home && !returnToPlayer) return;

        args.Handled = true;
        if (deliveryDisabled)
        {
            FoxGuiTheme.PlayUnavailable(api);
            return;
        }

        FoxGuiTheme.PlayAction(api);
        startDelivery(returnToPlayer);
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

    private void Redraw()
    {
        Bounds.CalcWorldBounds();
        int width = Math.Max(1, Bounds.OuterWidthInt);
        int height = Math.Max(1, Bounds.OuterHeightInt);
        using ImageSurface image = new(Format.Argb32, width, height);
        using Context ctx = new(image);

        ScaleCanvas(ctx);
        FoxGuiTheme.DrawJournal(ctx, CanvasWidth, CanvasHeight, FoxGuiSurfaceKind.Companion);
        FoxGuiTheme.DrawWindowControls(ctx, api, CanvasWidth);
        DrawText(ctx, Trim(title, 34), 18, 29, 20, FoxGuiTheme.Text);
        FoxGuiTheme.DrawSectionRule(ctx, 18, 43, CanvasWidth - 36);

        FoxGuiTheme.DrawPanel(ctx, 18, EquipmentPanelY, CanvasWidth - 36, EquipmentPanelHeight, false);
        DrawText(ctx, "Backpack", 30, 78, 16, FoxGuiTheme.Accent);
        DrawText(ctx,
            backpackEmpty ? "Equip one held bag here." : "Equipped bag",
            100, 117, 14, FoxGuiTheme.Muted);

        double contentsPanelHeight = Math.Max(70, contentBottom - ContentsPanelY - 8);
        FoxGuiTheme.DrawPanel(ctx, 18, ContentsPanelY, CanvasWidth - 36, contentsPanelHeight, false);
        DrawText(ctx, "Contents", 30, 188, 16, FoxGuiTheme.Accent);
        if (contentSlotCount == 0)
        {
            DrawText(ctx, "The equipped backpack's inventory appears here.",
                30, 224, 14, FoxGuiTheme.Muted);
        }
        else
        {
            DrawText(ctx,
                $"{contentSlotCount} slot{(contentSlotCount == 1 ? string.Empty : "s")}",
                276, 188, 13, FoxGuiTheme.Muted);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            FoxGuiTheme.DrawPanel(ctx, 18, contentBottom, CanvasWidth - 36, 46, true);
            DrawText(ctx, status, 30, contentBottom + 29, 14,
                deliveryDisabled ? FoxGuiTheme.Warning : FoxGuiTheme.Text);
        }

        if (!backpackEmpty)
        {
            DrawButton(ctx, "Go home and unload", HomeButtonX, buttonTop, ButtonWidth, ButtonHeight);
            DrawButton(ctx, "Go home, unload and come back", ReturnButtonX, buttonTop, ButtonWidth, ButtonHeight);
        }

        generateTexture(image, ref textureId);
    }

    private void DrawButton(Context ctx, string label, double x, double y, double width, double height)
    {
        FoxGuiTheme.DrawButtonSurface(ctx, x, y, width, height, !deliveryDisabled);
        DrawText(ctx, label, x + 14, y + height / 2 + 6, 15,
            deliveryDisabled ? FoxGuiTheme.Muted : FoxGuiTheme.Text);
    }

    private static void DrawText(
        Context ctx,
        string text,
        double x,
        double baseline,
        double size,
        double[] color)
    {
        CairoFont font = CairoFont.WhiteSmallText()
            .WithFontSize((float)FeralKinshipCompanionUiSettings.ScaleFont(size));
        font.Color = color;
        font.SetupContext(ctx);
        ctx.MoveTo(x, baseline);
        ctx.ShowText(text ?? string.Empty);
        font.Dispose();
    }

    private static string Trim(string value, int max) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Length <= max ? value : value[..Math.Max(1, max - 1)] + "…";
}
