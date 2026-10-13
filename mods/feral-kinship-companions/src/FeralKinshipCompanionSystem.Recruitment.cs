#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private List<(CompanionSpeciesProfile Profile, List<CompanionRecruitmentVariant> Variants, float Weight)>
        GetRecruitmentPool(FoxExpeditionRecord expedition)
    {
        var groups = new Dictionary<string, (CompanionSpeciesProfile Profile, List<CompanionRecruitmentVariant> Variants, float Weight)>(StringComparer.OrdinalIgnoreCase);
        if (serverApi == null) return groups.Values.ToList();
        var members = expedition.MemberSnapshots ?? new();
        if (members.Count == 0 && packRepository != null)
            members = expedition.SelectedFoxIds.Select(id => packRepository.TryGetRecord(id, out var record) && record != null
                && record.OwnerUid == expedition.OwnerUid
                ? new FoxExpeditionMemberSummaryPacket { FoxId = id, SpeciesId = record.SpeciesId, EntityCode = record.EntityCode }
                : null).Where(m => m != null).Cast<FoxExpeditionMemberSummaryPacket>().ToList();
        foreach (var member in members)
        {
            string speciesId = string.IsNullOrWhiteSpace(member.SpeciesId) ? "fox" : member.SpeciesId;
            if (!CompanionSpeciesCatalog.TryGetById(speciesId, out var profile)
                || !profile.HasCapability(CompanionCapabilities.Recruitment)) continue;
            string entityCode = member.EntityCode;
            if (string.IsNullOrEmpty(entityCode) && packRepository?.TryGetRecord(member.FoxId, out var legacy) == true
                && legacy != null && legacy.OwnerUid == expedition.OwnerUid && legacy.SpeciesId == speciesId)
                entityCode = legacy.EntityCode;
            var variants = CompanionNativeRecruitmentCatalog.ForMember(profile, entityCode, serverApi.World.GetEntityType)
                .Where(v => CompanionRecruitmentResolver.Resolve(profile, v, serverApi.World.GetEntityType) != null).ToList();
            if (variants.Count == 0) continue;
            string key = variants[0].FamilyId ?? profile.Id;
            float weight = Math.Max(0.01f, profile.RecruitmentWeight);
            if (groups.TryGetValue(key, out var existing)) groups[key] = (existing.Profile, existing.Variants, existing.Weight + weight);
            else groups[key] = (profile, variants, weight);
        }
        return groups.Values.ToList();
    }

    private bool IsSafeRecruitmentPosition(BlockPos feet, Vec3d candidate, EntityProperties type)
    {
        if (serverApi == null) return false;
        var world = serverApi.World;
        var blocks = world.BlockAccessor;
        bool FullWater(BlockPos cell)
        {
            Block fluid = blocks.GetBlock(cell, BlockLayersAccess.Fluid);
            return fluid.IsLiquid() && fluid.LiquidCode == "water" && fluid.LiquidLevel >= 7;
        }
        if (!CompanionRecruitmentPlacement.HabitatSafe(type, feet, candidate,
            cell => blocks.GetChunkAtBlockPos(cell) != null, FullWater,
            cell => blocks.GetBlock(cell, BlockLayersAccess.Fluid).IsLiquid(),
            cell => blocks.GetBlock(cell).SideSolid[BlockFacing.UP.Index])) return false;
        return !world.CollisionTester.IsColliding(blocks, type.SpawnCollisionBox, candidate, false)
            && world.GetEntitiesAround(candidate,
                Math.Max(0.9f, type.SpawnCollisionBox.XSize), Math.Max(1.5f, type.SpawnCollisionBox.YSize),
                nearby => nearby.Alive && !IsFoxAwayFromWorld(nearby)).Length == 0;
    }
}
