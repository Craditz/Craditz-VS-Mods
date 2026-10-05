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

namespace TamablesCritters;

public sealed class TamablesCrittersSystem : ModSystem
{
    private const string ConfigFileName = "TamablesCritters.json";
    private const string PetAiCookieDomain = "petai";

    private static readonly HashSet<string> SupportedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "africanmonitorlizards",
        "asianmonitorlizards",
        "bandedgeckos",
        "beardeddragons",
        "ensatinas",
        "knobtailedgeckos",
        "leopardgeckos",
        "newworldgianttortoises",
        "newzealandfrogs",
        "pacificnewts",
        "pondfrogsi",
        "pondfrogsiii",
        "rainfrogs",
        "thecritterpack",
        "moreanimals"
    };

    private static readonly HashSet<string> ExcludedSourceCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Temporary airborne form used by the crow's wild transformation AI.
        // The ordinary adult crow remains supported.
        "thecritterpack:crow_flying"
    };

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

    private TamablesCrittersConfig config = new();
    private static TamablesCrittersConfig activeConfig = new();
    private ICoreServerAPI? serverApi;

    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return true;
    }

    public override void Start(ICoreAPI api)
    {
        // Register custom tasks and behaviors before AssetsFinalize creates
        // the runtime tamed entity definitions.
        AiTaskRegistry.Register<AiTaskTamablesCrittersBasicFollow>("tamablescrittersbasicfollow");
        AiTaskRegistry.Register<AiTaskTamablesCrittersAirFollow>("tamablescrittersairfollow");

        api.RegisterEntityBehaviorClass(
            EntityBehaviorTamablesCrittersTameGuard.BehaviorCode,
            typeof(EntityBehaviorTamablesCrittersTameGuard)
        );
        api.RegisterEntityBehaviorClass(
            EntityBehaviorTamablesCrittersCommandBridge.BehaviorCode,
            typeof(EntityBehaviorTamablesCrittersCommandBridge)
        );
        api.RegisterEntityBehaviorClass(
            EntityBehaviorTamablesCrittersMultiply.BehaviorCode,
            typeof(EntityBehaviorTamablesCrittersMultiply)
        );
        api.RegisterEntityBehaviorClass(
            EntityBehaviorTamablesCrittersGrow.BehaviorCode,
            typeof(EntityBehaviorTamablesCrittersGrow)
        );
        api.RegisterEntityBehaviorClass(
            EntityBehaviorTamablesCrittersTameStageGuard.BehaviorCode,
            typeof(EntityBehaviorTamablesCrittersTameStageGuard)
        );
        api.RegisterEntityBehaviorClass(
            EntityBehaviorTamablesCrittersTameable.BehaviorCode,
            typeof(EntityBehaviorTamablesCrittersTameable)
        );
        api.RegisterEntityBehaviorClass(
            EntityBehaviorTamablesCrittersOwnerlessPurge.BehaviorCode,
            typeof(EntityBehaviorTamablesCrittersOwnerlessPurge)
        );
        api.RegisterEntityBehaviorClass(
            EntityBehaviorTamablesCrittersSourceName.BehaviorCode,
            typeof(EntityBehaviorTamablesCrittersSourceName)
        );

        AiTaskRegistry.Register<AiTaskTamablesCrittersBasicPetSeek>("tamablescrittersbasicpetseek");
        AiTaskRegistry.Register<AiTaskTamablesCrittersBasicPetMelee>("tamablescrittersbasicpetmelee");
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        List<EntityProperties> targets = new();
        foreach (EntityProperties properties in api.World.EntityTypes)
        {
            if (IsSupportedLifecycleTarget(properties))
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
        int directTameTargets = 0;
        int airFollowConfigs = 0;
        foreach (EntityProperties properties in targets)
        {
            string sourceKey = SourceKey(properties);
            if (!tameCodes.TryGetValue(sourceKey, out string? tameCode))
            {
                continue;
            }

            if (!IsInternalLifecycleStage(properties))
            {
                PatchEntity(properties, tameCode);
                directTameTargets++;
            }

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
                if (IsAirCreature(properties) && HasAirFollowTaskSet(registeredType))
                {
                    airFollowConfigs++;
                }
            }
            patched++;
            registered++;
        }

        List<string> unresolvedTamedGrowthCodes = ValidateGrowthMappings(api, targets, tameCodes);
        int sourceGrowthTypes = targets.Count(properties => FindBehavior(properties.Server?.BehaviorsAsJsonObj, "grow") != null);
        int resolvedTamedGrowthDestinations = sourceGrowthTypes - unresolvedTamedGrowthCodes.Count;
        if (unresolvedTamedGrowthCodes.Count != 0)
        {
            api.Logger.Error(
                "[tamablescritters] SAFETY CHECK FAILED: only {0}/{1} tamed baby types have registered tamed growth destinations. Missing: {2}",
                resolvedTamedGrowthDestinations,
                sourceGrowthTypes,
                string.Join(", ", unresolvedTamedGrowthCodes.Take(20))
            );
        }

        int spawnableTamedVariants = api.World.EntityTypes.Count(properties =>
            properties.Code.Domain.Equals("tamablescritters", StringComparison.OrdinalIgnoreCase)
            && properties.Server?.SpawnConditions != null
        );

        if (spawnableTamedVariants != 0)
        {
            api.Logger.Error(
                "[tamablescritters] SAFETY CHECK FAILED: {0} Tamables:Critters entity variants still have spawn conditions.",
                spawnableTamedVariants
            );
        }

        api.Logger.Notification(
            "[tamablescritters] Added PetAI definitions to {0} source types and registered {1} lifecycle variants ({2} resolvable, {3} complete server configs, {4} air-follow configs, {5} naturally spawnable tamed variants).",
            directTameTargets,
            registered,
            resolvable,
            completeServerConfigs,
            airFollowConfigs,
            spawnableTamedVariants
        );
        api.Logger.Notification(
            "[tamablescritters] Tamed growth destinations: {0}/{1} baby types resolved.",
            resolvedTamedGrowthDestinations,
            sourceGrowthTypes
        );
    }

    private static bool HasAirFollowTaskSet(EntityProperties properties)
    {
        JsonObject? taskAi = FindBehavior(properties.Server?.BehaviorsAsJsonObj, "taskai");
        if (taskAi?.Token is not JObject taskObject
            || taskObject["aitasks"] is not JArray tasks)
        {
            return false;
        }

        foreach (JToken task in tasks)
        {
            if (task["enabled"]?.Value<bool>() == false) continue;
            string code = task["code"]?.Value<string>() ?? string.Empty;
            if (code.Equals("tamablescrittersairfollow", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static List<string> ValidateGrowthMappings(
        ICoreAPI api,
        IEnumerable<EntityProperties> targets,
        IReadOnlyDictionary<string, string> tameCodes)
    {
        List<string> unresolved = new();
        foreach (EntityProperties source in targets)
        {
            JsonObject? grow = FindBehavior(source.Server?.BehaviorsAsJsonObj, "grow");
            if (grow == null) continue;
            JObject correctedGrow = grow.Token is JObject growObject
                ? (JObject)growObject.DeepClone()
                : new JObject();
            RewriteSupportedEntityReferences(correctedGrow, tameCodes);
            PatchKnownBrokenGrowthMappings(correctedGrow, source);
            List<string> destinations = ResolveReferencedEntityCodes(source, correctedGrow, tameCodes);
            if (destinations.Count == 0 || destinations.Any(code => code != "template" && api.World.GetEntityType(new AssetLocation(code)) == null))
                unresolved.Add(SourceKey(source));
        }
        return unresolved.OrderBy(code => code, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        serverApi = api;
        config = LoadConfig(api);
        activeConfig = config;
        api.Event.OnPlayerInteractEntity += OnPlayerInteractEntity;

        api.Logger.Notification(
            "[tamablescritters] Taming stage: {0}.",
            config.TamingStage
        );
    }

    public override void Dispose()
    {
        if (serverApi != null)
        {
            serverApi.Event.OnPlayerInteractEntity -= OnPlayerInteractEntity;
            serverApi = null;
            activeConfig = new TamablesCrittersConfig();
        }
    }

    private static TamablesCrittersConfig LoadConfig(ICoreAPI api)
    {
        try
        {
            TamablesCrittersConfig loaded = ConfigDefaults.LoadAndUpdate(
                api,
                ConfigFileName,
                () => new TamablesCrittersConfig());
            if (IsValidTamingStage(loaded.TamingStage))
            {
                return loaded;
            }

            TamablesCrittersConfig created = new();
            ConfigDefaults.StorePreservingUnknown(api, ConfigFileName, created);
            return created;
        }
        catch (Exception exception)
        {
            api.Logger.Warning(
                "[tamablescritters] Could not read {0}; using Any mode. {1}",
                ConfigFileName,
                exception.Message
            );
            return new TamablesCrittersConfig();
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
            || !IsSupportedDirectTarget(entity.Properties)
            || !IsTamingTreat(slot))
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

        if (IsAnyTamingStage(activeConfig.TamingStage)) return false;
        bool isYoung = IsYoung(entity.Properties);
        return IsAdultTamingStage(activeConfig.TamingStage) ? isYoung : !isYoung;
    }

    private static bool IsValidTamingStage(string? tamingStage)
    {
        return IsAnyTamingStage(tamingStage)
            || IsAdultTamingStage(tamingStage)
            || string.Equals(tamingStage, "Young", StringComparison.OrdinalIgnoreCase)
            || string.Equals(tamingStage, "Baby", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAdultTamingStage(string? tamingStage)
    {
        return string.Equals(tamingStage, "Adult", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAnyTamingStage(string? tamingStage)
    {
        return string.Equals(tamingStage, "Any", StringComparison.OrdinalIgnoreCase);
    }

    private static bool PatchEntity(EntityProperties properties, string tameEntityCode)
    {
        string size = GetPetSize(properties.Code.Domain, properties.Code.Path);
        Treat[] treats = GetTreats(properties.Code.Domain, properties.Code.Path);

        if (properties.Client != null)
        {
            properties.Client.BehaviorsAsJsonObj = ReplaceBehavior(
                properties.Client.BehaviorsAsJsonObj,
                CreateTameableBehavior(treats, size, includeServerSettings: false, tameEntityCode: null)
            );

            properties.Client.BehaviorsAsJsonObj = ReplaceBehavior(
                properties.Client.BehaviorsAsJsonObj,
                CreateReceiveCommandBehavior()
            );
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
        string size = GetPetSize(source.Code.Domain, source.Code.Path);
        Treat[] treats = GetTreats(source.Code.Domain, source.Code.Path);
        bool internalStage = IsInternalLifecycleStage(source);

        if (tamed.Client != null)
        {
            tamed.Client.BehaviorsAsJsonObj = BuildTamedClientBehaviors(
                source.Client?.BehaviorsAsJsonObj,
                treats,
                size,
                FindBehavior(source.Client?.BehaviorsAsJsonObj, "multiply")
                    ?? FindBehavior(source.Server?.BehaviorsAsJsonObj, "multiply"),
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
                tameCodes,
                internalStage
            );
        }
    }

    private static JsonObject[] BuildTamedClientBehaviors(
        JsonObject[]? existing,
        Treat[] treats,
        string size,
        JsonObject? sourceMultiply,
        AssetLocation sourceEntityCode)
    {
        List<JsonObject> result = FilterTamedBehaviors(existing, includeTaskAi: false);
        result.Add(CreateTameGuardBehavior());
        result.Add(CreateTameableBehavior(treats, size, includeServerSettings: false, tameEntityCode: null));
        result.Add(CreateReceiveCommandBehavior());
        result.Add(CreateNametagBehavior());
        result.Add(CreateSourceNameBehavior(sourceEntityCode));
        if (sourceMultiply != null)
        {
            result.Add(CreateTamedMultiplyClientBehavior(sourceMultiply));
        }

        return result.ToArray();
    }

    private static JsonObject[] BuildTamedServerBehaviors(
        JsonObject[]? existing,
        EntityProperties source,
        Treat[] treats,
        string size,
        IReadOnlyDictionary<string, string> tameCodes,
        bool internalStage)
    {
        List<JsonObject> result = FilterTamedBehaviors(existing, includeTaskAi: true);
        for (int i = 0; i < result.Count; i++)
        {
            if (!string.Equals(result[i]["code"].AsString(), "grow", StringComparison.OrdinalIgnoreCase)
                || result[i].Token is not JObject growObject)
            {
                continue;
            }

            JObject clone = (JObject)growObject.DeepClone();
            clone["code"] = EntityBehaviorTamablesCrittersGrow.BehaviorCode;
            RewriteSupportedEntityReferences(clone, tameCodes);
            PatchKnownBrokenGrowthMappings(clone, source);
            result[i] = new JsonObject(clone);
        }

        InsertTameGuardBeforeHealth(result);
        result.Add(CreateTameStageGuardBehavior());
        result.Add(CreateTameableBehavior(treats, size, includeServerSettings: true, tameEntityCode: null));
        result.Add(CreateOwnerlessPurgeBehavior());
        if (!internalStage)
        {
            result.Add(CreateReceiveCommandBehavior());
            result.Add(CreateNametagBehavior());
        }
        result.Add(CreateSourceNameBehavior(source.Code));
        if (!internalStage)
        {
            result.Add(CreateMortallyWoundableBehavior());
            InsertTaskAiBeforePettable(result, CreateTamedTaskAi(source));
            result.Add(CreateCommandBridgeBehavior());
        }

        JsonObject? sourceMultiply = FindBehavior(source.Server?.BehaviorsAsJsonObj, "multiply");
        if (sourceMultiply != null)
        {
            result.Add(CreateTamedMultiplyBehavior(sourceMultiply));
        }

        return result.ToArray();
    }

    private static void InsertTaskAiBeforePettable(List<JsonObject> behaviors, JsonObject taskAi)
    {
        // Vanilla pettable subscribes to the task manager during Initialize.
        // Source packs put taskai before pettable for that reason, so preserve that
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
                || code.Equals(EntityBehaviorTamablesCrittersTameable.BehaviorCode, StringComparison.OrdinalIgnoreCase)
                || code.Equals("receivecommand", StringComparison.OrdinalIgnoreCase)
                || code.Equals("nametag", StringComparison.OrdinalIgnoreCase)
                || code.Equals(EntityBehaviorTamablesCrittersTameGuard.BehaviorCode, StringComparison.OrdinalIgnoreCase)
                || code.Equals(EntityBehaviorTamablesCrittersMultiply.BehaviorCode, StringComparison.OrdinalIgnoreCase)
                || code.Equals(EntityBehaviorTamablesCrittersGrow.BehaviorCode, StringComparison.OrdinalIgnoreCase)
                || code.Equals(EntityBehaviorTamablesCrittersOwnerlessPurge.BehaviorCode, StringComparison.OrdinalIgnoreCase)
                || code.Equals(EntityBehaviorTamablesCrittersSourceName.BehaviorCode, StringComparison.OrdinalIgnoreCase)
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
        multiply["code"] = EntityBehaviorTamablesCrittersMultiply.BehaviorCode;

        RewriteTamedEntityCodeMap(multiply, "spawnEntityCodesByType");
        RewriteTamedEntityCodeMap(multiply, "requiresNearbyEntityCodeByType");
        RewriteTamedEntityCodeArray(multiply, "spawnEntityCodes");
        RewriteTamedEntityCodeArray(multiply, "requiresNearbyEntityCodes");
        RewriteTamedEntityCodeProperty(multiply, "spawnEntityCode");
        RewriteTamedEntityCodeProperty(multiply, "requiresNearbyEntityCode");

        return new JsonObject(multiply);
    }

    private static JsonObject CreateTamedMultiplyClientBehavior(JsonObject sourceMultiply)
    {
        JObject multiply = sourceMultiply.Token is JObject sourceObject
            ? (JObject)sourceObject.DeepClone()
            : new JObject();
        multiply["code"] = EntityBehaviorTamablesCrittersMultiply.BehaviorCode;
        return new JsonObject(multiply);
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
        return "tamablescritters:tame-" + domain + "-" + path;
    }

    private static void RewriteSupportedEntityReferences(
        JToken token,
        IReadOnlyDictionary<string, string> tameCodes)
    {
        if (token is JValue value && value.Type == JTokenType.String)
        {
            string sourceCode = value.Value<string>() ?? string.Empty;
            if (tameCodes.TryGetValue(sourceCode, out string? tameCode))
            {
                value.Value = tameCode;
                return;
            }

            int separator = sourceCode.IndexOf(':');
            if (separator > 0 && SupportedDomains.Contains(sourceCode[..separator]))
            {
                value.Value = ToTamedEntityCode(sourceCode);
            }
            return;
        }

        foreach (JToken child in token.Children())
        {
            RewriteSupportedEntityReferences(child, tameCodes);
        }
    }

    private static List<string> ResolveReferencedEntityCodes(
        EntityProperties source,
        JToken token,
        IReadOnlyDictionary<string, string> tameCodes)
    {
        List<string> result = new();
        Stack<JToken> pending = new();
        pending.Push(token);
        while (pending.Count > 0)
        {
            JToken current = pending.Pop();
            if (current is not JValue value)
            {
                foreach (JToken child in current.Children()) pending.Push(child);
                continue;
            }
            if (value.Type != JTokenType.String) continue;
            string code = value.Value<string>() ?? string.Empty;
            if (code.Contains('{') || code.Contains('*'))
            {
                // By-type/template destinations are resolved by the engine for
                // the concrete source variant after our domain rewrite.
                result.Add("template");
            }
            else if (code.StartsWith("tamablescritters:tame-", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(code);
            }
            else if (tameCodes.TryGetValue(code, out string? exact))
            {
                result.Add(exact);
            }
            else if (code.Contains(":" + source.Code.Path, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(ToTamedEntityCode(code));
            }
        }
        return result;
    }

    private static void PatchKnownBrokenGrowthMappings(JObject grow, EntityProperties source)
    {
        if (!source.Code.Domain.Equals("thecritterpack", StringComparison.OrdinalIgnoreCase)) return;

        string? destination = source.Code.Path switch
        {
            "blackswan-baby-young" => "tamablescritters:tame-thecritterpack-blackswan-baby-old",
            "call-duck-male-young" => "tamablescritters:tame-thecritterpack-call-duck-male-adult",
            "pekin-duck-male-young" => "tamablescritters:tame-thecritterpack-pekin-duck-male-adult",
            _ => null
        };
        if (destination != null)
        {
            grow.Remove("adultEntityCodesByType");
            grow["adultEntityCodes"] = new JArray(destination);
            return;
        }

        if (source.Code.Path.Equals("blackswan-baby-old", StringComparison.OrdinalIgnoreCase))
        {
            grow.Remove("adultEntityCodesByType");
            grow["adultEntityCodes"] = new JArray(
                "tamablescritters:tame-thecritterpack-blackswan-female-adult",
                "tamablescritters:tame-thecritterpack-blackswan-male-adult"
            );
        }
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
                    EntityBehaviorTamablesCrittersTameable.BehaviorCode,
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
            ?? FindBehavior(behaviors, EntityBehaviorTamablesCrittersTameable.BehaviorCode);
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
        return "tamablescritters:tame-" + properties.Code.Domain + "-" + properties.Code.Path;
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
            ["code"] = EntityBehaviorTamablesCrittersSourceName.BehaviorCode,
            ["sourceEntityCode"] = sourceEntityCode.ToString()
        });
    }

    private static JsonObject CreateTameGuardBehavior()
    {
        return new JsonObject(new JObject
        {
            ["code"] = EntityBehaviorTamablesCrittersTameGuard.BehaviorCode
        });
    }

    private static JsonObject CreateTameStageGuardBehavior()
    {
        return new JsonObject(new JObject
        {
            ["code"] = EntityBehaviorTamablesCrittersTameStageGuard.BehaviorCode
        });
    }

    private static JsonObject CreateCommandBridgeBehavior()
    {
        return new JsonObject(new JObject
        {
            ["code"] = EntityBehaviorTamablesCrittersCommandBridge.BehaviorCode
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
        bool air = IsAirCreature(source);
        string sourceCreatureType = GetSourceCreatureType(source);
        JObject taskAi = new()
        {
            ["code"] = "taskai",
            ["aiCreatureType"] = sourceCreatureType,
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
                if (code.Equals("getoutofwater", StringComparison.OrdinalIgnoreCase)
                    || code.Equals("fishoutofwater", StringComparison.OrdinalIgnoreCase)
                    || code.Equals("idle", StringComparison.OrdinalIgnoreCase)
                    || code.Equals("wander", StringComparison.OrdinalIgnoreCase)
                    || code.Equals("lookaround", StringComparison.OrdinalIgnoreCase)
                    || code.Equals("seekfoodandeat", StringComparison.OrdinalIgnoreCase)
                    || code.Equals("seekblockandlay", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsBurrowingTask(token))
                    {
                        continue;
                    }

                    JObject tamedTask = (JObject)token.DeepClone();
                    NormalizeTamedAmbientTask(tamedTask, code);
                    tasks.Add(tamedTask);
                }
            }
        }

        string attackAnimation = FindTaskAnimation(source, "meleeattack", "Attack");
        string moveAnimation = FindTaskAnimation(source, "seekentity", FindTaskAnimation(source, "wander", "Walk"));
        string followAnimation = air
            ? FindAnimation(source, "fly", FindAnimation(source, "flying", "Fly"))
            : FindAnimation(source, "run", FindAnimation(source, "gallop", FindAnimation(source, "swim", "Walk")));
        string sitAnimation = FindAnimation(source, "sit", FindAnimation(source, "idle", "Idle"));
        string layAnimation = FindAnimation(source, "lie", FindAnimation(source, "sleep", sitAnimation));

        JObject meleeTask = new()
        {
            ["code"] = "tamablescrittersbasicpetmelee",
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
            ["skipEntityCodes"] = new JArray("tamablescritters:tame-*"),
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
        tasks.Add(meleeTask);

        if (!air)
        {
            tasks.Add(new JObject
            {
                ["code"] = "tamablescrittersbasicpetseek",
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
                ["skipEntityCodes"] = new JArray("tamablescritters:tame-*"),
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
            ["code"] = air ? "tamablescrittersairfollow" : "tamablescrittersbasicfollow",
            ["id"] = air ? "tamablescrittersairfollow" : "tamablescrittersbasicfollow",
            ["priority"] = 3.5,
            ["priorityForCancel"] = 3.5,
            ["movespeed"] = 0.045,
            ["animation"] = followAnimation,
            ["animationSpeed"] = 2.2,
            ["maxDistance"] = 8,
            ["allowTeleport"] = true,
            ["pathCreatureType"] = sourceCreatureType
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
        // Source packs' wild task sets use their own priority bands, including
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
            ["code"] = EntityBehaviorTamablesCrittersTameable.BehaviorCode,
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
            ["code"] = EntityBehaviorTamablesCrittersOwnerlessPurge.BehaviorCode
        });
    }

    private static bool IsSupportedLifecycleTarget(EntityProperties properties)
    {
        return SupportedDomains.Contains(properties.Code.Domain)
            && !ExcludedSourceCodes.Contains(SourceKey(properties));
    }

    private static bool IsSupportedDirectTarget(EntityProperties properties)
    {
        return IsSupportedLifecycleTarget(properties) && !IsInternalLifecycleStage(properties);
    }

    private static bool IsInternalLifecycleStage(EntityProperties properties)
    {
        string path = properties.Code.Path;
        return path.Contains("-egg", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("egg-", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("egg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsYoung(EntityProperties properties)
    {
        string path = properties.Code.Path;
        return path.Contains("baby", StringComparison.OrdinalIgnoreCase)
            || path.Contains("juvenile", StringComparison.OrdinalIgnoreCase)
            || path.Contains("chick", StringComparison.OrdinalIgnoreCase)
            || path.Contains("calf", StringComparison.OrdinalIgnoreCase)
            || path.Contains("poult", StringComparison.OrdinalIgnoreCase)
            || path.Contains("young", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAirCreature(EntityProperties properties)
    {
        return properties.Habitat == EnumHabitat.Air;
    }

    private static string GetSourceCreatureType(EntityProperties properties)
    {
        JsonObject? taskAi = FindBehavior(properties.Server?.BehaviorsAsJsonObj, "taskai");
        string? value = taskAi?["aiCreatureType"].AsString();
        return string.IsNullOrWhiteSpace(value) ? "LandCreature" : value;
    }

    private static string GetPetSize(string domain, string path)
    {
        if (domain.Equals("africanmonitorlizards", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("asianmonitorlizards", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("newworldgianttortoises", StringComparison.OrdinalIgnoreCase)
            || (domain.Equals("thecritterpack", StringComparison.OrdinalIgnoreCase)
                && (path.Contains("yak", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("swan", StringComparison.OrdinalIgnoreCase))))
        {
            return "large";
        }

        return "small";
    }

    private static Treat[] GetTreats(string domain, string path)
    {
        if (domain.Equals("newworldgianttortoises", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("moreanimals", StringComparison.OrdinalIgnoreCase)
            || (domain.Equals("thecritterpack", StringComparison.OrdinalIgnoreCase)
                && (path.Contains("duck", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("swan", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("yak", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("fieldmus", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("squirrel", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("chipmunk", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("snail", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("isopod", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("woodlouse", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("robin", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("sparrow", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("waxwing", StringComparison.OrdinalIgnoreCase))))
        {
            return VeggieTreats;
        }

        if (domain.Equals("beardeddragons", StringComparison.OrdinalIgnoreCase)
            || (domain.Equals("thecritterpack", StringComparison.OrdinalIgnoreCase)
                && (path.Contains("crow", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("crab", StringComparison.OrdinalIgnoreCase))))
        {
            return OmnivoreTreats;
        }

        if (domain.Equals("africanmonitorlizards", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("asianmonitorlizards", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("bandedgeckos", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("ensatinas", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("knobtailedgeckos", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("leopardgeckos", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("newzealandfrogs", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("pacificnewts", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("pondfrogsi", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("pondfrogsiii", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("rainfrogs", StringComparison.OrdinalIgnoreCase)
            || (domain.Equals("thecritterpack", StringComparison.OrdinalIgnoreCase)
                && (path.Contains("owl", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("hedgehog", StringComparison.OrdinalIgnoreCase))))
        {
            return MeatTreats;
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

public sealed class TamablesCrittersConfig
{
    public string TamingStage { get; set; } = "Any";
}
