#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

public sealed class GuiDialogFeralKinshipWorkCart : GuiDialog
{
    private readonly FeralKinshipCompanionSystem system;
    private FoxWorkCartAssignmentStatePacket state;
    private GuiElementFeralKinshipWorkCartSurface surface;
    private string selectedTab = "assign";
    private BlockEntityFeralKinshipWorkCart toolCart;
    private bool toolGridsComposed;
    private int composedToolCount;

    public GuiDialogFeralKinshipWorkCart(ICoreClientAPI capi, FeralKinshipCompanionSystem system,
        FoxWorkCartAssignmentStatePacket state) : base(capi)
    {
        this.system = system;
        this.state = state;
    }

    public override string ToggleKeyCombinationCode => null;
    public override bool UnregisterOnClose => true;

    internal bool IsAt(BlockPos pos) => state != null && state.X == pos.X && state.Y == pos.Y
        && state.Z == pos.Z && state.Dimension == pos.dimension;

    public bool IsFor(FoxWorkCartAssignmentStatePacket packet) => state != null && packet != null
        && state.X == packet.X && state.Y == packet.Y && state.Z == packet.Z && state.Dimension == packet.Dimension;

    public override void OnGuiOpened()
    {
        toolCart = state.WorkCartKind is "generic" or "logging" or "quarrying" or "mining"
            ? system.EnsureWorkCartTools(new BlockPos(state.X, state.Y, state.Z, state.Dimension)) : null;
        if (toolCart != null)
        {
            toolCart.ConfigureForKind(state.WorkCartKind);
            capi.World.Player.InventoryManager.OpenInventory(toolCart.Tools);
        }
        ComposeCart();
        base.OnGuiOpened();
    }

    private void ComposeCart()
    {
        CloseSlotGrids();
        SingleComposer?.Dispose();
        toolGridsComposed = false;
        composedToolCount = toolCart?.Tools.Count ?? 0;
        ElementBounds content = ElementBounds.Fixed(0, 0, 520, 580).WithFixedPadding(0);
        content.BothSizing = ElementSizing.Fixed;
        ElementBounds dialog = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
        GuiComposer composer = capi.Gui.CreateCompo("feralkinship-work-cart", dialog)
            .AddShadedDialogBG(content)
            .AddDialogTitleBar("Work Cart", () => TryClose())
            .BeginChildElements(content);
        surface = new GuiElementFeralKinshipWorkCartSurface(capi, ElementBounds.Fixed(0, 0, 520, 580),
            system, toolCart?.Tools, selectedTab, tab => capi.Event.EnqueueMainThreadTask(() =>
            {
                if (!IsOpened()) return;
                selectedTab = tab;
                ComposeCart();
            }, "feralkinship-workcart-tab"));
        composer.AddInteractiveElement(surface, "work-cart-surface");
        if (toolCart != null && selectedTab == "duties")
        {
            toolGridsComposed = true;
            for (int i = 0; i < WorkCartAxeInventory.ToolSlotCount; i++)
            {
                composer.AddItemSlotGrid(toolCart.Tools,
                    packet => capi.Network.SendBlockEntityPacket(toolCart.Pos.X, toolCart.Pos.InternalY,
                        toolCart.Pos.Z, packet), 1, new[] { i },
                    ElementStdBounds.SlotGrid(EnumDialogArea.None, 30 + i * 120, 250, 1, 1),
                    "workcart-tool-" + i);
            }
            if (toolCart.Tools.QuarryMode)
                composer.AddItemSlotGrid(toolCart.Tools,
                    packet => capi.Network.SendBlockEntityPacket(toolCart.Pos.X, toolCart.Pos.InternalY, toolCart.Pos.Z, packet),
                    1, new[] { 4 }, ElementStdBounds.SlotGrid(EnumDialogArea.None, 30, 350, 1, 1), "workcart-tool-4");
        }
        SingleComposer = composer.Compose();
        surface.ApplyState(state);
    }

    internal void RefreshTools()
    {
        if (IsOpened() && composedToolCount != (toolCart?.Tools.Count ?? 0)) ComposeCart();
        else surface?.ApplyState(state);
    }

    private void CloseSlotGrids()
    {
        if (SingleComposer == null || !toolGridsComposed) return;
        for (int i = 0; i < composedToolCount; i++)
            SingleComposer.GetSlotGrid("workcart-tool-" + i)?.OnGuiClosed(capi);
    }

    public override void OnGuiClosed()
    {
        CloseSlotGrids();
        if (toolCart != null)
        {
            capi.Network.SendBlockEntityPacket(toolCart.Pos, BlockEntityFeralKinshipWorkCart.CloseToolsPacketId);
            capi.World.Player.InventoryManager.CloseInventoryAndSync(toolCart.Tools);
        }
        base.OnGuiClosed();
    }

    public override void OnFinalizeFrame(float dt)
    {
        base.OnFinalizeFrame(dt);
        BlockPos pos = new(state.X, state.Y, state.Z, state.Dimension);
        if (capi.World.Player.Entity.Pos.Dimension != pos.dimension
            || capi.World.Player.Entity.Pos.SquareDistanceTo(pos.ToVec3d().Add(0.5, 0.5, 0.5)) > 64
            || (toolCart != null && capi.World.BlockAccessor.GetBlockEntity(pos) != toolCart))
            capi.Event.EnqueueMainThreadTask(() => TryClose(), "feralkinship-workcart-distance");
    }

    public void ApplyState(FoxWorkCartAssignmentStatePacket packet)
    {
        state = packet;
        RefreshTools();
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
    private readonly WorkCartAxeInventory tools;
    private readonly Action<string> selectTab;
    private int hoveredTool = -1;
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
        FeralKinshipCompanionSystem system, WorkCartAxeInventory tools, string selectedTab,
        Action<string> selectTab) : base(capi, bounds)
    {
        this.system = system;
        this.tools = tools;
        this.selectedTab = selectedTab;
        this.selectTab = selectTab;
    }

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
        if (selectedTab == "duties" && tools != null
            && ((CanvasMouseY(args.Y) >= 244 && CanvasMouseY(args.Y) <= 312)
                || (tools.QuarryMode && CanvasMouseY(args.Y) >= 344 && CanvasMouseY(args.Y) <= 412
                    && CanvasMouseX(args.X) >= 24 && CanvasMouseX(args.X) <= 94))) return;
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
                FoxGuiTheme.PlayChoice(api);
                selectTab(nextTab);
            }
            return;
        }
        if (string.Equals(selectedTab, "duties", StringComparison.Ordinal))
        {
            if (x >= 400 && y >= 198 && y <= 232 && (tools?.MiningState?.Started == true || tools?.QuarryState?.Started == true))
            { system.RequestCartReport(state.X, state.Y, state.Z, state.Dimension); return; }
            if (y >= 560 && y <= 580 && tools != null)
            {
                system.SendFoxWorkCartMining(state, false, 0, 8, rescue: true); return;
            }
            if (state.WorkCartKind == "mining" && tools?.MiningState is WorkCartMiningState mining)
            {
                if (y >= 150 && y <= 190) system.SendFoxWorkCartMining(state, !mining.Enabled, mining.Depth, mining.Length);
                else if (!mining.Started && y >= 198 && y <= 232)
                {
                    int depth = mining.Depth, length = mining.Length;
                    if (x >= 24 && x <= 60) depth = Math.Max(0, depth - 1);
                    if (x >= 160 && x <= 196) depth = Math.Min(40, depth + 1);
                    if (x >= 265 && x <= 301) length = Math.Max(8, length - 8);
                    if (x >= 425 && x <= 461) length = Math.Min(64, length + 8);
                    system.SendFoxWorkCartMining(state, mining.Enabled, depth, length, configure: true);
                }
                return;
            }
            if (state.WorkCartKind == "quarrying" && tools?.QuarryState is WorkCartQuarryState quarry)
            {
                if (y >= 150 && y <= 190)
                    system.SendFoxWorkCartQuarry(state, !quarry.Enabled, quarry.Width, quarry.Depth);
                else if (!quarry.Started && y >= 198 && y <= 232)
                {
                    int width = quarry.Width, depth = quarry.Depth;
                    if (x >= 24 && x <= 60) width = Math.Max(7, width - 2);
                    if (x >= 160 && x <= 196) width = Math.Min(21, width + 2);
                    if (x >= 265 && x <= 301) depth = Math.Max(1, depth - 1);
                    if (x >= 425 && x <= 461) depth = Math.Min(40, depth + 1);
                    system.SendFoxWorkCartQuarry(state, quarry.Enabled, width, depth, configure: true);
                }
                return;
            }
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

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseMove(api, args);
        double x = CanvasMouseX(args.X), y = CanvasMouseY(args.Y);
        int hovered = selectedTab == "duties" && y >= 244 && y <= 344 && x >= 24 && x < 504
            ? (int)((x - 24) / 120) : -1;
        if (hovered == hoveredTool) return;
        hoveredTool = hovered;
        Redraw();
    }

    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        if (selectedTab == "duties" && tools != null
            && ((CanvasMouseY(api.Input.MouseY) >= 244 && CanvasMouseY(api.Input.MouseY) <= 312)
                || (tools.QuarryMode && CanvasMouseY(api.Input.MouseY) >= 344 && CanvasMouseY(api.Input.MouseY) <= 412))) return;
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
                if (selectedTab == "duties" && tools != null) {
            DrawRect(ctx, 10, 560, 500, 20, Panel);
            DrawText(ctx, "Resume rescued workers after checking access", 24, 574, 12, Gold);
        }
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
                    : fox.AssignedElsewhere ? "Has another Work Cart — click to move" : fox.Status;
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
        if (selectedTab == "duties" && tools != null) {
            DrawRect(ctx, 10, 560, 500, 20, Panel);
            DrawText(ctx, "Resume rescued workers after checking access", 24, 574, 12, Gold);
        }
        generateTexture(image, ref textureId);
    }

    private void DrawDuties(Context ctx)
    {
        if (state.WorkCartKind is "quarrying" or "mining") { DrawQuarryDuties(ctx); return; }
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

        FoxGuiTheme.DrawPanel(ctx, 10, 210, 500, 148, true);
        DrawText(ctx, "Axes · used from left to right", 24, 234, 15, Gold);
        for (int i = 0; tools != null && i < WorkCartAxeInventory.ToolSlotCount; i++)
        {
            double x = 24 + i * 120;
            bool reserved = tools.IsReserved(i);
            ItemStack stack = tools[i].Itemstack;
            if (stack != null)
            {
                int maximum = Math.Max(1, stack.Collectible.GetMaxDurability(stack));
                double fraction = Math.Clamp(stack.Collectible.GetRemainingDurability(stack) / (double)maximum, 0, 1);
                DrawRect(ctx, x + 6, 309, 48, 4, Muted);
                DrawRect(ctx, x + 6, 309, 48 * fraction, 4, Gold);
            }
            if (reserved)
            {
                ctx.SetSourceRGBA(Gold[0], Gold[1], Gold[2], Gold[3]);
                ctx.LineWidth = 2;
                ctx.Rectangle(x + 3, 247, 57, 57);
                ctx.Stroke();
                // Draw a padlock instead of depending on font emoji support.
                ctx.Rectangle(x + 73, 272, 13, 11); ctx.Fill();
                ctx.Arc(x + 79.5, 272, 4.5, Math.PI, Math.PI * 2); ctx.Stroke();
            }
            DrawText(ctx, $"{i + 1}: {(reserved ? "Reserved" : tools[i].Empty ? "Empty" : "Available")}",
                x, 326, 12, reserved ? Gold : Muted);
        }
        int available = tools == null ? 0 : Enumerable.Range(0, WorkCartAxeInventory.ToolSlotCount)
            .Count(i => !tools.IsReserved(i) && WorkCartAxeInventory.IsUsable(tools[i], 0));
        DrawText(ctx, available == 0 && tools?.ReservedMask == 0
            ? "Needs an axe · add a usable axe to a tool slot"
            : $"{available} usable axes available · one reserved axe per tree", 24, 348, 12, Muted);
        FoxGuiTheme.DrawPanel(ctx, 10, 366, 500, 130, true);
        string job = hoveredTool >= 0 && tools?.IsReserved(hoveredTool) == true
            ? tools.ClientDescriptions[hoveredTool] : string.Empty;
        DrawWrapped(ctx, string.IsNullOrEmpty(job)
            ? $"Ordinary trees within {state.Radius:0.#} blocks; fruit trees are ignored. Helpers share one axe."
            : job, 24, 388, 470, 17, White, 2);
        DrawWrapped(ctx,
            "One durability per wood block. Unlocked axes replace broken ones. Stop logging to remove reserved axes. Enable Ground Cleanup and provide storage for tree debris.",
            24, 432, 470, 17, Muted, 4);

        if (!string.IsNullOrWhiteSpace(state.Message))
        {
            FoxGuiTheme.DrawPanel(ctx, 10, 506, 470, 54, true);
            DrawWrapped(ctx, state.Message, 20, 531, 450, 14, Gold, 2);
        }
    }

    private void DrawQuarryDuties(Context ctx)
    {
        WorkCartQuarryState q = tools?.QuarryState;
        var m = tools?.MiningState;
        DrawText(ctx, m != null ? "Mining Cart duties" : "Quarrying Cart duties", 10, 94, 17, Gold);
        DrawWrapped(ctx, m != null ? "Extract reachable ore; probe fixed tunnels. Hidden ore sensed only within 2 blocks." : "A layered quarry south of this cart. Ore, cave barriers and the staircase remain protected.",
            10, 116, CanvasWidth - 20, 15, Muted, 2);
        if (q == null) { DrawText(ctx, "Waiting for cart inventory…", 24, 180, 16, Muted); return; }
        DrawButton(ctx, 10, 150, 500, 40, m != null ? m.Enabled ? "Pause mining" : m.Started ? "Resume mining" : "Start mining"
            : q.Stage == "complete" ? "Quarry complete"
            : q.Enabled ? "Pause quarrying" : q.Started ? "Resume quarrying" : "Start quarrying", Gold);
        if (m != null ? !m.Started : !q.Started)
        {
            DrawButton(ctx, 24, 199, 36, 30, "−", Muted);
            DrawButton(ctx, 160, 199, 36, 30, "+", Muted);
            DrawButton(ctx, 265, 199, 36, 30, "−", Muted);
            DrawButton(ctx, 425, 199, 36, 30, "+", Muted);
        }
        DrawText(ctx, m != null ? $"{m.Depth} down" : $"{q.Width} × {q.Width}", 74, 220, 15, White);
        DrawText(ctx, m != null ? $"{m.Length} long" : $"{q.Depth} deep", 319, 220, 15, White);
        DrawText(ctx, "Pickaxes · used from left to right", 24, 244, 15, Gold);
        if (m != null ? m.Started : q.Started) DrawButton(ctx, 400, 198, 110, 34, "Reports", Gold);
        for (int i = 0; tools != null && i < 4; i++)
        {
            double x = 24 + i * 120;
            bool reserved = tools.IsReserved(i);
            ItemStack stack = tools[i].Itemstack;
            if (stack != null)
            {
                double fraction = Math.Clamp(stack.Collectible.GetRemainingDurability(stack)
                    / (double)Math.Max(1, stack.Collectible.GetMaxDurability(stack)), 0, 1);
                DrawRect(ctx, x + 6, 309, 48, 4, Muted);
                DrawRect(ctx, x + 6, 309, 48 * fraction, 4, Gold);
            }
            if (reserved)
            {
                ctx.SetSourceRGBA(Gold[0], Gold[1], Gold[2], Gold[3]);
                ctx.LineWidth = 2; ctx.Rectangle(x + 3, 247, 57, 57); ctx.Stroke();
                ctx.Rectangle(x + 73, 272, 13, 11); ctx.Fill();
                ctx.Arc(x + 79.5, 272, 4.5, Math.PI, Math.PI * 2); ctx.Stroke();
            }
            DrawText(ctx, $"{i + 1}: {(reserved ? "Reserved" : tools[i].Empty ? "Empty" : "Available")}",
                x, 326, 12, reserved ? Gold : Muted);
        }
        FoxGuiTheme.DrawPanel(ctx, 10, 342, 500, 70, true);
        DrawText(ctx, m != null ? "Access rock buffer" : "Staircase rock buffer", 110, 368, 15, Gold);
        DrawWrapped(ctx, $"8 matching rocks per block · refilled first{(q.PreferredRock.Length == 0 ? "" : " · " + q.PreferredRock)}",
            110, 390, 380, 15, Muted, 2);
        FoxGuiTheme.DrawPanel(ctx, 10, 420, 500, 136, true);
        string job = hoveredTool >= 0 && hoveredTool < 4 && tools?.IsReserved(hoveredTool) == true
            ? tools.ClientDescriptions[hoveredTool] : "";
        DrawWrapped(ctx, job.Length > 0 ? job : m?.Status ?? q.Status, 24, 442, 470, 16, White, 3);
        DrawText(ctx, m != null ? $"{m.Mode} · {m.Mined} mined · {m.OreMined} ore · probe {m.Cursor}/{m.ProbeStations().Count}" : $"Layer {q.Layer}/{q.Depth} · {q.MinedBlocks} mined · {q.RecoveredBlocks} recovered whole", 24, 510, 13, Gold);
        DrawText(ctx, m != null ? $"{q.RocksSpent} rocks used · {m.Blocked.Count} blocked faces · within cart radius" : $"{q.RocksSpent} rocks used · {q.ProtectedColumns.Count} protected columns", 24, 532, 13, Muted);
        if (m != null ? !m.Started : !q.Started) DrawText(ctx, m != null ? "Probe plan is fixed once mining starts." : "Size is fixed once quarrying starts.", 24, 552, 12, Muted);
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
