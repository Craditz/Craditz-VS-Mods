using HarmonyLib;
using rustboundmagic.src;
using rustboundmagic.src.system.effects;
using rustboundmagic.src.system.spells;
using rustboundmagic.src.system.recipes.oculus.grimoire;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace RustboundExpansion;

public sealed class ExpansionSystem : ModSystem
{
    private Harmony? harmony;
    private static Harmony? sharedHarmony;
    private static int patchUsers;
    private ICoreServerAPI? server;
    private BrittleSigilVisuals? visuals;
    private static readonly StatusEffectsRM Effects = new();
    public const string ResonanceId = "rbxresonance1";
    public const string BrittleId = "rbxbrittlesigil1";
    public const string ArcId = "rbxarclightning1";
    public const string BurstId = "rbxburstofspeed1";
    public const string RecoveryId = "rbxburstfatigue";
    public const string BrittleBonusKey = "rustboundexpansion-brittlebonus";

    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass("RbxManaGem", typeof(ManaGemItem));
        api.RegisterItemClass("RbxHolyGrenade", typeof(HolyGrenadeItem));
        api.RegisterEntity("RbxHolyGrenade", typeof(HolyGrenadeEntity));
        api.RegisterEntity("RbxGrenadePin", typeof(GrenadePinEntity));
        api.RegisterItemClass("RbxTravelMark", typeof(TravelMarkItem));
        api.RegisterItemClass("RbxMarkBag", typeof(MarkBagItem));
        api.RegisterItemClass("RbxMischiefCipher", typeof(MischiefCipherItem));
        api.RegisterItemClass("RbxBloodCore", typeof(BloodCoreItem));
        api.RegisterCollectibleBehaviorClass("RbxBloodCoreTooltip",typeof(BloodCoreTooltipBehavior));
        api.RegisterEntity("RbxBloodGoober", typeof(BloodGoober));
        api.RegisterEntity("RbxSentry", typeof(SummonedSentry));
        api.RegisterEntity("RbxMagicProjectile", typeof(MagicProjectile));
        api.RegisterEntity("RbxNextProjectile", typeof(NextProjectile));
        api.RegisterEntity("RbxTravelGate", typeof(TravelGate));
    }

    public override void StartPre(ICoreAPI api)
    {
        if (api.ModLoader.GetMod("rustboundmagic")?.Info.Version != "4.0.7")
            throw new InvalidOperationException("Rustbound Expansion requires Rustbound Magic 4.0.7; its integration patches have not been checked against other versions.");
        InstallPatches();
    }

    internal void InstallPatches()
    {
        if (harmony != null) return;
        // Singleplayer runs a client and server ModSystem in the same process.
        // Install each process-wide Harmony patch once, then retain it until
        // both sides have disposed their systems.
        if (sharedHarmony != null) { harmony = sharedHarmony; patchUsers++; return; }
        harmony = new Harmony("franc.rustboundexpansion");
        try
        {
        TestingAttunement.Install(harmony);
        ChannelHooks.Install(harmony);
        ControlEffects.Install(harmony);
        NextModifiers.Install(harmony);
        MischiefSystem.Install(harmony);
        CreatureSupport.Install(harmony);
        HeatItemSystem.Install(harmony);
        HotHands.Install(harmony);
        HeatedStoneThrows.Install(harmony);
        BloodAttunement.Install(harmony);
        BloodMessages.Install(harmony);
        Bulwark.Install(harmony);
        HolyGrenadeSystem.Install(harmony);
        harmony.Patch(AccessTools.Method(typeof(Entity), nameof(Entity.Die)), prefix: new HarmonyMethod(typeof(ExpansionSystem), nameof(BeforeDeath)));
        harmony.Patch(AccessTools.Method(typeof(EntityBehaviorHealth), "ApplyOnDamageDelegates"),
            prefix: new HarmonyMethod(typeof(ExpansionSystem), nameof(BeforeMitigation)),
            postfix: new HarmonyMethod(typeof(ExpansionSystem), nameof(AfterMitigation)));
        Patch(typeof(RustboundMagic), "SpellList", nameof(AfterSpellList));
        Patch(typeof(RustboundMagic), "OculusRecipeList", nameof(AfterRecipes));
        Patch(typeof(StatusEffectsRM), "ResetAllTempBuffs", nameof(AfterReset));
        Patch(typeof(StatusEffectsRM), "ClearEffectState", nameof(AfterClearEffect));
        Patch(typeof(SpellcastingModifierSystemRM), "CalculateModifiedSpellAttributeStats", nameof(AfterModifiedStats));
        Patch(typeof(SpellcastingModifierSystemRM), "CalculateModifiedSpellAttributeDuration", nameof(AfterModifiedDuration));
        harmony.Patch(AccessTools.Method(typeof(EntityBehaviorHealth), nameof(EntityBehaviorHealth.OnEntityReceiveDamage)),
            prefix: new HarmonyMethod(typeof(ExpansionSystem), nameof(BeforeHealthDamage)));
        sharedHarmony = harmony; patchUsers = 1;
        }
        catch { harmony.UnpatchAll("franc.rustboundexpansion"); harmony = null; throw; }
    }

    private void Patch(Type type, string target, string postfix) => harmony!.Patch(
        AccessTools.Method(type, target) ?? throw new MissingMethodException(type.FullName, target),
        postfix: new HarmonyMethod(typeof(ExpansionSystem), postfix));

    public override void AssetsFinalize(ICoreAPI api)
    {
        var upstream = api.ModLoader.GetModSystem<RustboundMagic>();
        RegisterSpells(upstream);
        RegisterRecipes();
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        server = api;
        RuneNames.Register(api);
        api.Event.OnEntityLoaded += ClearAddonEffects;
        api.Event.OnEntityLoaded += CreatureBuffPersistence.Restore;
        api.Event.OnEntityDeath += OnDeath;
        api.Event.PlayerNowPlaying += OnPlayer;
        api.Event.PlayerDisconnect += OnPlayer;
    }

    public override void StartClientSide(ICoreClientAPI api) => visuals = new BrittleSigilVisuals(api);

    private static void OnPlayer(IServerPlayer player) => ClearAddonEffects(player.Entity);
    private static void OnDeath(Entity entity, DamageSource source)
    {
        ExpansionRuntime.For(entity.World)?.OnDeath(entity);
        CreatureBuffPersistence.Clear(entity);
        ClearAddonEffects(entity);
    }
    private static void BeforeDeath(Entity __instance, EnumDespawnReason reason)
    {
        if (reason == EnumDespawnReason.Death && __instance.Alive && __instance.World?.Side == EnumAppSide.Server)
        {
            ExpansionRuntime.For(__instance.World)?.OnDeath(__instance);
            CreatureBuffPersistence.Clear(__instance);
        }
    }

    public static void RegisterSpells(RustboundMagic upstream)
    {
        foreach(var d in FieldCatalog.All)
        {RustboundMagic.FullSpellDictionary[d.Id]=new FieldSpell(d);RustboundMagic.SpellBuffStats[d.Id]=new();}
        RustboundMagic.FullSpellDictionary[HolyGrenadeSpell.SpellId]=new HolyGrenadeSpell();
        RustboundMagic.SpellBuffStats[HolyGrenadeSpell.SpellId]=new();
        foreach(var definition in BloodCatalog.All)
        {
            RustboundMagic.FullSpellDictionary[definition.Id]=new BloodSpell(definition);
            RustboundMagic.SpellBuffStats[definition.Id]=new();
        }
        RustboundMagic.FullSpellDictionary[HeatItemSpell.SpellId] = new HeatItemSpell();
        RustboundMagic.SpellBuffStats[HeatItemSpell.SpellId] = new();
        RustboundMagic.FullSpellDictionary[HotHandsSpell.SpellId] = new HotHandsSpell();
        RustboundMagic.SpellBuffStats[HotHandsSpell.SpellId] = new();
        RustboundMagic.FullSpellDictionary[ResonanceId] = new ResonanceSpell();
        RustboundMagic.FullSpellDictionary[BrittleId] = new BrittleSigilSpell();
        RustboundMagic.FullSpellDictionary[ArcId] = new ArcLightningSpell();
        RustboundMagic.FullSpellDictionary[BurstId] = new BurstOfSpeedSpell();
        RustboundMagic.FullSpellDictionary[RecoveryId] = new BurstRecoverySpell();
        foreach (var definition in BacklogCatalog.All)
        {
            RustboundMagic.FullSpellDictionary[definition.Id] = new BacklogSpell(definition);
            RustboundMagic.SpellBuffStats[definition.Id] = new();
        }
        foreach (var d in NextCatalog.All) { RustboundMagic.FullSpellDictionary[d.Id] = new NextSpell(d); RustboundMagic.SpellBuffStats[d.Id] = new(); }
        foreach (var d in MemeCatalog.All) { RustboundMagic.FullSpellDictionary[d.Id] = new MemeSpell(d); RustboundMagic.SpellBuffStats[d.Id] = new(); }
        foreach(var d in BulwarkCatalog.All)
        {RustboundMagic.FullSpellDictionary[d.Id]=new BulwarkSpell(d);RustboundMagic.SpellBuffStats[d.Id]=new(){["walkspeed"]=-.25f};}
        foreach (var id in NextCatalog.Hidden) { RustboundMagic.FullSpellDictionary[id] = new NextStatusSpell(id); RustboundMagic.SpellBuffStats[id] = new(); }
        RustboundMagic.SpellBuffStats[ResonanceId] = new() { ["genericmodifierstats"] = ResonanceSpell.Potency };
        RustboundMagic.SpellBuffStats[BrittleId] = new();
        RustboundMagic.SpellBuffStats[BurstId] = new() { ["walkspeed"] = BurstSpeedEffect.SpeedBonus };
        RustboundMagic.SpellBuffStats[RecoveryId] = new() { ["walkspeed"] = -BurstSpeedEffect.SlowAmount };
        upstream.CreateSubSpellLists();
    }

    public static void RegisterRecipes()
    {
        foreach(var d in FieldCatalog.All)RustboundMagic.OculusRecipes[d.Id]=new RecipeOculusGrimoireTier1Neutral(d.Id);
        RustboundMagic.OculusRecipes[HolyGrenadeSpell.SpellId]=new RecipeOculusGrimoireTier2Light(HolyGrenadeSpell.SpellId);
        foreach(var d in BulwarkCatalog.All)
            RustboundMagic.OculusRecipes[d.Id]=d.Tier switch
            {1=>new RecipeOculusGrimoireTier1Neutral(d.Id),2=>new RecipeOculusGrimoireTier2Neutral(d.Id),_=>new RecipeOculusGrimoireTier3Neutral(d.Id)};
        foreach(var d in BloodCatalog.All)
            RustboundMagic.OculusRecipes[d.Id]=(d.Tier,d.Essence) switch
            {
                (1,"light")=>new RecipeOculusGrimoireTier1Light(d.Id),
                (1,"dark")=>new RecipeOculusGrimoireTier1Dark(d.Id),
                (1,_)=>new RecipeOculusGrimoireTier1Neutral(d.Id),
                (2,"light")=>new RecipeOculusGrimoireTier2Light(d.Id),
                (2,"dark")=>new RecipeOculusGrimoireTier2Dark(d.Id),
                (2,_)=>new RecipeOculusGrimoireTier2Neutral(d.Id),
                (3,"light")=>new RecipeOculusGrimoireTier3Light(d.Id),
                (3,"dark")=>new RecipeOculusGrimoireTier3Dark(d.Id),
                _=>new RecipeOculusGrimoireTier3Neutral(d.Id)
            };
        foreach (var d in UtilityCatalog.All) RustboundMagic.OculusRecipes[d.Id] = new RecipeOculusGrimoireTier1Neutral(d.Id);
        foreach (var d in MemeCatalog.All) RustboundMagic.OculusRecipes[d.Id] = new RecipeOculusGrimoireTier1Neutral(d.Id);
        RustboundMagic.OculusRecipes[ResonanceId] = new RecipeOculusGrimoireTier2Light(ResonanceId);
        RustboundMagic.OculusRecipes[BrittleId] = new RecipeOculusGrimoireTier2Neutral(BrittleId);
        RustboundMagic.OculusRecipes[ArcId] = new RecipeOculusGrimoireTier2Neutral(ArcId);
        RustboundMagic.OculusRecipes[BurstId] = new RecipeOculusGrimoireTier2Light(BurstId);
        foreach (var d in BacklogCatalog.All)
            RustboundMagic.OculusRecipes[d.Id] = d.Essence switch
            {
                "light" => new RecipeOculusGrimoireTier2Light(d.Id),
                "dark" => new RecipeOculusGrimoireTier2Dark(d.Id),
                _ => new RecipeOculusGrimoireTier2Neutral(d.Id)
            };
        foreach (var d in NextCatalog.All)
            RustboundMagic.OculusRecipes[d.Id] = (d.Tier, d.Essence) switch
            {
                (1, "light") => new RecipeOculusGrimoireTier1Light(d.Id),
                (1, "dark") => new RecipeOculusGrimoireTier1Dark(d.Id),
                (1, _) => new RecipeOculusGrimoireTier1Neutral(d.Id),
                (_, "light") => new RecipeOculusGrimoireTier2Light(d.Id),
                (_, "dark") => new RecipeOculusGrimoireTier2Dark(d.Id),
                _ => new RecipeOculusGrimoireTier2Neutral(d.Id)
            };
    }

    private static void AfterSpellList(RustboundMagic __instance) => RegisterSpells(__instance);
    private static void AfterRecipes() => RegisterRecipes();
    private static void AfterReset(Entity __0)
    {
        ClearAddonEffects(__0);
        CreatureBuffPersistence.Clear(__0);
    }
    private static void AfterClearEffect(Entity __0, string __1)
    {
        if (__1 == BrittleId) __0.WatchedAttributes.RemoveAttribute(BrittleBonusKey);
        if (__1 == BurstId) BurstSpeedEffect.OnBurstRemoved(__0);
        ExpansionRuntime.For(__0.World)?.NextEffectRemoved(__0, __1);
        ExpansionRuntime.For(__0.World)?.MemeEffectRemoved(__0, __1);
        ExpansionRuntime.For(__0.World)?.BloodEffectRemoved(__0,__1);
        Bulwark.Removed(__0,__1);
        ExpansionRuntime.For(__0.World)?.FieldRemoved(__0,__1);
    }

    // The grant is fixed even if Resonance or equipment already boosts spell stats.
    private static void AfterModifiedStats(string __1, ref float __result)
    {
        if (__1 == ResonanceId) __result = ResonanceSpell.Potency;
        if (__1 == BurstId) __result = BurstSpeedEffect.SpeedBonus;
    }
    private static void AfterModifiedDuration(string __1, ref float __result)
    {
        var definition = BacklogCatalog.All.FirstOrDefault(d => d.Id == __1);
        if (definition?.Kind is "blizzard" or "starfall") __result = definition.Duration;
    }

    public static void ClearAddonEffects(Entity? entity)
    {
        if (entity?.World == null) return;
        Bulwark.Clear(entity);
        if(entity is BloodGoober blood && ExpansionRuntime.For(entity.World)?.Transients.Contains(entity)!=true)
        {blood.Settle(false);entity.DespawnReason=new EntityDespawnData{Reason=EnumDespawnReason.Removed};}
        if (entity is MagicProjectile or TravelGate or SummonedSentry && ExpansionRuntime.For(entity.World)?.Transients.Contains(entity) != true)
            entity.DespawnReason = new EntityDespawnData { Reason = EnumDespawnReason.Removed };
        ExpansionRuntime.For(entity.World)?.ClearTarget(entity);
        if (entity is NextProjectile && ExpansionRuntime.For(entity.World)?.Transients.Contains(entity) != true)
            entity.DespawnReason = new EntityDespawnData { Reason = EnumDespawnReason.Removed };
        if (entity is EntityPlayer player) { ChannelHooks.Stop(player); ExpansionRuntime.For(entity.World)?.ClearOwner(player); }
        ControlEffects.Clear(entity);
        BurstSpeedEffect.Clear(entity);
        foreach (var id in new[] { ResonanceId, BrittleId, HotHandsSpell.SpellId })
        {
            // Reset must also catch an expired state whose scheduled callback was cancelled.
            if (entity.WatchedAttributes.HasAttribute("effectactive-" + id))
                Effects.RemoveEffect(entity, id);
        }
        foreach(var d in BloodCatalog.All)if(entity.WatchedAttributes.HasAttribute("effectactive-"+d.Id))Effects.RemoveEffect(entity,d.Id);
        entity.Stats?.Remove("genericmodifierstats", "rustboundmagic-" + ResonanceId);
        entity.WatchedAttributes.RemoveAttribute(BrittleBonusKey);
    }

    // This runs only after Entity.ReceiveDamage has accepted the hit, before health's
    // armor/elemental mitigation delegates. Augmenting the original hit preserves
    // the exact source, element, damage type, attribution, tier, and knockback.
    public static void BeforeHealthDamage(EntityBehaviorHealth __instance, ref DamageSource __0, ref float __1)
    {
        var target = __instance.entity;
        if (target.World.Side != EnumAppSide.Server || !target.Alive) return;
        CreatureSupport.NormalizeHealing(target, ref __0);
        if (ControlEffects.Banished(target) || __0.CauseEntity is { } cause && ControlEffects.Banished(cause)) { __1 = 0; return; }
        if (ExpansionRuntime.ReactiveDepth > 0)
        {
            if (ExpansionRuntime.TransferDepth == 0 && BurstSpeedEffect.Amplifies(target, __0, __1)) __1 *= BurstSpeedEffect.DamageMultiplier;
            return;
        }
        if (DamageRules.IsDirectHit(__0, __1, target) && Effects.IsEffectActive(target, BrittleId))
        {
            var bonus = target.WatchedAttributes.GetFloat(BrittleBonusKey);
            Effects.RemoveEffect(target, BrittleId); // Consume before damage or callbacks can re-enter.
            if (float.IsFinite(bonus) && bonus > 0)
            {
                __1 += bonus;
                PopBrittleSigil(target);
            }
        }
        if (BurstSpeedEffect.Amplifies(target, __0, __1)) __1 *= BurstSpeedEffect.DamageMultiplier;
        CreatureSupport.BeforeDamage(target, __0, ref __1);
    }

    private static bool BeforeMitigation() => ExpansionRuntime.TransferDepth == 0;
    private static void AfterMitigation(EntityBehaviorHealth __instance, DamageSource __0, ref float __1)
    {
        if (__instance.entity.World.Side == EnumAppSide.Server)
        {
            Bulwark.Reduce(__instance.entity,__0,ref __1);
            ExpansionRuntime.For(__instance.entity.World)?.AbsorbBloodDamage(__instance.entity,__0,ref __1);
            if (ExpansionRuntime.TransferDepth == 0) CreatureSupport.AfterDamageDelegates(__instance.entity, __0, ref __1);
            ExpansionRuntime.For(__instance.entity.World)?.PartyMitigation(__instance.entity, __0, ref __1);
            ExpansionRuntime.For(__instance.entity.World)?.AfterMitigation(__instance.entity, __0, ref __1);
        }
    }

    private static void PopBrittleSigil(Entity target)
    {
        // Server-only, at actual consumption. Generic removal also covers
        // expiry/reset, so it must never be used to infer a triggering hit.
        // A native particle packet reaches nearby observers even on a lethal hit.
        try
        {
            float height = Math.Clamp(target.SelectionBox?.YSize ?? 1.8f, 0.3f, 8f);
            var center = target.Pos.XYZ.AddCopy(0, height * 0.6, 0);
            target.World.SpawnParticles(new SimpleParticleProperties(28, 36,
                ColorUtil.ToRgba(255, 235, 190, 255),
                center.AddCopy(-0.12, -0.12, -0.12), center.AddCopy(0.12, 0.12, 0.12),
                new Vec3f(-3f, -1.5f, -3f), new Vec3f(3f, 3f, 3f),
                0.4f, 0, 0.9f, 1.4f, EnumParticleModel.Quad)
            {
                WithTerrainCollision = false,
                LightEmission = ColorUtil.ToRgba(255, 185, 90, 255),
                OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -2.5f)
            });
        }
        catch (Exception error)
        {
            // Cosmetic failure must not cancel the original health handler.
            target.World.Logger?.Warning("[RustboundExpansion] Brittle Sigil pop failed: {0}", error.Message);
        }
    }

    public override void Dispose()
    {
        visuals?.Dispose();
        if (server != null)
        {
            server.Event.OnEntityLoaded -= ClearAddonEffects;
            server.Event.OnEntityLoaded -= CreatureBuffPersistence.Restore;
            server.Event.OnEntityDeath -= OnDeath;
            server.Event.PlayerNowPlaying -= OnPlayer;
            server.Event.PlayerDisconnect -= OnPlayer;
        }
        if (harmony != null && --patchUsers == 0)
        {
            ChannelHooks.ClearAll();
            ChargeHooks.ClearAll();
            harmony.UnpatchAll("franc.rustboundexpansion");
            sharedHarmony = null;
        }
        harmony = null;
    }
}
