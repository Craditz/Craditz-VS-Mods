using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace RustboundExpansion;

internal static class HeatedStoneThrows
{
    private const string Popped = "rbx-heated-stone-popped";
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.DeclaredMethod(typeof(EntityThrownItem),nameof(EntityThrownItem.OnGameTick)),
            transpiler:new(typeof(HeatedStoneThrows),nameof(ThrownTick)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(EntityProjectileBase),"DealDamage"),
            transpiler:new(typeof(HeatedStoneThrows),nameof(ProjectileDamage)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(EntityProjectileBase),"ImpactOnEntity"),
            prefix:new(typeof(HeatedStoneThrows),nameof(EntityImpact)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(EntityThrownItem),nameof(EntityThrownItem.OnCollided)),
            prefix:new(typeof(HeatedStoneThrows),nameof(GroundImpact)));
    }
    private static bool TryHeat(Entity? projectile,out float temperature)
    {
        temperature=0;
        return projectile is EntityThrownItem stone && stone.World?.Side==EnumAppSide.Server
            && RockHeating.TryTemperature(stone.World,stone.ProjectileStack,out temperature);
    }
    internal static DamageSource Convert(DamageSource source)
    {
        Prepare(source,0);
        return source;
    }
    private static float Prepare(DamageSource source,float damage)
    {
        // Both callsites receive the original native amount, so permission
        // checking and applying the same source cannot compound the bonus.
        if (TryHeat(source.SourceEntity,out float temperature))
        {
            source.Type=EnumDamageType.Fire;
            if (float.IsFinite(damage) && damage>0) damage+=Math.Min(temperature,RockHeating.SafeCap)/100;
        }
        return damage;
    }
    private static bool ShouldReceive(Entity target,DamageSource source,float damage)
    {float total=Prepare(source,damage);return target.ShouldReceiveDamage(source,total);}
    private static bool Receive(Entity target,DamageSource source,float damage)
    {float total=Prepare(source,damage);return target.ReceiveDamage(source,total);}
    private static void EntityImpact(EntityProjectileBase __instance) {if(__instance.Alive)Pop(__instance);}
    private static bool GroundImpact(EntityThrownItem __instance)
    {
        if (!__instance.Alive || !TryHeat(__instance,out _)) return true;
        Pop(__instance);
        // A heated impact consumes the thrown stone. Skip native terrain
        // bounce/recovery, which otherwise leaves popped stones on the floor.
        __instance.Pos.Motion.Set(0,0,0);
        __instance.Die(EnumDespawnReason.Removed);
        return false;
    }
    internal static void Pop(Entity projectile)
    {
        if (!TryHeat(projectile,out float temperature) || projectile.Attributes.GetBool(Popped)) return;
        projectile.Attributes.SetBool(Popped,true);
        try
        {
            float heat=Math.Clamp((temperature-RockHeating.MinimumThrowTemperature)/(RockHeating.SafeCap-RockHeating.MinimumThrowTemperature),0,1);
            float speed=1+4*heat;
            int quantity=12+(int)MathF.Round(36*heat);
            int color=Vfx.Color("fire");
            // Cosmetic only: no explosion API, nearby-entity query or damage call.
            projectile.World.SpawnParticles(new SimpleParticleProperties(quantity,quantity,color,projectile.Pos.XYZ,projectile.Pos.XYZ,
                new Vec3f(-speed,-speed*.2f,-speed),new Vec3f(speed,speed,speed),.25f+.2f*heat,0,
                .15f+.35f*heat,.25f+.45f*heat,EnumParticleModel.Cube)
                {WithTerrainCollision=false,LightEmission=color,OpacityEvolve=EvolvingNatFloat.create(EnumTransformFunction.LINEAR,-3)});
            projectile.World.PlaySoundAt(new AssetLocation("game:sounds/effect/smallexplosion"),
                projectile.Pos.X,projectile.Pos.Y,projectile.Pos.Z,null,false,10+6*heat,.15f+.3f*heat);
        }
        catch(Exception error) {projectile.World.Logger?.Warning("[RustboundExpansion] Heated stone pop failed: {0}",error.Message);}
    }
    private static IEnumerable<CodeInstruction> ThrownTick(IEnumerable<CodeInstruction> instructions) => Rewrite(instructions,true);
    private static IEnumerable<CodeInstruction> ProjectileDamage(IEnumerable<CodeInstruction> instructions) => Rewrite(instructions,false);
    private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions,bool tick)
    {
        MethodInfo receive=AccessTools.Method(typeof(Entity),nameof(Entity.ReceiveDamage));
        MethodInfo should=AccessTools.Method(typeof(Entity),nameof(Entity.ShouldReceiveDamage));
        MethodInfo sound=AccessTools.Method(typeof(IWorldAccessor),nameof(IWorldAccessor.PlaySoundAt),
            [typeof(AssetLocation),typeof(Entity),typeof(IPlayer),typeof(bool),typeof(float),typeof(float)]);
        int receives=0,checks=0,pops=0;
        foreach(var instruction in instructions)
        {
            if(instruction.Calls(receive))
            {receives++;yield return CodeInstruction.Call(typeof(HeatedStoneThrows),nameof(Receive)).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);}
            else if(tick && instruction.Calls(should))
            {checks++;yield return CodeInstruction.Call(typeof(HeatedStoneThrows),nameof(ShouldReceive)).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);}
            else
            {
                if(tick && instruction.Calls(sound))
                {
                    pops++;
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                    yield return CodeInstruction.Call(typeof(HeatedStoneThrows),nameof(Pop));
                }
                yield return instruction;
            }
        }
        if(receives!=1 || tick && (checks!=1 || pops!=1))
            throw new InvalidOperationException("Unsupported native thrown-stone damage/impact layout.");
    }
}
