#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    internal const string FotsaPregnancyManagedKey = "feralKinshipPregnancyManaged";
    internal const string FotsaPregnancyDueDaysKey = "feralKinshipPregnancyDueDays";

    internal static void SetFotsaBreedingFeedback(Entity entity, ITreeAttribute status, string feedback)
    {
        if (status.GetString(BreedingFeedbackKey) == feedback) return;
        status.SetString(BreedingFeedbackKey, feedback);
        if (feedback == "pregnancy-conflict")
            entity.World.Logger.Warning("[FeralKinshipCompanions] Paused conflicting saved native/custom pregnancies for {0} (entity {1}); both records retained.", entity.Code, entity.EntityId);
        MarkSocialStateDirty(entity);
    }

    internal bool ReconcileFotsaPregnancy(Entity entity, CompanionBreedingDefinition definition)
    {
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        ITreeAttribute? native = entity.WatchedAttributes.GetTreeAttribute("multiply");
        bool customActive = status.GetBool(PregnancyActiveKey);
        bool managed = native?.GetBool(FotsaPregnancyManagedKey) == true;
        // Retain the due-date tombstone after completion: it also prevents an
        // older ledger snapshot from reviving a pregnancy after native birth.
        if (managed && customActive
            && native!.GetDouble("totalDaysLastBirth", -9999) >= native.GetDouble(FotsaPregnancyDueDaysKey, double.MaxValue)
            && status.GetDouble(PregnancyDueTotalHoursKey, double.MaxValue) / entity.World.Calendar.HoursPerDay
                <= native.GetDouble(FotsaPregnancyDueDaysKey, -9999))
        {
            status.SetBool(PregnancyActiveKey, false);
            status.SetBool(PendingBirthKey, false);
            status.SetDouble(PregnancyStartTotalHoursKey, 0);
            status.SetDouble(PregnancyDueTotalHoursKey, 0);
            SetFotsaBreedingFeedback(entity, status, "birth-success");
            PersistBreedingEvent(entity);
            // Do not conceive again within this update after a reconciled birth.
            return true;
        }
        if (native?.GetBool("isPregnant") == true)
        {
            if (customActive && managed) return false;
            SetFotsaBreedingFeedback(entity, status, customActive ? "pregnancy-conflict" : "native-pregnant");
            return true;
        }
        // A pre-candidate custom pregnancy can be mirrored only if there is no
        // competing native pregnancy. Its saved due time and parents stay intact.
        if (customActive) StartFotsaPregnancyMirror(entity, definition);
        return false;
    }

    internal static void StartFotsaPregnancyMirror(Entity entity, CompanionBreedingDefinition definition)
    {
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        ITreeAttribute native = entity.WatchedAttributes.GetTreeAttribute("multiply") ?? new TreeAttribute();
        double dueDays = status.GetDouble(PregnancyDueTotalHoursKey) / entity.World.Calendar.HoursPerDay;
        native.SetBool(FotsaPregnancyManagedKey, true);
        native.SetDouble(FotsaPregnancyDueDaysKey, dueDays);
        native.SetBool("isPregnant", true);
        native.SetDouble("totalDaysPregnancyStart", dueDays - definition.PregnancyDays);
        entity.WatchedAttributes.SetAttribute("multiply", native);
        entity.WatchedAttributes.MarkPathDirty("multiply");
    }

    internal static void CompleteFotsaPregnancyMirror(Entity entity, CompanionBreedingDefinition definition)
    {
        ITreeAttribute native = entity.WatchedAttributes.GetTreeAttribute("multiply") ?? new TreeAttribute();
        native.SetBool("isPregnant", false);
        native.SetDouble("totalDaysLastBirth", entity.World.Calendar.TotalDays);
        native.SetDouble("totalDaysCooldownUntil", entity.World.Calendar.TotalDays
            + definition.CooldownDaysMin + entity.World.Rand.NextDouble() * (definition.CooldownDaysMax - definition.CooldownDaysMin));
        // Keep the completion marker for idempotent ledger hydration/reload.
        entity.WatchedAttributes.SetAttribute("multiply", native);
        entity.WatchedAttributes.MarkPathDirty("multiply");
    }

    internal static void EnsureFotsaJuvenileMetadata(Entity entity)
    {
        if (!CompanionFotsaBreedingCatalog.TryGetGrowth(entity.Code, out CompanionFotsaOffspring offspring)) return;
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        status.SetBool(JuvenileKey, true);
        status.SetString(JuvenileSpeciesKey, "tamables-fotsa");
        if (string.IsNullOrEmpty(status.GetString(ChildAdultEntityCodeKey)))
            status.SetString(ChildAdultEntityCodeKey, offspring.AdultCode);
        MarkSocialStateDirty(entity);
    }

    internal static bool IsFotsaAquatic(EntityProperties properties) =>
        CompanionFotsaBreedingCatalog.IsFotsa(properties.Code)
        && (properties.Code.Path.StartsWith("tame-sirenia-", StringComparison.OrdinalIgnoreCase)
            || properties.Code.Path.StartsWith("tame-iniidae-", StringComparison.OrdinalIgnoreCase)
            || properties.Code.Path.StartsWith("tame-chelonioidea-", StringComparison.OrdinalIgnoreCase));

    private Vec3d? FindSafeCompanionBirthPosition(Entity mother, EntityProperties childType, IReadOnlyList<Vec3d> reserved)
    {
        if (!IsFotsaAquatic(childType)) return FindSafeEntityPosition(mother.Pos.AsBlockPos, childType, mother, 4, 2, reserved);
        if (serverApi == null) return null;
        // Only connected, full water cells near the mother are eligible. Land
        // placement must never be a fallback for aquatic newborns.
        BlockPos origin = mother.Pos.AsBlockPos;
        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        bool FullWater(BlockPos pos)
        {
            Block fluid = blocks.GetBlock(pos, BlockLayersAccess.Fluid);
            return fluid.IsLiquid() && fluid.LiquidLevel >= 7 && fluid.LiquidCode is "water" or "saltwater";
        }
        if (!FullWater(origin)) return null;
        Queue<BlockPos> pending = new();
        HashSet<BlockPos> visited = new();
        pending.Enqueue(origin); visited.Add(origin);
        while (pending.Count > 0)
        {
            BlockPos pos = pending.Dequeue();
            Vec3d candidate = new(pos.X + 0.5, pos.Y + pos.dimension * BlockPos.DimensionBoundary, pos.Z + 0.5);
            if (!pos.Equals(origin)
                && !reserved.Any(previous => Math.Abs(previous.X - candidate.X) < 0.9 && Math.Abs(previous.Y - candidate.Y) < 1.5 && Math.Abs(previous.Z - candidate.Z) < 0.9)
                && !serverApi.World.CollisionTester.IsColliding(blocks, childType.SpawnCollisionBox, candidate, false)
                && serverApi.World.GetEntitiesAround(candidate, 0.9f, 1.5f, other => other != mother && other.Alive && !IsFoxAwayFromWorld(other)).Length == 0)
                return candidate;
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                BlockPos next = pos.AddCopy(face);
                if (Math.Abs(next.X - origin.X) > 4 || Math.Abs(next.Z - origin.Z) > 4 || Math.Abs(next.Y - origin.Y) > 2
                    || !visited.Add(next) || !FullWater(next)) continue;
                pending.Enqueue(next);
            }
        }
        return null;
    }
}
