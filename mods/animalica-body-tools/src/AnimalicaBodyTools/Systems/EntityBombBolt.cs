using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace AnimalicaBodyTools.Systems;

public sealed class EntityBombBolt : EntityProjectile
{
    private bool spent;
    public override bool CanCollect(Entity byEntity) => false;
    protected override bool CanHitTarget(Entity target) => target != FiredBy && base.CanHitTarget(target);
    protected override void ImpactOnEntity(Entity target)
    {
        if (CanHitTarget(target)) Detonate(target);
    }
    protected override void IsColliding(EntityPos pos, double speed) => Detonate(null);

    internal bool MayDamage(Entity target) => CanDealDamage(target);

    private void Detonate(Entity? direct)
    {
        if (spent || !Alive || World.Api is not ICoreServerAPI api) return;
        spent = true;
        try
        {
            string? kind = ProjectileStack?.ItemAttributes?["boltBomb"].AsString();
            if (kind is not ("ore" or "stone" or "scrap") || FiredBy is not EntityPlayer { Player: IServerPlayer player }) return;
            var bomb = World.GetBlock(new AssetLocation("game", "bomb-" + kind));
            if (bomb?.Attributes == null) return;
            var pos = Pos.AsBlockPos;
            int radius = bomb.Attributes["blastRadius"].AsInt(4);
            var area = new Cuboidi(pos.AddCopy(-radius),pos.AddCopy(radius));
            foreach (var claim in api.WorldManager.LandClaims)
            {
                if (claim.Intersects(area) && claim.TestPlayerAccess(player,EnumBlockAccessFlags.BuildOrBreak) == EnumPlayerAccessResult.Denied)
                    return;
            }
            var prev = BoltBlast.Current;
            try
            {
                bool damaged = direct != null && CanDealDamage(direct) && DealDamage(direct);
                BoltBlast.Current = new(this,pos,kind != "ore",damaged ? direct : null);
                if (kind != "ore" && !BoltBlast.FilterInstalled) return;
                ((IServerWorldAccessor)World).CreateExplosion(pos,(EnumBlastType)bomb.Attributes["blastType"].AsInt(),
                    radius,bomb.Attributes["injureRadius"].AsInt(10),1,player.PlayerUID);
            }
            finally { BoltBlast.Current = prev; }
        }
        finally { Die(); }
    }
}

// Keep vanilla exposure, falloff, sounds and particles. Its destruction radius clamps to at least one.
// After the smoke copies the affected blocks, empty only this bolt's block callback and update list.
[HarmonyPatch("Vintagestory.Server.ServerMain", "CreateExplosion")]
internal static class BoltBlast
{
    internal record Scope(EntityBombBolt Bolt, BlockPos Center, bool PreserveTerrain, Entity? DirectHit);
    [ThreadStatic] internal static Scope? Current;
    internal static bool FilterInstalled;
    private static void Filter(Dictionary<BlockPos,Block> blocks)
    {
        if (Current?.PreserveTerrain == true) blocks.Clear();
    }
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        CodeInstruction? previous = null;
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.operand is MethodInfo { Name: "AddBlocks", DeclaringType.Name: "ExplosionSmokeParticles" }
                && previous != null && previous.IsLdloc())
            {
                yield return new CodeInstruction(previous.opcode,previous.operand);
                yield return CodeInstruction.Call(typeof(BoltBlast),nameof(Filter));
                FilterInstalled = true;
            }
            previous = instruction;
        }
    }
}

[HarmonyPatch(typeof(Entity),nameof(Entity.ReceiveDamage))]
internal static class DartAndBoltDamage
{
    private static bool Prefix(Entity __instance, DamageSource damageSource, ref float damage)
    {
        if (__instance.World?.Side != EnumAppSide.Server || damageSource.Type == EnumDamageType.Heal) return true;
        if (damageSource.Source == EnumDamageSource.Explosion && BoltBlast.Current is {} blast)
        {
            if (__instance != blast.Bolt.FiredBy && !blast.Bolt.MayDamage(__instance)) return false;
            if (__instance == blast.DirectHit) damageSource.IgnoreInvFrames = true;
        }
        if (damageSource.GetCauseEntity()?.Attributes.GetTreeAttribute(HuntingEffects.StateKey)?.GetFloat("tranq") > 0)
            damage *= .5f;
        return true;
    }
}
