#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TamablesCritters;

internal sealed class AiTaskTamablesCrittersAirFollow : AiTaskBase
{
    private const double TeleportDistanceSquared = 64d * 64d;
    private readonly float moveSpeed;
    private readonly float maxDistance;
    private readonly bool allowTeleport;
    private Entity? owner;

    public AiTaskTamablesCrittersAirFollow(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        moveSpeed = taskConfig["movespeed"].AsFloat(0.045f);
        maxDistance = taskConfig["maxDistance"].AsFloat(8f);
        allowTeleport = taskConfig["allowTeleport"].AsBool(true);
    }

    public override bool ShouldExecute()
    {
        if (!IsFollowCommandActive()) return false;
        owner = ResolveOwner();
        if (owner?.Alive != true || owner.Pos.Dimension != entity.Pos.Dimension) return false;
        if (allowTeleport && entity.Pos.SquareDistanceTo(owner.Pos) > TeleportDistanceSquared)
        {
            entity.TeleportTo(new Vec3d(owner.Pos.X, owner.Pos.Y + 2, owner.Pos.Z));
            return false;
        }
        return entity.Pos.SquareDistanceTo(owner.Pos) > maxDistance * maxDistance;
    }

    public override void StartExecute()
    {
        base.StartExecute();
        owner ??= ResolveOwner();
        if (owner != null) StartFlight();
    }

    public override bool CanContinueExecute() => owner?.Alive == true && IsFollowCommandActive();

    public override bool ContinueExecute(float dt)
    {
        owner = ResolveOwner();
        if (owner?.Alive != true || owner.Pos.Dimension != entity.Pos.Dimension) return false;
        if (entity.Pos.SquareDistanceTo(owner.Pos) <= maxDistance * maxDistance) return false;
        pathTraverser.CurrentTarget.Set(owner.Pos.X, owner.Pos.Y + 2, owner.Pos.Z);
        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        pathTraverser.Stop();
        owner = null;
        base.FinishExecute(cancelled);
    }

    private void StartFlight()
    {
        pathTraverser.WalkTowards(
            new Vec3d(owner!.Pos.X, owner.Pos.Y + 2, owner.Pos.Z),
            moveSpeed,
            1f,
            null,
            null
        );
    }

    private Entity? ResolveOwner()
    {
        string uid = entity.WatchedAttributes.GetTreeAttribute("domesticationstatus")?
            .GetString("owner", string.Empty) ?? string.Empty;
        return string.IsNullOrWhiteSpace(uid) ? null : entity.World.PlayerByUid(uid)?.Entity;
    }

    private bool IsFollowCommandActive()
    {
        ITreeAttribute? status = entity.WatchedAttributes.GetTreeAttribute("domesticationstatus");
        return string.Equals(entity.WatchedAttributes.GetString("activeCommand", string.Empty), "followmaster", StringComparison.OrdinalIgnoreCase)
            && (status?.GetFloat("obedience", 0f) ?? 0f) >= 0.6f;
    }
}
