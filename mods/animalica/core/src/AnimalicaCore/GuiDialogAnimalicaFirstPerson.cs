using System.Globalization;
using Vintagestory.API.Client;

namespace AnimalicaCore;

public sealed class GuiDialogAnimalicaFirstPerson : GuiDialog
{
    private const double DialogWidth = 500;
    private const double Padding = 12;
    private const double InputWidth = 92;
    private const double ButtonGap = 6;

    private readonly AnimalicaSeraphFirstPersonSystem system;
    private bool refreshing;

    public GuiDialogAnimalicaFirstPerson(
        ICoreClientAPI capi,
        AnimalicaSeraphFirstPersonSystem system
    ) : base(capi)
    {
        this.system = system;
    }

    public override string ToggleKeyCombinationCode =>
        AnimalicaSeraphFirstPersonSystem.PositioningHotkeyCode;

    public override void OnGuiOpened()
    {
        ComposeDialog();
        base.OnGuiOpened();
    }

    private void ComposeDialog()
    {
        const double bodyHeight = 590;
        double titleY = GuiStyle.TitleBarHeight + Padding;

        ElementBounds contentBounds = ElementBounds
            .Fixed(0, 0, DialogWidth, bodyHeight)
            .WithFixedPadding(Padding);
        contentBounds.BothSizing = ElementSizing.Fixed;

        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.LeftMiddle)
            .WithFixedAlignmentOffset(20, 0);

        GuiComposer composer = capi.Gui
            .CreateCompo("animalicacore-firstperson-positioning", dialogBounds)
            .AddShadedDialogBG(contentBounds)
            .AddDialogTitleBar("Animalica first-person positioning", () => TryClose())
            .BeginChildElements(contentBounds);

        composer.AddStaticText(
            "Changes apply live and save immediately. Positive X moves right, positive Y moves up, and positive Z moves back toward the camera.",
            CairoFont.WhiteSmallText(),
            ElementBounds.Fixed(0, titleY, DialogWidth - Padding * 2, 50)
        );

        double stateY = titleY + 54;
        composer.AddDynamicText(
            string.Empty,
            CairoFont.WhiteDetailText(),
            ElementBounds.Fixed(0, stateY, DialogWidth - Padding * 2, 34),
            "feature-state"
        );

        double stepY = stateY + 44;
        composer.AddStaticText(
            "Arrow step",
            CairoFont.WhiteDetailText(),
            ElementBounds.Fixed(0, stepY, 120, 34)
        );
        composer.AddNumberInput(
            ElementBounds.Fixed(125, stepY, InputWidth, 34),
            OnStepChanged,
            CairoFont.WhiteDetailText(),
            "position-step"
        );

        double mainY = stepY + 48;
        AddHandControls(composer, false, "Main hand", "main", mainY);

        double offY = mainY + 142;
        AddHandControls(composer, true, "Offhand", "off", offY);

        double valuesY = offY + 142;
        composer.AddDynamicText(
            string.Empty,
            CairoFont.WhiteDetailText(),
            ElementBounds.Fixed(0, valuesY, DialogWidth - Padding * 2, 54),
            "current-values"
        );

        composer.AddSmallButton(
            "Close",
            () => TryClose(),
            ElementBounds.Fixed(0, valuesY + 58, DialogWidth - Padding * 2, 34),
            EnumButtonStyle.Normal,
            "close"
        );

        SingleComposer = composer.EndChildElements().Compose();
        RefreshValues();
    }

    private void AddHandControls(
        GuiComposer composer,
        bool offHand,
        string title,
        string keyPrefix,
        double y
    )
    {
        composer.AddStaticText(
            title,
            CairoFont.WhiteDetailText(),
            ElementBounds.Fixed(0, y, 110, 30)
        );
        composer.AddSmallButton(
            "Reset",
            () => ResetHand(offHand),
            ElementBounds.Fixed(DialogWidth - Padding * 2 - 90, y - 4, 90, 30),
            EnumButtonStyle.Normal,
            keyPrefix + "-reset"
        );

        double inputY = y + 32;
        AddAxisInput(composer, offHand, keyPrefix, "X", 0, inputY);
        AddAxisInput(composer, offHand, keyPrefix, "Y", 155, inputY);
        AddAxisInput(composer, offHand, keyPrefix, "Z", 310, inputY);

        double buttonsY = inputY + 42;
        double buttonWidth = (DialogWidth - Padding * 2 - ButtonGap * 5) / 6;
        AddMoveButton(composer, "Left", offHand, -1, 0, 0, 0, buttonsY, buttonWidth, keyPrefix + "-left");
        AddMoveButton(composer, "Up", offHand, 0, 1, 0, buttonWidth + ButtonGap, buttonsY, buttonWidth, keyPrefix + "-up");
        AddMoveButton(composer, "Down", offHand, 0, -1, 0, (buttonWidth + ButtonGap) * 2, buttonsY, buttonWidth, keyPrefix + "-down");
        AddMoveButton(composer, "Right", offHand, 1, 0, 0, (buttonWidth + ButtonGap) * 3, buttonsY, buttonWidth, keyPrefix + "-right");
        AddMoveButton(composer, "Forward", offHand, 0, 0, -1, (buttonWidth + ButtonGap) * 4, buttonsY, buttonWidth, keyPrefix + "-forward");
        AddMoveButton(composer, "Back", offHand, 0, 0, 1, (buttonWidth + ButtonGap) * 5, buttonsY, buttonWidth, keyPrefix + "-back");
    }

    private void AddAxisInput(
        GuiComposer composer,
        bool offHand,
        string keyPrefix,
        string axis,
        double x,
        double y
    )
    {
        string key = keyPrefix + "-" + axis.ToLowerInvariant();
        composer.AddStaticText(
            axis,
            CairoFont.WhiteDetailText(),
            ElementBounds.Fixed(x, y, 22, 34)
        );
        composer.AddNumberInput(
            ElementBounds.Fixed(x + 25, y, InputWidth, 34),
            value => OnAxisChanged(offHand, axis, value),
            CairoFont.WhiteDetailText(),
            key
        );
    }

    private void AddMoveButton(
        GuiComposer composer,
        string label,
        bool offHand,
        int xDirection,
        int yDirection,
        int zDirection,
        double x,
        double y,
        double width,
        string key
    )
    {
        composer.AddSmallButton(
            label,
            () => MoveHand(offHand, xDirection, yDirection, zDirection),
            ElementBounds.Fixed(x, y, width, 34),
            EnumButtonStyle.Normal,
            key
        );
    }

    private void OnStepChanged(string value)
    {
        if (refreshing || !TryParse(value, out float parsed))
        {
            return;
        }

        system.SetPositionStep(parsed);
    }

    private void OnAxisChanged(bool offHand, string axis, string value)
    {
        if (refreshing || !TryParse(value, out float parsed))
        {
            return;
        }

        AnimalicaFirstPersonConfig config = system.Config;
        float x = offHand ? config.OffHandOffsetX : config.MainHandOffsetX;
        float y = offHand ? config.OffHandOffsetY : config.MainHandOffsetY;
        float z = offHand ? config.OffHandOffsetZ : config.MainHandOffsetZ;

        switch (axis)
        {
            case "X": x = parsed; break;
            case "Y": y = parsed; break;
            case "Z": z = parsed; break;
        }

        system.SetHandOffsets(offHand, x, y, z);
        RefreshStatusText();
    }

    private bool MoveHand(bool offHand, int xDirection, int yDirection, int zDirection)
    {
        float step = system.Config.PositionStep;
        system.AdjustHand(
            offHand,
            step * xDirection,
            step * yDirection,
            step * zDirection
        );
        RefreshValues();
        return true;
    }

    private bool ResetHand(bool offHand)
    {
        system.ResetHand(offHand);
        RefreshValues();
        return true;
    }

    private void RefreshValues()
    {
        if (SingleComposer == null)
        {
            return;
        }

        AnimalicaFirstPersonConfig config = system.Config;
        refreshing = true;
        SingleComposer.GetNumberInput("position-step")?.SetValue(Format(config.PositionStep));
        SingleComposer.GetNumberInput("main-x")?.SetValue(Format(config.MainHandOffsetX));
        SingleComposer.GetNumberInput("main-y")?.SetValue(Format(config.MainHandOffsetY));
        SingleComposer.GetNumberInput("main-z")?.SetValue(Format(config.MainHandOffsetZ));
        SingleComposer.GetNumberInput("off-x")?.SetValue(Format(config.OffHandOffsetX));
        SingleComposer.GetNumberInput("off-y")?.SetValue(Format(config.OffHandOffsetY));
        SingleComposer.GetNumberInput("off-z")?.SetValue(Format(config.OffHandOffsetZ));
        refreshing = false;

        RefreshStatusText();
    }

    private void RefreshStatusText()
    {
        AnimalicaFirstPersonConfig config = system.Config;
        SingleComposer?.GetDynamicText("feature-state")?.SetNewText(
            "Seraph animations: " + (config.Enabled ? "Enabled" : "Disabled")
        );
        SingleComposer?.GetDynamicText("current-values")?.SetNewText(
            "Main: X " + Format(config.MainHandOffsetX)
                + "  Y " + Format(config.MainHandOffsetY)
                + "  Z " + Format(config.MainHandOffsetZ)
                + "\nOffhand: X " + Format(config.OffHandOffsetX)
                + "  Y " + Format(config.OffHandOffsetY)
                + "  Z " + Format(config.OffHandOffsetZ)
        );
    }

    private static bool TryParse(string value, out float parsed)
    {
        return float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out parsed
            )
            || float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out parsed
            );
    }

    private static string Format(float value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
