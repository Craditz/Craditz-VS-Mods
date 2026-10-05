#nullable enable

using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace FeralKinshipCompanions;

/// <summary>Cached expedition and scavenging artwork for the Pack screens.</summary>
internal sealed class CompanionSceneArt : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly Dictionary<string, ImageSurface> surfaces = new(StringComparer.Ordinal);
    private readonly HashSet<string> missing = new(StringComparer.Ordinal);

    public CompanionSceneArt(ICoreClientAPI api) => this.api = api;

    public bool DrawMission(Context ctx, string missionId, double x, double y, double width, double height)
    {
        string name = missionId switch
        {
            FoxExpeditionType.Hunt => "hunt",
            FoxExpeditionType.GreatHunt => "great-hunt",
            FoxExpeditionType.ApexHunt => "apex-hunt",
            FoxExpeditionType.DeepWilds => "deep-wilds",
            FoxExpeditionType.Forage => "forage",
            FoxExpeditionType.DistantForage => "distant-forage",
            FoxExpeditionType.PrimevalReach => "primeval-reach",
            FoxExpeditionType.PackPatrol => "pack-patrol",
            FoxExpeditionType.Recruitment => "recruitment",
            FoxExpeditionType.SearchLost => "search-lost",
            FoxExpeditionType.Scout => "scout",
            FoxExpeditionType.Scavenge => "scavenge",
            FoxExpeditionType.RuinDelve => "ruin-delve",
            FoxExpeditionType.ResonantDepths => "resonant-depths",
            _ => "scavenge"
        };
        return Draw(ctx, $"scenes/missions/{name}.png", x, y, width, height);
    }

    public bool DrawSite(Context ctx, string label, double x, double y, double width, double height)
    {
        (string image, string marker) = FoxScavengeSites.SceneArtForLabel(label);
        if (!Draw(ctx, $"scenes/sites/{image}.png", x, y, width, height)) return false;
        if (marker.Length > 0) DrawSiteMark(ctx, marker, x, y, width, height);
        return true;
    }

    private bool Draw(Context ctx, string path, double x, double y, double width, double height)
    {
        ImageSurface? image = GetSurface(path);
        if (image == null) return false;
        ctx.Save();
        ctx.Rectangle(x, y, width, height);
        ctx.Clip();
        ctx.SetSourceRGB(.12, .12, .12);
        ctx.Paint();
        double scale = Math.Min(width / image.Width, height / image.Height);
        ctx.Translate(x + (width - image.Width * scale) / 2,
            y + (height - image.Height * scale) / 2);
        ctx.Scale(scale, scale);
        using SurfacePattern pattern = new(image)
        {
            Filter = path == "scenes/sites/unknowable.png" ? Filter.Bilinear : Filter.Nearest
        };
        ctx.SetSource(pattern);
        ctx.Paint();
        ctx.Restore();
        return true;
    }

    // Each named site shares its family picture but gets a small, stable pixel seal.
    // The label beside the picture supplies the actual identity; the seal ensures
    // two sites of one family are not visually identical in the compact list.
    private static void DrawSiteMark(Context ctx, string id,
        double x, double y, double width, double height)
    {
        if (width < 72 || height < 65) return;
        const double size = 21;
        double bx = x + width - size - 4, by = y + height - size - 4;
        ctx.Save();
        ctx.Rectangle(x, y, width, height);
        ctx.Clip();
        ctx.SetSourceRGBA(.075, .12, .09, .94);
        ctx.Rectangle(bx, by, size, size);
        ctx.Fill();
        uint hash = 2166136261;
        foreach (char letter in id) hash = (hash ^ letter) * 16777619;
        for (int row = 0; row < 5; row++)
        for (int col = 0; col < 3; col++)
        {
            if (((hash >> ((row * 3 + col) % 24)) & 1) == 0) continue;
            ctx.SetSourceRGB(.69, .84, .58);
            ctx.Rectangle(bx + 3 + col * 3, by + 3 + row * 3, 3, 3);
            ctx.Rectangle(bx + 3 + (4 - col) * 3, by + 3 + row * 3, 3, 3);
            ctx.Fill();
        }
        ctx.Restore();
    }

    private ImageSurface? GetSurface(string path)
    {
        if (surfaces.TryGetValue(path, out ImageSurface? cached)) return cached;
        if (missing.Contains(path)) return null;
        try
        {
            using ImageSurface original = GuiElement.getImageSurfaceFromAsset(
                api, new AssetLocation("feralkinshipcompanions", path));
            // Keep source proportions when reducing artwork for the cards.
            // In particular, the approved Great Hunt draft is not 3:2.
            double ratio = Math.Min(1d, Math.Min(384d / original.Width, 256d / original.Height));
            int reducedWidth = Math.Max(1, (int)Math.Round(original.Width * ratio));
            int reducedHeight = Math.Max(1, (int)Math.Round(original.Height * ratio));
            ImageSurface reduced = new(Format.Argb32, reducedWidth, reducedHeight);
            using (Context canvas = new(reduced))
            {
                canvas.Scale((double)reducedWidth / original.Width,
                    (double)reducedHeight / original.Height);
                using SurfacePattern pattern = new(original)
                {
                    Filter = path == "scenes/sites/unknowable.png" ? Filter.Bilinear : Filter.Nearest
                };
                canvas.SetSource(pattern);
                canvas.Paint();
            }
            surfaces.Add(path, reduced);
            return reduced;
        }
        catch (Exception exception)
        {
            missing.Add(path);
            api.Logger.Warning("[FeralKinshipCompanions] Could not load scene art {0}: {1}",
                path, exception.Message);
            return null;
        }
    }

    public void Dispose()
    {
        foreach (ImageSurface image in surfaces.Values) image.Dispose();
        surfaces.Clear();
    }
}
