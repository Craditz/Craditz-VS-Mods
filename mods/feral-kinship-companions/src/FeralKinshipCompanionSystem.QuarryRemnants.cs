#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;
public sealed partial class FeralKinshipCompanionSystem
{
    private void RecoverQuarryFrontier(BlockEntityFeralKinshipWorkCart cart)
    {
        var q = cart.Quarry;
        if (cart.QuarryJobs.Count != 0 || q.CompletedCutY == int.MaxValue || q.Structure.Count == 0
            || q.Stage is not ("flattening" or "excavating") || q.PlaneY <= q.CompletedCutY) return;
        // Preview.16 could rewind to an unreachable old pillar after paying for
        // the next step. Recover from completed terrain, never decrement counters.
        foreach (var point in q.Pending) q.QueueRemnant(point);
        q.Pending.Clear();
        q.Layer = Math.Clamp(q.SurfaceY - q.CompletedCutY + 2, 1, q.Depth);
        q.PlaneY = q.SurfaceY - q.Layer + 1;
        q.SurveyIndex = 0;
        var step = q.Structure.Where(s => s.Level == q.Layer).ToArray();
        bool ready = step.Length > 0 && step.All(s => s.Placed || s.DeferredFoundation);
        q.Stage = ready ? "excavating" : "stairs";
        if (ready) q.StairLevel = Math.Max(q.StairLevel, q.Layer);
        q.Status = $"Resuming layer {q.Layer}; old surface remnants are optional cleanup.";
        serverApi!.Logger.Notification("[Quarry] Restored main frontier at layer {0} for {1}; paid stairs and counters retained.", q.Layer, cart.Pos);
        cart.MarkDirty(true);
    }

    private bool TryCreateQuarryRemnantJob(Entity fox, BlockEntityFeralKinshipWorkCart cart, out WorkCartQuarryJob? job)
    {
        job = null;
        var q = cart.Quarry;
        if (!q.Enabled || IsCartWindDown(fox) || !AllowsWellbeingDuty(fox, demanding: true)
            || cart.Tools.FindAvailable(0) < 0 || cart.QuarryJobs.Any(j => j.Kind == QuarryJobKind.Build)) return false;
        long now = serverApi!.World.ElapsedMilliseconds;
        int inspected = 0;
        foreach (var point in q.CleanupPending.OrderBy(p => fox.Pos.SquareDistanceTo(p.Position(cart.Pos.dimension).ToVec3d())).ToArray())
        {
            if (cart.QuarryFailedTargets.GetValueOrDefault(point) > now
                || cart.QuarryJobs.Any(j => j.Target.X == point.X && j.Target.Y == point.Y && j.Target.Z == point.Z)) continue;
            if (++inspected > 24) break;
            var target = point.Position(cart.Pos.dimension);
            var blocks = serverApi.World.BlockAccessor;
            if (blocks.GetChunkAtBlockPos(target) == null) continue;
            Block block = blocks.GetBlock(target);
            if (block.Id == 0 || q.IsProtected(point) || q.IsStructure(point) || q.IsEntrance(point) || !QuarryNatural(block))
            { q.CleanupPending.Remove(point); continue; }
            string problem = QuarryCutProblem(cart, target, structure: false);
            if (problem.Length > 0)
            { ReportCartFinding(cart, target, problem); q.CleanupPending.Remove(point); continue; }
            if (!TryQuarryApproach(fox, cart, target, false, out var approach)
                || approach == null || !MiningLineClear(approach.AddCopy(0, .7, 0), target))
            {
                cart.QuarryFailedTargets[point] = now + 15000;
                ReportCartFinding(cart, target, "Unreachable quarry remnant; main excavation continues.");
                continue;
            }
            if (CreateQuarryPrimary(fox, cart, target, approach, QuarryJobKind.Mine, QuarryRockType(block), "", out job))
            {
                job!.IsRemnant = true;
                q.Status = $"Clearing a reachable old remnant; main layer {q.Layer} retained.";
                return true;
            }
        }
        return false;
    }
}
