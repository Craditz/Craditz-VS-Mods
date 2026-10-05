#nullable enable

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

/// <summary>
/// A stationary area-damage source for testing fox defenses. It damages every
/// living entity in range, including players, once per second.
/// </summary>
public sealed class EntityFeralKinshipAttackDummy : EntityHumanoid
{
    private const float DamageIntervalSeconds = 1f;
    private const float DamageRadius = 2.5f;
    private const float DamageAmount = 1f;
    private float damageTimer;
    private Vec3d? anchorPosition;

    public override bool AlwaysActive
    {
        get => true;
        set { }
    }

    public override bool IsCreature => true;

    public override void Initialize(EntityProperties properties, ICoreAPI api, long inChunkIndex3d)
    {
        base.Initialize(properties, api, inChunkIndex3d);
        RenderColor = ColorUtil.ColorFromRgba(112, 72, 42, 255);
    }

    public override void OnGameTick(float dt)
    {
        base.OnGameTick(dt);

        if (World?.Side != EnumAppSide.Server || !Alive)
        {
            return;
        }

        anchorPosition ??= new Vec3d(Pos.X, Pos.Y, Pos.Z);
        Pos.SetPos(anchorPosition);
        Pos.Motion.Set(0, 0, 0);

        damageTimer += dt;
        if (damageTimer < DamageIntervalSeconds)
        {
            return;
        }

        damageTimer = 0f;
        Entity[] targets = World.GetEntitiesAround(
            Pos.XYZ,
            DamageRadius,
            DamageRadius,
            target => target != this && target.Alive
        );

        foreach (Entity target in targets)
        {
            target.ReceiveDamage(
                new DamageSource
                {
                    Source = EnumDamageSource.Entity,
                    SourceEntity = this,
                    SourcePos = Pos.XYZ,
                    Type = EnumDamageType.BluntAttack,
                    DamageTier = 1,
                    KnockbackStrength = 1f,
                    IgnoreInvFrames = true
                },
                DamageAmount
            );
        }
    }
}
