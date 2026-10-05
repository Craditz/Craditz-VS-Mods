#nullable enable

using System;
using PetAI;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace TamablesCritters;

internal sealed class AiTaskTamablesCrittersBasicPetSeek : AiTaskPetSeekEntity
{
    public AiTaskTamablesCrittersBasicPetSeek(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
    }

    public override bool ShouldExecute()
    {
        bool shouldExecute = base.ShouldExecute();
        if (!shouldExecute || !IsAllowedTarget(targetEntity))
        {
            targetEntity = null;
            return false;
        }

        return true;
    }

    public override bool ContinueExecute(float dt)
    {
        return IsAllowedTarget(targetEntity) && base.ContinueExecute(dt);
    }

    public override bool IsTargetableEntity(Entity candidate, float range)
    {
        return IsAllowedTarget(candidate)
            && base.IsTargetableEntity(candidate, range);
    }

    private bool IsAllowedTarget(Entity? candidate)
    {
        return TamablesCrittersPetTargetRules.IsAllowed(candidate);
    }
}

internal sealed class AiTaskTamablesCrittersBasicPetMelee : AiTaskPetMeleeAttack
{
    public AiTaskTamablesCrittersBasicPetMelee(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
    }

    public override bool ShouldExecute()
    {
        long now = entity.World.ElapsedMilliseconds;
        if (now - lastCheckOrAttackMs < attackDurationMs
            || cooldownUntilMs > now
            || !PreconditionsSatisfied()) return false;

        EntityBehaviorReceiveCommand? commands = entity.GetBehavior<EntityBehaviorReceiveCommand>();
        EnumAggressionLevel aggression = commands?.AggressionLevel ?? EnumAggressionLevel.NEUTRAL;
        if (aggression == EnumAggressionLevel.PASSIVE) return false;

        Entity? resolved = null;
        EntityBehaviorGiveCommand? ownerCombat = entity.GetBehavior<EntityBehaviorTameable>()?
            .CachedOwner?.Entity?.GetBehavior<EntityBehaviorGiveCommand>();

        if (aggression is EnumAggressionLevel.PROTECTIVE or EnumAggressionLevel.AGGRESSIVE)
        {
            Entity? ownerAttacker = ownerCombat?.Attacker;
            Entity? ownerVictim = ownerCombat?.Victim;
            if (IsAvailableContact(ownerAttacker)) resolved = ownerAttacker;
            if (IsAvailableContact(ownerVictim)) resolved = ownerVictim;
        }

        if (IsAvailableContact(attackedByEntity)) resolved = attackedByEntity;

        if (resolved == null && aggression == EnumAggressionLevel.AGGRESSIVE)
        {
            Vec3d searchOrigin = entity.Pos.XYZ
                .Add(0, entity.SelectionBox.Y2 / 2, 0)
                .Ahead(entity.SelectionBox.XSize / 2, 0, entity.Pos.Yaw);
            resolved = entity.World.GetNearestEntity(
                searchOrigin,
                attackRange,
                attackRange,
                candidate => IsTargetableEntity(candidate, 15) && IsWithinMeleeAcquisitionRange(candidate)
            );
        }

        targetEntity = resolved;
        lastCheckOrAttackMs = now;
        damageInflicted = false;
        return targetEntity != null;
    }

    public override bool ContinueExecute(float dt)
    {
        return IsAllowedTarget(targetEntity) && base.ContinueExecute(dt);
    }

    public override bool IsTargetableEntity(Entity candidate, float range)
    {
        return IsAllowedTarget(candidate)
            && base.IsTargetableEntity(candidate, range);
    }

    protected override void attackTarget()
    {
        if (!IsAllowedTarget(targetEntity)
            || targetEntity == null
            || !HasBodyContact(targetEntity)) return;

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

    private bool HasBodyContact(Entity target)
    {
        if (target.Pos.Dimension != entity.Pos.Dimension) return false;

        Cuboidd ownBox = entity.SelectionBox.ToDouble().Translate(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
        Cuboidd targetBox = target.SelectionBox.ToDouble().Translate(target.Pos.X, target.Pos.Y, target.Pos.Z);
        double gapX = Math.Max(0d, Math.Max(targetBox.X1 - ownBox.X2, ownBox.X1 - targetBox.X2));
        double gapZ = Math.Max(0d, Math.Max(targetBox.Z1 - ownBox.Z2, ownBox.Z1 - targetBox.Z2));
        double verticalGap = Math.Max(0d, Math.Max(targetBox.Y1 - ownBox.Y2, ownBox.Y1 - targetBox.Y2));
        return Math.Sqrt(gapX * gapX + gapZ * gapZ) < minDist && verticalGap < minVerDist;
    }

    private bool IsWithinMeleeAcquisitionRange(Entity target)
    {
        if (target.Pos.Dimension != entity.Pos.Dimension) return false;

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
        return Math.Abs(targetMiddleY - ownMiddleY) < verticalReach;
    }

    private bool IsAvailableContact(Entity? candidate)
    {
        return IsAllowedTarget(candidate)
            && candidate != null
            && candidate.Pos.Dimension == entity.Pos.Dimension
            && IsWithinMeleeAcquisitionRange(candidate);
    }

    private bool IsAllowedTarget(Entity? candidate)
    {
        return TamablesCrittersPetTargetRules.IsAllowed(candidate);
    }
}

internal static class TamablesCrittersPetTargetRules
{
    public static bool IsAllowed(Entity? candidate)
    {
        if (candidate?.Alive != true || candidate is EntityPlayer) return false;

        string domesticationLevel = candidate.WatchedAttributes
            .GetTreeAttribute("domesticationstatus")?
            .GetString("domesticationLevel", string.Empty) ?? string.Empty;
        return string.IsNullOrWhiteSpace(domesticationLevel)
            || string.Equals(domesticationLevel, "WILD", StringComparison.OrdinalIgnoreCase);
    }
}
