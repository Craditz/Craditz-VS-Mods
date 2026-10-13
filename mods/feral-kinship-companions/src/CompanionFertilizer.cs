#nullable enable
using System;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
namespace FeralKinshipCompanions;

internal static class CompanionFertilizer
{
    internal static bool SupportedSourceSlot(ItemSlot? slot)
        => slot != null
            && slot.GetType().GetMethod(nameof(ItemSlot.TakeOut), new[] { typeof(int) })?.DeclaringType == typeof(ItemSlot)
            && slot.GetType().GetMethod(nameof(ItemSlot.TakeOutWhole), Type.EmptyTypes)?.DeclaringType == typeof(ItemSlot);
    internal readonly record struct Need(int Nutrient, float Deficit, string CropCode,
        float Target = 80, int OriginalFertility = 0, string[]? AppliedBoosts = null);
    internal static bool TryNeed(BlockCrop crop, float[] available, float[] queued, float target, out Need need)
    {
        need = default;
        var props = crop.CropProps;
        if (crop.Code == null || props == null || available.Length != 3 || queued.Length != 3 || !float.IsFinite(target)
            || target < 1 || target > 80 || props.GrowthStages < 2) return false;
        int stage = crop.CurrentCropStage, nutrient = (int)props.RequiredNutrient;
        if (stage < 1 || stage >= props.GrowthStages || nutrient < 0 || nutrient > 2
            || !float.IsFinite(props.NutrientConsumption) || props.NutrientConsumption <= 0) return false;
        for (int i = 0; i < 3; i++)
            if (!float.IsFinite(available[i]) || !float.IsFinite(queued[i]) || available[i] < 0 || queued[i] < 0) return false;
        float remaining = props.NutrientConsumption * (props.GrowthStages - stage) / (props.GrowthStages - 1f);
        float deficit = Math.Min(target - available[nutrient], remaining) - queued[nutrient];
        if (!float.IsFinite(deficit) || deficit <= .01f) return false;
        need = new Need(nutrient, deficit, crop.Code.ToString(), target);
        return true;
    }

    internal static bool TryDose(ItemStack? stack, Need need, float maxOvershoot, out double score)
    {
        score = double.MaxValue;
        if (stack?.StackSize is not > 0 || !float.IsFinite(maxOvershoot) || maxOvershoot < 0 || maxOvershoot > 20) return false;
        try
        {
            // This is the installed native data contract, independent of item code/domain.
            var json = stack.Collectible?.Attributes?["fertilizerProps"];
            if (json?.Exists != true) return false;
            var props = json.AsObject<FertilizerProps>();
            if (props == null) return false;
            float[] dose = { props.N, props.P, props.K };
            foreach (float value in dose) if (!float.IsFinite(value) || value < 0 || value > 150) return false;
            float required = dose[need.Nutrient];
            if (required <= 0 || need.Deficit <= 0) return false;
            float incidental = 0;
            for (int i = 0; i < 3; i++) if (i != need.Nutrient)
            {
                if (dose[i] > required) return false; // Never spend P fertilizer just for its incidental N.
                incidental += dose[i];
            }
            if (props.PermaBoost != null)
            {
                int[] boosts = { props.PermaBoost.N, props.PermaBoost.P, props.PermaBoost.K };
                if (string.IsNullOrWhiteSpace(props.PermaBoost.Code)) return false;
                for (int i = 0; i < 3; i++) if (boosts[i] < 0 || boosts[i] > 80 || (i != need.Nutrient && boosts[i] != 0)) return false;
                if (Array.IndexOf(need.AppliedBoosts ?? Array.Empty<string>(), props.PermaBoost.Code) < 0
                    && need.OriginalFertility + boosts[need.Nutrient] > need.Target) return false;
            }
            float useful = Math.Min(required, need.Deficit), overshoot = Math.Max(0, required - need.Deficit);
            if (overshoot > maxOvershoot || useful < required / 2) return false;
            score = (overshoot + incidental) / useful + Math.Max(0, need.Deficit - required) / 150d;
            return true;
        }
        catch { return false; }
    }

    internal static float[]? Queue(BlockEntityFarmland farm)
    {
        // No public queue getter exists in 1.22.7. Read the native serialized contract;
        // custom soil implementations are excluded before reaching this adapter.
        var tree = new TreeAttribute();
        try { farm.ToTreeAttributes(tree); }
        catch { return null; }
        if (!tree.HasAttribute("slowN") || !tree.HasAttribute("slowP") || !tree.HasAttribute("slowK")) return null;
        return new[] { tree.GetFloat("slowN"), tree.GetFloat("slowP"), tree.GetFloat("slowK") };
    }
}

// The public player interface has an internal range member in 1.22.7. Inherit
// its native implementation and explicitly forward the public identity surface.
// No real owner's selected slot, inventories or world data are changed.
internal sealed class FertilizerPlayer : Vintagestory.Server.ServerPlayer, IPlayer
{
    private readonly IPlayer owner;
    private readonly IPlayerInventoryManager inventory;
    protected override void Init() { }
    internal FertilizerPlayer(IPlayer owner, ItemSlot escrow) : base(null!, null!)
    {
        this.owner = owner;
        inventory = DispatchProxy.Create<IPlayerInventoryManager, FertilizerInventory>();
        ((FertilizerInventory)inventory).Slot = escrow;
    }
    IPlayerInventoryManager IPlayer.InventoryManager => inventory;
    public override string PlayerUID => owner.PlayerUID;
    string IPlayer.PlayerName => owner.PlayerName;
    int IPlayer.ClientId => owner.ClientId;
    Vintagestory.API.Common.EntityPlayer IPlayer.Entity => owner.Entity;
    IWorldPlayerData IPlayer.WorldData => owner.WorldData;
    IPlayerRole IPlayer.Role { get => owner.Role; set => throw new NotSupportedException(); }
    PlayerGroupMembership[] IPlayer.Groups => owner.Groups;
    System.Collections.Generic.List<Entitlement> IPlayer.Entitlements => owner.Entitlements;
    BlockSelection IPlayer.CurrentBlockSelection => owner.CurrentBlockSelection;
    EntitySelection IPlayer.CurrentEntitySelection => owner.CurrentEntitySelection;
    string[] IPlayer.Privileges => owner.Privileges;
    bool IPlayer.ImmersiveFpMode => owner.ImmersiveFpMode;
    PlayerGroupMembership[] IPlayer.GetGroups() => owner.GetGroups();
    PlayerGroupMembership IPlayer.GetGroup(int id) => owner.GetGroup(id);
    bool IPlayer.HasPrivilege(string privilege) => owner.HasPrivilege(privilege);
    bool IPlayer.IsInInteractionRangeOf(Vintagestory.API.Common.Entities.Entity entity, float slack) => owner.IsInInteractionRangeOf(entity, slack);
}
public class FertilizerInventory : DispatchProxy
{
    internal ItemSlot Slot = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        => method?.Name == "get_ActiveHotbarSlot" ? Slot : throw new NotSupportedException("Fertilizer escrow exposes only its real dose.");
}
