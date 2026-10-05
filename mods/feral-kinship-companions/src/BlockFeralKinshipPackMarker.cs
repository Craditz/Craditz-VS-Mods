#nullable enable

using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed class BlockFeralKinshipPackCairn : Block
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        // Blocks can receive right-click before the held item. Route the
        // developer ledger here too, so inspection also works without sneak.
        if (world.Side == EnumAppSide.Client && blockSel?.Position != null
            && byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible
                is ItemFeralKinshipDeveloperLedger)
        {
            world.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()
                ?.TryOpenDeveloperCartInspection(blockSel.Position);
        }
        if (world.Side == EnumAppSide.Server && blockSel?.Position != null)
        {
            world.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()?.OnPackCairnInteracted(byPlayer, blockSel.Position);
        }
        return true;
    }

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        if (world.Side == EnumAppSide.Server)
        {
            world.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()?.OnPackMarkerRemoved(pos, false);
        }
        base.OnBlockRemoved(world, pos);
    }
}

/// <summary>
/// Persists the placing player's UID on the Pack Cart itself. This block-level
/// tag is the authoritative ownership source once it exists; the world-level
/// repository remains an index for finding a player's active cart.
/// </summary>
public sealed class BlockEntityFeralKinshipPackCart : BlockEntity
{
    private const string OwnerUidAttribute = "feralKinshipPackCartOwnerUid";

    public string OwnerUid { get; private set; } = string.Empty;

    public bool SetOwnerUid(string? ownerUid)
    {
        string normalized = ownerUid?.Trim() ?? string.Empty;
        if (string.Equals(OwnerUid, normalized, System.StringComparison.Ordinal))
        {
            return false;
        }

        OwnerUid = normalized;
        MarkDirty(true);
        return true;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        OwnerUid = tree.GetString(OwnerUidAttribute, string.Empty).Trim();
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetString(OwnerUidAttribute, OwnerUid);
    }
}

public sealed class BlockFeralKinshipFoxBed : Block
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.Side == EnumAppSide.Server && blockSel?.Position != null)
        {
            world.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()?.OnFoxBedInteracted(byPlayer, blockSel.Position);
        }
        return true;
    }

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        if (world.Side == EnumAppSide.Server)
        {
            world.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()?.OnPackMarkerRemoved(pos, true);
        }
        base.OnBlockRemoved(world, pos);
    }
}

public sealed class BlockFeralKinshipWorkCart : Block
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.Side == EnumAppSide.Server && blockSel?.Position != null)
        {
            world.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()?.OnWorkCartInteracted(byPlayer, blockSel.Position);
        }
        return true;
    }

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        if (world.Side == EnumAppSide.Server)
        {
            world.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()?.OnWorkCartRemoved(pos);
        }
        base.OnBlockRemoved(world, pos);
    }
}

/// <summary>
/// A passive, fox-sized opening. Retention is deliberately non-zero so the
/// room system treats the frame as sealed even though its center is traversable.
/// </summary>
public sealed class BlockFeralKinshipFoxFlap : Block, IClaimTraverseable
{
    public override int GetRetention(BlockPos pos, BlockFacing facing, EnumRetentionType type)
    {
        return 3;
    }
}

/// <summary>A communal pack-owned AI landmark; it is never assigned to one fox.</summary>
public sealed class BlockFeralKinshipAmenity : Block
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.Side == EnumAppSide.Server && blockSel?.Position != null)
        {
            world.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()?.OnPackAmenityInteracted(byPlayer, blockSel.Position);
        }
        return true;
    }

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        if (world.Side == EnumAppSide.Server)
        {
            world.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()?.OnPackAmenityRemoved(pos);
        }
        base.OnBlockRemoved(world, pos);
    }
}

/// <summary>
/// The dining amenity keeps its established block code, but now owns a normal
/// unrestricted player container. Companion diet filtering happens only when
/// an animal chooses food; it never locks player storage slots.
/// </summary>
public sealed class BlockFeralKinshipDiningBoard : Block
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel?.Position == null)
        {
            return false;
        }

        FeralKinshipCompanionSystem? system = world.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (world.Side == EnumAppSide.Server
            && system?.OnPackAmenityInteracted(byPlayer, blockSel.Position, false) != true)
        {
            return true;
        }

        BlockEntityFeralKinshipDiningBoard? dining =
            world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityFeralKinshipDiningBoard;
        if (dining == null && world.Side == EnumAppSide.Server && !string.IsNullOrWhiteSpace(EntityClass))
        {
            // Existing worlds may contain the pre-container Dining Board.
            world.BlockAccessor.SpawnBlockEntity(EntityClass, blockSel.Position);
            dining = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityFeralKinshipDiningBoard;
            dining?.MarkDirty(true);
        }

        return dining?.OnPlayerRightClick(byPlayer, blockSel) ?? true;
    }

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        if (world.Side == EnumAppSide.Server)
        {
            world.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()?.OnPackAmenityRemoved(pos);
        }
        base.OnBlockRemoved(world, pos);
    }
}

public sealed class BlockEntityFeralKinshipDiningBoard : BlockEntityGenericContainer
{
}

/// <summary>A normal player-accessible container that also serves as the pack's communal deposit target.</summary>
public class BlockFeralKinshipPackStorage : BlockGenericTypedContainer
{
    internal static ItemStack[] GetVariantSafeDrops(Block block, IWorldAccessor world)
    {
        if (block.Code == null) return System.Array.Empty<ItemStack>();

        string path = block.Code.Path;
        int variantSeparator = path.LastIndexOf('-');
        if (variantSeparator <= 0)
        {
            return System.Array.Empty<ItemStack>();
        }

        string basePath = path[..variantSeparator];
        Block? dropBlock = world.BlockAccessor.GetBlock(
            new AssetLocation(block.Code.Domain, basePath + "-north"));
        return dropBlock == null
            ? System.Array.Empty<ItemStack>()
            : new[] { new ItemStack(dropBlock) };
    }

    public override ItemStack[] GetDrops(
        IWorldAccessor world,
        BlockPos pos,
        IPlayer byPlayer,
        float dropQuantityMultiplier = 1)
    {
        return GetVariantSafeDrops(this, world);
    }

    public override void OnBeforeRender(
        ICoreClientAPI capi,
        ItemStack itemstack,
        EnumItemRenderTarget target,
        ref ItemRenderInfo renderinfo)
    {
        base.OnBeforeRender(capi, itemstack, target, ref renderinfo);

        // Vanilla's typed-container cache uses FirstCodePart(), so all three
        // packstorage-* blocks share one mesh cache.  The first crate rendered
        // then supplies the held/placement mesh for the other variants.
        string key = "feralKinshipTypedContainerMeshRefs-" + Code.ToShortString() + "-" + SubtypeInventory;
        Dictionary<string, MultiTextureMeshRef> meshrefs = ObjectCacheUtil.GetOrCreate(capi, key, () =>
        {
            Dictionary<string, MultiTextureMeshRef> meshes = new();
            foreach (var value in GenGuiMeshes(capi))
            {
                meshes[value.Key] = capi.Render.UploadMultiTextureMesh(value.Value);
            }

            return meshes;
        });

        string type = itemstack.Attributes.GetString("type", Attributes["defaultType"].AsString("normal-generic"));
        if (meshrefs.TryGetValue(type, out MultiTextureMeshRef? modelRef))
        {
            renderinfo.ModelRef = modelRef;
        }
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);

        if (api is not ICoreClientAPI capi) return;

        string key = "feralKinshipTypedContainerMeshRefs-" + Code.ToShortString() + "-" + SubtypeInventory;
        Dictionary<string, MultiTextureMeshRef>? meshrefs = ObjectCacheUtil.TryGet<Dictionary<string, MultiTextureMeshRef>>(api, key);
        if (meshrefs == null) return;

        foreach (MultiTextureMeshRef meshref in meshrefs.Values)
        {
            meshref.Dispose();
        }

        capi.ObjectCache.Remove(key);
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        // A save opened while Companions is absent can retain this block through
        // the world's missing-block mapping while dropping its mod-owned block
        // entity. The block then looks correct when the mod returns, but the
        // Container behavior has nothing to open. Recreate only a genuinely
        // missing component; never replace another surviving block entity.
        if (blockSel?.Position != null
            && world.BlockAccessor.GetBlockEntity(blockSel.Position) == null
            && !string.IsNullOrWhiteSpace(EntityClass))
        {
            world.BlockAccessor.SpawnBlockEntity(EntityClass, blockSel.Position);
            BlockEntity? repaired = world.BlockAccessor.GetBlockEntity(blockSel.Position);
            repaired?.MarkDirty(true);

            if (world.Side == EnumAppSide.Server)
            {
                world.Logger.Warning(
                    "[FeralKinshipCompanions] Recreated a missing Pack Collection Box block entity at {0}. "
                    + "The save was likely opened while the mod was absent.",
                    blockSel.Position
                );
            }
        }

        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        if (world.Side == EnumAppSide.Server)
        {
            world.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()?.OnPackAmenityRemoved(pos);
        }
        base.OnBlockRemoved(world, pos);
    }
}

/// <summary>A separately registered wicker pack crate.</summary>
public sealed class BlockFeralKinshipPackStorageWicker : BlockFeralKinshipPackStorage
{
}

/// <summary>A dedicated one-block trunk-sized pack storage container.</summary>
public sealed class BlockFeralKinshipPackStorageTrunk : BlockGenericTypedContainer
{
    public override ItemStack[] GetDrops(
        IWorldAccessor world,
        BlockPos pos,
        IPlayer byPlayer,
        float dropQuantityMultiplier = 1)
    {
        return BlockFeralKinshipPackStorage.GetVariantSafeDrops(this, world);
    }

    public override void OnBeforeRender(
        ICoreClientAPI capi,
        ItemStack itemstack,
        EnumItemRenderTarget target,
        ref ItemRenderInfo renderinfo)
    {
        base.OnBeforeRender(capi, itemstack, target, ref renderinfo);

        string key = "feralKinshipTypedContainerMeshRefs-" + Code.ToShortString() + "-" + SubtypeInventory;
        Dictionary<string, MultiTextureMeshRef> meshrefs = ObjectCacheUtil.GetOrCreate(capi, key, () =>
        {
            Dictionary<string, MultiTextureMeshRef> meshes = new();
            foreach (var value in GenGuiMeshes(capi))
            {
                meshes[value.Key] = capi.Render.UploadMultiTextureMesh(value.Value);
            }

            return meshes;
        });

        string type = itemstack.Attributes.GetString("type", Attributes["defaultType"].AsString("normal-generic"));
        if (meshrefs.TryGetValue(type, out MultiTextureMeshRef? modelRef))
        {
            renderinfo.ModelRef = modelRef;
        }
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);

        if (api is not ICoreClientAPI capi) return;

        string key = "feralKinshipTypedContainerMeshRefs-" + Code.ToShortString() + "-" + SubtypeInventory;
        Dictionary<string, MultiTextureMeshRef>? meshrefs = ObjectCacheUtil.TryGet<Dictionary<string, MultiTextureMeshRef>>(api, key);
        if (meshrefs == null) return;

        foreach (MultiTextureMeshRef meshref in meshrefs.Values)
        {
            meshref.Dispose();
        }

        capi.ObjectCache.Remove(key);
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel?.Position != null
            && world.BlockAccessor.GetBlockEntity(blockSel.Position) == null
            && !string.IsNullOrWhiteSpace(EntityClass))
        {
            world.BlockAccessor.SpawnBlockEntity(EntityClass, blockSel.Position);
            world.BlockAccessor.GetBlockEntity(blockSel.Position)?.MarkDirty(true);
        }

        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        if (world.Side == EnumAppSide.Server)
        {
            world.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()?.OnPackAmenityRemoved(pos);
        }
        base.OnBlockRemoved(world, pos);
    }
}

/// <summary>
/// Generic typed containers add their own rotated mesh. Returning true after
/// that work prevents the terrain tessellator from drawing a second, cardinally
/// rotated copy of this open box underneath it.
/// </summary>
public class BlockEntityFeralKinshipPackStorage : BlockEntityGenericTypedContainer
{
    private bool renderTraceWritten;

    protected override void InitInventory(Block? block)
    {
        // Crates made before the typed variants were stabilized may carry the
        // legacy "storage" marker.  Both old and new assets support it, but
        // normalizing here makes the three crate capacities and their texture
        // sources deterministic for newly placed and migrated crates.
        if (block?.Code?.Path == "packstorage"
            || block?.Code?.Path == "packstorage-wicker"
            || block?.Code?.Path == "packstorage-trunk")
        {
            type = "storage";
        }

        base.InitInventory(block);
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        // The vanilla typed-container tesselator can resolve all of these
        // blocks through the same generic path.  Select the shape from the
        // complete block code here so a placed wicker or trunk crate cannot
        // inherit the standard pack-crate mesh.
        if (Api is ICoreClientAPI capi)
        {
            BlockGenericTypedContainer? typedBlock = Block as BlockGenericTypedContainer
                ?? Api.World.BlockAccessor.GetBlock(Pos) as BlockGenericTypedContainer;
            string path = typedBlock?.Code?.Path ?? string.Empty;
            string shape = path == "packstorage-wicker"
                || path.StartsWith("packstorage-wicker-")
                ? "game:block/reed/basket-reed"
                : "block/packstorage";

            if (!renderTraceWritten)
            {
                renderTraceWritten = true;
                Api.Logger.VerboseDebug(
                    "[FeralKinshipCompanions] Pack crate terrain render: block={0}, entity={1}, type={2}, shape={3}",
                    path,
                    GetType().Name,
                    this.type,
                    shape
                );
            }

            if (typedBlock != null)
            {
                MeshData mesh = typedBlock.GenMesh(capi, "storage", shape, tesselator);
                mesher.AddMeshData(mesh.Clone().Rotate(0, MeshAngle, 0));
                return true;
            }
        }

        base.OnTesselation(mesher, tesselator);
        return true;
    }
}

/// <summary>Dedicated entity registration for the wicker pack crate.</summary>
public sealed class BlockEntityFeralKinshipPackStorageWicker : BlockEntityFeralKinshipPackStorage
{
}

/// <summary>Dedicated entity registration for the trunk pack crate.</summary>
public sealed class BlockEntityFeralKinshipPackStorageTrunk : BlockEntityFeralKinshipPackStorage
{
}
