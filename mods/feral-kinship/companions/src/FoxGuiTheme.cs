#nullable enable

using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;

namespace FeralKinshipCompanions;

internal enum FoxGuiSurfaceKind
{
    General,
    Companion,
    Pack,
    Talents,
    Den,
    Whistle
}

internal enum FoxGuiVariant
{
    ClassicBaseline,
    FieldJournal,
    TactilePack,
    VanillaAdjacent,
    WarmJournal,
    Editorial,
    WildcardPremium,
    MoonlitDen,
    SurveyorsBlueprint,
    BoneAndBerry,
    FoxfireAlmanac
}

/// <summary>
/// Procedural drawing language shared by every ordinary companion screen.
/// The authored variants remain available as compile-time review defaults, but
/// players can choose the four winning styles from the hand-drawn UI.
/// </summary>
internal static class FoxGuiTheme
{
    private static readonly FoxGuiVariant[] SelectableVariants =
    {
        FoxGuiVariant.WarmJournal,
        FoxGuiVariant.WildcardPremium,
        FoxGuiVariant.MoonlitDen,
        FoxGuiVariant.FoxfireAlmanac
    };

    private static FoxGuiVariant variant = ResolveVariant();
    private static ThemePalette palette = CreatePalette(variant);
    private static int themeRevision;
    private static ThemePalette Palette => palette;

    public static FoxGuiVariant Variant => variant;
    public static string VariantId => GetVariantId(Variant);
    public static int ThemeRevision => themeRevision;
    public static IReadOnlyList<FoxGuiVariant> PlayerSelectableVariants => SelectableVariants;

    private static string GetVariantId(FoxGuiVariant value) => value switch
    {
        FoxGuiVariant.WarmJournal => "warm-journal",
        FoxGuiVariant.WildcardPremium => "wildcard-premium",
        FoxGuiVariant.MoonlitDen => "moonlit-den",
        FoxGuiVariant.FoxfireAlmanac => "foxfire-almanac",
        _ => "classic-baseline"
    };

    public static string GetVariantName(FoxGuiVariant value) => value switch
    {
        FoxGuiVariant.WarmJournal => "Warm Journal",
        FoxGuiVariant.WildcardPremium => "Wildcard Premium",
        FoxGuiVariant.MoonlitDen => "Moonlit Den",
        FoxGuiVariant.FoxfireAlmanac => "Foxfire Almanac",
        _ => "Classic Baseline"
    };

    public static bool TryParsePlayerVariant(string value, out FoxGuiVariant result)
    {
        result = FoxGuiVariant.WarmJournal;
        if (string.IsNullOrWhiteSpace(value)) return false;

        foreach (FoxGuiVariant candidate in SelectableVariants)
        {
            if (string.Equals(value, GetVariantId(candidate), StringComparison.OrdinalIgnoreCase))
            {
                result = candidate;
                return true;
            }
        }
        return false;
    }

    public static void SetPlayerVariant(FoxGuiVariant value)
    {
        if (Array.IndexOf(SelectableVariants, value) < 0 || value == variant) return;
        variant = value;
        palette = CreatePalette(value);
        themeRevision++;
    }

    public static void CyclePlayerVariant()
    {
        int current = Array.IndexOf(SelectableVariants, variant);
        SetPlayerVariant(SelectableVariants[(current + 1 + SelectableVariants.Length) % SelectableVariants.Length]);
    }

    public static double[] Text => palette.Text;
    public static double[] Muted => palette.Muted;
    public static double[] Accent => palette.Accent;
    public static double[] SecondaryAccent => palette.Secondary;
    public static double[] TertiaryAccent => palette.Tertiary;
    public static double[] DarkText => palette.DarkText;
    public static double[] Danger => palette.Danger;
    public static double[] Warning => palette.Warning;
    public static double[] Success => palette.Success;
    public static double[] PanelColor => palette.Panel;
    public static double[] SelectedPanelColor => palette.Selected;
    public static double[] DangerPanelColor => palette.DangerPanel;
    public static double[] MutedPanelColor => palette.MutedPanel;
    public static double[] CheckboxColor => palette.Checkbox;
    public static double[] ButtonColor => palette.Button;
    public static double[] ButtonDisabledColor => palette.ButtonDisabled;
    public static double[] LockedText => palette.Locked;
    public static double[] LockedPanel => palette.LockedPanel;
    public static double[] LockedBorder => palette.LockedBorder;
    public static double[] ScrollTrack => palette.ScrollTrack;
    public static double[] ScrollThumb => palette.ScrollThumb;
    public static double[] OverviewAccent => palette.Overview;
    public static double[] CommandsAccent => palette.Commands;
    public static double[] SocialAccent => palette.Social;
    public static double[] TalentsAccent => palette.Talents;
    public static double[] CombatAccent => palette.Combat;
    public static double[] SurvivalAccent => palette.Survival;
    public static double[] ReturnAccent => palette.Return;
    public static double[] TooltipShadow => palette.TooltipShadow;
    public static double[] TooltipBackground => palette.Tooltip;
    public static double[] ImplementedPanel => palette.Implemented;
    public static double[] FuturePanel => palette.Future;

    public static void DrawJournal(Context ctx, double width, double height) =>
        DrawJournal(ctx, width, height, FoxGuiSurfaceKind.General);

    public static void DrawJournal(Context ctx, double width, double height, FoxGuiSurfaceKind kind)
    {
        switch (Variant)
        {
            case FoxGuiVariant.FieldJournal: DrawFieldJournal(ctx, width, height); break;
            case FoxGuiVariant.TactilePack: DrawTactilePack(ctx, width, height); break;
            case FoxGuiVariant.VanillaAdjacent: DrawVanillaAdjacent(ctx, width, height); break;
            case FoxGuiVariant.WarmJournal: DrawWarmJournal(ctx, width, height); break;
            case FoxGuiVariant.Editorial: DrawEditorial(ctx, width, height); break;
            case FoxGuiVariant.WildcardPremium: DrawPremium(ctx, width, height); break;
            case FoxGuiVariant.MoonlitDen: DrawMoonlit(ctx, width, height); break;
            case FoxGuiVariant.SurveyorsBlueprint: DrawBlueprint(ctx, width, height); break;
            case FoxGuiVariant.BoneAndBerry: DrawBoneAndBerry(ctx, width, height); break;
            case FoxGuiVariant.FoxfireAlmanac: DrawFoxfire(ctx, width, height); break;
            default: DrawClassic(ctx, width, height); break;
        }
        DrawSurfaceSigil(ctx, width, height, kind);
    }

    public static void DrawOverlay(Context ctx, double width, double height, double[] color)
    {
        Fill(ctx, 0, 0, width, height, Palette.Overlay);
        if (Variant is FoxGuiVariant.FieldJournal or FoxGuiVariant.WarmJournal or FoxGuiVariant.BoneAndBerry)
        {
            StrokeRect(ctx, 14.5, 14.5, width - 29, height - 29, Palette.Border, 1);
        }
        else if (Variant == FoxGuiVariant.Editorial)
        {
            Fill(ctx, 0, 0, 7, height, Palette.Accent);
        }
    }

    public static double HelpButtonX(double width) => width - 90;

    public static double FontMinusButtonX(double width) => width - 220;

    public static double FontPlusButtonX(double width) => width - 160;

    public static double ThemeButtonX(double width) => width - 280;

    public static bool IsFontMinusHovered(double x, double y, double width)
    {
        return x >= FontMinusButtonX(width) && x <= FontMinusButtonX(width) + 54
            && y >= 5 && y <= 34;
    }

    public static bool IsFontPlusHovered(double x, double y, double width)
    {
        return x >= FontPlusButtonX(width) && x <= FontPlusButtonX(width) + 54
            && y >= 5 && y <= 34;
    }

    public static bool IsThemeHovered(double x, double y, double width)
    {
        return x >= ThemeButtonX(width) && x <= ThemeButtonX(width) + 54
            && y >= 5 && y <= 34;
    }

    public static void DrawWindowControls(Context ctx, ICoreClientAPI api, double width)
    {
        FeralKinshipCompanionUiSettings.EnsureLoaded(api);
        DrawButtonSurface(ctx, ThemeButtonX(width), 5, 54, 29, true);
        DrawButtonSurface(ctx, FontMinusButtonX(width), 5, 54, 29, true);
        DrawButtonSurface(ctx, FontPlusButtonX(width), 5, 54, 29, true);
        DrawFixedControlText(ctx, "Style", ThemeButtonX(width) + 8, 24, 12, palette.Text);
        DrawFixedControlText(ctx, "Font-", FontMinusButtonX(width) + 8, 24, 12, Palette.Text);
        DrawFixedControlText(ctx, "Font+", FontPlusButtonX(width) + 8, 24, 12, Palette.Text);

        double crossSize = 15;
        double menuSize = 17;
        double iconY = 7;
        double crossX = width - crossSize - 12;
        double menuX = width - crossSize - menuSize - 20;

        ctx.Operator = Operator.Over;
        ctx.SetSourceRGBA(0, 0, 0, 0.38);
        api.Gui.Icons.DrawCross(ctx, crossX + 2, iconY + 2, 2, crossSize);
        api.Gui.Icons.Drawmenuicon_svg(ctx, (int)menuX + 2, (int)iconY + 2, (int)menuSize, (int)menuSize, new[] { 0d, 0d, 0d, 0.38 });
        ctx.Operator = Operator.Source;
        SetSource(ctx, Palette.Text);
        api.Gui.Icons.DrawCross(ctx, crossX, iconY, 2, crossSize);
        api.Gui.Icons.Drawmenuicon_svg(ctx, (int)menuX, (int)iconY + 1, (int)menuSize, (int)menuSize, Palette.Text);
        ctx.Operator = Operator.Over;
    }

    private static void DrawFixedControlText(Context ctx, string text, double x, double baseline, double size, double[] color)
    {
        CairoFont font = CairoFont.WhiteSmallText().WithFontSize((float)size);
        font.Color = color;
        font.SetupContext(ctx);
        ctx.MoveTo(x, baseline);
        ctx.ShowText(text);
        font.Dispose();
    }

    public static bool IsHelpHovered(double x, double y, double width)
    {
        double buttonX = HelpButtonX(width);
        return x >= buttonX && x <= buttonX + 34 && y >= 5 && y <= 38;
    }

    public static void DrawHelpGlyph(Context ctx, double width, bool hovered)
    {
        double centerX = HelpButtonX(width) + 17;
        double centerY = 17;
        if (hovered)
        {
            DrawDisc(ctx, centerX, centerY, 13, WithAlpha(Palette.Accent, 0.28));
            StrokeCircle(ctx, centerX, centerY, 13, WithAlpha(Palette.Accent, 0.85), 1);
        }
        CairoFont font = CairoFont.WhiteSmallText().WithFontSize(20);
        font.Color = new[] { 0d, 0d, 0d, 0.55 };
        font.SetupContext(ctx);
        ctx.MoveTo(centerX - 3, centerY + 6);
        ctx.ShowText("?");
        font.Color = hovered ? Palette.Accent : Palette.Text;
        font.SetupContext(ctx);
        ctx.MoveTo(centerX - 4, centerY + 5);
        ctx.ShowText("?");
        font.Dispose();
    }

    public static void DrawPanel(Context ctx, double x, double y, double width, double height, bool accent = false)
    {
        double[] fill = accent ? Palette.Selected : Palette.Panel;
        switch (Variant)
        {
            case FoxGuiVariant.TactilePack:
                Fill(ctx, x, y, width, height, fill);
                StrokeRect(ctx, x + 0.5, y + 0.5, width - 1, height - 1, accent ? Palette.Accent : Palette.Border, 1.3);
                Line(ctx, x + 6, y + 5, x + width - 6, y + 5, WithAlpha(Palette.Text, 0.12), 1);
                DrawRivet(ctx, x + 7, y + 7, 2.2);
                DrawRivet(ctx, x + width - 7, y + 7, 2.2);
                break;
            case FoxGuiVariant.VanillaAdjacent:
                Fill(ctx, x, y, width, height, fill);
                Line(ctx, x, y, x + width, y, WithAlpha(Palette.Text, 0.30), 1);
                Line(ctx, x, y, x, y + height, WithAlpha(Palette.Text, 0.20), 1);
                Line(ctx, x, y + height, x + width, y + height, WithAlpha(Palette.DarkText, 0.72), 2);
                Line(ctx, x + width, y, x + width, y + height, WithAlpha(Palette.DarkText, 0.62), 2);
                break;
            case FoxGuiVariant.WarmJournal:
            case FoxGuiVariant.BoneAndBerry:
                RoundedRect(ctx, x, y, width, height, 7);
                SetSource(ctx, fill);
                ctx.FillPreserve();
                SetSource(ctx, accent ? Palette.Accent : Palette.Border);
                ctx.LineWidth = 1;
                ctx.Stroke();
                Line(ctx, x + 12, y + height - 4, x + width - 12, y + height - 4, WithAlpha(Palette.Accent, accent ? 0.48 : 0.16), 1);
                break;
            case FoxGuiVariant.Editorial:
                Fill(ctx, x, y, width, height, fill);
                Fill(ctx, x, y, accent ? 5 : 2, height, accent ? Palette.Accent : Palette.Border);
                Line(ctx, x, y + height, x + width, y + height, WithAlpha(Palette.Border, 0.75), 1);
                break;
            case FoxGuiVariant.WildcardPremium:
            case FoxGuiVariant.MoonlitDen:
            case FoxGuiVariant.FoxfireAlmanac:
                Fill(ctx, x, y, width, height, fill);
                StrokeRect(ctx, x + 0.5, y + 0.5, width - 1, height - 1, accent ? Palette.Accent : Palette.Border, accent ? 1.5 : 1);
                Line(ctx, x + 10, y + 3, x + width - 10, y + 3, WithAlpha(accent ? Palette.Accent : Palette.Secondary, accent ? 0.70 : 0.22), 1);
                if (accent) DrawDiamond(ctx, x + width - 13, y + 12, 4, Palette.Accent);
                break;
            case FoxGuiVariant.SurveyorsBlueprint:
                Fill(ctx, x, y, width, height, fill);
                StrokeRect(ctx, x + 0.5, y + 0.5, width - 1, height - 1, accent ? Palette.Accent : Palette.Border, 1);
                Line(ctx, x + 6, y + 6, x + 18, y + 6, Palette.Border, 1);
                Line(ctx, x + 6, y + 6, x + 6, y + 18, Palette.Border, 1);
                Line(ctx, x + width - 6, y + height - 6, x + width - 18, y + height - 6, Palette.Border, 1);
                Line(ctx, x + width - 6, y + height - 6, x + width - 6, y + height - 18, Palette.Border, 1);
                break;
            default:
                Fill(ctx, x, y, width, height, fill);
                StrokeRect(ctx, x + 0.5, y + 0.5, width - 1, height - 1, accent ? Palette.Accent : Palette.Border, 1);
                if (Variant == FoxGuiVariant.FieldJournal)
                {
                    StrokeRect(ctx, x + 3.5, y + 3.5, width - 7, height - 7, WithAlpha(Palette.Border, 0.34), 1);
                }
                break;
        }
    }

    public static void DrawButtonSurface(Context ctx, double x, double y, double width, double height, bool enabled)
    {
        DrawPanel(ctx, x, y, width, height, enabled);
        if (!enabled)
        {
            Fill(ctx, x + 1, y + 1, width - 2, height - 2, Palette.ButtonDisabled);
            return;
        }
        if (Variant == FoxGuiVariant.Editorial) Fill(ctx, x, y + height - 3, width, 3, Palette.Accent);
        else if (Variant is FoxGuiVariant.TactilePack or FoxGuiVariant.FieldJournal)
        {
            Line(ctx, x + 7, y + height - 4, x + width - 7, y + height - 4, WithAlpha(Palette.Accent, 0.46), 1);
        }
    }

    public static void DrawChoiceSurface(Context ctx, double x, double y, double width, double height, bool selected)
    {
        DrawPanel(ctx, x, y, width, height, selected);
        if (!selected) return;
        if (Variant is FoxGuiVariant.WildcardPremium or FoxGuiVariant.MoonlitDen or FoxGuiVariant.FoxfireAlmanac)
        {
            DrawDiamond(ctx, x + 10, y + height / 2, 4, Palette.Accent);
        }
        else Fill(ctx, x, y, Variant == FoxGuiVariant.Editorial ? 6 : 4, height, Palette.Accent);
    }

    public static void DrawTabSurface(Context ctx, double x, double y, double width, double height, bool selected, bool hovered, double[]? accent = null)
    {
        double[] mark = accent ?? Palette.Accent;
        bool active = selected || hovered;
        if (Variant == FoxGuiVariant.Editorial)
        {
            Fill(ctx, x, y, width, height, active ? Palette.Selected : Palette.Panel);
            Fill(ctx, x, y + height - (selected ? 5 : 2), width, selected ? 5 : 2, active ? mark : Palette.Border);
            return;
        }
        if (Variant is FoxGuiVariant.WildcardPremium or FoxGuiVariant.SurveyorsBlueprint)
        {
            ctx.NewPath();
            ctx.MoveTo(x + 8, y);
            ctx.LineTo(x + width - 8, y);
            ctx.LineTo(x + width, y + height);
            ctx.LineTo(x, y + height);
            ctx.ClosePath();
            SetSource(ctx, active ? Palette.Selected : Palette.Panel);
            ctx.FillPreserve();
            SetSource(ctx, active ? mark : Palette.Border);
            ctx.LineWidth = active ? 1.5 : 1;
            ctx.Stroke();
            if (selected) Fill(ctx, x + 10, y + height - 3, width - 20, 3, mark);
            return;
        }
        DrawPanel(ctx, x, y, width, height, active);
        if (selected) Fill(ctx, x + 8, y + height - 4, width - 16, 3, mark);
    }

    public static void DrawSectionRule(Context ctx, double x, double y, double width)
    {
        if (Variant == FoxGuiVariant.Editorial)
        {
            Line(ctx, x, y, x + width, y, Palette.Border, 1);
            Fill(ctx, x, y - 2, Math.Min(64, width), 4, Palette.Accent);
            return;
        }
        if (Variant == FoxGuiVariant.SurveyorsBlueprint)
        {
            Line(ctx, x, y, x + width, y, Palette.Border, 1);
            for (double dx = 0; dx <= width; dx += 24) Line(ctx, x + dx, y - 3, x + dx, y + 3, Palette.Border, 1);
            return;
        }
        Line(ctx, x, y, x + Math.Min(34, width), y, Palette.Accent, 2);
        if (width > 39) Line(ctx, x + 39, y, x + width, y, Palette.Border, 1);
        if (Variant is FoxGuiVariant.WildcardPremium or FoxGuiVariant.MoonlitDen or FoxGuiVariant.FoxfireAlmanac)
        {
            DrawDiamond(ctx, x + Math.Min(36.5, width), y, 2.5, Palette.Secondary);
        }
    }

    public static void DrawBadge(Context ctx, double x, double y, double width, double height, bool accent)
    {
        if (Variant is FoxGuiVariant.WarmJournal or FoxGuiVariant.BoneAndBerry)
        {
            RoundedRect(ctx, x, y, width, height, height / 2);
            SetSource(ctx, accent ? Palette.Selected : Palette.Panel);
            ctx.FillPreserve();
            SetSource(ctx, accent ? Palette.Accent : Palette.Border);
            ctx.LineWidth = 1;
            ctx.Stroke();
            return;
        }
        DrawPanel(ctx, x, y, width, height, accent);
    }

    public static void PlayNavigation(ICoreClientAPI api) => api.Gui.PlaySound("menubutton_wood", false, 0.32f);
    public static void PlayChoice(ICoreClientAPI api) => api.Gui.PlaySound("toggleswitch", false, 0.34f);
    public static void PlayAction(ICoreClientAPI api) => api.Gui.PlaySound("menubutton_press", false, 0.42f);
    public static void PlayUnavailable(ICoreClientAPI api) => api.Gui.PlaySound("menubutton_down", false, 0.20f);

    private static FoxGuiVariant ResolveVariant()
    {
#if UI_FIELD_JOURNAL
        return FoxGuiVariant.FieldJournal;
#elif UI_TACTILE_PACK
        return FoxGuiVariant.TactilePack;
#elif UI_VANILLA_ADJACENT
        return FoxGuiVariant.VanillaAdjacent;
#elif UI_WARM_JOURNAL
        return FoxGuiVariant.WarmJournal;
#elif UI_EDITORIAL
        return FoxGuiVariant.Editorial;
#elif UI_WILDCARD_PREMIUM
        return FoxGuiVariant.WildcardPremium;
#elif UI_MOONLIT_DEN
        return FoxGuiVariant.MoonlitDen;
#elif UI_SURVEYORS_BLUEPRINT
        return FoxGuiVariant.SurveyorsBlueprint;
#elif UI_BONE_AND_BERRY
        return FoxGuiVariant.BoneAndBerry;
#elif UI_FOXFIRE_ALMANAC
        return FoxGuiVariant.FoxfireAlmanac;
#else
        return FoxGuiVariant.WarmJournal;
#endif
    }

    private static ThemePalette CreatePalette(FoxGuiVariant variant)
    {
        return variant switch
        {
            FoxGuiVariant.FieldJournal => ThemePalette.Make(C(0.052, 0.043, 0.034, 0.99), C(0.105, 0.085, 0.063, 0.98), C(0.145, 0.113, 0.080, 0.91), C(0.285, 0.185, 0.082, 0.96), C(0.91, 0.57, 0.18, 1), C(0.55, 0.72, 0.56, 1), C(0.78, 0.50, 0.26, 1), C(0.97, 0.93, 0.84, 1), C(0.74, 0.69, 0.60, 1)),
            FoxGuiVariant.TactilePack => ThemePalette.Make(C(0.055, 0.032, 0.019, 1), C(0.125, 0.072, 0.038, 0.99), C(0.225, 0.155, 0.090, 0.94), C(0.38, 0.235, 0.085, 0.98), C(0.92, 0.66, 0.24, 1), C(0.55, 0.72, 0.47, 1), C(0.76, 0.42, 0.20, 1), C(0.97, 0.90, 0.75, 1), C(0.74, 0.65, 0.50, 1)),
            FoxGuiVariant.VanillaAdjacent => ThemePalette.Make(C(0.045, 0.050, 0.050, 1), C(0.105, 0.105, 0.095, 0.99), C(0.17, 0.165, 0.145, 0.94), C(0.29, 0.25, 0.15, 0.98), C(0.88, 0.65, 0.25, 1), C(0.49, 0.66, 0.54, 1), C(0.69, 0.48, 0.25, 1), C(0.94, 0.91, 0.82, 1), C(0.69, 0.67, 0.61, 1)),
            FoxGuiVariant.WarmJournal => ThemePalette.Make(C(0.105, 0.052, 0.035, 1), C(0.19, 0.105, 0.068, 0.99), C(0.255, 0.155, 0.105, 0.93), C(0.39, 0.225, 0.125, 0.98), C(0.98, 0.63, 0.30, 1), C(0.55, 0.78, 0.68, 1), C(0.91, 0.48, 0.38, 1), C(1.00, 0.92, 0.80, 1), C(0.79, 0.67, 0.56, 1)),
            FoxGuiVariant.Editorial => ThemePalette.Make(C(0.025, 0.027, 0.028, 1), C(0.060, 0.063, 0.064, 1), C(0.105, 0.108, 0.108, 0.98), C(0.19, 0.20, 0.19, 1), C(0.96, 0.55, 0.13, 1), C(0.26, 0.75, 0.67, 1), C(0.92, 0.76, 0.26, 1), C(0.98, 0.97, 0.93, 1), C(0.70, 0.72, 0.70, 1)),
            FoxGuiVariant.WildcardPremium => ThemePalette.Make(C(0.018, 0.026, 0.042, 1), C(0.035, 0.055, 0.083, 0.99), C(0.055, 0.088, 0.119, 0.95), C(0.09, 0.18, 0.22, 0.99), C(0.95, 0.58, 0.25, 1), C(0.25, 0.80, 0.76, 1), C(0.49, 0.61, 0.91, 1), C(0.94, 0.96, 0.94, 1), C(0.61, 0.70, 0.72, 1)),
            FoxGuiVariant.MoonlitDen => ThemePalette.Make(C(0.022, 0.021, 0.055, 1), C(0.047, 0.043, 0.098, 0.99), C(0.075, 0.072, 0.135, 0.95), C(0.14, 0.12, 0.23, 0.99), C(0.83, 0.66, 0.98, 1), C(0.40, 0.78, 0.86, 1), C(0.98, 0.68, 0.38, 1), C(0.94, 0.93, 1.00, 1), C(0.65, 0.66, 0.80, 1)),
            FoxGuiVariant.SurveyorsBlueprint => ThemePalette.Make(C(0.018, 0.065, 0.077, 1), C(0.028, 0.105, 0.121, 0.99), C(0.035, 0.145, 0.158, 0.94), C(0.055, 0.225, 0.225, 0.98), C(0.98, 0.73, 0.28, 1), C(0.45, 0.90, 0.84, 1), C(0.74, 0.89, 0.57, 1), C(0.91, 0.98, 0.96, 1), C(0.60, 0.78, 0.77, 1)),
            FoxGuiVariant.BoneAndBerry => ThemePalette.Make(C(0.115, 0.087, 0.070, 1), C(0.21, 0.16, 0.12, 0.99), C(0.285, 0.22, 0.165, 0.96), C(0.39, 0.22, 0.20, 0.99), C(0.91, 0.38, 0.36, 1), C(0.85, 0.70, 0.42, 1), C(0.64, 0.78, 0.48, 1), C(0.98, 0.92, 0.78, 1), C(0.75, 0.68, 0.57, 1)),
            FoxGuiVariant.FoxfireAlmanac => ThemePalette.Make(C(0.012, 0.037, 0.028, 1), C(0.022, 0.073, 0.053, 0.99), C(0.034, 0.115, 0.078, 0.95), C(0.055, 0.19, 0.12, 0.99), C(0.56, 0.98, 0.50, 1), C(0.35, 0.85, 0.77, 1), C(0.94, 0.69, 0.25, 1), C(0.91, 0.98, 0.87, 1), C(0.57, 0.75, 0.63, 1)),
            _ => ThemePalette.Make(C(0.075, 0.052, 0.034, 0.98), C(0.105, 0.075, 0.048, 0.94), C(0.12, 0.088, 0.055, 0.88), C(0.245, 0.16, 0.075, 0.94), C(0.90, 0.46, 0.12, 1), C(0.36, 0.68, 0.60, 1), C(0.90, 0.65, 0.20, 1), C(0.96, 0.92, 0.84, 1), C(0.72, 0.66, 0.56, 1))
        };
    }

    private static void DrawClassic(Context ctx, double width, double height)
    {
        Fill(ctx, 0, 0, width, height, Palette.Background);
        Fill(ctx, 2, 2, width - 4, height - 4, Palette.Inner);
        Line(ctx, 18, 5, width - 18, 5, WithAlpha(Palette.Accent, 0.52), 1);
        DrawPaw(ctx, width - 92, height - 65, 1.15, 0.38);
        DrawPaw(ctx, 42, height - 48, 0.72, 0.22);
        DrawStitch(ctx, 22, height - 18, width - 44);
    }

    private static void DrawFieldJournal(Context ctx, double width, double height)
    {
        Fill(ctx, 0, 0, width, height, Palette.Background);
        Fill(ctx, 4, 4, width - 8, height - 8, Palette.Inner);
        for (double y = 12; y < height - 12; y += 8) Line(ctx, 10, y, width - 10, y, WithAlpha(Palette.Text, 0.018), 1);
        for (double x = 13; x < width - 10; x += 11) Line(ctx, x, 8, x, height - 8, WithAlpha(Palette.DarkText, 0.020), 1);
        Fill(ctx, 9, 7, width - 18, 28, WithAlpha(Palette.Background, 0.52));
        StrokeRect(ctx, 5.5, 5.5, width - 11, height - 11, Palette.Border, 1);
        StrokeRect(ctx, 11.5, 11.5, width - 23, height - 23, WithAlpha(Palette.Border, 0.38), 1);
        DrawCornerFlourish(ctx, 14, 14, 1, 1);
        DrawCornerFlourish(ctx, width - 14, height - 14, -1, -1);
        DrawStitch(ctx, 24, height - 16, width - 48);
    }

    private static void DrawTactilePack(Context ctx, double width, double height)
    {
        Fill(ctx, 0, 0, width, height, Palette.Background);
        for (double y = 0; y < height; y += 46)
        {
            Fill(ctx, 0, y, width, 44, y % 92 == 0 ? Palette.Inner : WithAlpha(Palette.Panel, 0.82));
            Line(ctx, 0, y + 44, width, y + 44, WithAlpha(Palette.DarkText, 0.78), 2);
            Line(ctx, 0, y + 2, width, y + 2, WithAlpha(Palette.Text, 0.08), 1);
        }
        Fill(ctx, 10, 4, 28, height - 8, C(0.11, 0.052, 0.027, 0.96));
        Line(ctx, 38, 6, 38, height - 6, Palette.Accent, 1.5);
        for (double y = 18; y < height - 10; y += 36) DrawRivet(ctx, 24, y, 3.2);
        StrokeRect(ctx, 4.5, 4.5, width - 9, height - 9, Palette.Border, 1.5);
        DrawLeatherCorner(ctx, width - 66, height - 47);
    }

    private static void DrawVanillaAdjacent(Context ctx, double width, double height)
    {
        Fill(ctx, 0, 0, width, height, Palette.Background);
        Fill(ctx, 3, 3, width - 6, height - 6, Palette.Inner);
        for (double y = 4; y < height - 4; y += 3) Line(ctx, 4, y, width - 4, y, WithAlpha(y % 6 == 0 ? Palette.Text : Palette.DarkText, 0.015), 1);
        StrokeRect(ctx, 2.5, 2.5, width - 5, height - 5, WithAlpha(Palette.Text, 0.28), 1);
        StrokeRect(ctx, 6.5, 6.5, width - 13, height - 13, WithAlpha(Palette.DarkText, 0.72), 2);
        Fill(ctx, 9, 7, width - 18, 27, C(0.08, 0.08, 0.07, 0.78));
        DrawSquareBolt(ctx, 14, 14);
        DrawSquareBolt(ctx, width - 14, height - 14);
    }

    private static void DrawWarmJournal(Context ctx, double width, double height)
    {
        Fill(ctx, 0, 0, width, height, Palette.Background);
        Fill(ctx, 3, 3, width - 6, height - 6, Palette.Inner);
        for (double y = 10; y < height; y += 17) Line(ctx, 8, y, width - 8, y + 3, WithAlpha(Palette.Text, 0.018), 1);
        RoundedRect(ctx, 8, 7, width - 16, height - 14, 14);
        SetSource(ctx, WithAlpha(Palette.Inner, 0.36));
        ctx.FillPreserve();
        SetSource(ctx, Palette.Border);
        ctx.LineWidth = 1.5;
        ctx.Stroke();
        DrawPaw(ctx, width - 76, height - 54, 0.86, 0.28);
        DrawBondMark(ctx, 33, height - 33);
    }

    private static void DrawEditorial(Context ctx, double width, double height)
    {
        Fill(ctx, 0, 0, width, height, Palette.Background);
        Fill(ctx, 7, 0, width - 7, height, Palette.Inner);
        Fill(ctx, 0, 0, 7, height, Palette.Accent);
        Fill(ctx, 7, 0, width - 7, 38, C(0.035, 0.037, 0.038, 1));
        Line(ctx, 7, 38, width, 38, Palette.Border, 1);
        for (double x = 40; x < width; x += 160) Line(ctx, x, 44, x, height - 14, WithAlpha(Palette.Text, 0.018), 1);
        Line(ctx, 19, height - 17, width - 19, height - 17, Palette.Border, 1);
        Fill(ctx, 19, height - 19, 82, 4, Palette.Accent);
    }

    private static void DrawPremium(Context ctx, double width, double height)
    {
        Fill(ctx, 0, 0, width, height, Palette.Background);
        for (double y = 0; y < height; y += 24)
        {
            double t = y / Math.Max(1, height);
            Fill(ctx, 3, y, width - 6, 24, C(0.028 + t * 0.012, 0.045 + t * 0.018, 0.072 + t * 0.022, 0.98));
        }
        for (double x = -height; x < width; x += 58) Line(ctx, x, height, x + height, 0, WithAlpha(Palette.Secondary, 0.035), 1);
        StrokeRect(ctx, 3.5, 3.5, width - 7, height - 7, Palette.Border, 1);
        StrokeRect(ctx, 8.5, 8.5, width - 17, height - 17, WithAlpha(Palette.Accent, 0.34), 1);
        DrawConstellation(ctx, width - 170, height - 86);
        DrawDiamond(ctx, 18, 18, 4, Palette.Accent);
    }

    private static void DrawMoonlit(Context ctx, double width, double height)
    {
        Fill(ctx, 0, 0, width, height, Palette.Background);
        Fill(ctx, 3, 3, width - 6, height - 6, Palette.Inner);
        DrawDisc(ctx, width - 92, 82, 52, C(0.55, 0.52, 0.94, 0.055));
        DrawDisc(ctx, width - 92, 82, 30, C(0.76, 0.70, 1.0, 0.065));
        for (int i = 0; i < 18; i++)
        {
            double x = 22 + ((i * 73) % Math.Max(30, (int)width - 44));
            double y = 45 + ((i * 97) % Math.Max(40, (int)height - 90));
            DrawDisc(ctx, x, y, i % 3 == 0 ? 1.4 : 0.8, WithAlpha(Palette.Text, 0.24));
        }
        StrokeRect(ctx, 4.5, 4.5, width - 9, height - 9, Palette.Border, 1);
        DrawCrescent(ctx, 33, height - 35, 14);
    }

    private static void DrawBlueprint(Context ctx, double width, double height)
    {
        Fill(ctx, 0, 0, width, height, Palette.Background);
        Fill(ctx, 3, 3, width - 6, height - 6, Palette.Inner);
        for (double x = 8; x < width; x += 24) Line(ctx, x, 6, x, height - 6, WithAlpha(Palette.Secondary, 0.075), 1);
        for (double y = 8; y < height; y += 24) Line(ctx, 6, y, width - 6, y, WithAlpha(Palette.Secondary, 0.075), 1);
        for (double x = 8; x < width; x += 120) Line(ctx, x, 6, x, height - 6, WithAlpha(Palette.Secondary, 0.12), 1);
        for (double y = 8; y < height; y += 120) Line(ctx, 6, y, width - 6, y, WithAlpha(Palette.Secondary, 0.12), 1);
        StrokeRect(ctx, 4.5, 4.5, width - 9, height - 9, Palette.Border, 1);
        DrawCompass(ctx, width - 62, height - 61, 28);
    }

    private static void DrawBoneAndBerry(Context ctx, double width, double height)
    {
        Fill(ctx, 0, 0, width, height, Palette.Background);
        Fill(ctx, 4, 4, width - 8, height - 8, Palette.Inner);
        for (double y = 12; y < height; y += 12) Line(ctx, 8, y, width - 8, y, WithAlpha(Palette.Text, 0.018), 1);
        StrokeRect(ctx, 5.5, 5.5, width - 11, height - 11, Palette.Border, 1.5);
        DrawBoneLashing(ctx, 20, height - 18, width - 40);
        DrawBerrySprig(ctx, width - 64, height - 52);
    }

    private static void DrawFoxfire(Context ctx, double width, double height)
    {
        Fill(ctx, 0, 0, width, height, Palette.Background);
        Fill(ctx, 3, 3, width - 6, height - 6, Palette.Inner);
        for (int i = 0; i < 14; i++)
        {
            double x = 28 + ((i * 83) % Math.Max(40, (int)width - 56));
            double y = 52 + ((i * 59) % Math.Max(40, (int)height - 104));
            DrawDisc(ctx, x, y, 7 + i % 3, WithAlpha(Palette.Accent, 0.018));
            DrawDisc(ctx, x, y, 1.2, WithAlpha(Palette.Accent, 0.22));
        }
        StrokeRect(ctx, 4.5, 4.5, width - 9, height - 9, Palette.Border, 1);
        DrawLeafRune(ctx, width - 53, height - 47);
        Line(ctx, 18, 8, width - 18, 8, WithAlpha(Palette.Accent, 0.55), 1);
    }

    private static void DrawSurfaceSigil(Context ctx, double width, double height, FoxGuiSurfaceKind kind)
    {
        double x = width - 58;
        double y = height - 43;
        double[] ink = WithAlpha(Palette.Accent, Variant == FoxGuiVariant.Editorial ? 0.18 : 0.24);
        switch (kind)
        {
            case FoxGuiSurfaceKind.Companion:
                DrawPaw(ctx, x, y, 0.56, ink[3]);
                break;
            case FoxGuiSurfaceKind.Pack:
                StrokeRect(ctx, x - 18, y - 10, 36, 20, ink, 1.5);
                DrawDisc(ctx, x - 11, y + 14, 5, ink);
                DrawDisc(ctx, x + 11, y + 14, 5, ink);
                Line(ctx, x - 24, y - 14, x + 21, y - 14, ink, 1);
                break;
            case FoxGuiSurfaceKind.Talents:
                Line(ctx, x, y + 15, x, y - 14, ink, 2);
                Line(ctx, x, y - 2, x - 17, y - 17, ink, 1.5);
                Line(ctx, x, y - 2, x + 17, y - 17, ink, 1.5);
                DrawDisc(ctx, x, y + 15, 4, ink);
                DrawDisc(ctx, x - 17, y - 17, 4, ink);
                DrawDisc(ctx, x + 17, y - 17, 4, ink);
                break;
            case FoxGuiSurfaceKind.Den:
                Line(ctx, x - 21, y, x, y - 18, ink, 2);
                Line(ctx, x, y - 18, x + 21, y, ink, 2);
                StrokeRect(ctx, x - 16, y, 32, 20, ink, 1.5);
                break;
            case FoxGuiSurfaceKind.Whistle:
                Line(ctx, x - 21, y - 7, x + 2, y + 8, ink, 4);
                SetSource(ctx, ink);
                ctx.LineWidth = 1.5;
                ctx.Arc(x + 4, y - 2, 11, -0.8, 0.8);
                ctx.Stroke();
                ctx.Arc(x + 6, y - 2, 19, -0.65, 0.65);
                ctx.Stroke();
                break;
        }
    }

    private static void DrawCornerFlourish(Context ctx, double x, double y, double sx, double sy)
    {
        double[] ink = WithAlpha(Palette.Accent, 0.46);
        Line(ctx, x, y, x + sx * 24, y, ink, 1);
        Line(ctx, x, y, x, y + sy * 24, ink, 1);
        DrawDiamond(ctx, x + sx * 7, y + sy * 7, 2.5, ink);
    }

    private static void DrawLeatherCorner(Context ctx, double x, double y)
    {
        Fill(ctx, x, y, 50, 32, C(0.10, 0.045, 0.022, 0.56));
        Line(ctx, x + 5, y + 7, x + 45, y + 7, WithAlpha(Palette.Accent, 0.36), 1);
        DrawRivet(ctx, x + 8, y + 16, 3);
        DrawRivet(ctx, x + 42, y + 16, 3);
    }

    private static void DrawBondMark(Context ctx, double x, double y)
    {
        DrawDisc(ctx, x - 5, y, 6, WithAlpha(Palette.Tertiary, 0.22));
        DrawDisc(ctx, x + 5, y, 6, WithAlpha(Palette.Secondary, 0.22));
        DrawDiamond(ctx, x, y + 8, 5, WithAlpha(Palette.Accent, 0.26));
    }

    private static void DrawConstellation(Context ctx, double x, double y)
    {
        double[] ink = WithAlpha(Palette.Secondary, 0.23);
        double[,] points = { { 0, 18 }, { 31, 2 }, { 61, 24 }, { 94, 8 }, { 125, 32 } };
        for (int i = 0; i < points.GetLength(0) - 1; i++) Line(ctx, x + points[i, 0], y + points[i, 1], x + points[i + 1, 0], y + points[i + 1, 1], ink, 1);
        for (int i = 0; i < points.GetLength(0); i++) DrawDisc(ctx, x + points[i, 0], y + points[i, 1], 2.2, ink);
    }

    private static void DrawCompass(Context ctx, double x, double y, double radius)
    {
        StrokeCircle(ctx, x, y, radius, WithAlpha(Palette.Secondary, 0.26), 1);
        Line(ctx, x, y - radius, x, y + radius, WithAlpha(Palette.Secondary, 0.30), 1);
        Line(ctx, x - radius, y, x + radius, y, WithAlpha(Palette.Secondary, 0.30), 1);
        ctx.NewPath();
        ctx.MoveTo(x, y - radius + 5);
        ctx.LineTo(x - 5, y + 3);
        ctx.LineTo(x + 5, y + 3);
        ctx.ClosePath();
        SetSource(ctx, WithAlpha(Palette.Accent, 0.34));
        ctx.Fill();
    }

    private static void DrawCrescent(Context ctx, double x, double y, double radius)
    {
        DrawDisc(ctx, x, y, radius, WithAlpha(Palette.Accent, 0.27));
        DrawDisc(ctx, x + 6, y - 4, radius, Palette.Inner);
    }

    private static void DrawBoneLashing(Context ctx, double x, double y, double width)
    {
        double[] ink = WithAlpha(Palette.Text, 0.19);
        Line(ctx, x, y, x + width, y, ink, 2);
        for (double dx = 0; dx < width; dx += 18) Line(ctx, x + dx, y - 4, x + dx + 8, y + 4, WithAlpha(Palette.Accent, 0.26), 1);
    }

    private static void DrawBerrySprig(Context ctx, double x, double y)
    {
        Line(ctx, x - 20, y + 17, x + 18, y - 18, WithAlpha(Palette.Secondary, 0.28), 2);
        DrawDisc(ctx, x - 3, y + 1, 5, WithAlpha(Palette.Accent, 0.33));
        DrawDisc(ctx, x + 8, y - 7, 4, WithAlpha(Palette.Accent, 0.29));
        DrawDisc(ctx, x - 12, y + 10, 4, WithAlpha(Palette.Accent, 0.25));
    }

    private static void DrawLeafRune(Context ctx, double x, double y)
    {
        double[] ink = WithAlpha(Palette.Accent, 0.28);
        Line(ctx, x - 22, y + 18, x + 18, y - 20, ink, 1.5);
        ctx.NewPath();
        ctx.MoveTo(x - 10, y + 5);
        ctx.CurveTo(x - 25, y - 8, x - 28, y + 15, x - 10, y + 5);
        SetSource(ctx, ink);
        ctx.Fill();
        ctx.NewPath();
        ctx.MoveTo(x + 6, y - 10);
        ctx.CurveTo(x + 2, y - 28, x + 25, y - 25, x + 6, y - 10);
        SetSource(ctx, ink);
        ctx.Fill();
    }

    private static void DrawStitch(Context ctx, double x, double y, double width)
    {
        SetSource(ctx, WithAlpha(Palette.Accent, 0.38));
        ctx.LineWidth = 1;
        for (double dx = 0; dx < width; dx += 14)
        {
            ctx.MoveTo(x + dx, y);
            ctx.LineTo(x + dx + 7, y);
        }
        ctx.Stroke();
    }

    private static void DrawPaw(Context ctx, double x, double y, double scale, double alpha)
    {
        SetSource(ctx, WithAlpha(Palette.Accent, alpha));
        ctx.Save();
        ctx.Translate(x, y);
        ctx.Scale(scale, scale);
        ctx.Arc(0, 7, 13, 0, Math.PI * 2); ctx.Fill();
        ctx.Arc(-14, -8, 5, 0, Math.PI * 2); ctx.Fill();
        ctx.Arc(-5, -15, 5, 0, Math.PI * 2); ctx.Fill();
        ctx.Arc(6, -15, 5, 0, Math.PI * 2); ctx.Fill();
        ctx.Arc(15, -7, 5, 0, Math.PI * 2); ctx.Fill();
        ctx.Restore();
    }

    private static void DrawRivet(Context ctx, double x, double y, double radius)
    {
        DrawDisc(ctx, x, y, radius + 1, WithAlpha(Palette.DarkText, 0.76));
        DrawDisc(ctx, x, y, radius, WithAlpha(Palette.Accent, 0.74));
        DrawDisc(ctx, x - radius * 0.3, y - radius * 0.3, Math.Max(0.7, radius * 0.28), WithAlpha(Palette.Text, 0.55));
    }

    private static void DrawSquareBolt(Context ctx, double x, double y)
    {
        Fill(ctx, x - 3, y - 3, 6, 6, WithAlpha(Palette.Text, 0.25));
        Line(ctx, x - 2, y, x + 2, y, WithAlpha(Palette.DarkText, 0.65), 1);
    }

    private static void DrawDiamond(Context ctx, double x, double y, double radius, double[] color)
    {
        ctx.NewPath();
        ctx.MoveTo(x, y - radius);
        ctx.LineTo(x + radius, y);
        ctx.LineTo(x, y + radius);
        ctx.LineTo(x - radius, y);
        ctx.ClosePath();
        SetSource(ctx, color);
        ctx.Fill();
    }

    private static void RoundedRect(Context ctx, double x, double y, double width, double height, double radius)
    {
        radius = Math.Min(radius, Math.Min(width, height) / 2);
        ctx.NewPath();
        ctx.Arc(x + width - radius, y + radius, radius, -Math.PI / 2, 0);
        ctx.Arc(x + width - radius, y + height - radius, radius, 0, Math.PI / 2);
        ctx.Arc(x + radius, y + height - radius, radius, Math.PI / 2, Math.PI);
        ctx.Arc(x + radius, y + radius, radius, Math.PI, Math.PI * 1.5);
        ctx.ClosePath();
    }

    private static void Fill(Context ctx, double x, double y, double width, double height, double[] color)
    {
        SetSource(ctx, color);
        ctx.Rectangle(x, y, width, height);
        ctx.Fill();
    }

    private static void StrokeRect(Context ctx, double x, double y, double width, double height, double[] color, double lineWidth)
    {
        SetSource(ctx, color);
        ctx.LineWidth = lineWidth;
        ctx.Rectangle(x, y, width, height);
        ctx.Stroke();
    }

    private static void Line(Context ctx, double x1, double y1, double x2, double y2, double[] color, double width)
    {
        SetSource(ctx, color);
        ctx.LineWidth = width;
        ctx.MoveTo(x1, y1);
        ctx.LineTo(x2, y2);
        ctx.Stroke();
    }

    private static void DrawDisc(Context ctx, double x, double y, double radius, double[] color)
    {
        SetSource(ctx, color);
        ctx.Arc(x, y, radius, 0, Math.PI * 2);
        ctx.Fill();
    }

    private static void StrokeCircle(Context ctx, double x, double y, double radius, double[] color, double width)
    {
        SetSource(ctx, color);
        ctx.LineWidth = width;
        ctx.Arc(x, y, radius, 0, Math.PI * 2);
        ctx.Stroke();
    }

    private static void SetSource(Context ctx, double[] color) => ctx.SetSourceRGBA(color[0], color[1], color[2], color[3]);
    private static double[] WithAlpha(double[] color, double alpha) => new[] { color[0], color[1], color[2], alpha };
    private static double[] C(double r, double g, double b, double a) => new[] { r, g, b, a };

    private sealed class ThemePalette
    {
        public double[] Background = Array.Empty<double>();
        public double[] Inner = Array.Empty<double>();
        public double[] Panel = Array.Empty<double>();
        public double[] Selected = Array.Empty<double>();
        public double[] Accent = Array.Empty<double>();
        public double[] Secondary = Array.Empty<double>();
        public double[] Tertiary = Array.Empty<double>();
        public double[] Text = Array.Empty<double>();
        public double[] Muted = Array.Empty<double>();
        public double[] DarkText = Array.Empty<double>();
        public double[] Border = Array.Empty<double>();
        public double[] Overlay = Array.Empty<double>();
        public double[] Danger = Array.Empty<double>();
        public double[] Warning = Array.Empty<double>();
        public double[] Success = Array.Empty<double>();
        public double[] DangerPanel = Array.Empty<double>();
        public double[] MutedPanel = Array.Empty<double>();
        public double[] Checkbox = Array.Empty<double>();
        public double[] Button = Array.Empty<double>();
        public double[] ButtonDisabled = Array.Empty<double>();
        public double[] Locked = Array.Empty<double>();
        public double[] LockedPanel = Array.Empty<double>();
        public double[] LockedBorder = Array.Empty<double>();
        public double[] ScrollTrack = Array.Empty<double>();
        public double[] ScrollThumb = Array.Empty<double>();
        public double[] Overview = Array.Empty<double>();
        public double[] Commands = Array.Empty<double>();
        public double[] Social = Array.Empty<double>();
        public double[] Talents = Array.Empty<double>();
        public double[] Combat = Array.Empty<double>();
        public double[] Survival = Array.Empty<double>();
        public double[] Return = Array.Empty<double>();
        public double[] TooltipShadow = Array.Empty<double>();
        public double[] Tooltip = Array.Empty<double>();
        public double[] Implemented = Array.Empty<double>();
        public double[] Future = Array.Empty<double>();

        public static ThemePalette Make(double[] background, double[] inner, double[] panel, double[] selected, double[] accent, double[] secondary, double[] tertiary, double[] text, double[] muted)
        {
            return new ThemePalette
            {
                Background = background,
                Inner = inner,
                Panel = panel,
                Selected = selected,
                Accent = accent,
                Secondary = secondary,
                Tertiary = tertiary,
                Text = text,
                Muted = muted,
                DarkText = C(0.055, 0.045, 0.036, 1),
                Border = Mix(inner, accent, 0.43, 0.72),
                Overlay = Mix(background, inner, 0.36, 0.985),
                Danger = C(0.88, 0.31, 0.24, 1),
                Warning = tertiary,
                Success = secondary,
                DangerPanel = C(0.30, 0.10, 0.09, 0.78),
                MutedPanel = Mix(background, panel, 0.40, 0.74),
                Checkbox = Mix(panel, accent, 0.24, 0.94),
                Button = Mix(panel, selected, 0.40, 0.96),
                ButtonDisabled = Mix(background, panel, 0.35, 0.90),
                Locked = Mix(muted, accent, 0.34, 1),
                LockedPanel = Mix(background, panel, 0.20, 0.97),
                LockedBorder = Mix(inner, accent, 0.31, 0.84),
                ScrollTrack = Mix(background, inner, 0.28, 0.72),
                ScrollThumb = Mix(inner, accent, 0.52, 0.96),
                Overview = tertiary,
                Commands = accent,
                Social = secondary,
                Talents = Mix(secondary, tertiary, 0.55, 1),
                Combat = accent,
                Survival = tertiary,
                Return = Mix(secondary, tertiary, 0.54, 1),
                TooltipShadow = C(0.005, 0.005, 0.006, 0.78),
                Tooltip = Mix(background, inner, 0.22, 1),
                Implemented = Mix(background, secondary, 0.20, 0.92),
                Future = Mix(background, C(0.75, 0.13, 0.10, 1), 0.22, 0.92)
            };
        }

        private static double[] Mix(double[] a, double[] b, double amount, double alpha) =>
            C(a[0] + (b[0] - a[0]) * amount, a[1] + (b[1] - a[1]) * amount, a[2] + (b[2] - a[2]) * amount, alpha);
    }
}
