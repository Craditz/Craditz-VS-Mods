using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace RustboundExpansion;

internal static class Vfx
{
    internal static int EntityColor(string element) => unchecked((int)(element switch
    {
        "fire" => 0xffff8833u,
        "frost" => 0xff99ddffu,
        "water" => 0xff4499ffu,
        "nature" => 0xff88ee55u,
        "lightning" => 0xffddeeffu,
        "wind" => 0xffccffeeu,
        "earth" => 0xffbb9966u,
        "rust" => 0xffbb5522u,
        "arcane" => 0xffcc88ffu,
        "blood" => 0xffcc1438u,
        _ => 0xffaaaaaau
    }));
    internal static int Color(string element) => element switch
    {
        "blood" => ColorUtil.ToRgba(240,210,20,50),
        "fire" => ColorUtil.ToRgba(255, 255, 100, 25),
        "frost" => ColorUtil.ToRgba(255, 150, 220, 255),
        "nature" => ColorUtil.ToRgba(255, 100, 225, 75),
        "arcane" => ColorUtil.ToRgba(255, 195, 110, 255),
        "rust" => ColorUtil.ToRgba(255, 160, 70, 30),
        "water" => ColorUtil.ToRgba(255, 40, 140, 255),
        "wind" => ColorUtil.ToRgba(255, 210, 255, 230),
        "lightning" => ColorUtil.ToRgba(255, 185, 230, 255),
        _ => ColorUtil.ToRgba(255, 185, 170, 130)
    };
    internal static void Mark(Entity entity, string element) => Burst(entity.World, SpellTargeting.Center(entity), element, 0.2f, 6);
    internal static void Burst(IWorldAccessor world, Vec3d point, string element, float radius, int quantity = 28)
    {
        try
        {
            world.SpawnParticles(new SimpleParticleProperties(quantity, quantity, Color(element), point, point,
            new Vec3f(-radius, -radius * .2f, -radius), new Vec3f(radius, radius, radius), .4f, 0, .2f, .45f, EnumParticleModel.Cube)
            { WithTerrainCollision = false, LightEmission = Color(element), OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -2.5f) });
        }
        catch (Exception e) { world.Logger?.Warning("[RustboundExpansion] Cosmetic burst failed: {0}", e.Message); }
    }
    internal static void Fall(Entity entity, string element) => FallAt(entity.World, SpellTargeting.Center(entity), element);
    internal static void Explosion(IWorldAccessor world, Vec3d point, float radius)
    {
        try
        {
            // Damage is resolved by the spell so its ally rules and secondary
            // burns remain authoritative. This blast supplies fire, smoke,
            // a spherical shock front and sound without terrain destruction.
            world.PlaySoundAt(new AssetLocation(radius >= 5 ? "game:sounds/effect/largeexplosion" : "game:sounds/effect/smallexplosion"), point.X, point.Y, point.Z, null, false, radius * 8, 1);
            world.SpawnParticles(new SimpleParticleProperties(65, 90, Color("fire"), point.AddCopy(-.3, -.3, -.3), point.AddCopy(.3, .3, .3),
                new Vec3f(-radius * 2, -radius, -radius * 2), new Vec3f(radius * 2, radius * 2, radius * 2), .55f, 0, 1.3f, 2.7f, EnumParticleModel.Quad)
                { WithTerrainCollision = false, LightEmission = Color("fire"), SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 2),
                  OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -1.8f) });
            world.SpawnParticles(new SimpleParticleProperties(25, 40, ColorUtil.ToRgba(190, 65, 60, 55), point.AddCopy(-.5, -.3, -.5), point.AddCopy(.5, .5, .5),
                new Vec3f(-radius * .6f, .5f, -radius * .6f), new Vec3f(radius * .6f, 2, radius * .6f), 1.4f, -.05f, 2, 4, EnumParticleModel.Quad)
                { WithTerrainCollision = false, SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 2),
                  OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -.7f) });
            for (int i = 0; i < 32; i++)
            {
                double y = 1 - 2 * (i + .5) / 32, angle = i * 2.3999632297, r = Math.Sqrt(1 - y * y);
                var velocity = new Vec3f((float)(Math.Cos(angle) * r * radius * 2), (float)(y * radius * 2), (float)(Math.Sin(angle) * r * radius * 2));
                world.SpawnParticles(new SimpleParticleProperties(1, 1, ColorUtil.ToRgba(255, 255, 220, 150), point, point,
                    velocity, velocity, .45f, 0, .6f, 1, EnumParticleModel.Quad)
                    { WithTerrainCollision = false, LightEmission = Color("fire"), OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -2.2f) });
            }
        }
        catch (Exception e) { world.Logger?.Warning("[RustboundExpansion] Explosion visual failed: {0}", e.Message); }
    }
    internal static void BlizzardRain(IWorldAccessor world, Vec3d center)
    {
        try
        {
            world.SpawnParticles(new SimpleParticleProperties(80, 110, Color("frost"), center.AddCopy(-8, 6, -8), center.AddCopy(8, 10, 8),
            new Vec3f(-1, -16, -1), new Vec3f(1, -16, 1), .75f, 0, .8f, 1.6f, EnumParticleModel.Cube)
            { WithTerrainCollision = false, LightEmission = Color("frost") });
        }
        catch (Exception e) { world.Logger?.Warning("[RustboundExpansion] Blizzard visual failed: {0}", e.Message); }
    }
    internal static void FallAt(IWorldAccessor world, Vec3d point, string element)
    {
        try
        {
            bool star = element == "arcane";
            world.SpawnParticles(new SimpleParticleProperties(star ? 12 : 8, star ? 16 : 12, Color(element), point.AddCopy(-.25, 7, -.25), point.AddCopy(.25, 9, .25),
            new Vec3f(0, -16, 0), new Vec3f(0, -16, 0), .6f, 0, star ? 1.8f : .7f, star ? 2.8f : 1.2f, EnumParticleModel.Cube)
            { WithTerrainCollision = false, LightEmission = Color(element) });
        }
        catch (Exception e) { world.Logger?.Warning("[RustboundExpansion] Falling visual failed: {0}", e.Message); }
    }
    internal static void Line(IWorldAccessor world, Vec3d from, Vec3d to, string element, int dimension)
    {
        int count = Math.Max(1, (int)Math.Ceiling(from.DistanceTo(to) / 20));
        for (int i = 0; i < count; i++) ArcVisualSystem.Emit(world, from + (to - from) * (i / (double)count), from + (to - from) * ((i + 1) / (double)count),
            dimension, 24);
    }
}
