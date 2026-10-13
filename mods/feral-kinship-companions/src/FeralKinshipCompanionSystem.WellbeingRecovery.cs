#nullable enable
using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;
namespace FeralKinshipCompanions;
public sealed partial class FeralKinshipCompanionSystem
{
    internal WellbeingWorkBand GetWellbeingWorkBand(Entity entity)
    {
        if (packRepository?.Loaded != true) return WellbeingWorkBand.Normal;
        string id = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        return packRepository.TryGetRecord(id, out var record) && record != null
            ? CompanionWellbeingRecovery.Update(record.Wellbeing) : WellbeingWorkBand.Normal;
    }
    internal bool AllowsWellbeingDuty(Entity entity, bool demanding = false, bool food = false, bool essential = false)
        => CompanionWellbeingRecovery.AllowsDuty(IsCompanionFoodRestricted(entity) ? WellbeingWorkBand.Recovery : GetWellbeingWorkBand(entity), demanding, food, essential);
    internal static float WellbeingDutyScanFactor(Entity entity)
    {
        var system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        return CompanionWellbeingRecovery.ScanFactor(system.GetWellbeingWorkBand(entity));
    }
    private bool CanEatWellbeingFood(Entity entity, string code)
    {
        var diet = GetCreatureDiet(entity);
        var item = entity.World.GetItem(new AssetLocation(code));
        return diet != null && item != null && IsSuitableDiningBoardFood(entity, new ItemStack(item), diet);
    }
    private (bool Meat, bool Plants) WellbeingFoodRoutes(Entity entity) => (
        new[]{"game:bushmeat-raw", "game:redmeat-raw", "game:poultry-raw", "game:fish-raw"}.Any(code => CanEatWellbeingFood(entity, code)),
        new[]{"game:fruit-blueberry", "game:fruit-redcurrant", "game:grain-rye", "game:vegetable-carrot"}.Any(code => CanEatWellbeingFood(entity, code)));
    internal bool HasWellbeingSelfProvisionDuty(Entity entity)
        => GetCompanionActivityMode(entity) == CompanionActivityMode.AtEase
            && (HasFoxStorageCargo(entity) || IsFinishedProductsEnabled(entity) && IsFinishedBerriesEnabled(entity) && WellbeingFoodRoutes(entity).Plants
                || IsGeneralStorageSortingEnabled(entity) || IsGroundCleanupEnabled(entity));
    internal bool HasPendingWellbeingFoodTrip(Entity entity)
    {
        if (packRepository?.Loaded != true) return false;
        string id = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        var trip = packRepository.GetActiveExpeditions().FirstOrDefault(e => e.SelectedFoxIds.Contains(id));
        return trip != null && HasPendingExpeditionDeparture(entity) && IsWellbeingFoodTrip(entity, trip.Type);
    }
    private bool IsWellbeingFoodTrip(Entity entity, string type)
    { var diet = WellbeingFoodRoutes(entity); return CompanionWellbeingRecovery.FoodTrip(type, diet.Meat, diet.Plants); }
    private bool AllowsWellbeingExpedition(Entity entity, string type)
    { var diet = WellbeingFoodRoutes(entity); return CompanionWellbeingRecovery.AllowsTrip(GetWellbeingWorkBand(entity), type, diet.Meat, diet.Plants); }
    private bool AllowsWellbeingHarvest(Entity entity, Block block)
    {
        var band = IsCompanionFoodRestricted(entity) ? WellbeingWorkBand.Recovery : GetWellbeingWorkBand(entity);
        // Existing ripe-block/permission/option checks still apply. At recovery, keep edible berry gathering.
        bool berries = block is not BlockCrop && block is not BlockMushroom && WellbeingFoodRoutes(entity).Plants;
        return CompanionWellbeingRecovery.AllowsDuty(band, demanding: false, food: berries);
    }
    private bool AllowsWellbeingCargo(Entity entity, ItemStack stack)
    {
        var band = GetWellbeingWorkBand(entity);
        if (band < WellbeingWorkBand.Recovery && !IsCompanionFoodRestricted(entity)) return true;
        // Food logistics can feed the whole pack, not only the carrying animal.
        return (FoxStorageRouting.Classify(stack, entity.World) & FoxStorageRouting.Category.Food) != 0;
    }
}
