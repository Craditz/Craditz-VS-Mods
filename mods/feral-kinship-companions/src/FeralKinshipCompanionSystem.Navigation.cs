#nullable enable

using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    internal void LogCompanionNavigationDiagnostic(Entity entity, string message)
    {
        // Existing diagnostic opt-in and per-entity/stage three-second throttle.
        LogDutyDiagnostic(entity, "route-summary", message);
    }

    internal void LogCompanionPhysicsDiagnostic(Entity entity, string stage, string message)
    {
        // Keep the unload/reload and solid-overlap trace opt-in and bounded by
        // the existing per-entity/stage diagnostic throttle.
        LogDutyDiagnostic(entity, stage, message);
    }
}
