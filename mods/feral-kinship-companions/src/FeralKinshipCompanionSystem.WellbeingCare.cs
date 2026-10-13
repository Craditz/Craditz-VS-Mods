#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
namespace FeralKinshipCompanions;
public sealed partial class FeralKinshipCompanionSystem
{
    private readonly Dictionary<long, double> wellbeingCareHours = new();
    private readonly Dictionary<long, string> checkedFavoriteFood = new();
    private static string FavoriteCode(ItemStack stack) => (stack.Class == EnumItemClass.Block ? "block|" : "item|") + stack.Collectible.Code;
    private CollectibleObject? ResolveFavorite(Entity entity, string code)
    {
        int separator = code.IndexOf('|');
        if (separator <= 0 || separator == code.Length - 1) return null;
        var location = new AssetLocation(code.Substring(separator + 1));
        return code.StartsWith("block|", StringComparison.Ordinal) ? entity.World.GetBlock(location) : entity.World.GetItem(location);
    }
    // Shared by actual favorite selection and the read-only developer inspection.
    private List<(ItemStack Stack, bool Favorite)> GetWellbeingFoodOptions(Entity entity)
    {
        var result = new List<(ItemStack Stack, bool Favorite)>();
        var diet = GetCreatureDiet(entity);
        if (diet == null) return result;
        var patterns = companionConfig.FavoriteFoodCandidates ?? Array.Empty<string>();
        foreach (var collectible in entity.World.Collectibles)
        {
            if (collectible?.Code == null) continue;
            var stack = new ItemStack(collectible);
            if (!IsSuitableDiningBoardFood(entity, stack, diet)) continue;
            var nutrition = collectible.GetNutritionProperties(entity.World, stack, entity);
            bool favorite = (nutrition == null || nutrition.Health >= 0)
                && patterns.Any(p => !string.IsNullOrWhiteSpace(p) && WildcardUtil.Match(p, collectible.Code.ToString()));
            result.Add((stack, favorite));
        }
        result.Sort((a,b) => string.CompareOrdinal(FavoriteCode(a.Stack), FavoriteCode(b.Stack)));
        return result;
    }
    private void EnsureFavoriteFood(Entity entity, CompanionWellbeingState state)
    {
        if (state.Version != CompanionWellbeingRules.Version) return;
        if (checkedFavoriteFood.TryGetValue(entity.EntityId, out var checkedCode) && checkedCode == state.FavoriteFoodCode) return;
        var existing = string.IsNullOrEmpty(state.FavoriteFoodCode) ? null : ResolveFavorite(entity, state.FavoriteFoodCode);
        if (existing != null)
        {
            state.FavoriteFoodName = new ItemStack(existing).GetName();
            checkedFavoriteFood[entity.EntityId] = state.FavoriteFoodCode;
            return;
        }
        var candidates = GetWellbeingFoodOptions(entity).Where(option => option.Favorite).Select(option => option.Stack).ToList();
        string next = CompanionWellbeingCare.ChooseFavorite(state.FavoriteFoodCode, false,
            candidates.Select(FavoriteCode).ToArray(), entity.World.Rand.Next());
        if (next != state.FavoriteFoodCode) { state.FavoriteFoodSeconds = 0; state.FavoriteFoodContribution = 0; }
        state.FavoriteFoodCode = next;
        state.FavoriteFoodName = candidates.FirstOrDefault(c => FavoriteCode(c) == next)?.GetName() ?? string.Empty;
        checkedFavoriteFood[entity.EntityId] = next;
    }
    private void CreditWellbeingFavoriteFood(Entity entity, ItemStack food)
    {
        if (IsCompanionOwnerOffline(entity) || IsFoxAwayFromWorld(entity) || packRepository?.Loaded != true) return;
        string id = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (!packRepository.TryGetRecord(id, out var record) || record == null) return;
        record.Wellbeing = CompanionWellbeingRules.Normalize(record.Wellbeing);
        EnsureFavoriteFood(entity, record.Wellbeing);
        if (CompanionWellbeingCare.Meal(record.Wellbeing, FavoriteCode(food), true)) packRepository.Save();
    }
    private void ObserveWellbeingCare(Entity entity, FoxPackRecordV2 record, long previousMs, long nowMs)
    {
        var state = record.Wellbeing;
        if (state == null || state.Version != CompanionWellbeingRules.Version) return;
        EnsureFavoriteFood(entity, state);
        CompanionWellbeingCare.TickFood(state, CompanionWellbeingSafety.SafeDelta(previousMs, nowMs));
        double now = entity.World.Calendar.TotalHours;
        double hours = 0;
        if (previousMs >= 0 && nowMs >= previousMs && nowMs - previousMs <= 5000
            && wellbeingCareHours.TryGetValue(entity.EntityId, out double last) && double.IsFinite(now) && now >= last) hours = now - last;
        wellbeingCareHours[entity.EntityId] = now;
        // A known missing home supersedes retained comfort even if its bed chunk is unloaded.
        if (state.Home == "No home" || !state.HasEstablishedHome)
        {
            state.BedComfortContribution = 0;
            CompanionWellbeingRules.Recalculate(state);
        }
        var bed = packRepository?.GetBedForFox(record.FoxId);
        if (bed == null) { CompanionWellbeingCare.Bed(state, "", true, false, false, hours, entity.World.Calendar.HoursPerDay, false); return; }
        var pos = new BlockPos(bed.X, bed.Y, bed.Z, bed.Dimension);
        if (entity.World.BlockAccessor.GetChunkAtBlockPos(pos) == null) return;
        var block = entity.World.BlockAccessor.GetBlock(pos);
        if (block.Code?.ToShortString() != bed.BlockCode)
        { CompanionWellbeingCare.Bed(state, "", true, false, false, hours, entity.World.Calendar.HoursPerDay, false); return; }
        string key = $"{bed.Dimension}:{bed.X}:{bed.Y}:{bed.Z}:{bed.BlockCode}";
        bool reached = pos.dimension == entity.Pos.Dimension && entity.Pos.SquareDistanceTo(pos.X + .5, pos.Y + .2, pos.Z + .5) <= 2.25;
        if (reached)
        {
            BlockSelection? hit = null; EntitySelection? selected = null;
            var from = entity.Pos.XYZ.AddCopy(0, .3, 0);
            entity.World.RayTraceForSelection(from, new Vec3d(pos.X + .5, pos.Y + .2, pos.Z + .5), ref hit, ref selected,
                (p,b) => b.GetCollisionBoxes(entity.World.BlockAccessor,p)?.Length > 0, _ => false);
            reached = hit == null || hit.Position.Equals(pos);
        }
        CompanionWellbeingCare.Bed(state, key, true, reached, block.Code?.ToString() == "feralkinshipcompanions:foxbed-fancy",
            hours, entity.World.Calendar.HoursPerDay, state.Home == "Pack Cart registered");
    }
}
