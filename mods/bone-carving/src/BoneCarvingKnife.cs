using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace BoneCarving;

public sealed class BoneCarvingKnife : ItemKnife
{
    private const float CarveSeconds = 2f;
    private const string Animation = "knifecut";
    private readonly ConditionalWeakTable<EntityAgent, CarvingSession> sessions = new();
    private Item? handle;

    private sealed class CarvingSession(ItemSlot knifeSlot, ItemStack knife, ItemSlot boneSlot, ItemStack bone)
    {
        public readonly ItemSlot KnifeSlot = knifeSlot;
        public readonly ItemStack Knife = knife;
        public readonly ItemSlot BoneSlot = boneSlot;
        public readonly ItemStack Bone = bone;
        public bool Aborted = bone.StackSize != 1;
        public bool Completed;
    }

    private static bool IsBone(ItemStack? stack) =>
        stack?.Collectible.Code.Domain == "game" && stack.Collectible.Code.Path == "bone";

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity,
        BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        if (!firstEvent && sessions.TryGetValue(byEntity, out _))
        {
            handling = EnumHandHandling.PreventDefault;
            return;
        }

        sessions.Remove(byEntity);
        var offhand = byEntity.LeftHandItemSlot;
        if (byEntity is not EntityPlayer || !ReferenceEquals(slot, byEntity.RightHandItemSlot)
            || slot.Itemstack?.Collectible != this || !IsBone(offhand?.Itemstack))
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            return;
        }

        // Claim the entire interaction, including invalid bone stacks. Do not fall
        // through into carcass harvesting when carving was attempted.
        handling = EnumHandHandling.PreventDefault;
        var session = new CarvingSession(slot, slot.Itemstack!, offhand!, offhand!.Itemstack!);
        sessions.Add(byEntity, session);
        if (session.Aborted)
        {
            if (firstEvent && api is ICoreClientAPI client)
                client.TriggerIngameError(this, "onebone", Lang.Get("bonecarving:error-onebone"));
            return;
        }

        byEntity.StartAnimation(Animation);
    }

    private bool StillValid(EntityAgent entity, ItemSlot slot, CarvingSession session) =>
        !session.Aborted && !session.Completed && entity.Alive
        && ReferenceEquals(slot, session.KnifeSlot)
        && ReferenceEquals(entity.RightHandItemSlot, session.KnifeSlot)
        && ReferenceEquals(entity.LeftHandItemSlot, session.BoneSlot)
        && ReferenceEquals(slot.Itemstack, session.Knife)
        && ReferenceEquals(slot.Itemstack?.Collectible, this)
        && ReferenceEquals(session.BoneSlot.Itemstack, session.Bone)
        && IsBone(session.BoneSlot.Itemstack) && session.BoneSlot.StackSize == 1;

    public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity,
        BlockSelection blockSel, EntitySelection entitySel)
    {
        if (!sessions.TryGetValue(byEntity, out var session))
            return base.OnHeldInteractStep(secondsUsed, slot, byEntity, blockSel, entitySel);

        if (!StillValid(byEntity, slot, session))
        {
            session.Aborted = true;
            byEntity.StopAnimation(Animation);
            return false;
        }

        if (secondsUsed >= CarveSeconds && api.Side == EnumAppSide.Server)
        {
            Complete(byEntity, slot, session);
            byEntity.StopAnimation(Animation);
            return false;
        }

        // Brief client grace lets the authoritative server finish its own timer.
        return secondsUsed < CarveSeconds + 0.2f;
    }

    private void Complete(EntityAgent entity, ItemSlot slot, CarvingSession session)
    {
        if (api.Side != EnumAppSide.Server || !StillValid(entity, slot, session)) return;
        handle ??= api.World.GetItem(new AssetLocation("bonecarving", "bonehandle"));
        if (handle == null)
        {
            session.Aborted = true;
            api.Logger.Error("[Bone Carving] Missing bonehandle item; leaving bone untouched.");
            return;
        }

        session.Completed = true;
        // Exact one-for-one replacement avoids inventory-full losses and duplication.
        session.BoneSlot.Itemstack = new ItemStack(handle, 1);
        session.BoneSlot.MarkDirty();
    }

    public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity,
        BlockSelection blockSel, EntitySelection entitySel)
    {
        if (!sessions.TryGetValue(byEntity, out var session))
        {
            base.OnHeldInteractStop(secondsUsed, slot, byEntity, blockSel, entitySel);
            return;
        }

        // Stop can arrive between server step ticks at the completion boundary.
        if (secondsUsed >= CarveSeconds) Complete(byEntity, slot, session);
        byEntity.StopAnimation(Animation);
        sessions.Remove(byEntity);
    }

    public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity,
        BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
    {
        if (!sessions.TryGetValue(byEntity, out _))
            return base.OnHeldInteractCancel(secondsUsed, slot, byEntity, blockSel, entitySel, cancelReason);

        byEntity.StopAnimation(Animation);
        sessions.Remove(byEntity);
        return true;
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot) =>
        [.. base.GetHeldInteractionHelp(inSlot), new WorldInteraction
        {
            ActionLangCode = "bonecarving:heldhelp-carve",
            MouseButton = EnumMouseButton.Right
        }];
}
