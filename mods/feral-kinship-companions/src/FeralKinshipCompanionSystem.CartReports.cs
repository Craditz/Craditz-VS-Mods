#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Config;

namespace FeralKinshipCompanions;
internal sealed class CartFinding
{
    public string Key { get; set; } = "";
    public string Text { get; set; } = "";
    public List<QuarryPoint> Positions { get; set; } = new();
}
[ProtoContract]
public sealed class CartReportRequest
{
    [ProtoMember(1)] public int X { get; set; }
    [ProtoMember(2)] public int Y { get; set; }
    [ProtoMember(3)] public int Z { get; set; }
    [ProtoMember(4)] public int Dimension { get; set; }
}
[ProtoContract]
public sealed class CartReportResponse
{
    [ProtoMember(1)] public string Title { get; set; } = "";
    [ProtoMember(2)] public string[] Findings { get; set; } = Array.Empty<string>();
}
public sealed partial class FeralKinshipCompanionSystem
{
    private IClientNetworkChannel? cartReportClient;
    private IServerNetworkChannel? cartReportServer;
    private GuiDialogCartReport? cartReportDialog;
    private readonly Dictionary<string, long> cartReportRequests = new();
    private void RegisterCartReportsServer(ICoreServerAPI api)
    {
        cartReportServer = api.Network.RegisterChannel("feralkinship-cart-reports")
            .RegisterMessageType<CartReportRequest>().RegisterMessageType<CartReportResponse>()
            .SetMessageHandler<CartReportRequest>((player, request) =>
            {
                if (packRepository == null || request.Y < 0 || request.Y >= BlockPos.DimensionBoundary) return;
                long now = api.World.ElapsedMilliseconds;
                if (cartReportRequests.GetValueOrDefault(player.PlayerUID) > now) return;
                cartReportRequests[player.PlayerUID] = now + 500;
                var pos = new BlockPos(request.X, request.Y, request.Z, request.Dimension);
                var record = packRepository.GetWorkCart(pos);
                if (record?.OwnerUid != player.PlayerUID || api.World.BlockAccessor.GetChunkAtBlockPos(pos) == null
                    || api.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityFeralKinshipWorkCart cart) return;
                string[] lines = cart.Findings.SelectMany(f => new[] { f.Text,
                    $"{f.Positions.Count} recorded locations; latest: " + (f.Positions.Count == 0 ? "unknown" : $"{f.Positions[^1].X}, {f.Positions[^1].Y}, {f.Positions[^1].Z}") }).ToArray();
                cartReportServer!.SendPacket(new CartReportResponse { Title = CartReportTitle(pos), Findings = lines }, player);
            });
    }
    private void RegisterCartReportsClient(ICoreClientAPI api)
    {
        cartReportClient = api.Network.RegisterChannel("feralkinship-cart-reports")
            .RegisterMessageType<CartReportRequest>().RegisterMessageType<CartReportResponse>()
            .SetMessageHandler<CartReportResponse>(packet =>
            {
                cartReportDialog?.TryClose(); cartReportDialog?.Dispose();
                cartReportDialog = new GuiDialogCartReport(api, packet.Title, packet.Findings);
                cartReportDialog.TryOpen();
            });
        api.RegisterLinkProtocol("companioncart", link =>
        {
            string[] parts = link.Href.Replace("companioncart://", "").Split('/');
            if (parts.Length == 4 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y)
                && int.TryParse(parts[2], out int z) && int.TryParse(parts[3], out int d)) RequestCartReport(x, y, z, d);
        });
    }
    internal void RequestCartReport(int x, int y, int z, int dimension)
        => cartReportClient?.SendPacket(new CartReportRequest { X = x, Y = y, Z = z, Dimension = dimension });
    private string CartReportTitle(BlockPos pos)
    {
        var record = packRepository!.GetWorkCart(pos)!;
        var carts = packRepository.GetAllWorkCarts().Where(c => c.OwnerUid == record.OwnerUid && c.Kind == record.Kind)
            .OrderBy(c => c.Dimension).ThenBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Z).ToList();
        return (record.Kind == "quarrying" ? "Quarry " : record.Kind == "mining" ? "Mining cart " : "Logging cart ")
            + (carts.FindIndex(c => c.X == pos.X && c.Y == pos.Y && c.Z == pos.Z && c.Dimension == pos.dimension) + 1)
            + $" ({pos.X}, {pos.Y}, {pos.Z})";
    }
    private void ReportCartFinding(BlockEntityFeralKinshipWorkCart cart, BlockPos pos, string reason)
    {
        if (serverApi == null || packRepository == null) return;
        var blocks = serverApi.World.BlockAccessor;
        Block block = blocks.GetBlock(pos);
        bool ore = block.BlockMaterial == EnumBlockMaterial.Ore && !QuarryFlint(block);
        bool cave = reason.Contains("opening", StringComparison.OrdinalIgnoreCase) || reason.Contains("cave", StringComparison.OrdinalIgnoreCase);
        bool remnant = reason.StartsWith("Unreachable quarry remnant", StringComparison.Ordinal);
        if (!ore && !cave && !remnant) return;
        // Quarry discoveries are actual exposed findings, never distant buried ore.
        if (ore && !BlockFacing.ALLFACES.Any(f => blocks.GetChunkAtBlockPos(pos.AddCopy(f)) != null
            && blocks.GetBlock(pos.AddCopy(f)).Id == 0)) return;
        string key = ore ? "ore:" + block.Code : remnant ? "remnant:" + block.Code : "cave";
        var finding = cart.Findings.FirstOrDefault(f => f.Key == key);
        bool announce = finding == null;
        if (finding == null)
        {
            if (cart.Findings.Count >= 40) return;
            finding = new() { Key = key, Text = ore ? "Exposed ore: " + QuarryBlockName(block, pos) : remnant ? reason : "Underground opening encountered; barrier retained." };
            cart.Findings.Add(finding);
        }
        var point = new QuarryPoint(pos.X, pos.Y, pos.Z);
        if (finding.Positions.Contains(point)) return;
        if (finding.Positions.Count < 128) finding.Positions.Add(point);
        cart.MarkDirty(true);
        if (announce && serverApi.World.PlayerByUid(packRepository.GetWorkCart(cart.Pos)!.OwnerUid) is IServerPlayer owner)
            owner.SendMessage(GlobalConstants.GeneralChatGroup, "The companions found something at " + CartReportTitle(cart.Pos)
                + $". <a href=\"companioncart://{cart.Pos.X}/{cart.Pos.Y}/{cart.Pos.Z}/{cart.Pos.dimension}\">Open report</a>", EnumChatType.Notification);
    }
}
internal sealed class GuiDialogCartReport : GuiDialog
{
    private readonly string title;
    private readonly string[] findings;
    private int page;
    public GuiDialogCartReport(ICoreClientAPI api, string title, string[] findings) : base(api)
    { this.title = title; this.findings = findings; }
    public override string ToggleKeyCombinationCode => null!;
    public override void OnGuiOpened() { Compose(); base.OnGuiOpened(); }
    private void Compose()
    {
        var bounds = ElementBounds.Fixed(0, 0, 660, 440).WithFixedPadding(16);
        string body = findings.Length == 0 ? "No findings recorded yet." : string.Join("\n\n", findings.Skip(page * 8).Take(8));
        SingleComposer?.Dispose();
        SingleComposer = capi.Gui.CreateCompo("companion-cart-report", ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle))
            .AddShadedDialogBG(bounds).AddDialogTitleBar(title + " report", () => TryClose()).BeginChildElements(bounds)
            .AddStaticText(body, CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 12, 630, 355))
            .AddButton("Previous", () => { page = Math.Max(0, page - 1); Compose(); return true; }, ElementBounds.Fixed(0, 385, 150, 35))
            .AddButton("Next", () => { page = Math.Min(Math.Max(0, (findings.Length - 1) / 8), page + 1); Compose(); return true; }, ElementBounds.Fixed(170, 385, 150, 35))
            .EndChildElements().Compose();
    }
}
