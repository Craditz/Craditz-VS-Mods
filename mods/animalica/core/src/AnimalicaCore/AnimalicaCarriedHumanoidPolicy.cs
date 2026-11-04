using System.Collections;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace AnimalicaCore;

/// <summary>Conservative capability check for Core's Seraph carrier poses.</summary>
internal static class AnimalicaCarriedHumanoidPolicy
{
    private sealed record Capability(bool Compatible);
    private static readonly ConditionalWeakTable<Shape, Capability> capabilities = new();

    internal static bool Supports(Entity? entity)
    {
        if (entity == null) return false;
        string code = entity.WatchedAttributes.GetString("skinModel", "seraph");
        if (AnimalicaCarriedFoxSystem.IsModel(code, "seraph")) return true;
        ICoreAPI? api = entity.Api;
        if (entity is not EntityPlayer || api == null) return false;

        // Read PML's resolved registry, not raw configs or the entity's default
        // Seraph shape. That default also exists on incompatible animal players.
        object? models = api.ModLoader.GetModSystem("PlayerModelLib.CustomModelsSystem");
        if (models == null ||
            AccessTools.Property(models.GetType(), "ModelsLoaded")?.GetValue(models) is not true ||
            AccessTools.Property(models.GetType(), "CustomModels")?.GetValue(models) is not IDictionary registry)
            return false;

        object? selected = registry.Contains(code) ? registry[code] : null;
        if (selected == null && !code.Contains(':'))
        {
            foreach (DictionaryEntry entry in registry)
            {
                if (entry.Key is not string key || !key.EndsWith(":" + code, StringComparison.OrdinalIgnoreCase)) continue;
                if (selected != null) return false; // Ambiguous short codes fail closed.
                selected = entry.Value;
            }
        }
        if (selected == null ||
            AccessTools.Property(selected.GetType(), "Enabled")?.GetValue(selected) is not true ||
            AccessTools.Property(selected.GetType(), "Shape")?.GetValue(selected) is not Shape shape)
            return false;

        // PML can omit generated hand attachment metadata from the server shape.
        // Keep intrinsic capability cached separately from side/config inference.
        return capabilities.GetValue(shape, s => new Capability(HasCarryRig(s))).Compatible ||
            SupportsGeneratedServerHands(api, selected, shape);
    }

    private static object? ReadMember(object value, string name) =>
        AccessTools.Property(value.GetType(), name)?.GetValue(value) ??
        AccessTools.Field(value.GetType(), name)?.GetValue(value);

    private static bool SupportsGeneratedServerHands(ICoreAPI api, object model, Shape shape)
    {
        // Qualify the loaded model's hand-generation contract and rig, independent
        // of PML's version string. A raw server shape is not rendered; the client
        // must still pass the strict check with actual hand attachment points.
        if (api.Side != EnumAppSide.Server ||
            ReadMember(model, "CreateCuboidsForAttachmentPoints") is not true ||
            ReadMember(model, "OriginalConfig") is not object config ||
            ReadMember(config, "BaseShapeCode") is not string baseShape ||
            !string.Equals(baseShape, "playermodellib:digitigrade", StringComparison.Ordinal))
            return false;

        return CheckCarryRig(shape, serverGeneratedHands: true);
    }

    internal static bool HasCarryRig(Shape? shape) => CheckCarryRig(shape, serverGeneratedHands: false);

    private static bool CheckCarryRig(Shape? shape, bool serverGeneratedHands)
    {
        if (shape?.Elements == null) return false;
        var bones = new Dictionary<string, ShapeElement>(StringComparer.OrdinalIgnoreCase);
        var parents = new Dictionary<ShapeElement, ShapeElement?>();
        bool duplicate = false;
        void Visit(ShapeElement[]? elements, ShapeElement? parent)
        {
            if (elements == null) return;
            foreach (ShapeElement element in elements)
            {
                if (!parents.TryAdd(element, parent)) { duplicate = true; continue; }
                if (!string.IsNullOrEmpty(element.Name) && !bones.TryAdd(element.Name, element)) duplicate = true;
                Visit(element.Children, element);
            }
        }
        Visit(shape.Elements, null);
        if (duplicate) return false;
        bool Child(string child, string parent) => bones.TryGetValue(child, out var c) &&
            bones.TryGetValue(parent, out var p) && ReferenceEquals(parents[c], p);
        if (!Child("UpperTorso", "LowerTorso") || !Child("Neck", "UpperTorso") || !Child("Head", "Neck")) return false;
        if (!Child("UpperFootL", "LowerTorso") || !Child("UpperFootR", "LowerTorso")) return false;
        bool directFeet = Child("LowerFootL", "UpperFootL") && Child("LowerFootR", "UpperFootR");
        // PML's digitigrade layout adds one named middle segment on both sides.
        // Accept that exact topology, not arbitrary descendants or mixed legs.
        bool digitigradeFeet = Child("MiddleFootL", "UpperFootL") && Child("LowerFootL", "MiddleFootL") &&
            Child("MiddleFootR", "UpperFootR") && Child("LowerFootR", "MiddleFootR");
        if (!directFeet && !digitigradeFeet) return false;
        if (serverGeneratedHands && !digitigradeFeet) return false;
        foreach (string side in new[] { "L", "R" })
        {
            if (!Child("UpperArm" + side, "UpperTorso") || !Child("LowerArm" + side, "UpperArm" + side)) return false;
            string hand = side == "L" ? "LeftHand" : "RightHand";
            ShapeElement lowerArm = bones["LowerArm" + side];
            if (serverGeneratedHands)
            {
                // Match PML's exact missing-cuboid recipe. An existing canonical
                // node must be a direct child of its correct lower arm, with the
                // exact name; otherwise PML would create duplicate/wrong anchors.
                string anchor = side == "L" ? "ItemAnchorL" : "ItemAnchor";
                if (bones.TryGetValue(anchor, out ShapeElement? existing) &&
                    (!string.Equals(existing.Name, anchor, StringComparison.Ordinal) ||
                     !ReferenceEquals(parents[existing], lowerArm))) return false;
            }
            bool HandInArm(ShapeElement element) =>
                element.AttachmentPoints?.Any(p => string.Equals(p.Code, hand, StringComparison.Ordinal)) == true ||
                element.Children?.Any(HandInArm) == true;
            if (!serverGeneratedHands && !HandInArm(lowerArm)) return false;
        }
        return true;
    }

    internal static void Clear() => capabilities.Clear();
}
