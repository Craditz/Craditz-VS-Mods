using System;

namespace AnimalicaCore;

// Camera state belongs to one selector dialog. It never changes an entity or model.
internal sealed class CaribouSkinPreviewCamera
{
    private bool caribouActive;
    private float previousZoom;
    private float previousHeight;

    internal void Apply(string modelCode, ref float zoom, ref float height)
    {
        bool caribou = string.Equals(modelCode, "feraldeer:feraldeer-caribou", StringComparison.Ordinal)
            || string.Equals(modelCode, "feraldeer-caribou", StringComparison.Ordinal);

        if (caribou && !caribouActive)
        {
            previousZoom = zoom;
            previousHeight = height;
            zoom = 0.5f;
            height = 0f;
            caribouActive = true;
        }
        else if (!caribou && caribouActive)
        {
            zoom = previousZoom;
            height = previousHeight;
            caribouActive = false;
        }
        // Recomposing a Caribou skin/feature tab preserves the user's zoom and pan.
    }
}
