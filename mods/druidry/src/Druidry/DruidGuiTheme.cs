using System;
using Cairo;
using Vintagestory.API.Client;

namespace Druidry;

internal static class DruidGuiTheme
{
    public static readonly double[] Ink = { 0.91, 0.88, 0.75, 1.0 };
    public static readonly double[] Muted = { 0.66, 0.69, 0.59, 1.0 };
    public static readonly double[] Moss = { 0.43, 0.68, 0.39, 1.0 };
    public static readonly double[] Gold = { 0.85, 0.67, 0.28, 1.0 };
    public static readonly double[] Ember = { 0.82, 0.38, 0.22, 1.0 };
    public static readonly double[] Water = { 0.30, 0.62, 0.70, 1.0 };
    public static readonly double[] Locked = { 0.38, 0.39, 0.35, 1.0 };
    private static readonly double[] Deep = { 0.045, 0.075, 0.062, 1.0 };
    private static readonly double[] Panel = { 0.085, 0.125, 0.102, 0.96 };
    private static readonly double[] PanelSelected = { 0.12, 0.20, 0.145, 0.98 };
    private static readonly double[] Border = { 0.30, 0.40, 0.27, 0.9 };

    public static void DrawBackground(Context ctx, double width, double height)
    {
        Set(ctx, Deep);
        ctx.Rectangle(0, 0, width, height);
        ctx.Fill();

        ctx.LineWidth = 1;
        for (int ring = 0; ring < 4; ring++)
        {
            Set(ctx, new[] { Moss[0], Moss[1], Moss[2], 0.08 + ring * 0.025 });
            ctx.Arc(width - 110, height - 92, 115 + ring * 34, 0, Math.PI * 2);
            ctx.Stroke();
        }

        Set(ctx, new[] { Gold[0], Gold[1], Gold[2], 0.16 });
        ctx.LineWidth = 1.2;
        ctx.MoveTo(12, 54);
        ctx.CurveTo(width * 0.25, 28, width * 0.6, 78, width - 18, 43);
        ctx.Stroke();
        for (int x = 32; x < width - 20; x += 88)
        {
            ctx.MoveTo(x, 48);
            ctx.CurveTo(x + 8, 35, x + 15, 36, x + 20, 49);
            ctx.CurveTo(x + 12, 55, x + 7, 56, x, 48);
            ctx.Stroke();
        }

        Set(ctx, new[] { Moss[0], Moss[1], Moss[2], 0.42 });
        ctx.LineWidth = 2;
        ctx.Rectangle(7, 7, width - 14, height - 14);
        ctx.Stroke();
        Set(ctx, new[] { Gold[0], Gold[1], Gold[2], 0.22 });
        ctx.LineWidth = 1;
        ctx.Rectangle(11, 11, width - 22, height - 22);
        ctx.Stroke();
    }

    public static void DrawPanel(Context ctx, double x, double y, double width, double height, bool selected, bool available = true)
    {
        RoundedRect(ctx, x, y, width, height, 7);
        Set(ctx, selected ? PanelSelected : Panel);
        ctx.FillPreserve();
        Set(ctx, available ? (selected ? Moss : Border) : Locked);
        ctx.LineWidth = selected ? 1.8 : 1;
        ctx.Stroke();
        if (selected)
        {
            Set(ctx, new[] { Gold[0], Gold[1], Gold[2], 0.8 });
            ctx.Rectangle(x, y + 8, 3, height - 16);
            ctx.Fill();
        }
    }

    public static void DrawButton(Context ctx, double x, double y, double width, double height, bool enabled, bool highlighted)
    {
        DrawPanel(ctx, x, y, width, height, highlighted, enabled);
        if (!enabled)
        {
            Set(ctx, new[] { 0.03, 0.04, 0.035, 0.46 });
            RoundedRect(ctx, x + 1, y + 1, width - 2, height - 2, 6);
            ctx.Fill();
        }
    }

    public static void DrawGlyph(Context ctx, DruidGlyph glyph, double centerX, double centerY, double size, double[] color)
    {
        Set(ctx, color);
        ctx.LineWidth = Math.Max(1.2, size * 0.07);
        ctx.LineCap = LineCap.Round;
        switch (glyph)
        {
            case DruidGlyph.Fish:
                ctx.Arc(centerX - size * 0.08, centerY, size * 0.25, 0, Math.PI * 2);
                ctx.Stroke();
                ctx.MoveTo(centerX + size * 0.17, centerY);
                ctx.LineTo(centerX + size * 0.42, centerY - size * 0.22);
                ctx.LineTo(centerX + size * 0.42, centerY + size * 0.22);
                ctx.ClosePath();
                ctx.Stroke();
                break;
            case DruidGlyph.Feather:
                ctx.MoveTo(centerX - size * 0.25, centerY + size * 0.35);
                ctx.CurveTo(centerX - size * 0.12, centerY - size * 0.35, centerX + size * 0.33, centerY - size * 0.38, centerX + size * 0.22, centerY + size * 0.12);
                ctx.CurveTo(centerX + size * 0.04, centerY + size * 0.31, centerX - size * 0.10, centerY + size * 0.18, centerX - size * 0.25, centerY + size * 0.35);
                ctx.Stroke();
                ctx.MoveTo(centerX - size * 0.24, centerY + size * 0.34);
                ctx.LineTo(centerX + size * 0.22, centerY - size * 0.22);
                ctx.Stroke();
                break;
            case DruidGlyph.Antler:
            case DruidGlyph.Horn:
                ctx.MoveTo(centerX, centerY + size * 0.35);
                ctx.CurveTo(centerX - size * 0.08, centerY, centerX - size * 0.32, centerY - size * 0.12, centerX - size * 0.30, centerY - size * 0.38);
                ctx.MoveTo(centerX, centerY + size * 0.35);
                ctx.CurveTo(centerX + size * 0.08, centerY, centerX + size * 0.32, centerY - size * 0.12, centerX + size * 0.30, centerY - size * 0.38);
                if (glyph == DruidGlyph.Antler)
                {
                    ctx.MoveTo(centerX - size * 0.18, centerY - size * 0.06); ctx.LineTo(centerX - size * 0.38, centerY - size * 0.18);
                    ctx.MoveTo(centerX + size * 0.18, centerY - size * 0.06); ctx.LineTo(centerX + size * 0.38, centerY - size * 0.18);
                }
                ctx.Stroke();
                break;
            case DruidGlyph.Ear:
                ctx.MoveTo(centerX - size * 0.22, centerY + size * 0.30);
                ctx.CurveTo(centerX - size * 0.36, centerY - size * 0.30, centerX - size * 0.20, centerY - size * 0.42, centerX, centerY + size * 0.12);
                ctx.MoveTo(centerX + size * 0.22, centerY + size * 0.30);
                ctx.CurveTo(centerX + size * 0.36, centerY - size * 0.30, centerX + size * 0.20, centerY - size * 0.42, centerX, centerY + size * 0.12);
                ctx.Stroke();
                break;
            case DruidGlyph.Tusk:
            case DruidGlyph.Fang:
                ctx.MoveTo(centerX - size * 0.28, centerY - size * 0.28);
                ctx.CurveTo(centerX - size * 0.14, centerY + size * 0.36, centerX + size * 0.12, centerY + size * 0.35, centerX + size * 0.28, centerY - size * 0.28);
                ctx.Stroke();
                break;
            case DruidGlyph.Mask:
                ctx.Arc(centerX, centerY, size * 0.34, 0, Math.PI * 2);
                ctx.Stroke();
                ctx.Arc(centerX - size * 0.13, centerY - size * 0.02, size * 0.055, 0, Math.PI * 2);
                ctx.Arc(centerX + size * 0.13, centerY - size * 0.02, size * 0.055, 0, Math.PI * 2);
                ctx.Fill();
                break;
            case DruidGlyph.Wool:
                for (int i = 0; i < 6; i++)
                {
                    double angle = i * Math.PI / 3;
                    ctx.Arc(centerX + Math.Cos(angle) * size * 0.16, centerY + Math.Sin(angle) * size * 0.16, size * 0.17, 0, Math.PI * 2);
                }
                ctx.Stroke();
                break;
            case DruidGlyph.Tail:
                ctx.MoveTo(centerX - size * 0.34, centerY + size * 0.18);
                ctx.CurveTo(centerX - size * 0.06, centerY + size * 0.42, centerX + size * 0.42, centerY + size * 0.10, centerX + size * 0.18, centerY - size * 0.34);
                ctx.CurveTo(centerX + size * 0.07, centerY - size * 0.12, centerX + size * 0.06, centerY + size * 0.08, centerX - size * 0.34, centerY + size * 0.18);
                ctx.Stroke();
                break;
            default:
                DrawPaw(ctx, centerX, centerY, size);
                break;
        }
    }

    private static void DrawPaw(Context ctx, double x, double y, double size)
    {
        ctx.Arc(x, y + size * 0.13, size * 0.20, 0, Math.PI * 2);
        ctx.Fill();
        for (int i = -2; i <= 2; i++)
        {
            if (i == 0) continue;
            double px = x + i * size * 0.12;
            double py = y - size * (0.15 + (Math.Abs(i) == 2 ? 0 : 0.06));
            ctx.Arc(px, py, size * 0.075, 0, Math.PI * 2);
            ctx.Fill();
        }
    }

    private static void RoundedRect(Context ctx, double x, double y, double width, double height, double radius)
    {
        double right = x + width;
        double bottom = y + height;
        ctx.NewSubPath();
        ctx.Arc(right - radius, y + radius, radius, -Math.PI / 2, 0);
        ctx.Arc(right - radius, bottom - radius, radius, 0, Math.PI / 2);
        ctx.Arc(x + radius, bottom - radius, radius, Math.PI / 2, Math.PI);
        ctx.Arc(x + radius, y + radius, radius, Math.PI, Math.PI * 1.5);
        ctx.ClosePath();
    }

    private static void Set(Context ctx, double[] color) => ctx.SetSourceRGBA(color[0], color[1], color[2], color[3]);
}
