using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using rustboundmagic.src;
using rustboundmagic.src.common.item.tool.spellcasting;
using rustboundmagic.src.system.effects;
using rustboundmagic.src.system.spells;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace RustboundExpansion;

internal static class NextModifiers
{
    internal const string StanceKey = "rbx-stance", SpeedKey = "rbx-stance-speed", DamageKey = "rbx-stance-damage", ManaKey = "rbx-stance-mana";
    [ThreadStatic] private static bool? cosmeticOverride;
    [ThreadStatic] private static int manaInfoDepth;
    private sealed record DotActor(WeakReference<Entity> Actor);
    private static readonly ConditionalWeakTable<DamageOverTimeEffect, DotActor> dotActors = new();
    internal sealed record HealthReceipt(float Before, int DotCount, Entity? Actor);
    internal static NextSpell? Selected(EntityPlayer p) => RustboundMagic.FullSpellDictionary.TryGetValue(p.WatchedAttributes.GetString("entitybehavior-castingtool-currentlyselectedspell-name_rm", ""), out var spell) ? spell as NextSpell : null;
    private static bool TurningOff(EntityPlayer p, string id) => p.WatchedAttributes.GetString(StanceKey, "") == id
        && NextCatalog.All.Any(d => d.Id == id && NextCatalog.Stance(d.Kind));
    internal static int Cost(NextSpell spell, EntityPlayer p, bool cosmetic)
    {
        var previous = cosmeticOverride; cosmeticOverride = cosmetic;
        try { return spell.GetModifiedManaCost(p, spell.Id); }
        finally { cosmeticOverride = previous; }
    }
    internal static int Drain(EntityPlayer p, int amount) => Math.Max(1, (int)Math.Ceiling(amount * p.WatchedAttributes.GetFloat(ManaKey, 1)));
    internal static void Install(Harmony h)
    {
        var t = typeof(SpellcastingModifierSystemRM);
        h.Patch(AccessTools.Method(t, "GetModifiedManaCost"),
            prefix: new(typeof(NextModifiers), nameof(BeginManaInfo)),
            finalizer: new(typeof(NextModifiers), nameof(EndManaInfo)));
        h.Patch(AccessTools.Method(t, "CalculateModifiedSpellAttributeManaCost"), postfix: new(typeof(NextModifiers), nameof(Mana)));
        h.Patch(AccessTools.Method(t, "CalculateModifiedSpellAttributeDamage"), postfix: new(typeof(NextModifiers), nameof(Damage)));
        h.Patch(AccessTools.Method(t, "CalculateModifiedSpellAttributeCastTime", [typeof(EntityPlayer), typeof(string)]), postfix: new(typeof(NextModifiers), nameof(CastTime)));
        h.Patch(AccessTools.Method(t, "CalculateModifiedSpellAttributeCastTime", [typeof(EntityPlayer), typeof(float)]), postfix: new(typeof(NextModifiers), nameof(ScrollTime)));
        h.Patch(AccessTools.Method(typeof(EntityBehaviorHealth), nameof(EntityBehaviorHealth.OnEntityReceiveDamage)),
            prefix: new(typeof(NextModifiers), nameof(BeforeHealth)), postfix: new(typeof(NextModifiers), nameof(AfterHealth)));
        h.Patch(AccessTools.Method(typeof(EntityBehaviorHealth), "ProcessDoTEffects"), transpiler: new(typeof(NextModifiers), nameof(DotTicks)));
        ChargeHooks.Install(h);
    }
    private static void BeginManaInfo(out int __state)
    { __state = manaInfoDepth; manaInfoDepth++; }
    private static void EndManaInfo(int __state) => manaInfoDepth = __state;
    private static bool CanResolveManaTarget(EntityPlayer player)
    {
        // Native grimoire/handbook descriptions may be built on a worker.
        // Formatted previews show the full cost and never query world geometry,
        // even on the main thread. Unscoped numeric queries may also be async.
        if (manaInfoDepth != 0) return false;
        int worldThread = player.World?.Side switch
        {
            EnumAppSide.Client => RuntimeEnv.MainThreadId,
            EnumAppSide.Server => RuntimeEnv.ServerMainThreadId,
            _ => 0
        };
        return worldThread != 0 && Environment.CurrentManagedThreadId == worldThread;
    }
    private static void Mana(EntityPlayer __0, string __1, ref int __result)
    {
        if (TurningOff(__0, __1)) { __result = 0; return; }
        float factor = __0.WatchedAttributes.GetFloat(ManaKey, 1);
        if (__1 == NextCatalog.Kind("party").Id)
        {
            bool cosmetic = cosmeticOverride ?? (CanResolveManaTarget(__0)
                && !CombatRules.Enemy(__0, (RustboundMagic.FullSpellDictionary[__1] as NextSpell)?.Target(__0) ?? __0));
            if (cosmetic) factor *= .1f;
        }
        if (factor != 1) __result = Math.Max(1, (int)Math.Ceiling(__result * factor));
    }
    private static void Damage(EntityPlayer __0, ref float __result) => __result *= __0.WatchedAttributes.GetFloat(DamageKey, 1);
    private static void CastTime(EntityPlayer __0, string __1, ref float __result)
    { if (TurningOff(__0, __1)) __result = 0; else __result /= Math.Max(.05f, __0.WatchedAttributes.GetFloat(SpeedKey, 1)); }
    private static void ScrollTime(EntityPlayer __0, ref float __result) => __result /= Math.Max(.05f, __0.WatchedAttributes.GetFloat(SpeedKey, 1));
    private static void BeforeHealth(EntityBehaviorHealth __instance, DamageSource __0, out HealthReceipt __state)
        => __state = new(__instance.Health, __instance.ActiveDoTEffects.Count, ExpansionRuntime.DamageActor(__instance.entity.World, __0));
    private static void AfterHealth(EntityBehaviorHealth __instance, DamageSource __0, HealthReceipt __state)
    {
        if (__instance.entity.World.Side != EnumAppSide.Server) return;
        if (__state.Actor != null)
            for (int i = __state.DotCount; i < __instance.ActiveDoTEffects.Count; i++)
            { var dot = __instance.ActiveDoTEffects[i]; dotActors.Remove(dot); dotActors.Add(dot, new(new(__state.Actor))); }
        ExpansionRuntime.For(__instance.entity.World)?.AfterActualDamage(__instance.entity, __0, Math.Max(0, __state.Before - __instance.Health));
        ExpansionRuntime.For(__instance.entity.World)?.BloodActualDamage(__instance.entity,Math.Max(0,__state.Before-__instance.Health));
    }
    // Vanilla strips source entities when storing DOTs. Retain runtime attribution
    // for those created through ReceiveDamage so their later ticks obey Party Foul.
    private static IEnumerable<CodeInstruction> DotTicks(IEnumerable<CodeInstruction> input, System.Reflection.MethodBase original)
    {
        var local = original.GetMethodBody()!.LocalVariables.Single(v => v.LocalType == typeof(DamageOverTimeEffect));
        var receive = AccessTools.Method(typeof(Entity), nameof(Entity.ReceiveDamage));
        int replaced = 0;
        foreach (var instruction in input)
        {
            if (instruction.Calls(receive))
            { yield return new CodeInstruction(OpCodes.Ldloc, local.LocalIndex); yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NextModifiers), nameof(DotHit))); replaced++; }
            else yield return instruction;
        }
        if (replaced != 1) throw new InvalidOperationException("Unsupported native DOT damage layout: " + replaced);
    }
    private static bool DotHit(Entity target, DamageSource source, float damage, DamageOverTimeEffect dot)
    {
        if (dotActors.TryGetValue(dot, out var entry) && entry.Actor.TryGetTarget(out var actor))
        { source.CauseEntity = actor; source.SourceEntity = actor; }
        return target.ReceiveDamage(source, damage);
    }
}

internal static class ChargeHooks
{
    internal sealed class Session
    {
        internal required NextSpell Spell;
        internal long Started;
        internal int Dimension;
        internal float FullTime, Damage, Progress;
        internal int Rate, Paid;
    }
    private static readonly Dictionary<EntityPlayer, Session> sessions = new(ReferenceEqualityComparer.Instance);
    internal static void Install(Harmony h)
    {
        h.Patch(AccessTools.DeclaredMethod(typeof(ItemToolStaffRM), "OnHeldInteractStart"), prefix: new(typeof(ChargeHooks), nameof(AllowStart)), postfix: new(typeof(ChargeHooks), nameof(Start)));
        h.Patch(AccessTools.DeclaredMethod(typeof(ItemToolSpellScrollRM), "OnHeldInteractStart"), prefix: new(typeof(ChargeHooks), nameof(AllowScroll)));
        h.Patch(AccessTools.DeclaredMethod(typeof(ItemToolStaffRM), "OnHeldInteractStep"), prefix: new(typeof(ChargeHooks), nameof(Step)));
        h.Patch(AccessTools.DeclaredMethod(typeof(ItemToolStaffRM), "OnHeldInteractStop"), prefix: new(typeof(ChargeHooks), nameof(Stop)));
        h.Patch(AccessTools.DeclaredMethod(typeof(ItemToolStaffRM), "OnHeldInteractCancel"), prefix: new(typeof(ChargeHooks), nameof(Cancel)));
    }
    private static bool AllowScroll(ItemSlot itemslot, EntityAgent byEntity, ref EnumHandHandling handling)
    {
        if (itemslot.Itemstack?.Collectible.Code.Path.Split('-').Last() != NextCatalog.Kind("charge").Id) return true;
        handling = EnumHandHandling.NotHandled;
        if (byEntity is EntityPlayer p) ExpansionSpell.NotifyFailure(p);
        return false;
    }
    private static bool AllowStart(EntityAgent byEntity, ref EnumHandHandling handling)
    {
        if (byEntity is not EntityPlayer p || NextModifiers.Selected(p)?.Definition.Kind != "charge" || p.LeftHandItemSlot?.Itemstack?.Collectible.Code.Path != "toolscrollofbinding") return true;
        if (!p.World.BlockAccessor.GetBlock(p.Pos.AsBlockPos).Code.Path.Contains("array")) return true;
        handling = EnumHandHandling.NotHandled; ExpansionSpell.NotifyFailure(p); return false;
    }
    private static void Start(ItemToolStaffRM __instance, EntityAgent byEntity)
    {
        if (byEntity is not EntityPlayer p || NextModifiers.Selected(p) is not { Definition.Kind: "charge" } spell ||
            !p.WatchedAttributes.GetBool("entitybehavior-iscastingspell_rm")) return;
        var parent = typeof(ItemToolStaffRM).BaseType!;
        if (new[] { "IsInsideOfPrismOfEmanation", "IsInsideOfPrismOfMagnification", "IsInsideOfPrismOfDuplication" }
            .Any(n => (bool?)(AccessTools.Property(parent, n)?.GetValue(__instance) ?? AccessTools.Field(parent, n)?.GetValue(__instance)) == true)) return;
        Begin(p, spell, p.WatchedAttributes.GetFloat("entitybehavior-currentspellcastduration_rm", 4));
    }
    internal static void Begin(EntityPlayer p, NextSpell spell, float fullTime)
    {
        sessions[p] = new() { Spell = spell, Started = p.World.ElapsedMilliseconds, Dimension = p.Pos.Dimension, FullTime = Math.Max(.05f, fullTime), Damage = spell.ModifiedDamage(p), Rate = spell.GetModifiedManaCost(p, spell.Id) };
    }
    private static bool Valid(EntityPlayer p, Session s) => p.Alive && !p.ShouldDespawn && NextModifiers.Selected(p)?.Id == s.Spell.Id
        && p.Pos.Dimension == s.Dimension && !ControlEffects.Frozen(p) && !p.Controls.TriesToMove && !p.Controls.Jump && !p.Controls.FloorSitting
        && !ExpansionRuntime.Active(p, "silence") && !ExpansionRuntime.Active(p, "stasis") && !ExpansionRuntime.Active(p, "globalcooldown");
    internal static bool Advance(EntityPlayer p, float seconds)
    {
        if (!sessions.TryGetValue(p, out var s) || !Valid(p, s)) return false;
        float elapsed = Math.Min(seconds, Math.Max(0, (p.World.ElapsedMilliseconds - s.Started) / 1000f + .1f));
        float progress = Math.Clamp(elapsed * 4 / s.FullTime, s.Progress, 4);
        int due = Math.Max(0, (int)Math.Ceiling(progress * s.Rate - .0001f) - s.Paid);
        if (p.World.Side == EnumAppSide.Server && due > 0)
        {
            if (p.WatchedAttributes.GetInt("entitybehavior-resource-currentmana_rm") < due) return false;
            s.Spell.ConsumeManaForSpell(p, s.Spell.Id, due); s.Paid += due;
        }
        s.Progress = progress;
        p.WatchedAttributes.SetFloat("entitybehavior-currentspellcasttimer_rm", Math.Min(seconds, s.FullTime));
        if (p.World.Side == EnumAppSide.Server) Vfx.Burst(p.World, p.Pos.XYZ.AddCopy(p.LocalEyePos).Add(p.Pos.GetViewVector().ToVec3d() * .65), "arcane", .02f + progress * .04f, 2 + (int)progress);
        return true;
    }
    private static bool Step(float secondsUsed, EntityAgent byEntity, ref bool __result)
    {
        if (byEntity is not EntityPlayer p || !sessions.ContainsKey(p)) return true;
        __result = Advance(p, secondsUsed);
        if (!__result) Abort(p);
        return false;
    }
    internal static bool Release(EntityPlayer p, float seconds)
    {
        if (!sessions.TryGetValue(p, out var s)) return false;
        bool valid = Advance(p, seconds); sessions.Remove(p);
        if (!valid || s.Progress < 1 || p.World.Side != EnumAppSide.Server) return false;
        var from = p.Pos.XYZ.AddCopy(p.LocalEyePos);
        bool shot = NextProjectile.Launch(p, "star", from, ExpansionRuntime.Aim(p, 18), s.Damage * s.Progress / 4, 18, "arcane");
        if (shot) ExpansionRuntime.For(p.World)?.MemeSpellCast(p);
        if (shot) p.World.PlaySoundAt(s.Spell.SoundAssetLocationSpellCastingSuccessful, p.Pos.X, p.Pos.Y, p.Pos.Z, null, 1, 16);
        return shot;
    }
    private static void Stop(float secondsUsed, EntityAgent byEntity) { if (byEntity is EntityPlayer p) Release(p, secondsUsed); }
    private static void Cancel(EntityAgent byEntity, EnumItemUseCancelReason cancelReason)
    { if (byEntity is EntityPlayer p && sessions.ContainsKey(p) && cancelReason != EnumItemUseCancelReason.ReleasedMouse) Abort(p); }
    private static void Abort(EntityPlayer p)
    { sessions.Remove(p); p.WatchedAttributes.SetFloat("entitybehavior-currentspellcastduration_rm", float.MaxValue); p.WatchedAttributes.SetBool("entitybehavior-iscastingspell_rm", false); }
    internal static void Forget(EntityPlayer p) => sessions.Remove(p);
    internal static void ClearAll() => sessions.Clear();
}
