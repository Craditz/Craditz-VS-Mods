using HarmonyLib;
using rustboundmagic.src;
using rustboundmagic.src.system.abstracts;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using System.Text;
using Vintagestory.API.Config;

namespace RustboundExpansion;

public sealed class MischiefConfig { public bool EnableMemeCipherCrafting { get; set; } = true; }
public sealed class MischiefCipherItem : Item { }

public sealed class MischiefSystem : ModSystem
{
    internal const string ConfigFile = "rustboundexpansion.json";
    private static readonly Dictionary<IWorldAccessor, MischiefConfig> configs = new(ReferenceEqualityComparer.Instance);
    private IWorldAccessor? world;
    public override double ExecuteOrder() => .8;
    public override void StartPre(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server) return;
        world = api.World;
        MischiefConfig config;
        try { config = api.LoadModConfig<MischiefConfig>(ConfigFile) ?? new(); api.StoreModConfig(config, ConfigFile); }
        catch (Exception e) { api.Logger.Error("[CrookedCodex] Invalid {0}; meme cipher crafting disabled: {1}", ConfigFile, e.Message); config = new() { EnableMemeCipherCrafting = false }; }
        SetConfig(api.World, config);
    }
    public override void AssetsFinalize(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server && !CraftingEnabled(api.World))
            api.World.GridRecipes.RemoveAll(IsCipherRecipe);
    }
    internal static void SetConfig(IWorldAccessor world, MischiefConfig config) => configs[world] = config;
    internal static bool CraftingEnabled(IWorldAccessor world) => !configs.TryGetValue(world, out var config) || config.EnableMemeCipherCrafting;
    internal static bool IsCipherRecipe(GridRecipe recipe) => recipe.Output?.Code?.ToString() == "rustboundexpansion:" + MemeCatalog.Cipher;
    internal static bool CraftAllowed(GridRecipe recipe, IWorldAccessor? world) => world == null || world.Side != EnumAppSide.Server || !IsCipherRecipe(recipe) || CraftingEnabled(world);
    internal static void Install(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(RustboundMagic), "CreateSubSpellLists"), postfix: new(typeof(MischiefSystem), nameof(AfterPools)));
        h.Patch(AccessTools.Method(typeof(GridRecipe), nameof(GridRecipe.Matches)), prefix: new(typeof(MischiefSystem), nameof(Matches)));
        h.Patch(AccessTools.Method(typeof(GridRecipe), nameof(GridRecipe.ConsumeInput)), prefix: new(typeof(MischiefSystem), nameof(Consume)));
        h.Patch(AccessTools.DeclaredMethod(typeof(ItemSlotOutput), nameof(ItemSlot.CanTake)), postfix: new(typeof(MischiefSystem), nameof(CanTakeCraftingOutput)));
        var forge = typeof(RustboundMagic).Assembly.GetTypes().Single(t => t.Name == "BlockEntityStationSpellforgeRM");
        h.Patch(AccessTools.Method(forge, "CalculateNewSpellToDiscover"), prefix: new(typeof(MischiefSystem), nameof(Discover)));
        h.Patch(AccessTools.Method(forge, "GetBlockInfo"), postfix: new(typeof(MischiefSystem), nameof(ForgeInfo)));
        h.Patch(AccessTools.Method(typeof(InventoryBase), nameof(InventoryBase.Open)), postfix: new(typeof(MischiefSystem), nameof(Opened)));
        h.Patch(AccessTools.Method(typeof(SpellBase), nameof(SpellBase.ConsumeManaForSpell), [typeof(EntityPlayer), typeof(string), typeof(bool)]), postfix: new(typeof(MischiefSystem), nameof(PaidNormal)));
        h.Patch(AccessTools.Method(typeof(SpellBase), nameof(SpellBase.ConsumeManaForSpell), [typeof(EntityPlayer), typeof(string), typeof(int)]), postfix: new(typeof(MischiefSystem), nameof(PaidCustom)));
    }
    private static void AfterPools() => MemeCatalog.ExcludeNormalPools();
    private static bool Matches(GridRecipe __instance, IWorldAccessor __1, ref bool __result)
    { if (CraftAllowed(__instance, __1)) return true; __result = false; return false; }
    private static bool Consume(GridRecipe __instance, IPlayer __0, ref bool __result)
    { if (CraftAllowed(__instance, __0?.Entity?.World)) return true; __result = false; return false; }
    private static void CanTakeCraftingOutput(ItemSlot __instance, ref bool __result)
    {
        if (__result && __instance.GetType().Name == "ItemSlotCraftingOutput" && IsMischief(__instance.Itemstack)
            && __instance.Inventory?.Api?.World is { Side: EnumAppSide.Server } world && !CraftingEnabled(world)) __result = false;
    }
    // Only the dedicated blank is intercepted. Ordinary research keeps its
    // native failure/success and school/essence filtering without meme outcomes.
    private static bool Discover(BlockEntityContainer __instance)
    {
        if (!IsMischief(__instance.Inventory[0].Itemstack)) return true;
        if (__instance.Api.Side != EnumAppSide.Server || !__instance.Inventory[1].Empty) return false;
        var output = ResearchOutput(__instance.Api.World, __instance.Inventory[0].Itemstack);
        if (output == null) { __instance.Api.Logger.Error("[CrookedCodex] Missing mischief output cipher"); return false; }
        __instance.Inventory[0].TakeOut(1);
        __instance.Inventory[0].Itemstack = output;
        __instance.Inventory[0].MarkDirty(); __instance.MarkDirty(true);
        AccessTools.Method(typeof(BlockEntityDisplay), "updateMeshes").Invoke(__instance, null);
        return false;
    }
    internal static ItemStack? ResearchOutput(IWorldAccessor world, ItemStack? input)
    {
        if (!IsMischief(input)) return null;
        string id = MemeCatalog.All[world.Rand.Next(MemeCatalog.All.Length)].Id;
        var item = world.GetItem(new AssetLocation("rustboundmagic:spellcipher-" + id));
        return item == null ? null : new ItemStack(item);
    }
    private static void ForgeInfo(BlockEntityContainer __instance, StringBuilder dsc)
    {
        if (!IsMischief(__instance.Inventory[0].Itemstack)) return;
        dsc.Clear(); dsc.AppendLine("Mischief research: only joke spells can be discovered.");
        dsc.AppendLine("Prepare and research this cipher as a normal tier-1 Neutral cipher.");
        foreach (var d in MemeCatalog.All) dsc.AppendLine("• " + Lang.Get("rustboundmagic:spell-name-" + d.Id));
    }
    internal static bool IsMischief(ItemStack? stack) => stack?.Collectible.Code?.ToString() == "rustboundexpansion:" + MemeCatalog.Cipher;
    private static void Opened(InventoryBase __instance, IPlayer player)
    {
        if (__instance.Pos != null && player?.Entity is { } target && target.World.Side == EnumAppSide.Server)
            ExpansionRuntime.For(target.World)?.MemeContainerOpened(target);
    }
    private static void PaidNormal(EntityPlayer __0, string __1) => Paid(__0, __1);
    private static void PaidCustom(SpellBase __instance, EntityPlayer __0, string __1)
    { if (__instance is NextSpell { Definition.Kind: not "charge" }) Paid(__0, __1); }
    private static void Paid(EntityPlayer p, string id)
    { if (p.World.Side == EnumAppSide.Server) ExpansionRuntime.For(p.World)?.MemeSpellCast(p); }
    public override void Dispose() { if (world != null) configs.Remove(world); }
}
