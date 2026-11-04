using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Toolsmith;
using Toolsmith.Client;
using Toolsmith.Config;
using Toolsmith.ToolTinkering;
using Toolsmith.ToolTinkering.Behaviors;
using Toolsmith.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace AnimalicaToolsmith;

public sealed class IntegrationSystem : ModSystem
{
    public const string Domain = "animalicatoolsmith";
    public const string Marker = "animalicatoolsmith:assembled";
    private const string HeadRoute = "animalicaToolsmithOutput";
    private const string HarmonyId = "craditz.animalicatoolsmith";
    private static readonly ConditionalWeakTable<IWorldAccessor, Routes> Worlds = new();
    private Harmony? harmony;
    internal AnimalPresentation? Presentation;
    internal WornPresentation? Worn;
    internal static IntegrationSystem? Client;
    [ThreadStatic] internal static IPlayer? BenchPlayer;
    public override double ExecuteOrder() => 1.1;
    public override void StartClientSide(ICoreClientAPI api)
    {
        Client=this;
        Presentation = new AnimalPresentation(api);
        Worn = new WornPresentation(api);Worn.Start(harmony!);
    }

    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass(Domain + ".fitting", typeof(ItemAnimalFitting));
        api.RegisterItemClass(Domain + ".bundle", typeof(ItemAnimalBundle));
        api.RegisterCollectibleBehaviorClass(Domain + ".fittingrenderer", typeof(FittingRenderer));
        api.RegisterCollectibleBehaviorClass(Domain + ".animaltool", typeof(AnimalToolBehavior));
        api.RegisterCollectibleBehaviorClass(Domain + ".legacyrefit", typeof(LegacyRefitBehavior));
        harmony = new Harmony(HarmonyId);
        StationDismantling.Start(harmony);
        Patch(typeof(TinkeringUtility), "IsValidHandle", nameof(ValidHandle));
        Patch(typeof(TinkeringUtility), "ValidHandleInOffhand", nameof(ValidAnimalPair));
        harmony.Patch(AccessTools.Method(typeof(CollectibleObject), "GetBehavior", new[] { typeof(CollectibleBehavior[]), typeof(Type), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(IntegrationSystem), nameof(RecognizeAnimalTool)));
        Patch(typeof(TinkeringUtility), "AssemblePartBundle", nameof(PairHead));
        Patch(typeof(TinkeringUtility), "TryCraftToolFromSlots", nameof(BenchAssembly));
        Patch(typeof(TinkeringUtility), "HandleBrokenTinkeredTool", nameof(NoEquipmentRefill));
        Patch(typeof(TinkeringUtility), "DisassembleTool", nameof(LegacyDismantle));
        Patch(typeof(TinkeringUtility), "ToolOrHeadNeedsSharpening", nameof(LegacySharpen));
        Patch(typeof(TinkeringUtility), "TryWhetstoneSharpening", nameof(LegacyWhetstone));
        Patch(typeof(Toolsmith.ToolTinkering.Blocks.BlockEntityWorkbench), "AttemptToCraft", nameof(BenchActor), finalizer: nameof(BenchActorEnd));
        Patch(typeof(GridRecipe), "Matches", nameof(RecipeStart), nameof(RecipeEnd), nameof(RecipeFinally));
        Type resolver = AccessTools.TypeByName("AnimalicaBodyTools.Systems.WearableToolResolver")
            ?? throw new InvalidOperationException("Body Tools wearable resolver is unavailable.");
        Patch(resolver, "SyncVirtualToolStateToSource", nameof(SyncVirtual));
        Type cache = resolver.GetNestedType("EquippedToolCache", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Body Tools equipped cache contract changed.");
        Patch(cache, "EnsureCurrent", postfix: nameof(RefreshScoring));
        Type targeted = AccessTools.TypeByName("AnimalicaBodyTools.HarmonyPatches.WearableTargetedActionPatches")
            ?? throw new InvalidOperationException("Body Tools source-damage contract is unavailable.");
        Patch(targeted, "TryDamageSourceTool", nameof(SingleBlockWear));
    }

    private void Patch(Type type, string method, string? prefix = null, string? postfix = null, string? finalizer = null)
    {
        MethodInfo original = AccessTools.Method(type, method)
            ?? throw new InvalidOperationException($"Required integration method missing: {type.FullName}.{method}");
        HarmonyMethod? Hook(string? name) => name == null ? null : new HarmonyMethod(typeof(IntegrationSystem), name)
        { priority = name == nameof(RecipeStart) ? Priority.First : name is nameof(RecipeEnd) or nameof(RecipeFinally) ? Priority.Last : Priority.Normal };
        harmony!.Patch(original, Hook(prefix), Hook(postfix), finalizer: Hook(finalizer));
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        var routes = new Routes();
        Worlds.Remove(api.World);
        Worlds.Add(api.World, routes);
        foreach (CollectibleObject collectible in api.World.Collectibles)
        {
            if (IsFitting(collectible))
            {
                if (!collectible.HasBehavior<FittingRenderer>()) collectible.CollectibleBehaviors = collectible.CollectibleBehaviors.Append(new FittingRenderer(collectible)).ToArray();
            }
            if (IsTool(collectible))
            {
                if (!collectible.HasBehavior<AnimalToolBehavior>()) collectible.CollectibleBehaviors = collectible.CollectibleBehaviors.Append(new AnimalToolBehavior(collectible)).ToArray();
                if (IsWearable(collectible)) PrioritizeRefit(collectible);
            }
        }
        int disabled = 0;
        foreach (GridRecipe recipe in api.World.GridRecipes)
        {
            // Client recipes carry only ResolvedIngredients. The source dictionary
            // can also be discarded on the server after recipe resolution.
            IEnumerable<CraftingRecipeIngredient> ingredients = recipe.ResolvedIngredients?.OfType<CraftingRecipeIngredient>()
                ?? recipe.Ingredients?.Values.AsEnumerable() ?? Enumerable.Empty<CraftingRecipeIngredient>();
            CollectibleObject? output = recipe.Output?.ResolvedItemStack?.Collectible;
            if (IsTool(output))
            {
                if (IsWearable(output))
                    foreach (var ingredient in ingredients)
                    {
                        ItemStack? head = ingredient.ResolvedItemStack;
                        if (head != null && TinkeringUtility.IsValidHead(head))
                        {
                            string key = head.Collectible.Code.ToString();
                            if (routes.Outputs.TryGetValue(key, out Item? prior) && prior.Code != output!.Code)
                                throw new InvalidOperationException($"Ambiguous animal output for {key}: {prior.Code} / {output!.Code}");
                            routes.Outputs[key] = (Item)output!;
                            routes.LegacyHeads.TryAdd(output!.Code.ToString(), head.Clone());
                        }
                    }
                recipe.Enabled = false;
                routes.Suppressed.Add(recipe);
                disabled++;
            }
            // The base mod's simple ordinary-tool recovery must not bypass Toolsmith component recovery.
            if (recipe.Attributes?["animalicaRecoverHead"].AsBool(false) == true &&
                !ingredients.Any(i => i.Code?.Path.Contains("falx", StringComparison.Ordinal) == true))
            {
                recipe.Enabled = false;
                routes.Suppressed.Add(recipe);
                disabled++;
            }
        }
        // Shovel finds can be used for the existing digging/cutting claw family.
        api.World.GridRecipes.RemoveAll(routes.Suppressed.Contains);
        foreach (Item item in api.World.Items.Where(i => i?.Code != null && i.Code.Path.StartsWith("shovelhead-", StringComparison.Ordinal)))
        {
            string knife = item.Code.Domain + ":knifeblade-" + item.Code.Path[11..];
            if (routes.Outputs.TryGetValue(knife, out Item? claws)) routes.Outputs.TryAdd(item.Code.ToString(), claws);
        }
        // Bypass recipes are removed on the server before clients receive them.
        // Item attributes travel in native item packets, so publish head routes
        // there instead of relying on the removed recipes on the client.
        if (api.Side == EnumAppSide.Server)
        {
            foreach (var route in routes.Outputs)
            {
                Item head = api.World.GetItem(new AssetLocation(route.Key))
                    ?? throw new InvalidOperationException($"Supported head disappeared: {route.Key}");
                head.Attributes ??= new JsonObject(new JObject());
                ((JObject)head.Attributes.Token!)[HeadRoute] = route.Value.Code.ToString();
            }
        }
        else
        {
            foreach (Item head in api.World.Items.Where(i => i?.Code != null))
            {
                string? code = head.Attributes?[HeadRoute].AsString();
                if (code != null && api.World.GetItem(new AssetLocation(code)) is Item output && IsTool(output) && IsWearable(output))
                    routes.Outputs[head.Code.ToString()] = output;
            }
        }
        api.Logger.Notification("[animalicatoolsmith] Registered {0} head routes; disabled {1} bypass recipes. Weapons and natural tools excluded.", routes.Outputs.Count, disabled);
        if (api.Side == EnumAppSide.Server && routes.Outputs.Count == 0)
            throw new InvalidOperationException("No supported head routes found; refusing a silently nonfunctional integration.");
    }

    public override void Dispose()
    {
        // GPU resources are released by client collectible OnUnloaded, while
        // the render context still exists, rather than late mod teardown.
        Presentation = null;
        Worn?.Dispose();Worn=null;
        if(Client==this)Client=null;
        harmony?.UnpatchAll(HarmonyId);
    }
    internal sealed class Routes
    {
        public Dictionary<string, Item> Outputs { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, ItemStack> LegacyHeads { get; } = new(StringComparer.Ordinal);
        public HashSet<GridRecipe> Suppressed { get; } = new();
    }
    internal static Routes For(IWorldAccessor world) => Worlds.GetValue(world, _ => new Routes());
    public static bool IsFitting(CollectibleObject? item) => item?.Attributes?["animalicaToolsmithFitting"].AsBool(false) == true;
    public static bool Managed(ItemStack? stack) => stack?.Attributes.GetInt(Marker) == 1;
    public static bool IsWearable(CollectibleObject? item) => item?.Attributes?["animalicaWearableBodyTool"].AsBool(false) == true;
    public static bool IsTool(CollectibleObject? item) => item?.Code?.Domain == "animalicabodytools" &&
        new[] { "clawtips-", "rendingclaws-", "tunnelingclaws-", "prospectingclaws-", "harvestingclaws-", "cultivatingclaws-" }.Any(p => item.Code.Path.StartsWith(p, StringComparison.Ordinal));

    internal static readonly MethodInfo Holder = AccessTools.Method(AccessTools.TypeByName("AnimalicaBodyTools.Util.AnimalBodyToolTraitAccessUtil"), "HolderHasTraitFast")!;
    internal static readonly MethodInfo RestrictCraft = AccessTools.PropertyGetter(AccessTools.TypeByName("AnimalicaBodyTools.Config.AnimalicaBodyToolsPolicy"), "RestrictAnimalToolCrafting")!;
    internal static bool Allowed(EntityAgent actor) => !(bool)RestrictCraft.Invoke(null, null)! || (bool)Holder.Invoke(null, new object[] { actor })!;
    internal static bool Route(ItemStack? head, ItemStack? fitting, IWorldAccessor world, out Item output)
    {
        output = null!;
        return head != null && fitting != null && IsFitting(fitting.Collectible) && TinkeringUtility.IsValidHead(head) && For(world).Outputs.TryGetValue(head.Collectible.Code.ToString(), out output!);
    }
    internal static void Give(IWorldAccessor world, EntityAgent actor, ItemStack stack)
    {
        if (!actor.TryGiveItemStack(stack)) world.SpawnItemEntity(stack, actor.Pos.XYZ);
    }
    internal static ItemStack Bundle(ItemStack head, ItemStack fitting, IWorldAccessor world)
    {
        var bundle = new ItemStack(world.GetItem(new AssetLocation(Domain, "partsbundle")));
        bundle.SetToolhead(head);
        bundle.SetToolhandle(fitting);
        return bundle;
    }

    internal static void PrioritizeRefit(CollectibleObject item)
    {
        var behavior=item.GetBehavior<LegacyRefitBehavior>() ?? new LegacyRefitBehavior(item);
        item.CollectibleBehaviors=new CollectibleBehavior[]{behavior}.Concat(item.CollectibleBehaviors.Where(b=>b is not LegacyRefitBehavior)).ToArray();
    }
    private static bool ValidHandle(ItemStack? stack, ref bool __result)
    {
        if (!IsFitting(stack?.Collectible)) return true;
        __result = !stack!.HasWetTreatment();
        return false;
    }
    private static bool ValidAnimalPair(EntityAgent byEntity, ref bool __result)
    {
        ItemStack? fitting = byEntity.LeftHandItemSlot?.Itemstack;
        if (!IsFitting(fitting?.Collectible)) return true;
        __result = TinkeringUtility.IsValidHandle(fitting!) && Allowed(byEntity)
            && Route(byEntity.RightHandItemSlot?.Itemstack, fitting, byEntity.World, out _);
        return false;
    }
    private static bool RecognizeAnimalTool(CollectibleBehavior[] fromList, Type type, ref CollectibleBehavior? __result)
    {
        if (type != typeof(CollectibleBehaviorTinkeredTools)) return true;
        AnimalToolBehavior? behavior = fromList.OfType<AnimalToolBehavior>().FirstOrDefault();
        if (behavior == null) return true;
        __result = behavior; return false;
    }
    internal static void AssemblySound(EntityAgent actor)
    {
        if (actor.World.Side == EnumAppSide.Server)
            actor.World.PlaySoundAt(new AssetLocation("sounds/player/messycraft.ogg"), actor.Pos.X, actor.Pos.Y, actor.Pos.Z, null, true, 32, 1);
    }
    private static bool PairHead(ItemSlot slot, EntityAgent byEntity, BlockSelection? blockSel)
    {
        ItemSlot offhand = byEntity.LeftHandItemSlot;
        if (!IsFitting(offhand.Itemstack?.Collectible)) return true;
        if (byEntity.World.Side != EnumAppSide.Server || slot.Empty || !Allowed(byEntity) ||
            !Route(slot.Itemstack, offhand.Itemstack, byEntity.World, out _)) return false;
        ItemStack bundle = Bundle(slot.Itemstack!, offhand.Itemstack!, byEntity.World);
        slot.TakeOut(1);
        offhand.TakeOut(1);
        if (slot.Empty) slot.Itemstack = bundle; else Give(byEntity.World, byEntity, bundle);
        slot.MarkDirty(); offhand.MarkDirty();
        return false;
    }
    private static void BenchActor(IPlayer byPlayer, out IPlayer? __state) { __state = BenchPlayer; BenchPlayer = byPlayer; }
    private static Exception? BenchActorEnd(IPlayer? __state, Exception? __exception) { BenchPlayer = __state; return __exception; }
    private static bool BenchAssembly(ItemSlot[] slots, IWorldAccessor world, ref ItemStack? __result)
    {
        if (!slots.Any(s => IsFitting(s.Itemstack?.Collectible))) return true;
        __result = null;
        if (world.Side != EnumAppSide.Server || BenchPlayer?.Entity == null || !Allowed(BenchPlayer.Entity)) return false;
        ItemSlot[] fittings = slots.Where(s => IsFitting(s.Itemstack?.Collectible)).ToArray();
        ItemSlot[] heads = slots.Where(s => s.Itemstack != null && TinkeringUtility.IsValidHead(s.Itemstack)).ToArray();
        if (slots.Length is < 2 or > 3 || fittings.Length != 1 || heads.Length != 1) return false;
        ItemSlot? binding = slots.FirstOrDefault(s => s != fittings[0] && s != heads[0]);
        if (!BindingUsable(binding)) return false;
        __result = Assemble(heads[0].Itemstack, fittings[0].Itemstack, binding, world);
        if (__result != null) { heads[0].TakeOut(1); fittings[0].TakeOut(1); ConsumeBinding(binding); foreach (var s in slots) s.MarkDirty(); }
        return false;
    }
    private static void NoEquipmentRefill(ItemSlot itemslot, ref bool refillSlot)
    {
        if (Managed(itemslot.Itemstack)) refillSlot = false;
    }
    private static bool LegacyDismantle(IPlayer byPlayer)
        => !IsTool(byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack?.Collectible) || Managed(byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack);
    private static bool LegacySharpen(ItemStack item, ref bool __result)
    {
        if (IsTool(item.Collectible) && !Managed(item)) { __result = false; return false; }
        return true;
    }
    private static bool LegacyWhetstone(ItemSlot slot, ref bool __result)
    {
        if (IsTool(slot.Itemstack?.Collectible) && !Managed(slot.Itemstack)) { __result = false; return false; }
        return true;
    }
    private static void RecipeStart(GridRecipe __instance, out string? __state)
    {
        __state = null;
        if (__instance.Output?.Code?.Domain != Domain) return;
        __state = __instance.RequiresTrait;
        __instance.RequiresTrait = null!;
    }
    private static void RecipeEnd(GridRecipe __instance, IPlayer forPlayer, ref bool __result, string? __state)
    {
        if (__instance.Output?.Code?.Domain != Domain) return;
        __instance.RequiresTrait = __state!;
        if (__result && (forPlayer?.Entity == null || !Allowed(forPlayer.Entity))) __result = false;
    }
    private static Exception? RecipeFinally(GridRecipe __instance, string? __state, Exception? __exception)
    {
        if (__instance.Output?.Code?.Domain == Domain) __instance.RequiresTrait = __state!;
        return __exception;
    }
    private static bool SyncVirtual(ItemStack virtualStack, ItemSlot sourceSlot, ref bool __result)
    {
        if (!Managed(sourceSlot.Itemstack)) return true;
        // Stateful action modes belong to the real wearable; condition never flows back from a transient stack.
        foreach(string key in new[]{"toolMode","probePositions"})
            if (virtualStack.Attributes.HasAttribute(key)) sourceSlot.Itemstack!.Attributes[key] = virtualStack.Attributes[key].Clone();
        sourceSlot.MarkDirty(); __result = true; return false;
    }
    private static void RefreshScoring(object __instance)
    {
        if (AccessTools.Property(__instance.GetType(), "Candidates").GetValue(__instance) is not IEnumerable candidates) return;
        foreach (object candidate in candidates)
        {
            Type type = candidate.GetType();
            var slot = (ItemSlot)AccessTools.Property(type, "SourceSlot").GetValue(candidate)!;
            if (!Managed(slot.Itemstack)) continue;
            var scoring = (ItemStack)AccessTools.Property(type, "ScoringStack").GetValue(candidate)!;
            scoring.Attributes = slot.Itemstack!.Attributes.Clone();
        }
    }

    private static readonly Type BreakContextType = AccessTools.TypeByName("AnimalicaBodyTools.HarmonyPatches.WearableBlockBreakToolContext")!;
    private static readonly PropertyInfo CurrentBreak = AccessTools.Property(BreakContextType, "Current");
    private static readonly Type BreakStateType = CurrentBreak.PropertyType;
    private static readonly PropertyInfo BreakCharged = AccessTools.Property(BreakStateType, "DurabilityCharged");
    private static readonly PropertyInfo BreakPlayer = AccessTools.Property(BreakStateType, "Player");
    private static readonly PropertyInfo BreakBlock = AccessTools.Property(BreakStateType, "Block");
    private static readonly PropertyInfo BodyDiagnostics = AccessTools.Property(RestrictCraft.DeclaringType!, "DiagnosticLogging");
    private static bool SingleBlockWear(EntityAgent entity, ItemSlot sourceSlot, string action, ref bool __result)
    {
        if (!Managed(sourceSlot.Itemstack) || !action.StartsWith("block-break:", StringComparison.Ordinal)) return true;
        object? context = CurrentBreak.GetValue(null);
        if (context == null || !(bool)BreakCharged.GetValue(context)!
            || ((IPlayer)BreakPlayer.GetValue(context)!).Entity != entity) return true;
        var block = (Block)BreakBlock.GetValue(context)!;
        if (block.Code == null || !action.StartsWith("block-break:" + block.Code.ToShortString() + ":", StringComparison.Ordinal)) return true;
        // The dedicated native bridge already charged this actor/block action.
        // Use its lifetime-scoped marker; a new break at the same position is independent.
        __result = false;
        if ((bool)BodyDiagnostics.GetValue(null)!)
            entity.World.Logger.VerboseDebug("[animalicatoolsmith] Skipped duplicate generic wear after native bridge: action={0}, source={1}.", action, sourceSlot.Itemstack!.Collectible.Code);
        return false;
    }

    public static FittingStats Stats(ItemStack? fitting)
    {
        ArgumentNullException.ThrowIfNull(fitting);
        var defs = fitting.Collectible.Attributes;
        return FittingStats.Compose(ToolsmithModSystem.Stats.BaseHandleStats[defs["seatProfile"].AsString()!],
            ToolsmithModSystem.Stats.BaseHandleStats[defs["braceProfile"].AsString()!],
            ToolsmithModSystem.Stats.GripStats[defs["liningProfile"].AsString()!]);
    }
    internal static bool BindingUsable(ItemSlot? binding) => binding == null || binding.Empty || TinkeringUtility.IsValidBinding(binding.Itemstack);
    private static ItemStack? BindingStack(ItemSlot? slot)
    {
        if (slot == null || slot.Empty) return null;
        return slot.Itemstack.Block is BlockLiquidContainerBase liquid ? liquid.GetContent(slot.Itemstack) : slot.Itemstack;
    }
    internal static void ConsumeBinding(ItemSlot? slot)
    {
        if (slot == null || slot.Empty) return;
        if (slot.Itemstack.Block is BlockLiquidContainerBase liquid)
        {
            ItemStack content = liquid.GetContent(slot.Itemstack) ?? throw new InvalidOperationException("Binding container became empty.");
            var part = ToolsmithModSystem.Stats.BindingParts[content.Collectible.Code.Path];
            liquid.TryTakeLiquid(slot.Itemstack, part.litersUsed);
        }
        else slot.TakeOut(1);
        slot.MarkDirty();
    }

    internal static double PartFraction(ItemStack part)
    {
        int maximum = part.Attributes.GetInt("toolPartMaxDurability", 0);
        return maximum > 0 ? Math.Clamp((double)part.Attributes.GetInt("toolPartCurrentDurability", maximum) / maximum, 0, 1) : 1;
    }
    public static ItemStack? Assemble(ItemStack? inputHead, ItemStack? inputFitting, ItemSlot? bindingSlot, IWorldAccessor world)
    {
        if (!Route(inputHead, inputFitting, world, out Item item) || !BindingUsable(bindingSlot)) return null;
        ItemStack head = inputHead!.Clone(), fitting = inputFitting!.Clone();
        head.StackSize = fitting.StackSize = 1;
        double condition = PartFraction(head);
        var recovered = head.Attributes.GetTreeAttribute("animalicaRecoveredHeadWear");
        if (recovered != null && recovered.GetInt("denominator") > 0)
            condition = Math.Min(condition, (double)recovered.GetInt("numerator") / recovered.GetInt("denominator"));
        if (condition <= 0 || PartFraction(fitting) <= 0) return null;
        var output = new ItemStack(item) { Attributes = head.Attributes.Clone() };
        output.Attributes.RemoveAttribute("animalicaRecoveredHeadWear");
        output.Attributes.RemoveAttribute("toolPartCurrentDurability");
        output.Attributes.RemoveAttribute("toolPartMaxDurability");
        output.Attributes.SetInt(Marker, 1);
        ApplyQuenchBuffsOnce(output, head);
        int maximum = output.Collectible.GetMaxDurability(output);
        int toolBase = Math.Max(1, (int)(maximum / ToolsmithModSystem.Config.HeadDurabilityMult));
        int sharpMaximum = Math.Max(1, (int)(toolBase * ToolsmithModSystem.Config.SharpnessMult));
        var smith = head.Attributes.GetTreeAttribute("scientificsmithy:smithingStats");
        if (smith != null) sharpMaximum = Math.Max(1, (int)(smith.GetFloat("hardnessMult", (float)ToolsmithModSystem.Config.SharpnessMult) * smith.GetInt("halfToughness", toolBase)));
        int oldSharpMax = head.Attributes.GetInt("toolSharpnessMax", 0);
        double sharp = oldSharpMax > 0 ? (double)head.Attributes.GetInt("toolSharpnessCurrent", oldSharpMax) / oldSharpMax
            : (head.Collectible.IsCraftableMetal() ? 0.85 : 0.66);
        FittingStats stats = Stats(fitting);
        ItemStack? binding = BindingStack(bindingSlot)?.Clone();
        if (binding != null) binding.StackSize = 1;
        string bindingTag = binding == null ? "none" : ToolsmithModSystem.Stats.BindingParts[binding.Collectible.Code.Path].bindingStatTag;
        BindingStatDefines bindingStats = ToolsmithModSystem.Stats.BindingStats[bindingTag];
        float treatment = fitting.HasHandleTreatmentTag() ? ToolsmithModSystem.Stats.TreatmentStats[fitting.GetHandleTreatmentTag()].handleHPbonus : 0;
        int fitMaximum = stats.FittingLife(toolBase, treatment, bindingStats);
        int fitRemaining = FittingStats.Rebase(fitMaximum, PartFraction(fitting));
        head.SetPartMaxDurability(maximum); head.SetPartCurrentDurability(FittingStats.Rebase(maximum, condition));
        head.SetPartMaxSharpness(sharpMaximum); head.SetPartCurrentSharpness(FittingStats.Rebase(sharpMaximum, sharp));
        head.Attributes.RemoveAttribute("animalicaRecoveredHeadWear");
        fitting.SetPartMaxDurability(fitMaximum); fitting.SetPartCurrentDurability(fitRemaining);
        fitting.SetHandleGripTag(fitting.Collectible.Attributes["liningProfile"].AsString()!);
        output.SetToolhead(head); output.SetToolhandle(fitting);
        output.SetToolheadCurrentDurability(head.GetPartCurrentDurability());
        output.SetToolCurrentSharpness(head.GetPartCurrentSharpness()); output.SetToolMaxSharpness(sharpMaximum);
        output.SetToolhandleCurrentDurability(fitRemaining); output.SetToolhandleMaxDurability(fitMaximum);
        output.SetToolbindingMaxDurability(stats.BindingLife(toolBase, bindingStats));
        output.SetToolbindingCurrentDurability(output.GetToolbindingMaxDurability());
        if (binding != null) output.SetToolbinding(binding);
        output.SetSpeedBonus(stats.Speed); output.SetGripChanceToDamage(stats.WearChance);
        return output;
    }

    internal static void ApplyQuenchBuffsOnce(ItemStack output, ItemStack head)
    {
        var behavior = output.Collectible.GetBehavior<CollectibleBehaviorBuffable>();
        if (behavior == null) return;
        void Apply(string stat, float bonus)
        {
            if (bonus <= 0 || behavior.GetItemBuffs(output).Any(b => b.Code == "hardened" && b.StatCode == stat)) return;
            behavior.AddBuff(output, new AppliedCollectibleBuff { Code = "hardened", StatCode = stat, Multiplier = 1 + bonus });
        }
        Apply("attackpower", head.Attributes.GetFloat("powervalue"));
        Apply("miningspeed", head.Attributes.GetFloat("powervalue"));
        Apply("maxdurability", head.Attributes.GetFloat("durationbonus"));
    }

    internal static ItemStack? RecoverLegacy(ItemStack source, IWorldAccessor world)
    {
        if (Managed(source) || !For(world).LegacyHeads.TryGetValue(source.Collectible.Code.ToString(), out ItemStack? template)) return null;
        int maximum = source.Collectible.GetMaxDurability(source);
        if (maximum <= 0) return null;
        double fraction = Math.Clamp((double)source.Collectible.GetRemainingDurability(source) / maximum, 0, 1);
        if (fraction <= 0) return null;
        var head = template.Clone(); head.Attributes = source.Attributes.Clone();
        head.Attributes.RemoveAttribute("durability"); head.Attributes.RemoveAttribute("animalicaRecoveredHeadWear");
        head.SetPartMaxDurability(1000000); head.SetPartCurrentDurability(FittingStats.Rebase(1000000, fraction));
        head.SetPartMaxSharpness(1000000); head.SetPartCurrentSharpness(FittingStats.Rebase(850000, fraction));
        return head;
    }
}

public sealed class AnimalToolBehavior(CollectibleObject item) : CollectibleBehaviorTinkeredTools(item)
{
    public override void OnUnloaded(ICoreAPI api)
    {
        if (api is ICoreClientAPI client) (IntegrationSystem.Client ?? client.ModLoader?.GetModSystem<IntegrationSystem>())?.Presentation?.Dispose();
        base.OnUnloaded(api);
    }
    public override void OnBeforeRender(ICoreClientAPI api, ItemStack stack, EnumItemRenderTarget target, ref ItemRenderInfo info)
        => (IntegrationSystem.Client ?? api.ModLoader?.GetModSystem<IntegrationSystem>())?.Presentation?.Apply(stack,target,ref info);
    public override void GetHeldItemInfo(ItemSlot slot, StringBuilder text, IWorldAccessor world, bool debug)
    {
        ItemStack? stack = slot.Itemstack;
        if (stack == null || !IntegrationSystem.Managed(stack)) return;
        base.GetHeldItemInfo(slot, text, world, debug);
        text.AppendLine("Animal fitting: " + stack.GetToolhandle().GetName());
        ItemStack? binding = stack.GetToolbinding();
        text.AppendLine("Binding: " + (binding?.GetName() ?? "None"));
        text.AppendLine($"Fitting speed bonus: +{stack.GetSpeedBonus():P0}; fitting wear chance: {stack.GetGripChanceToDamage():P0}.");
    }
    public override void OnCreatedByCrafting(ItemSlot[] inputs, ItemSlot output, IRecipeBase recipe, ref EnumHandling handling) { }
    public override int GetMaxDurability(ItemStack stack, int durability, ref EnumHandling handling)
        => IntegrationSystem.Managed(stack) ? base.GetMaxDurability(stack, durability, ref handling) : durability;
    public override float GetMiningSpeed(ItemStack stack, BlockSelection selection, Block block, IPlayer player, ref EnumHandling handling)
        => IntegrationSystem.Managed(stack) ? base.GetMiningSpeed(stack, selection, block, player, ref handling) : 1;
    public override void OnDamageItem(IWorldAccessor world, Entity actor, ItemSlot slot, ref int amount, ref EnumHandling handling)
    {
        if (!IntegrationSystem.Managed(slot.Itemstack)) return;
        if (!IntegrationSystem.IsWearable(slot.Itemstack!.Collectible)) { handling = EnumHandling.PreventDefault; return; }
        base.OnDamageItem(world, actor, slot, ref amount, ref handling);
    }
}

public sealed class FittingRenderer(CollectibleObject item) : CollectibleBehavior(item), IModularPartRenderer
{
    public ITreeAttribute InitializeRenderTree(ITreeAttribute tree, Item item) => tree;
    public void ResetRotationAndOffset(ItemStack stack) { }
}

public sealed class ItemAnimalFitting : Item
{
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if (inSlot.Empty) return;
        if (ToolsmithModSystem.Stats == null) return;
        var stats = IntegrationSystem.Stats(inSlot.Itemstack);
        dsc.AppendLine($"Fitting durability: ×{stats.Base * (1 + stats.Self):0.##}; binding support: +{stats.Support:P0}; speed: +{stats.Speed:P0}");
        dsc.AppendLine($"Fitting wear chance: {stats.WearChance:P0} when wear applies.");
        if (inSlot.Itemstack!.HasPartMaxDurability()) dsc.AppendLine($"Condition: {inSlot.Itemstack.GetPartCurrentDurability()} / {inSlot.Itemstack.GetPartMaxDurability()}");
    }
}

public sealed class ItemAnimalBundle : Item
{
    public override void OnUnloaded(ICoreAPI api)
    {
        if (api is ICoreClientAPI client) (IntegrationSystem.Client ?? client.ModLoader?.GetModSystem<IntegrationSystem>())?.Presentation?.Dispose();
        base.OnUnloaded(api);
    }
    public override void OnBeforeRender(ICoreClientAPI api, ItemStack stack, EnumItemRenderTarget target, ref ItemRenderInfo info)
    {
        base.OnBeforeRender(api,stack,target,ref info);
        (IntegrationSystem.Client ?? api.ModLoader?.GetModSystem<IntegrationSystem>())?.Presentation?.Apply(stack,target,ref info);
    }
    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent actor, BlockSelection selection, EntitySelection entity, bool first, ref EnumHandHandling handling)
    {
        if (!first || slot.Empty || !IntegrationSystem.Allowed(actor) ||
            !actor.Controls.ShiftKey && !TinkeringUtility.ValidBindingInOffhand(actor)) return;
        handling = EnumHandHandling.PreventDefault;
        slot.Itemstack!.TempAttributes.SetBool("animalicatoolsmith:pairing", true);
        if (!actor.Controls.ShiftKey) IntegrationSystem.AssemblySound(actor);
        actor.StartAnimation("craftingwinding");
    }
    public override bool OnHeldInteractStep(float seconds, ItemSlot slot, EntityAgent actor, BlockSelection selection, EntitySelection entity)
        => !slot.Empty && slot.Itemstack.TempAttributes.GetBool("animalicatoolsmith:pairing") && seconds < 2.5f;
    public override void OnHeldInteractStop(float seconds, ItemSlot slot, EntityAgent actor, BlockSelection selection, EntitySelection entity)
    {
        actor.StopAnimation("craftingwinding");
        if (slot.Empty || !slot.Itemstack.TempAttributes.GetBool("animalicatoolsmith:pairing")) return;
        slot.Itemstack.TempAttributes.RemoveAttribute("animalicatoolsmith:pairing");
        if (seconds < 2.4f || actor.World.Side != EnumAppSide.Server || !IntegrationSystem.Allowed(actor)) return;
        ItemStack head = slot.Itemstack.GetToolhead(), fitting = slot.Itemstack.GetToolhandle();
        if (actor.Controls.ShiftKey)
        {
            slot.TakeOut(1); slot.MarkDirty();
            IntegrationSystem.Give(actor.World, actor, head); IntegrationSystem.Give(actor.World, actor, fitting); return;
        }
        ItemSlot binding = actor.LeftHandItemSlot;
        ItemStack? result = IntegrationSystem.Assemble(head, fitting, binding, actor.World);
        if (result == null) return;
        IntegrationSystem.ConsumeBinding(binding); slot.Itemstack = result; slot.MarkDirty();
    }
    public override bool OnHeldInteractCancel(float seconds, ItemSlot slot, EntityAgent actor, BlockSelection selection, EntitySelection entity, EnumItemUseCancelReason reason)
    {
        if (seconds >= 2.4f) OnHeldInteractStop(seconds, slot, actor, selection, entity);
        else if (!slot.Empty) slot.Itemstack.TempAttributes.RemoveAttribute("animalicatoolsmith:pairing");
        actor.StopAnimation("craftingwinding"); return true;
    }
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if (inSlot.Empty) return;
        dsc.AppendLine(inSlot.Itemstack!.GetToolhead().GetName()); dsc.AppendLine(inSlot.Itemstack.GetToolhandle().GetName());
    }
}

public sealed class LegacyRefitBehavior(CollectibleObject item) : CollectibleBehavior(item)
{
    private bool Ready(ItemSlot slot, EntityAgent actor) => !slot.Empty && !IntegrationSystem.Managed(slot.Itemstack) &&
        IntegrationSystem.IsFitting(actor.LeftHandItemSlot.Itemstack?.Collectible) && IntegrationSystem.Allowed(actor);
    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent actor, BlockSelection selection, EntitySelection entity, bool first, ref EnumHandHandling hand, ref EnumHandling handling)
    {
        if (!first || !Ready(slot, actor)) return;
        handling = EnumHandling.PreventSubsequent; hand = EnumHandHandling.PreventDefault;
        slot.Itemstack!.TempAttributes.SetBool("animalicatoolsmith:refitting", true); actor.StartAnimation("crafting");
    }
    public override bool OnHeldInteractStep(float seconds, ItemSlot slot, EntityAgent actor, BlockSelection selection, EntitySelection entity, ref EnumHandling handling)
    {
        if (slot.Empty || !slot.Itemstack.TempAttributes.GetBool("animalicatoolsmith:refitting")) return false;
        handling = EnumHandling.PreventSubsequent; return seconds < 2.5f && Ready(slot, actor);
    }
    public override void OnHeldInteractStop(float seconds, ItemSlot slot, EntityAgent actor, BlockSelection selection, EntitySelection entity, ref EnumHandling handling)
    {
        actor.StopAnimation("crafting");
        if (slot.Empty || !slot.Itemstack.TempAttributes.GetBool("animalicatoolsmith:refitting")) return;
        slot.Itemstack.TempAttributes.RemoveAttribute("animalicatoolsmith:refitting");
        handling = EnumHandling.PreventSubsequent;
        if (seconds < 2.4f || actor.World.Side != EnumAppSide.Server || !Ready(slot, actor)) return;
        ItemStack? head = IntegrationSystem.RecoverLegacy(slot.Itemstack, actor.World);
        if (head == null || !IntegrationSystem.Route(head, actor.LeftHandItemSlot.Itemstack, actor.World, out _)) return;
        ItemStack bundle = IntegrationSystem.Bundle(head, actor.LeftHandItemSlot.Itemstack!, actor.World);
        actor.LeftHandItemSlot.TakeOut(1); actor.LeftHandItemSlot.MarkDirty(); slot.Itemstack = bundle; slot.MarkDirty();
    }
    public override bool OnHeldInteractCancel(float seconds, ItemSlot slot, EntityAgent actor, BlockSelection selection, EntitySelection entity, EnumItemUseCancelReason reason, ref EnumHandling handled)
    {
        if(seconds>=2.4f) OnHeldInteractStop(seconds,slot,actor,selection,entity,ref handled);
        else if (!slot.Empty) slot.Itemstack.TempAttributes.RemoveAttribute("animalicatoolsmith:refitting");
        actor.StopAnimation("crafting"); return true;
    }
}
