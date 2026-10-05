using System;

namespace ScentTrails;

public enum NightVisionStrengthProfile
{
    Weak,
    Normal,
    Strong
}

public sealed class AnimalicaNightVisionTuning
{
    public bool Enabled { get; set; }

    public float BrightnessLift { get; private set; } = 1.0f;

    public float GrayscaleStrength { get; private set; } = 1.0f;

    public float DarknessStart { get; private set; } = 0.05f;

    public float DarknessRange { get; private set; } = 0.02f;

    public float DarknessCurve { get; private set; } = 1.0f;

    public float HighlightProtection { get; private set; } = 1.0f;

    public float EnvironmentalLightResponse { get; private set; } = 1.0f;

    public void ApplyProfile(NightVisionStrengthProfile profile)
    {
        switch (profile)
        {
            case NightVisionStrengthProfile.Weak:
                BrightnessLift = 0.5f;
                GrayscaleStrength = 0.5f;
                DarknessStart = 0.03f;
                DarknessRange = 0.02f;
                DarknessCurve = 1.0f;
                HighlightProtection = 1.0f;
                EnvironmentalLightResponse = 1.0f;
                break;
            case NightVisionStrengthProfile.Normal:
                BrightnessLift = 0.75f;
                GrayscaleStrength = 0.75f;
                DarknessStart = 0.04f;
                DarknessRange = 0.02f;
                DarknessCurve = 1.0f;
                HighlightProtection = 1.0f;
                EnvironmentalLightResponse = 1.0f;
                break;
            case NightVisionStrengthProfile.Strong:
                BrightnessLift = 1.0f;
                GrayscaleStrength = 1.0f;
                DarknessStart = 0.05f;
                DarknessRange = 0.02f;
                DarknessCurve = 1.0f;
                HighlightProtection = 1.0f;
                EnvironmentalLightResponse = 1.0f;
                break;
        }
    }

    public void AdjustBrightness(float amount)
    {
        BrightnessLift = Clamp(BrightnessLift + amount, 0f, 10f);
    }

    public void AdjustGrayscale(float amount)
    {
        GrayscaleStrength = Clamp(GrayscaleStrength + amount, 0f, 1f);
    }

    public void AdjustDarknessStart(float amount)
    {
        DarknessStart = Clamp(DarknessStart + amount, 0f, 1.5f);
    }

    public void AdjustDarknessRange(float amount)
    {
        DarknessRange = Clamp(DarknessRange + amount, 0.01f, 2f);
    }

    public void AdjustDarknessCurve(float amount)
    {
        DarknessCurve = Clamp(DarknessCurve + amount, 0.05f, 10f);
    }

    public void AdjustHighlightProtection(float amount)
    {
        HighlightProtection = Clamp(HighlightProtection + amount, 0f, 1f);
    }

    public void AdjustEnvironmentalLightResponse(float amount)
    {
        EnvironmentalLightResponse = Clamp(EnvironmentalLightResponse + amount, 0f, 10f);
    }

    public void Reset()
    {
        BrightnessLift = 1.0f;
        GrayscaleStrength = 1.0f;
        DarknessStart = 0.05f;
        DarknessRange = 0.02f;
        DarknessCurve = 1.0f;
        HighlightProtection = 1.0f;
        EnvironmentalLightResponse = 1.0f;
    }

    private static float Clamp(float value, float minimum, float maximum)
    {
        return Math.Clamp(value, minimum, maximum);
    }
}
