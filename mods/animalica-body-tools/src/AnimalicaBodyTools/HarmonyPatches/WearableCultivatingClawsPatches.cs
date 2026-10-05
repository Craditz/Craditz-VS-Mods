using System;
using System.Reflection;
using AnimalicaBodyTools.Config;
using AnimalicaBodyTools.Items;
using AnimalicaBodyTools.Systems;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace AnimalicaBodyTools.HarmonyPatches;

internal static class WearableCultivatingClawsClientAction
{
    private static ICoreClientAPI? api;
    private static IClientNetworkChannel? channel;
    private static ClientAction? current;

    public static void Configure(ICoreClientAPI clientApi, IClientNetworkChannel networkChannel)
    {
        api = clientApi;
        channel = networkChannel;
        current = null;
    }

    public static void Reset()
    {
        if (current != null)
        {
            current.Player.Entity.Controls.HandUse = EnumHandInteract.None;
        }

        current = null;
        channel = null;
        api = null;
    }

    public static bool TryStart(bool rightMouseDown)
    {
        if (!rightMouseDown || current != null || api?.World.Player is not IClientPlayer player || channel?.Connected != true)
        {
            return false;
        }

        EntityPlayer entity = player.Entity;
        BlockSelection? blockSel = entity.BlockSelection;
        ItemSlot? activeSlot = player.InventoryManager?.ActiveHotbarSlot;
        if (entity.Controls.HandUse != EnumHandInteract.None
            || blockSel?.Position == null
            || activeSlot == null
            || WearableToolResolver.GetRealItemstack(activeSlot) != null)
        {
            return false;
        }

        Block block = entity.World.BlockAccessor.GetBlock(blockSel.Position);
        if (block?.Code == null
            || !block.Code.PathStartsWith("soil")
            || entity.World.BlockAccessor.GetBlock(blockSel.Position.UpCopy()).BlockId != 0)
        {
            return false;
        }

        if (!WearableTargetedActionPatches.TryCreateVirtualDummySlot(
                player,
                activeSlot,
                ScoreCultivator,
                out ItemStack virtualTool,
                out _,
                out ItemSlot dummySlot,
                out _,
                out _)
            || virtualTool.Collectible is not ItemSpeciesLockedCultivatingClaws)
        {
            return false;
        }

        BlockSelection selection = new(blockSel.Position.Copy(), blockSel.Face, block);
        EnumHandHandling handling = EnumHandHandling.NotHandled;
        virtualTool.Collectible.OnHeldUseStart(
            dummySlot,
            entity,
            selection,
            null,
            EnumHandInteract.HeldItemInteract,
            true,
            ref handling);
        if (handling == EnumHandHandling.NotHandled)
        {
            return false;
        }

        long startMs = entity.World.ElapsedMilliseconds;
        entity.Controls.HandUse = EnumHandInteract.HeldItemInteract;
        entity.Controls.UsingBeginMS = startMs;
        entity.Controls.UsingCount = 0;
        current = new ClientAction(player, dummySlot, virtualTool, selection, startMs);
        channel.SendPacket(CultivatingClawsPacket.Start(selection.Position));
        return true;
    }

    public static bool TryStep(bool rightMouseDown)
    {
        ClientAction? action = current;
        if (action == null)
        {
            return false;
        }

        EntityPlayer entity = action.Player.Entity;
        ItemSlot? activeSlot = action.Player.InventoryManager?.ActiveHotbarSlot;
        BlockSelection? currentSelection = entity.BlockSelection;
        bool invalid = !rightMouseDown
            || activeSlot == null
            || WearableToolResolver.GetRealItemstack(activeSlot) != null
            || currentSelection?.Position == null
            || !currentSelection.Position.Equals(action.Selection.Position);

        float secondsUsed = (entity.World.ElapsedMilliseconds - action.StartMs) / 1000f;
        if (invalid)
        {
            action.VirtualTool.Collectible.OnHeldUseCancel(
                secondsUsed,
                action.DummySlot,
                entity,
                action.Selection,
                null,
                EnumItemUseCancelReason.ReleasedMouse);
            action.VirtualTool.Collectible.OnHeldUseStop(
                secondsUsed,
                action.DummySlot,
                entity,
                action.Selection,
                null,
                EnumHandInteract.HeldItemInteract);
            channel?.SendPacket(CultivatingClawsPacket.Cancel());
            Finish(entity);
            return true;
        }

        bool keepUsing = action.VirtualTool.Collectible.OnHeldUseStep(
            secondsUsed,
            action.DummySlot,
            entity,
            action.Selection,
            null) != EnumHandInteract.None;
        entity.Controls.UsingCount++;

        if (!keepUsing)
        {
            action.VirtualTool.Collectible.OnHeldUseStop(
                secondsUsed,
                action.DummySlot,
                entity,
                action.Selection,
                null,
                EnumHandInteract.HeldItemInteract);
            Finish(entity);
        }

        return true;
    }

    private static void Finish(EntityPlayer entity)
    {
        entity.Controls.HandUse = EnumHandInteract.None;
        current = null;
    }

    private static float ScoreCultivator(ItemStack stack)
    {
        return stack.Collectible is ItemSpeciesLockedCultivatingClaws
               && stack.Collectible.Tool == EnumTool.Hoe
            ? 1f
            : 0f;
    }

    private sealed class ClientAction
    {
        public ClientAction(
            IClientPlayer player,
            ItemSlot dummySlot,
            ItemStack virtualTool,
            BlockSelection selection,
            long startMs)
        {
            Player = player;
            DummySlot = dummySlot;
            VirtualTool = virtualTool;
            Selection = selection;
            StartMs = startMs;
        }

        public IClientPlayer Player { get; }
        public ItemSlot DummySlot { get; }
        public ItemStack VirtualTool { get; }
        public BlockSelection Selection { get; }
        public long StartMs { get; }
    }
}

[HarmonyPatch]
internal static class WearableCultivatingClawsClientStartPatch
{
    private static readonly Type? TargetType =
        AccessTools.TypeByName("Vintagestory.Client.NoObf.SystemMouseInWorldInteractions");
    private static readonly FieldInfo? GameField =
        TargetType == null ? null : AccessTools.Field(TargetType.BaseType, "game");

    private static MethodBase? TargetMethod()
    {
        return TargetType == null ? null : AccessTools.Method(TargetType, "HandleMouseInteractionsBlockSelected");
    }

    private static bool Prefix(object __instance)
    {
        object? game = GameField?.GetValue(__instance);
        MouseButtonState? mouse = game == null
            ? null
            : AccessTools.Field(game.GetType(), "InWorldMouseState")?.GetValue(game) as MouseButtonState;
        return !WearableCultivatingClawsClientAction.TryStart(mouse?.Right == true);
    }
}

[HarmonyPatch]
internal static class WearableCultivatingClawsClientStepPatch
{
    private static readonly Type? TargetType =
        AccessTools.TypeByName("Vintagestory.Client.NoObf.SystemMouseInWorldInteractions");
    private static readonly FieldInfo? GameField =
        TargetType == null ? null : AccessTools.Field(TargetType.BaseType, "game");

    private static MethodBase? TargetMethod()
    {
        return TargetType == null ? null : AccessTools.Method(TargetType, "HandleHandInteraction");
    }

    private static bool Prefix(object __instance)
    {
        object? game = GameField?.GetValue(__instance);
        MouseButtonState? mouse = game == null
            ? null
            : AccessTools.Field(game.GetType(), "InWorldMouseState")?.GetValue(game) as MouseButtonState;
        return !WearableCultivatingClawsClientAction.TryStep(mouse?.Right == true);
    }
}
