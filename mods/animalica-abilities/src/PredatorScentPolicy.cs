using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace ScentTrails;

internal static class PredatorScentPolicy
{
    internal static bool IsPredator(Entity entity)
    {
        // Author-provided species metadata, independent of current aggression.
        // Use the source's registry so modded tags resolve on the server too.
        if (entity.Api == null || entity.Properties == null) return false;
        entity.Api.EntityTagRegistry.TryCreateTagSet(out var predator, "predator");
        return entity.Properties.Tags.Overlaps(predator);
    }

    internal static (int Red, int Green, int Blue) Color => (255, 104, 64);
    internal static (int Red, int Green, int Blue) Glow => (185, 65, 35);

    // Particle tint is ARGB, but the native light buffer reads red from the
    // least significant byte. Keep this conversion local to the new palette.
    internal static int LightEmission(float multiplier) => ColorUtil.ToRgba(
        255,
        GameMath.Clamp((int)(Glow.Blue * multiplier), 0, 255),
        GameMath.Clamp((int)(Glow.Green * multiplier), 0, 255),
        GameMath.Clamp((int)(Glow.Red * multiplier), 0, 255));
}
