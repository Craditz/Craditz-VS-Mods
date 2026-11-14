using System.Text.Json;
using rustboundmagic.src.system.enums;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace RustboundExpansion;

internal sealed record SpellDefinition(string Id, string Name, string School, string Essence,
    int Mana, float Cast, float Duration, float Range, float Damage, string Kind, string Description, string Element = "none", int Tier = 2, float Stats = 0);

internal static class BacklogCatalog
{
    internal static readonly SpellDefinition[] All = Read();
    private static SpellDefinition[] Read()
    {
        using var stream = typeof(BacklogCatalog).Assembly.GetManifestResourceStream("RustboundExpansion.spell-definitions.json")!;
        return JsonSerializer.Deserialize<SpellDefinition[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
    internal static bool Owns(string id) => All.Any(d => d.Id == id);
}

public sealed class BacklogSpell : ExpansionSpell
{
    internal readonly SpellDefinition Definition;
    internal bool Channel => Definition.Kind is "blizzard" or "starfall";
    internal BacklogSpell(SpellDefinition definition) : base(definition.Id, definition.School, definition.Essence)
    {
        Definition = definition;
        ManaCost = definition.Mana; CastTime = definition.Cast; Duration = definition.Duration;
        Range = definition.Range; Damage = definition.Damage; Element = definition.Element;
        BuffType = definition.Kind == "oath" ? EnumBuffType.buff : EnumBuffType.none;
        EffectsOtherClosePlayers = definition.Kind is "armageddon" or "blizzard" or "starfall";
    }
    public override void OnCastStop(float seconds, ItemSlot slot, EntityAgent caster, BlockSelection block,
        EntitySelection entity, bool scroll, bool magnification, bool duplication, float remaining, bool infinite = false)
    {
        if (caster is not EntityPlayer player || player.World.Side != EnumAppSide.Server || remaining != 0) return;
        if (Channel)
        {
            if (slot?.Itemstack?.Collectible is not rustboundmagic.src.common.item.tool.spellcasting.ItemToolStaffRM)
                NotifyFailure(player);
            return;
        }
        var runtime = ExpansionRuntime.For(player.World);
        if (runtime == null || !player.Alive || ControlEffects.Banished(player)) return;
        var selected = Range > 0 ? RayTraceDestinationSelectedEntityAgent(player, Range) : null;
        var ground = Definition.Kind == "sentry" ? SpellTargeting.Ground(player, 12) : null;
        if (runtime.Cast(this, player, selected, ground)) Complete(player, scroll, false);
        else NotifyFailure(player);
    }
    internal Vec3d? Ground(EntityPlayer player) => SpellTargeting.Ground(player, Range);
    internal float ModifiedDamage(EntityPlayer player) => GetModifiedDamage(player, Id);
    internal float ModifiedDuration(EntityPlayer player) => GetModifiedDuration(player, Id);
}

internal static class CombatRules
{
    private static readonly ArcLightningSpell permissionSpell = new();
    internal static bool Enemy(EntityPlayer caster, Entity target)
    {
        if (target is not EntityAgent agent || target == caster || !target.Alive || target.ShouldDespawn ||
            target.Pos.Dimension != caster.Pos.Dimension || target.GetBehavior<Vintagestory.GameContent.EntityBehaviorHealth>() is not { Health: > 0 } ||
            ControlEffects.Banished(target) || BurstOfSpeedSpell.IsAlly(caster, agent)) return false;
        if (IsAlliedSentry(caster, target)) return false;
        if (target is not EntityPlayer && !caster.Player.HasPrivilege("attackcreatures")) return false;
        return permissionSpell.NativePermission(caster, target) && target.ShouldReceiveDamage(Source(caster), 1);
    }
    internal static bool IsAlliedSentry(EntityPlayer caster, Entity target)
    {
        if(target is BloodGoober blood)
        {var bloodOwner=caster.World.PlayerByUid(blood.OwnerUid)?.Entity;return blood.OwnerUid==caster.PlayerUID || bloodOwner!=null&&BurstOfSpeedSpell.IsAlly(caster,bloodOwner);}
        if (target is not SummonedSentry sentry) return false;
        var owner = caster.World.PlayerByUid(sentry.OwnerUid)?.Entity;
        return sentry.OwnerUid == caster.PlayerUID || owner != null && BurstOfSpeedSpell.IsAlly(caster, owner);
    }
    internal static Entity[] Enemies(EntityPlayer caster, Vec3d center, float radius) => caster.World.GetEntitiesAround(center, radius, radius)
        .Where(e => Enemy(caster, e) && e.Pos.XYZ.SquareDistanceTo(center) <= radius * radius).DistinctBy(e => e.EntityId).ToArray();
    internal static DamageSource Source(EntityPlayer caster, string element = "none", EnumDamageType type = EnumDamageType.Injury)
    {
        // Native NPC modifiers treat elemental "none" as their unconfigured
        // weakness/resistance/strength and can multiply it by zero. Physical
        // damage must use the native non-elemental overload instead.
        DamageSource source = Enum.TryParse<rustboundmagic.src.system.enums.spells.EnumMagicElement>(element, out var value)
            && value != rustboundmagic.src.system.enums.spells.EnumMagicElement.none
            ? new rustboundmagic.src.system.effects.ElementalDamageSource { ElementalType = value }
            : new DamageSource();
        source.Source = EnumDamageSource.Player;
        source.Type = type;
        source.SourceEntity = caster;
        source.CauseEntity = caster;
        source.KnockbackStrength = 0;
        source.IgnoreInvFrames = true;
        return source;
    }
    internal static bool Hit(EntityPlayer caster, Entity target, float damage, string element = "none", EnumDamageType type = EnumDamageType.Injury)
        => float.IsFinite(damage) && damage > 0 && Enemy(caster, target) && target.ReceiveDamage(Source(caster, element, type), damage);
}
