using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace RustboundExpansion;

// Keep the native 3D shape and texture coordinates; tint vertex colors instead
// of replacing a useful model with a flat spell icon or duplicating game art.
public abstract class TintedTravelItem : Item
{
    private readonly Dictionary<int, MultiTextureMeshRef> meshes = new();
    protected abstract int ItemTint(ItemStack stack);

    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack stack, EnumItemRenderTarget target, ref ItemRenderInfo info)
    {
        base.OnBeforeRender(capi, stack, target, ref info);
        int tint = ItemTint(stack);
        if (!meshes.TryGetValue(tint, out var mesh) || mesh.Disposed)
        {
            var data = BuildMesh(capi, tint);
            meshes[tint] = mesh = capi.Render.UploadMultiTextureMesh(data);
        }
        info.ModelRef = mesh;
        info.ApplyColor = true;
    }

    protected virtual MeshData BuildMesh(ICoreClientAPI capi, int tint)
    {
        capi.Tesselator.TesselateItem(this, out MeshData data);
        Tint(data, tint);
        return data;
    }

    internal static void Tint(MeshData mesh, int rgb)
    {
        for (int i = 0; i < mesh.VerticesCount; i++)
        {
            mesh.Rgba[i * 4] = (byte)(mesh.Rgba[i * 4] * ((rgb >> 16) & 255) / 255);
            mesh.Rgba[i * 4 + 1] = (byte)(mesh.Rgba[i * 4 + 1] * ((rgb >> 8) & 255) / 255);
            mesh.Rgba[i * 4 + 2] = (byte)(mesh.Rgba[i * 4 + 2] * (rgb & 255) / 255);
        }
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        foreach (var mesh in meshes.Values) mesh.Dispose();
        meshes.Clear();
        base.OnUnloaded(api);
    }
}

internal static class RuneColors
{
    internal const int StoneBrown = 0x60351F;
    internal const int Gold = 0xEDBE51;
    internal static readonly int[] Palette = [0x56CFE1, 0x6A9FFF, 0x78D89B, 0xB295F4, 0xE894D2, 0xED8487,
        0x65C5B5, 0x9DB7ED, 0xB8D978, 0xCA8FC3, 0xEEA48D, 0x82CBDF];
    private static readonly string[] Names = ["Turquoise", "Sapphire", "Emerald", "Violet", "Rose", "Crimson",
        "Jade", "Periwinkle", "Peridot", "Orchid", "Coral", "Aquamarine"];

    internal static int DestinationIndex(double x, double y, double z, int dimension)
    {
        // Random-looking but stable across restarts, clients and identical
        // destinations. Cell coordinates keep nearby casts from flickering.
        ulong hash = 14695981039346656037;
        foreach (long value in new[] { (long)Math.Floor(x), (long)Math.Floor(y), (long)Math.Floor(z), (long)dimension })
            for (int i = 0; i < 8; i++) { hash ^= (byte)((ulong)value >> (i * 8)); hash = unchecked(hash * 1099511628211); }
        return (int)(hash % (ulong)Palette.Length);
    }

    internal static int Index(ItemStack stack)
    {
        var a = stack.Attributes;
        int stored = a.GetInt("rbx-rune-color", -1);
        return stored >= 0 && stored < Palette.Length ? stored
            : DestinationIndex(a.GetDouble("rbx-x"), a.GetDouble("rbx-y"), a.GetDouble("rbx-z"), a.GetInt("rbx-dimension"));
    }
    internal static int Tint(ItemStack stack) => stack.Attributes.GetBool("rbx-marked") ? Palette[Index(stack)] : Gold;
    internal static string Name(ItemStack stack) => stack.Attributes.GetBool("rbx-marked") ? Names[Index(stack)] : "Gold";
}
