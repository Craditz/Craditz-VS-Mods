using System.Text.Json;
using rustboundmagic.src;
using rustboundmagic.src.system.enums;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace RustboundExpansion;

internal static class MemeCatalog
{
    internal const string Cipher = "spellcipherempty-tier1-neutral-none-mischief";
    internal static readonly SpellDefinition[] All = Read();
    private static SpellDefinition[] Read()
    {
        using var input = typeof(MemeCatalog).Assembly.GetManifestResourceStream("RustboundExpansion.meme-spell-definitions.json")!;
        return JsonSerializer.Deserialize<SpellDefinition[]>(input, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
    internal static string Key(string kind) => "rbx-meme-" + kind;
    internal static bool Owns(string id) => All.Any(d => d.Id == id);
    internal static void ExcludeNormalPools()
    {
        foreach (var pool in new[] { RustboundMagic.SubSpellDictionaryTier1Neutral, RustboundMagic.SubSpellDictionaryTier1Light, RustboundMagic.SubSpellDictionaryTier1Dark,
            RustboundMagic.SubSpellDictionaryTier2Neutral, RustboundMagic.SubSpellDictionaryTier2Light, RustboundMagic.SubSpellDictionaryTier2Dark,
            RustboundMagic.SubSpellDictionaryTier3Neutral, RustboundMagic.SubSpellDictionaryTier3Light, RustboundMagic.SubSpellDictionaryTier3Dark })
            foreach (var d in All) pool.Remove(d.Id);
    }
}

public sealed class MemeSpell : ExpansionSpell
{
    internal readonly SpellDefinition Definition;
    internal MemeSpell(SpellDefinition d) : base(d.Id, "general", "neutral")
    {
        Definition = d; Tier = 1; ManaCost = d.Mana; CastTime = d.Cast; Duration = d.Duration; Range = d.Range;
        ResearchableBySpellforge = false; BuffType = d.Duration > 0 ? EnumBuffType.buff : EnumBuffType.none;
        SoundAssetLocationSpellCastingSuccessful = new("rustboundmagic:sounds/effects/castcomplete-buff.ogg");
    }
    public override void OnCastStop(float seconds, ItemSlot slot, EntityAgent caster, BlockSelection block, EntitySelection entity,
        bool scroll, bool magnification, bool duplication, float remaining, bool infinite = false)
    {
        if (caster is not EntityPlayer p || p.World.Side != EnumAppSide.Server || remaining != 0 || !p.Alive || ControlEffects.Banished(p)) return;
        int cost = GetModifiedManaCost(p, Id);
        if (!scroll && p.WatchedAttributes.GetInt("entitybehavior-resource-currentmana_rm") < cost) return;
        var selected = RayTraceDestinationSelectedEntityAgent(p, Range);
        var target = selected is { Alive: true, ShouldDespawn: false } && selected.Pos.Dimension == p.Pos.Dimension && !ControlEffects.Banished(selected) ? selected : p;
        var runtime = ExpansionRuntime.For(p.World); if (runtime == null) return;
        runtime.ApplyMeme(p, target, Definition);
        Complete(p, scroll, false);
    }
}
