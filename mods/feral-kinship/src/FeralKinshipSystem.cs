using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using PetAI;
using VintageStoryConfigMigration;

namespace FeralKinship
{
public sealed class FeralKinshipSystem : ModSystem
{
    private const string ConfigFileName = "FeralKinship.json";
    internal const float KinTamingProgressMultiplier = 3f;

    private static readonly string[] SupportedTamingAnimals =
    {
        "bear",
        "chicken",
        "deer",
        "fox",
        "gazelle",
        "goat",
        "hare",
        "hyena",
        "cat",
        "pig",
        "raccoon",
        "sheep",
        "wolf"
    };

    private static readonly HashSet<string> SupportedTamingAnimalSet =
        new(SupportedTamingAnimals, StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> enabledTamingAnimals =
        new(SupportedTamingAnimals, StringComparer.OrdinalIgnoreCase);
    private static bool allowAdultTaming = true;
    private static bool allowBabyTaming;

    internal const string BabyTamingMarkerKey = "feralKinshipBabyTaming";

    private static readonly TreatRule[] PlantTreats =
    {
        new("game", "fruit-*"),
        new("game", "vegetable-*"),
        new("game", "grain-*"),
        new("game", "seeds-*"),
        new("game", "drygrass"),
        new("petai", "petcookie-veggie-perfect")
    };

    private static readonly TreatRule[] MeatTreats =
    {
        new("game", "fruit-*"),
        new("game", "bushmeat-raw"),
        new("game", "redmeat-raw"),
        new("game", "poultry-raw"),
        new("petai", "petcookie-meat-perfect")
    };

    private static readonly TreatRule[] OmnivoreTreats =
    {
        new("game", "fruit-*"),
        new("game", "vegetable-*"),
        new("game", "grain-*"),
        new("game", "bushmeat-raw"),
        new("game", "redmeat-raw"),
        new("game", "poultry-raw"),
        new("petai", "petcookie-meat-perfect"),
        new("petai", "petcookie-veggie-perfect")
    };

    private static readonly Dictionary<string, SpeciesRule> SpeciesRules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bear"] = new("bear", OmnivoreTreats),
        ["chicken"] = new("chicken", PlantTreats),
        ["deer"] = new("deer", PlantTreats),
        ["fox"] = new("fox", MeatTreats),
        ["gazelle"] = new("gazelle", PlantTreats),
        ["goat"] = new("goat", PlantTreats),
        ["hare"] = new("hare", PlantTreats),
        ["hyena"] = new("hyena", MeatTreats),
        ["pig"] = new("pig", OmnivoreTreats),
        ["raccoon"] = new("raccoon", OmnivoreTreats),
        ["sheep"] = new("sheep", PlantTreats),
        ["wolf"] = new("wolf", MeatTreats)
    };

    // Native Cats owns the entity definitions and PetAI treat values. Keep
    // this rule separate from the game-domain species table so Cats can use
    // the progress bonus without ever entering Kinship's tame conversion.
    private static readonly SpeciesRule CatsSpeciesRule =
        new("cat", Array.Empty<TreatRule>(), usesEntityTreatList: true);

    private sealed class TreatRule
    {
        public TreatRule(string domain, string path)
        {
            Domain = domain;
            Path = path;
        }

        public string Domain { get; }
        public string Path { get; }
    }

    private sealed class SpeciesRule
    {
        public SpeciesRule(string race, TreatRule[] treats, bool usesEntityTreatList = false)
        {
            Race = race;
            Treats = treats;
            UsesEntityTreatList = usesEntityTreatList;
        }

        public string Race { get; }
        public TreatRule[] Treats { get; }
        public bool UsesEntityTreatList { get; }
    }

    private ICoreServerAPI? serverApi;

    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return forSide == EnumAppSide.Server;
    }

    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass(
            EntityBehaviorFeralKinshipTameGuard.BehaviorCode,
            typeof(EntityBehaviorFeralKinshipTameGuard)
        );
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        serverApi = api;
        enabledTamingAnimals = LoadTamingConfig(api);
        // PetAI installs its BecomeAdult postfix from its common Start phase.
        // Install our replacement after that phase so PetAI cannot add the
        // unsafe postfix back after we remove it.
        PetAiGrowthCompatibility.Install(api);
        PetAiTamingCompatibility.Install(api);
        string enabledList = string.Join(
            ", ",
            SupportedTamingAnimals.Where(enabledTamingAnimals.Contains)
        );
        api.Logger.Notification(
            "[feralkinship] Taming enabled for: {0}.",
            string.IsNullOrEmpty(enabledList) ? "none" : enabledList
        );
        AiTaskRegistry.Register<AiTaskFeralKinshipFlee>("feralkinshipfleeentity");
        AiTaskRegistry.Register<AiTaskFeralKinshipSeek>("feralkinshipseekentity");
        AiTaskRegistry.Register<AiTaskFeralKinshipMelee>("feralkinshipmeleeattack");
        AiTaskRegistry.Register<AiTaskFeralKinshipBasicFollow>("feralkinshipbasicfollow");
        AiTaskRegistry.Register<AiTaskFeralKinshipBasicPetSeek>("feralkinshipbasicpetseek");
        AiTaskRegistry.Register<AiTaskFeralKinshipBasicPetMelee>("feralkinshipbasicpetmelee");
        api.Event.OnPlayerInteractEntity += OnPlayerInteractEntity;
    }

    public override void Dispose()
    {
        if (serverApi != null)
        {
            serverApi.Event.OnPlayerInteractEntity -= OnPlayerInteractEntity;
            serverApi = null;
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
        SpeciesRule? rule = GetTamingSpeciesRule(entity);
        if (rule == null || mode != (int)EnumInteractMode.Interact || slot == null)
        {
            return;
        }

        EnsureButcheringTamingTreat(entity, rule, slot);

        if (!IsTamingEnabled(rule.Race))
        {
            if (IsAcceptedTreat(entity, rule, slot) || MatchesAcceptedTreatPath(entity, rule, slot))
            {
                handling = EnumHandling.PreventSubsequent;
            }

            return;
        }

        if (!IsTamingAllowed(entity, rule.Race))
        {
            if (IsAcceptedTreat(entity, rule, slot) || MatchesAcceptedTreatPath(entity, rule, slot))
            {
                handling = EnumHandling.PreventSubsequent;
            }

            return;
        }

        if (!IsAcceptedTreat(entity, rule, slot))
        {
            // PetAI compares only the path portion of a treat code. Stop its
            // later handler when the path matches but the domain does not.
            if (MatchesAcceptedTreatPath(entity, rule, slot))
            {
                handling = EnumHandling.PreventSubsequent;
            }
            return;
        }

        if (IsBabyEntity(entity))
        {
            MarkBabyTamingTarget(entity);
        }

        // PetAI consumes the treat but does not mark the player's inventory
        // slot dirty. Refresh it after the interaction on the server.
        byPlayer.Entity.World.RegisterCallback(_ => slot.MarkDirty(), 0);
    }

    internal static bool IsMatchingKin(Entity entity, IPlayer byPlayer)
    {
        SpeciesRule? rule = GetTamingSpeciesRule(entity);
        return rule != null
            && byPlayer.Entity is EntityPlayer player
            && string.Equals(FeralRaceResolver.GetRace(player), rule.Race, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsAcceptedTamingTreat(Entity entity, ItemSlot slot)
    {
        SpeciesRule? rule = GetTamingSpeciesRule(entity);
        EnsureButcheringTamingTreat(entity, rule, slot);
        return rule != null
            && IsTamingAllowed(entity, rule.Race)
            && IsAcceptedTreat(entity, rule, slot);
    }

    internal static bool IsButcheringTamingTreatEligible(Entity entity, ItemSlot slot)
    {
        SpeciesRule? rule = GetTamingSpeciesRule(entity);
        EntityBehaviorTameable? tameable = entity.GetBehavior<EntityBehaviorTameable>();
        return rule != null
            && tameable != null
            && (tameable.DomesticationLevel == DomesticationLevel.WILD
                || tameable.DomesticationLevel == DomesticationLevel.TAMING)
            && IsTamingAllowed(entity, rule.Race)
            && FeralKinshipButcheringFoodPolicy.TryGetTamingTreat(
                rule.Race,
                slot?.Itemstack?.Collectible?.Code?.Domain ?? string.Empty,
                slot?.Itemstack?.Collectible?.Code?.Path ?? string.Empty,
                out _,
                out _);
    }

    internal static bool ShouldBlockTamingInteraction(Entity entity, ItemSlot slot)
    {
        SpeciesRule? rule = GetTamingSpeciesRule(entity);
        if (rule == null) return false;

        bool exactMatch = IsAcceptedTamingTreat(entity, slot);
        bool pathMatch = MatchesAcceptedTreatPath(entity, rule, slot);
        return !IsTamingAllowed(entity, rule.Race)
            ? exactMatch || pathMatch
            : !exactMatch && pathMatch;
    }

    internal static bool IsTamingEnabled(string race)
    {
        return enabledTamingAnimals.Contains(race);
    }

    internal static bool IsTamingAllowed(Entity entity, string race)
    {
        return IsTamingEnabled(race)
            && (IsBabyEntity(entity) ? allowBabyTaming : allowAdultTaming);
    }

    internal static void MarkBabyTamingTarget(Entity entity)
    {
        if (!IsBabyEntity(entity)) return;

        ITreeAttribute status = entity.WatchedAttributes.GetTreeAttribute("domesticationstatus")
            ?? new TreeAttribute();
        status.SetBool(BabyTamingMarkerKey, true);
        entity.WatchedAttributes["domesticationstatus"] = status;
        entity.WatchedAttributes.MarkPathDirty("domesticationstatus");
    }

    internal static bool IsMarkedBabyTamingTarget(Entity entity)
    {
        return entity.WatchedAttributes.GetTreeAttribute("domesticationstatus")?
            .GetBool(BabyTamingMarkerKey, false) == true;
    }

    internal static bool IsBabyEntity(Entity entity)
    {
        if (IsNativeCatsEntity(entity))
        {
            return string.Equals(
                entity.Code?.Path.Split('-').ElementAtOrDefault(1),
                "kitten",
                StringComparison.OrdinalIgnoreCase
            );
        }

        string path = entity.Code?.Path ?? string.Empty;
        string[] parts = path.Split('-');
        return (parts.Length == 2
                && string.Equals(parts[0], "chicken", StringComparison.OrdinalIgnoreCase)
                && (string.Equals(parts[1], "baby", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(parts[1], "henpoult", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(parts[1], "roosterpoult", StringComparison.OrdinalIgnoreCase)))
            || (parts.Length == 4
                && string.Equals(parts[2], "baby", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool TryGetTamedAdultCode(Entity adult, out AssetLocation code)
    {
        code = null!;
        if (adult.Code == null
            || !string.Equals(adult.Code.Domain, "game", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = adult.Code.Path.Split('-');
        if (parts.Length == 2
            && string.Equals(parts[0], "chicken", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(parts[1], "hen", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[1], "rooster", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[1], "henpoult", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[1], "roosterpoult", StringComparison.OrdinalIgnoreCase)))
        {
            string age = parts[1].EndsWith("poult", StringComparison.OrdinalIgnoreCase)
                ? parts[1][..^5]
                : parts[1];
            code = new AssetLocation("feralkinship", $"tamechicken-{age}");
            return true;
        }

        if (parts.Length != 4
            || (!string.Equals(parts[2], "adult", StringComparison.OrdinalIgnoreCase)
                && !(string.Equals(parts[0], "pig", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(parts[2], "elder", StringComparison.OrdinalIgnoreCase)))
            || (!string.Equals(parts[3], "male", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(parts[3], "female", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        string race = string.Equals(parts[0], "moose", StringComparison.OrdinalIgnoreCase)
            ? "deer"
            : parts[0];
        if (!SpeciesRules.ContainsKey(race)) return false;

        string tameCode = race == "pig"
            ? $"tamepig-{parts[1]}-{parts[2]}-{parts[3]}"
            : $"tame{race}-{parts[1]}-{parts[3]}";
        code = new AssetLocation("feralkinship", tameCode);
        return true;
    }

    internal static bool IsPerfectCookie(ItemSlot slot)
    {
        string domain = slot?.Itemstack?.Collectible?.Code?.Domain ?? string.Empty;
        string path = slot?.Itemstack?.Collectible?.Code?.Path ?? string.Empty;
        return string.Equals(domain, "petai", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(path, "petcookie-meat-perfect", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, "petcookie-veggie-perfect", StringComparison.OrdinalIgnoreCase));
    }

    private static SpeciesRule? GetTamingSpeciesRule(Entity entity)
    {
        return GetWildSpeciesRule(entity) ?? GetCatsSpeciesRule(entity);
    }

    private static SpeciesRule? GetWildSpeciesRule(Entity entity)
    {
        if (entity.Code == null || !string.Equals(entity.Code.Domain, "game", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string[] parts = entity.Code.Path.Split('-');
        if (parts.Length == 2 && string.Equals(parts[0], "chicken", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(parts[1], "hen", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[1], "rooster", StringComparison.OrdinalIgnoreCase)))
        {
            return SpeciesRules["chicken"];
        }

        if (parts.Length == 2
            && string.Equals(parts[0], "chicken", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(parts[1], "baby", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[1], "henpoult", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[1], "roosterpoult", StringComparison.OrdinalIgnoreCase)))
        {
            return SpeciesRules["chicken"];
        }

        string speciesId = string.Equals(parts.ElementAtOrDefault(0), "moose", StringComparison.OrdinalIgnoreCase)
            ? "deer"
            : parts.ElementAtOrDefault(0) ?? string.Empty;
        if (parts.Length != 4 || !SpeciesRules.TryGetValue(speciesId, out SpeciesRule? rule))
        {
            return null;
        }

        if (!string.Equals(parts[2], "adult", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(parts[2], "baby", StringComparison.OrdinalIgnoreCase)
            && !(string.Equals(speciesId, "pig", StringComparison.OrdinalIgnoreCase)
                && string.Equals(parts[2], "elder", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return string.Equals(parts[3], "male", StringComparison.OrdinalIgnoreCase)
            || string.Equals(parts[3], "female", StringComparison.OrdinalIgnoreCase)
            ? rule
            : null;
    }

    private static SpeciesRule? GetCatsSpeciesRule(Entity entity)
    {
        return IsNativeCatsEntity(entity) ? CatsSpeciesRule : null;
    }

    private static bool IsNativeCatsEntity(Entity entity)
    {
        if (entity.Code == null
            || !string.Equals(entity.Code.Domain, "cats", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = entity.Code.Path.Split('-');
        return parts.Length == 3
            && string.Equals(parts[0], "cat", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(parts[1], "female", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[1], "male", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parts[1], "kitten", StringComparison.OrdinalIgnoreCase));
    }

    private static HashSet<string> LoadTamingConfig(ICoreAPI api)
    {
        FeralKinshipConfig config;
        JsonObject? rawConfig = null;
        try
        {
            rawConfig = api.LoadModConfig(ConfigFileName);
            string?[]? configuredValues = rawConfig?.KeyExists("EnabledTamingAnimals") == true
                ? rawConfig["EnabledTamingAnimals"].AsArray<string>()
                : null;
            config = new FeralKinshipConfig
            {
                AllowAdultTaming = rawConfig?.KeyExists("AllowAdultTaming") == true
                    ? rawConfig["AllowAdultTaming"].AsBool(true)
                    : true,
                AllowBabyTaming = rawConfig?.KeyExists("AllowBabyTaming") == true
                    && rawConfig["AllowBabyTaming"].AsBool(false),
                EnabledTamingAnimals = configuredValues == null
                    ? new FeralKinshipConfig().EnabledTamingAnimals
                    : configuredValues.Select(value => value ?? string.Empty).ToList()
            };
        }
        catch (Exception exception)
        {
            api.Logger.Warning(
                "[feralkinship] Could not read {0}; all supported taming remains enabled. {1}",
                ConfigFileName,
                exception.Message
            );
            config = new FeralKinshipConfig();
        }

        HashSet<string> enabled = new(StringComparer.OrdinalIgnoreCase);
        List<string> configured = config.EnabledTamingAnimals ?? new List<string>();
        bool hadNonEmptyEntry = false;
        foreach (string? value in configured)
        {
            string normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
            hadNonEmptyEntry |= !string.IsNullOrEmpty(normalized);
            if (SupportedTamingAnimalSet.Contains(normalized))
            {
                enabled.Add(normalized);
            }
            else if (!string.IsNullOrEmpty(normalized))
            {
                api.Logger.Warning(
                    "[feralkinship] Ignoring unknown EnabledTamingAnimals entry '{0}'. Valid entries: {1}.",
                    value,
                    string.Join(", ", SupportedTamingAnimals)
                );
            }
        }

        if (hadNonEmptyEntry && enabled.Count == 0)
        {
            api.Logger.Warning(
                "[feralkinship] EnabledTamingAnimals contained no valid entries; keeping all supported taming enabled."
            );
            enabled.UnionWith(SupportedTamingAnimals);
        }

        config.EnabledTamingAnimals = SupportedTamingAnimals
            .Where(enabled.Contains)
            .ToList();
        allowAdultTaming = config.AllowAdultTaming;
        allowBabyTaming = config.AllowBabyTaming;
        ConfigDefaults.EnsureDefaults(api, ConfigFileName, rawConfig, config);
        return enabled;
    }

    private static bool IsAcceptedTreat(Entity entity, SpeciesRule rule, ItemSlot slot)
    {
        if (slot?.Itemstack?.Collectible?.Code == null)
        {
            return false;
        }

        string treatDomain = slot.Itemstack.Collectible.Code.Domain;
        string treatPath = slot.Itemstack.Collectible.Code.Path;

        // Butchering entries are injected into PetAI's live list only while
        // the species and age gates are enabled. Read that live entry back so
        // the server event and PetAI's direct OnInteract hook share the same
        // accepted-treat decision. Ordinary game/petai entries continue to
        // use Kinship's static compatibility table below.
        if (string.Equals(treatDomain, "butchering", StringComparison.OrdinalIgnoreCase))
        {
            EntityBehaviorTameable? tameable = entity.GetBehavior<EntityBehaviorTameable>();
            if (tameable != null
                && FeralKinshipButcheringFoodPolicy.IsAcceptedDynamicTamingTreat(
                    rule.Race,
                    treatDomain,
                    treatPath,
                    tameable.treatList.Select(item => (item.Domain, item.Name))))
            {
                return true;
            }
        }

        if (rule.UsesEntityTreatList)
        {
            EntityBehaviorTameable? tameable = entity.GetBehavior<EntityBehaviorTameable>();
            if (tameable == null
                || (tameable.DomesticationLevel != DomesticationLevel.WILD
                    && tameable.DomesticationLevel != DomesticationLevel.TAMING))
            {
                return false;
            }

            return tameable.treatList.Any(item =>
                string.Equals(item.Domain, treatDomain, StringComparison.OrdinalIgnoreCase)
                && MatchesPath(item.Name, treatPath));
        }

        EntityBehaviorTameable? dynamicTameable = entity.GetBehavior<EntityBehaviorTameable>();
        if (dynamicTameable != null
            && FeralKinshipButcheringFoodPolicy.IsAcceptedDynamicTamingTreat(
                rule.Race,
                "butchering",
                treatPath,
                dynamicTameable.treatList.Select(item => (item.Domain, item.Name))))
        {
            return true;
        }

        foreach (TreatRule treat in rule.Treats)
        {
            if (string.Equals(treatDomain, treat.Domain, StringComparison.OrdinalIgnoreCase)
                && MatchesPath(treat.Path, treatPath))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureButcheringTamingTreat(
        Entity entity,
        SpeciesRule? rule,
        ItemSlot slot)
    {
        if (rule == null
            || !IsTamingAllowed(entity, rule.Race)
            || !FeralKinshipButcheringFoodPolicy.TryGetTamingTreat(
                rule.Race,
                slot?.Itemstack?.Collectible?.Code?.Domain ?? string.Empty,
                slot?.Itemstack?.Collectible?.Code?.Path ?? string.Empty,
                out float progress,
                out long cooldownSeconds))
        {
            return;
        }

        EntityBehaviorTameable? tameable = entity.GetBehavior<EntityBehaviorTameable>();
        if (tameable == null
            || (tameable.DomesticationLevel != DomesticationLevel.WILD
                && tameable.DomesticationLevel != DomesticationLevel.TAMING))
        {
            return;
        }

        string path = slot?.Itemstack?.Collectible?.Code?.Path ?? string.Empty;
        if (string.IsNullOrEmpty(path)) return;
        if (tameable.treatList.Any(item =>
                string.Equals(item.Domain, "butchering", StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Name, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        tameable.treatList.Add(new TamingItem
        {
            Domain = "butchering",
            Name = path,
            Progress = progress,
            Cooldown = cooldownSeconds
        });
    }

    private static bool MatchesAcceptedTreatPath(Entity entity, SpeciesRule rule, ItemSlot slot)
    {
        string path = slot?.Itemstack?.Collectible?.Code?.Path ?? string.Empty;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (rule.UsesEntityTreatList)
        {
            EntityBehaviorTameable? tameable = entity.GetBehavior<EntityBehaviorTameable>();
            return tameable?.treatList.Any(item => MatchesPath(item.Name, path)) == true;
        }

        EntityBehaviorTameable? dynamicTameable = entity.GetBehavior<EntityBehaviorTameable>();
        if (dynamicTameable != null
            && FeralKinshipButcheringFoodPolicy.IsAcceptedDynamicTamingTreat(
                rule.Race,
                "butchering",
                path,
                dynamicTameable.treatList.Select(item => (item.Domain, item.Name))))
        {
            return true;
        }

        foreach (TreatRule treat in rule.Treats)
        {
            if (MatchesPath(treat.Path, path))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesPath(string pattern, string path)
    {
        return pattern.EndsWith("*", StringComparison.Ordinal)
            ? path.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(path, pattern, StringComparison.OrdinalIgnoreCase);
    }
}
}
