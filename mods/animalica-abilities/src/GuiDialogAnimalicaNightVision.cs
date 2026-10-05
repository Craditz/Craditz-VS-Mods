using System.Globalization;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace ScentTrails;

public sealed class GuiDialogAnimalicaNightVision : GuiDialog
{
    private const double DialogWidth = 600;
    private const double RowHeight = 42;
    private const double ButtonGap = 5;
    private const double Padding = 12;

    private readonly AnimalicaNightVisionSystem nightVision;

    public GuiDialogAnimalicaNightVision(ICoreClientAPI capi, AnimalicaNightVisionSystem nightVision)
        : base(capi)
    {
        this.nightVision = nightVision;
    }

    public override string ToggleKeyCombinationCode => AnimalicaNightVisionSystem.TuningHotkeyCode;

    public override void OnGuiOpened()
    {
        ComposeDialog();
        base.OnGuiOpened();
    }

    private void ComposeDialog()
    {
        const int leverCount = 7;
        double titleOffset = GuiStyle.TitleBarHeight + Padding;
        double bodyHeight = titleOffset + 68 + leverCount * RowHeight + 44 + 44 + Padding;

        ElementBounds contentBounds = ElementBounds
            .Fixed(0, 0, DialogWidth, bodyHeight)
            .WithFixedPadding(Padding);
        contentBounds.BothSizing = ElementSizing.Fixed;

        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle);

        GuiComposer composer = capi.Gui
            .CreateCompo("animalicaabilities-nightvision-tuning", dialogBounds)
            .AddShadedDialogBG(contentBounds)
            .AddDialogTitleBar(Lang.Get("animalicaabilities:nightvision-title"), () => TryClose())
            .BeginChildElements(contentBounds);

        composer.AddStaticText(
            Lang.Get("animalicaabilities:nightvision-description"),
            CairoFont.WhiteSmallText(),
            ElementBounds.Fixed(0, titleOffset, DialogWidth - Padding * 2, 58)
        );

        double firstRow = titleOffset + 66;
        double labelWidth = 215;
        double valueWidth = 100;
        double buttonWidth = 90;
        double buttonX = labelWidth + valueWidth + ButtonGap;

        AddLever(composer, "brightness", "animalicaabilities:nightvision-brightness", firstRow, labelWidth, valueWidth, buttonX, buttonWidth, "BrightnessLift");
        AddLever(composer, "gray", "animalicaabilities:nightvision-gray", firstRow + RowHeight, labelWidth, valueWidth, buttonX, buttonWidth, "GrayscaleStrength");
        AddLever(composer, "start", "animalicaabilities:nightvision-darkness-start", firstRow + RowHeight * 2, labelWidth, valueWidth, buttonX, buttonWidth, "DarknessStart");
        AddLever(composer, "range", "animalicaabilities:nightvision-darkness-range", firstRow + RowHeight * 3, labelWidth, valueWidth, buttonX, buttonWidth, "DarknessRange");
        AddLever(composer, "curve", "animalicaabilities:nightvision-darkness-curve", firstRow + RowHeight * 4, labelWidth, valueWidth, buttonX, buttonWidth, "DarknessCurve");
        AddLever(composer, "highlights", "animalicaabilities:nightvision-highlight-protection", firstRow + RowHeight * 5, labelWidth, valueWidth, buttonX, buttonWidth, "HighlightProtection");
        AddLever(composer, "environment", "animalicaabilities:nightvision-environment-response", firstRow + RowHeight * 6, labelWidth, valueWidth, buttonX, buttonWidth, "EnvironmentalLightResponse");

        double stateY = firstRow + RowHeight * leverCount + 6;
        composer.AddDynamicText(
            string.Empty,
            CairoFont.WhiteDetailText(),
            ElementBounds.Fixed(0, stateY, DialogWidth - Padding * 2 - buttonWidth - ButtonGap, 34),
            "state"
        );
        composer.AddSmallButton(
            Lang.Get("animalicaabilities:nightvision-toggle"),
            ToggleNightVision,
            ElementBounds.Fixed(DialogWidth - Padding * 2 - buttonWidth, stateY, buttonWidth, 34),
            EnumButtonStyle.Normal,
            "toggle"
        );

        composer.AddSmallButton(
            Lang.Get("animalicaabilities:nightvision-reset"),
            ResetTuning,
            ElementBounds.Fixed(0, stateY + 44, DialogWidth - Padding * 2, 34),
            EnumButtonStyle.Normal,
            "reset"
        );

        SingleComposer = composer.EndChildElements().Compose();
        RefreshValues();
    }

    private void AddLever(
        GuiComposer composer,
        string key,
        string labelKey,
        double y,
        double labelWidth,
        double valueWidth,
        double buttonX,
        double buttonWidth,
        string settingName
    )
    {
        composer.AddStaticText(
            Lang.Get(labelKey),
            CairoFont.WhiteDetailText(),
            ElementBounds.Fixed(0, y, labelWidth, 34)
        );
        composer.AddDynamicText(
            string.Empty,
            CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center),
            ElementBounds.Fixed(labelWidth, y, valueWidth, 34),
            key + "-value"
        );
        composer.AddSmallButton(
            "-",
            () => Adjust(settingName, -1),
            ElementBounds.Fixed(buttonX, y, buttonWidth, 34),
            EnumButtonStyle.Normal,
            key + "-down"
        );
        composer.AddSmallButton(
            "+",
            () => Adjust(settingName, 1),
            ElementBounds.Fixed(buttonX + buttonWidth + ButtonGap, y, buttonWidth, 34),
            EnumButtonStyle.Normal,
            key + "-up"
        );
    }

    private bool Adjust(string settingName, int direction)
    {
        float amount = settingName switch
        {
            "BrightnessLift" => 0.1f,
            "GrayscaleStrength" => 0.05f,
            "DarknessStart" => 0.05f,
            "DarknessRange" => 0.05f,
            "DarknessCurve" => 0.25f,
            "HighlightProtection" => 0.05f,
            "EnvironmentalLightResponse" => 0.1f,
            _ => 0f
        } * direction;

        switch (settingName)
        {
            case "BrightnessLift": nightVision.Tuning.AdjustBrightness(amount); break;
            case "GrayscaleStrength": nightVision.Tuning.AdjustGrayscale(amount); break;
            case "DarknessStart": nightVision.Tuning.AdjustDarknessStart(amount); break;
            case "DarknessRange": nightVision.Tuning.AdjustDarknessRange(amount); break;
            case "DarknessCurve": nightVision.Tuning.AdjustDarknessCurve(amount); break;
            case "HighlightProtection": nightVision.Tuning.AdjustHighlightProtection(amount); break;
            case "EnvironmentalLightResponse": nightVision.Tuning.AdjustEnvironmentalLightResponse(amount); break;
        }

        RefreshValues();
        return true;
    }

    private bool ToggleNightVision()
    {
        nightVision.ToggleNightVision();
        RefreshValues();
        return true;
    }

    private bool ResetTuning()
    {
        nightVision.Tuning.Reset();
        RefreshValues();
        return true;
    }

    private void RefreshValues()
    {
        if (SingleComposer == null)
        {
            return;
        }

        SingleComposer.GetDynamicText("brightness-value")?.SetNewText(Format(nightVision.Tuning.BrightnessLift));
        SingleComposer.GetDynamicText("gray-value")?.SetNewText(Format(nightVision.Tuning.GrayscaleStrength));
        SingleComposer.GetDynamicText("start-value")?.SetNewText(Format(nightVision.Tuning.DarknessStart));
        SingleComposer.GetDynamicText("range-value")?.SetNewText(Format(nightVision.Tuning.DarknessRange));
        SingleComposer.GetDynamicText("curve-value")?.SetNewText(Format(nightVision.Tuning.DarknessCurve));
        SingleComposer.GetDynamicText("highlights-value")?.SetNewText(Format(nightVision.Tuning.HighlightProtection));
        SingleComposer.GetDynamicText("environment-value")?.SetNewText(Format(nightVision.Tuning.EnvironmentalLightResponse));
        SingleComposer.GetDynamicText("state")?.SetNewText(
            Lang.Get("animalicaabilities:nightvision-state", nightVision.Tuning.Enabled ? Lang.Get("animalicaabilities:nightvision-on") : Lang.Get("animalicaabilities:nightvision-off"))
        );
    }

    private static string Format(float value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
