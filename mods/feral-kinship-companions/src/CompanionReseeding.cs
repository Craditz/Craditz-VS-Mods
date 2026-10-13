#nullable enable
using System;
using Vintagestory.API.Common;
namespace FeralKinshipCompanions;

internal static class CompanionReseeding
{
    internal static (float Unlock, float Pause) Thresholds(float unlock, float pause)
        => float.IsFinite(unlock) && float.IsFinite(pause) && unlock >= 55 && unlock <= 100
            && pause >= 50 && pause <= unlock - 1 ? (unlock, pause) : (80, 75);

    internal static bool Update(CompanionWellbeingState? state, float unlock, float pause)
    {
        if (state == null || state.Version > CompanionWellbeingRules.Version) return false;
        (unlock, pause) = Thresholds(unlock, pause);
        bool known = state.Version == CompanionWellbeingRules.Version && state.HasObservation
            && state.HasEstablishedHome && float.IsFinite(state.Value);
        state.ReseedingUnlocked = known && (state.ReseedingUnlocked ? state.Value >= pause : state.Value >= unlock);
        return state.ReseedingUnlocked;
    }

    // The source is an escrow slot containing only this harvest's real seed output.
    // Reserve before invoking native crop callbacks. Refund only while the plot
    // remains empty; callbacks that throw after placing cannot manufacture a seed.
    internal static bool TryPlant(ItemSlot source, System.Func<bool> permitted, System.Func<ItemSlot, bool> plant,
        System.Func<bool> plotOccupied, Action<Exception>? onError = null)
    {
        if (source.Itemstack?.StackSize is not > 0 || !permitted()) return false;
        ItemStack previous = source.Itemstack.Clone();
        try
        {
            ItemStack? one = source.TakeOut(1);
            if (one?.StackSize != 1 || source.StackSize != previous.StackSize - 1)
            {
                source.Itemstack = previous;
                return false;
            }
            // A permit revoked while acquiring the seed must still refuse.
            if (!permitted()) { source.Itemstack = previous; return false; }
            ItemSlot escrow = new DummySlot(one);
            bool accepted = plant(escrow);
            if (plotOccupied()) return accepted;
            source.Itemstack = previous;
            return false;
        }
        catch (Exception error)
        {
            if (!plotOccupied()) source.Itemstack = previous;
            onError?.Invoke(error);
            return false;
        }
    }
}
