#nullable enable

using System;
using System.Linq;
using Vintagestory.API.Common;

namespace FeralKinshipCompanions;

public sealed partial class EntityBehaviorFeralKinshipFoxSocial
{
    public override void OnTesselation(
        ref Shape entityShape,
        string shapePathForLogging,
        ref bool shapeIsCloned,
        ref string[] willDeleteElements)
    {
        base.OnTesselation(ref entityShape, shapePathForLogging, ref shapeIsCloned, ref willDeleteElements);
        if (entity.Api.Side != EnumAppSide.Client
            || FeralKinshipCompanionSystem.IsCompanionJuvenile(entity)
            || HasAttachmentPoint(entityShape.Elements, "LeftHand"))
        {
            return;
        }

        if (!shapeIsCloned)
        {
            entityShape = entityShape.Clone();
            shapeIsCloned = true;
        }

        ShapeElement? mouth = FindMouthElement(entityShape.Elements);
        if (mouth == null) return;
        double[] from = mouth.From ?? new[] { 0d, 0d, 0d };
        double[] to = mouth.To ?? new[] { 1d, 1d, 1d };
        AttachmentPoint point = new()
        {
            Code = "LeftHand",
            PosX = (from[0] + to[0]) * 0.5,
            PosY = from[1] + (to[1] - from[1]) * 0.35,
            PosZ = (from[2] + to[2]) * 0.5,
            RotationX = 0,
            RotationY = 0,
            RotationZ = 0,
            ParentElement = mouth
        };
        mouth.AttachmentPoints = (mouth.AttachmentPoints ?? Array.Empty<AttachmentPoint>())
            .Append(point)
            .ToArray();
    }

    private static bool HasAttachmentPoint(ShapeElement[]? elements, string code)
    {
        if (elements == null) return false;
        foreach (ShapeElement element in elements)
        {
            if (element.AttachmentPoints?.Any(point =>
                    string.Equals(point.Code, code, StringComparison.OrdinalIgnoreCase)) == true
                || HasAttachmentPoint(element.Children, code)) return true;
        }
        return false;
    }

    private static ShapeElement? FindMouthElement(ShapeElement[]? elements)
    {
        if (elements == null) return null;
        string[] preferred = { "iteminmouth", "nose", "snout", "muzzle", "beak", "head" };
        foreach (string name in preferred)
        {
            ShapeElement? found = FindElement(elements, name);
            if (found != null) return found;
        }
        return null;
    }

    private static ShapeElement? FindElement(ShapeElement[] elements, string name)
    {
        foreach (ShapeElement element in elements)
        {
            string normalized = (element.Name ?? string.Empty)
                .Replace(" ", string.Empty)
                .Replace("_", string.Empty)
                .ToLowerInvariant();
            if (normalized.Contains(name, StringComparison.Ordinal)) return element;
            ShapeElement? child = element.Children == null ? null : FindElement(element.Children, name);
            if (child != null) return child;
        }
        return null;
    }
}
