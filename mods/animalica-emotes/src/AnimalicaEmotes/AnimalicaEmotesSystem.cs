using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace AnimalicaEmotes;

public sealed class AnimalicaEmotesSystem : ModSystem
{
    public const string ActivePoseAttribute = "animalicaEmotesActivePose";
    public const string AnimationMetaPrefix = "animalicaemotes-";
    public const string MenuHotkeyCode = "animalicaemotes-menu";

    private const string NetworkChannelName = "animalicaemotes:main";
    private const int ClientSynchronizationIntervalMs = 100;
    private static readonly HashSet<string> SupportedPoses = new(StringComparer.Ordinal)
    {
        "lie",
        "sleep",
        "loaf",
        "sniff",
        "howl",
        "roar",
        "stalk",
        "threat",
        "stot",
        "dig",
        "longdig",
        "gather",
        "peck",
        "roostercall",
        "flopping",
        "idle-land",
        "rising-eat",
        "foxidle-test-calm",
        "foxidle-test-alert",
        "foxidle-test-tailflick",
        "foxidle-test-pawlift",
        "foxmove-test-sneakwalk",
        "foxmove-test-sneakidle",
        "foxcompare-handoff-sneakwalk",
        "sneakwalk",
        "sneakidle",
        "newjump",
        "creativefly",
        "swim",
        "swimidle",
        "ladderup",
        "ladderidle",
        "hammerhit",
        "scythe",
        "foxaction-test-scythe",
        "smithing",
        "smithingwide",
        "hoe",
        "pour",
        "water",
        "falx",
        "eat",
        "panning",
        "axehit",
        "axechop",
        "pickaxe"
    };

    private static readonly AnimalEmoteOption[] WolfPoses =
    {
        new("sleep", "pose-sleep"),
        new("sniff", "pose-sniff"),
        new("howl", "pose-howl"),
        new("stalk", "pose-stalk"),
        new("threat", "pose-threat")
    };

    private static readonly AnimalEmoteOption[] FoxPoses =
    {
        new("foxidle-test-calm", "pose-fox-idle-calm"),
        new("foxidle-test-alert", "pose-fox-idle-alert"),
        new("foxidle-test-tailflick", "pose-fox-idle-tail-flick"),
        new("foxidle-test-pawlift", "pose-fox-idle-paw-lift"),
        new("sneakwalk", "pose-fox-test-sneakwalk"),
        new("sneakidle", "pose-fox-test-sneakidle"),
        new("foxcompare-handoff-sneakwalk", "pose-fox-test-handoff-sneakwalk"),
        new("foxmove-test-sneakidle", "pose-fox-test-previous-sneakidle"),
        new("hammerhit", "pose-fox-test-hammerhit"),
        new("foxaction-test-scythe", "pose-fox-test-scythe"),
        new("smithing", "pose-fox-test-smithing"),
        new("smithingwide", "pose-fox-test-smithingwide"),
        new("hoe", "pose-fox-test-hoe"),
        new("pour", "pose-fox-test-pour"),
        new("water", "pose-fox-test-water"),
        new("falx", "pose-fox-test-falx"),
        new("eat", "pose-fox-test-eat"),
        new("panning", "pose-fox-test-panning"),
        new("axehit", "pose-fox-test-axehit"),
        new("axechop", "pose-fox-test-axechop"),
        new("pickaxe", "pose-fox-test-pickaxe"),
        new("newjump", "pose-fox-test-jump"),
        new("creativefly", "pose-fox-test-fly"),
        new("swim", "pose-fox-test-swim"),
        new("swimidle", "pose-fox-test-swimidle"),
        new("ladderup", "pose-fox-test-climbup"),
        new("ladderidle", "pose-fox-test-climbidle"),
        new("sleep", "pose-sleep"),
        new("sniff", "pose-sniff"),
        new("howl", "pose-howl")
    };

    private static readonly AnimalEmoteOption[] HyenaPoses =
    {
        new("lie", "pose-lie"),
        new("sleep", "pose-sleep"),
        new("sniff", "pose-sniff"),
        new("stalk", "pose-stalk")
    };

    private static readonly AnimalEmoteOption[] BearPoses =
    {
        new("lie", "pose-lie"),
        new("sleep", "pose-sleep"),
        new("sniff", "pose-sniff"),
        new("stalk", "pose-stalk")
    };

    private static readonly AnimalEmoteOption[] PantherPoses =
    {
        new("lie", "pose-lie"),
        new("sleep", "pose-sleep"),
        new("loaf", "pose-loaf"),
        new("roar", "pose-roar"),
        new("stalk", "pose-stalk"),
        new("rising-eat", "pose-rise-eat")
    };

    private static readonly AnimalEmoteOption[] HoovedPoses =
    {
        new("sleep", "pose-sleep"),
        new("sniff", "pose-sniff"),
        new("stalk", "pose-stalk"),
        new("stot", "pose-stot")
    };

    private static readonly AnimalEmoteOption[] HarePoses =
    {
        new("lie", "pose-lie"),
        new("sleep", "pose-sleep"),
        new("sniff", "pose-sniff"),
        new("dig", "pose-dig"),
        new("longdig", "pose-long-dig"),
        new("stalk", "pose-stalk")
    };

    private static readonly AnimalEmoteOption[] RaccoonPoses =
    {
        new("lie", "pose-lie"),
        new("sleep", "pose-sleep"),
        new("sniff", "pose-sniff"),
        new("gather", "pose-gather"),
        new("stalk", "pose-stalk"),
        new("rising-eat", "pose-rise-eat")
    };

    private static readonly AnimalEmoteOption[] ChickenPoses =
    {
        new("sleep", "pose-sleep"),
        new("peck", "pose-peck"),
        new("stalk", "pose-stalk"),
        new("roostercall", "pose-call")
    };

    private static readonly AnimalEmoteOption[] FishPoses =
    {
        new("sleep", "pose-sleep"),
        new("flopping", "pose-flop"),
        new("idle-land", "pose-land-idle"),
        new("rising-eat", "pose-rise-eat")
    };

    private static readonly AnimalEmoteOption[] FallbackPoses =
    {
        new("sleep", "pose-sleep"),
        new("sniff", "pose-sniff"),
        new("stalk", "pose-stalk")
    };

    private ICoreAPI? commonApi;
    private ICoreClientAPI? clientApi;
    private AnimalicaShared.LoadedPlayerIndex? loadedPlayers;
    private readonly HashSet<long> loadedPlayerIds = new();
    private IClientNetworkChannel? clientChannel;
    private ICoreServerAPI? serverApi;
    private GuiDialogAnimalEmotes? dialog;
    private readonly Dictionary<long, string> clientPoseByEntityId = new();
    private readonly HashSet<string> loggedPlaybackFailures = new(StringComparer.Ordinal);
    private long clientSynchronizationListenerId;

    public override void Start(ICoreAPI api)
    {
        commonApi = api;
        api.RegisterEntityBehaviorClass("animalicaemotes", typeof(AnimalEmoteBehavior));
        api.Event.OnEntitySpawn += AttachBehavior;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        clientApi = api;
        loadedPlayers = new AnimalicaShared.LoadedPlayerIndex(api);
        clientChannel = api.Network.RegisterChannel(NetworkChannelName)
            .RegisterMessageType<AnimalEmotePacket>();

        api.Input.RegisterHotKey(
            MenuHotkeyCode,
            Lang.Get("animalicaemotes:hotkey-menu"),
            GlKeys.K,
            HotkeyType.CharacterControls,
            shiftPressed: true
        );
        api.Input.SetHotKeyHandler(MenuHotkeyCode, ToggleMenu);
        dialog = new GuiDialogAnimalEmotes(api, this);
        clientSynchronizationListenerId = api.Event.RegisterGameTickListener(
            SynchronizeLoadedPlayers,
            ClientSynchronizationIntervalMs
        );
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        serverApi = api;
        api.Network.RegisterChannel(NetworkChannelName)
            .RegisterMessageType<AnimalEmotePacket>()
            .SetMessageHandler<AnimalEmotePacket>(OnPoseRequest);

        api.ChatCommands
            .Create("animalemote")
            .RequiresPlayer()
            .RequiresPrivilege(Privilege.chat)
            .WithDescription("Starts or stops an Animalica pose.")
            .WithArgs(api.ChatCommands.Parsers.Word("animation-code"))
            .HandleWith(HandlePoseCommand);
    }

    public override void Dispose()
    {
        if (commonApi != null)
        {
            commonApi.Event.OnEntitySpawn -= AttachBehavior;
        }

        dialog?.TryClose();
        dialog?.Dispose();
        if (clientApi != null && clientSynchronizationListenerId != 0)
        {
            clientApi.Event.UnregisterGameTickListener(clientSynchronizationListenerId);
        }

        StopAllClientPoses();
        loadedPlayers?.Dispose();
        loadedPlayers = null;
        loadedPlayerIds.Clear();
        dialog = null;
        clientChannel = null;
        clientApi = null;
        serverApi = null;
        commonApi = null;
        clientPoseByEntityId.Clear();
        loggedPlaybackFailures.Clear();
        clientSynchronizationListenerId = 0;
    }

    public static bool IsSupportedPose(string pose) => SupportedPoses.Contains(pose);

    public AnimalEmoteOption[] GetAvailablePosesForLocalPlayer()
    {
        if (clientApi?.World.Player?.Entity is not EntityPlayer player)
        {
            return Array.Empty<AnimalEmoteOption>();
        }

        return GetAvailablePoses(player);
    }

    public static bool HasActivePose(EntityPlayer player)
    {
        return !string.IsNullOrEmpty(player.WatchedAttributes.GetString(ActivePoseAttribute, string.Empty));
    }

    public static void SetActivePose(EntityPlayer player, string pose)
    {
        string normalizedPose = IsSupportedPose(pose) ? pose : string.Empty;
        if (string.Equals(
                player.WatchedAttributes.GetString(ActivePoseAttribute, string.Empty),
                normalizedPose,
                StringComparison.Ordinal
            ))
        {
            return;
        }

        player.WatchedAttributes.SetString(ActivePoseAttribute, normalizedPose);
        player.WatchedAttributes.MarkPathDirty(ActivePoseAttribute);
    }

    public void SendPoseRequest(string pose)
    {
        if (clientApi?.World.Player?.Entity is not EntityPlayer player)
        {
            return;
        }

        if (!IsAnimalicaPlayer(player))
        {
            clientApi.TriggerIngameError(
                this,
                "animalicaemotes-not-animal",
                Lang.Get("animalicaemotes:not-animal-model")
            );
            return;
        }

        clientChannel?.SendPacket(new AnimalEmotePacket { Pose = pose ?? string.Empty });
    }

    private bool ToggleMenu(KeyCombination keyCombination)
    {
        if (clientApi?.World.Player?.Entity is not EntityPlayer player || dialog == null)
        {
            return false;
        }

        if (!IsAnimalicaPlayer(player))
        {
            clientApi.TriggerIngameError(
                this,
                "animalicaemotes-not-animal",
                Lang.Get("animalicaemotes:not-animal-model")
            );
            return true;
        }

        if (dialog.IsOpened())
        {
            dialog.TryClose();
        }
        else
        {
            dialog.TryOpen();
        }

        return true;
    }

    private void OnPoseRequest(IServerPlayer fromPlayer, AnimalEmotePacket packet)
    {
        if (fromPlayer.Entity is not EntityPlayer player || !IsAnimalicaPlayer(player))
        {
            return;
        }

        if (string.IsNullOrEmpty(packet.Pose) || IsPoseAvailable(player, packet.Pose))
        {
            ToggleOrSetPose(player, packet.Pose);
        }
    }

    private TextCommandResult HandlePoseCommand(TextCommandCallingArgs args)
    {
        if (args.Caller.Entity is not EntityPlayer player || !IsAnimalicaPlayer(player))
        {
            return TextCommandResult.Error(Lang.Get("animalicaemotes:not-animal-model"));
        }

        string requestedPose = ((string)args[0]).ToLowerInvariant();
        if (string.Equals(requestedPose, "stop", StringComparison.Ordinal))
        {
            requestedPose = string.Empty;
        }
        else if (!IsPoseAvailable(player, requestedPose))
        {
            return TextCommandResult.Error(Lang.Get("animalicaemotes:unknown-pose"));
        }

        bool active = ToggleOrSetPose(player, requestedPose);
        return TextCommandResult.Success(
            Lang.Get(active ? "animalicaemotes:pose-started" : "animalicaemotes:pose-stopped")
        );
    }

    private static bool ToggleOrSetPose(EntityPlayer player, string? requestedPose)
    {
        string pose = requestedPose?.ToLowerInvariant() ?? string.Empty;
        if (!IsSupportedPose(pose))
        {
            SetActivePose(player, string.Empty);
            return false;
        }

        string currentPose = player.WatchedAttributes.GetString(ActivePoseAttribute, string.Empty);
        bool shouldStart = !string.Equals(currentPose, pose, StringComparison.Ordinal);
        SetActivePose(player, shouldStart ? pose : string.Empty);
        return shouldStart;
    }

    private static bool IsAnimalicaPlayer(EntityPlayer player)
    {
        string modelCode = player.WatchedAttributes.GetString("skinModel", string.Empty);
        return AnimalicaModelDetector.IsAnimalicaModel(modelCode);
    }

    private static bool IsPoseAvailable(EntityPlayer player, string pose)
    {
        foreach (AnimalEmoteOption option in GetAvailablePoses(player))
        {
            if (string.Equals(option.Pose, pose, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static AnimalEmoteOption[] GetAvailablePoses(EntityPlayer player)
    {
        string modelCode = player.WatchedAttributes.GetString("skinModel", string.Empty);
        return AnimalicaModelDetector.GetFamily(modelCode) switch
        {
            AnimalicaModelFamily.Wolf => WolfPoses,
            AnimalicaModelFamily.Fox => FoxPoses,
            AnimalicaModelFamily.Hyena => HyenaPoses,
            AnimalicaModelFamily.Bear => BearPoses,
            AnimalicaModelFamily.Panther => PantherPoses,
            AnimalicaModelFamily.Deer => HoovedPoses,
            AnimalicaModelFamily.Goat => HoovedPoses,
            AnimalicaModelFamily.Pig => HoovedPoses,
            AnimalicaModelFamily.Sheep => HoovedPoses,
            AnimalicaModelFamily.Hare => HarePoses,
            AnimalicaModelFamily.Raccoon => RaccoonPoses,
            AnimalicaModelFamily.Chicken => ChickenPoses,
            AnimalicaModelFamily.Fish => FishPoses,
            _ => FallbackPoses
        };
    }

    private static void AttachBehavior(Entity entity)
    {
        if (entity.Api.Side != EnumAppSide.Server
            || entity is not EntityPlayer
            || entity.GetBehavior<AnimalEmoteBehavior>() != null)
        {
            return;
        }

        AnimalEmoteBehavior behavior = new(entity);
        entity.AddBehavior(behavior);
        behavior.Initialize(entity.Properties, null!);
    }

    private void SynchronizeLoadedPlayers(float deltaTime)
    {
        if (clientApi == null)
        {
            return;
        }

        loadedPlayerIds.Clear();
        foreach (EntityPlayer player in loadedPlayers!.Snapshot)
        {
            loadedPlayerIds.Add(player.EntityId);
            string desiredPose = player.WatchedAttributes.GetString(ActivePoseAttribute, string.Empty);
            if (!IsSupportedPose(desiredPose) || !IsAnimalicaPlayer(player))
            {
                desiredPose = string.Empty;
            }

            clientPoseByEntityId.TryGetValue(player.EntityId, out string? previousPose);
            previousPose ??= string.Empty;

            if (!string.Equals(previousPose, desiredPose, StringComparison.Ordinal))
            {
                StopClientPose(player, previousPose);
                clientPoseByEntityId[player.EntityId] = desiredPose;

                if (!string.IsNullOrEmpty(desiredPose))
                {
                    TryStartClientPose(player, desiredPose, logSuccess: true);
                }

                continue;
            }

            if (!string.IsNullOrEmpty(desiredPose)
                && !IsClientPoseActive(player, desiredPose))
            {
                TryStartClientPose(player, desiredPose, logSuccess: false);
            }
        }

        List<long>? unloadedIds = null;
        foreach (long entityId in clientPoseByEntityId.Keys)
        {
            if (!loadedPlayerIds.Contains(entityId))
            {
                (unloadedIds ??= new List<long>()).Add(entityId);
            }
        }

        if (unloadedIds == null)
        {
            return;
        }

        foreach (long entityId in unloadedIds)
        {
            clientPoseByEntityId.Remove(entityId);
        }
    }

    private void TryStartClientPose(EntityPlayer player, string pose, bool logSuccess)
    {
        IAnimationManager? manager = player.TpAnimManager ?? player.AnimManager;
        if (manager == null)
        {
            LogPlaybackFailureOnce(player, pose, "no third-person animation manager is available");
            return;
        }

        float easeInSpeed = string.Equals(
            pose,
            "foxidle-test-alert",
            StringComparison.Ordinal
        ) ? 2f : 4f;

        AnimationMetaData metadata = new AnimationMetaData
        {
            Code = AnimationMetaPrefix + pose,
            Animation = pose,
            AnimationSpeed = 1f,
            Weight = 10f,
            EaseInSpeed = easeInSpeed,
            EaseOutSpeed = 4f,
            BlendMode = EnumAnimationBlendMode.Average,
            SupressDefaultAnimation = true,
            ClientSide = true
        }.Init();

        bool started = manager.StartAnimation(metadata);
        if (!started)
        {
            LogPlaybackFailureOnce(player, pose, "the animation manager rejected the pose");
            return;
        }

        if (logSuccess)
        {
            clientApi?.Logger.Notification(
                "[AnimalicaEmotes] Started synchronized pose '{0}' for player entity {1}.",
                pose,
                player.EntityId
            );
        }
    }

    private static bool IsClientPoseActive(EntityPlayer player, string pose)
    {
        IAnimationManager? manager = player.TpAnimManager ?? player.AnimManager;
        return manager?.IsAnimationActive(pose) == true;
    }

    private static void StopClientPose(EntityPlayer player, string pose)
    {
        if (string.IsNullOrEmpty(pose))
        {
            return;
        }

        player.TpAnimManager?.StopAnimation(pose);
        if (!ReferenceEquals(player.TpAnimManager, player.AnimManager))
        {
            player.AnimManager?.StopAnimation(pose);
        }
    }

    private void StopAllClientPoses()
    {
        if (clientApi == null)
        {
            return;
        }

        foreach (Entity entity in clientApi.World.LoadedEntities.Values)
        {
            if (entity is not EntityPlayer player
                || !clientPoseByEntityId.TryGetValue(player.EntityId, out string? pose))
            {
                continue;
            }

            StopClientPose(player, pose);
        }
    }

    private void LogPlaybackFailureOnce(EntityPlayer player, string pose, string reason)
    {
        string key = player.EntityId + "|" + pose + "|" + reason;
        if (!loggedPlaybackFailures.Add(key))
        {
            return;
        }

        clientApi?.Logger.Warning(
            "[AnimalicaEmotes] Could not play pose '{0}' for player entity {1}: {2}.",
            pose,
            player.EntityId,
            reason
        );
    }
}

public readonly record struct AnimalEmoteOption(string Pose, string LangCode);
