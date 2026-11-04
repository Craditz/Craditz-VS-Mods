using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace FeralKinship;

public sealed partial class FeralKinshipSystem
{
    private static void RegisterKinshipCommands(ICoreServerAPI api)
    {
        var parsers = api.ChatCommands.Parsers;
        api.ChatCommands.Create("kinship")
            .WithDescription("Administratively assign or clear animal kinship without changing appearance.")
            .RequiresPrivilege(Privilege.controlserver)
            .BeginSubCommand("set")
                .WithDescription("Set an online player's kinship. Choices: " + FeralRaceResolver.SupportedRaceNames)
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.OnlinePlayer("player"), parsers.Word("kinship"))
                .HandleWith(args => ApplyKinshipOverride((args[0] as IPlayer)?.Entity, args[1] as string, clear: false))
            .EndSubCommand()
            .BeginSubCommand("clear")
                .WithDescription("Clear an online player's override and restore automatic model detection.")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.OnlinePlayer("player"))
                .HandleWith(args => ApplyKinshipOverride((args[0] as IPlayer)?.Entity, null, clear: true))
            .EndSubCommand();
    }

    internal static TextCommandResult ApplyKinshipOverride(EntityPlayer? target, string? race, bool clear)
    {
        if (target == null) return TextCommandResult.Error("The target player must be online with a loaded entity.");
        string normalized = (race ?? string.Empty).Trim().ToLowerInvariant();
        if (!clear && !FeralRaceResolver.IsSupportedRace(normalized))
            return TextCommandResult.Error("Unknown kinship. Choose: " + FeralRaceResolver.SupportedRaceNames);

        if (clear) target.WatchedAttributes.RemoveAttribute(FeralRaceResolver.ExplicitRaceAttribute);
        else target.WatchedAttributes.SetString(FeralRaceResolver.ExplicitRaceAttribute, normalized);
        target.WatchedAttributes.MarkPathDirty(FeralRaceResolver.ExplicitRaceAttribute);
        string effective = FeralRaceResolver.GetRace(target) ?? "none (no supported model detected)";
        return TextCommandResult.Success(clear
            ? "Kinship override cleared. Automatic kinship: " + effective + ". Appearance unchanged."
            : "Kinship override set to " + effective + ". Appearance unchanged.");
    }
}
