#nullable enable
using System;
using System.Linq;
using Vintagestory.API.Common.Entities;
namespace FeralKinshipCompanions;
public sealed partial class FeralKinshipCompanionSystem
{
    private void ObserveWellbeingCompany(Entity entity, FoxPackRecordV2 record, double seconds)
    {
        var status = GetDomesticationStatus(entity);
        string personality = status?.GetString(PersonalityKey, string.Empty) ?? string.Empty;
        var peers = loadedFoxes.Values.Where(other => other.EntityId != entity.EntityId && other.Alive
            && IsOwner(other, record.OwnerUid) && !IsGuideFox(other) && !IsFoxAwayFromWorld(other)
            && !IsFoxIncapacitated(other) && other.Pos.Dimension == entity.Pos.Dimension
            && other.Pos.SquareDistanceTo(entity.Pos) <= 12 * 12 && HasWellbeingLineOfSight(entity, other)).ToArray();
        bool kin = peers.Any(other => TryGetCompanionSpecies(other, out var species) && species.Id == record.SpeciesId);
        var owner = entity.World.PlayerByUid(record.OwnerUid)?.Entity;
        bool ownerNearby = owner?.Alive == true && owner.Pos.Dimension == entity.Pos.Dimension
            && owner.Pos.SquareDistanceTo(entity.Pos) <= 8 * 8 && HasWellbeingLineOfSight(entity, owner);
        bool unverifiedPeer = peers.Length == 0 && !ownerNearby && packRepository!.GetRecordsForOwner(record.OwnerUid)
            .Any(other => other.FoxId != record.FoxId && !other.Archived && FindLoadedCompanionByFoxId(other.FoxId) == null);
        CompanionWellbeingCompany.Observe(record.Wellbeing!, personality, peers.Length > 0, kin, ownerNearby,
            peers.Length > 0 || ownerNearby || !unverifiedPeer,
            !IsFoxIncapacitated(entity) && record.Wellbeing!.HealthContribution >= -5, seconds);
    }
    internal bool IsWellbeingActivityPerformed(Entity entity, FoxIdlePlan plan, bool arrived, bool moving)
    {
        if (IsCompanionOwnerOffline(entity) || !entity.Alive || IsFoxAwayFromWorld(entity) || IsFoxIncapacitated(entity)) return false;
        string category = CompanionWellbeingCompany.Category(plan.Kind);
        if (category.Length == 0) return false;
        if (category == "social")
        {
            var peer = entity.World.GetEntityById(plan.PartnerEntityId);
            return peer?.Alive == true && !IsFoxIncapacitated(peer) && IsOwner(peer, GetCompanionOwnerUid(entity))
                && peer.Pos.Dimension == entity.Pos.Dimension && peer.Pos.SquareDistanceTo(entity.Pos) <= 6 * 6
                && HasWellbeingLineOfSight(entity, peer) && (arrived || moving);
        }
        if (category == "amenity")
        {
            if (!arrived) return false;
            if (plan.Kind == "pack-cart-sit") return ResolveWellbeingHome(GetCompanionOwnerUid(entity)) == CompanionHomeStatus.Present;
            if (plan.LookAtTarget == null || packRepository?.Loaded != true) return false;
            var pos = plan.LookAtTarget.AsBlockPos; pos.dimension = entity.Pos.Dimension;
            var amenity = packRepository.GetAmenity(pos);
            return amenity != null && amenity.OwnerUid == GetCompanionOwnerUid(entity) && amenity.Kind != "storage"
                && entity.Pos.SquareDistanceTo(plan.LookAtTarget) <= 4 * 4
                && entity.World.BlockAccessor.GetBlock(pos).Code?.Path.StartsWith(PackAmenityBlockPathPrefix, StringComparison.Ordinal) == true;
        }
        return arrived || moving && plan.Kind is "short-wander" or "perimeter-walk";
    }
    internal void CreditWellbeingActivity(Entity entity, string kind, double performedSeconds)
    {
        if (IsCompanionOwnerOffline(entity) || !entity.Alive || IsFoxIncapacitated(entity) || packRepository?.Loaded != true) return;
        string id = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (packRepository.TryGetRecord(id, out var record) && record?.Wellbeing?.Version == CompanionWellbeingRules.Version
            && CompanionWellbeingCompany.Credit(record.Wellbeing, kind, performedSeconds))
        {
            ObserveWellbeingCompany(entity, record, 0);
            packRepository.Save();
        }
    }
}
