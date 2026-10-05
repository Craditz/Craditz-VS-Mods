#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;

public partial class FeralKinshipCompanionSystem
{
    private GuiDialogFeralKinshipCartInspection? developerCartInspectionDialog;

    internal bool TryOpenDeveloperCartInspection(BlockPos pos)
    {
        if (clientApi == null || !CanUseDeveloperToolsClient()) return false;
        Block block = clientApi.World.BlockAccessor.GetBlock(pos);
        if (block.Code == null || !IsPackMarkerCode(block.Code)) return false;

        developerCartInspectionDialog?.TryClose();
        socialDialog?.TryClose();
        developerDialog?.TryClose();
        packDialog?.TryClose();
        perkDialog?.TryClose();
        developerCartInspectionDialog = new GuiDialogFeralKinshipCartInspection(clientApi, this, pos.Copy());
        developerCartInspectionDialog.TryOpen();
        RequestDeveloperCartInspection(pos);
        return true;
    }

    internal void RequestDeveloperCartInspection(BlockPos pos)
    {
        clientChannel?.SendPacket(new DeveloperCartInspectionRequestPacket
        {
            X = pos.X, Y = pos.Y, Z = pos.Z, Dimension = pos.dimension
        });
    }

    private void OnDeveloperCartInspectionState(DeveloperCartInspectionStatePacket packet)
    {
        developerCartInspectionDialog?.ApplyState(packet);
    }

    private void OnDeveloperCartInspectionRequest(
        IServerPlayer fromPlayer, DeveloperCartInspectionRequestPacket packet)
    {
        // This endpoint grants a snapshot only. Never register a Pack viewer,
        // refresh/repair records, load entities, or substitute the cart owner
        // into any normal-user or developer mutation handler.
        if (serverApi == null || packRepository?.Loaded != true) return;
        DeveloperCartInspectionStatePacket state = new()
        {
            X = packet.X, Y = packet.Y, Z = packet.Z, Dimension = packet.Dimension
        };
        if (!CanUseDeveloperTools(fromPlayer)
            || fromPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible
                is not ItemFeralKinshipDeveloperLedger)
        {
            state.Message = "Pack Cart inspection requires Creative/admin access and a held developer ledger.";
        }
        else
        {
            BlockPos pos = new(packet.X, packet.Y, packet.Z, packet.Dimension);
            EntityPlayer? playerEntity = fromPlayer.Entity;
            if (playerEntity == null || playerEntity.Pos.Dimension != pos.dimension
                || playerEntity.Pos.SquareDistanceTo(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5) > SocialActionRangeSquared
                || serverApi.World.BlockAccessor.GetChunkAtBlockPos(pos) == null)
            {
                state.Message = "The Pack Cart must be nearby and loaded in your dimension.";
            }
            else
            {
                Block block = serverApi.World.BlockAccessor.GetBlock(pos);
                if (block.Code == null || !IsPackMarkerCode(block.Code))
                {
                    state.Message = "There is no Pack Cart at that position.";
                }
                else
                {
                    // Unlike ResolvePackCartOwnership, this must not spawn a
                    // block entity, backfill a legacy tag or repair the index.
                    BlockEntityFeralKinshipPackCart? cart = serverApi.World.BlockAccessor
                        .GetBlockEntity(pos) as BlockEntityFeralKinshipPackCart;
                    PackCartOwnershipResolution ownership = PackCartOwnershipPolicy.Resolve(
                        cart?.OwnerUid, packRepository.GetCairn(pos)?.OwnerUid);
                    if (!ownership.HasOwner)
                    {
                        state.Message = "This Pack Cart has no recorded owner. No animal list is available.";
                    }
                    else
                    {
                        state.Available = true;
                        state.OwnerUid = ownership.OwnerUid;
                        state.Records = packRepository.GetRecordsForOwner(ownership.OwnerUid)
                            .Select(record => BuildCartInspectionEntry(record, ownership.OwnerUid)).ToList();
                        state.Message = "Read-only saved pack records. Inspection does not repair or load animals.";
                    }
                }
            }
        }
        serverChannel?.SendPacket(state, fromPlayer);
    }

    private DeveloperCartInspectionEntryPacket BuildCartInspectionEntry(FoxPackRecordV2 record, string ownerUid)
    {
        Entity? entity = record.EntityId > 0 ? serverApi?.World.GetEntityById(record.EntityId) : null;
        // An entity ID can outlive or refer to a different animal. Treat it as
        // loaded only if both stable identity and current ownership match.
        bool loaded = entity != null && IsOwnedCompanion(entity, ownerUid)
            && string.Equals(GetDomesticationStatus(entity)?.GetString(FoxIdKey), record.FoxId, StringComparison.Ordinal);
        return new DeveloperCartInspectionEntryPacket
        {
            Number = record.Number,
            Name = record.Name,
            Species = CompanionSpeciesCatalog.TryGetById(record.SpeciesId, out CompanionSpeciesProfile profile)
                ? profile.DisplayName : "Companion",
            Status = record.Status,
            EntityLoaded = loaded
        };
    }
}

// Deliberately separate from FoxSocialRequestPacket: no action, destination,
// selected companion or ownership fields can turn inspection into a command.
[ProtoContract]
public sealed class DeveloperCartInspectionRequestPacket
{
    [ProtoMember(1)] public int X;
    [ProtoMember(2)] public int Y;
    [ProtoMember(3)] public int Z;
    [ProtoMember(4)] public int Dimension;
}

[ProtoContract]
public sealed class DeveloperCartInspectionStatePacket
{
    [ProtoMember(1)] public int X;
    [ProtoMember(2)] public int Y;
    [ProtoMember(3)] public int Z;
    [ProtoMember(4)] public int Dimension;
    [ProtoMember(5)] public bool Available;
    [ProtoMember(6)] public string OwnerUid = string.Empty;
    [ProtoMember(7)] public string Message = string.Empty;
    [ProtoMember(8)] public List<DeveloperCartInspectionEntryPacket> Records = new();
}

[ProtoContract]
public sealed class DeveloperCartInspectionEntryPacket
{
    [ProtoMember(1)] public int Number;
    [ProtoMember(2)] public string Name = string.Empty;
    [ProtoMember(3)] public string Species = string.Empty;
    [ProtoMember(4)] public string Status = string.Empty;
    [ProtoMember(5)] public bool EntityLoaded;
}
