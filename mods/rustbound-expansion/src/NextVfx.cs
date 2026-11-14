using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace RustboundExpansion;

internal static class NextVfx
{
    internal static void Party(IWorldAccessor world, Vec3d at, float damage)
    {
        try
        {
            float strength = Math.Clamp(MathF.Log2(1 + Math.Max(0, damage)) / 4, .15f, 1.5f);
            int[] colors = [ColorUtil.ToRgba(255,255,80,120), ColorUtil.ToRgba(255,80,220,255), ColorUtil.ToRgba(255,255,225,65), ColorUtil.ToRgba(255,155,90,255)];
            foreach (int color in colors)
                world.SpawnParticles(new SimpleParticleProperties(3 + strength * 8, 3 + strength * 8, color, at, at,
                    new Vec3f(-2 * strength, 1, -2 * strength), new Vec3f(2 * strength, 3 * strength, 2 * strength),
                    .8f + strength * .4f, .25f, .12f, .25f, EnumParticleModel.Quad)
                { WithTerrainCollision = false, OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -.7f) });
            world.PlaySoundAt(new AssetLocation("rustboundexpansion:sounds/party-fwee.ogg"), at.X, at.Y, at.Z, null,
                .8f + strength * .3f, 16 + strength * 8, Math.Clamp(.2f + strength * .5f, .2f, .9f));
        }
        catch (Exception e) { world.Logger?.Warning("[CrookedCodex] Party effect failed: {0}", e.Message); }
    }
    internal static void Circle(IWorldAccessor world, Vec3d center, int dimension = 0)
    {
        // Real servers send one compact update; clients expand it into particles.
        // CPU fixtures without a core API can still inspect the visual properties.
        if (world.Side == EnumAppSide.Server && world.Api != null)
        { ArcVisualSystem.EmitCircle(world, center, dimension); return; }
        try
        {
            int green = ColorUtil.ToRgba(255, 100, 255, 125), gold = ColorUtil.ToRgba(255, 255, 225, 100);
            // Refresh overlapping, stationary boundary particles independently of
            // healing. The outer ring marks the actual four-block healing radius.
            for (int i = 0; i < 128; i++)
            {
                bool outer = i < 96; int index = outer ? i : i - 96, count = outer ? 96 : 32;
                double angle = index * Math.PI * 2 / count, radius = outer ? 4 : 2.6;
                var at = center.AddCopy(Math.Cos(angle) * radius, .14, Math.Sin(angle) * radius);
                int color = outer ? green : gold;
                world.SpawnParticles(new SimpleParticleProperties(1, 1, color, at, at, new Vec3f(), new Vec3f(),
                    .7f, 0, outer ? 1.2f : .8f, outer ? 1.7f : 1.2f, EnumParticleModel.Quad)
                { WithTerrainCollision = false, LightEmission = color, OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -.8f) });
            }
            world.SpawnParticles(new SimpleParticleProperties(8, 8, gold, center.AddCopy(-2, .18, -2), center.AddCopy(2, .3, 2),
                new Vec3f(-.08f, .3f, -.08f), new Vec3f(.08f, .7f, .08f), .9f, 0, .6f, 1.1f, EnumParticleModel.Quad)
            { WithTerrainCollision = false, LightEmission = gold, OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -.9f) });
        }
        catch (Exception e) { world.Logger?.Warning("[CrookedCodex] Healing circle visual failed: {0}", e.Message); }
    }
}
