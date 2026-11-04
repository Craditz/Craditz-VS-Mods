using Cairo;
using Vintagestory.API.Client;

namespace Druidry;

internal abstract class GuiElementDruidScaledSurface : GuiElement
{
    protected GuiElementDruidScaledSurface(ICoreClientAPI capi, ElementBounds bounds) : base(capi, bounds)
    {
    }

    protected abstract double DesignWidth { get; }
    protected abstract double DesignHeight { get; }
    protected double CanvasWidth => DesignWidth;
    protected double CanvasHeight => DesignHeight;
    private double ScaleX => Bounds.OuterWidth > 0 ? Bounds.OuterWidth / DesignWidth : 1d;
    private double ScaleY => Bounds.OuterHeight > 0 ? Bounds.OuterHeight / DesignHeight : 1d;
    protected double CanvasMouseX(double screenX) => (screenX - Bounds.renderX) / ScaleX;
    protected double CanvasMouseY(double screenY) => (screenY - Bounds.renderY) / ScaleY;
    protected void ScaleCanvas(Context context) => context.Scale(ScaleX, ScaleY);
}
