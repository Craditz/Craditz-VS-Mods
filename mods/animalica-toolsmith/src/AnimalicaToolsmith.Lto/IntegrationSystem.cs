using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using LTO.BlackSmithPlus.Assembly;
using LTO.BlackSmithPlus.Core;
using LTO.BlackSmithPlus.ManualAssembly;
using LTO.BlackSmithPlus.Quality;
using LTO.BlackSmithPlus.Sharpening;
using LTO.BlackSmithPlus.ToolParts;
using LTO.BlackSmithPlus.Tools;
using LTO.BlackSmithPlus.Upgrades;
using LTO.BlackSmithPlus.Workbench;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace AnimalicaToolsmith;

/// <summary>LTO-native state and interactions. This assembly never references standalone Toolsmith.</summary>
public sealed class IntegrationSystem : ModSystem
{
    public const string Domain="animalicatoolsmith";
    private const string HarmonyId="craditz.animalicatoolsmith.lto";
    private Harmony? harmony;
    internal static IntegrationSystem? Client;
    internal ICoreClientAPI? ClientApi;
    internal AnimalPresentation? Presentation;
    internal WornPresentation? Worn;
    private static readonly ConditionalWeakTable<IWorldAccessor,Dictionary<string,Item>> Routes=new();
    private static readonly ConditionalWeakTable<Item,Item> PrimitiveBaselines=new();
    internal static Dictionary<string,Item> For(IWorldAccessor world)=>Routes.GetValue(world,_=>new(StringComparer.Ordinal));
    internal static readonly MethodInfo Holder=AccessTools.Method(AccessTools.TypeByName("AnimalicaBodyTools.Util.AnimalBodyToolTraitAccessUtil"),"HolderHasTraitFast")!;
    internal static readonly MethodInfo RestrictCraft=AccessTools.PropertyGetter(AccessTools.TypeByName("AnimalicaBodyTools.Config.AnimalicaBodyToolsPolicy"),"RestrictAnimalToolCrafting")!;
    public static bool Allowed(EntityAgent actor)=>!(bool)RestrictCraft.Invoke(null,null)! || (bool)Holder.Invoke(null,new object[]{actor})!;
    public static bool IsFitting(CollectibleObject? item)=>item?.Attributes?["animalicaToolsmithFitting"].AsBool(false)==true;
    public static bool IsFittingCode(string? code)=>code?.StartsWith(Domain+":fitting-",StringComparison.Ordinal)==true;
    public static bool IsWearable(CollectibleObject? item)=>item?.Attributes?["animalicaWearableBodyTool"].AsBool(false)==true;
    public static bool IsTool(CollectibleObject? item)=>item?.Code?.Domain=="animalicabodytools" &&
        new[]{"clawtips-","rendingclaws-","tunnelingclaws-","prospectingclaws-","harvestingclaws-","cultivatingclaws-"}.Any(x=>item.Code.Path.StartsWith(x,StringComparison.Ordinal));
    public static bool Managed(ItemStack? stack)=>stack!=null && IsTool(stack.Collectible) && AssemblyStatState.HasAssembly(stack) && IsFittingCode(stack.Attributes.GetString("ltobsplus:manual-source-handle"));
    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass(Domain+".fitting",typeof(ItemAnimalFitting));
        api.RegisterItemClass(Domain+".bundle",typeof(ItemAnimalBundle));
        api.RegisterCollectibleBehaviorClass(Domain+".animaltool",typeof(AnimalToolBehavior));
        api.RegisterCollectibleBehaviorClass(Domain+".visual",typeof(AnimalVisualBehavior));
        harmony=new Harmony(HarmonyId);
        Patch(typeof(ToolAssemblyService),"Resolve",nameof(Resolve));
        Patch(typeof(ToolAssemblyService).Assembly.GetType("LTO.BlackSmithPlus.Assembly.PrimitiveAssemblyResolver")!,"Resolve",nameof(ResolvePrimitive));
        var primitiveBaseline=typeof(ToolAssemblyService).Assembly.GetType("LTO.BlackSmithPlus.Assembly.PrimitiveHeadBaseline")!;
        Patch(primitiveBaseline,"TryInitializeFreshHead",nameof(PrimitiveBaseline));
        Patch(primitiveBaseline,"TryRecoverUninitializedProvisional",nameof(PrimitiveBaseline));
        Patch(typeof(ToolAssemblyService),"Matches",nameof(MatchScope));
        Patch(typeof(ManualAssemblyBehavior),"CanCreateProvisional",postfix:nameof(PairPermission));
        Patch(typeof(ProvisionalAssemblyItem),"CanFinish",nameof(PrepareFinish),nameof(FinishPermission));
        Patch(typeof(AssemblyWorkbenchBlockEntity),"CompleteTimedCraft",nameof(BenchPermission));
        Patch(typeof(PartItemState),"CreateRecoveredHandle",nameof(RecoverFitting));
        Patch(typeof(SharpeningBehavior),"OnDamageItem",nameof(NativeWear));
        Patch(typeof(UpgradeBehavior),"GetMiningSpeed",postfix:nameof(MiningMultiplier));
        Patch(typeof(BrokenToolRecovery),"TryQueueBreak",nameof(NativeBreak));
        var resolver=AccessTools.TypeByName("AnimalicaBodyTools.Systems.WearableToolResolver")!;
        Patch(resolver,"SyncVirtualToolStateToSource",nameof(SyncVirtual));
        Patch(resolver.GetNestedType("EquippedToolCache",BindingFlags.NonPublic)!,"EnsureCurrent",postfix:nameof(RefreshScoring));
        Patch(AccessTools.TypeByName("AnimalicaBodyTools.HarmonyPatches.WearableTargetedActionPatches")!,"TryDamageSourceTool",nameof(SingleBlockWear));
        Patch(typeof(GridRecipe),"Matches",nameof(RecipeStart),nameof(RecipeEnd),nameof(RecipeFinally));
        StationDismantling.Start(harmony);
    }
    private void Patch(Type type,string method,string? prefix=null,string? postfix=null,string? finalizer=null)
    {
        var original=AccessTools.Method(type,method)??throw new InvalidOperationException("Required LTO integration method missing: "+type.FullName+"."+method);
        HarmonyMethod? Hook(string? name)=>name==null?null:new HarmonyMethod(typeof(IntegrationSystem),name);
        harmony!.Patch(original,Hook(prefix),Hook(postfix),null,Hook(finalizer));
    }
    public override void StartClientSide(ICoreClientAPI api)
    {Client=this;ClientApi=api;Presentation=new(api);Worn=new(api);Worn.Start(harmony!);}
    public override void Dispose()
    {Presentation=null;Worn?.Dispose();Worn=null;harmony?.UnpatchAll(HarmonyId);if(Client==this)Client=null;ClientApi=null;}

    public override void AssetsFinalize(ICoreAPI api)
    {
        var routes=For(api.World);routes.Clear();
        foreach(var head in api.World.Items.Where(i=>i?.Code?.Domain=="game"))
        {
            string? family=null,metal=null;
            foreach(var pair in new Dictionary<string,string>{{"pickaxehead-","tunnelingclaws"},{"prospectingpickhead-","prospectingclaws"},{"axehead-","rendingclaws"},{"knifeblade-","clawtips"},{"shovelhead-","clawtips"},{"scythehead-","harvestingclaws"},{"hoehead-","cultivatingclaws"}})
                if(head.Code.Path.StartsWith(pair.Key,StringComparison.Ordinal)){family=pair.Value;metal=head.Code.Path[pair.Key.Length..];break;}
            if(family==null || !new[]{"flint","copper","tinbronze","bismuthbronze","blackbronze","iron","meteoriciron","steel"}.Contains(metal))continue;
            if(!LTOBlackSmithPlusSystem.ToolParts.TryGet(head.Code.ToString(),out var definition) || definition!.Kind!=ToolPartKind.Head)continue;
            string bodyMetal=metal=="tinbronze"?"bronze":metal!;
            if(api.World.GetItem(new AssetLocation("animalicabodytools",family+"-"+bodyMetal+"-wearable")) is {} output)
            {
                routes[head.Code.ToString()]=output;
                // LTO recognizes primitive materials by the final code suffix;
                // wearables end in -wearable. Use the same ordinary tool as the
                // native baseline source, retaining its own durability/sharpness rules.
                if(metal=="flint" && api.World.GetItem(new AssetLocation("game",head.Code.Path.Replace("head-","-"))) is {} ordinary)
                {PrimitiveBaselines.Remove(output);PrimitiveBaselines.Add(output,ordinary);}
            }
        }
        foreach(var item in api.World.Collectibles)
        {
            if(IsFitting(item))RegisterFitting(item);
            if(item.Code?.ToString()=="ltobsplus:manualassemblybundle")Add(item,new AnimalVisualBehavior(item));
            if(!IsTool(item))continue;
            var first=item.GetBehavior<AnimalToolBehavior>()??new AnimalToolBehavior(item);
            item.CollectibleBehaviors=new CollectibleBehavior[]{first}.Concat(item.CollectibleBehaviors.Where(b=>b is not AnimalToolBehavior)).ToArray();
            Add(item,new ForgeQualityBehavior(item));Add(item,new UpgradeBehavior(item));Add(item,new ManualAssemblyMetadataBehavior(item));Add(item,new SharpeningBehavior(item));
            // Body Tools supplies a later GUI icon override. Apply the accepted
            // component mesh last while keeping interaction/wear handling first.
            Add(item,new AnimalVisualBehavior(item));
        }
        foreach(var group in routes.GroupBy(r=>r.Value.Code.ToString()))
        {
            string head=group.First().Key;
            LTOBlackSmithPlusSystem.Tools.Register(new ToolDefinition(group.Key,new[]{new ToolPartRequirement(ToolPartKind.Head,head),new ToolPartRequirement(ToolPartKind.Handle)},tags:new[]{"animalica"}),true);
        }
        var suppressed=api.World.GridRecipes.Where(r=>IsTool(r.Output?.ResolvedItemStack?.Collectible) ||
            r.Attributes?["animalicaRecoverHead"].AsBool(false)==true && !(r.ResolvedIngredients?.Any(i=>i?.Code?.Path.Contains("falx",StringComparison.Ordinal)==true)??false)).ToHashSet();
        foreach(var recipe in suppressed)recipe.Enabled=false;
        api.World.GridRecipes.RemoveAll(suppressed.Contains);
        api.Logger.Notification("[animalicatoolsmith] LTO provider registered {0} original-head routes and {1} animal fittings; native LTO state/maintenance active.",routes.Count,api.World.Collectibles.Count(IsFitting));
        if(api.Side==EnumAppSide.Server && routes.Count==0)throw new InvalidOperationException("LTO did not expose supported original heads; refusing empty animal integration.");
    }
    private static void Add(CollectibleObject item,CollectibleBehavior behavior)
    {if(!item.CollectibleBehaviors.Any(b=>b.GetType()==behavior.GetType()))item.CollectibleBehaviors=item.CollectibleBehaviors.Append(behavior).ToArray();}
    public static ToolPartStats FittingProfile(CollectibleObject fitting)
    {
        string seat=fitting.Attributes["seatProfile"].AsString()??"stick";
        string brace=fitting.Attributes["braceProfile"].AsString()??"stick";
        string lining=fitting.Attributes["liningProfile"].AsString()??"plain";
        string seatCode=seat switch{"bone"=>"game:bone","handle"=>"ltobsplus:handle","professional"=>"ltobsplus:carpentedhandle",_=>"game:firewood"};
        string braceCode=brace switch{"handle"=>"ltobsplus:leatherstrips","professional"=>"ltobsplus:sturdyleatherstrips",_=>"ltobsplus:vegetalfiberbinding"};
        string? liningCode=lining switch{"twine" or "cloth"=>"game:flaxtwine","leather"=>"ltobsplus:leatherstrips","sturdy"=>"ltobsplus:sturdyleatherstrips",_=>null};
        var registry=LTOBlackSmithPlusSystem.ToolParts;
        if(!registry.TryGet(seatCode,out var s) || !registry.TryGet(braceCode,out var b))throw new InvalidOperationException("LTO fitting material profile missing.");
        float liningMultiplier=liningCode!=null && registry.TryGet(liningCode,out var l)?l!.Stats.HandleDurabilityMultiplier:1;
        return new ToolPartStats{HandleToolBaseMultiplier=b!.Stats.BindingToolBaseMultiplier*liningMultiplier,BindingDurabilityMultiplier=s!.Stats.BindingDurabilityMultiplier};
    }
    private static void RegisterFitting(CollectibleObject fitting)=>LTOBlackSmithPlusSystem.ToolParts.Register(new ToolPartDefinition(fitting.Code.ToString(),ToolPartKind.Handle,"animal-fitting",FittingProfile(fitting),new[]{"animalica","handle"}),true);
    private static bool Resolve(IEnumerable<string> partCodes,ref AssemblyResolution __result)
    {
        var codes=partCodes.ToArray();if(!codes.Any(IsFittingCode))return true;
        var fittings=codes.Where(IsFittingCode).ToArray();var heads=codes.Where(c=>!IsFittingCode(c)).ToArray();
        __result=AssemblyResolution.Failed(AssemblyResolutionStatus.NoMatch,"Animal fitting requires one supported original head with a matching Body Tool.");
        if(fittings.Length!=1 || heads.Length!=1)return false;
        if(!LTOBlackSmithPlusSystem.ToolParts.TryGet(fittings[0],out var fit) || fit!.Kind!=ToolPartKind.Handle ||
           !LTOBlackSmithPlusSystem.ToolParts.TryGet(heads[0],out var headPart) || headPart!.Kind!=ToolPartKind.Head)return false;
        // Definitions can be shared between worlds; derive only from verified item codes.
        foreach(var route in LTOBlackSmithPlusSystem.Tools.All.Where(t=>t.Tags.Contains("animalica")))
        {
            var original=route.PartRequirements.First(r=>r.Kind==ToolPartKind.Head).ExactPartCode!;
            bool same=original==heads[0] || route.Code.StartsWith("animalicabodytools:clawtips-",StringComparison.Ordinal) &&
                original[(original.LastIndexOf('-')+1)..]==heads[0][(heads[0].LastIndexOf('-')+1)..] &&
                (heads[0].StartsWith("game:knifeblade-",StringComparison.Ordinal)||heads[0].StartsWith("game:shovelhead-",StringComparison.Ordinal));
            if(!same)continue;
            __result=AssemblyResolution.Matched(new ToolDefinition(route.Code,new[]{new ToolPartRequirement(ToolPartKind.Head,heads[0]),new ToolPartRequirement(ToolPartKind.Handle,fittings[0])},tags:new[]{"animalica"}));
            break;
        }
        return false;
    }
    private static bool ResolvePrimitive(string headCode,string handleCode,ref AssemblyResolution __result)
        =>Resolve(new[]{headCode,handleCode},ref __result);
    private static void PrimitiveBaseline(ref Item __1)
    {if(PrimitiveBaselines.TryGetValue(__1,out var ordinary))__1=ordinary;}
    private static void PairPermission(ItemSlot slot,EntityAgent byEntity,ref AssemblyResolution resolution,ref bool __result)
    {
        if(!IsFitting(byEntity.LeftHandItemSlot.Itemstack?.Collectible))return;
        // LTO also has a secondary primitive-knife fallback after the main
        // resolver. Revalidate the final result before either part is consumed.
        resolution=LTOBlackSmithPlusSystem.Assembly.Resolve(new[]{slot.Itemstack?.Collectible.Code.ToString()??"",PartItemState.GetLogicalHandleCode(byEntity.LeftHandItemSlot.Itemstack)});
        __result=__result && resolution.Success && Allowed(byEntity);
    }
    private static bool PrepareFinish(ItemSlot slot,EntityAgent byEntity,ref bool __result)
    {
        var stack=slot.Itemstack;string? fitting=stack?.Attributes.GetString("ltobsplus:manual-source-handle");
        if(!IsFittingCode(fitting))return true;
        var route=LTOBlackSmithPlusSystem.Assembly.Resolve(new[]{stack!.Attributes.GetString("ltobsplus:manual-source-head",""),fitting!});
        if(!route.Success || !Allowed(byEntity)){__result=false;return false;}
        // Repair saved provisional targets from the older fallback without
        // replacing the bundle or resetting any component condition/quality.
        if(stack.Attributes.GetString("ltobsplus:manual-target-tool")!=route.Tool!.Code)
        {stack.Attributes.SetString("ltobsplus:manual-target-tool",route.Tool.Code);slot.MarkDirty();}
        return true;
    }
    private static void FinishPermission(ItemSlot slot,EntityAgent byEntity,ref bool __result)
    {if(IsFittingCode(slot.Itemstack?.Attributes.GetString("ltobsplus:manual-source-handle")) && !Allowed(byEntity))__result=false;}
    private static bool MatchScope(ToolDefinition tool,IReadOnlyList<ToolPartDefinition> supplied,ref bool __result)
    {
        if(tool.Tags.Contains("animalica")==supplied.Any(p=>IsFittingCode(p.Code)))return true;
        __result=false;return false;
    }
    private static bool BenchPermission(AssemblyWorkbenchBlockEntity __instance,IPlayer player)
    {
        var inventory=(InventoryBase)AccessTools.Property(__instance.GetType(),"Inventory").GetValue(__instance)!;
        return !IsFitting(inventory[1].Itemstack?.Collectible) || Allowed(player.Entity);
    }
    private static bool RecoverFitting(IWorldAccessor world,string originalCode,int current,int maximum,ref ItemStack? __result)
    {
        if(!IsFittingCode(originalCode))return true;
        __result=null;
        if(current>0 && world.GetItem(new AssetLocation(originalCode)) is {} item)
        {__result=new ItemStack(item);PartItemState.Store(__result,current,maximum);}
        return false;
    }
    private static bool Virtual(ItemSlot? slot)=>slot is DummySlot && IsFittingCode(slot.Itemstack?.Attributes.GetString("ltobsplus:manual-source-handle")) && !IsWearable(slot.Itemstack?.Collectible);
    private static void MiningMultiplier(ItemStack itemstack,ref EnumHandling bhHandling)
    {
        // Keep the native upgrade number, but multiply the Body Tool's material
        // speed rather than replacing it with the bare multiplier.
        if(Managed(itemstack) && bhHandling==EnumHandling.PreventDefault)bhHandling=EnumHandling.Handled;
    }
    private static bool NativeWear(ItemSlot itemslot,ref int amount,ref EnumHandling bhHandling)
    {
        if(!Virtual(itemslot))return !IsTool(itemslot.Itemstack?.Collectible) || Managed(itemslot.Itemstack);
        amount=0;bhHandling=EnumHandling.PreventSubsequent;return false;
    }
    private static bool NativeBreak(ItemSlot? slot,ref bool __result)
    {if(!Virtual(slot))return true;__result=false;return false;}
    private static bool SyncVirtual(ItemStack virtualStack,ItemSlot sourceSlot,ref bool __result)
    {
        if(!Managed(sourceSlot.Itemstack))return true;
        foreach(string key in new[]{"toolMode","probePositions"})
            if(virtualStack.Attributes.HasAttribute(key))sourceSlot.Itemstack!.Attributes[key]=virtualStack.Attributes[key].Clone();
        sourceSlot.MarkDirty();__result=true;return false;
    }
    private static void RefreshScoring(object __instance)
    {
        if(AccessTools.Property(__instance.GetType(),"Candidates").GetValue(__instance) is not IEnumerable candidates)return;
        foreach(object candidate in candidates)
        {
            var source=(ItemSlot)AccessTools.Property(candidate.GetType(),"SourceSlot").GetValue(candidate)!;
            if(Managed(source.Itemstack))((ItemStack)AccessTools.Property(candidate.GetType(),"ScoringStack").GetValue(candidate)!).Attributes=source.Itemstack!.Attributes.Clone();
        }
    }
    private static bool SingleBlockWear(EntityAgent entity,ItemSlot sourceSlot,string action,ref bool __result)
    {
        if(!Managed(sourceSlot.Itemstack) || !action.StartsWith("block-break:",StringComparison.Ordinal))return true;
        var type=AccessTools.TypeByName("AnimalicaBodyTools.HarmonyPatches.WearableBlockBreakToolContext")!;
        object? context=AccessTools.Property(type,"Current").GetValue(null);
        if(context==null || !(bool)AccessTools.Property(context.GetType(),"DurabilityCharged").GetValue(context)! ||
            ((IPlayer)AccessTools.Property(context.GetType(),"Player").GetValue(context)!).Entity!=entity)return true;
        var block=(Block)AccessTools.Property(context.GetType(),"Block").GetValue(context)!;
        if(!action.StartsWith("block-break:"+block.Code.ToShortString()+":",StringComparison.Ordinal))return true;
        __result=false;return false;
    }
    private static void RecipeStart(GridRecipe __instance,out string? __state)
    {__state=null;if(__instance.Output?.Code?.Domain!=Domain)return;__state=__instance.RequiresTrait;__instance.RequiresTrait=null!;}
    private static void RecipeEnd(GridRecipe __instance,IPlayer forPlayer,ref bool __result,string? __state)
    {if(__instance.Output?.Code?.Domain!=Domain)return;__instance.RequiresTrait=__state!;if(__result && (forPlayer?.Entity==null || !Allowed(forPlayer.Entity)))__result=false;}
    private static Exception? RecipeFinally(GridRecipe __instance,string? __state,Exception? __exception)
    {if(__instance.Output?.Code?.Domain==Domain)__instance.RequiresTrait=__state!;return __exception;}

    public static AnimalVisualParts? VisualParts(ItemStack stack)
    {
        if(Client?.ClientApi?.World is not {} world)return null;
        bool bundle=stack.Collectible.Code.ToString()=="ltobsplus:manualassemblybundle";
        if(!bundle && !Managed(stack))return null;
        string head=stack.Attributes.GetString("ltobsplus:manual-source-head","");
        string fitting=stack.Attributes.GetString("ltobsplus:manual-source-handle","");
        if(!IsFittingCode(fitting) || world.GetItem(new AssetLocation(head)) is not {} h || world.GetItem(new AssetLocation(fitting)) is not {} f)return null;
        string binding=stack.Attributes.GetString("ltobsplus:manual-binding","");
        var b=bundle || binding.Length==0 || binding=="none"?null:world.GetItem(new AssetLocation(binding));
        return new(new ItemStack(h),new ItemStack(f),b==null?null:new ItemStack(b),bundle);
    }
    internal static bool PrepareLegacy(ItemSlot slot,EntityAgent actor)
    {
        if(slot.Empty || !IsTool(slot.Itemstack.Collectible) || Managed(slot.Itemstack) || slot.Itemstack.Attributes.HasAttribute("tinkeredToolHead") || !IsFitting(actor.LeftHandItemSlot.Itemstack?.Collectible) || !Allowed(actor))return false;
        if(actor.World.Side!=EnumAppSide.Server)return true;
        string wearableCode=slot.Itemstack.Collectible.Code.ToString();
        if(!wearableCode.EndsWith("-wearable",StringComparison.Ordinal))wearableCode+="-wearable";
        var route=For(actor.World).FirstOrDefault(r=>r.Value.Code.ToString()==wearableCode);
        string? headCode=route.Key;
        if(headCode==null || actor.World.GetItem(new AssetLocation(headCode)) is not {} head || actor.World.GetItem(new AssetLocation("ltobsplus:manualassemblybundle")) is not {} bundleItem)return false;
        var source=slot.Itemstack!;float condition=Math.Clamp((float)source.Collectible.GetRemainingDurability(source)/Math.Max(1,source.Collectible.GetMaxDurability(source)),0,1);
        var bundle=new ItemStack(bundleItem){Attributes=source.Attributes.Clone()};
        int maximum=Math.Max(1,route.Value.GetMaxDurability(new ItemStack(route.Value)));
        bundle.Attributes.SetInt("ltobsplus:repair-part-max-durability",maximum);bundle.Attributes.SetInt("ltobsplus:repair-part-current-durability",Math.Max(1,(int)(maximum*condition)));
        bundle.Attributes.SetInt(SharpeningState.MaximumAttribute,SharpeningState.GetMaximum(source));
        bundle.Attributes.SetInt(SharpeningState.CurrentAttribute,SharpeningState.GetCurrent(source));
        bundle.Attributes.SetString("ltobsplus:manual-source-head",headCode);bundle.Attributes.SetString("ltobsplus:manual-target-tool",wearableCode);
        bundle.Attributes.SetString("ltobsplus:manual-source-handle",actor.LeftHandItemSlot.Itemstack!.Collectible.Code.ToString());
        bundle.Attributes.SetFloat(AssemblyStatState.SourceHandleConditionAttribute,PartItemState.GetConditionRatio(actor.LeftHandItemSlot.Itemstack));
        slot.Itemstack=bundle;actor.LeftHandItemSlot.TakeOut(1);actor.LeftHandItemSlot.MarkDirty();slot.MarkDirty();
        actor.World.PlaySoundAt(new AssetLocation("game:sounds/player/messycraft"),actor.Pos.X,actor.Pos.Y,actor.Pos.Z,null,true,32,1);
        return true;
    }
}

public sealed class ItemAnimalFitting : Item
{
    public override void GetHeldItemInfo(ItemSlot inSlot,StringBuilder dsc,IWorldAccessor world,bool withDebugInfo)
    {base.GetHeldItemInfo(inSlot,dsc,world,withDebugInfo);if(inSlot.Empty)return;var s=IntegrationSystem.FittingProfile(this);dsc.AppendLine($"LTO fitting durability: ×{s.HandleToolBaseMultiplier:0.###}; binding support: +{s.BindingDurabilityMultiplier-1:P0}");if(PartItemState.TryGetStoredDurability(inSlot.Itemstack,out int current,out int maximum))dsc.AppendLine($"Condition: {current} / {maximum}");}
}
public sealed class ItemAnimalBundle : Item
{
    public override void GetHeldItemInfo(ItemSlot inSlot,StringBuilder dsc,IWorldAccessor world,bool withDebugInfo)
    {base.GetHeldItemInfo(inSlot,dsc,world,withDebugInfo);dsc.AppendLine("This is a standalone Toolsmith bundle. LTO uses its native provisional assembly; changing providers does not convert existing components.");}
}
public class AnimalVisualBehavior(CollectibleObject item) : CollectibleBehavior(item)
{
    public override void OnBeforeRender(ICoreClientAPI api,ItemStack stack,EnumItemRenderTarget target,ref ItemRenderInfo info)=>IntegrationSystem.Client?.Presentation?.Apply(stack,target,ref info);
    public override void OnUnloaded(ICoreAPI api)
    {if(api is ICoreClientAPI)IntegrationSystem.Client?.Presentation?.Dispose();base.OnUnloaded(api);}
}
public sealed class AnimalToolBehavior(CollectibleObject item) : CollectibleBehavior(item)
{
    private readonly SharpeningBehavior sharp=new(item);
    public override void OnDamageItem(IWorldAccessor world,Entity byEntity,ItemSlot itemslot,ref int amount,ref EnumHandling bhHandling)
    {
        if(!IntegrationSystem.Managed(itemslot.Itemstack))return;
        // Wearable's clothing damage runs before later behaviors and would
        // destroy the source before LTO's deferred recovery can use it.
        // Let LTO calculate wear first, then apply only its non-breaking head
        // charge here; its queued break keeps the real slot alive until recovery.
        var nativeHandling=EnumHandling.PassThrough;
        sharp.OnDamageItem(world,byEntity,itemslot,ref amount,ref nativeHandling);
        if(nativeHandling==EnumHandling.PassThrough && amount>0 && itemslot.Itemstack is {} source)
            source.Collectible.SetDurability(source,Math.Max(1,source.Collectible.GetRemainingDurability(source)-amount));
        amount=0;bhHandling=EnumHandling.PreventSubsequent;
    }
    public override void GetHeldItemInfo(ItemSlot inSlot,StringBuilder dsc,IWorldAccessor world,bool withDebugInfo)
    {
        if(!IntegrationSystem.Managed(inSlot.Itemstack))return;
        var s=inSlot.Itemstack!;string fitting=s.Attributes.GetString("ltobsplus:manual-source-handle");
        dsc.AppendLine("Provider: LTO BlackSmith Plus");dsc.AppendLine("Animal fitting: "+(world.GetItem(new AssetLocation(fitting))?.GetHeldItemName(new ItemStack(world.GetItem(new AssetLocation(fitting))))??fitting));
        dsc.AppendLine($"Head: {s.Collectible.GetRemainingDurability(s)} / {s.Collectible.GetMaxDurability(s)}; sharpness: {SharpeningState.GetCurrent(s)} / {SharpeningState.GetMaximum(s)}");
        dsc.AppendLine($"Fitting: {ToolPartWearState.GetHandleCurrent(s)} / {ToolPartWearState.GetHandleMaximum(s)}; binding: {ToolPartWearState.GetBindingCurrent(s)} / {ToolPartWearState.GetBindingMaximum(s)}");
    }
    public override void OnHeldInteractStart(ItemSlot slot,EntityAgent actor,BlockSelection block,EntitySelection entity,bool first,ref EnumHandHandling hand,ref EnumHandling handling)
    {
        if(!first || slot.Empty)return;
        if(IntegrationSystem.Managed(slot.Itemstack) && actor.LeftHandItemSlot.Itemstack?.Collectible.Code.ToString()=="ltobsplus:whetstone")
        {slot.Itemstack!.TempAttributes.SetBool("animalicatoolsmith:lto-sharpen",true);sharp.OnHeldInteractStart(slot,actor,block,entity,first,ref hand,ref handling);handling=EnumHandling.PreventSubsequent;return;}
        if(IntegrationSystem.Managed(slot.Itemstack) || !IntegrationSystem.IsFitting(actor.LeftHandItemSlot.Itemstack?.Collectible) || !IntegrationSystem.Allowed(actor) || slot.Itemstack!.Attributes.HasAttribute("tinkeredToolHead"))return;
        slot.Itemstack!.TempAttributes.SetBool("animalicatoolsmith:lto-refit",true);actor.StartAnimation("crafting");hand=EnumHandHandling.PreventDefault;handling=EnumHandling.PreventSubsequent;
    }
    public override bool OnHeldInteractStep(float seconds,ItemSlot slot,EntityAgent actor,BlockSelection block,EntitySelection entity,ref EnumHandling handling)
    {
        if(slot.Empty)return false;
        if(slot.Itemstack.TempAttributes.GetBool("animalicatoolsmith:lto-sharpen")){handling=EnumHandling.PreventSubsequent;return sharp.OnHeldInteractStep(seconds,slot,actor,block,entity,ref handling);}
        if(!slot.Itemstack.TempAttributes.GetBool("animalicatoolsmith:lto-refit"))return false;
        handling=EnumHandling.PreventSubsequent;return seconds<2.5f && IntegrationSystem.IsFitting(actor.LeftHandItemSlot.Itemstack?.Collectible) && IntegrationSystem.Allowed(actor);
    }
    public override void OnHeldInteractStop(float seconds,ItemSlot slot,EntityAgent actor,BlockSelection block,EntitySelection entity,ref EnumHandling handling)
    {
        if(slot.Empty)return;
        if(slot.Itemstack.TempAttributes.GetBool("animalicatoolsmith:lto-sharpen")){slot.Itemstack.TempAttributes.RemoveAttribute("animalicatoolsmith:lto-sharpen");sharp.OnHeldInteractStop(seconds,slot,actor,block,entity,ref handling);handling=EnumHandling.PreventSubsequent;return;}
        if(!slot.Itemstack.TempAttributes.GetBool("animalicatoolsmith:lto-refit"))return;
        slot.Itemstack.TempAttributes.RemoveAttribute("animalicatoolsmith:lto-refit");actor.StopAnimation("crafting");handling=EnumHandling.PreventSubsequent;
        if(seconds>=2.4f)IntegrationSystem.PrepareLegacy(slot,actor);
    }
    public override bool OnHeldInteractCancel(float seconds,ItemSlot slot,EntityAgent actor,BlockSelection block,EntitySelection entity,EnumItemUseCancelReason reason,ref EnumHandling handling)
    {
        if(!slot.Empty && slot.Itemstack.TempAttributes.GetBool("animalicatoolsmith:lto-sharpen"))
        {slot.Itemstack.TempAttributes.RemoveAttribute("animalicatoolsmith:lto-sharpen");return sharp.OnHeldInteractCancel(seconds,slot,actor,block,entity,reason,ref handling);}
        OnHeldInteractStop(seconds,slot,actor,block,entity,ref handling);return true;
    }
}
