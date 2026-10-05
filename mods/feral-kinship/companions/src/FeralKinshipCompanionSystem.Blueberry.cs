#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private static readonly string[] BlueberryModeSounds =
    {
        "feralkinshipcompanions:sounds/eastereggs/blueberry-crunch-1",
        "feralkinshipcompanions:sounds/eastereggs/blueberry-crunch-2",
        "feralkinshipcompanions:sounds/eastereggs/blueberry-crunch-3",
        "feralkinshipcompanions:sounds/eastereggs/blueberry-crunch-4"
    };
    private static readonly string[] BlueberryModeThoughts =
    {
        "I want blueberry fries.",
        "Not harvest. Eat. >:3",
        "Loud crushing noises.",
        "These pixels sure are imperfect...",
        "Berry problem has been solved!"
    };
    private const string BlueberryBushPathPrefix = "fruitingbush-";
    private const string BlueberryCuttingPathPrefix = "fruitingbushcutting-blueberry-";
    private const string BlueberryCuttingCode = "fruitingbushcutting-blueberry-free";
    private readonly Dictionary<string, long> blueberryModeReservations = new(StringComparer.Ordinal);

    internal bool TryFindBlueberryModeTarget(
        Entity fox,
        out BlockPos? target,
        out Vec3d? approachTarget)
    {
        target = null;
        approachTarget = null;
        if (serverApi == null
            || !BlueberryModeEnabled
            || !IsTamedFox(fox)
            || !TryGetCompanionSpecies(fox, out CompanionSpeciesProfile species)
            || !string.Equals(species.Id, "wolf", StringComparison.OrdinalIgnoreCase)
            || IsCompanionJuvenile(fox)) return false;

        if (!TryGetCompanionDutyCenter(fox, out BlockPos? center, out float radius)
            || center == null) return false;

        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        List<(BlockPos Position, Vec3d Approach, double Distance)> candidates = new();
        int horizontalRadius = (int)Math.Ceiling(radius);
        for (int dx = -horizontalRadius; dx <= horizontalRadius; dx++)
        {
            for (int dz = -horizontalRadius; dz <= horizontalRadius; dz++)
            {
                if (dx * dx + dz * dz > radius * radius) continue;

                BlockPos surfaceProbe = new(
                    center.X + dx,
                    center.Y,
                    center.Z + dz,
                    fox.Pos.Dimension);
                int terrainHeight = blocks.GetTerrainMapheightAt(surfaceProbe);
                int rainHeight = blocks.GetRainMapHeightAt(surfaceProbe);
                int minSurfaceHeight = Math.Min(terrainHeight, rainHeight) - 8;
                int maxSurfaceHeight = Math.Max(terrainHeight, rainHeight) + 8;
                for (int y = minSurfaceHeight; y <= maxSurfaceHeight; y++)
                {
                    BlockPos candidate = new(
                        center.X + dx,
                        y,
                        center.Z + dz,
                        fox.Pos.Dimension);
                    Block? block = blocks.GetBlock(candidate);
                    if (!IsBlueberryModeTarget(block, candidate)
                        || !CanCompanionModifyBlock(fox, candidate)
                        || !IsBlueberryModeTargetAvailable(fox, candidate)
                        || !TryGetNaturalCleanupApproachTarget(
                            fox,
                            candidate,
                            out Vec3d? candidateApproach)
                        || candidateApproach == null) continue;

                    candidates.Add((
                        candidate,
                        candidateApproach,
                        fox.Pos.SquareDistanceTo(candidateApproach)));
                }
            }
        }

        foreach ((BlockPos position, Vec3d approach, double _) in
                 candidates.OrderBy(candidate => candidate.Distance))
        {
            if (!TryReserveBlueberryModeTarget(fox, position)) continue;
            target = position;
            approachTarget = approach;
            return true;
        }

        return false;
    }

    internal void ReleaseBlueberryModeTarget(Entity fox, BlockPos? target)
    {
        if (target == null) return;
        string key = NaturalCleanupKey(target);
        if (blueberryModeReservations.TryGetValue(key, out long holderId)
            && holderId == fox.EntityId)
        {
            blueberryModeReservations.Remove(key);
        }
    }

    internal bool TryEatBlueberryBush(Entity fox, BlockPos target)
    {
        if (serverApi == null
            || !BlueberryModeEnabled
            || !IsTamedFox(fox)
            || !TryGetCompanionSpecies(fox, out CompanionSpeciesProfile species)
            || !string.Equals(species.Id, "wolf", StringComparison.OrdinalIgnoreCase)
            || IsCompanionJuvenile(fox)
            || target.dimension != fox.Pos.Dimension
            || fox.Pos.SquareDistanceTo(new Vec3d(
                target.X + 0.5,
                target.InternalY + 0.5,
                target.Z + 0.5)) > 3.2d * 3.2d
            || !CanCompanionModifyBlock(fox, target)) return false;

        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        Block? bushBlock = blocks.GetBlock(target);
        if (!IsBlueberryModeTarget(bushBlock, target) || bushBlock == null) return false;

        BEBehaviorFruitingBush? bush = bushBlock.GetBEBehavior<BEBehaviorFruitingBush>(target);
        Block? cuttingBlock;
        if (IsBlueberryCutting(bushBlock))
        {
            cuttingBlock = bushBlock;
        }
        else
        {
            if (bush == null) return false;

            string? cuttingCode = bushBlock.Attributes?["cuttingBlockCode"].AsString();
            if (string.IsNullOrWhiteSpace(cuttingCode)) cuttingCode = BlueberryCuttingCode;
            cuttingBlock = serverApi.World.GetBlock(
                AssetLocation.Create(cuttingCode, bushBlock.Code?.Domain ?? "game"));
        }
        if (cuttingBlock == null) return false;

        ItemStack cutting = new(cuttingBlock);
        if (bush?.BState?.Traits != null && bush.BState.Traits.Length > 0)
        {
            cutting.Attributes.SetString("traits", string.Join(",", bush.BState.Traits));
        }

        // This is deliberately not BreakBlock: vanilla bush drops would
        // produce berries or another cutting. Blueberry Mode eats the target
        // and leaves exactly one replantable cutting behind as recovery.
        blocks.SetBlock(0, target);
        serverApi.World.SpawnItemEntity(
            cutting,
            new Vec3d(
                target.X + 0.5,
                target.InternalY + 0.35,
                target.Z + 0.5));

        if (TryGetOwnerPlayer(fox, out IServerPlayer? owner))
        {
            string sound = BlueberryModeSounds[
                serverApi.World.Rand.Next(BlueberryModeSounds.Length)];
            SendOwnerSpatialSound(
                owner!,
                new AssetLocation(sound),
                target,
                pitch: 0.94f,
                range: 24f,
                volume: 1f);
            string thought = BlueberryModeThoughts[
                serverApi.World.Rand.Next(BlueberryModeThoughts.Length)];
            SendCompanionThought(fox, owner!, thought, durationMs: 5000);
        }

        return true;
    }

    private bool TryReserveBlueberryModeTarget(Entity fox, BlockPos target)
    {
        string key = NaturalCleanupKey(target);
        if (!IsBlueberryModeTargetAvailable(fox, target)) return false;
        blueberryModeReservations[key] = fox.EntityId;
        return true;
    }

    private bool IsBlueberryModeTargetAvailable(Entity fox, BlockPos target)
    {
        string key = NaturalCleanupKey(target);
        if (!blueberryModeReservations.TryGetValue(key, out long holderId)) return true;
        if (holderId == fox.EntityId) return true;

        Entity? holder = serverApi?.World.GetEntityById(holderId);
        if (holder?.Alive == true && !IsFoxAwayFromWorld(holder)) return false;
        blueberryModeReservations.Remove(key);
        return true;
    }

    private static bool IsBlueberryModeTarget(Block? block, BlockPos position)
    {
        if (block == null) return false;
        string? path = block.Code?.Path;
        if (!string.Equals(block.Code?.Domain, "game", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (IsBlueberryCutting(block)) return true;
        if (!path.StartsWith(BlueberryBushPathPrefix, StringComparison.OrdinalIgnoreCase)
            || path.Contains("blueberry", StringComparison.OrdinalIgnoreCase) == false)
        {
            return false;
        }

        // Do not gate on growth state: Blueberry Mode intentionally eats wild,
        // flowering, unripe, ripening, and ripe bushes alike.
        return block.GetBEBehavior<BEBehaviorFruitingBush>(position) != null;
    }

    private static bool IsBlueberryCutting(Block block)
    {
        return string.Equals(block.Code?.Domain, "game", StringComparison.OrdinalIgnoreCase)
            && block.Code?.Path?.StartsWith(
                BlueberryCuttingPathPrefix,
                StringComparison.OrdinalIgnoreCase) == true;
    }
}
