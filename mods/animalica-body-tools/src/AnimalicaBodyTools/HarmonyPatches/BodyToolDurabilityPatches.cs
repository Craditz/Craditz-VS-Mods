using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using AnimalicaBodyTools.Items;
using AnimalicaBodyTools.Systems;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace AnimalicaBodyTools.HarmonyPatches;

[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.DamageItem))]
internal static class BodyToolDamagePatch
{
    // A retained/equivalent wrapper can come from another mod load context.
    // Keep the event scope thread-local in shared BCL state, with engine/BCL
    // values only, rather than a context-specific static record instance.
    private static readonly LocalDataStoreSlot TraceSlot = Thread.GetNamedDataSlot("animalicabodytools.damage-sound-scope.v1");
    private static object?[]? Current
    {
        get => Thread.GetData(TraceSlot) as object?[];
        set => Thread.SetData(TraceSlot, value);
    }

    internal sealed record DamageTrace(IWorldAccessor World, ItemSlot Slot, ItemStack Stack, int Before, object?[]? Previous);

    private static void Prefix(CollectibleObject __instance, IWorldAccessor world, ItemSlot itemSlot,
        int amount, bool destroyOnZeroDurability, out DamageTrace? __state)
    {
        __state = null;
        if (amount <= 0 || !destroyOnZeroDurability
            || !BodyToolDurability.IsBodyTool(__instance)) return;
        ItemStack? stack = WearableToolResolver.GetRealItemstack(itemSlot);
        if (!ReferenceEquals(stack?.Collectible, __instance)) return;
        __state = new DamageTrace(world, itemSlot, stack!, __instance.GetRemainingDurability(stack!), Current);
        Current = new object?[] { world, itemSlot, stack };
    }

    // Let vanilla/behaviors charge exactly once, then finish the destruction that
    // Wearable suppresses. An engine refill or behavior replacement is never ours.
    private static void Postfix(IWorldAccessor world, Entity byEntity, ItemSlot itemSlot, DamageTrace? __state)
    {
        if (__state != null) BodyToolDurability.RemoveAfterDamage(world, byEntity, itemSlot, __state.Stack, __state.Before);
    }

    private static Exception? Finalizer(Exception? __exception, DamageTrace? __state)
    {
        if (__state != null) Current = __state.Previous;
        return __exception;
    }

    internal static bool OwnsWearableSound(IWorldAccessor world, ItemSlot slot) => Current is { Length: 3 } trace
        && ReferenceEquals(trace[0], world) && ReferenceEquals(trace[1], slot)
        && ReferenceEquals(WearableToolResolver.GetRealItemstack(slot), trace[2]);
}

internal static class BodyToolUseGuard
{
    private static readonly Type[] Types = { typeof(ItemSpeciesLockedBodyTool), typeof(ItemSpeciesLockedClaws),
        typeof(ItemSpeciesLockedCultivatingClaws), typeof(ItemSpeciesLockedProspectingPick),
        typeof(ItemSpeciesLockedRendingClaws), typeof(ItemSpeciesLockedScythe) };

    public static IEnumerable<MethodBase> Methods(params string[] names) => Types
        .SelectMany(type => names.Select(name => AccessTools.Method(type, name)))
        // Canonicalize inherited MethodInfo targets so Harmony combines our
        // guard with patches addressing the declaring type (notably ItemHoe).
        .OfType<MethodInfo>()
        .Select(method => AccessTools.DeclaredMethod(method.DeclaringType, method.Name,
            method.GetParameters().Select(parameter => parameter.ParameterType).ToArray()))
        .Distinct()!;

    public static bool Allow(CollectibleObject collectible, object[] args)
    {
        if (!BodyToolDurability.IsBodyTool(collectible)) return true;
        ItemSlot? slot = args.OfType<ItemSlot>().FirstOrDefault();
        if (slot == null) return true;
        ItemStack? stack = WearableToolResolver.GetRealItemstack(slot);
        // The action must still refer to the same collectible in this slot.
        if (!ReferenceEquals(stack?.Collectible, collectible)) return false;
        if (!BodyToolDurability.IsBroken(stack!)) return true;
        IWorldAccessor? world = args.OfType<IWorldAccessor>().FirstOrDefault()
            ?? args.OfType<Entity>().FirstOrDefault()?.World
            ?? args.OfType<IPlayer>().FirstOrDefault()?.Entity.World;
        if (world != null) BodyToolDurability.RemoveIfBroken(world, slot, stack!);
        return false;
    }
}

[HarmonyPatch]
internal static class BodyToolVoidUsePatch
{
    private static IEnumerable<MethodBase> TargetMethods() => BodyToolUseGuard.Methods(
        "OnHeldUseStart", "OnHeldAttackStart", "OnHeldAttackStop", "OnHeldInteractStart", "OnHeldInteractStop", "OnAttackingWith", "DoTill");

    private static bool Prefix(CollectibleObject __instance, object[] __args)
    {
        if (BodyToolUseGuard.Allow(__instance, __args)) return true;
        // Start handlers must consume the action so it cannot fall through to
        // normal placement/attack handling after the server removes the item.
        for (int i = 0; i < __args.Length; i++)
            if (__args[i] is EnumHandHandling) __args[i] = EnumHandHandling.PreventDefault;
        return false;
    }
}

[HarmonyPatch]
internal static class BodyToolBoolUsePatch
{
    private static IEnumerable<MethodBase> TargetMethods() => BodyToolUseGuard.Methods(
        "OnHeldAttackStep", "OnHeldInteractStep", "OnBlockBrokenWith");

    private static bool Prefix(CollectibleObject __instance, object[] __args, ref bool __result)
    {
        if (BodyToolUseGuard.Allow(__instance, __args)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch]
internal static class BodyToolMiningUsePatch
{
    private static IEnumerable<MethodBase> TargetMethods() => BodyToolUseGuard.Methods("OnBlockBreaking");

    private static bool Prefix(CollectibleObject __instance, object[] __args, float remainingResistance, ref float __result)
    {
        if (BodyToolUseGuard.Allow(__instance, __args)) return true;
        __result = remainingResistance;
        return false;
    }
}
