#nullable enable

using System;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;

/// <summary>
/// Small optional bridge to PetAI's public command behavior. Companions does
/// not reference PetAI at compile time, so removing either mod remains safe.
/// Reflection is used only when a nearby non-Companion actually has PetAI's
/// receive-command behavior.
/// </summary>
internal static class PetAiCompatibilityBridge
{
    public static bool IsOwnedPet(Entity entity, string ownerUid)
    {
        EntityBehavior? tameable = entity.GetBehavior("tameable");
        if (tameable == null) return false;

        string? directOwner = tameable.GetType().GetProperty("OwnerId")?.GetValue(tameable) as string;
        if (string.Equals(directOwner, ownerUid, StringComparison.Ordinal)) return true;

        object? cachedOwner = tameable.GetType().GetProperty("CachedOwner")?.GetValue(tameable);
        string? cachedOwnerUid = cachedOwner?.GetType().GetProperty("PlayerUID")?.GetValue(cachedOwner) as string;
        return string.Equals(cachedOwnerUid, ownerUid, StringComparison.Ordinal);
    }

    public static bool IsTaming(Entity entity)
    {
        EntityBehavior? tameable = entity.GetBehavior("tameable");
        if (tameable == null) return false;

        object? level = tameable.GetType().GetProperty("DomesticationLevel")?.GetValue(tameable);
        return string.Equals(level?.ToString(), "TAMING", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasReceiveCommand(Entity entity)
    {
        return entity.GetBehavior("receivecommand") != null;
    }

    public static bool HasClosestCommandMapping(string command)
    {
        return Map(command).HasValue;
    }

    public static bool TryApplyClosestCommand(Entity entity, EntityPlayer owner, string command)
    {
        if (!IsOwnedPet(entity, owner.PlayerUID)) return false;
        EntityBehavior? receive = entity.GetBehavior("receivecommand");
        if (receive == null) return false;

        (string typeName, string commandName)? mapped = Map(command);
        if (mapped == null) return false;

        Assembly assembly = receive.GetType().Assembly;
        Type? commandType = assembly.GetType("PetAI.Command");
        Type? commandEnumType = assembly.GetType("PetAI.EnumCommandType");
        if (commandType == null || commandEnumType == null) return false;

        object commandTypeValue;
        object petCommand;
        try
        {
            commandTypeValue = Enum.Parse(commandEnumType, mapped.Value.typeName, ignoreCase: true);
            petCommand = Activator.CreateInstance(commandType, commandTypeValue, mapped.Value.commandName)!;
        }
        catch
        {
            return false;
        }

        MethodInfo? setCommand = receive.GetType().GetMethod("SetCommand", new[] { commandType, typeof(EntityPlayer) });
        if (setCommand == null) return false;

        try
        {
            setCommand.Invoke(receive, new[] { petCommand, owner });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static (string typeName, string commandName)? Map(string command)
    {
        string category = CompanionWhistleCommand.Category(command);
        string value = CompanionWhistleCommand.Value(command);
        return category switch
        {
            CompanionWhistleCommand.ActivityCategory when value == CompanionActivityMode.Follow
                => ("COMPLEX", "followmaster"),
            CompanionWhistleCommand.ActivityCategory when value == CompanionActivityMode.AtEase
                => ("COMPLEX", "stay"),
            CompanionWhistleCommand.ActivityCategory when value == CompanionActivityMode.Rest
                => ("SIMPLE", "lay"),
            CompanionWhistleCommand.ActivityCategory when value == CompanionActivityMode.ReturnHome
                => ("COMPLEX", "stay"),
            CompanionWhistleCommand.CombatCategory when value == CompanionCombatStyle.Passive
                => ("AGGRESSIONLEVEL", "PASSIVE"),
            CompanionWhistleCommand.CombatCategory when value == CompanionCombatStyle.Defensive
                => ("AGGRESSIONLEVEL", "PROTECTIVE"),
            CompanionWhistleCommand.CombatCategory when value == CompanionCombatStyle.Protect
                => ("AGGRESSIONLEVEL", "PROTECTIVE"),
            CompanionWhistleCommand.CombatCategory when value == CompanionCombatStyle.Assist
                => ("AGGRESSIONLEVEL", "PROTECTIVE"),
            CompanionWhistleCommand.CombatCategory when value == CompanionCombatStyle.Aggressive
                => ("AGGRESSIONLEVEL", "AGGRESSIVE"),
            CompanionWhistleCommand.CombatCategory when value == CompanionCombatStyle.Flee
                => ("AGGRESSIONLEVEL", "PASSIVE"),
            _ => null
        };
    }
}
