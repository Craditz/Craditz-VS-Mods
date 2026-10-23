#nullable enable
using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;

/// <summary>
/// Native client dispatch tries held-item use before sending entity interaction.
/// Give a supported feeding attempt its normal entity route instead of beginning
/// player eating. All food transactions and refusals remain server-owned.
/// </summary>
internal static class FeralKinshipFoodInteractionPatch
{
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(CollectibleObject), nameof(CollectibleObject.OnHeldUseStart)),
            prefix: new HarmonyMethod(typeof(FeralKinshipFoodInteractionPatch), nameof(BeforeHeldUse))
            {
                priority = Priority.First,
                before = new[] { "TraitRestrictionsItemPermissions", "animalicacore.modular-diets" }
            });
        foreach (Type type in new[] { typeof(Entity), typeof(EntityAgent) })
            harmony.Patch(AccessTools.DeclaredMethod(type, nameof(Entity.OnInteract)),
                prefix: new HarmonyMethod(typeof(FeralKinshipFoodInteractionPatch), nameof(BeforeClientEntityInteract)));
    }

    private static bool BeforeClientEntityInteract(Entity __instance, EntityAgent __0, ItemSlot __1, EnumInteractMode __3)
    {
        // The native mouse dispatcher still sends its ordinary server packet.
        // Wild taming belongs to the server; avoid predicting a food removal
        // before its age/species/owner/cooldown gates have accepted the treat.
        if (__instance.Api?.Side != EnumAppSide.Client || __0?.Api?.Side != EnumAppSide.Client
            || __3 != EnumInteractMode.Interact || __0 is not EntityPlayer player || player.Controls.Sneak
            || __instance is not EntityAgent target || !target.Alive || __1?.Itemstack == null
            || target.Pos.Dimension != player.Pos.Dimension || target.Pos.SquareDistanceTo(player.Pos) > 64d) return true;
        if (!FeralKinshipCompanionSystem.IsTamedFox(target))
            return !IsWildTamingFood(target, player, __1.Itemstack);
        if (FeralKinshipCompanionSystem.IsFoxIncapacitated(target)
            || FeralKinshipCompanionSystem.IsFoxAwayFromWorld(target)
            || !string.Equals(FeralKinshipCompanionSystem.GetCompanionOwnerUid(target), player.PlayerUID, StringComparison.Ordinal)
            || !FeralKinshipCompanionSystem.IsCompanionFoodAccepted(target, __1.Itemstack)) return true;
        // Preserve hungry owned prediction; a full companion must not lose a
        // local PetAI treat before the server hunger handler refuses it.
        return !FeralKinshipCompanionSystem.IsCompanionFullForHandFeed(target);
    }

    private static bool BeforeHeldUse(ItemSlot slot, EntityAgent byEntity,
        BlockSelection? blockSel, EntitySelection? entitySel, EnumHandInteract useType,
        ref EnumHandHandling handling)
    {
        if (byEntity.Api.Side != EnumAppSide.Client || useType != EnumHandInteract.HeldItemInteract
            || blockSel != null || byEntity is not EntityPlayer player || player.Controls.Sneak
            || entitySel?.Entity is not EntityAgent target || !target.Alive
            || slot?.Itemstack == null || target.Pos.Dimension != player.Pos.Dimension
            || target.Pos.SquareDistanceTo(player.Pos) > 64d)
            return true;

        bool feeding;
        if (FeralKinshipCompanionSystem.IsTamedFox(target))
        {
            feeding = !FeralKinshipCompanionSystem.IsFoxIncapacitated(target)
                && !FeralKinshipCompanionSystem.IsFoxAwayFromWorld(target)
                && string.Equals(FeralKinshipCompanionSystem.GetCompanionOwnerUid(target),
                    player.PlayerUID, StringComparison.Ordinal)
                && FeralKinshipCompanionSystem.IsCompanionFoodAccepted(target, slot.Itemstack);
        }
        else
        {
            feeding = IsWildTamingFood(target, player, slot.Itemstack);
        }
        if (!feeding) return true;

        // NotHandled makes the native mouse dispatcher send exactly its usual
        // entity interaction, without starting a held-item eating lifecycle.
        // Do not consume, predict progress, or call PetAI from this prefix.
        handling = EnumHandHandling.NotHandled;
        return false;
    }

    private static bool IsWildTamingFood(Entity target, EntityPlayer player, ItemStack food)
    {
        if (target.Code.Domain != "game") return false;
        EntityBehavior? tameable = target.GetBehavior("tameable");
        if (tameable?.GetType().FullName != "PetAI.EntityBehaviorTameable") return false;
        var status = target.WatchedAttributes.GetTreeAttribute("domesticationstatus");
        string level = status?.GetString("domesticationLevel", "WILD") ?? "WILD";
        if (level != "WILD" && level != "TAMING") return false;
        string owner = status?.GetString("owner", string.Empty) ?? string.Empty;
        if (owner.Length != 0 && !string.Equals(owner, player.PlayerUID, StringComparison.Ordinal)) return false;
        if (tameable.GetType().GetField("treatList")?.GetValue(tameable) is not IEnumerable treats) return false;
        AssetLocation code = food.Collectible.Code;
        foreach (object treat in treats)
        {
            Type type = treat.GetType();
            string? domain = ReadString(type, treat, "Domain");
            string? name = ReadString(type, treat, "Name");
            if (!string.Equals(domain, code.Domain, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(name)) continue;
            if (name.EndsWith("*", StringComparison.Ordinal)
                ? code.Path.StartsWith(name[..^1], StringComparison.OrdinalIgnoreCase)
                : string.Equals(name, code.Path, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string? ReadString(Type type, object value, string name)
        => (type.GetProperty(name)?.GetValue(value) ?? type.GetField(name)?.GetValue(value)) as string;
}
