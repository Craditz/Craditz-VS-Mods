#nullable disable

using System;
using Cairo;
using Vintagestory.API.Client;
using VintageStoryConfigMigration;

namespace FeralKinshipCompanions;

internal sealed class FeralKinshipCompanionUiConfig
{
    public double TextScale { get; set; } = 1d;
    public string Theme { get; set; } = "warm-journal";
}

internal static class FeralKinshipCompanionUiSettings
{
    private const string ConfigFileName = "feralkinshipcompanions-ui.json";
    private const double DefaultTextScale = 1d;
    private const double MinimumTextScale = 0.5d;
    private const double TextScaleStep = 0.1d;
    private static bool loaded;
    private static double textScale = DefaultTextScale;

    public static double TextScale => textScale;

    public static void EnsureLoaded(ICoreClientAPI api)
    {
        if (loaded || api == null)
        {
            return;
        }

        loaded = true;
        try
        {
            FeralKinshipCompanionUiConfig saved = ConfigDefaults.LoadAndUpdate(
                api,
                ConfigFileName,
                () => new FeralKinshipCompanionUiConfig());
            textScale = saved.TextScale > 0d ? saved.TextScale : DefaultTextScale;
            if (FoxGuiTheme.TryParsePlayerVariant(saved.Theme, out FoxGuiVariant savedVariant))
            {
                FoxGuiTheme.SetPlayerVariant(savedVariant);
            }
            else
            {
                FoxGuiTheme.SetPlayerVariant(FoxGuiVariant.WarmJournal);
            }
        }
        catch (Exception exception)
        {
            textScale = DefaultTextScale;
            FoxGuiTheme.SetPlayerVariant(FoxGuiVariant.WarmJournal);
            api.Logger.Warning(
                "[FeralKinshipCompanions] Could not load the local UI text setting; using the default. {0}",
                exception.Message
            );
        }
    }

    public static bool TryHandleClick(ICoreClientAPI api, double x, double y, double width)
    {
        EnsureLoaded(api);
        if (FoxGuiTheme.IsThemeHovered(x, y, width))
        {
            FoxGuiTheme.CyclePlayerVariant();
            Save(api);
            return true;
        }

        if (FoxGuiTheme.IsFontMinusHovered(x, y, width))
        {
            Adjust(api, -TextScaleStep);
            return true;
        }

        if (FoxGuiTheme.IsFontPlusHovered(x, y, width))
        {
            Adjust(api, TextScaleStep);
            return true;
        }

        return false;
    }

    public static double ScaleFont(double size)
    {
        return Math.Max(1d, size * textScale);
    }

    public static double GetTextWidth(string text, double fontSize)
    {
        CairoFont font = CairoFont.WhiteSmallText().WithFontSize((float)ScaleFont(fontSize));
        try
        {
            return font.GetTextExtents(text ?? string.Empty).Width;
        }
        finally
        {
            font.Dispose();
        }
    }

    public static string TrimTextToWidth(string text, double maxWidth, double fontSize)
    {
        text ??= string.Empty;
        if (GetTextWidth(text, fontSize) <= maxWidth)
        {
            return text;
        }

        const string ellipsis = "…";
        for (int length = text.Length - 1; length > 0; length--)
        {
            string candidate = text.Substring(0, length) + ellipsis;
            if (GetTextWidth(candidate, fontSize) <= maxWidth)
            {
                return candidate;
            }
        }
        return ellipsis;
    }

    private static void Adjust(ICoreClientAPI api, double amount)
    {
        textScale = Math.Max(MinimumTextScale, textScale + amount);
        Save(api);
    }

    private static void Save(ICoreClientAPI api)
    {
        try
        {
            ConfigDefaults.StorePreservingUnknown(
                api,
                ConfigFileName,
                new FeralKinshipCompanionUiConfig
                {
                    TextScale = textScale,
                    Theme = FoxGuiTheme.VariantId
                });
        }
        catch (Exception exception)
        {
            api.Logger.Warning(
                "[FeralKinshipCompanions] Could not save the local UI text setting. {0}",
                exception.Message
            );
        }
    }
}
