#nullable disable

using Cairo;
using Vintagestory.API.Client;

namespace FeralKinshipCompanions;

/// <summary>
/// Small, ordinary Vintage Story text-entry dialog for changing a Companion's
/// visible name. The server remains authoritative and stores the name through
/// the vanilla nametag behavior.
/// </summary>
public sealed class GuiDialogFeralKinshipRename : GuiDialog
{
    private const double DialogWidth = 430;
    private const double DialogHeight = 180;
    private readonly FeralKinshipCompanionSystem system;
    private readonly long targetEntityId;
    private readonly string currentName;

    public GuiDialogFeralKinshipRename(
        ICoreClientAPI capi,
        FeralKinshipCompanionSystem system,
        long targetEntityId,
        string currentName)
        : base(capi)
    {
        this.system = system;
        this.targetEntityId = targetEntityId;
        this.currentName = currentName ?? string.Empty;
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
            .CreateCompo("feralkinship-rename", dialogBounds)
            .AddShadedDialogBG(contentBounds)
            .AddDialogTitleBar("Rename companion", OnTitleBarClose)
            .BeginChildElements(contentBounds)
                .AddStaticText(
                    "Choose a name, or leave it blank to clear the custom name.",
                    CairoFont.WhiteSmallText().WithFontSize(15),
                    ElementBounds.Fixed(24, 34, 382, 28))
                .AddTextInput(
                    ElementBounds.Fixed(24, 68, 382, 34),
                    null,
                    CairoFont.TextInput().WithFontSize(18),
                    "name")
                .AddSmallButton(
                    "Cancel",
                    OnCancel,
                    ElementBounds.Fixed(24, 120, 150, 32))
                .AddSmallButton(
                    "Save",
                    OnSave,
                    ElementBounds.Fixed(256, 120, 150, 32))
            .EndChildElements()
            .Compose();

        SingleComposer.GetTextInput("name").SetValue(currentName);
        SingleComposer.GetTextInput("name").SetMaxLength(32);
        base.OnGuiOpened();
        SingleComposer.FocusElement(SingleComposer.GetTextInput("name").TabIndex);
    }

    private void OnTitleBarClose()
    {
        TryClose();
    }

    private bool OnCancel()
    {
        TryClose();
        return true;
    }

    private bool OnSave()
    {
        string name = SingleComposer.GetTextInput("name").GetText() ?? string.Empty;
        system.SendFoxSocialAction(targetEntityId, FoxSocialRequestAction.Rename, name);
        TryClose();
        return true;
    }
}
