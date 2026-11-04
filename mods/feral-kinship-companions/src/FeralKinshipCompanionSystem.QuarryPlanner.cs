#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private int quarryRoundRobin;
    internal static string QuarryRockType(Block? b)
    {
        if (b?.Code?.Domain != "game" || b.BlockMaterial != EnumBlockMaterial.Stone) return "";
        string path = b.Code.Path;
        if (path.StartsWith("rock-", StringComparison.Ordinal)) return path[5..];
        // Vanilla generates fractured natural stone beneath surface soil too.
        // Its material is the same rock variant, not a constructed obstruction.
        if (path.StartsWith("crackedrock-", StringComparison.Ordinal)) return path[12..];
        return "";
    }

    internal static bool QuarryFlint(Block? b) => b?.Code?.Domain == "game"
        && b.BlockMaterial == EnumBlockMaterial.Ore
        && b.Code.Path.StartsWith("ore-flint-", StringComparison.Ordinal);

    // Natural clay deposits only: exclude fired clay, pottery and other structures.
    internal static bool QuarryClay(Block? b) => b?.Code?.Domain == "game"
        && b.BlockMaterial == EnumBlockMaterial.Soil
        && b.Code.Path.StartsWith("rawclay-", StringComparison.Ordinal);

    internal static bool QuarryNatural(Block? b)
    {
        if (b?.Code?.Domain != "game") return false;
        string p = b.Code.Path;
        return QuarryRockType(b).Length > 0 || QuarryFlint(b) || QuarryClay(b) || p.StartsWith("soil-", StringComparison.Ordinal)
            || p.StartsWith("sand-", StringComparison.Ordinal) || p.StartsWith("gravel-", StringComparison.Ordinal)
            || p.StartsWith("loosestones-", StringComparison.Ordinal)
            || p.StartsWith("snow", StringComparison.Ordinal) || (b.BlockMaterial == EnumBlockMaterial.Plant
                && (p.StartsWith("tallgrass", StringComparison.Ordinal) || p.StartsWith("flower-", StringComparison.Ordinal)
                    || p.StartsWith("fern-", StringComparison.Ordinal)));
    }

    private void MaintainWorkCartQuarries()
    {
        if (serverApi == null || packRepository?.Loaded != true) return;
        var records = packRepository.GetAllWorkCarts().Where(c => c.Kind is "quarrying" or "mining").ToArray();
        if (records.Length == 0) return;
        // One cart per pass, bounded column/voxel survey, independently of animal count.
        var record = records[quarryRoundRobin++ % records.Length];
        var pos = new BlockPos(record.X, record.Y, record.Z, record.Dimension);
        if (serverApi.World.BlockAccessor.GetChunkAtBlockPos(pos) == null) return;
        var cart = EnsureWorkCartTools(pos);
        if (cart?.Tools.QuarryMode != true) return;
        long now = serverApi.World.ElapsedMilliseconds;
        if (cart.QuarrySupplyLease < now) cart.QuarrySupplyWorker = 0;
        foreach (var job in cart.QuarryJobs.ToArray())
        {
            foreach (long id in job.Workers.Where(p => p.Value < now - 10000).Select(p => p.Key).ToArray())
                job.Workers.Remove(id);
            if (job.Workers.Count == 0) FinishQuarryPrimary(cart, job);
        }
        if (cart.MiningMode) return;
        MaintainQuarryRemnants(cart);
        if (!cart.Quarry.Enabled || cart.Quarry.UnreadablePayload != null) return;
        AdvanceQuarrySurvey(cart, 32);
    }

    private void AdvanceQuarrySurvey(BlockEntityFeralKinshipWorkCart cart, int budget)
    {
        if (serverApi == null) return;
        var q = cart.Quarry;
        var blocks = serverApi.World.BlockAccessor;
        RecoverQuarryFrontier(cart);
        RecheckProtectedQuarryDeposits(cart);
        if (q.Stage == "survey")
        {
            while (budget-- > 0 && q.SurveyIndex < q.Width * q.Width)
            {
                var p = q.PlaneCell(q.SurveyIndex).Position(cart.Pos.dimension);
                p.Y = q.SurfaceY;
                if (blocks.GetChunkAtBlockPos(p) == null) { q.Status = "Waiting for loaded quarry terrain."; return; }
                int top = Math.Max(blocks.GetTerrainMapheightAt(p), blocks.GetRainMapHeightAt(p));
                if (top > q.SurfaceY + 48) q.Protect(new(p.X, p.Y, p.Z), "Very high terrain remains protected.");
                else q.SurveyTop = Math.Max(q.SurveyTop, top + 1);
                q.SurveyIndex++;
            }
            if (q.SurveyIndex < q.Width * q.Width) return;
            q.PlaneY = q.SurveyTop; q.SurveyIndex = 0; q.Stage = "flattening";
        }
        if (q.Stage == "stairs")
        {
            if (!q.Structure.Any(s => s.Level == q.Layer)) PlanQuarryStair(cart);
            if (q.Structure.Any(s => s.Level == q.Layer)
                && q.Structure.Where(s => s.Level == q.Layer).All(s => s.Placed || s.DeferredFoundation))
            {
                q.StairLevel = q.Layer; q.Stage = "excavating";
                q.PlaneY = q.SurfaceY - q.Layer + 1; q.SurveyIndex = 0;
            }
        }
        if (q.Stage is not ("flattening" or "excavating")) return;
        while (budget-- > 0 && q.SurveyIndex < q.Width * q.Width)
        {
            QuarryPoint p = q.PlaneCell(q.SurveyIndex);
            BlockPos pos = p.Position(cart.Pos.dimension);
            if (blocks.GetChunkAtBlockPos(pos) == null) { q.Status = "Waiting for loaded quarry terrain."; return; }
            Block b = blocks.GetBlock(pos);
            q.SurveyIndex++;
            if (q.IsProtected(p) || q.IsStructure(p) || QuarryEntrance(q, p)) continue;
            if (b.Id == 0)
            {
                if (!q.WasExcavated(p) && !QuarryOpenToSky(pos, q))
                    q.Protect(p, "Working around a protected underground opening.");
                continue;
            }
            if (!QuarryNatural(b))
            {
                q.Protect(p, QuarryObstacleReason(b, pos));
                ReportCartFinding(cart, pos, QuarryObstacleReason(b, pos));
                continue;
            }
            if (!q.Pending.Contains(p)) q.Pending.Add(p);
            string rock = QuarryRockType(b);
            if (q.PreferredRock.Length == 0 && rock.Length > 0) q.PreferredRock = rock;
        }
        if (q.SurveyIndex < q.Width * q.Width || q.Pending.Count > 0 || cart.QuarryJobs.Any(j => !j.IsRemnant)) return;
        q.CompletedCutY = Math.Min(q.CompletedCutY, q.PlaneY);
        q.Excavated.RemoveWhere(p => p.Y >= q.CompletedCutY);
        if (q.Stage == "flattening")
        {
            if (q.PlaneY > q.SurfaceY + 1) { q.PlaneY--; q.SurveyIndex = 0; }
            else { q.Stage = "stairs"; q.Layer = 1; q.SurveyIndex = 0; }
        }
        else if (q.Layer >= q.Depth)
        {
            q.Stage = "complete"; q.Enabled = false;
            q.Status = $"Quarry complete at {q.Depth} layers; ore and cave barriers remain protected.";
        }
        else { q.Layer++; q.Stage = "stairs"; q.SurveyIndex = 0; }
        cart.MarkDirty(true);
    }

    private static bool QuarryEntrance(WorkCartQuarryState q, QuarryPoint p) => q.IsEntrance(p);

    private bool QuarryOpenToSky(BlockPos p, WorkCartQuarryState q)
    {
        if (serverApi == null) return false;
        var blocks = serverApi.World.BlockAccessor;
        for (int y = p.Y; y <= Math.Max(q.SurfaceY + 2, p.Y + 4); y++)
        {
            var check = new BlockPos(p.X, y, p.Z, p.dimension);
            if (blocks.GetChunkAtBlockPos(check) == null) return false;
            Block b = blocks.GetBlock(check);
            if (b.Id != 0 && b.BlockMaterial != EnumBlockMaterial.Plant && b.BlockMaterial != EnumBlockMaterial.Snow) return false;
        }
        return true;
    }

    private string QuarryBlockName(Block block, BlockPos pos)
    {
        string name = serverApi == null ? block.Code?.Path ?? "unknown block" : block.GetPlacedBlockName(serverApi.World, pos);
        return name.Length > 70 ? name[..67] + "..." : name;
    }

    private string QuarryObstacleReason(Block block, BlockPos pos)
    {
        string name = QuarryBlockName(block, pos);
        if (block.BlockMaterial == EnumBlockMaterial.Ore && !QuarryFlint(block)) return "Protected ore: " + name;
        return "Obstruction: " + name;
    }

    private string QuarryColumnReason(BlockEntityFeralKinshipWorkCart cart, BlockPos pos)
    {
        var q = cart.Quarry;
        string key = WorkCartQuarryState.ColumnKey(pos.X, pos.Z);
        if (q.ProtectedReasons.TryGetValue(key, out string? saved) && !string.IsNullOrWhiteSpace(saved)) return saved;
        // Old saves have only a column marker. Recover an observable material
        // reason where possible; never erase an unexplained safety marker.
        for (int y = Math.Min(q.SurveyTop, q.SurfaceY + 48); y >= pos.Y; y--)
        {
            var at = new BlockPos(pos.X, y, pos.Z, pos.dimension);
            if (serverApi!.World.BlockAccessor.GetChunkAtBlockPos(at) == null) return "Protected column; terrain not loaded";
            Block b = serverApi.World.BlockAccessor.GetBlock(at);
            if (b.Id != 0 && !QuarryNatural(b)) return QuarryObstacleReason(b, at);
        }
        return "Previously protected column; saved safety marker";
    }

    private bool QuarryCutSafe(BlockEntityFeralKinshipWorkCart cart, BlockPos target, bool structure)
        => QuarryCutProblem(cart, target, structure).Length == 0;

    private string QuarryCutProblem(BlockEntityFeralKinshipWorkCart cart, BlockPos target, bool structure, bool recheckDeposit = false)
    {
        if (serverApi == null) return "Server not ready";
        if (target.Y < 1) return "World depth limit";
        var q = cart.Quarry;
        var point = new QuarryPoint(target.X, target.Y, target.Z);
        var blocks = serverApi.World.BlockAccessor;
        var record = packRepository?.GetWorkCart(cart.Pos);
        IPlayer? owner = record == null ? null : serverApi.World.PlayerByUid(record.OwnerUid);
        if (owner == null) return "Cart owner is not online";
        if (!q.Contains(target.X, target.Z)) return "Outside quarry boundary";
        if (blocks.GetChunkAtBlockPos(target) == null) return "Terrain not loaded";
        EnumWorldAccessResponse access = serverApi.World.Claims.TestAccess(owner, target, EnumBlockAccessFlags.BuildOrBreak);
        if (access != EnumWorldAccessResponse.Granted) return access switch
        {
            EnumWorldAccessResponse.LandClaimed => "Land claim denies block changes",
            EnumWorldAccessResponse.InSpectatorMode => "Cart owner is in spectator mode",
            EnumWorldAccessResponse.PlayerDead => "Cart owner is dead; waiting for respawn",
            EnumWorldAccessResponse.InGuestMode => "Cart owner is in guest mode",
            EnumWorldAccessResponse.NoPrivilege => "Cart owner lacks build permission",
            EnumWorldAccessResponse.DeniedByMod => "Block changes denied by another mod",
            _ => $"Block access denied ({access})"
        };
        Block b = blocks.GetBlock(target);
        if (q.IsProtected(point) && !(recheckDeposit && QuarryNatural(b))) return QuarryColumnReason(cart, target);
        if (!structure && q.IsStructure(point)) return "Existing staircase must remain intact";
        if (!structure && QuarryEntrance(q, point)) return "Cart entrance must remain supported";
        if (b.Id != 0 && !QuarryNatural(b)) return QuarryObstacleReason(b, target);
        if (blocks.GetBlock(target, BlockLayersAccess.Fluid).IsLiquid()) return "Liquid in the work cell";
        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            var n = target.AddCopy(face);
            if (blocks.GetChunkAtBlockPos(n) == null) return "Neighboring terrain not loaded";
            if (blocks.GetBlock(n, BlockLayersAccess.Fluid).IsLiquid()) return "Liquid next to the work cell";
            Block neighbor = blocks.GetBlock(n);
            if (face == BlockFacing.UP && neighbor.BlockMaterial == EnumBlockMaterial.Ore && !QuarryFlint(neighbor))
                return "Would remove support from ore: " + QuarryBlockName(neighbor, n);
            if (neighbor.Id == 0 && !q.WasExcavated(new(n.X, n.Y, n.Z)) && !QuarryOpenToSky(n, q))
                return "Underground opening beside the work cell";
        }
        return "";
    }

    private void RecheckProtectedQuarryDeposits(BlockEntityFeralKinshipWorkCart cart)
    {
        var q = cart.Quarry;
        if (serverApi == null || !q.Started || q.Stage is not ("stairs" or "excavating")
            || cart.QuarryJob != null || q.ProtectedColumns.Count == 0
            || serverApi.World.ElapsedMilliseconds < cart.NextQuarryDepositRecheck
            || q.Structure.Any(s => s.Level == q.Layer && !s.Placed && !s.DeferredFoundation)) return;
        var record = packRepository?.GetWorkCart(cart.Pos);
        if (record == null || serverApi.World.PlayerByUid(record.OwnerUid) == null) return;
        // Login, game mode and mod access checks may deny work temporarily.
        // Retry while idle instead of making a failed check permanent this session.
        cart.NextQuarryDepositRecheck = serverApi.World.ElapsedMilliseconds + 15000;
        var blocks = serverApi.World.BlockAccessor;
        int lastPlane = q.Stage == "excavating" ? q.PlaneY : q.SurfaceY - q.Layer + 2;
        int reopened = 0;
        bool reasonsChanged = false;
        foreach (string key in q.ProtectedColumns.ToArray())
        {
            string[] parts = key.Split(',');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int z)) continue;
            for (int y = q.SurveyTop; y >= lastPlane; y--)
            {
                var at = new BlockPos(x, y, z, cart.Pos.dimension);
                if (blocks.GetChunkAtBlockPos(at) == null) break;
                Block b = blocks.GetBlock(at);
                if (b.Id == 0) continue;
                if (!QuarryNatural(b))
                {
                    if (!q.ProtectedFromY.ContainsKey(key)) { q.ProtectedFromY[key] = y; reasonsChanged = true; }
                    ReportCartFinding(cart, at, QuarryObstacleReason(b, at));
                }
                if (QuarryNatural(b))
                {
                    string problem = QuarryCutProblem(cart, at, structure: false, recheckDeposit: true);
                    if (problem.Length == 0)
                    {
                        q.ProtectedColumns.Remove(key); q.ProtectedReasons.Remove(key); q.ProtectedFromY.Remove(key);
                        q.QueueRemnant(new(x, y, z)); reopened++;
                    }
                    else if (!q.ProtectedReasons.TryGetValue(key, out string? previous) || previous != problem)
                    {
                        // Replace obsolete material reasons with the current safety blocker.
                        q.ProtectedReasons[key] = problem; reasonsChanged = true;
                    }
                }
                break; // Keep structures, other ores and unexplained columns protected.
            }
        }
        if (reopened == 0)
        {
            if (reasonsChanged) cart.MarkDirty(true);
            return;
        }
        // Optional old surface work must never rewind the layer or clear its queue.
        serverApi.Logger.Notification("[Quarry] Queued {0} safe old clearing columns at {1}; main layer unchanged.", reopened, cart.Pos);
        cart.MarkDirty(true);
    }

    private bool QuarryPastStairsIntact(BlockEntityFeralKinshipWorkCart cart)
    {
        if (serverApi == null) return false;
        foreach (var part in cart.Quarry.Structure.Where(s => s.Placed && !s.DeferredFoundation))
        {
            BlockPos pos = part.Point.Position(cart.Pos.dimension);
            if (serverApi.World.BlockAccessor.GetChunkAtBlockPos(pos) == null
                || serverApi.World.BlockAccessor.GetBlock(pos).Code?.ToString() != part.Code) return false;
        }
        return true;
    }
}
