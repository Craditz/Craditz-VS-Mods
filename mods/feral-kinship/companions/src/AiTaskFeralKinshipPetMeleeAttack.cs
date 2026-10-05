#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// Kinship-owned melee timing and target selection. The historical class name
/// and task id are retained for save/log familiarity, but this no longer
/// inherits from or consults PetAI.
/// </summary>
public sealed class AiTaskFeralKinshipPetMeleeAttack : AiTaskMeleeAttack
{
    private readonly float baseDamage;
    private readonly int baseAttackDurationMs;
    private readonly int baseDamageAtMs;
    private readonly int baseMinCooldownMs;
    private readonly int baseMaxCooldownMs;
    private FeralKinshipCompanionSystem? system;

    public AiTaskFeralKinshipPetMeleeAttack(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        baseDamage = damage;
        baseAttackDurationMs = attackDurationMs;
        baseDamageAtMs = damagePlayerAtMs;
        baseMinCooldownMs = MinCooldownMs;
        baseMaxCooldownMs = MaxCooldownMs;
    }

    public override bool ShouldExecute()
    {
        long now = entity.World.ElapsedMilliseconds;
        if (now - lastCheckOrAttackMs < attackDurationMs
            || cooldownUntilMs > now
            || !FeralKinshipCompanionSystem.CanRunCompanionCombatTask(entity)
            || FeralKinshipCompanionSystem.GetCompanionCombatStyle(entity) == CompanionCombatStyle.Flee)
        {
            return false;
        }

        system ??= entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (!system.TryResolveCompanionCombatTarget(entity, out Entity? resolved)
            || resolved == null
            || !IsWithinMeleeAcquisitionRange(resolved))
        {
            return false;
        }

        targetEntity = resolved;
        ApplyDerivedCombatStats();
        lastCheckOrAttackMs = now;
        damageInflicted = false;
        return true;
    }

    public override void StartExecute()
    {
        ApplyDerivedCombatStats();
        base.StartExecute();
        system ??= entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        system.NotifyFoxAttack(entity);
    }

    public override bool ContinueExecute(float dt)
    {
        if (targetEntity == null
            || !FeralKinshipCompanionSystem.IsValidCompanionCombatTarget(entity, targetEntity)
            || !IsWithinMeleeAcquisitionRange(targetEntity)
            || FeralKinshipCompanionSystem.GetCompanionCombatStyle(entity) is CompanionCombatStyle.Passive or CompanionCombatStyle.Flee
            || FeralKinshipCompanionSystem.GetCompanionActivityMode(entity) == CompanionActivityMode.ReturnHome
            || FeralKinshipCompanionSystem.GetDomesticationStatus(entity)?.GetBool("feralKinshipAutomaticRetreatActive", false) == true)
        {
            return false;
        }
        ApplyDerivedCombatStats();
        return base.ContinueExecute(dt);
    }

    protected override void attackTarget()
    {
        if (targetEntity == null || !HasBodyContact(targetEntity)) return;

        // AiTaskMeleeAttack's final obstruction test also repeats its
        // direction-dependent nose-distance test. Body contact above is the
        // authoritative distance check for companions, so temporarily widen
        // only that redundant part and let the inherited method retain its
        // three-height block ray test and normal damage handling.
        float configuredMinDist = minDist;
        float configuredMinVerDist = minVerDist;
        minDist = configuredMinDist + entity.SelectionBox.XSize + targetEntity.SelectionBox.XSize;
        minVerDist = configuredMinVerDist
            + (entity.SelectionBox.Y2 - entity.SelectionBox.Y1)
            + (targetEntity.SelectionBox.Y2 - targetEntity.SelectionBox.Y1);
        try
        {
            base.attackTarget();
        }
        finally
        {
            minDist = configuredMinDist;
            minVerDist = configuredMinVerDist;
        }
    }

    /// <summary>
    /// Determines whether the attack may begin turning toward its target.
    /// Vanilla's direct-contact check samples a point at the attacker's nose,
    /// so using it before the attack has turned makes wide companions such as
    /// bears and wolves fail the check when they arrive facing slightly away.
    /// The inherited damage step still uses the strict, ray-traced contact
    /// check after the companion has faced the target; this only breaks the
    /// facing/contact deadlock that prevented the attack from starting.
    /// </summary>
    private bool IsWithinMeleeAcquisitionRange(Entity target)
    {
        if (target.Pos.Dimension != entity.Pos.Dimension) return false;
        if (Math.Abs(target.Pos.Y - entity.Pos.Y) > 0.65d) return false;

        double dx = target.Pos.X - entity.Pos.X;
        double dz = target.Pos.Z - entity.Pos.Z;
        double horizontalReach = entity.SelectionBox.XSize * 0.5
            + target.SelectionBox.XSize * 0.5
            + minDist;
        if (dx * dx + dz * dz >= horizontalReach * horizontalReach) return false;

        double ownMiddleY = entity.Pos.Y + (entity.SelectionBox.Y1 + entity.SelectionBox.Y2) * 0.5;
        double targetMiddleY = target.Pos.Y + (target.SelectionBox.Y1 + target.SelectionBox.Y2) * 0.5;
        double verticalReach = (entity.SelectionBox.Y2 - entity.SelectionBox.Y1) * 0.5
            + (target.SelectionBox.Y2 - target.SelectionBox.Y1) * 0.5
            + minVerDist;
        return Math.Abs(targetMiddleY - ownMiddleY) < verticalReach
            && CompanionNavigation.IsClearArrival(
                entity,
                target.Pos.XYZ,
                horizontalReach,
                allowTargetBlock: false);
    }

    private bool HasBodyContact(Entity target)
    {
        if (target.Pos.Dimension != entity.Pos.Dimension) return false;

        Cuboidd ownBox = entity.SelectionBox.ToDouble().Translate(
            entity.Pos.X,
            entity.Pos.Y,
            entity.Pos.Z
        );
        Cuboidd targetBox = target.SelectionBox.ToDouble().Translate(
            target.Pos.X,
            target.Pos.Y,
            target.Pos.Z
        );

        double gapX = Math.Max(0d, Math.Max(targetBox.X1 - ownBox.X2, ownBox.X1 - targetBox.X2));
        double gapZ = Math.Max(0d, Math.Max(targetBox.Z1 - ownBox.Z2, ownBox.Z1 - targetBox.Z2));
        double verticalGap = Math.Max(0d, Math.Max(targetBox.Y1 - ownBox.Y2, ownBox.Y1 - targetBox.Y2));
        double horizontalGap = Math.Sqrt(gapX * gapX + gapZ * gapZ);
        return horizontalGap < minDist && verticalGap < minVerDist;
    }

    private void ApplyDerivedCombatStats()
    {
        damage = baseDamage * FeralKinshipCompanionSystem.GetFoxDamageMultiplier(entity);
        float speed = FeralKinshipCompanionSystem.GetFoxAttackSpeedMultiplier(entity);
        attackDurationMs = Math.Max(100, (int)Math.Round(baseAttackDurationMs / speed));
        damagePlayerAtMs = Math.Clamp(
            (int)Math.Round(baseDamageAtMs / speed),
            50,
            Math.Max(50, attackDurationMs - 50)
        );
        MinCooldownMs = Math.Max(0, (int)Math.Round(baseMinCooldownMs / speed));
        MaxCooldownMs = Math.Max(MinCooldownMs, (int)Math.Round(baseMaxCooldownMs / speed));
    }
}
