#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

/// <summary>The saved probe geometry never consults terrain or ore.</summary>
internal sealed partial class WorkCartMiningState
{
    public int Version { get; set; } = 3;
    public bool Started { get; set; }
    public bool Enabled { get; set; }
    public int Depth { get; set; } = 20;
    public int Length { get; set; } = 24;
    public int BranchLength { get; set; } = 12;
    public int Cursor { get; set; }
    public int Mined { get; set; }
    public int OreMined { get; set; }
    public string Status { get; set; } = "Ready: find reachable ore, then probe south.";
    public string Mode { get; set; } = "ready";
    public QuarryPoint Origin { get; set; }
    public List<QuarryPoint> Vein { get; set; } = new();
    public HashSet<QuarryPoint> Shell { get; set; } = new();
    public HashSet<QuarryPoint> Opened { get; set; } = new();
    public HashSet<QuarryPoint> Blocked { get; set; } = new();
    public List<QuarryStructureBlock> Supports { get; set; } = new();
    internal string? UnreadablePayload;

    internal void Start(BlockPos cart)
    {
        if (!Started) { Origin = new(cart.X, cart.Y, cart.Z + 2); Started = true; }
        Enabled = true;
        Status = "Looking for exposed reachable ore; otherwise probing within the Work Cart area.";
    }

    private List<(QuarryPoint Point, bool AlongX, int Branch)> LegacyProbeEntries()
    {
        var result=new List<(QuarryPoint,bool,int)>();var cursor=Origin;
        result.Add((cursor,false,0));
        (int X,int Z)[] headings={(0,1),(1,0),(0,-1),(-1,0)};
        int dx=0,dz=1;
        for(int i=0;i<Depth;i++)
        {
            var heading=headings[i/12%4];bool turning=i>0&&i%12==0;
            int stride=turning&&(heading.X>0||heading.Z>0)?2:1;
            cursor=cursor.Add(heading.X*stride,-1,heading.Z*stride);dx=heading.X;dz=heading.Z;
            result.Add((cursor,dx!=0,0));
        }
        var bottom=cursor;int branch=0;
        // Continue beyond the descent, never directly underneath its stairs.
        for(int i=1;i<=Length;i++)
        {
            var main=bottom.Add(dx*i,0,dz*i);result.Add((main,dx!=0,0));
            if(i%8!=0)continue;
            foreach(int sign in new[]{-1,1})
            {
                branch++;
                for(int j=1;j<=BranchLength;j++)result.Add((main.Add(-dz*sign*j,0,dx*sign*j),dz!=0,branch));
            }
        }
        return result;
    }
    internal List<QuarryPoint> ProbeStations()=>ProbeEntries().Select(s=>s.Point).ToList();
    internal bool ProbeAlongX(int cursor)=>ProbeEntries()[cursor].AlongX;

    internal static IEnumerable<QuarryPoint> Passage(QuarryPoint feet, bool alongX = false)
    {
        // Clear the upper blocks before the floor-level block; drops fall
        // onto the retained floor rather than into a newly opened shaft.
        for (int h = 2; h >= 0; h--)
            for (int lane = 0; lane < 2; lane++) yield return feet.Add(alongX ? 0 : lane, h, alongX ? lane : 0);
    }

    internal static bool LocalSense(Vec3d actualFeet, QuarryPoint ore, int dimension)
    {
        // Distance to the block volume, from the real animal's working body.
        double x = Math.Max(ore.X - actualFeet.X, Math.Max(0, actualFeet.X - ore.X - 1));
        double y = Math.Max(ore.Y + dimension * BlockPos.DimensionBoundary - (actualFeet.Y + .5),
            Math.Max(0, actualFeet.Y + .5 - ore.Y - dimension * BlockPos.DimensionBoundary - 1));
        double z = Math.Max(ore.Z - actualFeet.Z, Math.Max(0, actualFeet.Z - ore.Z - 1));
        return x * x + y * y + z * z <= 4;
    }

    internal void FoundOre(QuarryPoint p)
    {
        if (!Vein.Contains(p) && !Blocked.Contains(p)) Vein.Add(p);
    }

    internal void CompletedOre(QuarryPoint p)
    {
        Vein.Remove(p); OreMined++;
        // A one-block shell around ore only. Shell stone never grows a shell.
        for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
            if (x != 0 || y != 0 || z != 0) Shell.Add(p.Add(x, y, z));
    }

    internal string Save() => UnreadablePayload ?? JsonSerializer.Serialize(this);
    internal static WorkCartMiningState Load(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return new();
        try
        {
            if (payload.Length > 2 * 1024 * 1024) throw new JsonException();
            var s = JsonSerializer.Deserialize<WorkCartMiningState>(payload);
            if (s == null || s.Version is not (1 or 2 or 3) || s.Depth < 0 || s.Depth > 40 || s.Length < 8 || s.Length > 64
                || s.BranchLength < 1 || s.BranchLength > 24 || s.Cursor < 0 || !s.ValidProbePlan()
                || s.Cursor > s.ProbeStations().Count
                || s.Vein == null || s.Shell == null || s.Opened == null || s.Blocked == null || s.Supports == null)
                throw new JsonException();
            if(s.Version==1){s.Version=2;s.Cursor=0;s.Blocked.Clear();s.Status="Probe access plan upgraded; existing excavation, ore, tools and supports retained.";}
            return s;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        { return new() { UnreadablePayload = payload, Status = "Mining data unreadable; retained unchanged. Work stopped." }; }
    }
}
