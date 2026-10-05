#nullable enable

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;

/// <summary>
/// PetAI opens its naming profile from a private client message handler after
/// taming. Companions owns the naming flow, so suppress the PetAI profile
/// message and the profile dialog itself while this mod is installed.
/// </summary>
internal static class FeralKinshipPetAiNamePromptPatch
{
    private const string HarmonyId = "feralkinshipcompanions.petai-name-prompt";
    private static Harmony? harmony;
    private static ICoreClientAPI? clientApi;

    internal static void TryApply(ICoreClientAPI api)
    {
        if (harmony != null)
        {
            return;
        }

        clientApi = api;
        Type? petAiSystemType = AccessTools.TypeByName("PetAI.PetAI");
        MethodInfo? profileHandler = petAiSystemType == null
            ? null
            : AccessTools.Method(petAiSystemType, "OnPetProfileMessageClient");

        try
        {
            harmony = new Harmony(HarmonyId);
            if (profileHandler != null)
            {
                harmony.Patch(
                    profileHandler,
                    prefix: new HarmonyMethod(
                        typeof(FeralKinshipPetAiNamePromptPatch),
                        nameof(ProfileMessagePrefix))
                );
            }

            MethodInfo? tryOpen = AccessTools.Method(
                typeof(GuiDialog),
                nameof(GuiDialog.TryOpen),
                Type.EmptyTypes);
            MethodInfo? tryOpenWithFocus = AccessTools.Method(
                typeof(GuiDialog),
                nameof(GuiDialog.TryOpen),
                new[] { typeof(bool) });
            HarmonyMethod openPrefix = new(
                typeof(FeralKinshipPetAiNamePromptPatch),
                nameof(DialogOpenPrefix));
            if (tryOpen != null)
            {
                harmony.Patch(tryOpen, prefix: openPrefix);
            }
            if (tryOpenWithFocus != null)
            {
                harmony.Patch(tryOpenWithFocus, prefix: openPrefix);
            }

            api.Logger.Notification(
                "[FeralKinshipCompanions] PetAI name prompt suppression is active."
            );
        }
        catch (Exception exception)
        {
            harmony = null;
            api.Logger.Warning(
                "[FeralKinshipCompanions] Could not suppress PetAI's name prompt: {0}",
                exception.Message
            );
        }
    }

    internal static void Remove()
    {
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
        clientApi = null;
    }

    internal static void CloseOpenPetAiProfile(long targetEntityId)
    {
        if (clientApi?.Gui.OpenedGuis == null)
        {
            return;
        }

        foreach (GuiDialog dialog in clientApi.Gui.OpenedGuis.ToArray())
        {
            if (IsPetAiProfile(dialog)
                && TryGetTargetEntityId(dialog, out long dialogEntityId)
                && dialogEntityId == targetEntityId)
            {
                dialog.TryClose();
            }
        }
    }

    private static bool ProfileMessagePrefix(object __0)
    {
        return false;
    }

    private static bool DialogOpenPrefix(GuiDialog __instance)
    {
        return !IsPetAiProfile(__instance);
    }

    private static bool IsPetAiProfile(object dialog)
    {
        return string.Equals(
            dialog.GetType().FullName,
            "PetAI.PetProfileGUI",
            StringComparison.Ordinal);
    }

    private static bool TryGetTargetEntityId(object dialog, out long targetEntityId)
    {
        targetEntityId = 0;
        FieldInfo? targetField = dialog.GetType().GetField(
            "targetEntityId",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return targetField?.GetValue(dialog) is long value
            && (targetEntityId = value) != 0;
    }
}
