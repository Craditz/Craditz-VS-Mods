using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace ScentTrails;

public sealed class GuiDialogScentTrailsAbilities : GuiDialog
{
    private const double DialogWidth = 440;
    private const double Padding = 12;
    private const double ButtonHeight = 36;
    private const double ButtonSpacing = 8;
    private const double ProfileButtonGap = 6;
    private const double DescriptionHeight = 64;
    private const double SpeciesOffset = 68;
    private const double ControlsOffset = 120;

    private readonly ScentTrailsSystem scentTrails;
    private readonly AnimalicaNightVisionSystem nightVision;

    public GuiDialogScentTrailsAbilities(
        ICoreClientAPI capi,
        ScentTrailsSystem scentTrails,
        AnimalicaNightVisionSystem nightVision
    ) : base(capi)
    {
        this.scentTrails = scentTrails;
        this.nightVision = nightVision;
    }

    public override string ToggleKeyCombinationCode => ScentTrailsSystem.AbilitiesHotkeyCode;

    public override void OnGuiOpened()
    {
        ComposeDialog();
        base.OnGuiOpened();
    }

    private void ComposeDialog()
    {
        AnimalicaAbilityProfile profile = AnimalicaAbilityResolver.Resolve(capi.World.Player?.Entity);
        bool hasNightSight = profile.HasNightSight;
        bool hasScent = profile.HasScent;

        double titleOffset = GuiStyle.TitleBarHeight + Padding;
        double contentWidth = DialogWidth - Padding * 2;
        double y = titleOffset + ControlsOffset;

        if (!hasNightSight && !hasScent)
        {
            y += 42;
        }
        else
        {
            if (hasNightSight)
            {
                y += 20 + 24 + ButtonHeight + 18;
            }

            if (hasScent)
            {
                y += 20 + 24 + ButtonHeight + 18;
            }

            if (hasNightSight)
            {
                y += (ButtonHeight + ButtonSpacing) * 2;
            }

            if (hasScent)
            {
                y += ButtonHeight + ButtonSpacing;
            }
        }

        double bodyHeight = y + Padding;
        ElementBounds contentBounds = ElementBounds
            .Fixed(0, 0, DialogWidth, bodyHeight)
            .WithFixedPadding(Padding);
        contentBounds.BothSizing = ElementSizing.Fixed;

        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle);

        GuiComposer composer = capi.Gui
            .CreateCompo("animalicaabilities-abilities", dialogBounds)
            .AddShadedDialogBG(contentBounds)
            .AddDialogTitleBar(Lang.Get("animalicaabilities:abilities-title"), () => TryClose())
            .BeginChildElements(contentBounds);

        composer.AddStaticText(
            Lang.Get("animalicaabilities:abilities-description"),
            CairoFont.WhiteSmallText(),
            ElementBounds.Fixed(0, titleOffset, contentWidth, DescriptionHeight)
        );
        composer.AddStaticText(
            Lang.Get("animalicaabilities:abilities-species", Lang.Get(profile.SpeciesLocalizationKey)),
            CairoFont.WhiteDetailText(),
            ElementBounds.Fixed(0, titleOffset + SpeciesOffset, contentWidth, 22)
        );

        y = titleOffset + ControlsOffset;

        if (!hasNightSight && !hasScent)
        {
            composer.AddStaticText(
                Lang.Get("animalicaabilities:abilities-none"),
                CairoFont.WhiteDetailText(),
                ElementBounds.Fixed(0, y, contentWidth, 34)
            );
            EndCompose(composer);
            return;
        }

        double profileButtonWidth = (contentWidth - ProfileButtonGap * 2) / 3;
        if (hasNightSight)
        {
            composer.AddStaticText(
                Lang.Get("animalicaabilities:abilities-nightvision-title"),
                CairoFont.WhiteDetailText(),
                ElementBounds.Fixed(0, y, contentWidth, 20)
            );
            y += 24;
            AddProfileButtons(composer, y, profileButtonWidth, true);
            y += ButtonHeight + 18;
        }

        if (hasScent)
        {
            composer.AddStaticText(
                Lang.Get("animalicaabilities:abilities-scent-title"),
                CairoFont.WhiteDetailText(),
                ElementBounds.Fixed(0, y, contentWidth, 20)
            );
            y += 24;
            AddProfileButtons(composer, y, profileButtonWidth, false);
            y += ButtonHeight + 18;
        }

        if (hasNightSight)
        {
            composer.AddSmallButton(
                Lang.Get("animalicaabilities:abilities-toggle-nightvision"),
                ToggleNightVision,
                ElementBounds.Fixed(0, y, contentWidth, ButtonHeight),
                EnumButtonStyle.Normal,
                "toggle-nightvision"
            );
            y += ButtonHeight + ButtonSpacing;
            composer.AddSmallButton(
                Lang.Get("animalicaabilities:abilities-configure-nightvision"),
                ConfigureNightVision,
                ElementBounds.Fixed(0, y, contentWidth, ButtonHeight),
                EnumButtonStyle.Normal,
                "configure-nightvision"
            );
            y += ButtonHeight + ButtonSpacing;
        }

        if (hasScent)
        {
            composer.AddSmallButton(
                Lang.Get("animalicaabilities:abilities-toggle-scent"),
                ToggleScent,
                ElementBounds.Fixed(0, y, contentWidth, ButtonHeight),
                EnumButtonStyle.Normal,
                "toggle-scent"
            );
        }

        EndCompose(composer);
    }

    private void AddProfileButtons(GuiComposer composer, double y, double buttonWidth, bool nightSight)
    {
        string prefix = nightSight ? "night" : "scent";
        ElementBounds bounds = ElementBounds.Fixed(0, y, buttonWidth, ButtonHeight);
        composer.AddSmallButton(
            Lang.Get("animalicaabilities:abilities-strength-weak"),
            nightSight ? ApplyWeakProfile : ApplyWeakScentProfile,
            bounds,
            EnumButtonStyle.Normal,
            prefix + "-strength-weak"
        );
        composer.AddSmallButton(
            Lang.Get("animalicaabilities:abilities-strength-normal"),
            nightSight ? ApplyNormalProfile : ApplyNormalScentProfile,
            ElementBounds.Fixed(buttonWidth + ProfileButtonGap, y, buttonWidth, ButtonHeight),
            EnumButtonStyle.Normal,
            prefix + "-strength-normal"
        );
        composer.AddSmallButton(
            Lang.Get("animalicaabilities:abilities-strength-strong"),
            nightSight ? ApplyStrongProfile : ApplyStrongScentProfile,
            ElementBounds.Fixed((buttonWidth + ProfileButtonGap) * 2, y, buttonWidth, ButtonHeight),
            EnumButtonStyle.Normal,
            prefix + "-strength-strong"
        );
    }

    private void EndCompose(GuiComposer composer)
    {
        SingleComposer = composer.EndChildElements().Compose();
    }

    private bool ApplyWeakProfile()
    {
        ApplyProfile(NightVisionStrengthProfile.Weak, "animalicaabilities:abilities-strength-weak");
        return true;
    }

    private bool ApplyNormalProfile()
    {
        ApplyProfile(NightVisionStrengthProfile.Normal, "animalicaabilities:abilities-strength-normal");
        return true;
    }

    private bool ApplyStrongProfile()
    {
        ApplyProfile(NightVisionStrengthProfile.Strong, "animalicaabilities:abilities-strength-strong");
        return true;
    }

    private bool ApplyWeakScentProfile()
    {
        return scentTrails.ApplyScentProfile(ScentStrengthProfile.Weak);
    }

    private bool ApplyNormalScentProfile()
    {
        return scentTrails.ApplyScentProfile(ScentStrengthProfile.Normal);
    }

    private bool ApplyStrongScentProfile()
    {
        return scentTrails.ApplyScentProfile(ScentStrengthProfile.Strong);
    }

    private void ApplyProfile(NightVisionStrengthProfile profile, string labelKey)
    {
        if (!nightVision.HasNightSightAbility)
        {
            return;
        }

        nightVision.Tuning.ApplyProfile(profile);
        capi.ShowChatMessage(Lang.Get("animalicaabilities:abilities-strength-applied", Lang.Get(labelKey)));
    }

    private bool ToggleNightVision()
    {
        nightVision.ToggleNightVision();
        return true;
    }

    private bool ToggleScent()
    {
        return scentTrails.ToggleScentView();
    }

    private bool ConfigureNightVision()
    {
        TryClose();
        nightVision.OpenTuningDialog();
        return true;
    }
}
