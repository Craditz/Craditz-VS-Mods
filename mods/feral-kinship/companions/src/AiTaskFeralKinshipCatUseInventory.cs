#nullable enable

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// Keeps Cats' mouth-inventory feeding behavior alive after Companions
/// replaces the source task list. The base task preserves Cats' normal
/// one-item consumption, animation, and vanilla hunger update; completion
/// also credits the Companions food meter.
/// </summary>
public sealed class AiTaskFeralKinshipCatUseInventory : AiTaskUseInventory
{
    private ItemStack? foodBeingEaten;
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
        consumed = false;
        base.StartExecute();
    }

    public override bool ShouldExecute()
    {
        FeralKinshipCompanionSystem system = entity.Api.ModLoader
            .GetModSystem<FeralKinshipCompanionSystem>();
        return !system.IsCompanionOwnerOffline(entity) && base.ShouldExecute();
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

        bool hadFoodBeforeTick = entity.LeftHandItemSlot?.Empty == false;
        bool continueExecuting = base.ContinueExecute(dt);

        // AiTaskUseInventory removes the item only when its use timer reaches
        // the configured duration. Do not credit a cancellation or an item
        // removed by another system during the animation.
        consumed = !continueExecuting
            && hadFoodBeforeTick
            && useTimeNow >= useTime
            && entity.LeftHandItemSlot?.Empty == true;
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
        consumed = false;
        base.FinishExecute(cancelled);
    }
}
