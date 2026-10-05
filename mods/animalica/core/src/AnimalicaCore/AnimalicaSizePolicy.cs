using System;

namespace AnimalicaCore;

internal readonly record struct AnimalicaSizeSettings(
    double Minimum,
    double Maximum,
    double Default
);

internal static class AnimalicaSizePolicy
{
    public static AnimalicaSizeSettings Normalize(AnimalicaCoreConfig config)
    {
        double minimum = ClampFinite(config.SizeMinimum, 0.01, 100.0, 0.5);
        double maximum = ClampFinite(config.SizeMaximum, 0.01, 100.0, 3.0);
        if (maximum < minimum)
        {
            (minimum, maximum) = (maximum, minimum);
        }

        double defaultSize = ClampFinite(config.DefaultSize, minimum, maximum, 1.0);
        return new AnimalicaSizeSettings(minimum, maximum, defaultSize);
    }

    public static bool ShouldOverridePackDefaults(AnimalicaCoreConfig config)
    {
        if (!config.EnableSizeOverrides)
        {
            return false;
        }

        AnimalicaSizeSettings settings = Normalize(config);
        return Math.Abs(settings.Minimum - 0.5) >= 0.000001
            || Math.Abs(settings.Maximum - 3.0) >= 0.000001
            || Math.Abs(settings.Default - 1.0) >= 0.000001;
    }

    private static double ClampFinite(double value, double minimum, double maximum, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            value = fallback;
        }

        return Math.Clamp(value, minimum, maximum);
    }
}
