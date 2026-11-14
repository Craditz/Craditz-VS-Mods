using HarmonyLib;
using rustboundmagic.src;
using rustboundmagic.src.system.effects;
using rustboundmagic.src.system.enums;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace RustboundExpansion;

// Rustbound restores players/native companions itself. External pets need their
// nonpersistent stat entries and expiry callbacks restored after serialization.
// Store remaining time at serialization, not a process-relative clock value.
internal static class CreatureBuffPersistence
{
    internal const string Key = "rustboundexpansion-creaturebuffs";
    private static readonly StatusEffectsRM Effects = new();

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(StatusEffectsRM), nameof(StatusEffectsRM.ApplyStatusEffect)),
            postfix: new(typeof(CreatureBuffPersistence), nameof(AfterApply)));
        harmony.Patch(AccessTools.Method(typeof(StatusEffectsRM), "ClearEffectState"),
            postfix: new(typeof(CreatureBuffPersistence), nameof(AfterClear)));
        harmony.Patch(AccessTools.Method(typeof(Entity), nameof(Entity.ToBytes), [typeof(BinaryWriter), typeof(bool)]),
            prefix: new(typeof(CreatureBuffPersistence), nameof(BeforeSerialize)));
    }

    private static bool NativeBuff(string id) => !id.StartsWith("rbx", StringComparison.Ordinal)
        && RustboundMagic.FullSpellDictionary.TryGetValue(id, out var spell) && spell.BuffType == EnumBuffType.buff;

    private static void AfterApply(Entity __0, Dictionary<string, float> __1, string __2, string __4)
    {
        if (__0.World?.Side != EnumAppSide.Server || __0 is rustboundmagic.src.common.entity.EntityCompanionRM
            || !CreatureSupport.IsOwned(__0) || !NativeBuff(__4) || !Effects.IsEffectActive(__0, __4)) return;
        if (__2 != "rustboundmagic-" + __4) return;
        var root = __0.Attributes.GetOrAddTreeAttribute(Key);
        var entry = new TreeAttribute();
        var stats = new TreeAttribute();
        foreach (var (key, value) in __1) if (float.IsFinite(value)) stats.SetFloat(key, value);
        entry.SetAttribute("stats", stats);
        entry.SetBool("infinite", Effects.IsEffectInfinite(__0, __4));
        entry.SetLong("remaining", Effects.GetEffectRemainingMilliseconds(__0, __4));
        root[__4] = entry;
    }

    private static void AfterClear(Entity __0, string __1) => __0.Attributes.GetTreeAttribute(Key)?.RemoveAttribute(__1);

    private static void BeforeSerialize(Entity __instance, bool __1)
    {
        if (!__1 && __instance.World?.Side == EnumAppSide.Server) Snapshot(__instance);
    }

    internal static void Snapshot(Entity entity)
    {
        var root = entity.Attributes.GetTreeAttribute(Key);
        if (root == null) return;
        foreach (var (id, _) in root.ToArray())
        {
            if (!entity.Alive || !CreatureSupport.IsOwned(entity) || !Effects.IsEffectActive(entity, id))
            {
                // Also clear watched state before saving. A callback can be due
                // but not yet run, and its old-process expiry must not resurrect
                // an expired effect after the next process clock starts at zero.
                Effects.RemoveEffect(entity, id);
                root.RemoveAttribute(id);
                continue;
            }
            var entry = root.GetTreeAttribute(id);
            entry.SetBool("infinite", Effects.IsEffectInfinite(entity, id));
            entry.SetLong("remaining", Effects.GetEffectRemainingMilliseconds(entity, id));
        }
    }

    internal static void Restore(Entity entity)
    {
        if (entity.World?.Side != EnumAppSide.Server) return;
        var root = entity.Attributes.GetTreeAttribute(Key);
        if (root == null) return;
        // Snapshot first: applying/replacing native effects changes this tree.
        var saved = root.ToArray();
        entity.Attributes.RemoveAttribute(Key);
        foreach (var (id, value) in saved)
        {
            if (value is not ITreeAttribute entry) continue;
            bool infinite = entry.GetBool("infinite");
            long remaining = entry.GetLong("remaining");
            if (!entity.Alive || !CreatureSupport.IsOwned(entity) || !NativeBuff(id) || (!infinite && remaining <= 0))
            { Effects.RemoveEffect(entity, id); continue; }
            var stats = new Dictionary<string, float>();
            foreach (var (key, stat) in entry.GetTreeAttribute("stats") ?? new TreeAttribute())
                if (stat is FloatAttribute number && float.IsFinite(number.value)) stats[key] = number.value;
            // Mark as refreshing so loading Temperance cannot grant its initial
            // heal again. Barrier health is already saved in WatchedAttributes.
            entity.WatchedAttributes.SetBool("effectactive-" + id, true);
            if (!infinite) Effects.SetEffectExpiration(entity, id, remaining / 1000f);
            Effects.ApplyStatusEffect(entity, stats, "rustboundmagic-" + id, remaining / 1000f, id, infinite);
        }
    }

    internal static void Clear(Entity entity)
    {
        var root = entity.Attributes.GetTreeAttribute(Key);
        if (root == null) return;
        foreach (var (id, _) in root.ToArray()) Effects.RemoveEffect(entity, id);
        entity.Attributes.RemoveAttribute(Key);
    }
}
