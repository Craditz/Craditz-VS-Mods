using System.Text.Json;
using rustboundmagic.src.system.enums;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace RustboundExpansion;

internal static class NextCatalog
{
    internal static readonly SpellDefinition[] All = Read();
    internal static readonly string[] Hidden = ["rbxrecentlylifegripped", "rbxpartyfoulcooldown"];
    private static SpellDefinition[] Read()
    {
        using var stream = typeof(NextCatalog).Assembly.GetManifestResourceStream("RustboundExpansion.next-spell-definitions.json")!;
        return JsonSerializer.Deserialize<SpellDefinition[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
    internal static bool Stance(string kind) => kind is "snap" or "furtive" or "allin";
    internal static SpellDefinition Kind(string kind) => All.Single(d => d.Kind == kind);
}

public sealed class NextSpell : ExpansionSpell
{
    internal readonly SpellDefinition Definition;
    internal NextSpell(SpellDefinition d) : base(d.Id, d.School, d.Essence)
    {
        Definition = d; Tier = d.Tier; ManaCost = d.Mana; CastTime = d.Cast;
        Duration = d.Duration; Range = d.Range; Damage = d.Damage; Stats = d.Stats; Element = d.Element;
        if (d.School == "general") SoundAssetLocationSpellCastingSuccessful = new("rustboundmagic:sounds/effects/castcomplete-" + (d.Damage > 0 ? "evocation" : "buff") + ".ogg");
        BuffType = NextCatalog.Stance(d.Kind) || d.Kind is "prayer" or "wisps" ? EnumBuffType.buff
            : d.Kind == "party" ? EnumBuffType.debuff : EnumBuffType.none;
    }
    internal Entity? Target(EntityPlayer p) => Range > 0 ? RayTraceDestinationSelectedEntityAgent(p, Range) : null;
    internal float ModifiedDamage(EntityPlayer p) => GetModifiedDamage(p, Id);
    internal float ModifiedStats(EntityPlayer p) => GetModifiedStats(p, Id);
    internal float ModifiedDuration(EntityPlayer p) => GetModifiedDuration(p, Id);
    public override void OnCastStop(float seconds, ItemSlot slot, EntityAgent caster, BlockSelection block,
        EntitySelection entity, bool scroll, bool magnification, bool duplication, float remaining, bool infinite = false)
    {
        if (caster is not EntityPlayer p || p.World.Side != EnumAppSide.Server || remaining != 0 || !p.Alive || ControlEffects.Banished(p)) return;
        if (Definition.Kind == "charge") { if (slot?.Itemstack?.Collectible is not rustboundmagic.src.common.item.tool.spellcasting.ItemToolStaffRM) NotifyFailure(p); return; }
        var runtime = ExpansionRuntime.For(p.World); if (runtime == null) return;
        if (NextCatalog.Stance(Definition.Kind) && p.WatchedAttributes.GetString(NextModifiers.StanceKey) == Id)
        { runtime.RemoveStance(p); return; }
        var selected = Target(p);
        bool cosmetic = Definition.Kind == "party" && !CombatRules.Enemy(p, selected ?? p);
        int cost = NextModifiers.Cost(this, p, cosmetic);
        if (p.WatchedAttributes.GetInt("entitybehavior-resource-currentmana_rm") < cost) { NotifyFailure(p); return; }
        var ground = Definition.Kind == "circle" ? SpellTargeting.Ground(p, Range) : null;
        if (!runtime.CastNext(this, p, selected, ground)) { NotifyFailure(p); return; }
        ConsumeManaForSpell(p, Id, cost);
        p.World.PlaySoundAt(SoundAssetLocationSpellCastingSuccessful, p.Pos.X, p.Pos.Y, p.Pos.Z, null, 1, 16);
    }
}

internal sealed class NextStatusSpell : ExpansionSpell
{
    internal NextStatusSpell(string id) : base(id, "conjuration", "neutral")
    { Tier = 0; Duration = 20; BuffType = EnumBuffType.debuff; ResearchableBySpellforge = false; CanUseAttributeModifiers = false; }
    public override void OnCastStop(float seconds, ItemSlot slot, EntityAgent caster, BlockSelection block,
        EntitySelection entity, bool scroll, bool magnification, bool duplication, float remaining, bool infinite = false) { }
}
