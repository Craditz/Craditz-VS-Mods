using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VintageStoryConfigMigration;

namespace TamablesFotsa;

public sealed class TamablesFotsaSystem : ModSystem
{
    private const string ConfigFileName = "TamablesFotsa.json";
    private const string PetAiCookieDomain = "petai";

    private static readonly HashSet<string> SupportedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "caninae",
        "felinae",
        "vombatidae",
        "viverridae",
        "manidae",
        "spheniscidae",
        "meiolaniidae",
        "pantherinae",
        "machairodontinae",
        "thylacinidae",
        "sirenia",
        "iniidae",
        "elephantidae",
        "dinornithidae",
        "casuariidae",
        "cervinae",
        "rhinocerotidae",
        "bovinae",
        "capreolinae",
        "chelonioidea"
    };

    private static readonly HashSet<string> AquaticDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "sirenia",
        "iniidae",
        "chelonioidea"
    };

    private static IReadOnlyDictionary<string, AssetLocation[]> tamedBabyCodesByFamily =
        new Dictionary<string, AssetLocation[]>(StringComparer.OrdinalIgnoreCase);
    private static IReadOnlyDictionary<string, string> breedingFamilyAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly Treat[] MeatTreats =
    {
        new("game", "bushmeat-raw", 0.10f, 2),
        new("game", "redmeat-raw", 0.20f, 3),
        new("game", "poultry-raw", 0.20f, 3),
        new("game", "fish-raw", 0.30f, 3),
        new(PetAiCookieDomain, "petcookie-meat-perfect", 0.80f, 3)
    };

    private static readonly Treat[] VeggieTreats =
    {
        new("game", "drygrass", 0.10f, 2),
        new("game", "vegetable-carrot", 0.15f, 2),
        new("game", "grain-wheat", 0.15f, 2),
        new("game", "fruit-apple", 0.15f, 2),
        new(PetAiCookieDomain, "petcookie-veggie-perfect", 0.80f, 3)
    };

    private static readonly Treat[] OmnivoreTreats =
    {
        new("game", "bushmeat-raw", 0.10f, 2),
        new("game", "redmeat-raw", 0.20f, 3),
        new("game", "poultry-raw", 0.20f, 3),
        new("game", "fish-raw", 0.30f, 3),
        new("game", "vegetable-carrot", 0.15f, 2),
        new("game", "grain-wheat", 0.15f, 2),
        new("game", "fruit-apple", 0.15f, 2),
        new(PetAiCookieDomain, "petcookie-meat-perfect", 0.80f, 3),
        new(PetAiCookieDomain, "petcookie-veggie-perfect", 0.80f, 3)
    };

    private TamablesFotsaConfig config = new();
    private static TamablesFotsaConfig activeConfig = new();
    private ICoreServerAPI? serverApi;

    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return true;
    }

    public override void Start(ICoreAPI api)
    {
        // Register the custom task before AssetsFinalize creates the runtime
        // tamed entity definitions.  This is the same ordering used by the
        // working Feral Kinship pet definitions.
        AiTaskRegistry.Register<AiTaskTamablesFotsaBasicFollow>("tamablesfotsabasicfollow");
        AiTaskRegistry.Register<AiTaskTamablesFotsaAquaticFollow>("tamablesfotsaaquaticfollow");
        AiTaskRegistry.Register<AiTaskTamablesFotsaAquaticWander>("tamablesfotsaaquaticwander");

        api.RegisterEntityBehaviorClass(
            EntityBehaviorTamablesFotsaTameGuard.BehaviorCode,
            typeof(EntityBehaviorTamablesFotsaTameGuard)
        );
        api.RegisterEntityBehaviorClass(
            EntityBehaviorTamablesFotsaCommandBridge.BehaviorCode,
            typeof(EntityBehaviorTamablesFotsaCommandBridge)
        );
        api.RegisterEntityBehaviorClass(
            EntityBehaviorTamablesFotsaMultiply.BehaviorCode,
            typeof(EntityBehaviorTamablesFotsaMultiply)
        );
        api.RegisterEntityBehaviorClass(
            EntityBehaviorTamablesFotsaTameStageGuard.BehaviorCode,
            typeof(EntityBehaviorTamablesFotsaTameStageGuard)
        );
        api.RegisterEntityBehaviorClass(
            EntityBehaviorTamablesFotsaTameable.BehaviorCode,
            typeof(EntityBehaviorTamablesFotsaTameable)
        );
        api.RegisterEntityBehaviorClass(
            EntityBehaviorTamablesFotsaOwnerlessPurge.BehaviorCode,
            typeof(EntityBehaviorTamablesFotsaOwnerlessPurge)
        );
        api.RegisterEntityBehaviorClass(
            EntityBehaviorTamablesFotsaSourceName.BehaviorCode,
            typeof(EntityBehaviorTamablesFotsaSourceName)
        );

        AiTaskRegistry.Register<AiTaskTamablesFotsaBasicPetSeek>("tamablesfotsabasicpetseek");
        AiTaskRegistry.Register<AiTaskTamablesFotsaBasicPetMelee>("tamablesfotsabasicpetmelee");
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        List<EntityProperties> targets = new();
        foreach (EntityProperties properties in api.World.EntityTypes)
        {
            if (IsSupportedTarget(properties))
            {
                targets.Add(properties);
            }
        }

        Dictionary<string, string> tameCodes = new(StringComparer.OrdinalIgnoreCase);
        foreach (EntityProperties properties in targets)
        {
            tameCodes[SourceKey(properties)] = BuildTamedCode(properties);
        }

        int patched = 0;
        int registered = 0;
        int resolvable = 0;
        int completeServerConfigs = 0;
        int waterSafeAquaticConfigs = 0;
        foreach (EntityProperties properties in targets)
        {
            string sourceKey = SourceKey(properties);
            if (!tameCodes.TryGetValue(sourceKey, out string? tameCode))
            {
                continue;
            }

            PatchEntity(properties, tameCode);

            EntityProperties tamed = properties.Clone();
            // RegisterEntityClass uses the properties' Code when the runtime
            // looks up the destination named by PetAI's tameEntityCode. Keep
            // the cloned variant's identity separate from the source animal;
            // otherwise registration appears to succeed but PetAI cannot
            // resolve the tamed destination when taming completes.
            tamed.Code = new AssetLocation(tameCode);
            SetTamedCreatureHandbookGroups(tamed, properties);
            // The handbook's plain "Obtained by killing" list reads Drops
            // directly and does not honor handbook.groupcode. Tamed variants
            // use the same drops as their source, so hide that duplicate only
            // from the client-side handbook data; the server keeps real drops.
            if (api.Side == EnumAppSide.Client)
            {
                tamed.Drops = null;
            }
            // EntityProperties.Clone() deliberately retains the source
            // SpawnConditions reference. A dedicated tamed destination must
            // never participate in natural or runtime creature spawning;
            // only PetAI conversion, growth, and tamed reproduction may
            // create it.
            if (tamed.Server != null)
            {
                tamed.Server.SpawnConditions = null;
            }
            ConfigureTamedEntity(tamed, properties, tameCodes);
            api.RegisterEntityClass(tamed.Class, tamed);
            EntityProperties? registeredType = api.World.GetEntityType(new AssetLocation(tameCode));
            if (registeredType != null)
            {
                resolvable++;
                if (FindTameableBehavior(registeredType.Server?.BehaviorsAsJsonObj) != null
                    && FindBehavior(registeredType.Server?.BehaviorsAsJsonObj, "receivecommand") != null
                    && FindBehavior(registeredType.Server?.BehaviorsAsJsonObj, "taskai") != null)
                {
                    completeServerConfigs++;
                }
                if (IsAquatic(properties) && HasWaterSafeAquaticTaskSet(registeredType))
                {
                    waterSafeAquaticConfigs++;
                }
            }
            patched++;
            registered++;
        }

        breedingFamilyAliases = BuildBreedingFamilyAliases(targets, tameCodes);
        int breedingFamilies = BuildTamedOffspringCatalog(api, targets, tameCodes);
        List<string> unresolvedTamedGrowthCodes = targets
            .Where(IsBaby)
            .Where(properties =>
            {
                string? adultTameCode = FindAdultTameCode(properties, tameCodes);
                return string.IsNullOrWhiteSpace(adultTameCode)
                    || api.World.GetEntityType(new AssetLocation(adultTameCode)) == null;
            })
            .Select(SourceKey)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        int tamedBabyTypes = targets.Count(IsBaby);
        int resolvedTamedGrowthDestinations = tamedBabyTypes - unresolvedTamedGrowthCodes.Count;
        if (unresolvedTamedGrowthCodes.Count != 0)
        {
            api.Logger.Error(
                "[tamablesfotsa] SAFETY CHECK FAILED: only {0}/{1} tamed baby types have registered tamed growth destinations. Missing: {2}",
                resolvedTamedGrowthDestinations,
                tamedBabyTypes,
                string.Join(", ", unresolvedTamedGrowthCodes.Take(20))
            );
        }

        List<string> missingTamedFoodSeekingCodes = targets
            .Where(properties => HasTask(properties, "seekfoodandeat"))
            .Where(properties =>
            {
                if (!tameCodes.TryGetValue(SourceKey(properties), out string? tameCode))
                {
                    return true;
                }

                EntityProperties? tamed = api.World.GetEntityType(new AssetLocation(tameCode));
                return tamed == null || !HasTask(tamed, "seekfoodandeat");
            })
            .Select(SourceKey)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        int sourceFoodSeekingTypes = targets.Count(properties => HasTask(properties, "seekfoodandeat"));
        int preservedFoodSeekingTypes = sourceFoodSeekingTypes - missingTamedFoodSeekingCodes.Count;
        if (missingTamedFoodSeekingCodes.Count != 0)
        {
            api.Logger.Error(
                "[tamablesfotsa] SAFETY CHECK FAILED: only {0}/{1} source food-seeking task sets were preserved on tamed variants. Missing: {2}",
                preservedFoodSeekingTypes,
                sourceFoodSeekingTypes,
                string.Join(", ", missingTamedFoodSeekingCodes.Take(20))
            );
        }

        int spawnableTamedVariants = api.World.EntityTypes.Count(properties =>
            properties.Code.Domain.Equals("tamablesfotsa", StringComparison.OrdinalIgnoreCase)
            && properties.Server?.SpawnConditions != null
        );

        if (spawnableTamedVariants != 0)
        {
            api.Logger.Error(
                "[tamablesfotsa] SAFETY CHECK FAILED: {0} Tamables:FOTSA entity variants still have spawn conditions.",
                spawnableTamedVariants
            );
        }

        api.Logger.Notification(
            "[tamablesfotsa] Added PetAI definitions to {0} FotSA baby/adult types and registered {1} tamed variants ({2} resolvable, {3} complete server configs, {4} breeding families, {5} water-safe aquatic configs, {6} naturally spawnable tamed variants).",
            patched,
            registered,
            resolvable,
            completeServerConfigs,
            breedingFamilies,
            waterSafeAquaticConfigs,
            spawnableTamedVariants
        );
        api.Logger.Notification(
            "[tamablesfotsa] Tamed growth destinations: {0}/{1} baby types resolved.",
            resolvedTamedGrowthDestinations,
            tamedBabyTypes
        );
        api.Logger.Notification(
            "[tamablesfotsa] Tamed food-seeking task sets: {0}/{1} source types preserved.",
            preservedFoodSeekingTypes,
            sourceFoodSeekingTypes
        );
    }

    private static bool HasTask(EntityProperties properties, string taskCode)
    {
        JsonObject? taskAi = FindBehavior(properties.Server?.BehaviorsAsJsonObj, "taskai");
        if (taskAi?.Token is not JObject taskObject
            || taskObject["aitasks"] is not JArray tasks)
        {
            return false;
        }

        return tasks.Any(task => string.Equals(
            task["code"]?.Value<string>(),
            taskCode,
            StringComparison.OrdinalIgnoreCase
        ));
    }

    private static bool HasWaterSafeAquaticTaskSet(EntityProperties properties)
    {
        JsonObject? taskAi = FindBehavior(properties.Server?.BehaviorsAsJsonObj, "taskai");
        if (taskAi?.Token is not JObject taskObject
            || !string.Equals(
                taskObject["aiCreatureType"]?.Value<string>(),
                "SeaCreature",
                StringComparison.OrdinalIgnoreCase)
            || taskObject["aitasks"] is not JArray tasks)
        {
            return false;
        }

        bool hasWaterRecovery = false;
        bool hasAquaticFollow = false;
        bool hasAquaticWander = false;
        foreach (JToken task in tasks)
        {
            if (task["enabled"]?.Value<bool>() == false) continue;

            string code = task["code"]?.Value<string>() ?? string.Empty;
            if (code.Equals("fishoutofwater", StringComparison.OrdinalIgnoreCase)) hasWaterRecovery = true;
            if (code.Equals("tamablesfotsaaquaticfollow", StringComparison.OrdinalIgnoreCase)) hasAquaticFollow = true;
            if (code.Equals("tamablesfotsaaquaticwander", StringComparison.OrdinalIgnoreCase)) hasAquaticWander = true;
            if (code.Equals("tamablesfotsabasicfollow", StringComparison.OrdinalIgnoreCase)
                || code.Equals("getoutofwater", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return hasWaterRecovery && hasAquaticFollow && hasAquaticWander;
    }

    private static int BuildTamedOffspringCatalog(
        ICoreAPI api,
        IEnumerable<EntityProperties> targets,
        IReadOnlyDictionary<string, string> tameCodes)
    {
        Dictionary<string, List<AssetLocation>> catalog = new(StringComparer.OrdinalIgnoreCase);
        foreach (EntityProperties baby in targets.Where(IsBaby))
        {
            if (!tameCodes.TryGetValue(SourceKey(baby), out string? tameCode))
            {
                continue;
            }

            AssetLocation code = new(tameCode);
            if (api.World.GetEntityType(code) == null)
            {
                continue;
            }

            string family = BreedingFamilyKey(code);
            if (!catalog.TryGetValue(family, out List<AssetLocation>? familyCodes))
            {
                catalog[family] = familyCodes = new List<AssetLocation>();
            }

            familyCodes.Add(code);
        }

        tamedBabyCodesByFamily = catalog.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase
        );
        return tamedBabyCodesByFamily.Count;
    }

    private static IReadOnlyDictionary<string, string> BuildBreedingFamilyAliases(
        IEnumerable<EntityProperties> targets,
        IReadOnlyDictionary<string, string> tameCodes)
    {
        Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase);
        foreach (EntityProperties baby in targets.Where(IsBaby))
        {
            if (!tameCodes.TryGetValue(SourceKey(baby), out string? babyTameCode))
            {
                continue;
            }

            string? adultTameCode = FindAdultTameCode(baby, tameCodes);
            if (string.IsNullOrWhiteSpace(adultTameCode))
            {
                continue;
            }

            string babyFamily = RawBreedingFamilyKey(new AssetLocation(babyTameCode));
            string adultFamily = RawBreedingFamilyKey(new AssetLocation(adultTameCode));
            if (!adultFamily.Equals(babyFamily, StringComparison.OrdinalIgnoreCase))
            {
                // FotSA occasionally routes an unsuffixed baby into a known
                // ownership form such as -tamed or -semitamed. Derive only
                // that exact adult-to-baby alias; do not fuzzy-match families.
                aliases[adultFamily] = babyFamily;
            }
        }

        foreach (EntityProperties adult in targets.Where(IsAdult))
        {
            string unsuffixedAdultPath = RemoveOwnershipFormSuffix(adult.Code.Path);
            if (unsuffixedAdultPath.Equals(adult.Code.Path, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string babyPath = ReplaceAdultAgeToken(unsuffixedAdultPath);
            string babyKey = adult.Code.Domain + ":" + babyPath;
            if (!tameCodes.TryGetValue(babyKey, out string? babyTameCode)
                || !tameCodes.TryGetValue(SourceKey(adult), out string? adultTameCode))
            {
                continue;
            }

            aliases[RawBreedingFamilyKey(new AssetLocation(adultTameCode))] =
                RawBreedingFamilyKey(new AssetLocation(babyTameCode));
        }

        return aliases;
    }

    private static string RemoveOwnershipFormSuffix(string path)
    {
        foreach (string suffix in new[] { "-tamed", "-semitamed" })
        {
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return path[..^suffix.Length];
            }
        }

        return path;
    }

    private static string ReplaceAdultAgeToken(string path)
    {
        if (path.Contains("-adult-", StringComparison.OrdinalIgnoreCase))
        {
            return path.Replace("-adult-", "-baby-", StringComparison.OrdinalIgnoreCase);
        }

        if (path.StartsWith("adult-", StringComparison.OrdinalIgnoreCase))
        {
            return "baby-" + path[6..];
        }

        if (path.EndsWith("-adult", StringComparison.OrdinalIgnoreCase))
        {
            return path[..^6] + "-baby";
        }

        return path;
    }

    internal static AssetLocation[] GetRegisteredTamedOffspringCodes(Entity parent)
    {
        return tamedBabyCodesByFamily.TryGetValue(BreedingFamilyKey(parent.Code), out AssetLocation[]? codes)
            ? codes
            : Array.Empty<AssetLocation>();
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        serverApi = api;
        config = LoadConfig(api);
        activeConfig = config;
        api.Event.OnPlayerInteractEntity += OnPlayerInteractEntity;
        api.Event.OnEntitySpawn += OnEntitySpawn;

        api.Logger.Notification(
            "[tamablesfotsa] Taming stage: {0}.",
            config.TamingStage
        );
    }

    public override void Dispose()
    {
        if (serverApi != null)
        {
            serverApi.Event.OnPlayerInteractEntity -= OnPlayerInteractEntity;
            serverApi.Event.OnEntitySpawn -= OnEntitySpawn;
            serverApi = null;
            activeConfig = new TamablesFotsaConfig();
        }
    }

    private void OnEntitySpawn(Entity entity)
    {
        if (!entity.Code.Domain.Equals("tamablesfotsa", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        InitializeBredOffspring(entity);
    }

    private void InitializeBredOffspring(Entity entity)
    {
        if (!IsTamedVariant(entity)
            || !IsBabyPath(entity.Code.Path)
            || !string.Equals(entity.Attributes.GetString("origin"), "reproduction", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        PetAI.EntityBehaviorTameable? offspringTameable = entity.GetBehavior<PetAI.EntityBehaviorTameable>();
        if (offspringTameable == null)
        {
            return;
        }

        Entity? parent = serverApi?.World.GetNearestEntity(entity.Pos.XYZ, 6, 6, candidate =>
            candidate != entity
            && candidate.Alive
            && IsTamedVariant(candidate)
            && IsAdultPath(candidate.Code.Path)
            && string.Equals(BreedingFamilyKey(candidate.Code), BreedingFamilyKey(entity.Code), StringComparison.OrdinalIgnoreCase)
            && candidate.GetBehavior<PetAI.EntityBehaviorTameable>()?.DomesticationLevel == PetAI.DomesticationLevel.DOMESTICATED
        );
        PetAI.EntityBehaviorTameable? parentTameable = parent?.GetBehavior<PetAI.EntityBehaviorTameable>();
        if (parentTameable == null || string.IsNullOrWhiteSpace(parentTameable.OwnerId))
        {
            serverApi?.Logger.Warning(
                "[tamablesfotsa] Bred tamed offspring {0} spawned without a nearby owned tamed parent; leaving ownership unchanged.",
                entity.Code
            );
            return;
        }

        offspringTameable.DomesticationLevel = PetAI.DomesticationLevel.DOMESTICATED;
        offspringTameable.OwnerId = parentTameable.OwnerId;
        offspringTameable.Obedience = 1f;
        offspringTameable.MultiplyAllowed = true;
        entity.WatchedAttributes.SetInt(
            "generation",
            Math.Max(entity.WatchedAttributes.GetInt("generation", 0), parent!.WatchedAttributes.GetInt("generation", 0) + 1)
        );
        entity.WatchedAttributes.MarkPathDirty("domesticationstatus");
    }

    private static bool IsTamedVariant(Entity entity)
    {
        return entity.Code.Domain.Equals("tamablesfotsa", StringComparison.OrdinalIgnoreCase)
            && entity.Code.Path.StartsWith("tame-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAdultPath(string path)
    {
        return path.Contains("-adult", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("adult-", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("adult", StringComparison.OrdinalIgnoreCase);
    }

    private static string BreedingFamilyKey(AssetLocation code)
    {
        string family = RawBreedingFamilyKey(code);
        return breedingFamilyAliases.TryGetValue(family, out string? canonicalFamily)
            ? canonicalFamily
            : family;
    }

    private static string RawBreedingFamilyKey(AssetLocation code)
    {
        string path = code.Path.ToLowerInvariant();
        return path
            .Replace("-baby-", "-age-")
            .Replace("-adult-", "-age-")
            .Replace("-female-", "-gender-")
            .Replace("-male-", "-gender-")
            .Replace("-baby", "-age")
            .Replace("-adult", "-age")
            .Replace("-female", "-gender")
            .Replace("-male", "-gender");
    }

    private static TamablesFotsaConfig LoadConfig(ICoreAPI api)
    {
        try
        {
            TamablesFotsaConfig loaded = ConfigDefaults.LoadAndUpdate(
                api,
                ConfigFileName,
                () => new TamablesFotsaConfig());
            if (IsValidTamingStage(loaded.TamingStage))
            {
                return loaded;
            }

            TamablesFotsaConfig created = new();
            ConfigDefaults.StorePreservingUnknown(api, ConfigFileName, created);
            return created;
        }
        catch (Exception exception)
        {
            api.Logger.Warning(
                "[tamablesfotsa] Could not read {0}; using Baby mode. {1}",
                ConfigFileName,
                exception.Message
            );
            return new TamablesFotsaConfig();
        }
    }

    private void OnPlayerInteractEntity(
        Entity entity,
        IPlayer byPlayer,
        ItemSlot slot,
        Vec3d hitPosition,
        int mode,
        ref EnumHandling handling)
    {
        if (ShouldBlockTamingInteraction(entity, slot, (EnumInteractMode)mode))
        {
            handling = EnumHandling.PreventSubsequent;
        }
    }

    internal static bool ShouldBlockTamingInteraction(
        Entity entity,
        ItemSlot slot,
        EnumInteractMode mode)
    {
        if (mode != EnumInteractMode.Interact
            || slot?.Itemstack?.Collectible == null
            || !IsSupportedTarget(entity.Properties)
            || !IsTamingTreat(slot))
        {
            return false;
        }

        return IsWrongTamingStageForWildEntity(entity);
    }

    internal static bool IsWrongTamingStageForWildEntity(Entity entity)
    {
        if (!IsSupportedTarget(entity.Properties))
        {
            return false;
        }

        // PetAI uses this tree for both taming progress and ownership. Once a
        // pet is already being tamed or is domesticated, let it continue to
        // receive food even if it has grown past the configured starting age.
        string? domesticationLevel = entity.WatchedAttributes
            .GetTreeAttribute("domesticationstatus")?
            .GetString("domesticationLevel");
        if (!string.IsNullOrWhiteSpace(domesticationLevel)
            && !string.Equals(domesticationLevel, "WILD", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bool isBaby = IsBaby(entity.Properties);
        return IsAdultTamingStage(activeConfig.TamingStage) ? isBaby : !isBaby;
    }

    private static bool IsValidTamingStage(string? tamingStage)
    {
        return IsAdultTamingStage(tamingStage)
            || string.Equals(tamingStage, "Baby", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAdultTamingStage(string? tamingStage)
    {
        return string.Equals(tamingStage, "Adult", StringComparison.OrdinalIgnoreCase);
    }

    private static bool PatchEntity(EntityProperties properties, string tameEntityCode)
    {
        bool isBaby = IsBaby(properties);
        bool isAdult = IsAdult(properties);
        if (!isBaby && !isAdult)
        {
            return false;
        }

        string size = GetPetSize(properties.Code.Domain, properties.Code.Path);
        Treat[] treats = GetTreats(properties.Code.Domain, properties.Code.Path);

        if (properties.Client != null)
        {
            properties.Client.BehaviorsAsJsonObj = ReplaceBehavior(
                properties.Client.BehaviorsAsJsonObj,
                CreateTameableBehavior(treats, size, includeServerSettings: false, tameEntityCode: null)
            );

            if (isAdult)
            {
                properties.Client.BehaviorsAsJsonObj = ReplaceBehavior(
                    properties.Client.BehaviorsAsJsonObj,
                    CreateReceiveCommandBehavior()
                );
            }
        }

        if (properties.Server != null)
        {
            properties.Server.BehaviorsAsJsonObj = ReplaceBehavior(
                properties.Server.BehaviorsAsJsonObj,
                CreateTameableBehavior(treats, size, includeServerSettings: true, tameEntityCode)
            );
            properties.Server.BehaviorsAsJsonObj = InsertBehaviorBefore(
                properties.Server.BehaviorsAsJsonObj,
                CreateTameStageGuardBehavior(),
                "tameable"
            );
        }

        return true;
    }

    private static void ConfigureTamedEntity(
        EntityProperties tamed,
        EntityProperties source,
        IReadOnlyDictionary<string, string> tameCodes)
    {
        // Vanilla rejects healing when this attribute is absent. Despite its
        // name, 1.22 checks it as an upper generation bound. Dedicated pets
        // must remain healable after breeding, including source forms with a
        // zero bound. Clone before writing so wild source rules stay intact.
        JObject attributes = tamed.Attributes?.Token is JObject existingAttributes
            ? (JObject)existingAttributes.DeepClone()
            : new JObject();
        attributes["minGenerationToAllowHealing"] = int.MaxValue;
        tamed.Attributes = new JsonObject(attributes);

        bool isBaby = IsBaby(source);
        string size = GetPetSize(source.Code.Domain, source.Code.Path);
        Treat[] treats = GetTreats(source.Code.Domain, source.Code.Path);
        string? adultTameCode = isBaby ? FindAdultTameCode(source, tameCodes) : null;

        if (tamed.Client != null)
        {
            tamed.Client.BehaviorsAsJsonObj = BuildTamedClientBehaviors(
                source.Client?.BehaviorsAsJsonObj,
                treats,
                size,
                IsAdult(source) && FindBehavior(source.Server?.BehaviorsAsJsonObj, "multiply") != null,
                source.Code
            );
        }

        if (tamed.Server != null)
        {
            tamed.Server.BehaviorsAsJsonObj = BuildTamedServerBehaviors(
                source.Server?.BehaviorsAsJsonObj,
                source,
                treats,
                size,
                adultTameCode
            );
        }
    }

    private static JsonObject[] BuildTamedClientBehaviors(
        JsonObject[]? existing,
        Treat[] treats,
        string size,
        bool includeMultiply,
        AssetLocation sourceEntityCode)
    {
        List<JsonObject> result = FilterTamedBehaviors(existing, includeTaskAi: false);
        result.Add(CreateTameGuardBehavior());
        result.Add(CreateTameableBehavior(treats, size, includeServerSettings: false, tameEntityCode: null));
        result.Add(CreateReceiveCommandBehavior());
        result.Add(CreateNametagBehavior());
        result.Add(CreateSourceNameBehavior(sourceEntityCode));
        if (includeMultiply)
        {
            result.Add(CreateTamedMultiplyClientBehavior());
        }

        return result.ToArray();
    }

    private static JsonObject[] BuildTamedServerBehaviors(
        JsonObject[]? existing,
        EntityProperties source,
        Treat[] treats,
        string size,
        string? adultTameCode)
    {
        List<JsonObject> result = FilterTamedBehaviors(existing, includeTaskAi: true);
        for (int i = 0; i < result.Count; i++)
        {
            if (!string.Equals(result[i]["code"].AsString(), "grow", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(adultTameCode)
                || result[i].Token is not JObject growObject)
            {
                continue;
            }

            JObject clone = (JObject)growObject.DeepClone();
            clone["adultEntityCodes"] = new JArray(adultTameCode);
            clone["fedAdultEntityCodes"] = new JArray(adultTameCode);
            result[i] = new JsonObject(clone);
        }

        InsertTameGuardBeforeHealth(result);
        result.Add(CreateTameStageGuardBehavior());
        result.Add(CreateTameableBehavior(treats, size, includeServerSettings: true, tameEntityCode: null));
        result.Add(CreateOwnerlessPurgeBehavior());
        result.Add(CreateReceiveCommandBehavior());
        result.Add(CreateNametagBehavior());
        result.Add(CreateSourceNameBehavior(source.Code));
        result.Add(CreateMortallyWoundableBehavior());
        InsertTaskAiBeforePettable(result, CreateTamedTaskAi(source));
        result.Add(CreateCommandBridgeBehavior());

        JsonObject? sourceMultiply = FindBehavior(source.Server?.BehaviorsAsJsonObj, "multiply");
        if (sourceMultiply != null && IsAdult(source))
        {
            result.Add(CreateTamedMultiplyBehavior(sourceMultiply));
        }

        return result.ToArray();
    }

    private static void InsertTaskAiBeforePettable(List<JsonObject> behaviors, JsonObject taskAi)
    {
        // Vanilla pettable subscribes to the task manager during Initialize.
        // FotSA puts taskai before pettable for that reason, so preserve that
        // dependency when replacing the wild task list with the tamed one.
        int pettableIndex = behaviors.FindIndex(behavior =>
        {
            string code = behavior["code"].AsString() ?? string.Empty;
            return code.Equals("pettable", StringComparison.OrdinalIgnoreCase)
                || code.Equals("pettableextended", StringComparison.OrdinalIgnoreCase);
        });

        behaviors.Insert(pettableIndex >= 0 ? pettableIndex : behaviors.Count, taskAi);
    }

    private static List<JsonObject> FilterTamedBehaviors(JsonObject[]? existing, bool includeTaskAi)
    {
        List<JsonObject> result = new();
        if (existing == null) return result;

        foreach (JsonObject behavior in existing)
        {
            string code = behavior["code"].AsString() ?? string.Empty;
            if (code.Equals("tameable", StringComparison.OrdinalIgnoreCase)
                || code.Equals(EntityBehaviorTamablesFotsaTameable.BehaviorCode, StringComparison.OrdinalIgnoreCase)
                || code.Equals("receivecommand", StringComparison.OrdinalIgnoreCase)
                || code.Equals("nametag", StringComparison.OrdinalIgnoreCase)
                || code.Equals(EntityBehaviorTamablesFotsaTameGuard.BehaviorCode, StringComparison.OrdinalIgnoreCase)
                || code.Equals(EntityBehaviorTamablesFotsaMultiply.BehaviorCode, StringComparison.OrdinalIgnoreCase)
                || code.Equals(EntityBehaviorTamablesFotsaOwnerlessPurge.BehaviorCode, StringComparison.OrdinalIgnoreCase)
                || code.Equals(EntityBehaviorTamablesFotsaSourceName.BehaviorCode, StringComparison.OrdinalIgnoreCase)
                || code.Equals("emotionstates", StringComparison.OrdinalIgnoreCase)
                || code.Equals("despawn", StringComparison.OrdinalIgnoreCase)
                || code.Equals("multiply", StringComparison.OrdinalIgnoreCase)
                || (!includeTaskAi && code.Equals("taskai", StringComparison.OrdinalIgnoreCase))
                || (includeTaskAi && code.Equals("taskai", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result.Add(behavior);
        }

        return result;
    }

    private static JsonObject CreateTamedMultiplyBehavior(JsonObject sourceMultiply)
    {
        if (sourceMultiply.Token is not JObject sourceObject)
        {
            return sourceMultiply;
        }

        JObject multiply = (JObject)sourceObject.DeepClone();
        multiply["code"] = EntityBehaviorTamablesFotsaMultiply.BehaviorCode;

        RewriteTamedEntityCodeMap(multiply, "spawnEntityCodesByType");
        RewriteTamedEntityCodeMap(multiply, "requiresNearbyEntityCodeByType");
        RewriteTamedEntityCodeArray(multiply, "spawnEntityCodes");
        RewriteTamedEntityCodeArray(multiply, "requiresNearbyEntityCodes");
        RewriteTamedEntityCodeProperty(multiply, "spawnEntityCode");
        RewriteTamedEntityCodeProperty(multiply, "requiresNearbyEntityCode");

        return new JsonObject(multiply);
    }

    private static JsonObject CreateTamedMultiplyClientBehavior()
    {
        return new JsonObject(new JObject
        {
            ["code"] = EntityBehaviorTamablesFotsaMultiply.BehaviorCode,
            ["enabledByType"] = new JObject
            {
                ["*-adult-female-*"] = true,
                ["*"] = false
            }
        });
    }

    private static void RewriteTamedEntityCodeMap(JObject behavior, string propertyName)
    {
        if (behavior[propertyName] is not JObject map)
        {
            return;
        }

        foreach (JProperty entry in map.Properties())
        {
            RewriteTamedEntityCodeToken(entry.Value);
        }
    }

    private static void RewriteTamedEntityCodeArray(JObject behavior, string propertyName)
    {
        if (behavior[propertyName] is JToken token)
        {
            RewriteTamedEntityCodeToken(token);
        }
    }

    private static void RewriteTamedEntityCodeProperty(JObject behavior, string propertyName)
    {
        if (behavior[propertyName] is JValue value && value.Type == JTokenType.String)
        {
            value.Value = ToTamedEntityCode(value.Value<string>() ?? string.Empty);
        }
    }

    private static void RewriteTamedEntityCodeToken(JToken token)
    {
        if (token is JValue value && value.Type == JTokenType.String)
        {
            value.Value = ToTamedEntityCode(value.Value<string>() ?? string.Empty);
            return;
        }

        if (token is JObject obj)
        {
            if (obj["code"] is JValue code && code.Type == JTokenType.String)
            {
                code.Value = ToTamedEntityCode(code.Value<string>() ?? string.Empty);
            }

            return;
        }

        if (token is JArray array)
        {
            foreach (JToken child in array)
            {
                RewriteTamedEntityCodeToken(child);
            }
        }
    }

    private static string ToTamedEntityCode(string sourceCode)
    {
        int separator = sourceCode.IndexOf(':');
        if (separator <= 0 || separator >= sourceCode.Length - 1)
        {
            return sourceCode;
        }

        string domain = sourceCode[..separator];
        string path = sourceCode[(separator + 1)..];
        return "tamablesfotsa:tame-" + domain + "-" + path;
    }

    private static void InsertTameGuardBeforeHealth(List<JsonObject> behaviors)
    {
        int healthIndex = behaviors.FindIndex(behavior =>
        {
            string code = behavior["code"].AsString() ?? string.Empty;
            return code.Equals("health", StringComparison.OrdinalIgnoreCase)
                || code.Equals("mortallywoundable", StringComparison.OrdinalIgnoreCase);
        });

        behaviors.Insert(healthIndex >= 0 ? healthIndex : 0, CreateTameGuardBehavior());
    }

    private static JsonObject[] ReplaceBehavior(JsonObject[]? existing, JsonObject replacement)
    {
        List<JsonObject> result = new();
        string replacementCode = replacement["code"].AsString() ?? string.Empty;
        bool replaced = false;

        if (existing != null)
        {
            foreach (JsonObject current in existing)
            {
                string currentCode = current["code"].AsString() ?? string.Empty;
                bool sameCode = string.Equals(currentCode, replacementCode, StringComparison.OrdinalIgnoreCase);
                bool tameableAlias = replacementCode.Equals(
                    EntityBehaviorTamablesFotsaTameable.BehaviorCode,
                    StringComparison.OrdinalIgnoreCase
                ) && currentCode.Equals("tameable", StringComparison.OrdinalIgnoreCase);
                if (sameCode || tameableAlias)
                {
                    if (!replaced)
                    {
                        result.Add(replacement);
                        replaced = true;
                    }
                    continue;
                }

                result.Add(current);
            }
        }

        if (!replaced) result.Add(replacement);
        return result.ToArray();
    }

    private static JsonObject[] InsertBehaviorBefore(
        JsonObject[]? existing,
        JsonObject behavior,
        string beforeCode)
    {
        List<JsonObject> result = existing == null
            ? new List<JsonObject>()
            : new List<JsonObject>(existing);
        string behaviorCode = behavior["code"].AsString() ?? string.Empty;

        if (result.Any(current => string.Equals(
                current["code"].AsString(),
                behaviorCode,
                StringComparison.OrdinalIgnoreCase)))
        {
            return result.ToArray();
        }

        int index = result.FindIndex(current => string.Equals(
            current["code"].AsString(),
            beforeCode,
            StringComparison.OrdinalIgnoreCase));
        result.Insert(index >= 0 ? index : result.Count, behavior);
        return result.ToArray();
    }

    private static JsonObject? FindBehavior(JsonObject[]? behaviors, string code)
    {
        if (behaviors == null) return null;
        foreach (JsonObject behavior in behaviors)
        {
            if (string.Equals(behavior["code"].AsString(), code, StringComparison.OrdinalIgnoreCase))
            {
                return behavior;
            }
        }

        return null;
    }

    private static JsonObject? FindTameableBehavior(JsonObject[]? behaviors)
    {
        return FindBehavior(behaviors, "tameable")
            ?? FindBehavior(behaviors, EntityBehaviorTamablesFotsaTameable.BehaviorCode);
    }

    private static string FindTaskAnimation(EntityProperties properties, string taskCode, string fallback)
    {
        JsonObject? taskAi = FindBehavior(properties.Server?.BehaviorsAsJsonObj, "taskai");
        if (taskAi?.Token is JObject taskObject && taskObject["aitasks"] is JArray tasks)
        {
            foreach (JToken task in tasks)
            {
                if (string.Equals(task["code"]?.Value<string>(), taskCode, StringComparison.OrdinalIgnoreCase))
                {
                    string? animation = task["animation"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(animation)) return animation;
                }
            }
        }

        return fallback;
    }

    private static string FindAnimation(EntityProperties properties, string code, string fallback)
    {
        if (properties.Client?.Animations != null)
        {
            foreach (AnimationMetaData animation in properties.Client.Animations)
            {
                if (string.Equals(animation.Code, code, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(animation.Animation))
                {
                    return animation.Animation;
                }
            }
        }

        return fallback;
    }

    private static string SourceKey(EntityProperties properties)
    {
        return properties.Code.Domain + ":" + properties.Code.Path;
    }

    private static string BuildTamedCode(EntityProperties properties)
    {
        return "tamablesfotsa:tame-" + properties.Code.Domain + "-" + properties.Code.Path;
    }

    private static void SetTamedCreatureHandbookGroups(EntityProperties tamed, EntityProperties source)
    {
        if (source.Attributes?.Token is not JObject sourceAttributes)
        {
            return;
        }

        JObject attributes = (JObject)sourceAttributes.DeepClone();
        string creatureGroupCode = attributes["creatureDietGroup"]?.Value<string>()
            ?? attributes["handbook"]?["groupcode"]?.Value<string>()
            ?? (source.Code.Domain + ":item-creature-" + source.Code.Path);

        // Vanilla uses creatureDietGroup for food lists and trough text. Keep
        // creatureDiet itself unchanged because feeding behavior still needs it.
        if (attributes["creatureDiet"] != null)
        {
            attributes["creatureDietGroup"] = creatureGroupCode;
        }

        // The handbook's "Obtained by killing & harvesting" list collapses
        // harvestable-drop sources by handbook.groupcode. A private tamed
        // clone has a different entity code, so give it the source animal's
        // group to avoid adding another entry for the same harvested drops.
        JObject handbook = attributes["handbook"] as JObject ?? new JObject();
        handbook["groupcode"] = handbook["groupcode"]?.Value<string>()
            ?? (source.Code.Domain + ":item-creature-" + source.Code.Path);
        attributes["handbook"] = handbook;
        tamed.Attributes = new JsonObject(attributes);
    }

    private static string? FindAdultTameCode(
        EntityProperties baby,
        IReadOnlyDictionary<string, string> tameCodes)
    {
        string adultPath = ReplaceAgeToken(baby.Code.Path);
        string adultKey = baby.Code.Domain + ":" + adultPath;
        if (tameCodes.TryGetValue(adultKey, out string? adultCode))
        {
            return adultCode;
        }

        // Some FotSA babies grow into separately registered ownership forms
        // instead of a same-path adult. Bovinae's domestic water buffalo uses
        // "-tamed" and Caninae's domestic dog uses "-semitamed". Keep these as
        // exact suffix fallbacks rather than fuzzy matching so unrelated packs
        // cannot resolve to the wrong species.
        string tamedAdultKey = adultKey + "-tamed";
        if (tameCodes.TryGetValue(tamedAdultKey, out adultCode))
        {
            return adultCode;
        }

        string semitamedAdultKey = adultKey + "-semitamed";
        return tameCodes.TryGetValue(semitamedAdultKey, out adultCode) ? adultCode : null;
    }

    private static string ReplaceAgeToken(string path)
    {
        if (path.Contains("-baby-", StringComparison.OrdinalIgnoreCase))
        {
            return path.Replace("-baby-", "-adult-", StringComparison.OrdinalIgnoreCase);
        }

        if (path.StartsWith("baby-", StringComparison.OrdinalIgnoreCase))
        {
            return "adult-" + path[5..];
        }

        if (path.EndsWith("-baby", StringComparison.OrdinalIgnoreCase))
        {
            return path[..^5] + "-adult";
        }

        if (path.EndsWith("baby", StringComparison.OrdinalIgnoreCase))
        {
            return path[..^4] + "adult";
        }

        return path;
    }

    private static JsonObject CreateNametagBehavior()
    {
        return new JsonObject(new JObject
        {
            ["code"] = "nametag",
            ["showtagonlywhentargeted"] = true
        });
    }

    private static JsonObject CreateSourceNameBehavior(AssetLocation sourceEntityCode)
    {
        return new JsonObject(new JObject
        {
            ["code"] = EntityBehaviorTamablesFotsaSourceName.BehaviorCode,
            ["sourceEntityCode"] = sourceEntityCode.ToString()
        });
    }

    private static JsonObject CreateTameGuardBehavior()
    {
        return new JsonObject(new JObject
        {
            ["code"] = EntityBehaviorTamablesFotsaTameGuard.BehaviorCode
        });
    }

    private static JsonObject CreateTameStageGuardBehavior()
    {
        return new JsonObject(new JObject
        {
            ["code"] = EntityBehaviorTamablesFotsaTameStageGuard.BehaviorCode
        });
    }

    private static JsonObject CreateCommandBridgeBehavior()
    {
        return new JsonObject(new JObject
        {
            ["code"] = EntityBehaviorTamablesFotsaCommandBridge.BehaviorCode
        });
    }

    private static JsonObject CreateMortallyWoundableBehavior()
    {
        return new JsonObject(new JObject
        {
            ["code"] = "mortallywoundable",
            ["remainAliveHours"] = 24,
            ["healingRequiredForRescue"] = 0,
            ["whenBelowHealth"] = 1
        });
    }

    private static JsonObject CreateTamedTaskAi(EntityProperties source)
    {
        bool aquatic = IsAquatic(source);
        JObject taskAi = new()
        {
            ["code"] = "taskai",
            ["aiCreatureType"] = aquatic ? "SeaCreature" : "LandCreature",
            ["aitasks"] = new JArray()
        };
        JArray tasks = (JArray)taskAi["aitasks"]!;

        JsonObject? sourceTaskAi = FindBehavior(source.Server?.BehaviorsAsJsonObj, "taskai");
        if (sourceTaskAi?.Token is JObject sourceTaskObject
            && sourceTaskObject["aitasks"] is JArray sourceTasks)
        {
            foreach (JToken token in sourceTasks)
            {
                string code = token["code"]?.Value<string>() ?? string.Empty;
                if ((!aquatic && code.Equals("getoutofwater", StringComparison.OrdinalIgnoreCase))
                    || (aquatic && code.Equals("fishoutofwater", StringComparison.OrdinalIgnoreCase))
                    || code.Equals("idle", StringComparison.OrdinalIgnoreCase)
                    || code.Equals("wander", StringComparison.OrdinalIgnoreCase)
                    || code.Equals("lookaround", StringComparison.OrdinalIgnoreCase)
                    || code.Equals("seekfoodandeat", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsBurrowingTask(token))
                    {
                        continue;
                    }

                    JObject tamedTask = (JObject)token.DeepClone();
                    NormalizeTamedAmbientTask(tamedTask, code);
                    NormalizeKnownSourceMovementErrors(source, tamedTask, code);
                    if (aquatic && code.Equals("wander", StringComparison.OrdinalIgnoreCase))
                    {
                        tamedTask["code"] = "tamablesfotsaaquaticwander";
                    }
                    tasks.Add(tamedTask);
                }
            }
        }

        string attackAnimation = FindTaskAnimation(source, "meleeattack", "Attack");
        string moveAnimation = FindTaskAnimation(source, "seekentity", FindTaskAnimation(source, "wander", "Walk"));
        string followAnimation = aquatic
            ? FindAnimation(source, "swimfast", FindAnimation(source, "swim", "Swim"))
            : FindAnimation(source, "run", FindAnimation(source, "gallop", "Walk"));
        string sitAnimation = aquatic
            ? FindAnimation(source, "idle", "Idle")
            : FindAnimation(source, "sit", "Sit");
        string layAnimation = aquatic
            ? FindAnimation(source, "sleep", FindAnimation(source, "idle", "Idle"))
            : FindAnimation(source, "lie", FindAnimation(source, "sleep", "Lie"));

        JObject meleeTask = new()
        {
            ["code"] = "tamablesfotsabasicpetmelee",
            ["priority"] = 3.2,
            ["priorityForCancel"] = 3.3,
            ["slot"] = 1,
            ["isCommandable"] = true,
            ["tamingGenerations"] = 1000,
            ["entityCodes"] = new JArray
            {
                "trainingdummy", "drifter-*", "shiver-*", "bowtorn-*", "locust-*",
                "wolf-*", "hyena-*", "bear-*", "cockatrice-*", "direwolf-*",
                "hellboar-*", "scorpion-*", "spider-*", "golem-*", "geodecrab-*",
                "shark", "bigshark"
            },
            ["skipEntityCodes"] = new JArray("tamablesfotsa:tame-*"),
            ["minDist"] = 1.0,
            ["minVerDist"] = 0.9,
            ["attackAngleRangeDeg"] = 35,
            ["retaliateAttacks"] = true,
            ["attackDurationMs"] = 1000,
            ["animation"] = attackAnimation,
            ["animationSpeed"] = 2.5,
            ["mincooldown"] = 900,
            ["maxcooldown"] = 1200,
            ["damage"] = 4.0,
            ["damageType"] = "SlashingAttack",
            ["damageTier"] = 1
        };
        if (aquatic)
        {
            meleeTask["aquaticOnly"] = true;
            meleeTask["whenSwimming"] = true;
        }
        tasks.Add(meleeTask);

        if (!aquatic)
        {
            tasks.Add(new JObject
            {
                ["code"] = "tamablesfotsabasicpetseek",
                ["priority"] = 2.9,
                ["priorityForCancel"] = 3.3,
                ["slot"] = 1,
                ["isCommandable"] = true,
                ["tamingGenerations"] = 1000,
                ["entityCodes"] = new JArray
                {
                    "trainingdummy", "drifter-*", "shiver-*", "bowtorn-*", "locust-*",
                    "wolf-*", "hyena-*", "bear-*", "cockatrice-*", "direwolf-*",
                    "hellboar-*", "scorpion-*", "spider-*", "golem-*", "geodecrab-*",
                    "shark", "bigshark"
                },
                ["skipEntityCodes"] = new JArray("tamablesfotsa:tame-*"),
                ["retaliateAttacks"] = true,
                ["seekingRange"] = 20,
                ["movespeed"] = 0.045,
                ["animation"] = moveAnimation,
                ["animationSpeed"] = 2.2
            });
        }
        tasks.Add(new JObject
        {
            ["code"] = "simplecommand",
            ["priority"] = 3.6,
            ["priorityForCancel"] = 3.6,
            ["minduration"] = 2000,
            ["maxduration"] = 10000,
            ["animation"] = sitAnimation,
            ["command"] = "sit"
        });
        tasks.Add(new JObject
        {
            ["code"] = "simplecommand",
            ["priority"] = 3.6,
            ["priorityForCancel"] = 3.6,
            ["minduration"] = 5000,
            ["maxduration"] = 15000,
            ["animation"] = layAnimation,
            ["command"] = "lay"
        });
        tasks.Add(new JObject
        {
            ["code"] = "stay",
            ["priority"] = 3.5,
            ["priorityForCancel"] = 3.5,
            ["command"] = "stay",
            ["movespeed"] = 0.006,
            ["maxDistance"] = 10,
            ["searchRange"] = 40
        });
        tasks.Add(new JObject
        {
            // Use the same dedicated follow task as Feral Kinship.  PetAI's
            // native follow task adds a base precondition that is not valid
            // for these runtime-created FotSA variants.
            ["code"] = aquatic ? "tamablesfotsaaquaticfollow" : "tamablesfotsabasicfollow",
            ["id"] = aquatic ? "tamablesfotsaaquaticfollow" : "tamablesfotsabasicfollow",
            ["priority"] = 3.5,
            ["priorityForCancel"] = 3.5,
            ["movespeed"] = 0.045,
            ["animation"] = followAnimation,
            ["animationSpeed"] = 2.2,
            ["maxDistance"] = 8,
            ["allowTeleport"] = true
        });

        return new JsonObject(taskAi);
    }

    private static bool IsBurrowingTask(JToken task)
    {
        if (string.Equals(task["animation"]?.Value<string>(), "burrow", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return task["tagsAppliedToEntity"] is JArray tags
            && tags.Values<string>().Any(tag =>
                string.Equals(tag, "state-burrowed", StringComparison.OrdinalIgnoreCase));
    }

    private static void NormalizeTamedAmbientTask(JObject task, string code)
    {
        // FotSA's wild task sets use their own priority bands, including
        // conditional wander tasks above 3.5 and long idles with cancel
        // priorities as high as 6. Those values are correct for wild animals
        // but must not outrank commands after the task is copied to a pet.
        task["slot"] = 0;
        if (code.Equals("getoutofwater", StringComparison.OrdinalIgnoreCase)
            || code.Equals("fishoutofwater", StringComparison.OrdinalIgnoreCase))
        {
            // Keep the immediate safety task above commands, matching the
            // working Feral Kinship companion task set.
            task["priority"] = 4.2;
            task["priorityForCancel"] = 4.2;
            return;
        }

        double maximumPriority = code.Equals("idle", StringComparison.OrdinalIgnoreCase)
            ? 1.2
            : code.Equals("wander", StringComparison.OrdinalIgnoreCase)
                ? 1.0
                : 0.5;
        double sourcePriority = task["priority"]?.Value<double>() ?? maximumPriority;
        double sourceCancelPriority = task["priorityForCancel"]?.Value<double>() ?? sourcePriority;
        task["priority"] = Math.Min(sourcePriority, maximumPriority);
        task["priorityForCancel"] = Math.Min(sourceCancelPriority, 1.35);
    }

    private static void NormalizeKnownSourceMovementErrors(
        EntityProperties source,
        JObject task,
        string code)
    {
        if (!code.Equals("wander", StringComparison.OrdinalIgnoreCase)
            || !source.Code.Domain.Equals("capreolinae", StringComparison.OrdinalIgnoreCase)
            || !source.Code.Path.Contains("rangifertarandusdomesticus", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string animation = task["animation"]?.Value<string>()
            ?? task["animationByType"]?["*"]?.Value<string>()
            ?? string.Empty;
        if (!animation.Equals("canter", StringComparison.OrdinalIgnoreCase)
            || task["movespeedByType"] is not JObject moveSpeeds)
        {
            return;
        }

        // Capreolinae 2.0.16 gives the domestic reindeer's canter speeds as
        // 0.0021/0.0027 while the adjacent wild reindeer and the source gait
        // configuration use 0.021/0.027. The misplaced decimal makes the
        // animal animate at a canter while moving almost imperceptibly.
        foreach (JProperty entry in moveSpeeds.Properties())
        {
            if (!entry.Name.Contains("rangifertarandusdomesticus", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            double speed = entry.Value.Value<double>();
            if (speed > 0 && speed < 0.01)
            {
                entry.Value = speed * 10;
            }
        }
    }

    private static void AddTaskIfMissing(JArray tasks, JObject task)
    {
        string code = task["code"]?.Value<string>() ?? string.Empty;
        foreach (JToken token in tasks)
        {
            if (string.Equals(token["code"]?.Value<string>(), code, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        tasks.Add(task);
    }

    private static JsonObject CreateTameableBehavior(
        Treat[] treats,
        string size,
        bool includeServerSettings,
        string? tameEntityCode)
    {
        JObject tameable = new()
        {
            ["code"] = EntityBehaviorTamablesFotsaTameable.BehaviorCode,
            ["treat"] = new JArray()
        };

        if (includeServerSettings)
        {
            tameable["size"] = size;
            tameable["disobediencePerDay"] = 0.05;
        }

        if (!string.IsNullOrWhiteSpace(tameEntityCode))
        {
            tameable["tameEntityCode"] = tameEntityCode;
        }

        JArray treatList = (JArray)tameable["treat"]!;
        foreach (Treat treat in treats)
        {
            JObject entry = new()
            {
                ["code"] = treat.Code,
                ["progress"] = treat.Progress,
                ["cooldown"] = treat.Cooldown
            };
            if (!string.Equals(treat.Domain, "game", StringComparison.OrdinalIgnoreCase))
            {
                entry["domain"] = treat.Domain;
            }

            treatList.Add(entry);
        }

        return new JsonObject(tameable);
    }

    private static JsonObject CreateReceiveCommandBehavior()
    {
        return new JsonObject(new JObject
        {
            ["code"] = "receivecommand",
            ["availableCommands"] = new JArray
            {
                new JObject
                {
                    ["commandName"] = "sit",
                    ["commandType"] = "SIMPLE",
                    ["minObedience"] = 0.20
                },
                new JObject
                {
                    ["commandName"] = "lay",
                    ["commandType"] = "SIMPLE",
                    ["minObedience"] = 0.20
                },
                new JObject
                {
                    ["commandName"] = "stay",
                    ["commandType"] = "COMPLEX",
                    ["minObedience"] = 0.10
                },
                new JObject
                {
                    ["commandName"] = "followmaster",
                    ["commandType"] = "COMPLEX",
                    ["minObedience"] = 0.60
                },
                new JObject
                {
                    ["commandName"] = "NEUTRAL",
                    ["commandType"] = "AGGRESSIONLEVEL",
                    ["minObedience"] = 0.20
                },
                new JObject
                {
                    ["commandName"] = "PROTECTIVE",
                    ["commandType"] = "AGGRESSIONLEVEL",
                    ["minObedience"] = 0.20
                },
                new JObject
                {
                    ["commandName"] = "AGGRESSIVE",
                    ["commandType"] = "AGGRESSIONLEVEL",
                    ["minObedience"] = 0.20
                },
                new JObject
                {
                    ["commandName"] = "PASSIVE",
                    ["commandType"] = "AGGRESSIONLEVEL",
                    ["minObedience"] = 0.20
                }
            }
        });
    }

    private static JsonObject CreateOwnerlessPurgeBehavior()
    {
        return new JsonObject(new JObject
        {
            ["code"] = EntityBehaviorTamablesFotsaOwnerlessPurge.BehaviorCode
        });
    }

    private static bool IsSupportedTarget(EntityProperties properties)
    {
        if (!SupportedDomains.Contains(properties.Code.Domain))
        {
            return false;
        }

        string path = properties.Code.Path;
        return IsBaby(properties) || IsAdult(properties);
    }

    private static bool IsAquatic(EntityProperties properties)
    {
        return AquaticDomains.Contains(properties.Code.Domain);
    }

    private static bool IsBaby(EntityProperties properties)
    {
        return IsBabyPath(properties.Code.Path);
    }

    private static bool IsAdult(EntityProperties properties)
    {
        string path = properties.Code.Path;
        return path.Contains("-adult", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("adult", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBabyPath(string path)
    {
        return path.Contains("-baby", StringComparison.OrdinalIgnoreCase)
            || path.Contains("baby-", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("baby", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetPetSize(string domain, string path)
    {
        if (domain.Equals("pantherinae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("machairodontinae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("meiolaniidae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("sirenia", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("elephantidae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("cervinae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("rhinocerotidae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("bovinae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("capreolinae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("chelonioidea", StringComparison.OrdinalIgnoreCase))
        {
            return "large";
        }

        if (domain.Equals("spheniscidae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("manidae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("viverridae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("felinae", StringComparison.OrdinalIgnoreCase))
        {
            return "small";
        }

        return "medium";
    }

    private static Treat[] GetTreats(string domain, string path)
    {
        if (domain.Equals("chelonioidea", StringComparison.OrdinalIgnoreCase))
        {
            // The pack defines leatherbacks as fish/insect eaters; its other
            // sea turtles use the vegetable diet family.
            return path.Contains("dermochelyscoriacea", StringComparison.OrdinalIgnoreCase)
                ? MeatTreats
                : VeggieTreats;
        }

        if (domain.Equals("spheniscidae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("pantherinae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("machairodontinae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("felinae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("thylacinidae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("iniidae", StringComparison.OrdinalIgnoreCase))
        {
            return MeatTreats;
        }

        if (domain.Equals("dinornithidae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("casuariidae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("manidae", StringComparison.OrdinalIgnoreCase))
        {
            return OmnivoreTreats;
        }

        if (domain.Equals("meiolaniidae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("sirenia", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("elephantidae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("cervinae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("rhinocerotidae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("bovinae", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("capreolinae", StringComparison.OrdinalIgnoreCase))
        {
            return VeggieTreats;
        }

        if (domain.Equals("vombatidae", StringComparison.OrdinalIgnoreCase))
        {
            return VeggieTreats;
        }

        return OmnivoreTreats;
    }

    internal static bool IsTamingTreat(ItemSlot slot)
    {
        AssetLocation? code = slot.Itemstack?.Collectible?.Code;
        if (code == null)
        {
            return false;
        }

        if (code.Domain.Equals(PetAiCookieDomain, StringComparison.OrdinalIgnoreCase)
            && (code.Path.Equals("petcookie-meat-perfect", StringComparison.OrdinalIgnoreCase)
                || code.Path.Equals("petcookie-veggie-perfect", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        foreach (Treat treat in OmnivoreTreats)
        {
            if (code.Domain.Equals(treat.Domain, StringComparison.OrdinalIgnoreCase)
                && code.Path.Equals(treat.Code, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class Treat
    {
        public Treat(string domain, string code, float progress, int cooldown)
        {
            Domain = domain;
            Code = code;
            Progress = progress;
            Cooldown = cooldown;
        }

        public string Domain { get; }
        public string Code { get; }
        public float Progress { get; }
        public int Cooldown { get; }
    }
}

public sealed class TamablesFotsaConfig
{
    public string TamingStage { get; set; } = "Baby";
}
