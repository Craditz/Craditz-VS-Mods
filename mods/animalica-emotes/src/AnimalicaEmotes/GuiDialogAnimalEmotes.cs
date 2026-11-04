using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace AnimalicaEmotes;

public sealed class GuiDialogAnimalEmotes : GuiDialog
{
    private const double DialogWidth = 520;
    private const double ButtonHeight = 34;
    private const double ButtonGap = 5;
    private const double Padding = 12;

    private readonly AnimalicaEmotesSystem modSystem;

    public GuiDialogAnimalEmotes(ICoreClientAPI capi, AnimalicaEmotesSystem modSystem) : base(capi)
    {
        this.modSystem = modSystem;
    }

    public override string ToggleKeyCombinationCode => AnimalicaEmotesSystem.MenuHotkeyCode;

    public override void OnGuiOpened()
    {
        ComposeDialog();
        base.OnGuiOpened();
    }

    private void ComposeDialog()
    {
        AnimalEmoteOption[] poseButtons = modSystem.GetAvailablePosesForLocalPlayer();
        double titleOffset = GuiStyle.TitleBarHeight + Padding;
        int poseRows = (poseButtons.Length + 1) / 2;
        double stopButtonY = titleOffset + poseRows * (ButtonHeight + ButtonGap);
        double bodyHeight = titleOffset
            + poseRows * (ButtonHeight + ButtonGap)
            + ButtonHeight
            + Padding;

        ElementBounds contentBounds = ElementBounds
            .Fixed(0, 0, DialogWidth, bodyHeight)
            .WithFixedPadding(Padding);
        contentBounds.BothSizing = ElementSizing.Fixed;

        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle);

        GuiComposer composer = capi.Gui
            .CreateCompo("animalicaemotes-menu", dialogBounds)
            .AddShadedDialogBG(contentBounds)
            .AddDialogTitleBar(Lang.Get("animalicaemotes:menu-title"), () => TryClose())
            .BeginChildElements(contentBounds);

        double buttonWidth = (DialogWidth - Padding * 2 - ButtonGap) / 2;
        for (int index = 0; index < poseButtons.Length; index++)
        {
            string pose = poseButtons[index].Pose;
            string langCode = poseButtons[index].LangCode;
            int row = index / 2;
            int column = index % 2;
            double x = column * (buttonWidth + ButtonGap);
            double y = titleOffset + row * (ButtonHeight + ButtonGap);
            string capturedPose = pose;
            composer.AddSmallButton(
                Lang.Get("animalicaemotes:" + langCode),
                () => ActivatePose(capturedPose),
                ElementBounds.Fixed(x, y, buttonWidth, ButtonHeight),
                EnumButtonStyle.Normal,
                "pose-" + pose
            );
        }

        composer.AddSmallButton(
            Lang.Get("animalicaemotes:pose-stop"),
            () => ActivatePose(string.Empty),
            ElementBounds.Fixed(0, stopButtonY, DialogWidth - Padding * 2, ButtonHeight),
            EnumButtonStyle.Normal,
            "pose-stop"
        );

        SingleComposer = composer.EndChildElements().Compose();
    }

    private bool ActivatePose(string pose)
    {
        modSystem.SendPoseRequest(pose);
        return true;
    }
}
