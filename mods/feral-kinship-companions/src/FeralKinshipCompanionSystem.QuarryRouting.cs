#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;
public sealed partial class FeralKinshipCompanionSystem
{
    private void ActivateQuarryFoundations(BlockEntityFeralKinshipWorkCart cart)
    {
        var q = cart.Quarry;
        int plane = q.SurfaceY - q.Layer + 1;
        foreach (var part in q.Structure.Where(s => s.DeferredFoundation && s.Point.Y >= plane))
        {
            var pos = part.Point.Position(cart.Pos.dimension);
            if (serverApi!.World.BlockAccessor.GetChunkAtBlockPos(pos) == null) continue;
            Block actual = serverApi.World.BlockAccessor.GetBlock(pos);
            if (actual.BlockMaterial == EnumBlockMaterial.Ore && !QuarryFlint(actual))
            { ReportCartFinding(cart, pos, "Protected ore: " + QuarryBlockName(actual, pos)); continue; }
            // A natural foundation is already carrying the step. Convert only when
            // this layer reaches it and replacement cannot undercut protected ore.
            if (QuarryCutProblem(cart, pos, structure: true).Length == 0) part.DeferredFoundation = false;
        }
    }

    private IEnumerable<(QuarryPoint A, QuarryPoint B, int Dx, int Dz)> QuarryTurns(
        WorkCartQuarryState q, QuarryPoint a, QuarryPoint b, int hx, int hz, int y)
    {
        var seen = new HashSet<(QuarryPoint, QuarryPoint)>();
        foreach (var d in new[] { (X:hx,Z:hz), (X:-hz,Z:hx), (X:hz,Z:-hx), (X:-hx,Z:-hz) })
        foreach (var previous in new[] { a, b }) foreach (int sign in new[] { 1, -1 })
        {
            var next = new QuarryPoint(previous.X + d.X, y, previous.Z + d.Z);
            var other = next.Add(-d.Z * sign, 0, d.X * sign);
            bool Wall(QuarryPoint p) => q.Contains(p.X, p.Z)
                && Math.Min(Math.Min(p.X - q.MinX, q.MaxX - p.X), Math.Min(p.Z - q.MinZ, q.MaxZ - p.Z)) <= 4;
            if (!Wall(next) || !Wall(other) || next.X == a.X && next.Z == a.Z || next.X == b.X && next.Z == b.Z
                || other.X == a.X && other.Z == a.Z || other.X == b.X && other.Z == b.Z
                || !seen.Add((next, other))) continue;
            yield return (next, other, d.X, d.Z);
        }
    }

    private bool QuarryRouteSegment(BlockEntityFeralKinshipWorkCart cart, QuarryPoint a, QuarryPoint b,
        int dx, int dz, bool landing, List<QuarryStructureBlock> prefix,
        out List<QuarryStructureBlock> path, out string problem)
    {
        var q = cart.Quarry; var blocks = serverApi!.World.BlockAccessor;
        path = new(prefix); problem = "";
        string rock = q.PreferredRock;
        if (rock.Length == 0 && cart.Tools.Count > 4 && WorkCartAxeInventory.IsRock(cart.Tools[4].Itemstack))
            rock = cart.Tools[4].Itemstack!.Collectible.Code.Path[6..];
        if (rock.Length == 0) { problem = "Put matching rocks in the staircase buffer"; return false; }
        var parts = WorkCartQuarryState.StepBlocks(a, b, dx, dz, rock, q.Layer);
        if (landing) foreach (var part in parts.Where(p => p.Point.Y == a.Y)) part.Code = "game:cobblestone-" + rock;
        foreach (var part in parts)
        {
            var pos = part.Point.Position(cart.Pos.dimension);
            if (blocks.GetChunkAtBlockPos(pos) == null) { problem = "Support terrain not loaded"; return false; }
            var earlier = path.Concat(q.Structure).FirstOrDefault(s => s.Point == part.Point);
            if (earlier != null)
            {
                if (earlier.Code != part.Code) { problem = "Existing staircase blocks this turn"; return false; }
                continue; // Reuse one compatible paid support; never charge twice.
            }
            if (serverApi.World.GetBlock(new AssetLocation(part.Code)) is not Block desired || desired.Id == 0)
            { problem = "No matching cobblestone/stair block exists"; return false; }
            Block actual = blocks.GetBlock(pos);
            bool natural = (QuarryNatural(actual) || actual.BlockMaterial == EnumBlockMaterial.Ore)
                && actual.BlockMaterial is EnumBlockMaterial.Stone or EnumBlockMaterial.Ore or EnumBlockMaterial.Soil;
            // Solid, untouched foundations already support the stair. Do not dig
            // three layers early to substitute cobble, reveal ore, or expose a cave.
            if (part.Point.Y < a.Y && actual.Id != 0 && natural && actual.SideSolid[BlockFacing.UP.Index]
                && !blocks.GetBlock(pos, BlockLayersAccess.Fluid).IsLiquid()) part.DeferredFoundation = true;
            else
            {
                problem = QuarryEntrance(q, part.Point) ? "Cart entrance must remain supported"
                    : QuarryCutProblem(cart, pos, structure: true);
                if (problem.Length > 0) return false;
            }
            path.Add(part);
        }
        foreach (var top in new[] { a, b }) for (int h = 1; h <= 2; h++)
        {
            var pos = top.Add(0, h, 0).Position(cart.Pos.dimension);
            if (blocks.GetChunkAtBlockPos(pos) == null) { problem = "Headroom terrain not loaded"; return false; }
            Block actual = blocks.GetBlock(pos);
            if (actual.Id != 0 && actual.BlockMaterial != EnumBlockMaterial.Plant)
            { problem = "Headroom blocked by " + QuarryBlockName(actual, pos); return false; }
        }
        return true;
    }

    private void PlanQuarryStair(BlockEntityFeralKinshipWorkCart cart)
    {
        var q = cart.Quarry;
        long now = serverApi!.World.ElapsedMilliseconds;
        if (now < cart.NextStairRoutePlan) return;
        cart.NextStairRoutePlan = now + 5000;
        var queue = new Queue<(QuarryPoint A, QuarryPoint B, int Dx, int Dz, List<QuarryStructureBlock> Path)>();
        var visited = new HashSet<(QuarryPoint, QuarryPoint)>();
        var rejected = new Dictionary<string, int>(StringComparer.Ordinal);
        queue.Enqueue((q.StairA, q.StairB, q.HeadingX, q.HeadingZ, new()));
        int budget = 256;
        while (queue.Count > 0 && budget-- > 0)
        {
            var at = queue.Dequeue();
            foreach (var next in QuarryTurns(q, at.A, at.B, at.Dx, at.Dz, q.SurfaceY - q.Layer + 1))
            {
                if (!QuarryRouteSegment(cart, next.A, next.B, next.Dx, next.Dz, false, at.Path, out var path, out var why))
                { rejected[why] = rejected.GetValueOrDefault(why) + 1; continue; }
                q.Structure.AddRange(path);
                q.StairA = next.A; q.StairB = next.B; q.HeadingX = next.Dx; q.HeadingZ = next.Dz;
                q.Status = at.Path.Count == 0 ? $"Building layer {q.Layer} staircase."
                    : $"Building a two-wide wall detour before layer {q.Layer}.";
                cart.MarkDirty(true); return;
            }
            // Move sideways on a paid, two-wide level landing before trying to
            // descend elsewhere. Prefix footprints prevent turns through the route.
            foreach (var next in QuarryTurns(q, at.A, at.B, at.Dx, at.Dz, at.A.Y))
            {
                if (!visited.Add((next.A, next.B))) continue;
                if (QuarryRouteSegment(cart, next.A, next.B, next.Dx, next.Dz, true, at.Path, out var path, out _))
                    queue.Enqueue((next.A, next.B, next.Dx, next.Dz, path));
            }
        }
        string details = string.Join("; ", rejected.OrderByDescending(p => p.Value).Take(2).Select(p => p.Key + $" [{p.Value} routes]"));
        q.Status = "No safe wall detour currently reachable: " + details;
    }
}
