#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;
public sealed partial class FeralKinshipCompanionSystem
{
    internal static bool MiningStair(Block b) => b.Code?.Domain == "game"
        && b.Code.Path.StartsWith("cobblestonestairs-",StringComparison.Ordinal);

    private bool AlignMiningStandingPoint(Entity fox,Vec3d point,BlockPos feet)
    {
        var blocks=serverApi!.World.BlockAccessor;var floor=feet.DownCopy();Block b=blocks.GetBlock(floor);
        if(!MiningStair(b))return true;
        var boxes=b.GetCollisionBoxes(blocks,floor);
        if(boxes==null)return false;
        var body=(fox.CollisionBox??fox.Properties.SpawnCollisionBox).Clone();body.Y2=Math.Max(body.Y2,2);
        foreach(var box in boxes.OrderByDescending(b=>b.Y2))
        {
            double[] xs={(box.X1+box.X2)/2,box.X1+.05,box.X2-.05};
            double[] zs={(box.Z1+box.Z2)/2,box.Z1+.05,box.Z2-.05};
            foreach(var candidate in xs.SelectMany(x=>zs.Select(z=>new Vec3d(floor.X+x,floor.InternalY+box.Y2,floor.Z+z)))
                .OrderBy(p=>p.SquareDistanceTo(point)))
            {
                if(serverApi.World.CollisionTester.IsColliding(blocks,body,candidate,false))continue;
                point.Set(candidate);return true;
            }
        }
        return false;
    }

    private bool MiningPassageClear(BlockEntityFeralKinshipWorkCart cart,QuarryPoint p)
        => MiningAir(p.Position(cart.Pos.dimension)) || cart.Mining.Supports.Any(s=>s.Placed&&s.Point==p
            && s.Code.Contains("stairs",StringComparison.Ordinal)
            && serverApi!.World.BlockAccessor.GetBlock(p.Position(cart.Pos.dimension)).Code?.ToString()==s.Code);

    private bool PlanMiningStep(Entity fox,BlockEntityFeralKinshipWorkCart cart,QuarryPoint a,
        bool alongX,int dx,int dz)
    {
        var b=a.Add(alongX?0:1,0,alongX?1:0);var blocks=serverApi!.World.BlockAccessor;
        if(cart.Mining.Supports.Any(s=>s.Point==a||s.Point==b))return false;
        string rock=MiningHostRock(cart,a.Position(cart.Pos.dimension));if(rock.Length==0)return false;
        string facing=dx<0?"east":dx>0?"west":dz<0?"south":"north";
        var parts=new List<QuarryStructureBlock>();
        foreach(var top in new[]{a,b})
        {
            // Excavation has to clear the headroom and ore first. Construction
            // never silently replaces a solid block or covers remaining ore.
            for(int h=0;h<=2;h++)if(!MiningAir(top.Add(0,h,0).Position(cart.Pos.dimension)))return false;
            for(int below=3;below>=0;below--)
            {
                var p=top.Add(0,-below,0);var pos=p.Position(cart.Pos.dimension);
                if(!MiningBounds(fox,cart,p)||blocks.GetChunkAtBlockPos(pos)==null
                    ||blocks.GetBlock(pos,BlockLayersAccess.Fluid).IsLiquid())return false;
                Block existing=blocks.GetBlock(pos);
                bool ownedSupport=cart.Mining.Supports.Any(s=>s.Placed&&s.Point==p&&s.Code==existing.Code?.ToString());
                if(below>0&&existing.SideSolid[BlockFacing.UP.Index]&&(QuarryRockType(existing).Length>0||ownedSupport))continue;
                if(existing.Id!=0||!MiningCutSafe(fox,cart,pos,true))return false;
                string code=below==0?$"game:cobblestonestairs-{rock}-up-{facing}-free":$"game:cobblestone-{rock}";
                if(serverApi.World.GetBlock(new AssetLocation(code)) is not Block desired||desired.Id==0)return false;
                parts.Add(new(){Point=p,Rock=rock,Code=code,Level=cart.Mining.Cursor});
            }
        }
        cart.Mining.Supports.AddRange(parts);cart.Quarry.PreferredRock=rock;
        cart.Mining.Mode="building access";
        cart.Mining.Status="Building a needed two-wide stair; three solid supports beneath, 8 rocks per placed block.";
        cart.MarkDirty(true);return true;
    }

    private bool PlanMiningVeinAccess(Entity fox,BlockEntityFeralKinshipWorkCart cart)
    {
        var m=cart.Mining;
        foreach(var p in m.Opened.Where(p=>p.Y<cart.Pos.Y&&!m.Supports.Any(s=>s.Point==p))
            .OrderBy(p=>fox.Pos.SquareDistanceTo(p.Position(cart.Pos.dimension).ToVec3d())).Take(32))
        {
            if(!m.Vein.Any(v=>v.Y<=p.Y)&&!m.Shell.Any(v=>v.Y<p.Y))continue;
            if(!MiningAir(p.Position(cart.Pos.dimension)))continue;
            foreach(var face in BlockFacing.HORIZONTALS)
            {
                var upper=p.Add(-face.Normali.X,1,-face.Normali.Z);
                if(!cart.MiningReach.Contains(upper))continue;
                if(PlanMiningStep(fox,cart,p,face.Normali.X!=0,face.Normali.X,face.Normali.Z))return true;
            }
        }
        return false;
    }

    // Voxel traversal uses actual collision boxes inside partial stair blocks.
    // This permits sight through their empty half without allowing a ray to
    // cross a solid cube or the filled half of the stair.
    private bool MiningSightCellClear(Vec3d from,Vec3d to,BlockPos p)
    {
        var blocks=serverApi!.World.BlockAccessor;
        if(blocks.GetChunkAtBlockPos(p)==null)return false;
        Block b=blocks.GetBlock(p);if(b.Id==0)return true;
        var boxes=b.GetCollisionBoxes(blocks,p);
        if(boxes==null||boxes.Length==0)return true;
        foreach(var box in boxes)
        {
            double entry=0,exit=1;
            bool Axis(double start,double delta,double min,double max)
            {
                // Touching a face at the segment endpoint is not penetration.
                min+=.00001;max-=.00001;
                if(Math.Abs(delta)<1e-10)return start>=min&&start<=max;
                double a=(min-start)/delta,c=(max-start)/delta;
                if(a>c)(a,c)=(c,a);entry=Math.Max(entry,a);exit=Math.Min(exit,c);return entry<exit;
            }
            if(Axis(from.X,to.X-from.X,p.X+box.X1,p.X+box.X2)
                &&Axis(from.Y,to.Y-from.Y,p.InternalY+box.Y1,p.InternalY+box.Y2)
                &&Axis(from.Z,to.Z-from.Z,p.Z+box.Z1,p.Z+box.Z2)&&entry<exit)return false;
        }
        return true;
    }
}
