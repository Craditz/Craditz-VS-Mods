using HarmonyLib;
using rustboundmagic.src;
using rustboundmagic.src.common.item.tool.spellcasting;
using rustboundmagic.src.system.effects;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace RustboundExpansion;

internal static class ChannelHooks
{
    private sealed record Session(string Id, float Warmup, Vec3d Anchor);
    private static readonly Dictionary<EntityPlayer, Session> sessions = new(ReferenceEqualityComparer.Instance);
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.DeclaredMethod(typeof(ItemToolStaffRM), "OnHeldInteractStart"), prefix: new(typeof(ChannelHooks), nameof(AllowStaffStart)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(ItemToolSpellScrollRM), "OnHeldInteractStart"), prefix: new(typeof(ChannelHooks), nameof(AllowScrollStart)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(ItemToolStaffRM), "OnHeldInteractStart"), postfix: new(typeof(ChannelHooks), nameof(Start)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(ItemToolStaffRM), "OnHeldInteractStep"), prefix: new(typeof(ChannelHooks), nameof(Step)));
        foreach (var method in new[] { "OnHeldInteractCancel", "OnHeldInteractStop" })
            harmony.Patch(AccessTools.DeclaredMethod(typeof(ItemToolStaffRM), method), prefix: new(typeof(ChannelHooks), nameof(StopHook)));
    }
    private static void Explain(EntityPlayer p)
    {
        ExpansionSpell.NotifyFailure(p);
    }
    private static bool AllowScrollStart(ItemSlot itemslot, EntityAgent byEntity, ref EnumHandHandling handling)
    {
        var id = itemslot.Itemstack?.Collectible.Code.Path.Split('-').Last();
        if (id == null || !RustboundMagic.FullSpellDictionary.TryGetValue(id, out var value) || value is not BacklogSpell { Channel: true }) return true;
        handling = EnumHandHandling.NotHandled; if (byEntity is EntityPlayer p) Explain(p); return false;
    }
    private static bool AllowStaffStart(EntityAgent byEntity, ref EnumHandHandling handling)
    {
        if (byEntity is not EntityPlayer p || Selected(p) == null || p.LeftHandItemSlot?.Itemstack?.Collectible.Code.Path != "toolscrollofbinding") return true;
        string path = p.World.BlockAccessor.GetBlock(p.Pos.AsBlockPos).Code.Path;
        if (!path.Contains("array")) return true;
        handling = EnumHandHandling.NotHandled; Explain(p); return false;
    }
    internal static BacklogSpell? Selected(EntityPlayer p)
    {
        var id = p.WatchedAttributes.GetString("entitybehavior-castingtool-currentlyselectedspell-name_rm", "");
        return RustboundMagic.FullSpellDictionary.TryGetValue(id, out var value) && value is BacklogSpell { Channel: true } spell ? spell : null;
    }
    private static bool Prism(ItemToolStaffRM staff)
    {
        var type = typeof(ItemToolStaffRM).BaseType!;
        return new[] { "IsInsideOfPrismOfEmanation", "IsInsideOfPrismOfMagnification", "IsInsideOfPrismOfDuplication" }
            .Any(n => (bool?)(AccessTools.Property(type, n)?.GetValue(staff) ?? AccessTools.Field(type, n)?.GetValue(staff)) == true);
    }
    private static void Start(ItemToolStaffRM __instance, EntityAgent byEntity)
    {
        if (byEntity is not EntityPlayer p || Selected(p) is not { } spell || Prism(__instance) ||
            !p.WatchedAttributes.GetBool("entitybehavior-iscastingspell_rm")) return;
        sessions[p] = new(spell.Id, p.WatchedAttributes.GetFloat("entitybehavior-currentspellcastduration_rm", spell.CastTime), p.Pos.XYZ);
    }
    private static bool Step(float secondsUsed, EntityAgent byEntity, ref bool __result)
    {
        if (byEntity is not EntityPlayer p || !sessions.TryGetValue(p, out var session)) return true;
        var spell = Selected(p); var effects = new StatusEffectsRM();
        if (spell == null || spell.Id != session.Id || !p.Alive || !p.WatchedAttributes.GetBool("entitybehavior-iscastingspell_rm") || ControlEffects.Frozen(p) || p.Controls.TriesToMove || p.Controls.Jump || p.Controls.FloorSitting ||
            p.Pos.XYZ.DistanceTo(session.Anchor) > 0.4 || effects.IsEffectActive(p, "silence") || effects.IsEffectActive(p, "stasis") || effects.IsEffectActive(p, "globalcooldown"))
        { Stop(p); __result = false; return false; }
        if (secondsUsed < session.Warmup) { __result = true; return false; }
        float active = secondsUsed - session.Warmup;
        p.WatchedAttributes.SetFloat("entitybehavior-currentspellcasttimer_rm", secondsUsed);
        p.WatchedAttributes.SetFloat("entitybehavior-currentspellcastduration_rm", session.Warmup + spell.Definition.Duration);
        if (active >= spell.Definition.Duration) { Stop(p); __result = false; return false; }
        __result = p.World.Side != EnumAppSide.Server || ExpansionRuntime.For(p.World)?.ChannelStep(p, spell) == true;
        if (!__result) Stop(p);
        return false;
    }
    private static void StopHook(EntityAgent byEntity) { if (byEntity is EntityPlayer p) Stop(p); }
    internal static void Stop(EntityPlayer p) { sessions.Remove(p); ExpansionRuntime.For(p.World)?.StopChannel(p); }
    internal static void Forget(EntityPlayer p) => sessions.Remove(p);
    internal static void ClearAll() => sessions.Clear();
}

public sealed partial class ExpansionRuntime
{
    internal bool ChannelStep(EntityPlayer caster, BacklogSpell spell)
    {
        if (Channels.TryGetValue(caster.EntityId, out var active)) { active.LastStep = Now; return active.Spell.Id == spell.Id; }
        var center = spell.Definition.Kind == "blizzard" ? spell.Ground(caster) : caster.Pos.XYZ;
        if (center == null || center.DistanceTo(caster.Pos.XYZ) > spell.Range + 2 || !Mana(caster, spell.GetModifiedManaCost(caster, spell.Id), spell)) return false;
        Channels[caster.EntityId] = new()
        {
            Caster = caster,
            Spell = spell,
            Center = center,
            Anchor = caster.Pos.XYZ,
            Started = Now,
            End = Now + (long)(spell.Definition.Duration * 1000),
            LastStep = Now,
            Next = Now,
            NextMana = Now + 1000,
            Damage = spell.ModifiedDamage(caster)
        };
        MemeSpellCast(caster);
        return true;
    }
    internal static bool Mana(EntityPlayer caster, int amount, BacklogSpell? spell = null)
    {
        int current = caster.WatchedAttributes.GetInt("entitybehavior-resource-currentmana_rm");
        if (amount < 0 || current < amount) return false;
        if (spell != null && amount > 0) { spell.ConsumeManaForSpell(caster, spell.Id, amount); return true; }
        caster.WatchedAttributes.SetInt("entitybehavior-resource-currentmana_rm", current - amount);
        caster.WatchedAttributes.MarkPathDirty("entitybehavior-resource-currentmana_rm"); return true;
    }
    internal void TickChannels()
    {
        foreach (var channel in Channels.Values.ToArray())
        {
            var caster = channel.Caster; var effects = new StatusEffectsRM();
            if (Now >= channel.End || Now - channel.LastStep > 600 || !caster.Alive || caster.ShouldDespawn ||
                caster.Pos.XYZ.DistanceTo(channel.Anchor) > 0.4 || caster.Controls.TriesToMove || caster.Controls.Jump ||
                ControlEffects.Banished(caster) || effects.IsEffectActive(caster, "silence") || effects.IsEffectActive(caster, "stasis") ||
                ChannelHooks.Selected(caster)?.Id != channel.Spell.Id)
            { StopChannel(caster); continue; }
            if (Now >= channel.NextMana)
            {
                channel.NextMana = Now + 1000;
                if (!Mana(caster, NextModifiers.Drain(caster, channel.Spell.Definition.Kind == "blizzard" ? 10 : 15), channel.Spell)) { StopChannel(caster); caster.TryStopHandAction(true); continue; }
            }
            if (Now < channel.Next) continue;
            var star = channel.Spell.Definition.Kind == "starfall";
            channel.Next = Now + (star ? 200 : 500);
            var targets = CombatRules.Enemies(caster, channel.Center, star ? 24 : 8);
            if (star)
            {
                if (targets.Length > 0) { var target = targets[World.Rand.Next(targets.Length)]; CombatRules.Hit(caster, target, channel.Damage, "arcane"); Vfx.Fall(target, "arcane"); }
            }
            else
            {
                foreach (var target in targets)
                {
                    CombatRules.Hit(caster, target, channel.Damage, "frost"); Vfx.Fall(target, "frost");
                    target.Stats.Set("walkspeed", "rustboundexpansion-blizzardslow", -0.4f, true);
                    var expiry = Now + 800; target.WatchedAttributes.SetLong("rbx-slow-until", expiry);
                    World.RegisterCallback(_ => { if (target.WatchedAttributes.GetLong("rbx-slow-until") == expiry) target.Stats.Remove("walkspeed", "rustboundexpansion-blizzardslow"); }, 800);
                }
                Vfx.BlizzardRain(World, channel.Center);
            }
        }
    }
    internal void StopChannel(EntityPlayer player)
    {
        if (!Channels.Remove(player.EntityId)) return;
        player.WatchedAttributes.SetBool("entitybehavior-iscastingspell_rm", false);
        player.WatchedAttributes.MarkPathDirty("entitybehavior-iscastingspell_rm");
    }
}
