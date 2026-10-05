using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace HideNameAddon;

public sealed class HideNameAddonModSystem : ModSystem
{
    public const string HiddenAttributeKey = "hidenameaddon.hidden";

    private const string ModDataKey = "hidenameaddon.hidden";
    private const string StoredNametagNameKey = "hidenameaddon.originalNametagName";
    private const string HarmonyId = "hidenameaddon.nametag";
    private const string NametagTreeKey = "nametag";
    private const string NametagNameKey = "name";

    private Harmony? harmony;
    private ICoreServerAPI? serverApi;

    public override void Start(ICoreAPI api)
    {
        harmony = new Harmony(HarmonyId);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        serverApi = api;

        api.ChatCommands
            .Create("hidename")
            .WithAlias("hidenametag", "namehide")
            .WithDescription("Hide or show your overhead name tag.")
            .WithAdditionalInformation("Usage: /hidename [on|off|toggle|status]")
            .WithArgs(api.ChatCommands.Parsers.OptionalWordRange("state", "on", "off", "toggle", "status"))
            .RequiresPlayer()
            .RequiresPrivilege(Privilege.chat)
            .HandleWith(OnHideNameCommand);

        api.Event.PlayerJoin += ApplyStoredHiddenState;
        api.Event.PlayerRespawn += ApplyStoredHiddenState;
        api.Event.PlayerNowPlaying += ApplyStoredHiddenState;
    }

    public override void Dispose()
    {
        if (serverApi is not null)
        {
            serverApi.Event.PlayerJoin -= ApplyStoredHiddenState;
            serverApi.Event.PlayerRespawn -= ApplyStoredHiddenState;
            serverApi.Event.PlayerNowPlaying -= ApplyStoredHiddenState;
        }

        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
    }

    private TextCommandResult OnHideNameCommand(TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player)
        {
            return TextCommandResult.Error("notplayer", "Only players can use /hidename.");
        }

        bool current = GetStoredHidden(player);
        string state = args.ArgCount > 0 ? Convert.ToString(args[0])?.ToLowerInvariant() ?? string.Empty : "toggle";

        if (state == "status")
        {
            string status = current ? "hidden" : "visible";
            return TextCommandResult.Success($"Your overhead name tag is currently {status}.", null);
        }

        bool hidden = state switch
        {
            "on" => true,
            "off" => false,
            "toggle" or "" => !current,
            _ => !current
        };

        SetStoredHidden(player, hidden);
        ApplyHiddenState(player, hidden);

        string result = hidden ? "hidden" : "visible";
        return TextCommandResult.Success($"Your overhead name tag is now {result}.", null);
    }

    private void ApplyStoredHiddenState(IPlayer player)
    {
        if (player is IServerPlayer serverPlayer)
        {
            ApplyHiddenState(serverPlayer, GetStoredHidden(serverPlayer));
        }
    }

    private static bool GetStoredHidden(IServerPlayer player)
    {
        return player.GetModData(ModDataKey, false);
    }

    private static void SetStoredHidden(IServerPlayer player, bool hidden)
    {
        player.SetModData(ModDataKey, hidden);
    }

    private static void ApplyHiddenState(IServerPlayer player, bool hidden)
    {
        EntityPlayer? entity = player.Entity;
        if (entity is null)
        {
            return;
        }

        entity.WatchedAttributes.SetBool(HiddenAttributeKey, hidden);
        SetSyncedNametagName(player, entity, hidden);

        if (entity.WatchedAttributes is SyncedTreeAttribute synced)
        {
            synced.MarkPathDirty(HiddenAttributeKey);
            synced.MarkPathDirty(NametagTreeKey);
        }
    }

    private static void SetSyncedNametagName(IServerPlayer player, EntityPlayer entity, bool hidden)
    {
        ITreeAttribute nametagTree = entity.WatchedAttributes.GetOrAddTreeAttribute(NametagTreeKey);

        if (hidden)
        {
            string? currentName = nametagTree.GetString(NametagNameKey, null);
            if (!string.IsNullOrEmpty(currentName))
            {
                player.SetModData(StoredNametagNameKey, currentName);
            }

            nametagTree.SetString(NametagNameKey, string.Empty);
            return;
        }

        string storedName = player.GetModData(StoredNametagNameKey, string.Empty);
        nametagTree.SetString(NametagNameKey, string.IsNullOrEmpty(storedName) ? player.PlayerName : storedName);
    }
}

[HarmonyPatch(typeof(EntityBehaviorNameTag), nameof(EntityBehaviorNameTag.OnRenderFrame))]
public static class EntityBehaviorNameTag_OnRenderFrame_Patch
{
    private static readonly AccessTools.FieldRef<EntityBehavior, Entity> EntityRef =
        AccessTools.FieldRefAccess<EntityBehavior, Entity>("entity");

    public static bool Prefix(EntityBehaviorNameTag __instance)
    {
        return !NameTagVisibility.IsHidden(EntityRef(__instance));
    }
}

public static class NameTagVisibility
{
    public static bool IsHidden(Entity? entity)
    {
        return entity?.WatchedAttributes.GetBool(HideNameAddonModSystem.HiddenAttributeKey, false) == true;
    }
}
