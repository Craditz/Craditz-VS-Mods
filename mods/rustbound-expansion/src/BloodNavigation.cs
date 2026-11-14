using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Essentials;

namespace RustboundExpansion;

// Search uses the game's worker queue. This follower keeps vertical waypoints
// and validates real footing instead of steering at unsupported flank points.
internal sealed class BloodNavigation(BloodGoober entity,Action<PathfinderTask>? submit=null)
{
    private readonly CollisionTester collision=new();
    private readonly List<Vec3d> route=[];
    private List<Vec3d> goals=[];
    private Entity? target;
    private Vec3d anchor=new(),requestGoal=new(),lastProgress=new();
    private int dimension=-1,generation,requestGeneration,goalIndex,index;
    private long nextSearch,progressAt;
    internal PathfinderTask? Pending {get;private set;}
    internal IReadOnlyList<Vec3d> Route=>route;
    internal Vec3d? Waypoint=>index<route.Count?route[index]:null;
    // Route over ordinary single steps even though physical clambering retains
    // its two-block allowance. Never plan a shortcut that depends on that spare ability.
    private const float PathStepHeight=1.1251f;
    private Cuboidf Box=>entity.CollisionBox??new(-.35f,0,-.35f,.35f,.95f,.35f);
    private long Now=>entity.World.ElapsedMilliseconds;
    internal static Vec3d BodyPoint(Entity e)=>SpellTargeting.BodyCenter(e).AddCopy(0,e.Pos.DimensionYAdjustment,0);

    internal void Stop()
    {
        generation++;target=null;Pending=null;route.Clear();goals.Clear();index=0;
        entity.Controls.WalkVector.Set(0,0,0);entity.Controls.Forward=false;
    }
    internal void Tick(Entity desired,bool combat,IReadOnlyList<BloodGoober> siblings)
    {
        if(!desired.Alive || desired.ShouldDespawn || desired.Pos.Dimension!=entity.Pos.Dimension)
        {Stop();return;}
        if(target!=desired || dimension!=entity.Pos.Dimension)
        {
            generation++;target=desired;dimension=entity.Pos.Dimension;anchor=desired.Pos.XYZ;
            route.Clear();index=goalIndex=0;goals=GroundGoals(desired,combat);
            lastProgress=entity.Pos.XYZ;progressAt=Now;
        }
        else if(anchor.SquareDistanceTo(desired.Pos.XYZ)>1 && Pending==null && Now>=nextSearch)
        {
            // Keep walking the last valid route while a moving target's update
            // is searched. Repeated small movements must not starve pursuit.
            generation++;anchor=desired.Pos.XYZ;goals=GroundGoals(desired,combat);goalIndex=0;
            RequestPath(desired,combat);
        }
        if(Pending is {Finished:true} completed)
        {
            Pending=null;
            if(requestGeneration==generation && completed.waypoints!=null)
            {
                var validated=ValidateRoute(completed.waypoints,requestGoal);
                if(validated!=null){route.Clear();route.AddRange(validated);index=0;lastProgress=entity.Pos.XYZ;progressAt=Now;}
                else goalIndex++;
            }
            else if(requestGeneration==generation)goalIndex++;
        }
        while(index<route.Count && Reached(entity.Pos.XYZ,route[index]))index++;
        if(index<route.Count)
        {
            if(!ClearEdge(entity.Pos.XYZ,route[index])){route.Clear();index=0;goalIndex++;}
            else
            {
                if(lastProgress.SquareDistanceTo(entity.Pos.XYZ)>.04){lastProgress=entity.Pos.XYZ;progressAt=Now;}
                if(Now-progressAt<1800)
                {
                    // Keep precision on ledges; separate at the final, level approach.
                    var avoid=index==route.Count-1 && Math.Abs(route[index].Y-entity.Pos.InternalY)<.3?siblings:null;
                    entity.Steer(route[index],.08,avoid);return;
                }
                route.Clear();index=0;goalIndex++;
            }
        }
        entity.Controls.WalkVector.Set(0,0,0);entity.Controls.Forward=false;
        if(goals.Count>0 && goals.Any(g=>Reached(entity.Pos.XYZ,g)))return;
        RequestPath(desired,combat);
    }
    private void RequestPath(Entity desired,bool combat)
    {
        if(Pending!=null || Now<nextSearch)return;
        // Re-evaluate terrain when cycling goals: player edits and moving blocks
        // must not leave a permanently impossible destination cached.
        if(goals.Count==0 || goalIndex>=goals.Count){goals=GroundGoals(desired,combat);goalIndex=0;}
        nextSearch=Now+650+entity.HostIndex*70;
        if(goals.Count==0)return;
        requestGoal=goals[goalIndex].Clone();requestGeneration=generation;
        var end=new BlockPos((int)Math.Floor(requestGoal.X),(int)Math.Floor(requestGoal.Y-entity.Pos.DimensionYAdjustment),
            (int)Math.Floor(requestGoal.Z),entity.Pos.Dimension);
        var task=new PathfinderTask(entity.Pos.AsBlockPos,end,-1,3,PathStepHeight,Box.Clone(),2048,1);
        if(submit!=null){Pending=task;submit(task);}
        else if(entity.World.Api?.ModLoader.GetModSystem<PathfindingAsync>() is {} service)
        {Pending=task;service.EnqueuePathfinderTask(task);}
    }
    internal static bool Reached(Vec3d from,Vec3d to)
        =>Math.Abs(from.Y-to.Y)<.35 && Math.Pow(from.X-to.X,2)+Math.Pow(from.Z-to.Z,2)<.12*.12;

    internal List<Vec3d> GroundGoals(Entity desired,bool combat)
    {
        var center=desired.Pos.XYZ;var preferred=entity.Approach(desired,combat?1.1:1.6);
        var result=new List<Vec3d>();int x0=(int)Math.Floor(center.X),z0=(int)Math.Floor(center.Z);
        for(int x=x0-2;x<=x0+2;x++)for(int z=z0-2;z<=z0+2;z++)
            foreach(var point in StandPoints(x,z,center.Y-2.1,center.Y+.4))
                if(!combat || point.SquareDistanceTo(center)<=1.95*1.95 &&
                    SpellTargeting.HasLineOfSight(entity.World,point.AddCopy(0,Box.Y2*.5,0),BodyPoint(desired)))result.Add(point);
        return result.OrderBy(p=>p.SquareDistanceTo(preferred)).ThenBy(p=>p.SquareDistanceTo(entity.Pos.XYZ)).Take(8).ToList();
    }
    private IEnumerable<Vec3d> StandPoints(int x,int z,double min,double max)
    {
        int offset=entity.Pos.DimensionYAdjustment;
        for(int y=(int)Math.Floor(max-offset);y>=(int)Math.Floor(min-offset)-1;y--)
        {
            var pos=new BlockPos(x,y,z,entity.Pos.Dimension);var block=entity.World.BlockAccessor.GetBlock(pos);
            if(block==null || !block.CanStep)continue;
            foreach(var box in block.GetCollisionBoxes(entity.World.BlockAccessor,pos)??[])
            {
                if(box.X1>.5 || box.X2<.5 || box.Z1>.5 || box.Z2<.5)continue;
                var point=new Vec3d(x+.5,y+offset+box.Y2+.001,z+.5);
                if(point.Y>=min && point.Y<=max && Clear(point))yield return point;
            }
        }
    }
    private bool Clear(Vec3d point)=>!collision.IsColliding(entity.World.BlockAccessor,Box,point,false);
    private bool ClearLine(Vec3d from,Vec3d to)
    {
        int steps=Math.Max(1,(int)Math.Ceiling(from.DistanceTo(to)/.15));
        for(int i=1;i<=steps;i++)if(!Clear(from+(to-from)*(i/(double)steps)))return false;
        return true;
    }
    internal bool ClearEdge(Vec3d from,Vec3d to)
    {
        if(Math.Abs(from.X-to.X)>2.5 || Math.Abs(from.Z-to.Z)>2.5 || to.Y-from.Y>PathStepHeight+.02 || from.Y-to.Y>3.05)return false;
        if(to.Y>from.Y+.05)
        {
            var raised=new Vec3d(from.X,to.Y,from.Z);
            return ClearLine(from,raised) && ClearLine(raised,to);
        }
        var over=new Vec3d(to.X,from.Y,to.Z);
        return ClearLine(from,over) && ClearLine(over,to);
    }
    internal List<Vec3d>? ValidateRoute(List<Vec3d> native,Vec3d goal)
    {
        if(native.Count>128)return null;
        var result=new List<Vec3d>();var previous=entity.Pos.XYZ;
        foreach(var raw in native)
        {
            // Resolve native intermediate nodes to supported tops without
            // expanding the planner's single-step limit to physical clamber height.
            var point=StandPoints((int)Math.Floor(raw.X),(int)Math.Floor(raw.Z),raw.Y-.05,raw.Y+PathStepHeight)
                .OrderBy(p=>Math.Abs(p.Y-raw.Y)).FirstOrDefault();
            if(point==null || !Bridge(previous,point,result))return null;
            if(!Reached(previous,point)){result.Add(point);previous=point;}
        }
        if(!Reached(previous,goal))
        {if(!Bridge(previous,goal,result))return null;result.Add(goal);}
        return result;
    }
    private bool Bridge(Vec3d from,Vec3d to,List<Vec3d> result)
    {
        if(ClearEdge(from,to))return true;
        // Native ToWaypoints skips its first node. Restore a safe intermediate
        // step when that shortcut would cut a corner or skip a staircase tread.
        int x0=(int)Math.Floor(from.X),z0=(int)Math.Floor(from.Z);
        var candidates=new List<Vec3d>();
        for(int x=x0-1;x<=x0+1;x++)for(int z=z0-1;z<=z0+1;z++)
            candidates.AddRange(StandPoints(x,z,from.Y-3,from.Y+PathStepHeight));
        var middle=candidates.Where(p=>!Reached(from,p) && ClearEdge(from,p) && ClearEdge(p,to))
            .OrderBy(p=>from.DistanceTo(p)+p.DistanceTo(to)).FirstOrDefault();
        if(middle==null)return false;
        result.Add(middle);return true;
    }
}
