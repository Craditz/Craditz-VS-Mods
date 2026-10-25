#nullable enable
using System;
using Vintagestory.API.Client;

namespace FeralKinshipCompanions;

public sealed class GuiDialogCompanionMissingCart : GuiDialog
{
    private readonly Action<bool> respond;
    private bool responded;
    public GuiDialogCompanionMissingCart(ICoreClientAPI api, Action<bool> respond) : base(api) { this.respond = respond; }
    public override string ToggleKeyCombinationCode => null!;
    public override void OnGuiOpened()
    {
        var bounds = ElementBounds.Fixed(0, 0, 610, 250).WithFixedPadding(15);
        SingleComposer = capi.Gui.CreateCompo("companion-missing-cart", ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle))
            .AddShadedDialogBG(bounds).AddDialogTitleBar("Pack cart missing", () => TryClose())
            .BeginChildElements(bounds)
            .AddStaticText("Your companions have returned from their expedition,\nbut your pack cart is missing.\n\nWould you like them placed safely near you, or kept\nsuspended until you replace the pack cart?",
                CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 8, 580, 160))
            .AddButton("Place near me", () => Choose(true), ElementBounds.Fixed(0, 188, 230, 35), EnumButtonStyle.Normal)
            .AddButton("Wait for a replacement cart", () => Choose(false), ElementBounds.Fixed(245, 188, 335, 35), EnumButtonStyle.Normal)
            .EndChildElements().Compose();
        base.OnGuiOpened();
    }
    private bool Choose(bool place) { if (!responded) { responded = true; respond(place); } return TryClose(); }
    public override void OnGuiClosed() { if (!responded) { responded = true; respond(false); } base.OnGuiClosed(); }
}
