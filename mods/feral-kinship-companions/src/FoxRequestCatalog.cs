#nullable enable

using System;
using System.Collections.Generic;

namespace FeralKinshipCompanions;

internal enum FoxRequestDeliveryMode
{
    None,
    ConsumeHeldItem,
    ShowHeldItem,
    PlaceBlock
}

internal sealed class FoxRequestDefinition
{
    public FoxRequestDefinition(
        string id,
        string actionText,
        float durationSeconds = 0f,
        bool experimental = false,
        bool environmentScan = false,
        FoxRequestDeliveryMode deliveryMode = FoxRequestDeliveryMode.None)
    {
        Id = id;
        ActionText = actionText;
        DurationSeconds = durationSeconds;
        Experimental = experimental;
        EnvironmentScan = environmentScan;
        DeliveryMode = deliveryMode;
    }

    public string Id { get; }
    public string ActionText { get; }
    public float DurationSeconds { get; }
    public bool Experimental { get; }
    public bool EnvironmentScan { get; }
    public FoxRequestDeliveryMode DeliveryMode { get; }
}

internal static class FoxRequestCatalog
{
    private const float PlaceDurationSeconds = 60f;

    private static readonly Dictionary<string, FoxRequestDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase)
    {
        [FoxRequestType.Predator] = new(FoxRequestType.Predator, "deal with that predator"),
        [FoxRequestType.StayClose] = new(FoxRequestType.StayClose, "stay close to me for one minute", PlaceDurationSeconds),
        [FoxRequestType.StayAway] = new(FoxRequestType.StayAway, "give me some space for one minute", PlaceDurationSeconds),
        [FoxRequestType.Inside] = new(FoxRequestType.Inside, "take me inside for one minute", PlaceDurationSeconds),
        [FoxRequestType.NearOwnerStill] = new(FoxRequestType.NearOwnerStill, "stay near me while I stand still for one minute", PlaceDurationSeconds),
        [FoxRequestType.TravelDistance] = new(FoxRequestType.TravelDistance, "travel sixteen blocks from here"),
        [FoxRequestType.HigherGround] = new(FoxRequestType.HigherGround, "reach ground three blocks higher than here"),
        [FoxRequestType.NearWater] = new(FoxRequestType.NearWater, "move near water and stay there for one minute", PlaceDurationSeconds, environmentScan: true),
        [FoxRequestType.NearLight] = new(FoxRequestType.NearLight, "stay near a light source", PlaceDurationSeconds, environmentScan: true),
        [FoxRequestType.NearHeat] = new(FoxRequestType.NearHeat, "stay near an active heat source", PlaceDurationSeconds, environmentScan: true),
        [FoxRequestType.Shelter] = new(FoxRequestType.Shelter, "stay under shelter during the rain or snow", PlaceDurationSeconds),
        [FoxRequestType.OutsideClear] = new(FoxRequestType.OutsideClear, "stay outside while the weather is clear", PlaceDurationSeconds),
        [FoxRequestType.NearTamedAnimal] = new(FoxRequestType.NearTamedAnimal, "stay near another tamed animal", PlaceDurationSeconds),
        [FoxRequestType.LargeTree] = new(FoxRequestType.LargeTree, "visit a large tree", environmentScan: true),
        [FoxRequestType.CropField] = new(FoxRequestType.CropField, "visit a farm or crop field", environmentScan: true),
        [FoxRequestType.Trader] = new(FoxRequestType.Trader, "visit a trader"),
        [FoxRequestType.MechanicalDevice] = new(FoxRequestType.MechanicalDevice, "visit a mechanical device", environmentScan: true),
        [FoxRequestType.AnotherAnimal] = new(FoxRequestType.AnotherAnimal, "visit another animal"),

        // These remain available to creative/server-control developer tools,
        // but they are deliberately excluded from normal random progression
        // until their full gameplay presentation has been tested.
        [FoxRequestType.RegularFood] = new(FoxRequestType.RegularFood, "hand me the selected regular food", experimental: true, deliveryMode: FoxRequestDeliveryMode.ConsumeHeldItem),
        [FoxRequestType.LuxuryFood] = new(FoxRequestType.LuxuryFood, "hand me the selected luxury food", experimental: true, deliveryMode: FoxRequestDeliveryMode.ConsumeHeldItem),
        [FoxRequestType.NonFoodConsumed] = new(FoxRequestType.NonFoodConsumed, "hand me the selected non-food item to chew", experimental: true, deliveryMode: FoxRequestDeliveryMode.ConsumeHeldItem),
        [FoxRequestType.NonFoodNearby] = new(FoxRequestType.NonFoodNearby, "show me the selected non-food item nearby", experimental: true, deliveryMode: FoxRequestDeliveryMode.ShowHeldItem),
        [FoxRequestType.HealingItem] = new(FoxRequestType.HealingItem, "hand me the selected healing item", experimental: true, deliveryMode: FoxRequestDeliveryMode.ConsumeHeldItem),
        [FoxRequestType.PlaceableNearby] = new(FoxRequestType.PlaceableNearby, "place the selected block nearby", experimental: true, deliveryMode: FoxRequestDeliveryMode.PlaceBlock),
        [FoxRequestType.OutsideUntilMorning] = new(FoxRequestType.OutsideUntilMorning, "stay outside continuously until morning", experimental: true),
        [FoxRequestType.InsideUntilMorning] = new(FoxRequestType.InsideUntilMorning, "stay inside continuously until morning", experimental: true)
    };

    public static string NormalizeId(string request)
    {
        return string.Equals(request, FoxRequestType.WalkDistance, StringComparison.OrdinalIgnoreCase)
            ? FoxRequestType.TravelDistance
            : request;
    }

    public static bool TryGet(string request, out FoxRequestDefinition? definition)
    {
        return Definitions.TryGetValue(NormalizeId(request), out definition);
    }

    public static FoxRequestDefinition? Get(string request)
    {
        TryGet(request, out FoxRequestDefinition? definition);
        return definition;
    }

    public static float GetDurationSeconds(string request)
    {
        return Get(request)?.DurationSeconds ?? 0f;
    }

    public static bool IsExperimental(string request)
    {
        return Get(request)?.Experimental == true;
    }

    public static bool RequiresEnvironmentScan(string request)
    {
        return Get(request)?.EnvironmentScan == true;
    }

    public static FoxRequestDeliveryMode GetDeliveryMode(string request)
    {
        return Get(request)?.DeliveryMode ?? FoxRequestDeliveryMode.None;
    }

    public static string GetActionText(string request)
    {
        return Get(request)?.ActionText ?? "None";
    }
}
