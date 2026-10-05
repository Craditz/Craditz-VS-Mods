using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PetAI;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace FeralKinship;

/// <summary>
/// Applies the player-specific Kinship taming advantage without changing the
/// shared PetAI treat values for ordinary players. Perfect PetAI cookies are
/// allowed to complete a matching kinship bond in one interaction.
/// </summary>
internal static class PetAiTamingCompatibility
{
    private const string HarmonyId = "feralkinship.taming-bonus";
    private static readonly Harmony Harmony = new(HarmonyId);

    public static void Install(ICoreAPI api)
    {
        MethodInfo? method = typeof(EntityBehaviorTameable).GetMethod(
            nameof(EntityBehaviorTameable.OnInteract),
            BindingFlags.Instance | BindingFlags.Public
        );
        MethodInfo? prefix = typeof(PetAiTamingCompatibility).GetMethod(
            nameof(Prefix),
            BindingFlags.Static | BindingFlags.NonPublic
        );
        MethodInfo? postfix = typeof(PetAiTamingCompatibility).GetMethod(
            nameof(Postfix),
            BindingFlags.Static | BindingFlags.NonPublic
        );
        MethodInfo? heldForwardPostfix = typeof(PetAiTamingCompatibility).GetMethod(
            nameof(HeldFoodForwardPostfix),
            BindingFlags.Static | BindingFlags.NonPublic
        );
        MethodInfo? heldForwardPrefix = typeof(PetAiTamingCompatibility).GetMethod(
            nameof(HeldFoodForwardPrefix),
            BindingFlags.Static | BindingFlags.NonPublic
        );

        if (method == null
            || prefix == null
            || postfix == null
            || heldForwardPostfix == null
            || heldForwardPrefix == null)
        {
            api.Logger.Warning(
                "[feralkinship] Could not install the Kinship taming bonus; PetAI's tameable interaction method was not found."
            );
            return;
        }

        try
        {
            bool alreadyInstalled = Harmony.GetPatchInfo(method)?.Prefixes.Any(
                patch => string.Equals(patch.owner, HarmonyId, StringComparison.Ordinal)
            ) == true;
            if (!alreadyInstalled)
            {
                Harmony.Patch(
                    method,
                    prefix: new HarmonyMethod(prefix),
                    postfix: new HarmonyMethod(postfix)
                );
            }

            InstallHeldFoodForwardGuard(heldForwardPrefix, heldForwardPostfix);

            api.Logger.Notification(
                "[feralkinship] Installed the optional PetAI Kinship taming bonus."
            );
        }
        catch (Exception exception)
        {
            api.Logger.Warning(
                "[feralkinship] Could not install the Kinship taming bonus: {0}",
                exception.Message
            );
        }
    }

    private sealed class AdjustmentState
    {
        public AdjustmentState(TamingItem item, float originalProgress)
        {
            Item = item;
            OriginalProgress = originalProgress;
        }

        public TamingItem Item { get; }
        public float OriginalProgress { get; }
    }

    private sealed class HeldFoodState
    {
        public HeldFoodState(
            EntityBehaviorTameable tameable,
            ItemSlot slot,
            int stackSize,
            DomesticationLevel domesticationLevel,
            float progress)
        {
            Tameable = tameable;
            Slot = slot;
            StackSize = stackSize;
            DomesticationLevel = domesticationLevel;
            Progress = progress;
        }

        public EntityBehaviorTameable Tameable { get; }
        public ItemSlot Slot { get; }
        public int StackSize { get; }
        public DomesticationLevel DomesticationLevel { get; }
        public float Progress { get; }
    }

    private static bool Prefix(
        EntityBehaviorTameable __instance,
        EntityAgent byEntity,
        ItemSlot itemslot,
        EnumInteractMode mode,
        out AdjustmentState? __state
    )
    {
        __state = null;

        if (mode != EnumInteractMode.Interact
            || byEntity is not EntityPlayer player
            || __instance.entity is not EntityAgent animal
            || !animal.Alive
            || (__instance.DomesticationLevel != DomesticationLevel.WILD
                && __instance.DomesticationLevel != DomesticationLevel.TAMING)
            )
        {
            return true;
        }

        // PetAI's food interaction calls this behavior directly, bypassing
        // the server OnPlayerInteractEntity event. Enforce species and
        // source-domain gates before PetAI can advance domestication.
        if (FeralKinshipSystem.ShouldBlockTamingInteraction(animal, itemslot))
        {
            return false;
        }

        if (!FeralKinshipSystem.IsAcceptedTamingTreat(animal, itemslot)
            || !FeralKinshipSystem.IsMatchingKin(animal, player.Player))
        {
            if (FeralKinshipSystem.IsAcceptedTamingTreat(animal, itemslot))
            {
                FeralKinshipSystem.MarkBabyTamingTarget(animal);
            }
            return true;
        }

        FeralKinshipSystem.MarkBabyTamingTarget(animal);

        if (!string.IsNullOrEmpty(__instance.OwnerId)
            && !string.Equals(__instance.OwnerId, player.PlayerUID, StringComparison.Ordinal))
        {
            return true;
        }

        TamingItem? item = FindTamingItem(__instance, itemslot);
        if (item == null)
        {
            return true;
        }

        float originalProgress = item.Progress;
        if (FeralKinshipSystem.IsPerfectCookie(itemslot))
        {
            // PetAI checks for completion only in its TAMING branch. Moving a
            // wild entity into that branch lets its own conversion code run,
            // preserving ownership and all normal tame-variant transfer.
            if (__instance.DomesticationLevel == DomesticationLevel.WILD)
            {
                __instance.DomesticationLevel = DomesticationLevel.TAMING;
                __instance.OwnerId = player.PlayerUID;
            }

            item.Progress = 1f;
        }
        else
        {
            item.Progress = Math.Min(
                1f,
                originalProgress * FeralKinshipSystem.KinTamingProgressMultiplier
            );
        }

        __state = new AdjustmentState(item, originalProgress);
        return true;
    }

    private static void Postfix(AdjustmentState? __state)
    {
        if (__state != null)
        {
            __state.Item.Progress = __state.OriginalProgress;
        }
    }

    private static void InstallHeldFoodForwardGuard(
        MethodInfo heldForwardPrefix,
        MethodInfo heldForwardPostfix)
    {
        Type? behaviorType = typeof(EntityBehaviorTameable).Assembly.GetType(
            "PetAI.BehaviorConsiderHumanFoodForPetsToo"
        );
        MethodInfo? method = behaviorType?.GetMethod(
            "OnHeldInteractStart",
            BindingFlags.Instance | BindingFlags.Public
        );
        if (method == null)
        {
            return;
        }

        var patchInfo = Harmony.GetPatchInfo(method);
        bool prefixInstalled = patchInfo?.Prefixes.Any(
            patch => string.Equals(patch.owner, HarmonyId, StringComparison.Ordinal)
        ) == true;
        bool postfixInstalled = patchInfo?.Postfixes.Any(
            patch => string.Equals(patch.owner, HarmonyId, StringComparison.Ordinal)
        ) == true;
        if (!prefixInstalled || !postfixInstalled)
        {
            Harmony.Patch(
                method,
                prefix: prefixInstalled ? null : new HarmonyMethod(heldForwardPrefix),
                postfix: postfixInstalled ? null : new HarmonyMethod(heldForwardPostfix)
            );
        }
    }

    private static void HeldFoodForwardPrefix(
        ItemSlot slot,
        EntityAgent byEntity,
        EntitySelection entitySel,
        out HeldFoodState? __state)
    {
        __state = null;
        Entity? target = entitySel?.Entity;
        if (target is not EntityAgent agent
            || !agent.Alive
            || byEntity is not EntityPlayer
            || target.GetBehavior<EntityBehaviorTameable>() is not EntityBehaviorTameable tameable
            || !FeralKinshipSystem.IsButcheringTamingTreatEligible(target, slot))
        {
            return;
        }

        __state = new HeldFoodState(
            tameable,
            slot,
            slot?.Itemstack?.StackSize ?? 0,
            tameable.DomesticationLevel,
            tameable.DomesticationProgress
        );
    }

    private static void HeldFoodForwardPostfix(
        HeldFoodState? __state,
        ref EnumHandling handling)
    {
        if (__state == null)
        {
            return;
        }

        // PetAI's forwarding behavior invokes Tameable.OnInteract and then
        // continues through the held item's normal use path. Only suppress
        // that later path when PetAI actually accepted this Butchering item.
        int remaining = __state.Slot?.Itemstack?.StackSize ?? 0;
        bool consumed = remaining < __state.StackSize
            || __state.Tameable.DomesticationLevel != __state.DomesticationLevel
            || __state.Tameable.DomesticationProgress > __state.Progress;
        if (consumed)
        {
            // PetAI can decrement a stack through TakeOut without marking
            // the server slot dirty. Keep stacks greater than one in sync
            // before suppressing the normal edible-item path.
            HeldFoodState state = __state;
            state.Slot?.MarkDirty();
            handling = EnumHandling.PreventSubsequent;
        }
    }

    private static TamingItem? FindTamingItem(EntityBehaviorTameable tameable, ItemSlot slot)
    {
        string domain = slot?.Itemstack?.Collectible?.Code?.Domain ?? string.Empty;
        string path = slot?.Itemstack?.Collectible?.Code?.Path ?? string.Empty;
        if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(path))
        {
            return null;
        }

        return tameable.treatList.Find(item =>
            string.Equals(item.Domain, domain, StringComparison.OrdinalIgnoreCase)
            && (item.Name.EndsWith("*", StringComparison.Ordinal)
                ? path.StartsWith(item.Name[..^1], StringComparison.OrdinalIgnoreCase)
                : string.Equals(item.Name, path, StringComparison.OrdinalIgnoreCase))
        );
    }
}
