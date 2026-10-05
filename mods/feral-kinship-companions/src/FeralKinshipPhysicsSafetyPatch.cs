using System;
using System.Collections.Concurrent;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// Prevents a malformed movement value from entering vanilla controlled
/// physics. A single NaN motion value makes the server repeatedly throw while
/// enumerating tickables, which can make every other animal appear frozen.
/// This guard is limited to entities carrying our Companion social behavior.
/// </summary>
[HarmonyPatch(
    typeof(EntityBehaviorControlledPhysics),
    nameof(EntityBehaviorControlledPhysics.ApplyTests),
    new[] { typeof(EntityPos), typeof(EntityControls), typeof(float), typeof(bool) })]
internal static class FeralKinshipPhysicsSafetyPatch
{
    private static readonly ConcurrentDictionary<long, byte> ReportedRepairs = new();
    private static readonly ConcurrentDictionary<long, Vec3d> LastServerPositions = new();

    private static void Prefix(
        EntityBehaviorControlledPhysics __instance,
        EntityPos pos,
        EntityControls controls)
    {
        Entity? entity = __instance.Entity;
        if (entity?.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>() == null)
        {
            return;
        }

        bool repaired = false;
        if (!IsFinite(pos.Motion))
        {
            pos.Motion.Set(0, 0, 0);
            repaired = true;
        }

        if (!IsFinite(controls.WalkVector))
        {
            controls.WalkVector.Set(0, 0, 0);
            repaired = true;
        }

        if (!IsFinite(controls.FlyVector))
        {
            controls.FlyVector.Set(0, 0, 0);
            repaired = true;
        }

        if (repaired && ReportedRepairs.TryAdd(entity.EntityId, 0))
        {
            entity.World.Logger.Warning(
                "[FeralKinshipCompanions] Repaired invalid physics input for companion {0} ({1}); movement was reset for this tick.",
                entity.EntityId,
                entity.Code
            );
        }

        FeralKinshipCompanionSystem system = entity.Api.ModLoader
            .GetModSystem<FeralKinshipCompanionSystem>();
        if (!system.DiagnosticLoggingEnabled)
        {
            return;
        }

        BlockPos cell = pos.AsBlockPos;
        Vec3d currentPosition = pos.XYZ.Clone();
        Vec3d? previousPosition = LastServerPositions.TryGetValue(entity.EntityId, out Vec3d? previous)
            ? previous.Clone()
            : null;
        LastServerPositions[entity.EntityId] = currentPosition;
        bool loaded = entity.World is IServerWorldAccessor serverWorld
            && serverWorld.IsFullyLoadedChunk(cell);
        bool intersects = loaded
            && entity.World.CollisionTester.IsColliding(
                entity.World.BlockAccessor, entity.CollisionBox, pos.XYZ, false);
        if (intersects || !loaded)
        {
            AiTaskManager? manager = entity.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager;
            string tasks = string.Join(
                "|",
                manager?.ActiveTasksBySlot
                    .Where(task => task != null)
                    .Select(task => task!.GetType().Name)
                ?? Enumerable.Empty<string>());
            FeralKinshipCompanionSystem.TryGetCompanionSpecies(entity, out CompanionSpeciesProfile species);
            system.LogCompanionPhysicsDiagnostic(
                entity,
                "physics-overlap",
                $"species={species.Id} activity={FeralKinshipCompanionSystem.GetCompanionActivityMode(entity)} "
                + $"tasks={tasks} loaded={loaded} intersects={intersects} "
                + $"previous={previousPosition?.ToString() ?? "none"} "
                + $"current={currentPosition} "
                + $"motion={pos.Motion.X:0.000},{pos.Motion.Y:0.000},{pos.Motion.Z:0.000} "
                + $"chunk={cell.X >> 4},{cell.Z >> 4}");
        }
    }

    private static bool IsFinite(Vec3d value)
    {
        return IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
