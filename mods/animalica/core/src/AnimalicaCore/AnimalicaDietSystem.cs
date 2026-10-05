using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace AnimalicaCore;

public sealed class AnimalicaDietSystem : ModSystem
{
    private const string HarmonyId = "animalicacore.modular-diets";
    private const string DietAssetCode = "animalicacore:config/animalica/diets.json";
    private const string PlayerModelSystemTypeName = "PlayerModelLib.CustomModelsSystem";

    private static readonly ConditionalWeakTable<ICoreAPI, AnimalicaDietSystem> Instances = new();
    [ThreadStatic]
    private static int storageInteractionDepth;
    [ThreadStatic]
    private static int heldUseTraceDepth;

    private struct HeldUseInteractionState
    {
        public bool TraceEntered;
        public bool StorageEntered;
    }

    private readonly Dictionary<string, AnimalicaResolvedDiet> modelProfiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AnimalicaResolvedDiet> resolvedProfiles = new(StringComparer.Ordinal);
    private readonly HashSet<string> warnedProfileKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AnimalicaFoodDefinition> exactFoods = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AnimalicaFoodDefinition> wildcardFoods = new();
    private readonly Dictionary<string, Dictionary<string, decimal>> traitAffinities = new(StringComparer.Ordinal);
    private readonly HashSet<string> primaryTraits = new(StringComparer.Ordinal);
    private Harmony? harmony;
    private ICoreAPI? api;

    public override double ExecuteOrder() => 0.061;

    public override bool ShouldLoad(EnumAppSide forSide) => true;

    public override void Start(ICoreAPI coreApi)
    {
        api = coreApi;
        Instances.Remove(coreApi);
        Instances.Add(coreApi, this);
        MethodInfo? nutritionMethod = typeof(CollectibleObject).GetMethod(
            nameof(CollectibleObject.GetNutritionProperties),
            BindingFlags.Instance | BindingFlags.Public
        );
        MethodInfo? heldUseMethod = typeof(CollectibleObject).GetMethod(
            nameof(CollectibleObject.OnHeldUseStart),
            BindingFlags.Instance | BindingFlags.Public
        );
        MethodInfo? eatBeginMethod = typeof(CollectibleObject).GetMethod(
            "tryEatBegin",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        MethodInfo? nutritionPostfix = AccessTools.Method(typeof(AnimalicaDietSystem), nameof(AfterGetNutritionProperties));
        MethodInfo? heldUsePrefix = AccessTools.Method(
            typeof(AnimalicaDietSystem),
            nameof(BeforeCollectibleHeldUseStart)
        );
        MethodInfo? eatBeginPrefix = AccessTools.Method(
            typeof(AnimalicaDietSystem),
            nameof(BeforeCollectibleTryEatBegin)
        );
        if (nutritionMethod == null
            || heldUseMethod == null
            || eatBeginMethod == null
            || nutritionPostfix == null
            || heldUsePrefix == null
            || eatBeginPrefix == null)
        {
            coreApi.Logger.Error(
                "[AnimalicaCore] Could not install the modular diet hooks; GetNutritionProperties, " +
                "OnHeldUseStart, or the collectible eat-start method was not found."
            );
            return;
        }

        harmony = new Harmony(HarmonyId);
        bool nutritionHookInstalled = Harmony.GetPatchInfo(nutritionMethod)?.Postfixes.Any(
            patch => string.Equals(patch.owner, HarmonyId, StringComparison.Ordinal)
        ) == true;
        if (!nutritionHookInstalled)
        {
            // Trait Restrictions writes the positive food override first. Core
            // then enforces the server switch and stack-dependent exceptions.
            harmony.Patch(nutritionMethod, postfix: new HarmonyMethod(nutritionPostfix)
            {
                priority = Priority.Low
            });
        }

        Patches? heldUsePatches = Harmony.GetPatchInfo(heldUseMethod);
        bool heldUseHookInstalled = heldUsePatches?.Prefixes.Any(
            patch => string.Equals(patch.owner, HarmonyId, StringComparison.Ordinal)
        ) == true;
        bool heldUseFinalizerInstalled = heldUsePatches?.Finalizers.Any(
            patch => string.Equals(patch.owner, HarmonyId, StringComparison.Ordinal)
        ) == true;
        if (!heldUseHookInstalled || !heldUseFinalizerInstalled)
        {
            MethodInfo? heldUseFinalizer = AccessTools.Method(
                typeof(AnimalicaDietSystem),
                nameof(AfterCollectibleHeldUseStart)
            );
            if (heldUseFinalizer == null)
            {
                coreApi.Logger.Error("[AnimalicaCore] Could not install the held-use interaction scope finalizer.");
                return;
            }

            harmony.Patch(
                heldUseMethod,
                prefix: heldUseHookInstalled ? null : new HarmonyMethod(heldUsePrefix),
                finalizer: heldUseFinalizerInstalled ? null : new HarmonyMethod(heldUseFinalizer)
            );
        }

        bool eatBeginHookInstalled = Harmony.GetPatchInfo(eatBeginMethod)?.Prefixes.Any(
            patch => string.Equals(patch.owner, HarmonyId, StringComparison.Ordinal)
        ) == true;
        if (!eatBeginHookInstalled)
        {
            harmony.Patch(eatBeginMethod, prefix: new HarmonyMethod(eatBeginPrefix));
        }

        bool nutritionPostfixInstalled = Harmony.GetPatchInfo(nutritionMethod)?.Postfixes.Any(
            patch => string.Equals(patch.owner, HarmonyId, StringComparison.Ordinal)
        ) == true;
        bool heldUsePrefixInstalled = Harmony.GetPatchInfo(heldUseMethod)?.Prefixes.Any(
            patch => string.Equals(patch.owner, HarmonyId, StringComparison.Ordinal)
        ) == true;
        bool eatBeginPrefixInstalled = Harmony.GetPatchInfo(eatBeginMethod)?.Prefixes.Any(
            patch => string.Equals(patch.owner, HarmonyId, StringComparison.Ordinal)
        ) == true;
        if (nutritionPostfixInstalled && heldUsePrefixInstalled && eatBeginPrefixInstalled)
        {
            coreApi.Logger.Notification(
                "[AnimalicaCore][DietUseTrace] Hooks verified: GetNutritionProperties={0}, OnHeldUseStart={1}, " +
                "tryEatBegin={2}.",
                nutritionPostfixInstalled,
                heldUsePrefixInstalled,
                eatBeginPrefixInstalled
            );
        }
        else
        {
            coreApi.Logger.Error(
                "[AnimalicaCore][DietUseTrace] Hook verification failed: GetNutritionProperties={0}, " +
                "OnHeldUseStart={1}, tryEatBegin={2}.",
                nutritionPostfixInstalled,
                heldUsePrefixInstalled,
                eatBeginPrefixInstalled
            );
        }
    }

    public override void AssetsLoaded(ICoreAPI coreApi)
    {
        exactFoods.Clear();
        wildcardFoods.Clear();
        traitAffinities.Clear();
        primaryTraits.Clear();
        modelProfiles.Clear();
        resolvedProfiles.Clear();
        warnedProfileKeys.Clear();

        IAsset? asset = coreApi.Assets.TryGet(new AssetLocation(DietAssetCode));
        AnimalicaDietDocument? document = asset?.ToObject<AnimalicaDietDocument>();
        if (document == null)
        {
            coreApi.Logger.Error("[AnimalicaCore] Modular diet data is missing or invalid: {0}.", DietAssetCode);
            return;
        }

        foreach (string trait in document.PrimaryTraits.Where(code => !string.IsNullOrWhiteSpace(code)))
        {
            primaryTraits.Add(trait);
        }

        var foodTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AnimalicaFoodDefinition definition in document.Foods)
        {
            if (!definition.TryNormalize(coreApi.Logger, out bool wildcard))
            {
                continue;
            }

            foodTags.Add(definition.Tag);

            if (wildcard)
            {
                wildcardFoods.Add(definition);
            }
            else if (!exactFoods.TryAdd(definition.Code, definition))
            {
                coreApi.Logger.Error("[AnimalicaCore] Duplicate animal food code '{0}'.", definition.Code);
            }
        }

        foreach ((string traitCode, Dictionary<string, string> affinities) in document.TraitAffinities)
        {
            var normalized = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach ((string foodTag, string level) in affinities)
            {
                if (!foodTags.Contains(foodTag))
                {
                    coreApi.Logger.Error(
                        "[AnimalicaCore] Diet trait '{0}' references unknown animal food tag '{1}'.",
                        traitCode,
                        foodTag
                    );
                    continue;
                }

                if (!document.AffinityMultipliers.TryGetValue(level, out decimal multiplier)
                    || multiplier <= 0m
                    || multiplier > 1m)
                {
                    coreApi.Logger.Error(
                        "[AnimalicaCore] Diet trait '{0}' uses unknown or invalid affinity '{1}'.",
                        traitCode,
                        level
                    );
                    continue;
                }

                normalized[foodTag] = multiplier;
            }

            traitAffinities[traitCode] = normalized;
        }

        wildcardFoods.Sort((left, right) => right.MatchPrefix.Length.CompareTo(left.MatchPrefix.Length));
        coreApi.Logger.Notification(
            "[AnimalicaCore] Modular diet data loaded: {0} animal foods and {1} affinity traits.",
            exactFoods.Count + wildcardFoods.Count,
            traitAffinities.Count
        );
    }

    public override void Dispose()
    {
        if (api != null)
        {
            Instances.Remove(api);
        }

        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
        api = null;
        exactFoods.Clear();
        wildcardFoods.Clear();
        traitAffinities.Clear();
        primaryTraits.Clear();
        modelProfiles.Clear();
        resolvedProfiles.Clear();
        warnedProfileKeys.Clear();
    }

    private static void AfterGetNutritionProperties(
        CollectibleObject __instance,
        IWorldAccessor world,
        ItemStack itemstack,
        Entity forEntity,
        ref FoodNutritionProperties? __result)
    {
        if (world?.Api != null && Instances.TryGetValue(world.Api, out AnimalicaDietSystem? system))
        {
            bool trace = forEntity is EntityPlayer && heldUseTraceDepth > 0;
            bool hadNutrition = __result != null;
            bool dietFood = trace && system.TryGetFood(__instance, itemstack, out _);
            system.ApplyAnimalFoodNutrition(__instance, itemstack, forEntity, ref __result);
            if (trace)
            {
                system.api?.Logger.Notification(
                    "[AnimalicaCore][DietUseTrace] nutrition item={0}; class={1}; dietFood={2}; " +
                    "scopeDepth={3}; before={4}; after={5}.",
                    itemstack.Collectible.Code?.ToString() ?? "<unknown>",
                    __instance.GetType().FullName ?? __instance.GetType().Name,
                    dietFood,
                    storageInteractionDepth,
                    hadNutrition,
                    __result != null
                );
            }
        }
    }

    private static void BeforeCollectibleHeldUseStart(
        CollectibleObject __instance,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection? blockSel,
        EnumHandInteract useType,
        bool firstEvent,
        out HeldUseInteractionState __state)
    {
        __state = default;
        if (heldUseTraceDepth <= 0
            || byEntity is not EntityPlayer player
            || slot.Empty
            || !Instances.TryGetValue(player.Api, out AnimalicaDietSystem? system)
            || !system.IsAnimalicaPlayer(player)
            || useType != EnumHandInteract.HeldItemInteract)
        {
            __state = default;
            return;
        }

        heldUseTraceDepth++;
        __state.TraceEntered = true;
        bool dietFood = system.TryGetFood(__instance, slot.Itemstack, out _);
        Block? targetBlock = blockSel?.Position == null
            ? null
            : player.World.BlockAccessor.GetBlock(blockSel.Position);
        string? whitelistAttribute = targetBlock?.Attributes?["worldInteractionAttributeCheck"].AsString(null);
        bool nutritionEnabled = system.IsAnimalFoodNutritionEnabled();
        bool scope = dietFood
            && nutritionEnabled
            && !string.IsNullOrWhiteSpace(whitelistAttribute);

        if (firstEvent)
        {
            system.api?.Logger.Notification(
                "[AnimalicaCore][DietUseTrace] held-use item={0}; class={1}; use={2}; target={3}; " +
                "whitelistAttribute={4}; dietFood={5}; nutritionEnabled={6}; scope={7}.",
                slot.Itemstack.Collectible.Code?.ToString() ?? "<unknown>",
                __instance.GetType().FullName ?? __instance.GetType().Name,
                useType,
                targetBlock?.Code?.ToString() ?? "<none>",
                whitelistAttribute ?? "<none>",
                dietFood,
                nutritionEnabled,
                scope
            );
        }

        if (!scope)
        {
            return;
        }

        storageInteractionDepth++;
        __state.StorageEntered = true;
    }

    private static Exception? AfterCollectibleHeldUseStart(
        Exception? __exception,
        HeldUseInteractionState __state)
    {
        if (__state.StorageEntered && storageInteractionDepth > 0)
        {
            storageInteractionDepth--;
        }

        if (__state.TraceEntered && heldUseTraceDepth > 0)
        {
            heldUseTraceDepth--;
        }

        return __exception;
    }

    private static bool BeforeCollectibleTryEatBegin(
        CollectibleObject __instance,
        ItemSlot slot,
        EntityAgent byEntity,
        ref EnumHandHandling handling)
    {
        if (byEntity is not EntityPlayer player
            || slot.Empty
            || !Instances.TryGetValue(player.Api, out AnimalicaDietSystem? system)
            || !system.IsAnimalicaPlayer(player))
        {
            return true;
        }

        bool dietFood = system.TryGetFood(__instance, slot.Itemstack, out _);
        bool suppressEat = storageInteractionDepth > 0 && dietFood;
        system.api?.Logger.Notification(
            "[AnimalicaCore][DietUseTrace] tryEatBegin item={0}; class={1}; dietFood={2}; " +
            "scopeDepth={3}; decision={4}.",
            slot.Itemstack.Collectible.Code?.ToString() ?? "<unknown>",
            __instance.GetType().FullName ?? __instance.GetType().Name,
            dietFood,
            storageInteractionDepth,
            suppressEat ? "suppress" : "allow"
        );
        if (!suppressEat)
        {
            return true;
        }

        handling = EnumHandHandling.NotHandled;
        return false;
    }

    private void ApplyAnimalFoodNutrition(
        CollectibleObject instance,
        ItemStack itemstack,
        Entity? forEntity,
        ref FoodNutritionProperties? result)
    {
        // Vanilla and other mods use context-free nutrition queries to test
        // whether an item is food and to inspect its ordinary category. Keep
        // those queries on the collectible's original nutrition properties;
        // species-specific values apply only when a player is the eater.
        if (forEntity is not EntityPlayer player)
        {
            return;
        }

        // Other race mods can use the same items and their own Trait
        // Restrictions values. Core's compatibility and denial rules apply
        // only to models that Animalica actually owns or adapts.
        if (!IsAnimalicaPlayer(player))
        {
            return;
        }

        if (!TryGetFood(instance, itemstack, out AnimalicaFoodDefinition? food))
        {
            return;
        }

        if (!IsAnimalFoodNutritionEnabled())
        {
            // These entries are normally inedible vanilla items. Leaving the
            // result null restores that behavior when the server-owned Core
            // switch is disabled.
            result = null;
            return;
        }

        AnimalicaFoodDefinition resolvedFood = food!;

        decimal affinity = 0m;
        if (TryResolvePlayerDiet(player, out AnimalicaResolvedDiet? diet)
            && diet != null)
        {
            diet.Affinities.TryGetValue(resolvedFood.Tag, out affinity);
        }

        if (affinity <= 0m)
        {
            // These items have no ordinary vanilla nutrition. Blocking eating
            // here leaves feeding, ground storage and shelf use available.
            result = null;
            return;
        }

        // Trait Restrictions owns the normal code-only positive path. If it
        // supplied no value (for example, a trait added after model loading),
        // Core supplies the same positive value as a compatibility fallback.
        // A food with a stack condition (currently pressed mash) stays in Core.
        if (resolvedFood.StackAttributeAtMost == null && result != null)
        {
            return;
        }

        FoodNutritionProperties nutrition = result?.Clone() ?? new FoodNutritionProperties();
        nutrition.Satiety = (float)Math.Round(
            (decimal)resolvedFood.BaseSatiety * affinity,
            MidpointRounding.AwayFromZero
        );
        nutrition.FoodCategory = resolvedFood.ParsedFoodCategory;
        nutrition.Health = 0f;
        nutrition.Intoxication = 0f;
        nutrition.Psychedelic = 0f;
        nutrition.EatenStack = null;
        result = nutrition;
    }

    private bool IsAnimalFoodNutritionEnabled()
    {
        AnimalicaCoreSystem? coreSystem = api?.ModLoader.GetModSystem<AnimalicaCoreSystem>();
        return coreSystem == null || coreSystem.AnimalFoodNutritionEnabled;
    }

    private bool IsAnimalicaPlayer(EntityPlayer player)
    {
        string modelCode = player.WatchedAttributes.GetString("skinModel", "seraph") ?? "seraph";
        AnimalicaCoreSystem? coreSystem = api?.ModLoader.GetModSystem<AnimalicaCoreSystem>();
        return coreSystem?.IsAnimalicaModelCode(modelCode) == true;
    }

    private bool TryGetFood(
        CollectibleObject instance,
        ItemStack itemstack,
        out AnimalicaFoodDefinition? definition)
    {
        AssetLocation? code = itemstack?.Collectible?.Code ?? instance?.Code;
        if (code == null)
        {
            definition = null;
            return false;
        }

        string fullCode = code.Domain + ":" + code.Path;
        if (exactFoods.TryGetValue(fullCode, out definition) && definition.MatchesStack(itemstack))
        {
            return true;
        }

        foreach (AnimalicaFoodDefinition wildcard in wildcardFoods)
        {
            if (wildcard.Matches(fullCode) && wildcard.MatchesStack(itemstack))
            {
                definition = wildcard;
                return true;
            }
        }

        definition = null;
        return false;
    }

    private bool TryResolvePlayerDiet(EntityPlayer player, out AnimalicaResolvedDiet? profile)
    {
        string modelCode = player.WatchedAttributes.GetString("skinModel", "seraph") ?? "seraph";
        if (!TryGetModelProfile(player, modelCode, out AnimalicaResolvedDiet? modelProfile))
        {
            profile = null;
            return false;
        }

        string[] extraTraits = player.WatchedAttributes.GetStringArray("extraTraits", Array.Empty<string>())
            ?? Array.Empty<string>();
        string[] relevantExtraTraits = extraTraits
            .Where(trait => traitAffinities.ContainsKey(trait)
                || primaryTraits.Contains(trait)
                || trait.StartsWith(AnimalicaTraitRestrictionsFoodBridge.CombinedPrefix, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(trait => trait, StringComparer.Ordinal)
            .ToArray();
        if (relevantExtraTraits.Length == 0)
        {
            profile = modelProfile;
            return true;
        }

        string profileKey = modelCode + "\u001f" + string.Join("\u001f", relevantExtraTraits);
        if (!resolvedProfiles.TryGetValue(profileKey, out profile))
        {
            profile = BuildProfile(profileKey, modelProfile!.Traits.Concat(relevantExtraTraits));
            resolvedProfiles[profileKey] = profile;
        }

        return true;
    }

    private bool TryGetModelProfile(
        EntityPlayer player,
        string modelCode,
        out AnimalicaResolvedDiet? profile)
    {
        if (modelProfiles.TryGetValue(modelCode, out profile))
        {
            return true;
        }

        object? modelSystem = player.Api.ModLoader.GetModSystem(PlayerModelSystemTypeName);
        object? modelsValue = GetMemberValue(modelSystem, "CustomModels");
        if (modelsValue is not IDictionary models || !models.Contains(modelCode) || models[modelCode] == null)
        {
            profile = null;
            return false;
        }

        object model = models[modelCode]!;
        string[] modelTraits = GetMemberValue(model, "ExtraTraits") switch
        {
            string[] array => array,
            IEnumerable<string> enumerable => enumerable.ToArray(),
            _ => Array.Empty<string>()
        };
        profile = BuildProfile(modelCode, modelTraits);
        modelProfiles[modelCode] = profile;
        return true;
    }

    private AnimalicaResolvedDiet BuildProfile(string profileKey, IEnumerable<string> traits)
    {
        string[] activeTraits = traits
            .Where(trait => !string.IsNullOrWhiteSpace(trait))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string[] affinityTraits = activeTraits
            .SelectMany(AnimalicaTraitRestrictionsFoodBridge.ExpandCombinedFoodTrait)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var affinities = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (string trait in affinityTraits)
        {
            if (!traitAffinities.TryGetValue(trait, out Dictionary<string, decimal>? granted))
            {
                continue;
            }

            foreach ((string foodTag, decimal multiplier) in granted)
            {
                if (!affinities.TryGetValue(foodTag, out decimal current) || multiplier > current)
                {
                    affinities[foodTag] = multiplier;
                }
            }
        }

        string[] selectedPrimaries = activeTraits.Where(primaryTraits.Contains).ToArray();
        bool isAnimalicaProfile = affinityTraits.Any(trait =>
            primaryTraits.Contains(trait)
            || traitAffinities.ContainsKey(trait)
            || trait.StartsWith("animalica-stomach-", StringComparison.Ordinal)
            || trait.StartsWith("animalica-metabolism-", StringComparison.Ordinal)
            || trait.StartsWith("animalica-digestion-", StringComparison.Ordinal)
        );
        if (isAnimalicaProfile && selectedPrimaries.Length != 1 && warnedProfileKeys.Add(profileKey))
        {
            api?.Logger.Warning(
                "[AnimalicaCore] Diet profile '{0}' has {1} primary nutrition traits ({2}); exactly one is expected.",
                profileKey,
                selectedPrimaries.Length,
                selectedPrimaries.Length == 0 ? "none" : string.Join(", ", selectedPrimaries)
            );
        }

        return new AnimalicaResolvedDiet(activeTraits, affinities);
    }

    private static object? GetMemberValue(object? value, string memberName)
    {
        if (value == null)
        {
            return null;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        Type type = value.GetType();
        return type.GetProperty(memberName, flags)?.GetValue(value)
            ?? type.GetField(memberName, flags)?.GetValue(value);
    }
}

internal sealed class AnimalicaResolvedDiet
{
    public AnimalicaResolvedDiet(string[] traits, Dictionary<string, decimal> affinities)
    {
        Traits = traits;
        Affinities = affinities;
    }

    public string[] Traits { get; }
    public Dictionary<string, decimal> Affinities { get; }
}

internal sealed class AnimalicaDietDocument
{
    [JsonProperty("affinityMultipliers")]
    public Dictionary<string, decimal> AffinityMultipliers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonProperty("primaryTraits")]
    public List<string> PrimaryTraits { get; set; } = new();

    [JsonProperty("foods")]
    public List<AnimalicaFoodDefinition> Foods { get; set; } = new();

    [JsonProperty("traitAffinities")]
    public Dictionary<string, Dictionary<string, string>> TraitAffinities { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class AnimalicaFoodDefinition
{
    [JsonProperty("code")]
    public string Code { get; set; } = string.Empty;

    [JsonProperty("baseSatiety")]
    public float BaseSatiety { get; set; }

    [JsonProperty("tag")]
    public string Tag { get; set; } = string.Empty;

    [JsonProperty("foodCategory")]
    public string FoodCategory { get; set; } = string.Empty;

    [JsonProperty("stackAttributeAtMost")]
    public AnimalicaStackAttributeLimit? StackAttributeAtMost { get; set; }

    [JsonIgnore]
    public EnumFoodCategory ParsedFoodCategory { get; private set; }

    [JsonIgnore]
    public string MatchPrefix { get; private set; } = string.Empty;

    public bool TryNormalize(ILogger logger, out bool wildcard)
    {
        Code = Code.Trim().ToLowerInvariant();
        Tag = Tag.Trim().ToLowerInvariant();
        wildcard = Code.EndsWith('*');
        if (wildcard && Code.IndexOf('*') != Code.Length - 1)
        {
            logger.Error("[AnimalicaCore] Animal food code supports only a trailing wildcard: '{0}'.", Code);
            return false;
        }

        MatchPrefix = wildcard ? Code[..^1] : Code;
        if (MatchPrefix.Length == 0 || Tag.Length == 0 || BaseSatiety <= 0f)
        {
            logger.Error("[AnimalicaCore] Invalid animal food definition '{0}'.", Code);
            return false;
        }

        if (!Enum.TryParse(FoodCategory, true, out EnumFoodCategory category)
            || category == EnumFoodCategory.NoNutrition)
        {
            logger.Error("[AnimalicaCore] Animal food '{0}' has invalid nutrition category '{1}'.", Code, FoodCategory);
            return false;
        }

        ParsedFoodCategory = category;
        return true;
    }

    public bool Matches(string fullCode)
    {
        return fullCode.StartsWith(MatchPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public bool MatchesStack(ItemStack? stack)
    {
        return StackAttributeAtMost == null
            || stack != null
            && stack.Attributes.GetDecimal(StackAttributeAtMost.Attribute, 0d) <= StackAttributeAtMost.Value;
    }
}

internal sealed class AnimalicaStackAttributeLimit
{
    [JsonProperty("attribute")]
    public string Attribute { get; set; } = string.Empty;

    [JsonProperty("value")]
    public double Value { get; set; }
}
