using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace AnimalicaCore;

/// <summary>
/// Keeps the reviewed fox styles and gates Core-owned styles by selected models.
/// The bridge is optional: no Carried types are referenced by Core at compile time.
/// </summary>
public sealed class AnimalicaCarriedFoxSystem : ModSystem
{
    private const string HarmonyId = "animalicacore.carriedfox";
    private const string FoxScruff = "animalica-foxscruff";
    private const string FoxWalk = "animalica-foxwalk";
    private const string LegacyFoxUnderarm = "animalica-foxunderarm";
    private const string AnimalicaWalk = "animalica-walk";
    private const string AnimalicaCradle = "animalica-cradle";
    private const string AnimalicaBabyCradle = "animalica-babycradle";
    private const string BodyCentersAsset = "animalicacore:config/animalica/carried-body-centers.json";

    private static readonly Dictionary<string, Dictionary<string, float[]>> bodyCenters =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, float[]> bodyTargets = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> loggedPlacements = new(StringComparer.OrdinalIgnoreCase);
    [ThreadStatic] private static Entity? consentRequiredFor;
    private static MethodInfo? executeCarry;
    private static MethodInfo? floorPenalty;
    private static bool penaltyWarningLogged;

    private sealed class ConsentState
    {
        public IServerPlayer Player { get; init; } = null!;
        public byte[]? PreviousSetting { get; init; }
    }

    private sealed class BodyCenterDocument
    {
        public Dictionary<string, float[]> Target { get; set; } = new();
        public Dictionary<string, Dictionary<string, float[]>> Centers { get; set; } = new();
    }

    private Harmony? harmony;

    public override double ExecuteOrder() => 0.07;

    public override void Start(ICoreAPI api)
    {
        if (!api.ModLoader.IsModEnabled("carried"))
        {
            return;
        }

        Type? behavior = AccessTools.TypeByName("Carried.EntityBehaviorCarry");
        Type? picker = AccessTools.TypeByName("Carried.CarryPicker");
        Type? modSystem = AccessTools.TypeByName("Carried.CarriedModSystem");
        Type? seat = AccessTools.TypeByName("Carried.CarrySeat");
        MethodInfo? canBegin = behavior == null ? null : AccessTools.Method(behavior, "CanBeginCarry");
        MethodInfo? switchType = behavior == null ? null : AccessTools.Method(behavior, "SwitchType");
        MethodInfo? updateCarry = behavior == null ? null : AccessTools.Method(behavior, "UpdateCarry");
        MethodInfo? entries = picker == null ? null : AccessTools.Method(picker, "Entries");
        MethodInfo? startCarry = modSystem == null ? null : AccessTools.Method(modSystem, "StartCarry");
        MethodInfo? execute = modSystem == null ? null : AccessTools.Method(modSystem, "ExecuteCarry");
        MethodInfo? applyPenalty = behavior == null ? null : AccessTools.Method(behavior, "ApplyMovementPenalty");
        MethodInfo? floor = behavior == null ? null : AccessTools.Method(behavior, "FloorPenalty");
        MethodInfo? skipConsent = behavior == null ? null : AccessTools.PropertyGetter(behavior, "SkipsConsent");
        MethodInfo? seatPosition = seat == null ? null : AccessTools.PropertyGetter(seat, "SeatPosition");
        if (canBegin == null || switchType == null || updateCarry == null || entries == null ||
            startCarry == null || execute == null || applyPenalty == null || floor == null ||
            skipConsent == null || seatPosition == null)
        {
            api.Logger.Error("[AnimalicaCore] Carried fox bridge could not find the expected Carried 2.1.0 methods.");
            return;
        }

        try
        {
            executeCarry = execute;
            floorPenalty = floor;
            harmony = new Harmony(HarmonyId);
            harmony.Patch(canBegin, prefix: new HarmonyMethod(typeof(AnimalicaCarriedFoxSystem), nameof(CanBeginPrefix)));
            harmony.Patch(switchType, prefix: new HarmonyMethod(typeof(AnimalicaCarriedFoxSystem), nameof(SwitchTypePrefix)));
            harmony.Patch(updateCarry, prefix: new HarmonyMethod(typeof(AnimalicaCarriedFoxSystem), nameof(UpdateCarryPrefix)));
            harmony.Patch(startCarry,
                prefix: new HarmonyMethod(typeof(AnimalicaCarriedFoxSystem), nameof(StartCarryPrefix)),
                postfix: new HarmonyMethod(typeof(AnimalicaCarriedFoxSystem), nameof(StartCarryPostfix)),
                finalizer: new HarmonyMethod(typeof(AnimalicaCarriedFoxSystem), nameof(StartCarryFinalizer)));
            harmony.Patch(skipConsent, prefix: new HarmonyMethod(typeof(AnimalicaCarriedFoxSystem), nameof(SkipsConsentPrefix)));
            harmony.Patch(applyPenalty, prefix: new HarmonyMethod(typeof(AnimalicaCarriedFoxSystem), nameof(ApplyMovementPenaltyPrefix)));
            harmony.Patch(seatPosition, postfix: new HarmonyMethod(typeof(AnimalicaCarriedFoxSystem), nameof(SeatPositionPostfix)));
            if (api.Side == EnumAppSide.Client)
            {
                harmony.Patch(entries, postfix: new HarmonyMethod(typeof(AnimalicaCarriedFoxSystem), nameof(EntriesPostfix)));
            }
            api.Logger.Notification("[AnimalicaCore] Carried Animalica style rules enabled for Carried 2.1.0.");
        }
        catch (Exception exception)
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            api.Logger.Error("[AnimalicaCore] Could not enable Carried fox style rules: {0}", exception);
        }
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
        executeCarry = null;
        floorPenalty = null;
        penaltyWarningLogged = false;
        consentRequiredFor = null;
        bodyCenters.Clear();
        bodyTargets.Clear();
        loggedPlacements.Clear();
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (!api.ModLoader.IsModEnabled("carried"))
        {
            return;
        }

        BodyCenterDocument? loaded = api.Assets
            .TryGet(new AssetLocation(BodyCentersAsset))
            ?.ToObject<BodyCenterDocument>();
        bodyCenters.Clear();
        bodyTargets.Clear();
        if (loaded == null)
        {
            api.Logger.Error("[AnimalicaCore] Carried body centering asset is missing.");
            return;
        }

        foreach ((string style, float[] point) in loaded.Target)
        {
            if (point.Length == 3 && Array.TrueForAll(point, float.IsFinite))
            {
                bodyTargets[style] = point;
            }
        }

        foreach ((string model, Dictionary<string, float[]> styles) in loaded.Centers)
        {
            if (styles.TryGetValue(AnimalicaCradle, out float[]? cradle) && cradle.Length == 3 &&
                Array.TrueForAll(cradle, float.IsFinite) &&
                styles.TryGetValue(AnimalicaBabyCradle, out float[]? baby) && baby.Length == 3 &&
                Array.TrueForAll(baby, float.IsFinite))
            {
                bodyCenters[model] = styles;
            }
        }

        api.Logger.Notification("[AnimalicaCore] Loaded {0} Animalica cradle body centers and {1} arm targets.",
            bodyCenters.Count, bodyTargets.Count);
    }

    private static bool CanBeginPrefix(object __instance, Entity? __0, object __1, ref string? __result)
    {
        if (!Denied(Carrier(__instance), __0, TypeCode(__1)))
        {
            return true;
        }

        __result = "carried:type-denied";
        return false;
    }

    private static bool SwitchTypePrefix(object __instance, object __0, ref string? __result)
    {
        Entity? carrier = Carrier(__instance);
        Entity? passenger = Target(carrier);
        if (!Denied(carrier, passenger, TypeCode(__0)))
        {
            return true;
        }

        __result = "carried:type-denied";
        return false;
    }

    private static bool StartCarryPrefix(object __instance, IServerPlayer? __0, Entity? __1, object? __2,
        ref TextCommandResult __result, out ConsentState? __state)
    {
        __state = null;
        Entity? carrier = __0?.Entity;
        string style = TypeCode(__2);
        if (Denied(carrier, __1, style))
        {
            __result = TextCommandResult.Error(Lang.Get("carried:type-denied"), "");
            return false;
        }

        if (carrier == null || __1 is not EntityPlayer passenger ||
            !IsModel(carrier.WatchedAttributes.GetString("skinModel", "seraph"), "seraph"))
        {
            return true;
        }

        string model = passenger.WatchedAttributes.GetString("skinModel", string.Empty);
        if (style == FoxScruff && IsModel(model, "feralfox"))
        {
            // ExecuteCarry still runs Carried's range, hand, health and target checks.
            if (executeCarry == null || carrier is not EntityPlayer player)
            {
                __result = TextCommandResult.Error(Lang.Get("carried:carry-failed"), "");
                return false;
            }

            try
            {
                __result = executeCarry.Invoke(__instance, new object?[] { player, passenger, __2 })
                    as TextCommandResult ?? TextCommandResult.Error(Lang.Get("carried:carry-failed"), "");
            }
            catch (Exception exception)
            {
                carrier.Api?.Logger.Warning("[AnimalicaCore] Scruff pickup failed: {0}", exception);
                __result = TextCommandResult.Error(Lang.Get("carried:carry-failed"), "");
            }
            return false;
        }

        if (!AnimalicaCoreSystem.IsKnownAnimalicaModelCode(model) ||
            style is not (FoxWalk or AnimalicaWalk or AnimalicaCradle or AnimalicaBabyCradle) ||
            passenger.Player is not IServerPlayer targetPlayer)
        {
            return true;
        }

        // Carried's request path checks a player setting and can exempt downed
        // targets. Temporarily force its usual request path for these styles.
        __state = new ConsentState
        {
            Player = targetPlayer,
            PreviousSetting = targetPlayer.GetModdata("carriedConsent")
        };
        targetPlayer.SetModData("carriedConsent", true);
        consentRequiredFor = passenger;
        return true;
    }

    private static void StartCarryPostfix(ConsentState? __state) => RestoreConsent(__state);

    private static Exception? StartCarryFinalizer(Exception? __exception, ConsentState? __state)
    {
        RestoreConsent(__state);
        return __exception;
    }

    private static void RestoreConsent(ConsentState? state)
    {
        if (state == null)
        {
            return;
        }

        consentRequiredFor = null;
        if (state.PreviousSetting == null)
        {
            state.Player.RemoveModdata("carriedConsent");
        }
        else
        {
            state.Player.SetModdata("carriedConsent", state.PreviousSetting);
        }
    }

    private static bool SkipsConsentPrefix(object __instance, ref bool __result)
    {
        if (consentRequiredFor == null ||
            !ReferenceEquals(Carrier(__instance), consentRequiredFor))
        {
            return true;
        }

        __result = false;
        return false;
    }

    private static bool ApplyMovementPenaltyPrefix(object __instance, object __0, Entity? __1,
        ref float ___appliedPenalty)
    {
        Entity? carrier = Carrier(__instance);
        if (carrier == null || __1 == null ||
            !IsModel(carrier.WatchedAttributes.GetString("skinModel", "seraph"), "seraph"))
        {
            return true;
        }

        string model = __1.WatchedAttributes.GetString("skinModel", string.Empty);
        string style = TypeCode(__0);
        if (!AnimalicaCoreSystem.IsKnownAnimalicaModelCode(model) ||
            style is not (FoxWalk or AnimalicaWalk or FoxScruff or AnimalicaCradle or AnimalicaBabyCradle))
        {
            return true;
        }

        try
        {
            float penalty = CarryPenalty(style, model, carrier, __1);
            if (floorPenalty?.Invoke(__instance, new object[] { penalty }) is not float floored)
            {
                return true;
            }

            if (Math.Abs(floored - ___appliedPenalty) >= 0.0001f)
            {
                ___appliedPenalty = floored;
                carrier.Stats.Set("walkspeed", "carrying", floored, false);
            }
            return false;
        }
        catch (Exception exception)
        {
            if (!penaltyWarningLogged)
            {
                penaltyWarningLogged = true;
                carrier.Api?.Logger.Warning("[AnimalicaCore] Could not apply Animalica carry weight; using Carried's penalty: {0}", exception);
            }
            return true;
        }
    }

    private static float CarryPenalty(string style, string model, Entity carrier, Entity passenger)
    {
        if (style is FoxWalk or AnimalicaWalk)
        {
            return 0f;
        }

        string name = model[(model.LastIndexOf(':') + 1)..].ToLowerInvariant();
        float basePenalty = name switch
        {
            "feralfish" => 0f,
            "feralfox" or "feralraccoon" or "deer-pudu" => -0.08f,
            "chicken-hen" or "chicken-rooster" or "hare-female" or "hare-male" => -0.04f,
            "polarbear" or "brownbear" or "deer-moose" or "deer-elk" or "pig-eurasian-elder" => -0.75f,
            "blackbear" or "giantpanda" or "goat-muskox" => -0.65f,
            "sunbear" or "goat-takingold" => -0.50f,
            _ => -0.30f
        };

        // Model identity gives the weight tier; small randomized size changes
        // then make a proportionate adjustment within that tier.
        float carrierSize = carrier.Properties?.Client?.Size ?? 1f;
        float passengerSize = passenger.Properties?.Client?.Size ?? 1f;
        if (!float.IsFinite(carrierSize) || carrierSize <= 0f ||
            !float.IsFinite(passengerSize) || passengerSize <= 0f)
        {
            return basePenalty;
        }

        return Math.Max(-1f, basePenalty * Math.Clamp(passengerSize / carrierSize, 0.75f, 1.5f));
    }

    private static bool UpdateCarryPrefix(object __instance)
    {
        Entity? carrier = Carrier(__instance);
        if (carrier?.World?.Side != EnumAppSide.Server)
        {
            return true;
        }

        Entity? passenger = Target(carrier);
        string code = carrier.WatchedAttributes.GetString("carryTypeCode", string.Empty);
        if (passenger == null || !Denied(carrier, passenger, code))
        {
            return true;
        }

        AccessTools.Method(__instance.GetType(), "StopCarry")?.Invoke(__instance, null);
        return false;
    }

    private static void SeatPositionPostfix(IMountableSeat __instance, EntityPos __result)
    {
        Entity carrier = __instance.Entity;
        Entity passenger = __instance.Passenger;
        if (carrier == null || passenger == null || __result == null)
        {
            return;
        }

        string style = carrier.WatchedAttributes.GetString("carryTypeCode", string.Empty);
        if (style is not (AnimalicaCradle or AnimalicaBabyCradle))
        {
            return;
        }

        string model = passenger.WatchedAttributes.GetString("skinModel", string.Empty);
        // Keep the visually reviewed fox placement exactly as Carried supplied it.
        if (IsModel(model, "feralfox") ||
            !TryBodyCenter(model, style, out float[]? center) || center == null ||
            !bodyTargets.TryGetValue(style, out float[]? target))
        {
            return;
        }

        float renderSize = passenger.Properties?.Client?.Size ?? 1f;
        if (!float.IsFinite(renderSize) || renderSize <= 0f)
        {
            renderSize = 1f;
        }

        float carrierSize = carrier.Properties?.Client?.Size ?? 1f;
        if (!float.IsFinite(carrierSize) || carrierSize <= 0f)
        {
            carrierSize = 1f;
        }

        // Shape coordinates are centered by the renderer at X/Z = 0.5.
        // Its yaw adds 90 degrees, so shape X is forward and shape Z is right.
        double forward = target[0] * carrierSize - (center[0] - 0.5f) * renderSize;
        double up = target[1] * carrierSize - center[1] * renderSize;
        double right = target[2] * carrierSize - (center[2] - 0.5f) * renderSize;

        double baseX = carrier.Pos.X;
        double baseY = carrier.Pos.Y;
        double baseZ = carrier.Pos.Z;
        if (carrier is EntityPlayer player && carrier.Api is ICoreClientAPI client &&
            ReferenceEquals(client.World?.Player?.Entity, carrier))
        {
            baseX = player.CameraPos.X;
            baseY = player.CameraPos.Y - carrier.Pos.DimensionYAdjustment;
            baseZ = player.CameraPos.Z;
        }

        double sin = Math.Sin(__result.Yaw);
        double cos = Math.Cos(__result.Yaw);
        __result.SetPos(baseX - sin * forward + cos * right,
            baseY + up,
            baseZ - cos * forward - sin * right);

        if (carrier.World?.Side == EnumAppSide.Client && loggedPlacements.Add(model + "|" + style))
        {
            carrier.Api?.Logger.Notification("[AnimalicaCore] Centered {0} with {1} at the cradle arm target.", model, style);
        }
    }

    private static bool TryBodyCenter(string model, string style, out float[]? center)
    {
        center = null;
        if (bodyCenters.TryGetValue(model, out Dictionary<string, float[]>? styles))
        {
            return styles.TryGetValue(style, out center);
        }

        if (model.IndexOf(':') >= 0)
        {
            return false;
        }

        foreach ((string key, Dictionary<string, float[]> value) in bodyCenters)
        {
            if (key.EndsWith(":" + model, StringComparison.OrdinalIgnoreCase))
            {
                return value.TryGetValue(style, out center);
            }
        }

        return false;
    }

    private static void EntriesPostfix(object __instance, object __result)
    {
        if (__result is not IList entries ||
            AccessTools.Field(__instance.GetType(), "capi")?.GetValue(__instance) is not ICoreClientAPI api ||
            AccessTools.Field(__instance.GetType(), "passenger")?.GetValue(__instance) is true ||
            AccessTools.Field(__instance.GetType(), "targetEntityId")?.GetValue(__instance) is not long targetId)
        {
            return;
        }

        Entity? carrier = api.World?.Player?.Entity;
        Entity? passenger = api.World?.GetEntityById(targetId);
        for (int index = entries.Count - 1; index >= 0; index--)
        {
            object? entry = entries[index];
            string code = TypeCode(entry);
            if (code == "carried:letgo")
            {
                continue;
            }

            if (Denied(carrier, passenger, code))
            {
                entries.RemoveAt(index);
            }
        }
    }

    private static Entity? Carrier(object instance) =>
        AccessTools.Property(instance.GetType(), "OnEntity")?.GetValue(instance) as Entity;

    private static Entity? Target(Entity? carrier)
    {
        if (carrier?.World == null)
        {
            return null;
        }

        long id = carrier.WatchedAttributes.GetLong("carryTargetId", 0);
        return id == 0 ? null : carrier.World.GetEntityById(id);
    }

    private static string TypeCode(object? type) =>
        type == null ? string.Empty :
        AccessTools.Property(type.GetType(), "Code")?.GetValue(type) as string ?? string.Empty;

    private static bool Denied(Entity? carrier, Entity? passenger, string code)
    {
        bool custom = code is FoxScruff or FoxWalk or LegacyFoxUnderarm or
            AnimalicaWalk or AnimalicaCradle or AnimalicaBabyCradle;
        if (carrier == null || passenger == null)
        {
            return custom;
        }

        string carrierModel = carrier.WatchedAttributes.GetString("skinModel", "seraph");
        string passengerModel = passenger.WatchedAttributes.GetString("skinModel", "seraph");
        bool carrierFox = IsModel(carrierModel, "feralfox");
        bool passengerFox = IsModel(passengerModel, "feralfox");
        bool carrierSeraph = IsModel(carrierModel, "seraph");
        bool passengerSeraph = IsModel(passengerModel, "seraph");

        if (carrierFox && (passengerFox || passengerSeraph))
        {
            return custom || code is "firefighter" or "piggyback";
        }

        if (carrierSeraph && AnimalicaCoreSystem.IsKnownAnimalicaModelCode(passengerModel))
        {
            // Stock humanoid carries distort Animalica passengers. Keep the
            // accepted styles on an explicit allowlist, including fox's
            // separately named copy of its original handhold pose.
            return passengerFox
                ? code is not (FoxWalk or FoxScruff or AnimalicaCradle or AnimalicaBabyCradle)
                : code is not (AnimalicaWalk or AnimalicaCradle or AnimalicaBabyCradle);
        }

        return custom;
    }

    internal static bool IsModel(string? code, string model)
    {
        if (string.Equals(model, "seraph", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(code) ||
                string.Equals(code, "seraph", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "playermodellib:seraph", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(model, "feralfox", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(code, "feralfox", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(code, "feralfox:feralfox", StringComparison.OrdinalIgnoreCase));
    }
}
