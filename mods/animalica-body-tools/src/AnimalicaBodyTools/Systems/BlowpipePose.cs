using System;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AnimalicaBodyTools.Systems;

// Animal models put the hand socket at the mouth, with different rotations on each rig.
// Keep the socket's animated position and point the pipe along the same ray as the dart.
[HarmonyPatch(typeof(EntityShapeRenderer), "RenderItem")]
internal static class BlowpipePose
{
    private static void Prefix(EntityShapeRenderer __instance, EntityAgent ___eagent, ItemStack stack,
        AttachmentPointAndPose apap, ItemRenderInfo renderInfo, out ModelTransform? __state)
    {
        __state = null;
        if (stack.Collectible.Code is not {Domain:"animalicabodytools",Path:"blowpipe"} ||
            ___eagent is not EntityPlayer player || apap?.AttachPoint is not {} socket || renderInfo?.Transform == null) return;

        Span<float> inv = stackalloc float[16];
        __instance.ModelMat.AsSpan().CopyTo(inv);
        Mat4f.Mul(inv,apap.AnimModelMatrix);
        Mat4f.Invert(inv);
        var pos = player.Pos;
        float flat = MathF.Cos(pos.Pitch);
        float x = -flat*MathF.Sin(pos.Yaw), y = MathF.Sin(pos.Pitch), z = -flat*MathF.Cos(pos.Yaw);
        float dx = inv[0]*x+inv[4]*y+inv[8]*z, dy=inv[1]*x+inv[5]*y+inv[9]*z, dz=inv[2]*x+inv[6]*y+inv[10]*z;
        float length = MathF.Sqrt(dx*dx+dy*dy+dz*dz);
        if (!(length > .0001f) || !float.IsFinite(length)) return;
        dx /= length;
        dy /= length;
        dz /= length;
        __state = renderInfo.Transform;
        var pose = __state.Clone();
        pose.Rotation.Set(-(float)socket.RotationX,MathF.Atan2(-dz,dx)*GameMath.RAD2DEG-(float)socket.RotationY,
            MathF.Asin(Math.Clamp(dy,-1,1))*GameMath.RAD2DEG-(float)socket.RotationZ);
        pose.Translation.X += dx*.35f;
        pose.Translation.Y += dy*.35f;
        pose.Translation.Z += dz*.35f;
        renderInfo.Transform = pose;
    }

    private static void Postfix(ItemRenderInfo renderInfo, ModelTransform? __state)
    {
        if (__state != null) renderInfo.Transform = __state;
    }
}
