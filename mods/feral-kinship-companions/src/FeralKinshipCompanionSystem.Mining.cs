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
    internal static bool MiningOre(Block? block) => block?.BlockMaterial == EnumBlockMaterial.Ore;
    private bool MiningBounds(Entity worker, BlockEntityFeralKinshipWorkCart cart, QuarryPoint p)
    {
        var record = packRepository?.GetWorkCart(cart.Pos);
        double radius = record != null ? GetOwnerWorkCartRadius(record.OwnerUid) : GetWorkCartRadius(worker);
        return WorkCartMiningState.WithinWorkArea(p, new(cart.Pos.X, cart.Pos.Y, cart.Pos.Z), radius) && p.Y >= 4
            && p.Y >= cart.Pos.Y - cart.Mining.Depth - 12 && p.Y <= cart.Pos.Y + 12;
    }

    private bool MiningStand(Entity fox, BlockPos feet)
    {
        var blocks = serverApi!.World.BlockAccessor;
        for (int h = -1; h <= 3; h++)
        {
            var p = feet.AddCopy(0, h, 0);
            if (blocks.GetChunkAtBlockPos(p) == null || blocks.GetBlock(p, BlockLayersAccess.Fluid).IsLiquid()) return false;
        }
        Block floor=blocks.GetBlock(feet.DownCopy());
        if (!floor.SideSolid[BlockFacing.UP.Index] && !MiningStair(floor)) return false;
        var clearance = (fox.CollisionBox ?? fox.Properties.SpawnCollisionBox).Clone();
        clearance.Y2 = Math.Max(clearance.Y2, 2);
        return !serverApi.World.CollisionTester.IsColliding(blocks, clearance,
            new Vec3d(feet.X + .5, feet.InternalY, feet.Z + .5), false);
    }

    /// <summary>Walkable-air flood, not an ore-volume scan. Only loaded,
    /// physically connected standing positions are searched; native navigation
    /// subsequently validates the chosen route as well.</summary>
    private void SurveyMiningAccess(Entity fox, BlockEntityFeralKinshipWorkCart cart)
    {
        long now = serverApi!.World.ElapsedMilliseconds;
        if (now < cart.NextMiningSurvey && cart.MiningReach.Count > 0) return;
        cart.NextMiningSurvey = now + 3000;
        var start = fox.Pos.AsBlockPos;
        start.Y = (int)Math.Floor(fox.Pos.Y + .05) - start.dimension * BlockPos.DimensionBoundary;
        if (!MiningStand(fox, start))
        {
            BlockPos? candidate = null;
            foreach (int dy in new[] { 0, 1, -1 })
            foreach (var direction in BlockFacing.HORIZONTALS)
            {
                var p = start.AddCopy(direction.Normali.X, dy, direction.Normali.Z);
                if (MiningStand(fox, p)) { candidate = p; break; }
            }
            // Walking over beds, partial blocks or a falling transition must
            // not erase the last valid map or permanently classify air as cave.
            if (candidate == null) return;
            start = candidate;
        }
        cart.MiningReach.Clear();
        var queue = new Queue<QuarryPoint>();
        var first = new QuarryPoint(start.X, start.Y, start.Z);
        queue.Enqueue(first); cart.MiningReach.Add(first);
        int budget = 1536;
        while (queue.Count > 0 && budget-- > 0)
        {
            QuarryPoint current = queue.Dequeue();
            foreach (var direction in BlockFacing.HORIZONTALS)
            {
                foreach (int dy in new[] { 0, 1, -1 })
                {
                    var p = current.Add(direction.Normali.X, dy, direction.Normali.Z);
                    if (cart.MiningReach.Contains(p) || !MiningBounds(fox, cart, p)) continue;
                    // A step up needs headroom above the previous floor too.
                    if (dy > 0 && !MiningAir(current.Add(0, 2, 0).Position(cart.Pos.dimension))) continue;
                    if (!MiningStand(fox, p.Position(cart.Pos.dimension))) continue;
                    cart.MiningReach.Add(p); queue.Enqueue(p); break;
                }
            }
        }
    }

    private bool MiningAir(BlockPos pos)
    {
        var b = serverApi!.World.BlockAccessor;
        return b.GetChunkAtBlockPos(pos) != null && b.GetBlock(pos).Id == 0
            && !b.GetBlock(pos, BlockLayersAccess.Fluid).IsLiquid();
    }

    private bool MiningLineClear(Vec3d from, BlockPos target)
    {
        var end = target.ToVec3d().Add(.5, .5, .5);
        double dx=end.X-from.X,dy=end.Y-from.Y,dz=end.Z-from.Z;
        int x=(int)Math.Floor(from.X),y=(int)Math.Floor(from.Y),z=(int)Math.Floor(from.Z);
        int sx=Math.Sign(dx),sy=Math.Sign(dy),sz=Math.Sign(dz);
        double tx=sx==0?double.PositiveInfinity:((sx>0?x+1:x)-from.X)/dx;
        double ty=sy==0?double.PositiveInfinity:((sy>0?y+1:y)-from.Y)/dy;
        double tz=sz==0?double.PositiveInfinity:((sz>0?z+1:z)-from.Z)/dz;
        double stepx=sx==0?double.PositiveInfinity:Math.Abs(1/dx),stepy=sy==0?double.PositiveInfinity:Math.Abs(1/dy),stepz=sz==0?double.PositiveInfinity:Math.Abs(1/dz);
        bool Clear(int a,int b,int c) {
            var p=new BlockPos(a,b-target.dimension*BlockPos.DimensionBoundary,c,target.dimension);
            return p.Equals(target)||MiningSightCellClear(from,end,p);
        }
        for(int i=0;i<64;i++)
        {
            var p=new BlockPos(x,y-target.dimension*BlockPos.DimensionBoundary,z,target.dimension);
            if(p.Equals(target))return true;
            if(!MiningSightCellClear(from,end,p))return false;
            double next=Math.Min(tx,Math.Min(ty,tz));if(next>1)return false;
            bool ax=tx<=next+1e-9,ay=ty<=next+1e-9,az=tz<=next+1e-9;
            // A corner is not a clear face when either touching side is solid.
            if((ax?1:0)+(ay?1:0)+(az?1:0)>1 && ((ax&&!Clear(x+sx,y,z))||(ay&&!Clear(x,y+sy,z))||(az&&!Clear(x,y,z+sz))))return false;
            if(ax){x+=sx;tx+=stepx;}if(ay){y+=sy;ty+=stepy;}if(az){z+=sz;tz+=stepz;}
        }
        return false;
    }

    private bool TryMiningApproach(Entity fox, BlockEntityFeralKinshipWorkCart cart,
        BlockPos target, bool build, out Vec3d? approach)
    {
        approach = null;
        foreach (var p in cart.MiningReach.OrderBy(p => fox.Pos.SquareDistanceTo(p.Position(cart.Pos.dimension).ToVec3d())))
        {
            var point = new Vec3d(p.X + .5, p.Y + target.dimension * BlockPos.DimensionBoundary, p.Z + .5);
            if (target.Y < p.Y)
            {
                double hx=target.X+.5-point.X,hz=target.Z+.5-point.Z,length=Math.Sqrt(hx*hx+hz*hz);
                if(length>.01)point.Add(hx/length*.2,0,hz/length*.2);
            }
            if (p.X == target.X && p.Z == target.Z && p.Y == target.Y + 1) continue;
            if(!AlignMiningStandingPoint(fox,point,p.Position(target.dimension)))continue;
            var body = point.AddCopy(0, .7, 0);
            if (body.SquareDistanceTo(target.ToVec3d().Add(.5, .5, .5)) > (build ? 36 : 10.24)
                || !MiningStand(fox, p.Position(target.dimension)) || (!build && (!MiningLineClear(body, target)
                    || !MiningLineClear(body.AddCopy(.3,0,0),target) || !MiningLineClear(body.AddCopy(-.3,0,0),target)
                    || !MiningLineClear(body.AddCopy(0,0,.3),target) || !MiningLineClear(body.AddCopy(0,0,-.3),target)))) continue;
            approach = point; return true;
        }
        return false;
    }

    private void DiscoverMiningOre(Entity fox, BlockEntityFeralKinshipWorkCart cart)
    {
        var m = cart.Mining;
        var blocks = serverApi!.World.BlockAccessor;
        // Exposed, pathable ore only: inspect the walls/ceiling/floor of actual
        // reachable air. Never enumerate buried ore in a surrounding volume.
        foreach (var stand in cart.MiningReach)
        {
            if (m.Vein.Count >= 64) break;
            for (int h = 0; h <= 2; h++) foreach (var face in BlockFacing.ALLFACES)
            {
                var p = stand.Add(face.Normali.X, h + face.Normali.Y, face.Normali.Z);
                if (!MiningBounds(fox, cart, p) || m.Blocked.Contains(p)) continue;
                var pos = p.Position(cart.Pos.dimension);
                if (blocks.GetChunkAtBlockPos(pos) != null && MiningOre(blocks.GetBlock(pos))
                    && TryMiningApproach(fox, cart, pos, false, out _)) m.FoundOre(p);
            }
        }
        // Explicit local exception: bounded to two blocks from the REAL worker,
        // never from a planned station, virtual scout or distant vein block.
        var center = fox.Pos.AsBlockPos;
        for (int x = -3; x <= 3; x++) for (int y = -2; y <= 3; y++) for (int z = -3; z <= 3; z++)
        {
            var p = new QuarryPoint(center.X + x, center.Y + y, center.Z + z);
            if (!WorkCartMiningState.LocalSense(fox.Pos.XYZ, p, center.dimension) || !MiningBounds(fox, cart, p)
                || m.Blocked.Contains(p)) continue;
            var pos = p.Position(center.dimension);
            if (blocks.GetChunkAtBlockPos(pos) != null && MiningOre(blocks.GetBlock(pos))) m.FoundOre(p);
        }
    }

    private bool MiningCutSafe(Entity fox, BlockEntityFeralKinshipWorkCart cart, BlockPos target, bool build)
    {
        if (serverApi == null || !MiningBounds(fox, cart, new(target.X, target.Y, target.Z))) return false;
        var blocks = serverApi.World.BlockAccessor;
        var owner = serverApi.World.PlayerByUid(packRepository!.GetWorkCart(cart.Pos)!.OwnerUid);
        if (owner == null || blocks.GetChunkAtBlockPos(target) == null || !CanPlayerModifyBlock(owner, target)
            || blocks.GetBlock(target, BlockLayersAccess.Fluid).IsLiquid()) return false;
        Block block = blocks.GetBlock(target);
        if (block.Id != 0 && !(MiningOre(block) || QuarryRockType(block).Length > 0)) return false;
        if (build && block.Id != 0) return false;
        if (!build && cart.Mining.Supports.Any(s => s.Point == new QuarryPoint(target.X, target.Y, target.Z))) return false;
        foreach (var face in BlockFacing.ALLFACES)
        {
            var n = target.AddCopy(face);
            if (blocks.GetChunkAtBlockPos(n) == null || blocks.GetBlock(n, BlockLayersAccess.Fluid).IsLiquid()) return false;
            if (blocks.GetBlock(n).Id == 0 && !MiningKnownAir(cart, n)) return false;
        }
        return true;
    }

    private bool MiningKnownAir(BlockEntityFeralKinshipWorkCart cart, BlockPos p)
        => cart.Mining.Opened.Contains(new(p.X, p.Y, p.Z))
            || cart.MiningReach.Any(s => s.X == p.X && s.Z == p.Z && p.Y >= s.Y && p.Y <= s.Y + 2);

    private string MiningHostRock(BlockEntityFeralKinshipWorkCart cart, BlockPos p)
    {
        string rock = QuarryRockType(serverApi!.World.BlockAccessor.GetBlock(p));
        if (rock.Length == 0)
        {
            // Vanilla ore variants carry their host rock. Do not convert ore
            // into resources; this only selects the matching building material.
            Block b = serverApi.World.BlockAccessor.GetBlock(p);
            rock = b.Variant?["rock"] ?? "";
        }
        if (rock.Length == 0) foreach (var face in BlockFacing.ALLFACES)
        {
            rock = QuarryRockType(serverApi.World.BlockAccessor.GetBlock(p.AddCopy(face)));
            if (rock.Length > 0) break;
        }
        return rock.Length > 0 ? rock : cart.Quarry.PreferredRock;
    }

    private bool PlanMiningFloor(Entity fox, BlockEntityFeralKinshipWorkCart cart, QuarryPoint floor, string rock, bool alongX = false)
    {
        if (rock.Length == 0) return false;
        var blocks = serverApi!.World.BlockAccessor;
        var parts = new List<QuarryStructureBlock>();
        // Only create a platform where removing the floor would lose access.
        // Two lanes, retained natural supports need no replacement or payment.
        for (int lane = 0; lane < 2; lane++) for (int below = 3; below >= 0; below--)
        {
            var p = floor.Add(alongX ? 0 : lane, -below, alongX ? lane : 0); var pos = p.Position(cart.Pos.dimension);
            if (cart.Mining.Supports.Any(s => s.Point == p)) continue;
            Block b = blocks.GetBlock(pos);
            if (blocks.GetChunkAtBlockPos(pos) == null || MiningOre(b)
                || blocks.GetBlock(pos, BlockLayersAccess.Fluid).IsLiquid()
                || (b.Id != 0 && QuarryRockType(b).Length == 0)
                || !MiningBounds(fox, cart, p)) return false;
            if (b.SideSolid[BlockFacing.UP.Index]) continue;
            if (!MiningCutSafe(fox, cart, pos, true)) return false;
            parts.Add(new() { Point = p, Rock = rock, Code = "game:cobblestone-" + rock });
        }
        cart.Mining.Supports.AddRange(parts); cart.MarkDirty(true); return true;
    }

    internal bool TryCreateMiningJob(Entity fox, out WorkCartQuarryJob? job)
    {
        job = null;
        if (serverApi == null || !TryGetAssignedWorkCart(fox, false, out var record, out var pos)
            || record?.Kind != "mining" || pos == null) return false;
        var cart = EnsureWorkCartTools(pos);
        if (cart?.MiningMode != true || !CanContinueQuarry(fox, cart)) return false;
        var m = cart.Mining;
        if (fox.WatchedAttributes.GetItemstack(QuarryCarryKey) is ItemStack cargo && cargo.StackSize > 0)
            return TryCreateQuarryDelivery(fox, cart, cargo, out job);
        if (!m.Enabled || m.UnreadablePayload != null) return false;
        if (m.EnsureProbePlan(GetOwnerWorkCartRadius(record.OwnerUid))) cart.MarkDirty(true);
        // Legacy numeric history is retained for display/progress, but the
        // adapted plan separately validates those passages in the real world.
        m.Cursor = Math.Max(m.Cursor, m.ProbeHistoryCount);
        SurveyMiningAccess(fox, cart);
        if (!IsCartWindDown(fox) && TryCreateQuarrySupply(fox, cart, out job)) return true;
        if (IsCartWindDown(fox)) return TryCreateQuarryCollection(fox, cart, true, out job)
            || TryCreateQuarryCollection(fox, cart, false, out job);
        if (TryCreateQuarryCollection(fox, cart, false, out job)) return true;
        if (!AllowsWellbeingDuty(fox, demanding: true)) return false;
        if (cart.QuarryJobs.Count > 0 && (cart.QuarryJobs.Any(j => j.Kind == QuarryJobKind.Build)
            || cart.Tools.FindAvailable(0) < 0)) return TryJoinCartJob(fox, cart, out job);
        if (serverApi.World.ElapsedMilliseconds < cart.NextQuarryPlanAt) return false;
        cart.NextQuarryPlanAt = serverApi.World.ElapsedMilliseconds + 750;
        SurveyMiningAccess(fox, cart);
        if (!TryGetFoxStorage(fox, out _)) { m.Status = "Provide linked storage space before further mining."; return false; }
        var construction = m.Supports.FirstOrDefault(s => MiningBounds(fox, cart, s.Point) && (!s.Placed
            || serverApi.World.BlockAccessor.GetBlock(s.Point.Position(pos.dimension)).Code?.ToString() != s.Code));
        if (construction != null)
        {
            if (cart.QuarryJobs.Count > 0) return TryJoinCartJob(fox, cart, out job);
            cart.Quarry.PreferredRock = construction.Rock;
            if (!QuarryBufferHas(cart, construction.Rock, 8))
            { m.Status = "Need 8 matching rocks to maintain access. Refill the rock buffer."; return false; }
            var target = construction.Point.Position(pos.dimension);
            if (!MiningPlacementClear(fox, target)) { m.Status = "Clear animals or players from the access placement first."; return false; }
            if (MiningCutSafe(fox, cart, target, true) && TryMiningApproach(fox, cart, target, true, out var approach))
                return CreateMiningPrimary(fox, cart, target, approach!, QuarryJobKind.Build, construction.Rock, construction.Code, out job);
            m.Status = "Access repair blocked; excavation waits for a safe route."; return false;
        }
        if (serverApi.World.ElapsedMilliseconds >= cart.NextMiningDiscovery)
        { cart.NextMiningDiscovery = serverApi.World.ElapsedMilliseconds + 5000; DiscoverMiningOre(fox, cart); }
        if(PlanMiningVeinAccess(fox,cart))return false;
        foreach (var ore in m.Vein.ToArray())
        {
            var p = ore.Position(pos.dimension);
            if (!MiningOre(serverApi.World.BlockAccessor.GetBlock(p))) { m.Vein.Remove(ore); continue; }
            if (TryMiningTarget(fox, cart, ore, "extracting", out job)) return true;
        }
        foreach (var shell in m.Shell.OrderByDescending(p => p.Y).ToArray())
        {
            Block b = serverApi.World.BlockAccessor.GetBlock(shell.Position(pos.dimension));
            if (b.Id == 0 || m.Blocked.Contains(shell)) { m.Shell.Remove(shell); continue; }
            if (TryMiningTarget(fox, cart, shell, "checking vein surroundings", out job)) return true;
        }
        var entries = m.ProbeEntries();
        var stations = entries.Select(e => e.Point).ToList();
        int budget = 8;
        while (m.Cursor < stations.Count && budget-- > 0)
        {
            var station = stations[m.Cursor];
            bool alongX = entries[m.Cursor].AlongX;
            var cuts = WorkCartMiningState.Passage(station, alongX).ToArray();
            // Bound the complete passage, not just its anchor. A clipped edge
            // is the end of that branch, not an unreachable work face to retry.
            if (cuts.Any(p => !MiningBounds(fox, cart, p)))
            {
                int branch = entries[m.Cursor].Branch;
                m.Cursor++;
                if (branch > 0) while (m.Cursor < entries.Count && entries[m.Cursor].Branch == branch) m.Cursor++;
                continue;
            }
            if (cuts.Any(p => m.Blocked.Contains(p))) { SkipMiningBranch(m, stations); continue; }
            if (cuts.All(p => MiningPassageClear(cart,p)))
            {
                // Retain a supported floor, patching gaps only when necessary.
                var floor = station.Add(0, -1, 0);
                if (!serverApi.World.BlockAccessor.GetBlock(floor.Position(pos.dimension)).SideSolid[BlockFacing.UP.Index]
                    || !serverApi.World.BlockAccessor.GetBlock(floor.Add(alongX ? 0 : 1, 0, alongX ? 1 : 0).Position(pos.dimension)).SideSolid[BlockFacing.UP.Index])
                {
                    if (PlanMiningFloor(fox, cart, floor, MiningHostRock(cart, floor.Position(pos.dimension)), alongX))
                    { m.Status = "Preparing a supported crossing before advancing."; return false; }
                    m.Status = "Probe branch blocked by an opening or unsafe floor."; m.Blocked.Add(station); SkipMiningBranch(m, stations); continue;
                }
                if(m.Cursor>0&&entries[m.Cursor].Descending && !m.Supports.Any(s=>s.Point==station&&s.Code.Contains("stairs")))
                {
                    var previous=stations[m.Cursor-1];int dx=Math.Sign(station.X-previous.X),dz=Math.Sign(station.Z-previous.Z);
                    if(PlanMiningStep(fox,cart,station,alongX,dx,dz))return false;
                    m.Status="Descending probe needs a safe two-wide supported stair; check rocks, ore or access.";return false;
                }
                m.Cursor++; continue;
            }
            foreach (var cut in cuts)
                if (!MiningPassageClear(cart,cut) && TryMiningTarget(fox, cart, cut, "probing", out job)) return true;
            m.Status = "Probe waits for a reachable face, usable pickaxe or safe access.";
            return TryJoinCartJob(fox, cart, out job);
        }
        if (m.Cursor >= stations.Count)
        { m.Mode = m.Blocked.Count > 0 ? "blocked" : "complete"; m.Status = m.Blocked.Count > 0 ? "Probe route blocked. Check access, then pause/resume to retry. Reachable ore discovery remains active." : "Probe plan finished within the Work Cart area. Reachable ore discovery remains active; range upgrades may add probes."; }
        cart.MarkDirty(true); return TryJoinCartJob(fox, cart, out job);
    }

    private static void SkipMiningBranch(WorkCartMiningState m, List<QuarryPoint> stations)
    {
        // A blocked main/descending passage cannot be tunneled around by
        // secretly selecting another face. A blocked side branch skips only
        // its remaining stations and leaves the other branches available.
        var entries=m.ProbeEntries();int branch=entries[m.Cursor].Branch;m.Cursor++;
        if(branch==0){m.Cursor=stations.Count;return;}
        while(m.Cursor<entries.Count&&entries[m.Cursor].Branch==branch)m.Cursor++;
    }

    private bool TryMiningTarget(Entity fox, BlockEntityFeralKinshipWorkCart cart, QuarryPoint point,
        string mode, out WorkCartQuarryJob? job)
    {
        job = null;
        if (cart.QuarryJobs.Any(j => j.Target.X == point.X && j.Target.Y == point.Y && j.Target.Z == point.Z)) return false;
        if (cart.MiningReach.Count == 0 || cart.Mining.Blocked.Contains(point) || cart.QuarryFailedTargets.GetValueOrDefault(point) > serverApi!.World.ElapsedMilliseconds) return false;
        var target = point.Position(cart.Pos.dimension);
        Block block = serverApi!.World.BlockAccessor.GetBlock(target);
        if (!MiningCutSafe(fox, cart, target, false))
        { cart.Mining.Blocked.Add(point); cart.Mining.Status = "Working around a cave, liquid, structure or protected access."; return false; }
        if (!TryMiningApproach(fox, cart, target, false, out var approach))
        {
            // The two-block exception may reveal ore behind one intervening
            // stone. Dig only a visible, locally reachable intervening block.
            if (MiningOre(block) && WorkCartMiningState.LocalSense(fox.Pos.XYZ, point, target.dimension))
            {
                foreach (var face in BlockFacing.ALLFACES)
                {
                    var near = target.AddCopy(face); Block b = serverApi.World.BlockAccessor.GetBlock(near);
                    if (QuarryRockType(b).Length == 0 || !MiningCutSafe(fox, cart, near, false)
                        || !TryMiningApproach(fox, cart, near, false, out approach)) continue;
                    return CreateMiningPrimary(fox, cart, near, approach!, QuarryJobKind.Mine,
                        MiningHostRock(cart, near), "", out job, "opening sensed ore");
                }
            }
            return false;
        }
        // Do not remove a floor until matching replacement materials and a
        // neighboring standing position exist. The hole is repaired before
        // another cut; its drops must be collected before construction.
        bool floor = cart.MiningReach.Any(p => p.X == point.X && p.Z == point.Z && p.Y == point.Y + 1)
            && !serverApi.World.BlockAccessor.GetBlock(target.DownCopy()).SideSolid[BlockFacing.UP.Index];
        string rock = MiningHostRock(cart, target);
        if (floor && (!QuarryBufferHas(cart, rock, 8) || approach!.Y <= target.InternalY + 1 &&
            (int)Math.Floor(approach.X) == target.X && (int)Math.Floor(approach.Z) == target.Z))
        { cart.Quarry.PreferredRock = rock; cart.Mining.Status = "Need matching rocks and a safe neighboring position before removing the floor."; return false; }
        return CreateMiningPrimary(fox, cart, target, approach!, QuarryJobKind.Mine, rock, "", out job, mode);
    }

    private bool CreateMiningPrimary(Entity fox, BlockEntityFeralKinshipWorkCart cart, BlockPos target,
        Vec3d approach, QuarryJobKind kind, string rock, string buildCode, out WorkCartQuarryJob? job, string mode = "building access")
    {
        job = null;
        if (cart.QuarryJobs.Count >= 4 || cart.QuarryJobs.Any(j => j.Target.Equals(target)
            || kind == QuarryJobKind.Mine && (int)Math.Floor(j.Approach.X) == target.X
                && (int)Math.Floor(j.Approach.Z) == target.Z && (int)Math.Floor(j.Approach.Y) - 1 == target.InternalY)) return false;
        job = new() { Kind = kind, Cart = cart, Target = target, Approach = approach,
            OriginalBlockId = serverApi!.World.BlockAccessor.GetBlock(target).Id, Rock = rock, BuildCode = buildCode };
        if (job.OriginalBlockId != 0)
        {
            job.ToolIndex = cart.Tools.Reserve(job, serverApi.World.BlockAccessor.GetBlock(target).RequiredMiningTier);
            if (job.ToolIndex < 0) { job = null; cart.Mining.Status = "Supply an unlocked pickaxe of the required tier."; return false; }
        }
        cart.Mining.Mode = mode; cart.Mining.Status = $"{mode}: {target.X}, {target.Y}, {target.Z}";
        if (rock.Length > 0) cart.Quarry.PreferredRock = rock;
        job.Workers[fox.EntityId] = serverApi.World.ElapsedMilliseconds; cart.QuarryJobs.Add(job);
        cart.MarkDirty(true); return true;
    }

    private bool MiningPlacementClear(Entity worker, BlockPos target)
    {
        var world = serverApi!.World;
        var center = target.ToVec3d().Add(.5, .5, .5);
        // Loose drops are moved aside at completion. They must not prevent
        // starting construction when a worker cannot climb into their hole.
        return !world.GetEntitiesAround(center, 2, 3, e => e.Alive && e is not EntityItem
            && e.CollisionBox != null && e.CollisionBox.ToDouble().Translate(e.Pos.XYZ)
                .Intersects(new Cuboidd(target.X, target.InternalY, target.Z, target.X + 1, target.InternalY + 1, target.Z + 1))).Any();
    }

    private bool AdvanceMiningWork(Entity fox, WorkCartQuarryJob job, float dt)
    {
        var cart = job.Cart; var m = cart.Mining;
        if (!m.Enabled || !cart.QuarryJobs.Contains(job) || job.Done
            || fox.Pos.SquareDistanceTo(job.Target.ToVec3d().Add(.5, .5, .5)) > (job.Kind == QuarryJobKind.Build ? 42.25 : 16) || !MiningCutSafe(fox, cart, job.Target, job.Kind == QuarryJobKind.Build)) return false;
        var world = serverApi!.World;
        if (job.Kind == QuarryJobKind.Mine && !MiningLineClear(fox.Pos.XYZ.AddCopy(0,.7,0),job.Target))
        {
            cart.QuarryFailedTargets[new(job.Target.X,job.Target.Y,job.Target.Z)] = world.ElapsedMilliseconds + 12000;
            FinishQuarryPrimary(cart, job); return false;
        }
        Block block = world.BlockAccessor.GetBlock(job.Target);
        if (block.Id != job.OriginalBlockId) { FinishQuarryPrimary(cart, job); return false; }
        var owner = world.PlayerByUid(packRepository!.GetWorkCart(cart.Pos)!.OwnerUid);
        if (owner == null) return false;
        ItemSlot? pick = job.ToolIndex >= 0 && cart.Tools.Owns(job.ToolIndex, job) ? cart.Tools[job.ToolIndex] : null;
        if (block.Id != 0 && (pick == null || !cart.Tools.IsUsableTool(pick, block.RequiredMiningTier)))
        { FinishQuarryPrimary(cart, job); return false; }
        var selection = new BlockSelection { Position = job.Target, Block = block, Face = BlockFacing.UP };
        double speed = block.Id == 0 ? 1 : pick!.Itemstack!.Collectible.GetMiningSpeed(pick.Itemstack, selection, block, owner);
        job.Work += Math.Clamp(dt, 0, .25f) * Math.Max(.05, speed);
        if (job.Work < (block.Id == 0 ? .8 : Math.Max(.15, block.GetResistance(world.BlockAccessor, job.Target)))) return true;
        var point = new QuarryPoint(job.Target.X, job.Target.Y, job.Target.Z);
        bool wasFloor = cart.MiningReach.Any(p => p.X == point.X && p.Z == point.Z && p.Y == point.Y + 1)
            && !world.BlockAccessor.GetBlock(job.Target.DownCopy()).SideSolid[BlockFacing.UP.Index];
        if (job.Kind == QuarryJobKind.Build)
        {
            Block? desired = world.GetBlock(new AssetLocation(job.BuildCode));
            if (desired == null || desired.Id == 0 || !QuarryBufferHas(cart, job.Rock, 8)
                || !PrepareCartPlacement(fox, cart, job.Target)) { FinishQuarryPrimary(cart, job); return false; }
            cart.Tools[4].TakeOut(8); cart.Tools[4].MarkDirty();
            world.BlockAccessor.SetBlock(desired.Id, job.Target); SyncCartWorkBlock(job.Target);
            cart.Quarry.RocksSpent += 8; m.Supports.First(p => p.Point == point).Placed = true;
        }
        else
        {
            if (!MiningLineClear(fox.Pos.XYZ.AddCopy(0, .7, 0), job.Target)) { FinishQuarryPrimary(cart, job); return false; }
            if (MiningOre(block)) ReportCartFinding(cart, job.Target, "Ore recovered");
            BreakCartWorkBlock(fox, pick!, selection);
            SyncCartWorkBlock(job.Target);
            if (world.BlockAccessor.GetBlock(job.Target).Id == block.Id) { FinishQuarryPrimary(cart, job); return false; }
            m.Mined++; m.Opened.Add(point); m.Shell.Remove(point);
            if (MiningOre(block)) m.CompletedOre(point);
            if (wasFloor)
            {
                // No permanent lining: one two-wide platform only where an
                // actual floor was excavated. Natural solid supports remain.
                m.Supports.Add(new() { Point = point, Rock = job.Rock, Code = "game:cobblestone-" + job.Rock });
                PlanMiningFloor(fox, cart, point, job.Rock);
            }
        }
        cart.NextMiningSurvey = 0; cart.NextMiningDiscovery = 0;
        RecordWellbeingDuty(fox, .1, label: "Mining");
        FinishQuarryPrimary(cart, job); cart.MarkDirty(true); return false;
    }
}
