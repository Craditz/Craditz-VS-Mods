#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

internal readonly record struct QuarryPoint(int X, int Y, int Z)
{
    internal BlockPos Position(int dimension) => new(X, Y, Z, dimension);
    internal QuarryPoint Add(int x, int y, int z) => new(X + x, Y + y, Z + z);
}

internal sealed class QuarryStructureBlock
{
    public QuarryPoint Point { get; set; }
    public string Code { get; set; } = "";
    public string Rock { get; set; } = "";
    public int Level { get; set; }
    public bool Placed { get; set; }
    public bool DeferredFoundation { get; set; }
}

/// <summary>Cart-local save payload; no companion ledger/schema changes.</summary>
internal sealed class WorkCartQuarryState
{
    public int Version { get; set; } = 1;
    public int Width { get; set; } = 21;
    public int Depth { get; set; } = 20;
    public bool Started { get; set; }
    public bool Enabled { get; set; }
    public int OriginX { get; set; }
    public int OriginZ { get; set; }
    public int SurfaceY { get; set; }
    public int Layer { get; set; }
    public int PlaneY { get; set; }
    public int SurveyIndex { get; set; }
    public int SurveyTop { get; set; }
    public int CompletedCutY { get; set; } = int.MaxValue;
    public string Stage { get; set; } = "ready";
    public string Status { get; set; } = "Ready — quarry extends south of this cart.";
    public string PreferredRock { get; set; } = "";
    public int MinedBlocks { get; set; }
    public int RecoveredBlocks { get; set; }
    public int RocksSpent { get; set; }
    public int StairLevel { get; set; }
    public int HeadingX { get; set; } = -1;
    public int HeadingZ { get; set; }
    public QuarryPoint StairA { get; set; }
    public QuarryPoint StairB { get; set; }
    public List<QuarryPoint> Pending { get; set; } = new();
    public List<QuarryPoint> CleanupPending { get; set; } = new();
    public HashSet<string> ProtectedColumns { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> ProtectedFromY { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> ProtectedReasons { get; set; } = new(StringComparer.Ordinal);
    public HashSet<QuarryPoint> Excavated { get; set; } = new();
    public List<QuarryStructureBlock> Structure { get; set; } = new();
    public Dictionary<string, int> RecoveryProgress { get; set; } = new(StringComparer.Ordinal);
    internal string? UnreadablePayload;

    internal int MinX => OriginX - Width / 2;
    internal int MaxX => OriginX + Width / 2;
    internal int MinZ => OriginZ + 3;
    internal int MaxZ => MinZ + Width - 1;
    internal bool Contains(int x, int z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
    internal bool IsProtected(QuarryPoint p) => ProtectedColumns.Contains(ColumnKey(p.X, p.Z))
        && p.Y <= ProtectedFromY.GetValueOrDefault(ColumnKey(p.X, p.Z), int.MaxValue);
    internal static string ColumnKey(int x, int z) => x + "," + z;
    internal QuarryPoint PlaneCell(int index) => new(MinX + (index / Width % 2 == 0
        ? index % Width : Width - 1 - index % Width), PlaneY, MinZ + index / Width);
    internal bool IsStructure(QuarryPoint p) => Structure.Any(s => s.Point == p);
    internal bool WasExcavated(QuarryPoint p) => Excavated.Contains(p)
        || (Contains(p.X, p.Z) && p.Y >= CompletedCutY && p.Y <= SurveyTop && !IsProtected(p) && !IsEntrance(p));
    internal bool IsDescending => Stage is "stairs" or "excavating" or "complete";
    internal bool IsEntrance(QuarryPoint p) => IsDescending && p.Y <= SurfaceY
        && p.X >= OriginX && p.X <= OriginX + 1 && p.Z >= MinZ && p.Z <= MinZ + 1;

    internal void Start(BlockPos cart)
    {
        if (Started) { Enabled = Stage != "complete"; return; }
        Started = Enabled = true;
        OriginX = cart.X; OriginZ = cart.Z; SurfaceY = cart.Y - 1;
        SurveyTop = SurfaceY + 1; SurveyIndex = 0; Stage = "survey";
        StairA = new(cart.X, SurfaceY, MinZ);
        StairB = StairA.Add(0, 0, 1);
        Status = "Surveying the south quarry footprint.";
    }

    internal void Protect(QuarryPoint p, string reason)
    {
        string key = ColumnKey(p.X, p.Z);
        ProtectedColumns.Add(key);
        ProtectedFromY[key] = Math.Min(ProtectedFromY.GetValueOrDefault(key, int.MaxValue), p.Y);
        ProtectedReasons[key] = reason;
        Pending.RemoveAll(q => q.X == p.X && q.Z == p.Z);
        Status = reason;
    }

    internal void QueueRemnant(QuarryPoint point)
    {
        if (Contains(point.X, point.Z) && CleanupPending.Count < 4096 && !CleanupPending.Contains(point)) CleanupPending.Add(point);
    }

    internal bool WillRecover(string rock) => RecoveryProgress.GetValueOrDefault(rock) == 6;
    internal void CountStone(string rock)
    {
        int value = RecoveryProgress.GetValueOrDefault(rock) + 1;
        if (value == 7) { value = 0; RecoveredBlocks++; }
        RecoveryProgress[rock] = value;
    }

    internal string Save()
    {
        if (UnreadablePayload != null) return UnreadablePayload;
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(JsonSerializer.SerializeToUtf8Bytes(this));
        return "gz1:" + Convert.ToBase64String(output.ToArray());
    }
    internal static WorkCartQuarryState Load(string json)
    {
        if (string.IsNullOrEmpty(json)) return new();
        try
        {
            string decoded = json;
            if (json.StartsWith("gz1:", StringComparison.Ordinal))
            {
                using var input = new MemoryStream(Convert.FromBase64String(json[4..]));
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                byte[] buffer = new byte[4096]; int n;
                while ((n = gzip.Read(buffer)) > 0)
                { if (output.Length + n > 1024 * 1024) throw new InvalidDataException(); output.Write(buffer, 0, n); }
                decoded = Encoding.UTF8.GetString(output.ToArray());
            }
            var s = JsonSerializer.Deserialize<WorkCartQuarryState>(decoded);
            if (s == null || s.Version != 1 || s.Width < 7 || s.Width > 21 || s.Width % 2 == 0
                || s.Depth < 1 || s.Depth > 40 || s.Layer < 0 || s.Layer > s.Depth
                || s.Pending == null || s.Structure == null || s.RecoveryProgress == null
                || s.ProtectedColumns == null || s.Excavated == null
                || s.RecoveryProgress.Values.Any(v => v < 0 || v > 6)) throw new JsonException();
            s.ProtectedReasons ??= new(StringComparer.Ordinal);
            s.ProtectedFromY ??= new(StringComparer.Ordinal);
            s.CleanupPending = (s.CleanupPending ?? new()).Take(4096).Distinct().ToList();
            return s;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or IOException or FormatException)
        {
            return new() { Enabled = false, UnreadablePayload = json,
                Status = "Quarry data could not be read; retained unchanged. Work is stopped." };
        }
    }

    // Two adjacent stair lanes. Turning may use their inner lane connection;
    // candidates remain in a bounded wall band, so obstructions produce local
    // detours rather than diagonal shafts across the quarry.
    internal IEnumerable<(QuarryPoint A, QuarryPoint B, int Dx, int Dz)> StairCandidates()
    {
        int y = SurfaceY - Layer + 1;
        (int X, int Z)[] directions = { (HeadingX, HeadingZ), (-HeadingZ, HeadingX),
            (HeadingZ, -HeadingX), (-HeadingX, -HeadingZ) };
        var seen = new HashSet<(QuarryPoint, QuarryPoint)>();
        foreach (var d in directions)
        foreach (QuarryPoint previous in new[] { StairA, StairB })
        foreach (int sign in new[] { 1, -1 })
        {
            var a = new QuarryPoint(previous.X + d.X, y, previous.Z + d.Z);
            var b = a.Add(-d.Z * sign, 0, d.X * sign);
            if (!Contains(a.X, a.Z) || !Contains(b.X, b.Z)
                || Math.Min(Math.Min(a.X - MinX, MaxX - a.X), Math.Min(a.Z - MinZ, MaxZ - a.Z)) > 4
                || Math.Min(Math.Min(b.X - MinX, MaxX - b.X), Math.Min(b.Z - MinZ, MaxZ - b.Z)) > 4
                || a.X == StairA.X && a.Z == StairA.Z || a.X == StairB.X && a.Z == StairB.Z
                || b.X == StairA.X && b.Z == StairA.Z || b.X == StairB.X && b.Z == StairB.Z
                || !seen.Add((a, b))) continue;
            yield return (a, b, d.X, d.Z);
        }
    }

    internal static List<QuarryStructureBlock> StepBlocks(QuarryPoint a, QuarryPoint b,
        int dx, int dz, string rock, int level)
    {
        string facing = dx < 0 ? "east" : dx > 0 ? "west" : dz < 0 ? "south" : "north";
        var result = new List<QuarryStructureBlock>();
        foreach (QuarryPoint top in new[] { a, b })
        {
            for (int below = 3; below >= 0; below--)
                result.Add(new() { Point = top.Add(0, -below, 0), Rock = rock, Level = level,
                    Code = below == 0 ? $"game:cobblestonestairs-{rock}-up-{facing}-free" : $"game:cobblestone-{rock}" });
        }
        return result;
    }
}

internal enum QuarryJobKind { Mine, Build, Collect, Withdraw, TakeBuffer, Deliver, WithdrawTool, DeliverTool }
internal sealed class WorkCartQuarryJob
{
    internal QuarryJobKind Kind;
    internal BlockEntityFeralKinshipWorkCart Cart = null!;
    internal BlockPos Target = null!;
    internal Vec3d Approach = new();
    internal int OriginalBlockId;
    internal int ToolIndex = -1;
    internal string Rock = "";
    internal string BuildCode = "";
    internal long ItemEntityId;
    internal int SourceSlot = -1;
    internal bool BufferDelivery;
    internal bool IsRemnant;
    internal bool Done;
    internal double Work;
    internal readonly Dictionary<long, long> Workers = new();
}
