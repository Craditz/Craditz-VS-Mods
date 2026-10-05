using System;
using ProtoBuf;

namespace AnimalicaBodyTools.Config;

public sealed class AnimalicaBodyToolsConfig
{
    public const string RestrictionModeOptions = "Options: Default, Light, Disabled. Default reserves body tools for Animalica players and blocks their use of ordinary humanoid tools, except hammers and chisels. Light lets Animalica players use humanoid tools and allows anyone to craft body tools, while body tools remain Animalica-only. Disabled allows everyone to use and craft both kinds of tools.";
    public const string DiagnosticLoggingOptions = "Options: false, true. false keeps routine body-tool actions out of the logs. true enables detailed wearable resolution, durability, mining, harvesting, combat, scythe, and prospecting debug logging.";

    public string RestrictionMode { get; set; } = "Default";
    public string RestrictionModeDescription { get; set; } = string.Empty;
    public bool DiagnosticLogging { get; set; }
    public string DiagnosticLoggingDescription { get; set; } = string.Empty;

    public bool RefreshDescriptions()
    {
        bool changed = false;

        if (!string.Equals(RestrictionModeDescription, RestrictionModeOptions, StringComparison.Ordinal))
        {
            RestrictionModeDescription = RestrictionModeOptions;
            changed = true;
        }

        if (!string.Equals(DiagnosticLoggingDescription, DiagnosticLoggingOptions, StringComparison.Ordinal))
        {
            DiagnosticLoggingDescription = DiagnosticLoggingOptions;
            changed = true;
        }

        return changed;
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class AnimalicaBodyToolsConfigPacket
{
    public string RestrictionMode { get; set; } = "Default";
    public bool DiagnosticLogging { get; set; }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class CultivatingClawsPacket
{
    public const int StartAction = 1;
    public const int CancelAction = 2;

    public int Action { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Dimension { get; set; }

    public static CultivatingClawsPacket Start(Vintagestory.API.MathTools.BlockPos pos)
    {
        return new CultivatingClawsPacket
        {
            Action = StartAction,
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            Dimension = pos.dimension
        };
    }

    public static CultivatingClawsPacket Cancel()
    {
        return new CultivatingClawsPacket { Action = CancelAction };
    }
}

internal enum BodyToolRestrictionMode
{
    Default,
    Light,
    Disabled
}

internal static class AnimalicaBodyToolsPolicy
{
    public static BodyToolRestrictionMode CurrentMode { get; private set; } = BodyToolRestrictionMode.Default;

    public static bool RestrictAnimalToolUse => CurrentMode != BodyToolRestrictionMode.Disabled;
    public static bool RestrictAnimalToolCrafting => CurrentMode == BodyToolRestrictionMode.Default;
    public static bool DiagnosticLogging { get; private set; }

    public static string Apply(string? mode, bool diagnosticLogging = false)
    {
        CurrentMode = Parse(mode);
        DiagnosticLogging = diagnosticLogging;
        return CurrentMode.ToString();
    }

    private static BodyToolRestrictionMode Parse(string? mode)
    {
        if (string.Equals(mode, "Light", StringComparison.OrdinalIgnoreCase))
        {
            return BodyToolRestrictionMode.Light;
        }

        if (string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            return BodyToolRestrictionMode.Disabled;
        }

        return BodyToolRestrictionMode.Default;
    }
}
