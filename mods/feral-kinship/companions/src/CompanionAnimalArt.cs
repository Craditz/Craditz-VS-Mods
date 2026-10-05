#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Path = System.IO.Path;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;

/// <summary>Animal art for the pack and individual companion screens.</summary>
internal sealed class CompanionAnimalArt : IDisposable
{
    // These are the native type names represented by full-body masters. Keep
    // this allowlist in step with the vanilla variants in CompanionSpeciesCatalog.
    private static readonly HashSet<string> NativeTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bear-black", "bear-brown", "bear-sun", "bear-panda", "bear-polar",
        "deer-whitetail", "deer-redbrocket", "deer-marsh", "deer-caribou",
        "deer-water", "deer-pudu", "deer-elk", "deer-moose", "deer-taruca",
        "deer-chital", "deer-guemal", "deer-pampas", "deer-fallow",
        "goat-angora", "goat-ibexalp", "goat-ibexnub", "goat-markhor",
        "goat-mountain", "goat-muskox", "goat-nubian", "goat-sirohi",
        "goat-takingold", "goat-turdag", "goat-valais",
        "hare-arctic", "hare-cape", "hare-european", "hare-indian",
        "hare-jackblack", "hare-scrub",
        "pig-eurasian", "pig-redriver", "pig-warthog",
        "sheep-bighorn", "sheep-mouflon",
        "deer-whitetail-female", "deer-redbrocket-female",
        "deer-marsh-female", "deer-caribou-female", "deer-pudu-female",
        "deer-elk-female", "deer-moose-female", "deer-taruca-female",
        "deer-chital-female", "deer-guemal-female",
        "deer-pampas-female", "deer-fallow-female",
        "sheep-bighorn-female", "sheep-mouflon-female"
    };

    private readonly ICoreClientAPI api;
    private readonly Dictionary<string, ImageSurface> surfaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImageSurface> customSurfaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> customLastUse = new(StringComparer.OrdinalIgnoreCase);
    private long customUseSequence;
    private long customSurfaceBytes;
    private readonly HashSet<string> rejectedCustomFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly PortraitAssignments portraitAssignments;
    private const string PortraitConfigName = "FeralKinshipCompanionPortraits.json";
    private const long MaxPngBytes = 16L * 1024 * 1024;
    private const int MaxCustomDimension = 1024;
    private const long MaxCustomSurfaceBytes = 16L * 1024 * 1024;

    private sealed class PortraitAssignments
    {
        public PortraitAssignments() { }
        public Dictionary<string, string> ByCompanion = new(StringComparer.Ordinal);
    }

    public CompanionAnimalArt(ICoreClientAPI api)
    {
        this.api = api;
        try { portraitAssignments = api.LoadModConfig<PortraitAssignments>(PortraitConfigName) ?? new PortraitAssignments(); }
        catch (Exception e)
        {
            api.Logger.Warning("[FeralKinshipCompanions] Could not read local portrait choices: {0}", e.Message);
            portraitAssignments = new PortraitAssignments();
        }
        portraitAssignments.ByCompanion ??= new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public string PortraitFolder => Path.Combine(api.GetOrCreateDataPath("ModConfig"), "FeralKinshipCompanionPortraits");

    public string[] ListCustomImages()
    {
        try
        {
            Directory.CreateDirectory(PortraitFolder);
            return Directory.EnumerateFiles(PortraitFolder, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => IsSafePngName(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray()!;
        }
        catch (Exception e)
        {
            api.Logger.Warning("[FeralKinshipCompanions] Could not list local portraits: {0}", e.Message);
            return Array.Empty<string>();
        }
    }

    public string? AssignedImage(string? foxId)
    {
        string? key = PortraitKey(foxId);
        return key != null && portraitAssignments.ByCompanion.TryGetValue(key, out string? name) ? name : null;
    }

    public bool ChooseImage(string? foxId, string? fileName)
    {
        string? key = PortraitKey(foxId);
        if (key == null || (fileName != null && (!IsSafePngName(fileName) || GetCustomSurface(fileName) == null))) return false;
        portraitAssignments.ByCompanion.TryGetValue(key, out string? previous);
        if (fileName == null) portraitAssignments.ByCompanion.Remove(key);
        else portraitAssignments.ByCompanion[key] = fileName;
        try { api.StoreModConfig(portraitAssignments, PortraitConfigName); return true; }
        catch (Exception e)
        {
            if (previous == null) portraitAssignments.ByCompanion.Remove(key);
            else portraitAssignments.ByCompanion[key] = previous;
            api.Logger.Warning("[FeralKinshipCompanions] Could not save local portrait choice: {0}", e.Message);
            return false;
        }
    }

    public void RefreshCustomImages()
    {
        foreach (ImageSurface image in customSurfaces.Values) image.Dispose();
        customSurfaces.Clear();
        customLastUse.Clear();
        customSurfaceBytes = 0;
        rejectedCustomFiles.Clear();
    }

    public void DrawCustomPreview(Context ctx, string fileName, double x, double y, double size)
    {
        ImageSurface? image = GetCustomSurface(fileName);
        if (image != null) DrawContained(ctx, image, x, y, size);
    }

    private string? PortraitKey(string? foxId)
    {
        if (string.IsNullOrWhiteSpace(foxId)) return null;
        string worldId = api.World?.SavegameIdentifier ?? string.Empty;
        return string.IsNullOrWhiteSpace(worldId) ? null : worldId + "|" + foxId;
    }

    private static bool IsSafePngName(string? name) => !string.IsNullOrWhiteSpace(name)
        && name == Path.GetFileName(name)
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !name.Contains('/') && !name.Contains('\\')
        && name.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

    private ImageSurface? GetCustomSurface(string fileName)
    {
        if (!IsSafePngName(fileName) || rejectedCustomFiles.Contains(fileName)) return null;
        if (customSurfaces.TryGetValue(fileName, out ImageSurface? cached))
        {
            customLastUse[fileName] = ++customUseSequence;
            return cached;
        }
        try
        {
            string path = Path.Combine(PortraitFolder, fileName);
            FileInfo info = new(path);
            if (!info.Exists || info.Length < 24 || info.Length > MaxPngBytes) throw new InvalidDataException("PNG is missing or exceeds 16 MB");
            byte[] data = File.ReadAllBytes(path);
            if (data[0] != 137 || data[1] != 80 || data[2] != 78 || data[3] != 71
                || data[4] != 13 || data[5] != 10 || data[6] != 26 || data[7] != 10
                || data[12] != 73 || data[13] != 72 || data[14] != 68 || data[15] != 82)
                throw new InvalidDataException("Invalid PNG header");
            uint width = ReadBigEndian(data, 16);
            uint height = ReadBigEndian(data, 20);
            if (width == 0 || height == 0 || width > 4096 || height > 4096 || (long)width * height > 16000000)
                throw new InvalidDataException("PNG dimensions exceed 4096 pixels or 16 megapixels");
            using BitmapExternal bitmap = api.Render.BitmapCreateFromPng(data);
            ImageSurface image = GuiElement.getImageSurfaceFromAsset(bitmap);
            if (image.Width > MaxCustomDimension || image.Height > MaxCustomDimension)
            {
                using ImageSurface original = image;
                double scale = Math.Min((double)MaxCustomDimension / original.Width, (double)MaxCustomDimension / original.Height);
                image = new ImageSurface(Format.Argb32, Math.Max(1, (int)Math.Round(original.Width * scale)),
                    Math.Max(1, (int)Math.Round(original.Height * scale)));
                try
                {
                    using Context resize = new(image);
                    resize.Scale((double)image.Width / original.Width, (double)image.Height / original.Height);
                    using SurfacePattern pattern = new(original) { Filter = Filter.Best };
                    resize.SetSource(pattern);
                    resize.Paint();
                }
                catch { image.Dispose(); throw; }
            }
            long imageBytes = (long)image.Width * image.Height * 4;
            while (customSurfaceBytes + imageBytes > MaxCustomSurfaceBytes && customSurfaces.Count > 0)
            {
                string oldest = customLastUse.MinBy(pair => pair.Value).Key;
                ImageSurface evicted = customSurfaces[oldest];
                customSurfaceBytes -= (long)evicted.Width * evicted.Height * 4;
                customSurfaces.Remove(oldest);
                customLastUse.Remove(oldest);
                evicted.Dispose();
            }
            customSurfaces.Add(fileName, image);
            customLastUse[fileName] = ++customUseSequence;
            customSurfaceBytes += imageBytes;
            return image;
        }
        catch (Exception e)
        {
            rejectedCustomFiles.Add(fileName);
            api.Logger.Warning("[FeralKinshipCompanions] Could not load local portrait {0}: {1}", fileName, e.Message);
            return null;
        }
    }

    private static uint ReadBigEndian(byte[] bytes, int index) =>
        ((uint)bytes[index] << 24) | ((uint)bytes[index + 1] << 16) | ((uint)bytes[index + 2] << 8) | bytes[index + 3];

    private static void DrawContained(Context ctx, ImageSurface image, double x, double y, double size)
    {
        ctx.Save();
        ctx.Rectangle(x, y, size, size);
        ctx.Clip();
        double scale = Math.Min(size / image.Width, size / image.Height);
        ctx.Translate(x + (size - image.Width * scale) / 2, y + (size - image.Height * scale) / 2);
        ctx.Scale(scale, scale);
        using SurfacePattern pattern = new(image) { Filter = Filter.Nearest };
        ctx.SetSource(pattern);
        ctx.Paint();
        ctx.Restore();
    }

    public void DrawFace(Context ctx, string? speciesId, string? appearanceCode,
        double x, double y, double size, long entityId = 0, string? foxId = null) => Draw(ctx, speciesId, appearanceCode, false, x, y, size, entityId, foxId);

    public void DrawBody(Context ctx, string? speciesId, string? appearanceCode,
        double x, double y, double size, long entityId = 0, string? foxId = null) => Draw(ctx, speciesId, appearanceCode, true, x, y, size, entityId, foxId);

    private void Draw(Context ctx, string? speciesId, string? appearanceCode,
        bool body, double x, double y, double size, long entityId, string? foxId)
    {
        // The compat portrait has transparent corners. Give it the same charcoal
        // field as the full-body masters instead of leaking the green panel.
        ctx.Save();
        ctx.SetSourceRGB(0.13, 0.13, 0.13);
        ctx.Rectangle(x, y, size, size);
        ctx.Fill();

        string? assigned = AssignedImage(foxId);
        ImageSurface? custom = assigned == null ? null : GetCustomSurface(assigned);
        if (custom != null)
        {
            DrawContained(ctx, custom, x, y, size);
            ctx.Restore();
            return;
        }

        int textureIndex = -1;
        if (speciesId == "wolf" && entityId > 0)
        {
            Entity? entity = api.World.GetEntityById(entityId);
            if (entity?.WatchedAttributes.HasAttribute("textureIndex") == true)
            {
                textureIndex = entity.WatchedAttributes.GetInt("textureIndex");
            }
        }
        string name = ResolveName(speciesId, appearanceCode, textureIndex);
        bool nativeAnimal = name != "compat";
        string path = nativeAnimal
            ? $"animals/fullbody/{name}.png"
            : "animals/faces/compat.png";
        ImageSurface? image = GetSurface(path);
        bool croppedFace = nativeAnimal && !body && image != null;
        image ??= GetSurface("animals/faces/compat.png");
        if (image != null)
        {
            // Native avatars are framed from their full-body master. This keeps
            // every small portrait pixel-identical to the chosen large art.
            (double cropX, double cropY, double cropSize) =
                croppedFace ? FaceCrop(name) : (0, 0, image.Width);
            if (croppedFace)
            {
                double cropScale = image.Width / 1254.0;
                cropX *= cropScale;
                cropY *= cropScale;
                cropSize *= cropScale;
            }
            ctx.Rectangle(x, y, size, size);
            ctx.Clip();
            ctx.Translate(x, y);
            ctx.Scale(size / Math.Max(1, cropSize), size / Math.Max(1, cropSize));
            ctx.Translate(-cropX, -cropY);
            using SurfacePattern pattern = new(image) { Filter = Filter.Nearest };
            ctx.SetSource(pattern);
            ctx.Paint();
        }
        ctx.Restore();
    }

    // Square source rectangles in the approved 1254px full-body masters. The
    // extra room around beaks, long ears, and antlers avoids clipping at 72px.
    private static (double X, double Y, double Size) FaceCrop(string name) => name switch
    {
        "bear-brown" => (145, 315, 485),
        "bear-black" => (145, 315, 485),
        "bear-polar" => (145, 315, 485),
        "chicken-hen" => (225, 255, 455),
        "chicken-rooster" => (220, 235, 480),
        "deer-antlered" => (180, 175, 505),
        "deer-moose" => (150, 250, 570),
        "fox-red" => (155, 250, 490),
        "fox-red-male" => (155, 250, 490),
        "fox-arctic-female" => (155, 250, 490),
        "fox-arctic-male" => (155, 250, 490),
        "gazelle-thomson" => (175, 215, 490),
        "goat-mountain" => (180, 250, 490),
        "hare-brown" => (255, 215, 460),
        "hare-arctic" => (255, 215, 460),
        "hyena-spotted" => (145, 265, 500),
        "pig-eurasian" => (130, 300, 490),
        "raccoon-common" => (155, 330, 475),
        "sheep-bighorn" => (180, 230, 500),
        "wolf-gray" => (140, 265, 510),
        "wolf-charcoal" => (140, 265, 510),
        "wolf-silver" => (140, 265, 510),
        "wolf-brown" => (140, 265, 510),
        "wolf-tan" => (140, 265, 510),
        _ when name.StartsWith("bear-", StringComparison.Ordinal) => (145, 315, 485),
        _ when name.StartsWith("deer-", StringComparison.Ordinal)
            && name.EndsWith("-female", StringComparison.Ordinal) => (180, 255, 505),
        _ when name.StartsWith("deer-", StringComparison.Ordinal) => (180, 175, 505),
        _ when name.StartsWith("goat-", StringComparison.Ordinal) => (180, 250, 490),
        _ when name.StartsWith("hare-", StringComparison.Ordinal) => (255, 215, 460),
        _ when name.StartsWith("pig-", StringComparison.Ordinal) => (130, 300, 490),
        _ when name.StartsWith("sheep-", StringComparison.Ordinal) => (180, 230, 500),
        _ => (0, 0, 1254)
    };

    internal static string ResolveName(string? speciesId, string? appearanceCode, int textureIndex = -1)
    {
        string code = appearanceCode ?? string.Empty;
        return speciesId?.ToLowerInvariant() switch
        {
            "bear" => ResolveNativeTypeName("bear", code, "bear-brown"),
            "chicken" => code.Contains("rooster", StringComparison.OrdinalIgnoreCase)
                ? "chicken-rooster" : "chicken-hen",
            "deer" => ResolveNativeTypeName("deer", code, "deer-antlered"),
            "fox" => code.Contains("-arctic-", StringComparison.OrdinalIgnoreCase)
                ? code.EndsWith("-male", StringComparison.OrdinalIgnoreCase)
                    ? "fox-arctic-male" : "fox-arctic-female"
                : code.EndsWith("-male", StringComparison.OrdinalIgnoreCase)
                    ? "fox-red-male" : "fox-red",
            "gazelle" => "gazelle-thomson",
            "goat" => ResolveNativeTypeName("goat", code, "goat-mountain"),
            "hare" => ResolveNativeTypeName("hare", code, "hare-brown"),
            "hyena" => "hyena-spotted",
            "pig" => ResolveNativeTypeName("pig", code, "pig-eurasian"),
            "raccoon" => "raccoon-common",
            "sheep" => ResolveNativeTypeName("sheep", code, "sheep-bighorn"),
            // The watched textureIndex is the actual server-selected alternate:
            // 0 = adult1 (base), 1 = adult2, ... 9 = adult10.
            "wolf" => textureIndex switch
            {
                0 or 1 => "wolf-charcoal",
                2 or 3 => "wolf-gray",
                4 or 5 or 9 => "wolf-silver",
                6 => "wolf-brown",
                7 or 8 => "wolf-tan",
                _ => "wolf-gray"
            },
            _ => "compat"
        };
    }

    private static string ResolveNativeTypeName(string family, string code, string fallback)
    {
        // Both wild and tamed codes place the native type between the first
        // two dashes: game:deer-chital-adult-female, for example.
        int firstDash = code.IndexOf('-');
        if (firstDash < 0) return fallback;
        int secondDash = code.IndexOf('-', firstDash + 1);
        if (secondDash <= firstDash + 1) return fallback;

        string name = family + "-" + code.Substring(firstDash + 1, secondDash - firstDash - 1);
        if (!NativeTypeNames.Contains(name)) return fallback;

        if (code.EndsWith("-female", StringComparison.OrdinalIgnoreCase)
            && NativeTypeNames.Contains(name + "-female")) return name + "-female";
        return name;
    }

    private ImageSurface? GetSurface(string path)
    {
        if (surfaces.TryGetValue(path, out ImageSurface? cached)) return cached;
        try
        {
            ImageSurface image = GuiElement.getImageSurfaceFromAsset(
                api, new AssetLocation("feralkinshipcompanions", path));
            surfaces.Add(path, image);
            return image;
        }
        catch (Exception exception)
        {
            api.Logger.Warning("[FeralKinshipCompanions] Could not load animal art {0}: {1}",
                path, exception.Message);
            return null;
        }
    }

    public void Dispose()
    {
        RefreshCustomImages();
        foreach (ImageSurface image in surfaces.Values) image.Dispose();
        surfaces.Clear();
    }
}
