#nullable disable
using System;
using System.Linq;
using Vintagestory.API.Client;

namespace FeralKinshipCompanions;

public sealed class GuiDialogFeralKinshipRecoveryConfirmation : GuiDialog
{
    private readonly FeralKinshipCompanionSystem system;
    private readonly CompanionRecoveryStatePacket preview;
    private const int PageSize = 3;
    private int page;
    private bool responded;
    public GuiDialogFeralKinshipRecoveryConfirmation(ICoreClientAPI api, FeralKinshipCompanionSystem system, CompanionRecoveryStatePacket preview) : base(api)
    { this.system = system; this.preview = preview; }
    public override string ToggleKeyCombinationCode => null;
    public override void OnGuiOpened() { ComposeDialog(); base.OnGuiOpened(); }
    public override void OnGuiClosed() { Respond(false); base.OnGuiClosed(); }
    private void Respond(bool confirm)
    {
        if (responded) return;
        responded = true;
        system.RespondToRecoveryPreview(preview, confirm);
    }
    private bool OnConfirm() { Respond(true); return TryClose(); }
    private bool OnCancel() { Respond(false); return TryClose(); }
    private bool OnPrevious() { page = Math.Max(0, page - 1); ComposeDialog(); return true; }
    private bool OnNext() { page = Math.Min(Math.Max(0, (preview.PreviewEntries.Count - 1) / PageSize), page + 1); ComposeDialog(); return true; }
    private void ComposeDialog()
    {
        var content = ElementBounds.Fixed(0, 0, 800, 630).WithFixedPadding(14);
        var composer = capi.Gui.CreateCompo("feralkinship-recovery-confirmation", ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle))
            .AddShadedDialogBG(content).AddDialogTitleBar("Review companion recovery", () => OnCancel()).BeginChildElements(content);
        composer.AddStaticText(preview.Message, CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 12, 770, 100));
        var visible = preview.PreviewEntries.Skip(page * PageSize).Take(PageSize).ToArray();
        for (int i = 0; i < visible.Length; i++) {
            var row = visible[i];
            composer.AddStaticText(row.Name + " — " + row.Decision + "\n" + row.Reason,
                CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 125 + i * 120, 770, 114));
        }
        composer.AddStaticText($"Page {page + 1} / {Math.Max(1, (preview.PreviewEntries.Count + PageSize - 1) / PageSize)}. Cancel or close to leave every animal unchanged.",
            CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 492, 770, 28));
        composer.AddButton("Previous", OnPrevious, ElementBounds.Fixed(0, 532, 130, 32), EnumButtonStyle.Normal, "previous")
            .AddButton("Next", OnNext, ElementBounds.Fixed(140, 532, 130, 32), EnumButtonStyle.Normal, "next")
            .AddButton("CONFIRM recovery", OnConfirm, ElementBounds.Fixed(310, 532, 250, 32), EnumButtonStyle.Normal, "confirm")
            .AddButton("Cancel", OnCancel, ElementBounds.Fixed(580, 532, 190, 32), EnumButtonStyle.Normal, "cancel");
        SingleComposer = composer.EndChildElements().Compose();
        SingleComposer.GetButton("previous").SetActive(page > 0);
        SingleComposer.GetButton("next").SetActive((page + 1) * PageSize < preview.PreviewEntries.Count);
        SingleComposer.GetButton("confirm").SetActive(!string.IsNullOrEmpty(preview.PreviewToken) && preview.PreviewEntries.Any(row => row.Decision != "Skip"));
    }
}
