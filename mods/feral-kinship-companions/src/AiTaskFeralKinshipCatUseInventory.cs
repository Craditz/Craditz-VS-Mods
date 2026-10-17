#nullable enable

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// Keeps native pet mouth-inventory feeding behavior alive after Companions
/// replaces the source task list. The base task preserves Cats' normal
/// one-item consumption, animation, and vanilla hunger update; completion
/// also credits the Companions food meter for a diet-valid meal. The historic
/// task code remains registered for compatibility; Dogs use this adapter too.
/// </summary>
public sealed class AiTaskFeralKinshipCatUseInventory : AiTaskUseInventory
{
    private ItemStack? foodBeingEaten;
    private ItemStack? sourceStack;
    private bool consumed;

    public AiTaskFeralKinshipCatUseInventory(
        EntityAgent entity,
        JsonObject taskConfig,
        JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
    }

    public override void StartExecute()
    {
        foodBeingEaten = entity.LeftHandItemSlot?.Itemstack?.Clone();
        sourceStack = entity.LeftHandItemSlot?.Itemstack;
        consumed = false;
        base.StartExecute();
    }

    public override bool ShouldExecute()
    {
        FeralKinshipCompanionSystem system = entity.Api.ModLoader
            .GetModSystem<FeralKinshipCompanionSystem>();
        if (system.IsCompanionOwnerOffline(entity)
            || FeralKinshipCompanionSystem.IsFoxAwayFromWorld(entity)
            || FeralKinshipCompanionSystem.IsFoxIncapacitated(entity)) return false;
        // Native breeding saturation and Companion hunger are different meters.
        // A hungry Companion can finish an already offered, diet-valid meal
        // while retaining normal timing, cooldown and task preconditions.
        ItemStack? held = entity.LeftHandItemSlot?.Itemstack;
        if (held != null && FeralKinshipCompanionSystem.ShouldCompanionSeekFood(entity)
            && cooldownUntilMs <= entity.World.ElapsedMilliseconds
            && cooldownUntilTotalHours <= entity.World.Calendar.TotalHours
            && PreconditionsSatisfied()
            && FeralKinshipCompanionSystem.IsCompanionFoodAccepted(entity, held))
        {
            isEdible = true;
            return true;
        }
        return base.ShouldExecute();
    }

    public override bool ContinueExecute(float dt)
    {
        FeralKinshipCompanionSystem system = entity.Api.ModLoader
            .GetModSystem<FeralKinshipCompanionSystem>();
        if (system.IsCompanionOwnerOffline(entity))
        {
            consumed = false;
            return false;
        }

        ItemStack? current = entity.LeftHandItemSlot?.Itemstack;
        if (!ReferenceEquals(sourceStack, current)) return false;
        int before = current?.StackSize ?? 0;
        bool continueExecuting = base.ContinueExecute(dt);

        // AiTaskUseInventory removes the item only when its use timer reaches
        // the configured duration. Do not credit a cancellation or an item
        // removed by another system during the animation.
        consumed = CompanionHeldFoodCompletion.WasConsumed(
            before, entity.LeftHandItemSlot?.Itemstack?.StackSize ?? 0,
            continueExecuting, useTimeNow, useTime);
        return continueExecuting;
    }

    public override void FinishExecute(bool cancelled)
    {
        if (!cancelled && consumed && foodBeingEaten != null
            && FeralKinshipCompanionSystem.IsTamedFox(entity))
        {
            entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()
                .CreditCompanionFoodFromHeldItem(entity, foodBeingEaten);
        }

        foodBeingEaten = null;
        sourceStack = null;
        consumed = false;
        base.FinishExecute(cancelled);
    }
}

internal static class CompanionHeldFoodCompletion
{
    internal static bool WasConsumed(int before, int after, bool continues, float elapsed, float duration) =>
        !continues && before > 0 && after == before - 1 && elapsed >= duration;
}
