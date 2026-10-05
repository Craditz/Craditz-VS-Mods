using HarmonyLib;
using PlayerModelLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace AnimalicaRequests;

public sealed class AntlerSizes : ModSystem
{
    private Harmony? harmony;
    private static readonly string[] LegacyAntlerParts =
        { "animalicarequests-fox-antlers", "animalicarequests-hare-antlers-male", "animalicarequests-hare-antlers-female" };
    public override void StartClientSide(ICoreClientAPI api)
    {
        harmony = new Harmony("animalicarequests.antler-sizes");
        harmony.Patch(AccessTools.Method(typeof(PlayerSkinBehavior), "AddSkinParts"),
            postfix: new HarmonyMethod(typeof(AntlerSizes), nameof(FilterSizes)));
        harmony.Patch(AccessTools.Method(typeof(PlayerSkinBehavior), "GetAppliedSkinParts"),
            prefix: new HarmonyMethod(typeof(AntlerSizes), nameof(MigrateStage)));
    }
    public override void Dispose() { harmony?.UnpatchAll("animalicarequests.antler-sizes"); }

    // AddSkinParts is shared by live entities and the character preview.
    // Filter after all parts, independent of selection/change order. Existing
    // skins with no saved size retain the approved Normal-sized rack.
    private static void FilterSizes(PlayerSkinBehavior __instance, ref Shape entityShape)
    {
        List<ShapeElement> antlers = new();
        CollectAntlers(entityShape.Elements, antlers);
        if (antlers.Count == 0) return;
        var parts = __instance.GetAppliedSkinParts();
        var selected = parts.FirstOrDefault(p =>
            p.PartCode.StartsWith("animalicarequests-") && p.PartCode.Contains("-antlers") && p.PartCode.EndsWith("-size"));
        string size = selected?.Code is "small" or "large" ? selected.Code : "normal";
        var growth = parts.FirstOrDefault(p => p.PartCode.StartsWith("animalicarequests-") && p.PartCode.EndsWith("-growth"));
        int stage = int.TryParse(growth?.Code, out int value) ? Math.Clamp(value, 1, 9) : 1;
        int maximum = MaxStage(antlers.ToArray());
        string prefix = $"AnimalicaRequestsAntlers_{size}_stage{Math.Min(stage, maximum):00}_";
        string[] removed = antlers.Where(element => !element.Name!.StartsWith(prefix, StringComparison.Ordinal))
            .Select(element => element.Name!).ToArray();
        if (removed.Length > 0) entityShape.RemoveElements(removed);
    }

    private static void MigrateStage(ITreeAttribute? ___SkinTree)
    {
        var applied = ___SkinTree?.GetTreeAttribute("appliedParts");
        if (applied == null) return;
        foreach (string code in LegacyAntlerParts)
        {
            string old = applied.GetString(code, "");
            if (old.StartsWith("moose-") && int.TryParse(old[6..], out int stage) && stage is >= 1 and <= 9)
            {
                applied.SetString(code, "moose");
                if (!applied.HasAttribute(code + "-growth")) applied.SetString(code + "-growth", stage.ToString());
            }
        }
    }

    private static int MaxStage(ShapeElement[]? elements)
    {
        int maximum = 1;
        foreach (var e in elements ?? Array.Empty<ShapeElement>())
        {
            if (e.Name?.StartsWith("AnimalicaRequestsAntlers_") == true)
            {
                string[] pieces = e.Name.Split('_');
                if (pieces.Length > 2 && pieces[2].StartsWith("stage") && int.TryParse(pieces[2][5..], out int n)) maximum = Math.Max(maximum, n);
            }
            else maximum = Math.Max(maximum, MaxStage(e.Children));
        }
        return maximum;
    }

    private static void CollectAntlers(ShapeElement[]? elements, List<ShapeElement> antlers)
    {
        if (elements == null) return;
        foreach (var e in elements)
        {
            if (e.Name?.StartsWith("AnimalicaRequestsAntlers_", StringComparison.Ordinal) == true) antlers.Add(e);
            else CollectAntlers(e.Children, antlers);
        }
    }
}
