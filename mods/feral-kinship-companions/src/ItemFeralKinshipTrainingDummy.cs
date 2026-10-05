#nullable enable

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace FeralKinshipCompanions;

/// <summary>
/// Places the Feral Kinship training dummy. The item is intentionally a test
/// tool and is only consumed outside creative mode.
/// </summary>
public sealed class ItemFeralKinshipTrainingDummy : Item
{
    public override void OnHeldInteractStart(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        bool firstEvent,
        ref EnumHandHandling handling)
    {
        if (blockSel == null)
        {
            return;
        }

        IPlayer? player = byEntity.World.PlayerByUid((byEntity as EntityPlayer)?.PlayerUID);
        if (player == null)
        {
            return;
        }

        if (!byEntity.World.Claims.TryAccess(player, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
        {
            slot.MarkDirty();
            return;
        }

        EntityProperties? type = byEntity.World.GetEntityType(
            Code
        );
        if (type == null)
        {
            byEntity.World.Logger.Error(
                "Feral Kinship training dummy item could not find entity type {0}.",
                Code
            );
            return;
        }

        double x = blockSel.Position.X
            + (blockSel.DidOffset ? 0 : blockSel.Face.Normali.X)
            + 0.5;
        double y = blockSel.Position.Y
            + (blockSel.DidOffset ? 0 : blockSel.Face.Normali.Y);
        double z = blockSel.Position.Z
            + (blockSel.DidOffset ? 0 : blockSel.Face.Normali.Z)
            + 0.5;
        Vec3d spawnPosition = new(x, y, z);

        // Ignore the supporting block at the entity's feet. This is the same
        // placement-safe test used by vanilla placeable entity items.
        if (byEntity.World.CollisionTester.IsColliding(
                byEntity.World.BlockAccessor,
                type.SpawnCollisionBox.OmniNotDownGrowBy(0.1f),
                spawnPosition,
                false
            ))
        {
            return;
        }

        Entity? entity = byEntity.World.ClassRegistry.CreateEntity(type);
        if (entity == null)
        {
            return;
        }

        entity.Pos.SetPosWithDimension(spawnPosition);
        entity.Pos.Yaw = byEntity.Pos.Yaw;
        entity.PositionBeforeFalling.Set(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
        entity.Attributes.SetString("origin", "playerplaced");
        entity.WatchedAttributes.SetBool("noSpawnAnim", true);

        if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            slot.TakeOut(1);
            slot.MarkDirty();
        }

        byEntity.World.SpawnEntity(entity);
        handling = EnumHandHandling.PreventDefaultAction;
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
    {
        return new[]
        {
            new WorldInteraction
            {
                ActionLangCode = "heldhelp-place",
                MouseButton = EnumMouseButton.Right
            }
        }.Append(base.GetHeldInteractionHelp(inSlot));
    }
}
