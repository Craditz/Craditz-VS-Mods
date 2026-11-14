using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace RustboundExpansion;

internal static class ControlEffects
{
    internal static bool Banished(Entity e) => e.WatchedAttributes.GetBool("rbx-banished");
    internal static bool Frozen(Entity e) => e.WatchedAttributes.GetBool("rbx-frozen");
    internal static void Set(Entity target, bool banish)
    {
        target.WatchedAttributes.SetBool("rbx-banished", banish);
        target.WatchedAttributes.SetBool("rbx-frozen", true);
        target.WatchedAttributes.SetDouble("rbx-anchor-x", target.Pos.X);
        target.WatchedAttributes.SetDouble("rbx-anchor-y", target.Pos.Y);
        target.WatchedAttributes.SetDouble("rbx-anchor-z", target.Pos.Z);
        if (banish && target is EntityPlayer p) { p.TryStopHandAction(true); ChannelHooks.Stop(p); }
        target.WatchedAttributes.MarkAllDirty();
    }
    internal static void Hold(Entity target, Vec3d anchor, int dimension)
    {
        if (target.Pos.Dimension != dimension) return;
        target.Pos.SetPos(anchor); target.Pos.Motion.Set(0, 0, 0);
    }
    internal static void Clear(Entity target)
    {
        ClearControl(target);
        if (!target.WatchedAttributes.HasAttribute("rbx-slow-until")) return;
        target.WatchedAttributes.RemoveAttribute("rbx-slow-until");
        target.Stats?.Remove("walkspeed", "rustboundexpansion-blizzardslow");
    }
    internal static void ClearControl(Entity target)
    {
        foreach (var key in new[] { "rbx-banished", "rbx-frozen", "rbx-anchor-x", "rbx-anchor-y", "rbx-anchor-z" })
            if (target.WatchedAttributes.HasAttribute(key)) target.WatchedAttributes.RemoveAttribute(key);
    }
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(rustboundmagic.src.system.effects.StatusEffectsRM),"ApplyStatusEffect"),
            prefix:new(typeof(ControlEffects),nameof(StatusGate)));
        foreach (var type in new[] { typeof(Entity), typeof(EntityPlayer) })
            harmony.Patch(AccessTools.DeclaredPropertyGetter(type, nameof(Entity.IsInteractable)), postfix: new(typeof(ControlEffects), nameof(Interactable)));
        harmony.Patch(AccessTools.Method(typeof(Entity), nameof(Entity.ReceiveDamage)), prefix: new(typeof(ControlEffects), nameof(DamageGate)));
        harmony.Patch(AccessTools.Method(typeof(EntityAgent), nameof(EntityAgent.OnGameTick)),
            prefix: new(typeof(ControlEffects), nameof(AgentTick)), postfix: new(typeof(ControlEffects), nameof(HoldAfterTick)));
        foreach (var type in new[] { typeof(EntityShapeRenderer), typeof(EntityPlayerShapeRenderer) })
            foreach (var method in new[] { "DoRender3DOpaque", "DoRender3DOpaqueBatched", "DoRender2D" })
                harmony.Patch(AccessTools.DeclaredMethod(type, method), prefix: new(typeof(ControlEffects), nameof(Draw)));
        harmony.Patch(AccessTools.Method(typeof(EntityBehaviorNameTag), "OnRenderFrame"), prefix: new(typeof(ControlEffects), nameof(NameTag)));
        harmony.Patch(AccessTools.Method(typeof(AiTaskBaseTargetable), "IsTargetableEntity", new[] { typeof(Entity), typeof(float) }),
            prefix: new(typeof(ControlEffects), nameof(Targetable)));
        harmony.Patch(AccessTools.Method(typeof(CollectibleObject), "OnHeldUseStart"), prefix: new(typeof(ControlEffects), nameof(ActionGate)));
    }
    private static bool StatusGate(Entity entityIn)=>!Banished(entityIn);
    private static void Interactable(Entity __instance, ref bool __result) { if (Banished(__instance)) __result = false; }
    private static bool DamageGate(Entity __instance, DamageSource damageSource, ref bool __result)
    {
        if (Banished(__instance) || damageSource.CauseEntity is { } cause && Banished(cause) || damageSource.SourceEntity is { } source && Banished(source))
        { __result = false; return false; }
        return true;
    }
    private static bool AgentTick(EntityAgent __instance) => !Banished(__instance);
    private static void HoldAfterTick(EntityAgent __instance)
    {
        if (Frozen(__instance)) Hold(__instance, new(__instance.WatchedAttributes.GetDouble("rbx-anchor-x"),
            __instance.WatchedAttributes.GetDouble("rbx-anchor-y"), __instance.WatchedAttributes.GetDouble("rbx-anchor-z")), __instance.Pos.Dimension);
    }
    internal static bool Hidden(Entity e) => Banished(e) || e.WatchedAttributes.GetBool(MemeCatalog.Key("entrance"));
    private static bool Draw(EntityShapeRenderer __instance) => !Hidden(__instance.entity);
    private static bool NameTag(EntityBehaviorNameTag __instance) => !Hidden(__instance.entity);
    private static bool Targetable(Entity e, ref bool __result) { if (Banished(e)) { __result = false; return false; } return true; }
    private static bool ActionGate(EntityAgent byEntity) => !Banished(byEntity);
}
