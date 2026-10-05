#nullable enable

using System;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

/// <summary>A saved roster snapshot with no companion or Pack action controls.</summary>
public sealed class GuiDialogFeralKinshipCartInspection : GuiDialog
{
    private const int PageSize = 8;
    private readonly FeralKinshipCompanionSystem system;
    private readonly BlockPos position;
    private DeveloperCartInspectionStatePacket? state;
    private int page;

    public GuiDialogFeralKinshipCartInspection(
        ICoreClientAPI capi, FeralKinshipCompanionSystem system, BlockPos position) : base(capi)
    {
        this.system = system;
        this.position = position;
    }

    public override string ToggleKeyCombinationCode => null!;

    public override void OnGuiOpened()
    {
        ComposeDialog();
        base.OnGuiOpened();
    }

    public void ApplyState(DeveloperCartInspectionStatePacket packet)
    {
        // Late replies for another cart must not replace this cart's roster.
        if (!IsOpened() || packet.X != position.X || packet.Y != position.Y
            || packet.Z != position.Z || packet.Dimension != position.dimension) return;
        state = packet;
        page = Math.Clamp(page, 0, PageCount - 1);
        ComposeDialog();
    }

    private int PageCount => Math.Max(1, ((state?.Records.Count ?? 0) + PageSize - 1) / PageSize);

    private void ComposeDialog()
    {
        ElementBounds content = ElementBounds.Fixed(0, 0, 640, 620).WithFixedPadding(14);
        content.BothSizing = ElementSizing.Fixed;
        GuiComposer composer = capi.Gui.CreateCompo("feralkinship-cart-inspection",
                ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle))
            .AddShadedDialogBG(content)
            .AddDialogTitleBar("Pack Cart - Read-only animal list", () => TryClose())
            .BeginChildElements(content);

        composer.AddStaticText(state?.Available == true
                ? $"{state.Records.Count} active records - Page {page + 1}/{PageCount}"
                : "Pack Cart inspection", CairoFont.WhiteMediumText(), ElementBounds.Fixed(0, 12, 640, 28));
        composer.AddStaticText(state == null ? "Reading saved pack records from the server..." : state.Message,
            CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 48, 640, 42));
        composer.AddInset(ElementBounds.Fixed(0, 100, 640, 462), 2, 0.72f);

        if (state?.Available == true && state.Records.Count == 0)
        {
            composer.AddStaticText("This cart's owner has no active Companion records.",
                CairoFont.WhiteSmallText(), ElementBounds.Fixed(14, 120, 612, 42));
        }
        for (int slot = 0; state?.Available == true && slot < PageSize; slot++)
        {
            int index = page * PageSize + slot;
            if (index >= state.Records.Count) break;
            DeveloperCartInspectionEntryPacket record = state.Records[index];
            int y = 110 + slot * 56;
            string name = string.IsNullOrWhiteSpace(record.Name) ? "Unnamed Companion" : record.Name;
            composer.AddStaticText($"#{record.Number}  {Clip(name, 36)}  -  {Clip(record.Species, 24)}",
                CairoFont.WhiteSmallishText(), ElementBounds.Fixed(14, y, 612, 24));
            composer.AddStaticText($"{(record.EntityLoaded ? "In world" : "Saved / not loaded")}  -  Saved status: {Clip(record.Status, 52)}",
                CairoFont.WhiteSmallText(), ElementBounds.Fixed(14, y + 25, 612, 24));
        }

        composer.AddSmallButton("Previous", () => ChangePage(-1), ElementBounds.Fixed(0, 578, 144, 32),
                EnumButtonStyle.Normal, "previous")
            .AddSmallButton("Next", () => ChangePage(1), ElementBounds.Fixed(154, 578, 144, 32),
                EnumButtonStyle.Normal, "next")
            .AddSmallButton("Refresh", Refresh, ElementBounds.Fixed(308, 578, 154, 32), EnumButtonStyle.Normal, "refresh")
            .AddSmallButton("Close", TryClose, ElementBounds.Fixed(472, 578, 168, 32), EnumButtonStyle.Normal, "close")
            .EndChildElements();
        SingleComposer = composer.Compose();
        SingleComposer.GetButton("previous").Enabled = page > 0;
        SingleComposer.GetButton("next").Enabled = page + 1 < PageCount;
    }

    private bool ChangePage(int delta)
    {
        page = Math.Clamp(page + delta, 0, PageCount - 1);
        ComposeDialog();
        return true;
    }

    private bool Refresh()
    {
        state = null;
        page = 0;
        ComposeDialog();
        system.RequestDeveloperCartInspection(position);
        return true;
    }

    private static string Clip(string? text, int limit)
    {
        string value = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
        return value.Length <= limit ? value : value[..(limit - 3)] + "...";
    }
}
