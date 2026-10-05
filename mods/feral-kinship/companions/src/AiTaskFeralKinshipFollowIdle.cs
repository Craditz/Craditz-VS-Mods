#nullable enable

using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

/// <summary>Short local walks and look-around pauses inside a stationary owner's follow ring.</summary>
public sealed class AiTaskFeralKinshipFollowIdle : AiTaskBase
{
    private readonly int minimumDurationMs;
    private readonly int maximumDurationMs;
    private readonly CompanionNavigation navigation;
    private readonly Vec3d ownerAnchor = new();
    private readonly Vec3d observedOwner = new();
    private Vec3d? destination;
    private long idleUntilMs;
    private long ownerStillSinceMs;
    private long nextGroundCheckMs;
    private long nextLookMs;
    private bool observed;
    private bool moving;
    private bool sitAtOwner;
    private bool lieAtOwner;
    private bool RestAtOwner => sitAtOwner || lieAtOwner;
    private Vec3d[]? paceSpots;
    private CompanionPacingSequence? paceSequence;
    private bool startReturnLeg;
    private bool pacing;
    private long nextAttentionMs;
    private int lookTurns;
    private string activeAnimation = string.Empty;

    public AiTaskFeralKinshipFollowIdle(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        minimumDurationMs = taskConfig["minduration"].AsInt(4500);
        maximumDurationMs = Math.Max(minimumDurationMs, taskConfig["maxduration"].AsInt(6500));
        navigation = new CompanionNavigation(entity, pathTraverser);
    }

    protected override void SetDefaultValues()
    {
        base.SetDefaultValues();
        Priority = 1.21f;
        priorityForCancel = 1.21f;
        ExecutionChance = 1f;
        MinCooldownMs = 4000;
        MaxCooldownMs = 9000;
    }

    public override bool ShouldExecute()
    {
        Entity? owner = ResolveOwner();
        if (!CanIdle(owner)) { observed = false; return false; }
        long now = entity.World.ElapsedMilliseconds;
        if (!observed || observedOwner.SquareDistanceTo(owner!.Pos.XYZ) > 0.15 * 0.15)
        {
            observedOwner.Set(owner!.Pos.X, owner.Pos.Y, owner.Pos.Z);
            ownerStillSinceMs = now;
            observed = true;
        }
        return now - ownerStillSinceMs >= 1500 && !IsOnCooldown() && now >= idleUntilMs;
    }

    public override void StartExecute()
    {
        Entity? owner = ResolveOwner();
        long now = entity.World.ElapsedMilliseconds;
        idleUntilMs = now + minimumDurationMs + rand.Next(Math.Max(1, maximumDurationMs - minimumDurationMs + 1));
        destination = null;
        moving = false;
        sitAtOwner = false;
        lieAtOwner = false;
        paceSpots = null;
        paceSequence = null;
        startReturnLeg = false;
        pacing = false;
        lookTurns = 0;
        nextLookMs = now + 400;
        if (owner != null)
        {
            ownerAnchor.Set(owner.Pos.X, owner.Pos.Y, owner.Pos.Z);
            int choice = rand.Next(5);
            if (now >= nextAttentionMs && choice <= 1)
            {
                bool lying = choice == 1;
                string pose = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, lying ? "lie" : "sit");
                bool hasPose = entity.Properties.Client.Animations?.Any(animation =>
                    string.Equals(animation.Code, pose, StringComparison.OrdinalIgnoreCase)) == true;
                if (hasPose)
                {
                    nextAttentionMs = now + 45000 + rand.Next(30000);
                    destination = lying ? ChooseCloseRestDestination(owner) : ChooseAttentionDestination(owner);
                    lieAtOwner = lying && destination != null;
                    sitAtOwner = !lying && destination != null;
                    if (RestAtOwner) idleUntilMs = now + 12000;
                }
            }
            // Pacing is a rarer 5% choice; ordinary walks still fill the remaining pauses.
            if (destination == null && choice == 2 && rand.Next(4) == 0)
            {
                paceSpots = ChoosePacingSpots(owner);
                if (paceSpots != null)
                {
                    pacing = true;
                    paceSequence = new CompanionPacingSequence(rand.Next(2) == 0 ? 4 : 6);
                    destination = paceSpots[0];
                    idleUntilMs = now + 25000;
                }
            }
            if (destination == null && choice != 4) destination = ChooseDestination(owner);
        }
        moving = destination != null;
        activeAnimation = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, moving ? "walk" : "idle");
        animMeta = ResolveAnimation(activeAnimation);
        if (pacing) animMeta.AnimationSpeed *= 2f / 3f;
        base.StartExecute();
        nextGroundCheckMs = now;
        if (destination != null)
        {
            navigation.Start(destination, pacing ? 0.012f : 0.018f, 0.35f, Arrived, Failed,
                600, CompanionNavigationTargetKind.Position, 1, exactTargetOnly: true);
        }
        else EmitWaitingLine(false);
    }

    public override bool CanContinueExecute() => true;

    public override bool ContinueExecute(float dt)
    {
        Entity? owner = ResolveOwner();
        long now = entity.World.ElapsedMilliseconds;
        if (!CanIdle(owner) || now >= idleUntilMs
            || ownerAnchor.SquareDistanceTo(owner!.Pos.XYZ) > 0.35 * 0.35) return false;

        if (moving)
        {
            if (destination == null || !InsideRing(destination, owner!, 0.4)) return false;
            if (now >= nextGroundCheckMs)
            {
                nextGroundCheckMs = now + 200;
                if (!ClearSupportedCorridor(entity.Pos.XYZ, destination, RestAtOwner || pacing ? 9.5 : 3.2)) return false;
                if ((lieAtOwner || pacing) && !ClearOfOwnerAlongApproach(entity.Pos.XYZ, destination, owner!)) return false;
            }
            if (startReturnLeg)
            {
                startReturnLeg = false;
                navigation.Start(destination, pacing ? 0.012f : 0.018f, 0.35f, Arrived, Failed,
                    600, CompanionNavigationTargetKind.Position, 1, exactTargetOnly: true);
            }
            else navigation.Tick();
            return true;
        }

        if (RestAtOwner)
        {
            FaceOwner(owner!);
            return true;
        }
        if (now >= nextLookMs)
        {
            // Existing pack look-around behavior, bounded to two visible turns per pause.
            if (lookTurns < 2) entity.Pos.Yaw += lookTurns++ == 0 ? 0.85f : -1.7f;
            nextLookMs = now + 1100;
        }
        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        navigation.Cancel();
        moving = false;
        destination = null;
        paceSpots = null;
        paceSequence = null;
        startReturnLeg = false;
        observed = false;
        idleUntilMs = entity.World.ElapsedMilliseconds;
        base.FinishExecute(cancelled);
    }

    private void Arrived()
    {
        Entity? owner = ResolveOwner();
        if (!CanIdle(owner) || ownerAnchor.SquareDistanceTo(owner!.Pos.XYZ) > 0.35 * 0.35
            || destination == null || entity.Pos.XYZ.SquareDistanceTo(destination) > 0.65 * 0.65)
        {
            Failed();
            return;
        }
        if (pacing && paceSpots != null && paceSequence?.NextEndpointAfterArrival() is int next)
        {
            destination = paceSpots[next];
            startReturnLeg = true;
            nextGroundCheckMs = 0;
            idleUntilMs = entity.World.ElapsedMilliseconds + 25000;
            return;
        }
        if (RestAtOwner) idleUntilMs = entity.World.ElapsedMilliseconds
            + (lieAtOwner ? 30000 + rand.Next(30001) : 5000 + rand.Next(3000));
        else if (pacing) idleUntilMs = entity.World.ElapsedMilliseconds + 2500;
        BeginLooking();
        if (RestAtOwner || pacing) FaceOwner(owner!);
        EmitWaitingLine(sitAtOwner);
    }

    private void Failed()
    {
        sitAtOwner = false;
        lieAtOwner = false;
        pacing = false;
        paceSpots = null;
        paceSequence = null;
        startReturnLeg = false;
        idleUntilMs = entity.World.ElapsedMilliseconds + 2500;
        BeginLooking();
    }

    private void FaceOwner(Entity owner)
    {
        double dx = owner.Pos.X - entity.Pos.X, dz = owner.Pos.Z - entity.Pos.Z;
        if (dx * dx + dz * dz > 0.01) entity.Pos.Yaw = (float)Math.Atan2(dx, dz);
    }

    private void EmitWaitingLine(bool attention) => entity.Api.ModLoader
        .GetModSystem<FeralKinshipCompanionSystem>().TryEmitFollowIdleDialogue(entity, attention, lieAtOwner, pacing);

    private void BeginLooking()
    {
        moving = false;
        entity.AnimManager?.StopAnimation(activeAnimation);
        activeAnimation = FeralKinshipCompanionSystem.ResolveCompanionAnimation(entity, lieAtOwner ? "lie" : sitAtOwner ? "sit" : "idle");
        animMeta = ResolveAnimation(activeAnimation);
        entity.AnimManager?.StartAnimation(animMeta);
        nextLookMs = entity.World.ElapsedMilliseconds + 200;
    }

    private Vec3d? ChooseDestination(Entity owner)
    {
        CompanionFollowDistance.GetRing(FeralKinshipCompanionSystem.GetCompanionFollowDistance(entity), out _, out float radius);
        Vec3d start = entity.Pos.XYZ;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            double angle = rand.NextDouble() * Math.PI * 2;
            double distance = 1.0 + rand.NextDouble();
            Vec3d candidate = start.AddCopy(Math.Sin(angle) * distance, 0, Math.Cos(angle) * distance);
            if (CompanionFollowIdleBounds.Candidate(candidate.X, candidate.Z, start.X, start.Z,
                    owner.Pos.X, owner.Pos.Z, radius) && ClearSupportedCorridor(start, candidate)) return candidate;
        }
        return null;
    }

    private Vec3d[]? ChoosePacingSpots(Entity owner)
    {
        Vec3d start = entity.Pos.XYZ;
        CompanionFollowDistance.GetRing(FeralKinshipCompanionSystem.GetCompanionFollowDistance(entity), out _, out float radius);
        double toward = Math.Atan2(start.X - owner.Pos.X, start.Z - owner.Pos.Z);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            double angle = toward + (attempt == 0 ? 0 : (rand.NextDouble() - 0.5) * Math.PI);
            double centerRadius = Math.Min(2.5, radius * 0.45);
            double halfLength = 1.5 + rand.NextDouble();
            double cx = owner.Pos.X + Math.Sin(angle) * centerRadius;
            double cz = owner.Pos.Z + Math.Cos(angle) * centerRadius;
            double dx = Math.Cos(angle) * halfLength, dz = -Math.Sin(angle) * halfLength;
            Vec3d a = new(cx + dx, start.Y, cz + dz);
            Vec3d b = new(cx - dx, start.Y, cz - dz);
            if (!InsideRing(a, owner, 0.65) || !InsideRing(b, owner, 0.65)) continue;
            if (start.SquareDistanceTo(b) < start.SquareDistanceTo(a)) (a, b) = (b, a);
            if (ClearSupportedCorridor(start, a, 9.5) && ClearSupportedCorridor(a, b, 5.1)
                && ClearOfOwnerAlongApproach(start, a, owner) && ClearOfOwnerAlongApproach(a, b, owner))
                return new[] { a, b };
        }
        return null;
    }

    private Vec3d? ChooseAttentionDestination(Entity owner)
    {
        Vec3d start = entity.Pos.XYZ;
        CompanionFollowDistance.GetRing(FeralKinshipCompanionSystem.GetCompanionFollowDistance(entity), out _, out float radius);
        double angle = Math.Atan2(start.X - owner.Pos.X, start.Z - owner.Pos.Z);
        // Approach from the companion's side first, avoiding the player's collision box.
        foreach (double offset in new[] { 0d, 0.5d, -0.5d, 1d, -1d })
        {
            Vec3d candidate = new(owner.Pos.X + Math.Sin(angle + offset) * 1.8,
                start.Y, owner.Pos.Z + Math.Cos(angle + offset) * 1.8);
            if (CompanionFollowIdleBounds.AttentionCandidate(candidate.X, candidate.Z, start.X, start.Z,
                    owner.Pos.X, owner.Pos.Z, radius)
                && entity.World.GetEntitiesAround(candidate, 0.9f, 1.5f,
                    other => other.EntityId != entity.EntityId && other.Alive && other is EntityAgent).Length == 0
                && ClearSupportedCorridor(start, candidate, 9.5)) return candidate;
        }
        return null;
    }

    private Vec3d? ChooseCloseRestDestination(Entity owner)
    {
        Vec3d start = entity.Pos.XYZ;
        double toward = Math.Atan2(start.X - owner.Pos.X, start.Z - owner.Pos.Z);
        // Search by increasing clearance, testing all directions before moving farther out.
        var candidates = new System.Collections.Generic.List<Vec3d>();
        for (int ring = 0; ring < 5; ring++)
        for (int i = 0; i < 16; i++)
        {
            double angle = toward + i * Math.PI / 8;
            double dx = Math.Sin(angle), dz = Math.Cos(angle);
            double radius = CompanionFollowIdleBounds.RestSpacing(dx, dz,
                Math.Max(Math.Abs(entity.CollisionBox.MinX), Math.Abs(entity.CollisionBox.MaxX))
                    + Math.Max(Math.Abs(owner.CollisionBox.MinX), Math.Abs(owner.CollisionBox.MaxX)),
                Math.Max(Math.Abs(entity.CollisionBox.MinZ), Math.Abs(entity.CollisionBox.MaxZ))
                    + Math.Max(Math.Abs(owner.CollisionBox.MinZ), Math.Abs(owner.CollisionBox.MaxZ))) + ring * 0.2;
            candidates.Add(new Vec3d(owner.Pos.X + dx * radius, start.Y, owner.Pos.Z + dz * radius));
        }
        foreach (Vec3d candidate in candidates.OrderBy(point => point.SquareDistanceTo(owner.Pos.XYZ)))
        {
            if (!InsideRing(candidate, owner, 0.65) || start.DistanceTo(candidate) > 9.5) continue;
            if (entity.World.GetEntitiesAround(candidate, 3f, 2f,
                other => other.EntityId != entity.EntityId && other.Alive && other is EntityAgent)
                .Any(other => OverlapsAt(candidate, other))) continue;
            if (ClearSupportedCorridor(start, candidate, 9.5)
                && ClearOfOwnerAlongApproach(start, candidate, owner)) return candidate;
        }
        return null;
    }

    private bool OverlapsAt(Vec3d point, Entity other) =>
        point.X + entity.CollisionBox.MaxX + 0.05 > other.Pos.X + other.CollisionBox.MinX
        && point.X + entity.CollisionBox.MinX - 0.05 < other.Pos.X + other.CollisionBox.MaxX
        && point.Z + entity.CollisionBox.MaxZ + 0.05 > other.Pos.Z + other.CollisionBox.MinZ
        && point.Z + entity.CollisionBox.MinZ - 0.05 < other.Pos.Z + other.CollisionBox.MaxZ
        && point.Y + entity.CollisionBox.MaxY > other.Pos.Y + other.CollisionBox.MinY
        && point.Y + entity.CollisionBox.MinY < other.Pos.Y + other.CollisionBox.MaxY;

    private bool ClearOfOwnerAlongApproach(Vec3d start, Vec3d end, Entity owner)
    {
        int count = Math.Max(1, (int)Math.Ceiling(start.DistanceTo(end) / 0.2));
        if (count > 48) return false;
        for (int i = 0; i <= count; i++)
        {
            double t = (double)i / count;
            if (OverlapsAt(new Vec3d(start.X + (end.X - start.X) * t,
                start.Y + (end.Y - start.Y) * t, start.Z + (end.Z - start.Z) * t), owner)) return false;
        }
        return true;
    }

    private bool ClearSupportedCorridor(Vec3d start, Vec3d end, double maxDistance = 3.2)
    {
        // Small level-ground strolls only: no ledge, water, obstacle or elevation shortcut.
        int samples = Math.Max(1, (int)Math.Ceiling(start.DistanceTo(end) / 0.2));
        if (samples > Math.Ceiling(maxDistance / 0.2)) return false;
        for (int i = 0; i <= samples; i++)
        {
            double t = (double)i / samples;
            Vec3d point = new(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t,
                start.Z + (end.Z - start.Z) * t);
            if (!navigation.IsSafeStandingPosition(point)) return false;
        }
        return true;
    }

    private bool CanIdle(Entity? owner) => owner?.Alive == true
        && owner.Pos.Dimension == entity.Pos.Dimension
        && entity.Alive && entity.MountedOn == null && !entity.Swimming && !entity.FeetInLiquid
        && (entity.RightHandItemSlot == null || entity.RightHandItemSlot.Empty)
        && FeralKinshipCompanionSystem.GetCompanionActivityMode(entity) == CompanionActivityMode.Follow
        && FeralKinshipCompanionSystem.CanRunCompanionCommandTask(entity)
        && !FeralKinshipCompanionSystem.ShouldPauseAmbientCompanionAi(entity, this)
        && Math.Abs(entity.Pos.Y - owner.Pos.Y) <= 1.5
        && InsideRing(entity.Pos.XYZ, owner, 0.1);

    private bool InsideRing(Vec3d point, Entity owner, double margin)
    {
        CompanionFollowDistance.GetRing(FeralKinshipCompanionSystem.GetCompanionFollowDistance(entity), out _, out float radius);
        return CompanionFollowIdleBounds.Inside(point.X, point.Z, owner.Pos.X, owner.Pos.Z, radius - margin);
    }

    private Entity? ResolveOwner()
    {
        string uid = FeralKinshipCompanionSystem.GetCompanionOwnerUid(entity);
        return string.IsNullOrWhiteSpace(uid) ? null : entity.World.PlayerByUid(uid)?.Entity;
    }

    private AnimationMetaData ResolveAnimation(string code)
    {
        AnimationMetaData? configured = entity.Properties.Client.Animations?
            .FirstOrDefault(animation => string.Equals(animation.Code, code, StringComparison.OrdinalIgnoreCase));
        return configured?.Clone() ?? new AnimationMetaData
        {
            Code = code, Animation = code, AnimationSpeed = 1f, EaseInSpeed = 1f, EaseOutSpeed = 1f
        }.Init();
    }
}
