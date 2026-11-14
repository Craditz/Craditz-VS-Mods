using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace RustboundExpansion;

internal static class UtilityCatalog
{
    internal static readonly SpellDefinition[] All = Read();
    private static SpellDefinition[] Read()
    {
        using var stream = typeof(UtilityCatalog).Assembly.GetManifestResourceStream("RustboundExpansion.utility-spell-definitions.json")!;
        return JsonSerializer.Deserialize<SpellDefinition[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}

public sealed class HeatItemSpell : ExpansionSpell
{
    public const string SpellId = "rbxheatitem1";
    internal static readonly SpellDefinition Definition = UtilityCatalog.All.Single(d => d.Id == SpellId);
    public HeatItemSpell() : base(SpellId,Definition.School,Definition.Essence)
    {
        Tier=Definition.Tier;ManaCost=Definition.Mana;CastTime=Definition.Cast;
        Duration=0;Stats=Definition.Stats;
        SoundAssetLocationSpellCastingSuccessful=new("rustboundmagic:sounds/effects/castcomplete-buff.ogg");
    }
    public override void OnCastStop(float seconds, ItemSlot slot, EntityAgent caster, BlockSelection block,
        EntitySelection entity, bool scroll, bool magnification, bool duplication, float remaining, bool infinite = false)
    {
        if (caster is not EntityPlayer player || player.World.Side != EnumAppSide.Server || remaining != 0) return;
        var system = HeatItemSystem.For(player.World);
        if (system == null || !system.Begin(player, this, slot, scroll)) NotifyFailure(player);
    }
    internal float Heating(EntityPlayer player) => GetModifiedStats(player,Id);
    internal void Pay(EntityPlayer player,int cost)
    {
        // Native custom-cost overload treats zero as the normal cost.
        if(cost>0)ConsumeManaForSpell(player,Id,cost);
    }
}

internal readonly record struct HeatPreview(float Current, float Result, float Cap)
{
    internal float Gain => Result - Current;
}

internal static class ItemHeating
{
    internal const float MeltingMargin = 1;
    internal static bool Calculate(float current, float melting, float totalHeat, int count, out HeatPreview result)
    {
        result = default;
        if (!float.IsFinite(current) || !float.IsFinite(melting) || !float.IsFinite(totalHeat)
            || melting <= MeltingMargin || totalHeat <= 0 || count <= 0) return false;
        float cap = melting - MeltingMargin;
        float temperature = (float)Math.Min(cap, (double)current + (double)totalHeat / count);
        if (!float.IsFinite(temperature) || temperature <= current || temperature >= melting) return false;
        result = new(current, temperature, cap);
        return true;
    }

    internal static bool Preview(IWorldAccessor world, ItemSlot? slot, float heat, out HeatPreview preview)
    {
        preview = default;
        if (slot == null || slot.Empty || slot.Itemstack?.Collectible is not { } item) return false;
        try
        {
            // The virtual native method covers material-specific overrides too.
            // No guessed temperature for items lacking an actual threshold.
            float melting = item.GetMeltingPoint(world, null!, slot);
            if (RockHeating.IsStone(slot.Itemstack)) melting = melting > MeltingMargin ? Math.Min(melting, RockHeating.SafeCap + MeltingMargin) : RockHeating.SafeCap + MeltingMargin;
            if (!float.IsFinite(melting) || melting <= MeltingMargin) return false;
            return Calculate(item.GetTemperature(world, slot.Itemstack), melting, heat, slot.StackSize, out preview);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // An item requiring a furnace/cooking context is not a safe target
            // for an inventory-only cast. Other candidates remain available.
            return false;
        }
    }
}
