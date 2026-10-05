#nullable disable

using Cairo;
using Vintagestory.API.Client;

namespace FeralKinshipCompanions;

/// <summary>
/// Shared coordinate handling for the hand-drawn companion surfaces.
/// Vintage Story scales ElementBounds for the user's GUI scale. These surfaces
/// still lay themselves out in a stable design coordinate system, then scale
/// that canvas into the actual texture so the artwork and hit regions agree.
/// </summary>
internal abstract class GuiElementFeralKinshipScaledSurface : GuiElement
{
    protected GuiElementFeralKinshipScaledSurface(ICoreClientAPI capi, ElementBounds bounds)
        : base(capi, bounds)
    {
    }

    protected abstract double DesignWidth { get; }
    protected abstract double DesignHeight { get; }

    protected double CanvasWidth => DesignWidth;
    protected double CanvasHeight => DesignHeight;

    protected double CanvasScaleX
    {
        get
        {
            double width = Bounds.OuterWidth;
            return width > 0 ? width / DesignWidth : 1d;
        }
    }

    protected double CanvasScaleY
    {
        get
        {
            double height = Bounds.OuterHeight;
            return height > 0 ? height / DesignHeight : 1d;
        }
    }

    protected double CanvasMouseX(double screenX)
    {
        return (screenX - Bounds.renderX) / CanvasScaleX;
    }

    protected double CanvasMouseY(double screenY)
    {
        return (screenY - Bounds.renderY) / CanvasScaleY;
    }

    protected void ScaleCanvas(Context ctx)
    {
        FeralKinshipCompanionUiSettings.EnsureLoaded(api);
        ctx.Scale(CanvasScaleX, CanvasScaleY);
    }

    protected bool HandleFontScaleClick(double x, double y)
    {
        return FeralKinshipCompanionUiSettings.TryHandleClick(api, x, y, CanvasWidth);
    }
}
