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
    private const long UndergroundRecoveryScanIntervalMs = 5000;
    private const long UndergroundRecoveryGraceMs = 10000;
    private const long UndergroundRecoveryRetryMs = 15000;
    private const int UndergroundRecoverySearchRadius = 5;
    private const int UndergroundRecoveryVerticalRange = 4;

    private readonly Dictionary<long, long> undergroundRecoveryDetectedAtMs = new();
    private readonly Dictionary<long, long> undergroundRecoveryNextAttemptAtMs = new();
    private long nextUndergroundRecoveryScanAtMs;

    private enum UndergroundRecoveryAnchorResult
    {
        Unavailable,
        WaitingForChunk,
        Found
    }

    private void MaintainUndergroundCompanionRecovery(Entity[] companions)
    {
        if (serverApi == null || serverApi.World.ElapsedMilliseconds < nextUndergroundRecoveryScanAtMs)
        {
            return;
        }

        long now = serverApi.World.ElapsedMilliseconds;
        nextUndergroundRecoveryScanAtMs = now + UndergroundRecoveryScanIntervalMs;
        HashSet<long> loadedIds = companions.Select(fox => fox.EntityId).ToHashSet();
        foreach (long entityId in undergroundRecoveryDetectedAtMs.Keys.Where(id => !loadedIds.Contains(id)).ToArray())
        {
            undergroundRecoveryDetectedAtMs.Remove(entityId);
            undergroundRecoveryNextAttemptAtMs.Remove(entityId);
        }

        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        foreach (Entity fox in companions)
        {
            if (!fox.Alive
                || !IsTamedFox(fox)
                || IsFoxAwayFromWorld(fox)
                || fox.Pos.Dimension != 0)
            {
                ClearUndergroundRecoveryState(fox.EntityId);
                continue;
            }

            // Match Essentials' floatupwhenstuck detector: ignore touching faces,
            // shrink the entity box slightly, and only consider actual terrain overlap.
            bool intersectsTerrain = !fox.Swimming
                && serverApi.World.CollisionTester.GetCollidingCollisionBox(
                    blocks,
                    fox.CollisionBox.Clone().ShrinkBy(0.01f),
                    fox.Pos.XYZ,
                    false
                ) != null;
            bool mounted = fox is EntityAgent mountedAgent && mountedAgent.MountedOn != null;

            if (!UndergroundCompanionRecoveryPolicy.ShouldRecover(
                    fox.Pos.Dimension,
                    fox.Swimming,
                    fox.Teleporting,
                    mounted,
                    intersectsTerrain))
            {
                ClearUndergroundRecoveryState(fox.EntityId);
                continue;
            }

            if (!undergroundRecoveryDetectedAtMs.TryGetValue(fox.EntityId, out long detectedAtMs))
            {
                undergroundRecoveryDetectedAtMs[fox.EntityId] = now;
                continue;
            }
            if (now - detectedAtMs < UndergroundRecoveryGraceMs
                || (undergroundRecoveryNextAttemptAtMs.TryGetValue(fox.EntityId, out long nextAttemptAtMs)
                    && now < nextAttemptAtMs))
            {
                continue;
            }

            undergroundRecoveryNextAttemptAtMs[fox.EntityId] = now + UndergroundRecoveryRetryMs;
            if (!TryFindUndergroundRecoveryDestination(
                    fox,
                    out Vec3d? safePosition,
                    out string anchorName)
                || safePosition == null)
            {
                continue;
            }

            double originalY = fox.Pos.Y;
            int surfaceY = blocks.GetRainMapHeightAt(new BlockPos(
                (int)Math.Floor(fox.Pos.X),
                0,
                (int)Math.Floor(fox.Pos.Z),
                0
            ));
            double belowSurface = surfaceY - originalY;
            fox.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.StopTasks();
            if (fox is EntityAgent agent)
            {
                agent.Controls.StopAllMovement();
            }
            fox.Pos.Motion.Set(0, 0, 0);
            fox.TeleportTo(safePosition);
            fox.PositionBeforeFalling.Set(safePosition.X, safePosition.Y, safePosition.Z);
            ClearUndergroundRecoveryState(fox.EntityId);
            serverApi.Logger.Notification(
                "[FeralKinshipCompanions] Recovered underground Companion entity {0} from {1:0.0} blocks below the surface to its {2} at {3}.",
                fox.EntityId,
                belowSurface,
                anchorName,
                safePosition
            );
        }
    }

    private bool TryFindUndergroundRecoveryDestination(
        Entity fox,
        out Vec3d? safePosition,
        out string anchorName)
    {
        safePosition = null;
        anchorName = string.Empty;
        if (serverApi == null || packRepository?.Loaded != true)
        {
            return false;
        }

        string ownerUid = GetCompanionOwnerUid(fox);
        string foxId = GetDomesticationStatus(fox)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        FoxBedRecord? bed = packRepository.GetBedForFox(foxId);
        if (bed != null
            && string.Equals(bed.OwnerUid, ownerUid, StringComparison.Ordinal)
            && bed.Dimension == 0)
        {
            BlockPos bedPos = new(bed.X, bed.Y, bed.Z, bed.Dimension);
            if (serverApi.World.BlockAccessor.GetChunkAtBlockPos(bedPos) == null)
            {
                RequestCairnChunkLoad(bedPos);
                return false;
            }

            Block bedBlock = serverApi.World.BlockAccessor.GetBlock(bedPos);
            if (bedBlock.Code == null || !IsFoxBedCode(bedBlock.Code))
            {
                bed = null;
            }
        }
        if (bed != null
            && string.Equals(bed.OwnerUid, ownerUid, StringComparison.Ordinal)
            && bed.Dimension == 0)
        {
            BlockPos bedPos = new(bed.X, bed.Y, bed.Z, bed.Dimension);
            UndergroundRecoveryAnchorResult bedResult = FindSafeUndergroundRecoveryPosition(
                fox,
                bedPos,
                null,
                out safePosition
            );
            if (bedResult == UndergroundRecoveryAnchorResult.WaitingForChunk) return false;
            if (bedResult == UndergroundRecoveryAnchorResult.Found)
            {
                anchorName = "assigned bed";
                return true;
            }
        }

        BlockPos? packCart = GetActiveCairnPosition(ownerUid);
        if (packCart != null && packCart.dimension == 0)
        {
            if (serverApi.World.BlockAccessor.GetChunkAtBlockPos(packCart) == null)
            {
                RequestCairnChunkLoad(packCart);
                return false;
            }

            Block packCartBlock = serverApi.World.BlockAccessor.GetBlock(packCart);
            if (packCartBlock.Code != null && IsPackMarkerCode(packCartBlock.Code))
            {
                BlockPos portal = GetPackMarkerPortalBlock(packCart, packCartBlock);
                UndergroundRecoveryAnchorResult cartResult = FindSafeUndergroundRecoveryPosition(
                    fox,
                    portal,
                    null,
                    out safePosition
                );
                if (cartResult == UndergroundRecoveryAnchorResult.WaitingForChunk) return false;
                if (cartResult == UndergroundRecoveryAnchorResult.Found)
                {
                    anchorName = "Pack Cart";
                    return true;
                }
            }
        }

        Entity? owner = serverApi.World.PlayerByUid(ownerUid)?.Entity;
        if (owner?.Alive == true && owner.Pos.Dimension == 0)
        {
            UndergroundRecoveryAnchorResult ownerResult = FindSafeUndergroundRecoveryPosition(
                fox,
                owner.Pos.AsBlockPos,
                owner,
                out safePosition
            );
            if (ownerResult == UndergroundRecoveryAnchorResult.Found)
            {
                anchorName = "owner";
                return true;
            }
        }

        return false;
    }

    private UndergroundRecoveryAnchorResult FindSafeUndergroundRecoveryPosition(
        Entity fox,
        BlockPos anchor,
        Entity? nearbyOwner,
        out Vec3d? safePosition)
    {
        safePosition = null;
        if (serverApi == null || anchor.dimension != 0)
        {
            return UndergroundRecoveryAnchorResult.Unavailable;
        }

        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        if (blocks.GetChunkAtBlockPos(anchor) == null)
        {
            RequestCairnChunkLoad(anchor);
            return UndergroundRecoveryAnchorResult.WaitingForChunk;
        }

        bool encounteredUnloadedChunk = false;
        for (int radius = 0; radius <= UndergroundRecoverySearchRadius; radius++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius) continue;

                int x = anchor.X + dx;
                int z = anchor.Z + dz;
                foreach (int dy in UndergroundCompanionRecoveryPolicy.EnumerateVerticalOffsets(
                             UndergroundRecoveryVerticalRange))
                {
                    int y = anchor.Y + dy;
                    if (y <= 0 || y >= blocks.MapSizeY - 3) continue;

                    BlockPos feet = new(x, y, z, 0);
                    if (blocks.GetChunkAtBlockPos(feet) == null)
                    {
                        encounteredUnloadedChunk = true;
                        continue;
                    }

                    Vec3d candidate = new(x + 0.5, y, z + 0.5);
                    if (!IsSafeUndergroundRecoveryPosition(feet, candidate, fox, nearbyOwner)) continue;
                    safePosition = candidate;
                    return UndergroundRecoveryAnchorResult.Found;
                }
            }
        }

        if (encounteredUnloadedChunk)
        {
            RequestCairnChunkLoad(anchor);
            return UndergroundRecoveryAnchorResult.WaitingForChunk;
        }

        return UndergroundRecoveryAnchorResult.Unavailable;
    }

    private bool IsSafeUndergroundRecoveryPosition(
        BlockPos feet,
        Vec3d candidate,
        Entity fox,
        Entity? nearbyOwner)
    {
        if (serverApi == null) return false;

        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        BlockPos below = feet.DownCopy();
        if (!blocks.GetBlock(below).SideSolid[BlockFacing.UP.Index]
            || blocks.GetBlock(feet, BlockLayersAccess.Fluid).IsLiquid()
            || blocks.GetBlock(feet.UpCopy(), BlockLayersAccess.Fluid).IsLiquid()
            || serverApi.World.CollisionTester.IsColliding(
                blocks,
                fox.CollisionBox,
                candidate,
                false
            ))
        {
            return false;
        }

        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            BlockPos nearby = feet.AddCopy(dx, 0, dz);
            Block solid = blocks.GetBlock(nearby);
            Block liquid = blocks.GetBlock(nearby, BlockLayersAccess.Fluid);
            string path = solid.Code?.Path ?? string.Empty;
            if (IsStaticAmbientHazard(nearby)
                || IsStaticAmbientHazard(nearby.DownCopy())
                || IsStaticAmbientHazard(nearby.UpCopy())
                || liquid.LiquidCode == "lava"
                || path.Contains("fire", StringComparison.OrdinalIgnoreCase)
                || path.Contains("lava", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        foreach (BlockFacing facing in BlockFacing.HORIZONTALS)
        {
            BlockPos neighborFeet = feet.AddCopy(facing);
            bool nearbySupport = blocks.GetBlock(neighborFeet.DownCopy()).SideSolid[BlockFacing.UP.Index]
                || blocks.GetBlock(neighborFeet.DownCopy(2)).SideSolid[BlockFacing.UP.Index];
            if (!nearbySupport) return false;
        }

        if (nearbyOwner != null)
        {
            double dx = candidate.X - nearbyOwner.Pos.X;
            double dy = candidate.Y - nearbyOwner.Pos.Y;
            double dz = candidate.Z - nearbyOwner.Pos.Z;
            if (dx * dx + dy * dy + dz * dz < 1.2d * 1.2d) return false;
        }

        return serverApi.World.GetEntitiesAround(
            candidate,
            0.8f,
            1.5f,
            nearby => nearby != fox
                && nearby != nearbyOwner
                && nearby.Alive
                && !IsFoxAwayFromWorld(nearby)
        ).Length == 0;
    }

    private void ClearUndergroundRecoveryState(long entityId)
    {
        undergroundRecoveryDetectedAtMs.Remove(entityId);
        undergroundRecoveryNextAttemptAtMs.Remove(entityId);
    }
}
