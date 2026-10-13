#nullable enable
using System;
using System.Linq;
using Vintagestory.API.MathTools;
namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private CompanionHomeStatus ResolveWellbeingHome(string ownerUid)
    {
        if (serverApi == null || packRepository?.Loaded != true) return CompanionHomeStatus.Unknown;
        var home = packRepository.GetWellbeingHome(ownerUid);
        bool unknown = false;
        bool changed = false;
        if (!home.EverEstablished && packRepository.GetRecordsForOwner(ownerUid).Any(record => record.Wellbeing?.HasEstablishedHome == true))
            changed |= packRepository.MarkWellbeingHomeEstablished(ownerUid);
        foreach (var marker in packRepository.GetCairnsForOwner(ownerUid))
        {
            var pos = new BlockPos(marker.X, marker.Y, marker.Z, marker.Dimension);
            if (serverApi.World.BlockAccessor.GetChunkAtBlockPos(pos) == null)
            {
                if (marker.IsPackCart)
                {
                    changed |= packRepository.MarkWellbeingHomeEstablished(ownerUid);
                    if (changed) packRepository.Save();
                    return CompanionHomeStatus.Present; // Registered unloaded carts remain homes.
                }
                unknown = true; // Legacy marker type cannot be inferred from an unloaded block.
                continue;
            }
            var block = serverApi.World.BlockAccessor.GetBlock(pos);
            bool actualCart = block.Code?.Domain == "feralkinshipcompanions"
                && block.Code.Path.StartsWith("packcart", StringComparison.Ordinal);
            if (marker.IsPackCart) changed |= packRepository.MarkWellbeingHomeEstablished(ownerUid);
            if (!actualCart) continue;
            var ownership = ResolvePackCartOwnership(pos, applyRepairs: true);
            if (!string.Equals(ownership.OwnerUid, ownerUid, StringComparison.Ordinal)) continue;
            if (!marker.IsPackCart) { marker.IsPackCart = true; changed = true; }
            changed |= packRepository.MarkWellbeingHomeEstablished(ownerUid);
            if (changed) packRepository.Save();
            return CompanionHomeStatus.Present;
        }
        if (changed) packRepository.Save();
        if (!home.EverEstablished) return CompanionHomeStatus.Unestablished;
        return unknown ? CompanionHomeStatus.Unknown : CompanionHomeStatus.Missing;
    }
}
