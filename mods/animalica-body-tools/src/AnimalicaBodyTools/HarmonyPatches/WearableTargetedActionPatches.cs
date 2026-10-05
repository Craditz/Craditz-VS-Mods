using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using AnimalicaBodyTools.Config;
using AnimalicaBodyTools.Systems;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace AnimalicaBodyTools.HarmonyPatches;

internal static class WearableTargetedActionPatches
{
    public static bool DiagnosticsEnabled => AnimalicaBodyToolsPolicy.DiagnosticLogging;

    [ThreadStatic]
    private static int blockBreakDurabilitySuppressions;
    [ThreadStatic]
    private static int suppressedLogBreakCount;

    public static bool TryCreateVirtualDummySlot(EntityAgent entity, ItemSlot activeSlot, out ItemStack virtualTool, out string sourceCode, out ItemSlot dummySlot)
    {
        return TryCreateVirtualDummySlot(entity, activeSlot, out virtualTool, out sourceCode, out dummySlot, out _);
    }

    public static bool TryCreateVirtualDummySlot(EntityAgent entity, ItemSlot activeSlot, out ItemStack virtualTool, out string sourceCode, out ItemSlot dummySlot, out ItemSlot sourceSlot)
    {
        return TryCreateVirtualDummySlot(entity, activeSlot, _ => 1f, out virtualTool, out sourceCode, out dummySlot, out sourceSlot, out _);
    }

    public static bool TryCreateVirtualDummySlot(
        EntityAgent entity,
        ItemSlot activeSlot,
        System.Func<ItemStack, float> scoreSelector,
        out ItemStack virtualTool,
        out string sourceCode,
        out ItemSlot dummySlot,
        out ItemSlot sourceSlot,
        out float selectedScore)
    {
        virtualTool = null!;
        sourceCode = string.Empty;
        dummySlot = null!;
        sourceSlot = null!;
        selectedScore = 0f;

        if (!WearableToolResolver.TryGetVirtualToolForEmptyHand(entity, activeSlot, scoreSelector, out virtualTool, out sourceCode, out sourceSlot, out selectedScore))
        {
            return false;
        }

        dummySlot = new DummySlot(virtualTool);
        return true;
    }

    public static bool TryCreateVirtualDummySlot(IPlayer player, ItemSlot activeSlot, out ItemStack virtualTool, out string sourceCode, out ItemSlot dummySlot)
    {
        return TryCreateVirtualDummySlot(player, activeSlot, out virtualTool, out sourceCode, out dummySlot, out _);
    }

    public static bool TryCreateVirtualDummySlot(IPlayer player, ItemSlot activeSlot, out ItemStack virtualTool, out string sourceCode, out ItemSlot dummySlot, out ItemSlot sourceSlot)
    {
        return TryCreateVirtualDummySlot(player, activeSlot, _ => 1f, out virtualTool, out sourceCode, out dummySlot, out sourceSlot, out _);
    }

    public static bool TryCreateVirtualDummySlot(
        IPlayer player,
        ItemSlot activeSlot,
        System.Func<ItemStack, float> scoreSelector,
        out ItemStack virtualTool,
        out string sourceCode,
        out ItemSlot dummySlot,
        out ItemSlot sourceSlot,
        out float selectedScore)
    {
        virtualTool = null!;
        sourceCode = string.Empty;
        dummySlot = null!;
        sourceSlot = null!;
        selectedScore = 0f;

        if (player.Entity is not EntityAgent entity)
        {
            return false;
        }

        return TryCreateVirtualDummySlot(entity, activeSlot, scoreSelector, out virtualTool, out sourceCode, out dummySlot, out sourceSlot, out selectedScore);
    }

    public static bool TryCreateVirtualMiningDummySlot(
        IPlayer player,
        ItemSlot activeSlot,
        Block block,
        BlockSelection blockSel,
        out ItemStack virtualTool,
        out string sourceCode,
        out ItemSlot dummySlot,
        out ItemSlot sourceSlot,
        out float selectedScore)
    {
        virtualTool = null!;
        sourceCode = string.Empty;
        dummySlot = null!;
        sourceSlot = null!;
        selectedScore = 0f;

        if (player.Entity is not EntityAgent entity
            || !WearableToolResolver.TryGetVirtualMiningToolForEmptyHand(
                entity,
                activeSlot,
                block,
                blockSel,
                player,
                out virtualTool,
                out sourceCode,
                out sourceSlot,
                out selectedScore))
        {
            return false;
        }

        dummySlot = new DummySlot(virtualTool);
        return true;
    }

    public static string DescribeTool(ItemStack stack, ItemSlot slot)
    {
        CollectibleObject? collectible = stack.Collectible;
        string code = collectible?.Code?.ToShortString() ?? "unknown";
        string tool = collectible?.GetTool(slot)?.ToString() ?? "none";
        int tier = collectible?.GetToolTier(slot) ?? 0;
        return $"{code}; tool={tool}; tier={tier}";
    }

    public static bool TryDamageSourceTool(EntityAgent entity, ItemSlot sourceSlot, string sourceCode, string action, int amount = 1)
    {
        if (entity.World.Side != EnumAppSide.Server || amount <= 0)
        {
            return false;
        }

        ItemStack? beforeStack = WearableToolResolver.GetRealItemstack(sourceSlot);
        CollectibleObject? collectible = beforeStack?.Collectible;
        if (beforeStack == null || collectible == null)
        {
            entity.World.Logger.Warning("[animalicabodytools] Wearable durability skipped: action={0}, source={1}, reason=source-slot-empty.", action, sourceCode);
            return false;
        }

        int maxBefore = collectible.GetMaxDurability(beforeStack);
        if (maxBefore <= 0)
        {
            entity.World.Logger.Warning("[animalicabodytools] Wearable durability skipped: action={0}, source={1}, item={2}, reason=max-durability-zero.", action, sourceCode, collectible.Code);
            return false;
        }

        int before = GetRemainingDurability(beforeStack, maxBefore);
        string beforeSlotState = DescribeSourceSlot(sourceSlot);
        collectible.DamageItem(entity.World, entity, sourceSlot, amount, true);
        sourceSlot.MarkDirty();

        ItemStack? afterStack = WearableToolResolver.GetRealItemstack(sourceSlot);
        int maxAfter = afterStack?.Collectible?.GetMaxDurability(afterStack) ?? maxBefore;
        int after = afterStack == null ? 0 : GetRemainingDurability(afterStack, maxAfter);
        bool broken = afterStack == null;

        if (afterStack != null && after <= 0)
        {
            bool removed = WearableToolResolver.RemoveBrokenSourceTool(sourceSlot);
            PlayWearableToolBreakSound(entity, sourceCode);
            afterStack = null;
            after = 0;
            broken = true;
            if (!removed)
            {
                entity.World.Logger.Warning("[animalicabodytools] Wearable durability reached zero but source slot did not clear: action={0}, source={1}.", action, sourceCode);
            }
        }

        if (DiagnosticsEnabled)
        {
            entity.World.Logger.VerboseDebug("[animalicabodytools] Wearable durability damage: action={0}, source={1}, amount={2}, before={3}/{4}, after={5}/{6}, broken={7}.",
                action,
                sourceCode,
                amount,
                before,
                maxBefore,
                after,
                maxAfter,
                broken);
            entity.World.Logger.VerboseDebug(
                "[animalicabodytools] Wearable source-slot lifecycle: action={0}, source={1}, slot={2}, before={3}, after={4}.",
                action,
                sourceCode,
                sourceSlot.GetType().FullName,
                beforeSlotState,
                DescribeSourceSlot(sourceSlot));
        }

        return true;
    }

    private static string DescribeSourceSlot(ItemSlot sourceSlot)
    {
        ItemStack? publicStack = sourceSlot.Itemstack;
        ItemStack? realStack = WearableToolResolver.GetRealItemstack(sourceSlot);
        static string Describe(ItemStack? stack) => stack == null
            ? "empty"
            : $"{stack.Collectible?.Code?.ToShortString() ?? "unknown"}x{stack.StackSize};durability={stack.Attributes?.GetInt("durability", -1) ?? -1}";
        return $"slotHash={sourceSlot.GetHashCode()};public={Describe(publicStack)};real={Describe(realStack)}";
    }

    private static void PlayWearableToolBreakSound(EntityAgent entity, string sourceCode)
    {
        try
        {
            entity.World.PlaySoundAt(new AssetLocation("sounds/player/destruct"), entity, null, false, 16f, 1f);
        }
        catch (Exception ex)
        {
            entity.World.Logger.Warning("[animalicabodytools] Wearable break sound failed: source={0}, error={1}: {2}", sourceCode, ex.GetType().Name, ex.Message);
        }
    }

    public static bool IsCuttableTallGrass(string blockCode)
    {
        return blockCode.StartsWith("tallgrass-", StringComparison.Ordinal)
               && blockCode.EndsWith("-free", StringComparison.Ordinal)
               && !blockCode.StartsWith("tallgrass-eaten-", StringComparison.Ordinal);
    }

    public static bool IsCuttableNormalReed(string blockCode)
    {
        return blockCode.StartsWith("tallplant-", StringComparison.Ordinal)
               && blockCode.Contains("-normal-", StringComparison.Ordinal);
    }

    public static bool IsBlockBreakDurabilitySuppressed => blockBreakDurabilitySuppressions > 0;

    public static void BeginBlockBreakDurabilitySuppression()
    {
        blockBreakDurabilitySuppressions++;
        if (blockBreakDurabilitySuppressions == 1)
        {
            suppressedLogBreakCount = 0;
        }
    }

    public static int EndBlockBreakDurabilitySuppression()
    {
        int count = suppressedLogBreakCount;
        if (blockBreakDurabilitySuppressions > 0)
        {
            blockBreakDurabilitySuppressions--;
        }

        if (blockBreakDurabilitySuppressions == 0)
        {
            suppressedLogBreakCount = 0;
        }

        return count;
    }

    public static void RecordSuppressedBlockBreak(string blockCode)
    {
        if (!IsBlockBreakDurabilitySuppressed)
        {
            return;
        }

        if (IsLogBlockCode(blockCode))
        {
            suppressedLogBreakCount++;
        }
    }

    public static bool IsLogBlockCode(string blockCode)
    {
        return blockCode.StartsWith("log-", StringComparison.Ordinal)
               || blockCode.StartsWith("bamboo-grown-", StringComparison.Ordinal);
    }

    public static void RecordTimedValue(Dictionary<string, long> cache, string key, long now, long retentionMs, int maximumEntries = 512)
    {
        cache[key] = now;
        if (cache.Count <= maximumEntries)
        {
            return;
        }

        foreach (string expiredKey in cache
                     .Where(entry => now - entry.Value > retentionMs)
                     .OrderBy(entry => entry.Value)
                     .Select(entry => entry.Key)
                     .Take(Math.Max(1, maximumEntries / 4))
                     .ToArray())
        {
            cache.Remove(expiredKey);
        }

        if (cache.Count > maximumEntries)
        {
            foreach (string oldestKey in cache.OrderBy(entry => entry.Value).Select(entry => entry.Key).Take(cache.Count - maximumEntries).ToArray())
            {
                cache.Remove(oldestKey);
            }
        }
    }

    public static bool TryLogOnce(HashSet<string> keys, string key, int maximumEntries = 1024)
    {
        if (keys.Count >= maximumEntries)
        {
            keys.Clear();
        }

        return keys.Add(key);
    }

    private static int GetRemainingDurability(ItemStack stack, int maxDurability)
    {
        return stack.Attributes?.GetInt("durability", maxDurability) ?? maxDurability;
    }

    public static float ScoreKnifeTool(ItemStack stack)
    {
        return stack.Collectible?.Tool == EnumTool.Knife ? 1f : 0f;
    }

    public static float ScoreReedCuttingTool(ItemStack stack, Block block, BlockSelection blockSel, IPlayer player)
    {
        EnumTool? tool = stack.Collectible?.Tool;
        if (tool != EnumTool.Knife && tool != EnumTool.Sickle && tool != EnumTool.Scythe)
        {
            return 0f;
        }

        float speed = stack.Collectible?.GetMiningSpeed(stack, blockSel, block, player) ?? 0f;
        return 1f + Math.Max(0f, speed);
    }

    public static float ScoreScytheTool(ItemStack stack, Block block, BlockSelection blockSel, IPlayer player)
    {
        if (stack.Collectible?.Tool != EnumTool.Scythe)
        {
            return 0f;
        }

        float speed = stack.Collectible.GetMiningSpeed(stack, blockSel, block, player);
        return 1f + Math.Max(0f, speed);
    }

    public static float ScoreProspectingPickTool(ItemStack stack, Block block, BlockSelection blockSel, IPlayer player)
    {
        if (stack.Collectible is not Vintagestory.GameContent.ItemProspectingPick)
        {
            return 0f;
        }

        float speed = stack.Collectible.GetMiningSpeed(stack, blockSel, block, player);
        return 1f + Math.Max(0f, speed);
    }

    public static System.Func<ItemStack, float> ScoreMiningTool(Block block, BlockSelection blockSel, IPlayer player)
    {
        return stack => stack.Collectible?.GetMiningSpeed(stack, blockSel, block, player) ?? 0f;
    }

}

[HarmonyPatch]
internal static class WearableCorpseHarvestPatch
{
    private const int HarvestAnimationDurationMs = 400;
    private static readonly HashSet<string> ActivationLogged = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> LastHarvestDamage = new(StringComparer.Ordinal);
    private static readonly HashSet<string> DuplicateLogged = new(StringComparer.Ordinal);
    private static readonly Dictionary<long, (int Generation, string Animation)> ActiveHarvestAnimations = new();

    private static MethodBase? TargetMethod()
    {
        Type? type = AccessTools.TypeByName("Vintagestory.GameContent.EntityBehaviorHarvestable");
        return type == null
            ? null
            : AccessTools.Method(type, "OnInteract", new[]
            {
                typeof(EntityAgent),
                typeof(ItemSlot),
                typeof(Vec3d),
                typeof(EnumInteractMode),
                typeof(EnumHandling).MakeByRefType()
            });
    }

    private static bool Prefix(object __instance, EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
    {
        if (mode != EnumInteractMode.Interact)
        {
            return true;
        }

        if (__instance is not IHarvestable harvestable)
        {
            return true;
        }

        if (byEntity is not EntityPlayer entityPlayer || entityPlayer.Player == null)
        {
            return true;
        }

        bool shift = byEntity.Controls?.ShiftKey == true;
        if (!WearableTargetedActionPatches.TryCreateVirtualDummySlot(byEntity, itemslot, WearableTargetedActionPatches.ScoreKnifeTool, out ItemStack virtualTool, out string sourceCode, out ItemSlot dummySlot, out ItemSlot sourceSlot, out _))
        {
            if (WearableTargetedActionPatches.DiagnosticsEnabled)
            {
                byEntity.World.Logger.VerboseDebug("[animalicabodytools] Wearable corpse harvest patch hit: resolved=no, shift={0}, mode={1}.", shift, mode);
            }
            return true;
        }

        bool harvestableOk = harvestable.IsHarvestable(dummySlot, byEntity);
        if (WearableTargetedActionPatches.DiagnosticsEnabled)
        {
            byEntity.World.Logger.VerboseDebug("[animalicabodytools] Wearable corpse harvest patch hit: resolved=yes, shift={0}, source={1}, virtual={2}, harvestable={3}.",
                shift,
                sourceCode,
                WearableTargetedActionPatches.DescribeTool(virtualTool, dummySlot),
                harvestableOk);
        }

        if (!shift || !harvestableOk)
        {
            return true;
        }

        handled = EnumHandling.PreventDefault;

        if (byEntity.World.Side == EnumAppSide.Server)
        {
            string harvestKey = $"{entityPlayer.PlayerUID}:{sourceCode}:{RuntimeHelpers.GetHashCode(__instance)}";
            long now = byEntity.World.ElapsedMilliseconds;
            bool duplicate = LastHarvestDamage.TryGetValue(harvestKey, out long last) && now - last < 1000;

            if (!duplicate)
            {
                LastHarvestDamage[harvestKey] = now;
                if (LastHarvestDamage.Count > 512)
                {
                    foreach (string oldKey in LastHarvestDamage.Where(entry => now - entry.Value > 5000).Select(entry => entry.Key).Take(128).ToArray())
                    {
                        LastHarvestDamage.Remove(oldKey);
                    }
                }

                harvestable.SetHarvested(entityPlayer.Player, 1f);
                WearableTargetedActionPatches.TryDamageSourceTool(byEntity, sourceSlot, sourceCode, "corpse-harvest");
            }
            else if (WearableTargetedActionPatches.DiagnosticsEnabled && WearableTargetedActionPatches.TryLogOnce(DuplicateLogged, harvestKey))
            {
                byEntity.World.Logger.VerboseDebug("[animalicabodytools] Wearable durability duplicate corpse-harvest charge skipped: player={0}, source={1}, harvestable={2}.",
                    entityPlayer.Player?.PlayerName,
                    sourceCode,
                    __instance.GetType().FullName);
            }

            if (WearableTargetedActionPatches.DiagnosticsEnabled && WearableTargetedActionPatches.TryLogOnce(ActivationLogged, entityPlayer.PlayerUID + ":" + sourceCode))
            {
                byEntity.World.Logger.VerboseDebug("[animalicabodytools] Wearable corpse harvest handled on server: player={0}, source={1}, virtual={2}.",
                    entityPlayer.Player?.PlayerName,
                    sourceCode,
                    virtualTool.Collectible?.Code);
            }
        }
        else
        {
            string animation = harvestable.HarvestAnimation;
            if (!string.IsNullOrWhiteSpace(animation))
            {
                PlaySingleHarvestAnimation(byEntity, animation, sourceCode);
            }
        }

        return false;
    }

    private static void PlaySingleHarvestAnimation(EntityAgent byEntity, string animation, string sourceCode)
    {
        long entityId = byEntity.EntityId;
        int generation = 1;

        if (ActiveHarvestAnimations.TryGetValue(entityId, out (int Generation, string Animation) previous))
        {
            generation = previous.Generation + 1;
            byEntity.StopAnimation(previous.Animation);
        }

        ActiveHarvestAnimations[entityId] = (generation, animation);
        byEntity.StopAnimation(animation);
        byEntity.StartAnimation(animation);

        if (WearableTargetedActionPatches.DiagnosticsEnabled)
        {
            byEntity.World.Logger.VerboseDebug(
                "[animalicabodytools] Wearable corpse harvest animation started: entity={0}, source={1}, animation={2}, generation={3}, stopAfterMs={4}.",
                entityId,
                sourceCode,
                animation,
                generation,
                HarvestAnimationDurationMs);
        }

        byEntity.World.RegisterCallback(_ =>
        {
            if (!ActiveHarvestAnimations.TryGetValue(entityId, out (int Generation, string Animation) current)
                || current.Generation != generation)
            {
                return;
            }

            byEntity.StopAnimation(current.Animation);
            ActiveHarvestAnimations.Remove(entityId);

            if (WearableTargetedActionPatches.DiagnosticsEnabled)
            {
                byEntity.World.Logger.VerboseDebug(
                    "[animalicabodytools] Wearable corpse harvest animation stopped: entity={0}, source={1}, animation={2}, generation={3}.",
                    entityId,
                    sourceCode,
                    current.Animation,
                    generation);
            }
        }, HarvestAnimationDurationMs);
    }
}

[HarmonyPatch(typeof(BEBehaviorFruitingBush), nameof(BEBehaviorFruitingBush.IsHarvestable))]
internal static class WearableFruitingBushCuttingHarvestablePatch
{
    private static readonly FieldInfo? YearsBetweenCuttingsField = AccessTools.Field(typeof(BEBehaviorFruitingBush), "yearsBetweenCuttings");
    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);

    private static void Postfix(BEBehaviorFruitingBush __instance, ItemSlot __0, Entity __1, ref bool __result)
    {
        ItemSlot itemslot = __0;
        Entity byEntity = __1;

        if (__result || byEntity == null)
        {
            return;
        }

        if (byEntity is not EntityAgent entityAgent)
        {
            return;
        }

        if (entityAgent.Controls?.ShiftKey != true)
        {
            return;
        }

        if (!WearableTargetedActionPatches.TryCreateVirtualDummySlot(entityAgent, itemslot, WearableTargetedActionPatches.ScoreKnifeTool, out ItemStack virtualTool, out string sourceCode, out ItemSlot dummySlot, out _, out _))
        {
            return;
        }

        if (virtualTool.Collectible?.GetTool(dummySlot) != EnumTool.Knife)
        {
            return;
        }

        double yearsBetweenCuttings = YearsBetweenCuttingsField?.GetValue(__instance) is double value ? value : 1d;
        double daysSinceLastCutting = byEntity.World.Calendar.TotalDays - __instance.BState.LastCuttingTakenTotalDays;
        double yearsSinceLastCutting = daysSinceLastCutting / Math.Max(1, byEntity.World.Calendar.DaysPerYear);

        __result = yearsSinceLastCutting >= yearsBetweenCuttings;

        if (WearableTargetedActionPatches.DiagnosticsEnabled && WearableTargetedActionPatches.TryLogOnce(Logged, $"{byEntity.EntityId}:{sourceCode}:{__result}"))
        {
            byEntity.World.Logger.VerboseDebug("[animalicabodytools] Wearable fruiting-bush cutting harvestability: resolved=yes, source={0}, virtual={1}, yearsSinceLast={2:0.###}, yearsRequired={3:0.###}, harvestable={4}.",
                sourceCode,
                WearableTargetedActionPatches.DescribeTool(virtualTool, dummySlot),
                yearsSinceLastCutting,
                yearsBetweenCuttings,
                __result);
        }
    }
}

[HarmonyPatch(typeof(BEBehaviorFruitingBush), nameof(BEBehaviorFruitingBush.OnBlockInteractStart))]
internal static class WearableFruitingBushCuttingInteractPatch
{
    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);
    private static readonly HashSet<string> FailedLogged = new(StringComparer.Ordinal);

    private static bool Prefix(BEBehaviorFruitingBush __instance, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling, ref bool __result)
    {
        if (byPlayer?.Entity == null || blockSel?.Position == null || byPlayer.Entity.Controls?.ShiftKey != true)
        {
            return true;
        }

        ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
        if (activeSlot == null)
        {
            return true;
        }

        if (!WearableTargetedActionPatches.TryCreateVirtualDummySlot(byPlayer, activeSlot, WearableTargetedActionPatches.ScoreKnifeTool, out ItemStack virtualTool, out string sourceCode, out ItemSlot dummySlot, out _, out _))
        {
            if (WearableTargetedActionPatches.DiagnosticsEnabled && WearableTargetedActionPatches.TryLogOnce(FailedLogged, $"{byPlayer.PlayerUID}:resolve"))
            {
                world.Logger.VerboseDebug("[animalicabodytools] Wearable fruiting-bush cutting interact hit: resolved=no, player={0}.", byPlayer.PlayerName);
            }

            return true;
        }

        if (virtualTool.Collectible?.GetTool(dummySlot) != EnumTool.Knife)
        {
            return true;
        }

        bool harvestable = __instance.IsHarvestable(dummySlot, byPlayer.Entity);
        if (!harvestable)
        {
            if (WearableTargetedActionPatches.DiagnosticsEnabled && WearableTargetedActionPatches.TryLogOnce(FailedLogged, $"{byPlayer.PlayerUID}:{sourceCode}:not-harvestable"))
            {
                world.Logger.VerboseDebug("[animalicabodytools] Wearable fruiting-bush cutting interact declined: player={0}, source={1}, virtual={2}, harvestable=false.",
                    byPlayer.PlayerName,
                    sourceCode,
                    WearableTargetedActionPatches.DescribeTool(virtualTool, dummySlot));
            }

            return true;
        }

        handling = EnumHandling.PreventDefault;
        __result = true;

        AssetLocation sound = __instance.HarvestableSound;
        if (sound != null)
        {
            Vec3d pos = blockSel.Position.ToVec3d().Add(0.5, 0, 0.5);
            world.PlaySoundAt(sound, pos.X, pos.Y, pos.Z, byPlayer, false, 12f, 1f);
        }

        if (world.Side == EnumAppSide.Server)
        {
            __instance.SetHarvested(byPlayer, 1f);
        }

        if (WearableTargetedActionPatches.DiagnosticsEnabled && WearableTargetedActionPatches.TryLogOnce(Logged, $"{byPlayer.PlayerUID}:{sourceCode}:{blockSel.Position}"))
        {
            world.Logger.VerboseDebug("[animalicabodytools] Wearable fruiting-bush cutting interact handled: side={0}, player={1}, source={2}, virtual={3}, pos={4}.",
                world.Side,
                byPlayer.PlayerName,
                sourceCode,
                virtualTool.Collectible?.Code,
                blockSel.Position);
        }

        return false;
    }
}

[HarmonyPatch(typeof(BEBehaviorFruitingBush), nameof(BEBehaviorFruitingBush.SetHarvested))]
internal static class WearableFruitingBushCuttingDurabilityPatch
{
    private static readonly Dictionary<string, long> LastDamage = new(StringComparer.Ordinal);
    private static readonly HashSet<string> DuplicateLogged = new(StringComparer.Ordinal);

    private static void Postfix(BEBehaviorFruitingBush __instance, IPlayer byPlayer, float dropQuantityMultiplier)
    {
        if (byPlayer?.Entity == null || byPlayer.Entity.World.Side != EnumAppSide.Server)
        {
            return;
        }

        ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
        if (activeSlot == null)
        {
            return;
        }

        if (!WearableTargetedActionPatches.TryCreateVirtualDummySlot(byPlayer, activeSlot, WearableTargetedActionPatches.ScoreKnifeTool, out ItemStack virtualTool, out string sourceCode, out ItemSlot dummySlot, out ItemSlot sourceSlot, out _))
        {
            return;
        }

        if (virtualTool.Collectible?.GetTool(dummySlot) != EnumTool.Knife)
        {
            return;
        }

        string key = $"{byPlayer.PlayerUID}:{sourceCode}:{RuntimeHelpers.GetHashCode(__instance)}";
        long now = byPlayer.Entity.World.ElapsedMilliseconds;
        if (LastDamage.TryGetValue(key, out long last) && now - last < 1000)
        {
            if (WearableTargetedActionPatches.DiagnosticsEnabled && WearableTargetedActionPatches.TryLogOnce(DuplicateLogged, key))
            {
                byPlayer.Entity.World.Logger.VerboseDebug("[animalicabodytools] Wearable durability duplicate fruiting-bush cutting charge skipped: player={0}, source={1}.",
                    byPlayer.PlayerName,
                    sourceCode);
            }

            return;
        }

        LastDamage[key] = now;
        if (LastDamage.Count > 512)
        {
            foreach (string oldKey in LastDamage.Where(entry => now - entry.Value > 5000).Select(entry => entry.Key).Take(128).ToArray())
            {
                LastDamage.Remove(oldKey);
            }
        }

        WearableTargetedActionPatches.TryDamageSourceTool(byPlayer.Entity, sourceSlot, sourceCode, "fruitingbush-cutting");
        if (WearableTargetedActionPatches.DiagnosticsEnabled)
        {
            byPlayer.Entity.World.Logger.VerboseDebug("[animalicabodytools] Wearable fruiting-bush cutting handled: player={0}, source={1}, virtual={2}.",
                byPlayer.PlayerName,
                sourceCode,
                virtualTool.Collectible?.Code);
        }
    }
}

[HarmonyPatch(typeof(BlockBerryBush), nameof(BlockBerryBush.OnBlockInteractStart))]
internal static class WearableLegacyBerryBushPrunePatch
{
    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);

    private static bool Prefix(BlockBerryBush __instance, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref bool __result)
    {
        if (byPlayer?.Entity == null || blockSel?.Position == null || byPlayer.Entity.Controls?.ShiftKey != true)
        {
            return true;
        }

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityBerryBush berryBush || berryBush.Pruned)
        {
            return true;
        }

        ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
        if (activeSlot == null)
        {
            return true;
        }

        if (!WearableTargetedActionPatches.TryCreateVirtualDummySlot(byPlayer, activeSlot, WearableTargetedActionPatches.ScoreKnifeTool, out ItemStack virtualTool, out string sourceCode, out ItemSlot dummySlot, out ItemSlot sourceSlot, out _))
        {
            return true;
        }

        if (virtualTool.Collectible?.GetTool(dummySlot) != EnumTool.Knife)
        {
            return true;
        }

        berryBush.Prune();
        WearableTargetedActionPatches.TryDamageSourceTool(byPlayer.Entity, sourceSlot, sourceCode, "legacy-berrybush-prune");

        if (WearableTargetedActionPatches.DiagnosticsEnabled && WearableTargetedActionPatches.TryLogOnce(Logged, $"{byPlayer.PlayerUID}:{sourceCode}:{__instance.Code}"))
        {
            world.Logger.VerboseDebug("[animalicabodytools] Wearable legacy berry-bush prune handled: player={0}, block={1}, source={2}, virtual={3}.",
                byPlayer.PlayerName,
                __instance.Code,
                sourceCode,
                virtualTool.Collectible?.Code);
        }

        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Block), nameof(Block.OnGettingBroken))]
internal static class WearableBlockGettingBrokenPatch
{
    private static readonly HashSet<string> LoggedBlocks = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> LastSoundMs = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> ScytheBreakStartedMs = new(StringComparer.Ordinal);
    private const long WearableScytheChargeMs = 1050;

    private static bool Prefix(Block __instance, IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter, ref float __result)
    {
        // SleepNeed owns the temporary ground-bed decision in its optional
        // Block.OnGettingBroken prefix. Let that prefix see the original call
        // instead of turning a fitted tool into a bypass around it.
        if (SleepNeedOwnsGroundBedBuild(player))
        {
            return true;
        }

        if (!WearableTargetedActionPatches.TryCreateVirtualMiningDummySlot(player, itemslot, __instance, blockSel, out ItemStack virtualTool, out string sourceCode, out ItemSlot dummySlot, out _, out float selectedScore))
        {
            return true;
        }

        if (WearableTargetedActionPatches.DiagnosticsEnabled)
        {
            string blockCode = __instance.Code?.ToShortString() ?? "unknown";
            if (WearableTargetedActionPatches.TryLogOnce(LoggedBlocks, $"{player.PlayerUID}:{blockCode}:{sourceCode}"))
            {
                float miningSpeed = virtualTool.Collectible?.GetMiningSpeed(virtualTool, blockSel, __instance, player) ?? 0f;
                string tool = virtualTool.Collectible?.GetTool(dummySlot)?.ToString() ?? "none";
                int tier = virtualTool.Collectible?.GetToolTier(dummySlot) ?? 0;
                int requiredTier = 0;
                try
                {
                    requiredTier = __instance.GetRequiredMiningTier(player.Entity.World, blockSel.Position);
                }
                catch
                {
                    // Diagnostic context only.
                }

                player.Entity.World.Logger.VerboseDebug("[animalicabodytools] Wearable block breaking patch hit: block={0}, source={1}, virtual={2}, tool={3}, tier={4}, requiredTier={5}, miningSpeed={6}.",
                    blockCode,
                    sourceCode,
                    virtualTool.Collectible?.Code,
                    tool,
                    tier,
                    requiredTier,
                    miningSpeed);
            }
        }

        if (virtualTool.Collectible is AnimalicaBodyTools.Items.ItemSpeciesLockedScythe scythe && scythe.CanMultiBreak(__instance))
        {
            ApplyScytheBreakDelay(__instance, player, blockSel, virtualTool, sourceCode, remainingResistance, dt, counter, ref __result);
            return false;
        }

        if (dt > 0)
        {
            foreach (BlockBehavior behavior in __instance.BlockBehaviors)
            {
                dt *= behavior.GetMiningSpeedModifier(player.Entity.World, blockSel.Position, player);
            }
        }

        __result = virtualTool.Collectible?.OnBlockBreaking(player, blockSel, dummySlot, remainingResistance, dt, counter) ?? remainingResistance;
        PlayVirtualBreakSound(__instance, player, blockSel, virtualTool, __result);
        return false;
    }

    private static bool SleepNeedOwnsGroundBedBuild(IPlayer player)
    {
        if (player?.Entity?.Controls?.FloorSitting != true
            || !player.Entity.Api.ModLoader.IsModEnabled("sleepneed"))
        {
            return false;
        }

        ITreeAttribute? energy = player.Entity.WatchedAttributes.GetTreeAttribute("sleepneed:energy");
        ITreeAttribute? sleepiness = player.Entity.WatchedAttributes.GetTreeAttribute("sleepneed:sleepiness");
        return energy?.GetBool("buildinggroundbed") == true
               || sleepiness?.GetBool("buildinggroundbed") == true;
    }

    internal static void ApplyScytheBreakDelay(
        Block block,
        IPlayer player,
        BlockSelection blockSel,
        ItemStack virtualTool,
        string sourceCode,
        float remainingResistance,
        float dt,
        int counter,
        ref float result)
    {
        long now = player.Entity.World.ElapsedMilliseconds;
        BlockPos pos = blockSel.Position;
        int toolMode = virtualTool.Attributes?.GetInt("toolMode", 0) ?? 0;
        string virtualCode = virtualTool.Collectible?.Code?.ToShortString() ?? "unknown";
        string delayKey = $"{player.PlayerUID}:{pos.dimension}:{pos.X}:{pos.InternalY}:{pos.Z}:{sourceCode}:{virtualCode}:{toolMode}";

        if (counter <= 1 || !ScytheBreakStartedMs.TryGetValue(delayKey, out long startedMs) || now - startedMs > 4000)
        {
            startedMs = now;
            WearableTargetedActionPatches.RecordTimedValue(ScytheBreakStartedMs, delayKey, startedMs, 5000);
        }

        long elapsedMs = now - startedMs;
        if (elapsedMs < WearableScytheChargeMs)
        {
            result = Math.Max(0.05f, remainingResistance - (dt * 0.15f));
            PlayVirtualScytheChargeSound(block, player, blockSel, elapsedMs);
            return;
        }

        ScytheBreakStartedMs.Remove(delayKey);
        result = 0f;
        PlayVirtualBreakSound(block, player, blockSel, virtualTool, result);
    }

    private static void PlayVirtualScytheChargeSound(Block block, IPlayer player, BlockSelection blockSel, long elapsedMs)
    {
        if (elapsedMs < 700)
        {
            return;
        }

        try
        {
            IWorldAccessor world = player.Entity.World;
            long now = world.ElapsedMilliseconds;
            string key = $"{player.PlayerUID}:{blockSel.Position}:scythe";
            if (LastSoundMs.TryGetValue(key, out long last) && now - last <= 900)
            {
                return;
            }

            WearableTargetedActionPatches.RecordTimedValue(LastSoundMs, key, now, 5000);
            world.PlaySoundAt(new AssetLocation("sounds/tool/scythe1"), player.Entity, player, false, 16f, 1f);
        }
        catch (Exception ex)
        {
            player.Entity.World.Logger.Warning("[animalicabodytools] Wearable scythe charge sound failed: {0}: {1}", ex.GetType().Name, ex.Message);
        }
    }

    private static void PlayVirtualBreakSound(Block block, IPlayer player, BlockSelection blockSel, ItemStack virtualTool, float remainingResistance)
    {
        try
        {
            IWorldAccessor world = player.Entity.World;
            long now = world.ElapsedMilliseconds;
            string key = $"{player.PlayerUID}:{blockSel.Position}";
            bool isBreak = remainingResistance <= 0;

            if (!isBreak && LastSoundMs.TryGetValue(key, out long last) && now - last <= 225)
            {
                return;
            }

            WearableTargetedActionPatches.RecordTimedValue(LastSoundMs, key, now, 5000);

            BlockSounds sounds = block.GetSounds(world.BlockAccessor, blockSel, virtualTool);
            SoundAttributes sound = isBreak ? sounds.GetBreakSound(player) : sounds.GetHitSound(player);
            if (sound == null)
            {
                return;
            }

            Vec3d hit = blockSel.HitPosition ?? new Vec3d(0.5, 0.5, 0.5);
            double x = blockSel.Position.X + hit.X;
            double y = blockSel.Position.InternalY + hit.Y;
            double z = blockSel.Position.Z + hit.Z;

            world.PlaySoundAt(sound, x, y, z, blockSel.Position.dimension, player, 1f);
        }
        catch (Exception ex)
        {
            player.Entity.World.Logger.Warning("[animalicabodytools] Wearable block sound patch failed: {0}: {1}", ex.GetType().Name, ex.Message);
        }
    }
}

[HarmonyPatch(typeof(BlockReeds), nameof(BlockReeds.OnGettingBroken))]
internal static class WearableReedsGettingBrokenPatch
{
    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> LastSoundMs = new(StringComparer.Ordinal);

    private static bool Prefix(BlockReeds __instance, IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter, ref float __result)
    {
        if (player?.Entity == null || blockSel?.Position == null)
        {
            return true;
        }

        string blockCode = __instance.Code?.ToShortString() ?? "unknown";
        string state = __instance.Variant["state"];
        bool isNormal = string.Equals(state, "normal", StringComparison.Ordinal);
        bool isHarvested = string.Equals(state, "harvested", StringComparison.Ordinal);
        if (!isNormal && !isHarvested)
        {
            return true;
        }

        System.Func<ItemStack, float> scoreSelector = isNormal
            ? stack => WearableTargetedActionPatches.ScoreReedCuttingTool(stack, __instance, blockSel, player)
            : WearableTargetedActionPatches.ScoreMiningTool(__instance, blockSel, player);

        if (!WearableTargetedActionPatches.TryCreateVirtualDummySlot(player, itemslot, scoreSelector, out ItemStack virtualTool, out string sourceCode, out ItemSlot dummySlot, out _, out float selectedScore))
        {
            return true;
        }

        if (isNormal)
        {
            EnumTool? tool = virtualTool.Collectible?.GetTool(dummySlot);
            if (tool != EnumTool.Knife && tool != EnumTool.Sickle && tool != EnumTool.Scythe)
            {
                return true;
            }
        }

        if (selectedScore <= 0f)
        {
            return true;
        }

        // BlockReeds overrides Block.OnGettingBroken, so the generic scythe
        // delay patch never sees normal papyrus, cattails, or tule. Apply the
        // same vanilla-length charge here before reed-specific speed handling.
        if (isNormal
            && virtualTool.Collectible is AnimalicaBodyTools.Items.ItemSpeciesLockedScythe scythe
            && scythe.CanMultiBreak(__instance))
        {
            WearableBlockGettingBrokenPatch.ApplyScytheBreakDelay(
                __instance,
                player,
                blockSel,
                virtualTool,
                sourceCode,
                remainingResistance,
                dt,
                counter,
                ref __result);
            return false;
        }

        float adjustedDt = dt;
        if (isNormal)
        {
            float miningSpeed = virtualTool.Collectible?.GetMiningSpeed(virtualTool, blockSel, __instance, player) ?? 0f;
            adjustedDt *= Math.Max(1f, miningSpeed);
        }
        else
        {
            float miningSpeed = virtualTool.Collectible?.GetMiningSpeed(virtualTool, blockSel, __instance, player) ?? selectedScore;
            adjustedDt *= Math.Max(0.5f, miningSpeed);
        }

        __result = __instance.GetRequiredMiningTier(player.Entity.World, blockSel.Position) != 0
            ? remainingResistance
            : remainingResistance - adjustedDt;

        PlayReedSound(__instance, player, blockSel, __result);

        if (WearableTargetedActionPatches.DiagnosticsEnabled && WearableTargetedActionPatches.TryLogOnce(Logged, $"{player.PlayerUID}:{blockCode}:{sourceCode}:{isNormal}:{isHarvested}"))
        {
            player.Entity.World.Logger.VerboseDebug("[animalicabodytools] Wearable reeds breaking patch hit: block={0}, state={1}, source={2}, virtual={3}, score={4}, adjustedDt={5}, remaining={6}.",
                blockCode,
                state,
                sourceCode,
                WearableTargetedActionPatches.DescribeTool(virtualTool, dummySlot),
                selectedScore,
                adjustedDt,
                __result);
        }

        return false;
    }

    private static void PlayReedSound(Block block, IPlayer player, BlockSelection blockSel, float remainingResistance)
    {
        try
        {
            IWorldAccessor world = player.Entity.World;
            long now = world.ElapsedMilliseconds;
            string key = $"{player.PlayerUID}:{blockSel.Position}";
            bool isBreak = remainingResistance <= 0;

            if (!isBreak && LastSoundMs.TryGetValue(key, out long last) && now - last <= 225)
            {
                return;
            }

            WearableTargetedActionPatches.RecordTimedValue(LastSoundMs, key, now, 5000);
            SoundAttributes sound = isBreak ? block.Sounds.GetBreakSound(player) : block.Sounds.GetHitSound(player);
            if (sound == null)
            {
                return;
            }

            Vec3d hit = blockSel.HitPosition ?? new Vec3d(0.5, 0.5, 0.5);
            world.PlaySoundAt(sound,
                blockSel.Position.X + hit.X,
                blockSel.Position.InternalY + hit.Y,
                blockSel.Position.Z + hit.Z,
                blockSel.Position.dimension,
                player,
                1f);
        }
        catch (Exception ex)
        {
            player.Entity.World.Logger.Warning("[animalicabodytools] Wearable reeds sound patch failed: {0}: {1}", ex.GetType().Name, ex.Message);
        }
    }
}

[HarmonyPatch(typeof(BlockReeds), nameof(BlockReeds.OnBlockBroken))]
internal static class WearableReedsKnifeHarvestPatch
{
    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);

    private static bool Prefix(BlockReeds __instance, IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier)
    {
        if (world.Side != EnumAppSide.Server || byPlayer?.Entity == null)
        {
            return true;
        }

        string blockCode = __instance.Code?.ToShortString() ?? string.Empty;
        if (!WearableTargetedActionPatches.IsCuttableNormalReed(blockCode))
        {
            return true;
        }

        ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
        if (activeSlot == null)
        {
            return true;
        }

        BlockSelection blockSel = new(pos, BlockFacing.UP, __instance)
        {
            HitPosition = new Vec3d(0.5, 0.5, 0.5)
        };

        if (!WearableTargetedActionPatches.TryCreateVirtualDummySlot(
                byPlayer,
                activeSlot,
                stack => WearableTargetedActionPatches.ScoreReedCuttingTool(stack, __instance, blockSel, byPlayer),
                out ItemStack virtualTool,
                out string sourceCode,
                out ItemSlot dummySlot,
                out ItemSlot sourceSlot,
                out float selectedScore))
        {
            return true;
        }

        EnumTool? tool = virtualTool.Collectible?.GetTool(dummySlot);
        if (tool != EnumTool.Knife && tool != EnumTool.Sickle)
        {
            return true;
        }

        foreach (BlockDropItemStack drop in __instance.Drops ?? Array.Empty<BlockDropItemStack>())
        {
            ItemStack? stack = drop.GetNextItemStack(1f);
            if (stack != null)
            {
                world.SpawnItemEntity(stack, pos, null);
            }
        }

        SoundAttributes sound = __instance.Sounds.GetBreakSound(byPlayer);
        if (sound != null)
        {
            world.PlaySoundAt(sound, pos, -0.5, byPlayer, 1f);
        }

        Block? harvestedBlock = world.GetBlock(__instance.CodeWithVariants(new[] { "habitat", "state" }, new[] { "land", "harvested" }));
        if (harvestedBlock == null)
        {
            return true;
        }

        world.BlockAccessor.SetBlock(harvestedBlock.BlockId, pos);
        WearableTargetedActionPatches.TryDamageSourceTool(byPlayer.Entity, sourceSlot, sourceCode, "reeds-cut:" + blockCode);

        if (WearableTargetedActionPatches.DiagnosticsEnabled && WearableTargetedActionPatches.TryLogOnce(Logged, $"{byPlayer.PlayerUID}:{blockCode}:{sourceCode}"))
        {
            world.Logger.VerboseDebug("[animalicabodytools] Wearable reeds knife harvest handled: player={0}, block={1}, replacement={2}, source={3}, virtual={4}, score={5}.",
                byPlayer.PlayerName,
                blockCode,
                harvestedBlock.Code,
                sourceCode,
                virtualTool.Collectible?.Code,
                selectedScore);
        }

        return false;
    }
}

[HarmonyPatch(typeof(Block), nameof(Block.OnBlockBroken))]
internal static class WearableTallGrassKnifePatch
{
    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);

    private static bool Prefix(Block __instance, IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier)
    {
        if (world.Side != EnumAppSide.Server || byPlayer?.Entity == null)
        {
            return true;
        }

        string blockCode = __instance.Code?.ToShortString() ?? string.Empty;
        if (!WearableTargetedActionPatches.IsCuttableTallGrass(blockCode))
        {
            return true;
        }

        ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
        if (activeSlot == null)
        {
            return true;
        }

        BlockSelection blockSel = new(pos, BlockFacing.UP, __instance)
        {
            HitPosition = new Vec3d(0.5, 0.5, 0.5)
        };

        if (!WearableTargetedActionPatches.TryCreateVirtualDummySlot(
                byPlayer,
                activeSlot,
                stack => WearableTargetedActionPatches.ScoreReedCuttingTool(stack, __instance, blockSel, byPlayer),
                out ItemStack virtualTool,
                out string sourceCode,
                out ItemSlot dummySlot,
                out ItemSlot sourceSlot,
                out _))
        {
            return true;
        }

        EnumTool? tool = virtualTool.Collectible?.GetTool(dummySlot);
        if (tool != EnumTool.Knife && tool != EnumTool.Sickle)
        {
            return true;
        }

        Item? dryGrass = world.GetItem(new AssetLocation("drygrass"));
        if (dryGrass != null)
        {
            int quantity = Math.Max(1, (int)MathF.Round(dropQuantityMultiplier));
            Vec3d posVec = new(pos.X + 0.5, pos.InternalY + 0.5, pos.Z + 0.5);
            world.SpawnItemEntity(new ItemStack(dryGrass, quantity), posVec, new Vec3d(0, 0.05, 0));
        }

        Block? eatenGrass = world.GetBlock(new AssetLocation("tallgrass-eaten-free"));
        if (eatenGrass == null)
        {
            return true;
        }

        world.BlockAccessor.SetBlock(eatenGrass.BlockId, pos);
        WearableTargetedActionPatches.TryDamageSourceTool(byPlayer.Entity, sourceSlot, sourceCode, "tallgrass-cut");

        if (WearableTargetedActionPatches.DiagnosticsEnabled && WearableTargetedActionPatches.TryLogOnce(Logged, $"{byPlayer.PlayerUID}:{blockCode}:{sourceCode}"))
        {
            world.Logger.VerboseDebug("[animalicabodytools] Wearable tallgrass cutting patch handled: block={0}, source={1}, virtual={2}, tool={3}, drop=drygrass, replacement={4}.",
                blockCode,
                sourceCode,
                virtualTool.Collectible?.Code,
                tool,
                eatenGrass.Code);
        }

        return false;
    }
}

[HarmonyPatch(typeof(Block), nameof(Block.OnBlockBroken))]
internal static class WearableBlockBrokenDurabilityPatch
{
    private static readonly Dictionary<string, long> LastDamageByBreak = new(StringComparer.Ordinal);
    private static readonly HashSet<string> DuplicateLogged = new(StringComparer.Ordinal);

    private static bool Prefix(Block __instance, IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier)
    {
        if (world.Side != EnumAppSide.Server || byPlayer?.Entity == null)
        {
            return true;
        }

        string blockCode = __instance.Code?.ToShortString() ?? string.Empty;
        if (WearableTargetedActionPatches.IsCuttableTallGrass(blockCode)
            || WearableTargetedActionPatches.IsCuttableNormalReed(blockCode))
        {
            return true;
        }

        if (WearableTargetedActionPatches.IsBlockBreakDurabilitySuppressed)
        {
            WearableTargetedActionPatches.RecordSuppressedBlockBreak(blockCode);
            return true;
        }

        ItemSlot? activeSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
        if (activeSlot == null)
        {
            return true;
        }

        BlockSelection blockSel = new(pos, BlockFacing.UP, __instance)
        {
            HitPosition = new Vec3d(0.5, 0.5, 0.5)
        };

        if (!WearableTargetedActionPatches.TryCreateVirtualDummySlot(byPlayer, activeSlot, WearableTargetedActionPatches.ScoreMiningTool(__instance, blockSel, byPlayer), out ItemStack virtualTool, out string sourceCode, out _, out ItemSlot sourceSlot, out float selectedScore))
        {
            return true;
        }

        string breakKey = $"{byPlayer.PlayerUID}:{pos.dimension}:{pos.X}:{pos.InternalY}:{pos.Z}:{blockCode}:{sourceCode}";
        long now = world.ElapsedMilliseconds;
        if (LastDamageByBreak.TryGetValue(breakKey, out long last) && now - last < 1000)
        {
            if (WearableTargetedActionPatches.DiagnosticsEnabled && WearableTargetedActionPatches.TryLogOnce(DuplicateLogged, breakKey))
            {
                world.Logger.VerboseDebug("[animalicabodytools] Wearable durability duplicate block-break charge skipped: block={0}, source={1}, pos={2}.",
                    blockCode,
                    sourceCode,
                    pos);
            }

            return true;
        }

        LastDamageByBreak[breakKey] = now;
        if (LastDamageByBreak.Count > 512)
        {
            foreach (string oldKey in LastDamageByBreak.Where(entry => now - entry.Value > 5000).Select(entry => entry.Key).Take(128).ToArray())
            {
                LastDamageByBreak.Remove(oldKey);
            }
        }

        string tool = virtualTool.Collectible?.Tool.ToString() ?? "none";
        bool durabilityCharged = WearableTargetedActionPatches.TryDamageSourceTool(byPlayer.Entity, sourceSlot, sourceCode, "block-break:" + blockCode + ":" + tool + ":" + selectedScore);
        if (durabilityCharged)
        {
            WearableBlockBreakToolContext.MarkDurabilityCharged(byPlayer, pos);
        }
        return true;
    }
}

[HarmonyPatch]
internal static class WearableServerBreakTierPatch
{
    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);

    private static MethodBase? TargetMethod()
    {
        Type? type = AccessTools.TypeByName("Vintagestory.Server.ServerSystemBlockSimulation");
        if (type == null)
        {
            return null;
        }

        return type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method => method.Name == "TryModifyBlockInWorld" && method.GetParameters().Length == 2);
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> codes = instructions.ToList();
        MethodInfo requiredTierMethod = AccessTools.Method(
            typeof(Block),
            nameof(Block.GetRequiredMiningTier),
            new[] { typeof(IWorldAccessor), typeof(BlockPos) });
        MethodInfo helper = AccessTools.Method(typeof(WearableServerBreakTierPatch), nameof(AdjustRequiredMiningTier));
        int requiredTierIndex = codes.FindIndex(code => code.Calls(requiredTierMethod));
        if (requiredTierIndex < 0)
        {
            return codes;
        }

        codes.InsertRange(requiredTierIndex + 1, new[]
        {
            new CodeInstruction(OpCodes.Ldarg_1),
            new CodeInstruction(OpCodes.Call, helper)
        });

        return codes;
    }

    public static int AdjustRequiredMiningTier(int requiredTier, object serverPlayer)
    {
        if (requiredTier <= 0 || serverPlayer is not IServerPlayer player)
        {
            return requiredTier;
        }

        ItemSlot? activeSlot = player.InventoryManager?.ActiveHotbarSlot;
        if (activeSlot == null || !activeSlot.Empty)
        {
            return requiredTier;
        }

        WearableBlockBreakContext? context = WearableBlockBreakToolContext.Current;
        if (context == null || !ReferenceEquals(context.Player, player))
        {
            return requiredTier;
        }

        BlockSelection selection = new(context.Position, BlockFacing.UP, context.Block)
        {
            HitPosition = new Vec3d(0.5, 0.5, 0.5)
        };

        if (!WearableTargetedActionPatches.TryCreateVirtualMiningDummySlot(
                player,
                activeSlot,
                context.Block,
                selection,
                out ItemStack virtualTool,
                out string sourceCode,
                out ItemSlot dummySlot,
                out _,
                out float selectedScore))
        {
            return requiredTier;
        }

        int virtualTier = virtualTool.Collectible?.GetToolTier(dummySlot) ?? 0;
        if (virtualTier < requiredTier)
        {
            return requiredTier;
        }

        string tool = virtualTool.Collectible?.GetTool(dummySlot)?.ToString() ?? "none";
        string key = $"{player.PlayerUID}:{context.Block.Code}:{sourceCode}:{requiredTier}:{virtualTier}";
        if (WearableTargetedActionPatches.DiagnosticsEnabled && WearableTargetedActionPatches.TryLogOnce(Logged, key))
        {
            player.Entity.World.Logger.VerboseDebug("[animalicabodytools] Wearable block tier validation accepted: block={0}, source={1}, virtual={2}, tool={3}, requiredTier={4}, virtualTier={5}, miningScore={6}.",
                context.Block.Code,
                sourceCode,
                virtualTool.Collectible?.Code,
                tool,
                requiredTier,
                virtualTier,
                selectedScore);
        }

        return 0;
    }
}
