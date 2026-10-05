#nullable disable

using Cairo;
using Vintagestory.API.Client;

namespace FeralKinshipCompanions;

/// <summary>
/// A brief first-name prompt shown for a new companion or a requested reroll.
/// Keeping the suggested name is the default; choosing a name opens the
/// existing authoritative rename flow.
/// </summary>
public sealed class GuiDialogFeralKinshipNameSuggestion : GuiDialog
{
    private const double DialogWidth = 430;
    private const double DialogHeight = 180;
    private readonly FeralKinshipCompanionSystem system;
    private readonly long targetEntityId;
    private readonly string suggestedName;
    private readonly bool isNewborn;

    public GuiDialogFeralKinshipNameSuggestion(
        ICoreClientAPI capi,
        FeralKinshipCompanionSystem system,
        long targetEntityId,
        string suggestedName,
        bool isNewborn = false)
        : base(capi)
    {
        this.system = system;
        this.targetEntityId = targetEntityId;
        this.suggestedName = suggestedName ?? string.Empty;
        this.isNewborn = isNewborn;
    }

    public override string ToggleKeyCombinationCode => null;

    public override void OnGuiOpened()
    {
        ElementBounds contentBounds = ElementBounds.Fixed(0, 0, DialogWidth, DialogHeight)
            .WithFixedPadding(0);
        contentBounds.BothSizing = ElementSizing.Fixed;
        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle);

        SingleComposer = capi.Gui
            .CreateCompo("feralkinship-name-suggestion", dialogBounds)
            .AddShadedDialogBG(contentBounds)
            .AddDialogTitleBar(isNewborn ? "A new child" : "A new companion", OnTitleBarClose)
            .BeginChildElements(contentBounds)
                .AddStaticText(
                    isNewborn
                        ? $"\"{suggestedName}\" is a suggestion for your new child. Keep it, or choose a name yourself."
                        : $"\"{suggestedName}\" is only a suggestion. Keep it, or choose a name yourself.",
                    CairoFont.WhiteSmallText().WithFontSize(15),
                    ElementBounds.Fixed(24, 38, 382, 46))
                .AddSmallButton(
                    "Pick a name",
                    OnPickName,
                    ElementBounds.Fixed(24, 120, 170, 32))
                .AddSmallButton(
                    $"Keep {suggestedName}",
                    OnKeepSuggestion,
                    ElementBounds.Fixed(236, 120, 170, 32))
            .EndChildElements()
            .Compose();

        base.OnGuiOpened();
    }

    private void OnTitleBarClose()
    {
        TryClose();
    }

    private bool OnPickName()
    {
        TryClose();
        system.TryOpenFoxRenameGui(targetEntityId, string.Empty, fromNameSuggestion: true);
        return true;
    }

    private bool OnKeepSuggestion()
    {
        TryClose();
        return true;
    }
}
