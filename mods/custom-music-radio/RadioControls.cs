using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CustomMusicRadio;

// Shared by physical hit testing, server state and client model generation.
public static class RadioControls
{
    public const float MaxVolume = 1f;
    public const float VolumeStep = .1f;
    public const float DefaultVolume = .6f;
    public const int NeedlePositions = 12;
    public const float MinListeningRange = 16f;
    public const float MaxListeningRange = 256f;
    public const float DefaultListeningRange = MaxListeningRange;
    public const float ReferenceDistance = 16f;
    public const float CloseCenterDistance = 1.5f;
    public const float CloseBlendEndDistance = 4f;
    public const float RequestTolerance = 4f;
    public static float ClampVolume(float volume) => float.IsFinite(volume) ? Math.Clamp(volume, 0, MaxVolume) : DefaultVolume;
    public static float ClampCap(float cap) => float.IsFinite(cap) ? Math.Clamp((float)Math.Floor(cap), MinListeningRange, MaxListeningRange) : MaxListeningRange;
    public static float ClampListeningRange(float range, float cap = MaxListeningRange) => float.IsFinite(range) ? Math.Clamp(range, MinListeningRange, ClampCap(cap)) : ClampCap(cap);
    public static int ClampNeedle(int position) => Math.Clamp(position, 0, NeedlePositions - 1);
    public static int AdvanceNeedle(int position) => (ClampNeedle(position) + 1) % NeedlePositions;
    // Keep a nearby mono radio centered at the listener; restore its true world position
    // over the next few blocks. Range checks always use the cabinet, not this audio proxy.
    public static Vec3f AudioPosition(Vec3f cabinet, Vec3f listener)
    {
        float dx = cabinet.X - listener.X, dy = cabinet.Y - listener.Y, dz = cabinet.Z - listener.Z;
        float distance = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (distance <= CloseCenterDistance) return listener;
        if (distance >= CloseBlendEndDistance) return cabinet;
        float t = (distance - CloseCenterDistance) / (CloseBlendEndDistance - CloseCenterDistance);
        t = t * t * (3 - 2 * t);
        return new Vec3f(listener.X + dx * t, listener.Y + dy * t, listener.Z + dz * t);
    }
    public static float StepVolume(float volume, int direction) => ClampVolume((float)Math.Round((ClampVolume(volume) + direction * VolumeStep) * 10) / 10);
    // Viewed from the front, lower local X is to the right. Travel left -> right.
    public static float SliderX(float range, float cap = MaxListeningRange) => ClampCap(cap) == MinListeningRange ? 12.5f : 12.5f - 9f * (ClampListeningRange(range, cap) - MinListeningRange) / (ClampCap(cap) - MinListeningRange);
    public static float ListeningRangeAtX(double x, float cap = MaxListeningRange) => double.IsFinite(x) ? ClampListeningRange((float)Math.Round(MinListeningRange + (12.5 - x * 16) / 9 * (ClampCap(cap) - MinListeningRange)), cap) : ClampCap(cap);
    public static Cuboidf ModeButton => new(10.5f / 16, 13f / 16, .65f / 16, 13.5f / 16, 14.5f / 16, 2.15f / 16);
    public static Cuboidf PlaylistButton => new(2.5f / 16, 13f / 16, .65f / 16, 5.5f / 16, 14.5f / 16, 2.15f / 16);
    public static Cuboidf SliderRail => new(2.8f / 16, 2.65f / 16, 1.65f / 16, 13.2f / 16, 3f / 16, 2.4f / 16);
    public static Cuboidf SliderKnob(float range, float cap = MaxListeningRange)
    {
        float x = SliderX(range, cap);
        return new((x - .5f) / 16, 2.1f / 16, .6f / 16, (x + .5f) / 16, 3.6f / 16, 1.8f / 16);
    }
    public static Cuboidf Needle(int position)
    {
        float x = 13f - 10f * ClampNeedle(position) / (NeedlePositions - 1);
        return new((x - .08f) / 16, 9.5f / 16, 1.55f / 16, (x + .08f) / 16, 11.5f / 16, 1.8f / 16);
    }
    public static Cuboidf Rotate(Cuboidf box, string facing)
    {
        Vec3d a = BlockVintageRadio.ToWorld(new Vec3d(box.X1, box.Y1, box.Z1), facing);
        Vec3d b = BlockVintageRadio.ToWorld(new Vec3d(box.X2, box.Y2, box.Z2), facing);
        return new((float)Math.Min(a.X, b.X), box.Y1, (float)Math.Min(a.Z, b.Z),
            (float)Math.Max(a.X, b.X), box.Y2, (float)Math.Max(a.Z, b.Z));
    }
    // Shape is an owned clone. Change actual cuboids, never overlay coplanar quads.
    public static void ApplyVisualState(Shape shape, string facing, float range, int needle, float cap = MaxListeningRange,
        RadioPlaybackMode mode = RadioPlaybackMode.RepeatCurrent)
    {
        foreach (ShapeElement element in shape.Elements)
        {
            if (element.Name == "button-mode" && element.FacesResolved != null)
            {
                // Select an opaque atlas tile on the existing face, in every facing.
                for (int i = 0; i < element.FacesResolved.Length; i++)
                {
                    ShapeElementFace face = element.FacesResolved[i];
                    if (face?.Texture != "mode") continue;
                    float u = (int)RadioPlayback.Sanitize((int)mode) * 64;
                    // Shape.Clone shares face objects; replace this face to isolate radios/templates.
                    element.FacesResolved[i] = new ShapeElementFace
                    {
                        Texture = face.Texture, Uv = new float[] { u, 0, u + 64, 32 },
                        Enabled = face.Enabled, Rotation = face.Rotation, Glow = face.Glow,
                        ReflectiveMode = face.ReflectiveMode, WindMode = face.WindMode, WindData = face.WindData
                    };
                }
            }
            Cuboidf? box = element.Name == "range-knob" ? SliderKnob(range, cap)
                : element.Name == "tuning-needle" ? Needle(needle) : null;
            if (box == null) continue;
            box = Rotate(box, facing);
            element.From = new double[] { box.X1 * 16, box.Y1 * 16, box.Z1 * 16 };
            element.To = new double[] { box.X2 * 16, box.Y2 * 16, box.Z2 * 16 };
        }
    }
    public static string PlacementFacing(double playerX, double playerZ, BlockPos position)
    {
        double dx = playerX - (position.X + .5), dz = playerZ - (position.Z + .5);
        return Math.Abs(dx) > Math.Abs(dz) ? (dx > 0 ? "east" : "west") : (dz > 0 ? "south" : "north");
    }
}
