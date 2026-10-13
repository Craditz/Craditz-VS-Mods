#nullable enable
using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
namespace FeralKinshipCompanions;

// Fit complete paragraphs into an existing cell without changing global font settings.
internal static class CompanionGuiParagraph
{
    internal sealed record LayoutResult(float FontSize, string[] Lines, double Ascent, double LineHeight, double Height);
    internal static LayoutResult Layout(Context ctx, string text, double width, double height, double fontSize)
    {
        string[] words = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        using CairoFont font = CairoFont.WhiteSmallText();
        LayoutResult Measure(float size)
        {
            font.WithFontSize(size).SetupContext(ctx);
            var lines = new List<string>();
            string line = string.Empty;
            foreach (string word in words)
            {
                string next = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Advance(ctx, next) > width)
                { lines.Add(line); line = word; }
                else line = next;
            }
            if (line.Length > 0) lines.Add(line);
            FontExtents ext = ctx.FontExtents;
            double spacing = Math.Max(1, ext.Height);
            return new(size, lines.ToArray(), ext.Ascent, spacing,
                lines.Count == 0 ? 0 : (lines.Count - 1) * spacing + ext.Ascent + ext.Descent);
        }
        bool Fits(LayoutResult result)
        {
            if (result.Height > height) return false;
            foreach (string line in result.Lines) if (Advance(ctx, line) > width) return false;
            return true;
        }
        float upper = (float)FeralKinshipCompanionUiSettings.ScaleFont(fontSize);
        LayoutResult requested = Measure(upper);
        if (Fits(requested)) return requested;
        float lower = 1;
        for (int i = 0; i < 28; i++)
        {
            float middle = (lower + upper) / 2;
            if (Fits(Measure(middle))) lower = middle; else upper = middle;
        }
        return Measure(lower);
    }
    private static double Advance(Context ctx, string text)
    {
        TextExtents ext = ctx.TextExtents(text);
        return Math.Max(ext.XAdvance, ext.Width + Math.Max(0, ext.XBearing));
    }
    internal static void Draw(Context ctx, string text, double x, double top, double width, double height, double size, double[] color)
    {
        LayoutResult layout = Layout(ctx, text, width, height, size);
        using CairoFont font = CairoFont.WhiteSmallText().WithFontSize(layout.FontSize);
        font.Color = color; font.SetupContext(ctx);
        for (int i = 0; i < layout.Lines.Length; i++)
        { ctx.MoveTo(x, top + layout.Ascent + i * layout.LineHeight); ctx.ShowText(layout.Lines[i]); }
    }
}
