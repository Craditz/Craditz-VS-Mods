#nullable enable
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Essentials;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>Only the neighbour test is inherited. Vanilla's search state is never used.</summary>
internal sealed class CompanionTerrainProbe : AStar
{
    internal CompanionTerrainProbe(ICoreServerAPI api) : base(api) { }
    internal void Begin(EnumAICreatureType type)
    {
        creatureType = type;
        centerOffsetX = centerOffsetZ = 0.5;
        blockAccess.Begin();
    }
    internal bool TryStep(PathNode node, Cardinal direction, Cuboidf box, float step, int fall, ref float cost) =>
        traversable(node, step, fall, box, direction, ref cost);
}

internal sealed class CompanionJourneyTerrain : ICompanionJourneyGraph
{
    private readonly ICoreServerAPI api;
    private readonly CompanionTerrainProbe probe;
    private readonly Cuboidf box;
    private readonly int dimension;
    private readonly float stepHeight;
    private readonly int maxFall;
    private readonly EnumAICreatureType creatureType;
    private readonly System.Func<BlockPos, JourneyDoor?> doorAt;
    private readonly Dictionary<JourneyCell, bool> loadedChunks = new();
    internal CompanionJourneyTerrain(ICoreServerAPI api, CompanionTerrainProbe probe, EntityAgent entity,
        float speed, EnumAICreatureType creatureType, System.Func<BlockPos, JourneyDoor?> doorAt)
    {
        this.api = api;
        this.probe = probe;
        box = entity.CollisionBox.Clone();
        dimension = entity.Pos.Dimension;
        stepHeight = entity.GetBehavior<EntityBehaviorControlledPhysics>()?.StepHeight ?? 0.6f;
        bool avoidFall = entity.Properties.FallDamage && entity.Properties.Attributes?["reckless"].AsBool(false) != true;
        maxFall = Math.Max(0, avoidFall ? 4 - (int)(speed * 30) : 12);
        this.creatureType = creatureType;
        this.doorAt = doorAt;
    }

    public int DirectionCount => Cardinal.ALL.Length;
    internal JourneyMovementProfile CacheProfile(string owner) => new(owner, dimension, (int)creatureType,
        box.X1, box.Y1, box.Z1, box.X2, box.Y2, box.Z2, stepHeight, maxFall);
    public bool TouchedUnloaded { get; private set; }
    public void BeginSlice() { loadedChunks.Clear(); probe.Begin(creatureType); }
    internal BlockPos Position(JourneyCell cell) => new(cell.X, cell.Y, cell.Z, dimension);
    internal Vec3d Waypoint(JourneyCell cell) => new(cell.X + 0.5, Position(cell).InternalY, cell.Z + 0.5);
    internal static JourneyCell Cell(BlockPos position) => new(position.X, position.Y, position.Z);

    private bool Loaded(JourneyCell cell)
    {
        const int chunkSize = 32;
        JourneyCell chunk = new((int)Math.Floor(cell.X / (double)chunkSize),
            (int)Math.Floor(cell.Y / (double)chunkSize), (int)Math.Floor(cell.Z / (double)chunkSize));
        if (!loadedChunks.TryGetValue(chunk, out bool loaded))
        {
            loaded = api.World.IsFullyLoadedChunk(Position(cell));
            loadedChunks[chunk] = loaded;
        }
        if (!loaded) TouchedUnloaded = true;
        return loaded;
    }

    private bool LoadedAround(JourneyCell cell)
    {
        int minX = (int)Math.Floor(cell.X + 0.5 + box.X1);
        int maxX = (int)Math.Floor(cell.X + 0.5 + box.X2);
        int minZ = (int)Math.Floor(cell.Z + 0.5 + box.Z1);
        int maxZ = (int)Math.Floor(cell.Z + 0.5 + box.Z2);
        int minY = cell.Y - maxFall - 2;
        int maxY = cell.Y + (int)Math.Ceiling(box.Y2 + stepHeight) + 1;
        for (int x = minX; x <= maxX; x++)
        for (int z = minZ; z <= maxZ; z++)
        {
            for (int y = minY; y <= maxY; y += 16)
                if (!Loaded(new JourneyCell(x, y, z))) return false;
            if (!Loaded(new JourneyCell(x, maxY, z))) return false;
        }
        return true;
    }

    public bool TryEdge(JourneyCell from, int direction, out JourneyEdge edge)
    {
        edge = default;
        Cardinal cardinal = Cardinal.ALL[direction];
        JourneyCell next = new(from.X + cardinal.Normali.X, from.Y, from.Z + cardinal.Normali.Z);
        if (!LoadedAround(from) || !LoadedAround(next)) return false;
        JourneyDoor? door = DoorInBody(next);
        if (door != null)
        {
            if (!door.Value.Allowed || cardinal.IsDiagonal
                || Math.Abs(cardinal.Normali.X * door.Value.NormalX + cardinal.Normali.Z * door.Value.NormalZ) != 1)
                return false;
            // Cross a normal door as an explicit, level transition. Other terrain
            // keeps vanilla's neighbour rules; no world blocks are changed here.
            JourneyCell beyond = new(next.X + cardinal.Normali.X, from.Y, next.Z + cardinal.Normali.Z);
            if (!LoadedAround(beyond) || DoorInBody(beyond) != null
                || !ClearDoorPassage(from, beyond, door.Value, ignoreClosedDoor: true)) return false;
            edge = new JourneyEdge(from, beyond, 3, door);
            return true;
        }

        PathNode node = new(Position(next));
        float extraCost = 0;
        if (!probe.TryStep(node, cardinal, box, stepHeight, maxFall, ref extraCost)) return false;
        JourneyCell to = Cell(node);
        if (!LoadedAround(to) || DoorInBody(to) != null) return false;
        edge = new JourneyEdge(from, to, (cardinal.IsDiagonal ? Math.Sqrt(2) : 1) + Math.Max(0, extraCost));
        return true;
    }

    private JourneyDoor? DoorInBody(JourneyCell cell)
    {
        for (int x = (int)Math.Floor(cell.X + 0.5 + box.X1); x <= (int)Math.Floor(cell.X + 0.5 + box.X2); x++)
        for (int z = (int)Math.Floor(cell.Z + 0.5 + box.Z1); z <= (int)Math.Floor(cell.Z + 0.5 + box.Z2); z++)
        for (int y = cell.Y; y < cell.Y + Math.Max(1, (int)Math.Ceiling(box.Y2)); y++)
        {
            JourneyDoor? door = doorAt(Position(new JourneyCell(x, y, z)));
            if (door != null) return door;
        }
        return null;
    }

    internal bool ClearDoorPassage(JourneyCell from, JourneyCell to, JourneyDoor door, bool ignoreClosedDoor)
    {
        if (from.Y != to.Y || !LoadedAround(from) || !LoadedAround(to)) return false;
        Vec3d start = Waypoint(from);
        Vec3d end = Waypoint(to);
        int samples = Math.Max(1, (int)Math.Ceiling(start.DistanceTo(end) / 0.2));
        for (int i = 0; i <= samples; i++)
        {
            double t = i / (double)samples;
            Vec3d point = new(start.X + (end.X - start.X) * t, start.Y, start.Z + (end.Z - start.Z) * t);
            // Require support across the crossing. A planned door is not a bridge.
            BlockPos floor = new((int)Math.Floor(point.X), from.Y - 1, (int)Math.Floor(point.Z), dimension);
            Block support = api.World.BlockAccessor.GetBlock(floor);
            Cuboidf[]? supportBoxes = support.GetCollisionBoxes(api.World.BlockAccessor, floor);
            bool supported = false;
            if (supportBoxes != null)
                foreach (Cuboidf supportBox in supportBoxes)
                    supported |= supportBox.Y2 >= 0.99f
                        && point.X - floor.X >= supportBox.X1 && point.X - floor.X <= supportBox.X2
                        && point.Z - floor.Z >= supportBox.Z1 && point.Z - floor.Z <= supportBox.Z2;
            if (!supported || !support.CanStep || support.GetTraversalCost(floor, creatureType) > 10000) return false;

            Cuboidd body = box.ToDouble().Translate(point.X, point.Y, point.Z);
            // Rotating a wooden door's float collision box can put its tangent
            // face a few ulps inside the fox. Ignore only that numerical contact,
            // not meaningful penetration (the engine neighbour probe accepts it).
            const double contactTolerance = 0.00001;
            body.X1 += contactTolerance; body.X2 -= contactTolerance;
            body.Y1 += contactTolerance; body.Y2 -= contactTolerance;
            body.Z1 += contactTolerance; body.Z2 -= contactTolerance;
            for (int x = (int)Math.Floor(body.X1); x <= (int)Math.Floor(body.X2); x++)
            for (int z = (int)Math.Floor(body.Z1); z <= (int)Math.Floor(body.Z2); z++)
            for (int y = from.Y - 1; y <= from.Y + (int)Math.Ceiling(box.Y2); y++)
            {
                BlockPos pos = new(x, y, z, dimension);
                Block fluid = api.World.BlockAccessor.GetBlock(pos, BlockLayersAccess.Fluid);
                if (fluid.GetTraversalCost(pos, creatureType) > 10000) return false;
                if (ignoreClosedDoor && doorAt(pos) is JourneyDoor part && part.Position == door.Position && part.Allowed) continue;
                Block block = api.World.BlockAccessor.GetBlock(pos, BlockLayersAccess.MostSolid);
                if (block.GetTraversalCost(pos, creatureType) > 10000) return false;
                Cuboidf[]? collisions = block.GetCollisionBoxes(api.World.BlockAccessor, pos);
                if (collisions == null) continue;
                foreach (Cuboidf collision in collisions)
                    if (body.Intersects(collision, pos.X, pos.InternalY, pos.Z)) return false;
            }
        }
        return true;
    }
}
