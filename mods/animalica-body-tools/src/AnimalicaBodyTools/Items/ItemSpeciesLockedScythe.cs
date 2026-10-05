using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace AnimalicaBodyTools.Items;

/// <summary>
/// Animal scythe tool. Trait Restrictions owns permission to use or equip it.
///
/// This derives from the base game's ItemScythe so held behavior and wearable
/// area breaks use the vanilla scythe path.
/// </summary>
public sealed class ItemSpeciesLockedScythe : ItemScythe
{
    private static readonly FieldInfo? TrimModeField = AccessTools.Field(typeof(ItemScythe), "trimMode");

    public bool TryPrepareWearableMode(ItemStack stack, out bool previousTrimMode)
    {
        previousTrimMode = false;
        if (TrimModeField?.GetValue(this) is not bool currentTrimMode || stack?.Attributes == null)
        {
            return false;
        }

        previousTrimMode = currentTrimMode;
        int toolMode = stack.Attributes.GetInt("toolMode", 0);
        TrimModeField.SetValue(this, toolMode == 0);
        return true;
    }

    public void RestoreWearableMode(bool previousTrimMode)
    {
        TrimModeField?.SetValue(this, previousTrimMode);
    }

}
