#nullable disable
using System;
using System.Linq;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace FeralKinshipCompanions;

public sealed class GuiDialogFeralKinshipRecoveryConfirmation : GuiDialog
{
    private readonly FeralKinshipCompanionSystem system;
    private readonly CompanionRecoveryStatePacket preview;
    private List<string> pages = new();
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
        if (string.IsNullOrEmpty(preview.PreviewToken)) return;
        system.RespondToRecoveryPreview(preview, confirm);
    }
    private bool OnConfirm() { Respond(true); return TryClose(); }
    private bool OnCancel() { Respond(false); return TryClose(); }
    private bool OnPrevious() { page = Math.Max(0, page - 1); ComposeDialog(); return true; }
    private bool OnNext() { page = Math.Min(pages.Count - 1, page + 1); ComposeDialog(); return true; }
    private void ComposeDialog()
    {
        var content = ElementBounds.Fixed(0, 0, 800, 630).WithFixedPadding(14);
        var composer = capi.Gui.CreateCompo("feralkinship-recovery-confirmation", ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle))
            .AddShadedDialogBG(content).AddDialogTitleBar(string.IsNullOrEmpty(preview.PreviewTitle) ? "Review companion recovery" : preview.PreviewTitle, () => OnCancel()).BeginChildElements(content);
        var font = CairoFont.WhiteSmallText();
        double scale = RuntimeEnv.GUIScale;
        var textUtil = new TextDrawUtil();
        if (pages.Count == 0) {
            foreach (string part in new[] { preview.Message }.Concat(preview.PreviewEntries.Select(row => row.Name + " — " + row.Decision + "\n" + row.Reason))) {
                string wrapped = string.Join("\n", textUtil.Lineize(font, part, 770 * scale, EnumLinebreakBehavior.AfterCharacter).Select(line => line.Text.TrimEnd('\r', '\n')));
                pages.AddRange(RecoveryTextPages.Split(wrapped, value => textUtil.GetMultilineTextHeight(font, value, 770 * scale) / scale, 444));
            }
        }
        page = Math.Clamp(page, 0, pages.Count - 1);
        composer.AddStaticText(pages[page], font, ElementBounds.Fixed(0, 12, 770, 450));
        composer.AddStaticText($"Page {page + 1} / {pages.Count}. Cancel or close to decline this confirmation.",
            CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 492, 770, 28));
        composer.AddButton("Previous", OnPrevious, ElementBounds.Fixed(0, 532, 130, 32), EnumButtonStyle.Normal, "previous")
            .AddButton("Next", OnNext, ElementBounds.Fixed(140, 532, 130, 32), EnumButtonStyle.Normal, "next")
            .AddButton("CONFIRM recovery", OnConfirm, ElementBounds.Fixed(310, 532, 250, 32), EnumButtonStyle.Normal, "confirm")
            .AddButton("Cancel", OnCancel, ElementBounds.Fixed(580, 532, 190, 32), EnumButtonStyle.Normal, "cancel");
        SingleComposer = composer.EndChildElements().Compose();
        SingleComposer.GetButton("previous").SetActive(page > 0);
        SingleComposer.GetButton("next").SetActive(page + 1 < pages.Count);
        SingleComposer.GetButton("confirm").SetActive(!string.IsNullOrEmpty(preview.PreviewToken) && preview.PreviewEntries.Any(row => row.Decision != "Skip"));
    }
}
