using HarmonyLib;
using rustboundmagic.src.system.effects;
using rustboundmagic.src.system.enums;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace RustboundExpansion;

public sealed class HotHandsSpell : ExpansionSpell
{
    public const string SpellId = "rbxhothands1";
    internal static readonly SpellDefinition Definition = UtilityCatalog.All.Single(d => d.Id == SpellId);
    public HotHandsSpell() : base(SpellId, Definition.School, Definition.Essence)
    {
        Tier = Definition.Tier; ManaCost = Definition.Mana; CastTime = Definition.Cast;
        Duration = Definition.Duration; Range = Definition.Range; BuffType = EnumBuffType.buff;
        SoundAssetLocationSpellCastingSuccessful = new("rustboundmagic:sounds/effects/castcomplete-buff.ogg");
    }
    public override void OnCastStop(float seconds, ItemSlot slot, EntityAgent caster, BlockSelection block,
        EntitySelection entity, bool scroll, bool magnification, bool duplication, float remaining, bool infinite = false)
    {
        if (caster is not EntityPlayer player || player.World.Side != EnumAppSide.Server || !player.Alive
            || ControlEffects.Banished(player) || StatusEffects.IsEffectActive(player,"stasis")) return;
        bool restoring = IsRestoringExistingBuff(remaining);
        var target = !restoring && RayTraceDestinationSelectedEntityAgent(player,Range) is EntityPlayer ally
            && ExpansionRuntime.Ally(player,ally) ? ally : player;
        float duration = GetEffectiveDuration(player,remaining);
        if (!float.IsFinite(duration) || duration <= 0) { if (!restoring) NotifyFailure(player); return; }
        ExpansionRuntime.Marker(target,SpellId,duration,infinite);
        if (!StatusEffects.IsEffectActive(target,SpellId)) { if (!restoring) NotifyFailure(player); return; }
        Complete(player,scroll,restoring);
    }
}

internal static class HotHands
{
    private static readonly StatusEffectsRM Effects = new();
    internal static bool Protects(EntityPlayer? player) => player?.World?.Side == EnumAppSide.Server && player.Alive
        && !player.ShouldDespawn && Effects.IsEffectActive(player,HotHandsSpell.SpellId);
    internal static void Install(Harmony harmony) => harmony.Patch(AccessTools.Method(typeof(InventoryBase),nameof(InventoryBase.DropSlotIfHot)),
        prefix:new(typeof(HotHands),nameof(AllowNativeHotDrop)));
    // Skip only this contact-burn/drop operation. No damage handler, fire
    // resistance stat, collectible attributes or global temperature is changed.
    private static bool AllowNativeHotDrop(IPlayer player) => !Protects(player?.Entity);
}

internal static class RockHeating
{
    internal const float SafeCap = 600, MinimumThrowTemperature = 100;
    internal static bool IsStone(ItemStack? stack) => stack?.Class == EnumItemClass.Item
        && stack.Collectible?.Code is { Domain:"game" } code && code.Path.StartsWith("stone-",StringComparison.Ordinal)
        && stack.Collectible is Vintagestory.GameContent.ItemStone;
    internal static bool TryTemperature(IWorldAccessor world,ItemStack? stack,out float temperature)
    {
        temperature=0;
        if (stack is not {StackSize:>0} || !IsStone(stack)) return false;
        try
        {
            temperature=stack.Collectible.GetTemperature(world,stack);
            return float.IsFinite(temperature) && temperature>=MinimumThrowTemperature;
        }
        catch(Exception e) when(e is not OutOfMemoryException) {return false;}
    }
}
