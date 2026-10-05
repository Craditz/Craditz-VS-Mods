using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace AnimalicaCore;

public sealed class AnimalicaActionSystem : ModSystem
{
    public const string PostureAttribute = "animalicaCorePosture";
    public const string VocalizationSelectHotkeyCode = "animalicacore-vocalization-select";
    public const string VocalizationHotkeyCode = "animalicacore-vocalize";

    private const string NetworkChannelName = "animalicacore:actions";
    private const int ClientTickIntervalMs = 50;
    private const int ServerTickIntervalMs = 50;
    private const int VocalizationCooldownMs = 350;
    private const float VocalizationRange = 32f;
    private const byte PostureStanding = 0;
    private const byte PostureSitting = 1;
    private const byte PostureLaying = 2;
    private const byte PostureActionLay = 1;
    private const byte PostureActionStand = 2;
    private const byte PostureActionSit = 3;
    private const byte NoLocalPostureOverride = byte.MaxValue;
    internal const double SittingEyeHeightMultiplier = 1.25;

    private readonly Dictionary<string, AnimalicaVoiceOption[]> voiceOptionsByModel = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> selectedVoiceByModel = new(StringComparer.Ordinal);
    private static readonly HashSet<string> AnimalicaModelCodes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> RaisedSittingModelCodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, string> clientLayAnimationByEntityId = new();
    private readonly HashSet<string> loggedAnimationFailures = new(StringComparer.Ordinal);
    private readonly Dictionary<long, long> lastStandRequestByEntityId = new();
    private ICoreClientAPI? clientApi;
    private AnimalicaShared.LoadedPlayerIndex? loadedPlayers;
    private readonly HashSet<long> loadedPlayerIds = new();
    private IClientNetworkChannel? clientChannel;
    private ICoreServerAPI? serverApi;
    private Harmony? eyeHeightHarmony;
    private long clientTickListenerId;
    private long serverTickListenerId;
    private readonly Dictionary<string, long> lastVocalizationByPlayerUid = new(StringComparer.Ordinal);
    private byte localPostureOverride = NoLocalPostureOverride;

    public override void Start(ICoreAPI api)
    {
        eyeHeightHarmony = new Harmony("animalicacore.action-eye-height");
        AnimalicaEyeHeightPatch.Start(eyeHeightHarmony);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        clientApi = api;
        loadedPlayers = new AnimalicaShared.LoadedPlayerIndex(api);
        clientChannel = api.Network.RegisterChannel(NetworkChannelName)
            .RegisterMessageType<AnimalicaPosturePacket>()
            .RegisterMessageType<AnimalicaVocalizePacket>();

        api.Input.RegisterHotKey(
            VocalizationSelectHotkeyCode,
            Lang.Get("animalicacore:hotkey-vocalization-select"),
            GlKeys.V,
            HotkeyType.CharacterControls,
            shiftPressed: true
        );
        api.Input.SetHotKeyHandler(VocalizationSelectHotkeyCode, SelectNextVocalization);
        api.Input.RegisterHotKey(
            VocalizationHotkeyCode,
            Lang.Get("animalicacore:hotkey-vocalize"),
            GlKeys.V,
            HotkeyType.CharacterControls
        );
        api.Input.SetHotKeyHandler(VocalizationHotkeyCode, OnVocalizeHotkey);
        if (eyeHeightHarmony != null)
        {
            AnimalicaSitInputPatch.Start(eyeHeightHarmony, this);
        }
        clientTickListenerId = api.Event.RegisterGameTickListener(SynchronizeClientPostures, ClientTickIntervalMs);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        serverApi = api;
        api.Network.RegisterChannel(NetworkChannelName)
            .RegisterMessageType<AnimalicaPosturePacket>()
            .RegisterMessageType<AnimalicaVocalizePacket>()
            .SetMessageHandler<AnimalicaPosturePacket>(OnPostureRequest)
            .SetMessageHandler<AnimalicaVocalizePacket>(OnVocalizeRequest);
        serverTickListenerId = api.Event.RegisterGameTickListener(ProcessServerPostures, ServerTickIntervalMs);
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        voiceOptionsByModel.Clear();
        AnimalicaModelCodes.Clear();
        RaisedSittingModelCodes.Clear();
        foreach (IAsset asset in api.Assets.GetMany("config/customplayermodels", null, true))
        {
            string text = asset.ToText();
            if (string.IsNullOrWhiteSpace(text)
                || text.IndexOf("animalica", StringComparison.OrdinalIgnoreCase) < 0
                || asset.Location.Path.IndexOf("compatibility", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            JObject root;
            try
            {
                root = JObject.Parse(text);
            }
            catch (Exception exception)
            {
                api.Logger.Warning("[AnimalicaCore] Could not read action model config {0}: {1}", asset.Location, exception.Message);
                continue;
            }

            foreach (JProperty property in root.Properties())
            {
                if (property.Value is not JObject model || !IsEligibleModel(model))
                {
                    continue;
                }

                string modelCode = $"{asset.Location.Domain}:{property.Name}";
                AnimalicaModelCodes.Add(modelCode);
                if (IsRaisedSittingModel(model))
                {
                    RaisedSittingModelCodes.Add(modelCode);
                }

                AnimalicaVoiceOption[] options = ReadVoiceOptions(modelCode, model);
                if (options.Length > 0)
                {
                    voiceOptionsByModel[modelCode] = options;
                }
            }
        }

        api.Logger.Notification(
            "[AnimalicaCore] Registered {0} first-party Animalica model(s); temporary vocalizations are available for {1}; models without voice variants are omitted.",
            AnimalicaModelCodes.Count,
            voiceOptionsByModel.Count
        );
    }

    public override void Dispose()
    {
        if (clientApi != null)
        {
            if (clientTickListenerId != 0)
            {
                clientApi.Event.UnregisterGameTickListener(clientTickListenerId);
            }
        }

        if (serverApi != null && serverTickListenerId != 0)
        {
            serverApi.Event.UnregisterGameTickListener(serverTickListenerId);
        }

        StopAllClientLayAnimations();
        loadedPlayers?.Dispose();
        loadedPlayers = null;
        loadedPlayerIds.Clear();
        eyeHeightHarmony?.UnpatchAll("animalicacore.action-eye-height");
        AnimalicaSitInputPatch.Stop();
        AnimalicaEyeHeightPatch.Stop();
        clientApi = null;
        clientChannel = null;
        serverApi = null;
        voiceOptionsByModel.Clear();
        selectedVoiceByModel.Clear();
        clientLayAnimationByEntityId.Clear();
        loggedAnimationFailures.Clear();
        lastStandRequestByEntityId.Clear();
        lastVocalizationByPlayerUid.Clear();
        localPostureOverride = NoLocalPostureOverride;
        clientTickListenerId = 0;
        serverTickListenerId = 0;
    }

    public AnimalicaVoiceOption[] GetAvailableVoicesForLocalPlayer()
    {
        if (clientApi?.World.Player?.Entity is not EntityPlayer player)
        {
            return Array.Empty<AnimalicaVoiceOption>();
        }

        return GetAvailableVoices(player);
    }

    public void SelectVoice(string code)
    {
        if (clientApi?.World.Player?.Entity is not EntityPlayer player)
        {
            return;
        }

        AnimalicaVoiceOption[] options = GetAvailableVoices(player);
        if (options.Any(option => string.Equals(option.Code, code, StringComparison.Ordinal)))
        {
            selectedVoiceByModel[GetModelCode(player)] = code;
        }
    }

    public void SendVocalization()
    {
        if (AnimalicaCoreSystem.MuzzleModeEnabled)
        {
            return;
        }

        if (clientApi?.World.Player?.Entity is not EntityPlayer player
            || clientChannel == null)
        {
            return;
        }

        AnimalicaVoiceOption[] options = GetAvailableVoices(player);
        string? code = selectedVoiceByModel.TryGetValue(GetModelCode(player), out string? selected)
            ? selected
            : options.FirstOrDefault().Code;
        if (string.IsNullOrEmpty(code)
            || !options.Any(option => string.Equals(option.Code, code, StringComparison.Ordinal)))
        {
            return;
        }

        clientChannel.SendPacket(new AnimalicaVocalizePacket { VoiceCode = code });
    }

    private bool SelectNextVocalization(KeyCombination keyCombination)
    {
        if (AnimalicaCoreSystem.MuzzleModeEnabled)
        {
            return true;
        }

        if (clientApi?.World.Player?.Entity is not EntityPlayer player)
        {
            return false;
        }

        AnimalicaVoiceOption[] options = GetAvailableVoices(player);
        if (options.Length == 0)
        {
            clientApi.ShowChatMessage(Lang.Get("animalicacore:vocalization-unavailable"));
            return true;
        }

        string modelCode = GetModelCode(player);
        int currentIndex = selectedVoiceByModel.TryGetValue(modelCode, out string? selected)
            ? Array.FindIndex(options, option => string.Equals(option.Code, selected, StringComparison.Ordinal))
            : -1;
        int nextIndex = (currentIndex + 1 + options.Length) % options.Length;
        AnimalicaVoiceOption next = options[nextIndex];
        selectedVoiceByModel[modelCode] = next.Code;
        clientApi.ShowChatMessage(Lang.Get("animalicacore:vocalization-selected", next.DisplayName));

        return true;
    }

    private bool OnVocalizeHotkey(KeyCombination keyCombination)
    {
        SendVocalization();

        return true;
    }

    private void OnPostureRequest(IServerPlayer fromPlayer, AnimalicaPosturePacket packet)
    {
        if (AnimalicaCoreSystem.OldSitModeEnabled
            || fromPlayer.Entity is not EntityPlayer player
            || !IsEligiblePlayer(player))
        {
            return;
        }

        switch (packet.Action)
        {
            case PostureActionSit:
                if (!player.ServerControls.TriesToMove)
                {
                    player.ServerControls.FloorSitting = true;
                    SetPosture(player, PostureSitting);
                }
                break;
            case PostureActionLay:
                if (!player.ServerControls.TriesToMove
                    && GetPosture(player) != PostureLaying)
                {
                    player.ServerControls.FloorSitting = false;
                    SetPosture(player, PostureLaying);
                }
                break;
            case PostureActionStand:
                StandPlayer(player);
                break;
        }
    }

    private void OnVocalizeRequest(IServerPlayer fromPlayer, AnimalicaVocalizePacket packet)
    {
        if (AnimalicaCoreSystem.MuzzleModeEnabled
            || serverApi == null
            || fromPlayer.Entity is not EntityPlayer player
            || !TryGetVoice(player, packet.VoiceCode, out AnimalicaVoiceOption option))
        {
            return;
        }

        long now = serverApi.World.ElapsedMilliseconds;
        if (lastVocalizationByPlayerUid.TryGetValue(fromPlayer.PlayerUID, out long previous)
            && now - previous < VocalizationCooldownMs)
        {
            return;
        }

        lastVocalizationByPlayerUid[fromPlayer.PlayerUID] = now;
        serverApi.World.PlaySoundAt(
            option.Sound,
            player,
            null,
            1f,
            VocalizationRange,
            1f
        );
    }

    private void ProcessServerPostures(float deltaTime)
    {
        if (serverApi == null)
        {
            return;
        }

        if (AnimalicaCoreSystem.OldSitModeEnabled)
        {
            foreach (IPlayer onlinePlayer in serverApi.World.AllOnlinePlayers)
            {
                if (onlinePlayer.Entity is EntityPlayer player && IsEligiblePlayer(player))
                {
                    // Clear only Core's private posture marker. Do not touch FloorSitting;
                    // vanilla must remain free to own the sit control in this mode.
                    SetPosture(player, PostureStanding);
                }
            }

            return;
        }

        foreach (IPlayer onlinePlayer in serverApi.World.AllOnlinePlayers)
        {
            if (onlinePlayer.Entity is not EntityPlayer player || !IsEligiblePlayer(player))
            {
                continue;
            }

            byte posture = GetPosture(player);
            if (posture == PostureStanding)
            {
                if (player.ServerControls.FloorSitting && !player.ServerControls.TriesToMove)
                {
                    SetPosture(player, PostureSitting);
                }

                continue;
            }

            if (player.ServerControls.TriesToMove)
            {
                StandPlayer(player);
            }
        }
    }

    private void SynchronizeClientPostures(float deltaTime)
    {
        if (clientApi == null)
        {
            return;
        }

        if (AnimalicaCoreSystem.OldSitModeEnabled)
        {
            StopAllClientLayAnimations();
            localPostureOverride = NoLocalPostureOverride;
            lastStandRequestByEntityId.Clear();
            return;
        }

        loadedPlayerIds.Clear();
        foreach (EntityPlayer player in loadedPlayers!.Snapshot)
        {
            loadedPlayerIds.Add(player.EntityId);
            bool isLocalPlayer = ReferenceEquals(player, clientApi.World.Player?.Entity);
            byte posture = IsEligiblePlayer(player)
                ? (isLocalPlayer ? GetLocalPosture(player) : GetPosture(player))
                : PostureStanding;
            if (isLocalPlayer
                && posture != PostureStanding
                && player.Controls.TriesToMove)
            {
                RequestLocalStand(player);
                posture = PostureStanding;
            }

            if (isLocalPlayer
                && IsEligiblePlayer(player)
                && !player.Controls.TriesToMove)
            {
                SetLocalFloorSitting(player, posture == PostureSitting);
            }

            if (posture != PostureLaying)
            {
                StopClientLayAnimation(player);
                continue;
            }

            string animation = HasAnimation(player, "animalicacore-laypose") ? "animalicacore-laypose"
                : HasAnimation(player, "lie") ? "lie" : "sleep";
            if (!clientLayAnimationByEntityId.TryGetValue(player.EntityId, out string? previous)
                || !string.Equals(previous, animation, StringComparison.Ordinal))
            {
                StopClientLayAnimation(player);
                clientLayAnimationByEntityId[player.EntityId] = animation;
                TryStartClientLayAnimation(player, animation);
            }
            else if (!IsClientLayAnimationActive(player, animation))
            {
                TryStartClientLayAnimation(player, animation);
            }
        }

        foreach (long entityId in clientLayAnimationByEntityId.Keys.ToArray())
        {
            if (!loadedPlayerIds.Contains(entityId))
            {
                clientLayAnimationByEntityId.Remove(entityId);
            }
        }
    }

    private void RequestLocalStand(EntityPlayer player)
    {
        if (clientChannel == null || clientApi == null)
        {
            return;
        }

        long now = clientApi.World.ElapsedMilliseconds;
        if (lastStandRequestByEntityId.TryGetValue(player.EntityId, out long previous)
            && now - previous < 250)
        {
            return;
        }

        lastStandRequestByEntityId[player.EntityId] = now;
        localPostureOverride = PostureStanding;
        SetLocalFloorSitting(player, false);
        clientChannel.SendPacket(new AnimalicaPosturePacket { Action = PostureActionStand });
    }

    private byte GetLocalPosture(EntityPlayer player)
    {
        return ReferenceEquals(player, clientApi?.World.Player?.Entity)
            && localPostureOverride != NoLocalPostureOverride
            ? localPostureOverride
            : GetPosture(player);
    }

    private void SetLocalFloorSitting(EntityPlayer player, bool value)
    {
        player.Controls.FloorSitting = value;
    }

    internal bool TryHandleSitKeyPress(object controlSystem, KeyEvent args)
    {
        if (AnimalicaCoreSystem.OldSitModeEnabled)
        {
            return false;
        }

        if (clientApi?.World.Player?.Entity is not EntityPlayer player
            || !IsEligiblePlayer(player)
            || clientChannel == null
            || player.Controls.TriesToMove
            || player.Controls.IsFlying)
        {
            return false;
        }

        byte posture = GetLocalPosture(player);
        switch (posture)
        {
            case PostureStanding:
                localPostureOverride = PostureSitting;
                SetLocalFloorSitting(player, true);
                clientChannel.SendPacket(new AnimalicaPosturePacket { Action = PostureActionSit });
                break;
            case PostureSitting:
                localPostureOverride = PostureLaying;
                SetLocalFloorSitting(player, false);
                clientChannel.SendPacket(new AnimalicaPosturePacket { Action = PostureActionLay });
                break;
            case PostureLaying:
                localPostureOverride = PostureStanding;
                SetLocalFloorSitting(player, false);
                clientChannel.SendPacket(new AnimalicaPosturePacket { Action = PostureActionStand });
                break;
            default:
                return false;
        }

        AnimalicaSitInputPatch.SetInternalSitToggle(controlSystem, posture == PostureStanding);
        return true;
    }

    private static void StandPlayer(EntityPlayer player)
    {
        player.ServerControls.FloorSitting = false;
        SetPosture(player, PostureStanding);
    }

    private static void SetPosture(EntityPlayer player, byte posture)
    {
        if (GetPosture(player) == posture)
        {
            return;
        }

        player.WatchedAttributes.SetInt(PostureAttribute, posture);
        player.WatchedAttributes.MarkPathDirty(PostureAttribute);
    }

    private static byte GetPosture(EntityPlayer player)
    {
        return (byte)Math.Clamp(player.WatchedAttributes.GetInt(PostureAttribute, 0), 0, PostureLaying);
    }

    internal static bool IsEligiblePlayer(EntityPlayer player)
    {
        return AnimalicaModelCodes.Contains(GetModelCode(player));
    }

    internal static bool UsesRaisedSittingHeight(EntityPlayer player)
    {
        return RaisedSittingModelCodes.Contains(GetModelCode(player));
    }

    private static string GetModelCode(EntityPlayer player)
    {
        return player.WatchedAttributes.GetString("skinModel", string.Empty);
    }

    private AnimalicaVoiceOption[] GetAvailableVoices(EntityPlayer player)
    {
        return voiceOptionsByModel.TryGetValue(GetModelCode(player), out AnimalicaVoiceOption[]? options)
            ? options
            : Array.Empty<AnimalicaVoiceOption>();
    }

    private bool TryGetVoice(EntityPlayer player, string code, out AnimalicaVoiceOption option)
    {
        option = default;
        if (!IsEligiblePlayer(player)
            || string.IsNullOrWhiteSpace(code)
            || !voiceOptionsByModel.TryGetValue(GetModelCode(player), out AnimalicaVoiceOption[]? options))
        {
            return false;
        }

        foreach (AnimalicaVoiceOption candidate in options)
        {
            if (string.Equals(candidate.Code, code, StringComparison.Ordinal))
            {
                option = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool IsEligibleModel(JObject model)
    {
        string group = model.Value<string>("Group") ?? string.Empty;
        return IsAnimalicaModel(model)
            && group.IndexOf("compat", StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static bool IsAnimalicaModel(JObject model)
    {
        string group = model.Value<string>("Group") ?? string.Empty;
        if (group.StartsWith("animalica", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return model["AddTags"] is JArray tags
            && tags.Values<string>().Any(tag => string.Equals(tag, "animalica", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRaisedSittingModel(JObject model)
    {
        string group = model.Value<string>("Group") ?? string.Empty;
        if (string.Equals(group, "animalica-carnivores", StringComparison.OrdinalIgnoreCase)
            || string.Equals(group, "animalica-bears", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return model["AddTags"] is JArray tags
            && tags.Values<string>().Any(tag =>
                string.Equals(tag, "raccoon", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "hare", StringComparison.OrdinalIgnoreCase));
    }

    private static AnimalicaVoiceOption[] ReadVoiceOptions(string modelCode, JObject model)
    {
        if (model["SkinnableParts"] is not JArray parts)
        {
            return Array.Empty<AnimalicaVoiceOption>();
        }

        JObject? voicePart = parts
            .OfType<JObject>()
            .FirstOrDefault(part => string.Equals(part.Value<string>("code"), "voicetype", StringComparison.Ordinal));
        if (voicePart?["variants"] is not JArray variants)
        {
            return Array.Empty<AnimalicaVoiceOption>();
        }

        return variants
            .OfType<JObject>()
            .Select(variant => new
            {
                Code = variant.Value<string>("code") ?? string.Empty,
                Sound = variant.Value<string>("sound") ?? string.Empty
            })
            .Where(value => !string.IsNullOrWhiteSpace(value.Code) && !string.IsNullOrWhiteSpace(value.Sound))
            .Where(value => !string.Equals(value.Code, "altoflute", StringComparison.OrdinalIgnoreCase))
            .Select(value => new AnimalicaVoiceOption(
                value.Code,
                HumanizeVoiceCode(modelCode, value.Code),
                new AssetLocation(ResolveVocalizationSound(modelCode, value.Code, value.Sound))
            ))
            .ToArray();
    }

    private static string HumanizeVoiceCode(string modelCode, string voiceCode)
    {
        string? family = modelCode.StartsWith("feralbear:", StringComparison.OrdinalIgnoreCase) ? "bear"
            : modelCode.StartsWith("feralfox:", StringComparison.OrdinalIgnoreCase) ? "fox"
            : modelCode.StartsWith("feralhyena:", StringComparison.OrdinalIgnoreCase) ? "hyena"
            : modelCode.StartsWith("feralwolf:", StringComparison.OrdinalIgnoreCase) ? "wolf"
            : modelCode.StartsWith("feraldeer:", StringComparison.OrdinalIgnoreCase) ? "deer"
            : modelCode.StartsWith("feralraccoon:", StringComparison.OrdinalIgnoreCase) ? "raccoon"
            : null;

        string displayCode = family != null && voiceCode.StartsWith(family, StringComparison.OrdinalIgnoreCase)
            ? voiceCode[family.Length..]
            : voiceCode;
        return Humanize(displayCode);
    }

    private static string ResolveVocalizationSound(string modelCode, string voiceCode, string configuredSound)
    {
        string? family = modelCode.StartsWith("feralbear:", StringComparison.OrdinalIgnoreCase) ? "bear"
            : modelCode.StartsWith("feralfox:", StringComparison.OrdinalIgnoreCase) ? "fox"
            : modelCode.StartsWith("feralhyena:", StringComparison.OrdinalIgnoreCase) ? "hyena"
            : modelCode.StartsWith("feralwolf:", StringComparison.OrdinalIgnoreCase) ? "wolf"
            : modelCode.StartsWith("feraldeer:", StringComparison.OrdinalIgnoreCase) ? "deer"
            : modelCode.StartsWith("feralraccoon:", StringComparison.OrdinalIgnoreCase) ? "raccoon"
            : null;

        if (family == null)
        {
            return configuredSound;
        }

        string? vanillaPath = family switch
        {
            "bear" => voiceCode switch
            {
                "bearaggro" => "sounds/creature/bear/aggro",
                "bearattack" => "sounds/creature/bear/attack",
                "bearflee" => "sounds/creature/bear/flee",
                "beargrowl1" => "sounds/creature/bear/growl1",
                "beargrowl2" => "sounds/creature/bear/growl2",
                "bearhurt" => "sounds/creature/bear/hurt",
                _ => null
            },
            "fox" => voiceCode switch
            {
                "foxhurt" => "sounds/creature/fox/hurt",
                "foxgrowl" => "sounds/creature/fox/growl",
                "foxattack" => "sounds/creature/fox/attack",
                "foxdie" => "sounds/creature/fox/die",
                "foxyip" => "sounds/creature/fox/yip",
                _ => null
            },
            "hyena" => voiceCode switch
            {
                "hyenaattack" => "sounds/creature/hyena/attack",
                "hyenadeath" => "sounds/creature/hyena/death",
                "hyenagrowl" => "sounds/creature/hyena/growl",
                "hyenahurt" => "sounds/creature/hyena/hurt",
                "hyenalaugh" => "sounds/creature/hyena/laugh",
                _ => null
            },
            "wolf" => voiceCode switch
            {
                "wolfhurt" => "sounds/creature/wolf/hurt",
                "wolfgrowl1" => "sounds/creature/wolf/growl1",
                "wolfgrowl2" => "sounds/creature/wolf/growl2",
                "wolfgrowl3" => "sounds/creature/wolf/growl3",
                "wolfgrowl4" => "sounds/creature/wolf/growl4",
                "wolfattack1" => "sounds/creature/wolf/attack1",
                "wolfattack2" => "sounds/creature/wolf/attack2",
                "wolfattack3" => "sounds/creature/wolf/attack3",
                "wolfhowl1" => "sounds/creature/wolf/howl1",
                "wolfhowl2" => "sounds/creature/wolf/howl2",
                "wolfhowl3" => "sounds/creature/wolf/howl3",
                _ => null
            },
            "deer" => voiceCode switch
            {
                "deercall1" => "sounds/creature/hooved/medium/deer-call1",
                "deercall2" => "sounds/creature/hooved/medium/deer-call2",
                "deersniff" => "sounds/creature/hooved/generic/sniff",
                _ => null
            },
            "raccoon" => voiceCode switch
            {
                "raccoonidle" => "sounds/creature/raccoon/idle",
                "raccoonaggro" => "sounds/creature/raccoon/aggro",
                "raccoonhurt" => "sounds/creature/raccoon/hurt",
                "raccoondeath" => "sounds/creature/raccoon/death",
                _ => null
            },
            _ => null
        };

        return vanillaPath ?? configuredSound;
    }

    private static string Humanize(string code)
    {
        List<string> words = new();
        foreach (string word in code.Replace('-', ' ').Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int digitStart = 0;
            while (digitStart < word.Length && !char.IsDigit(word[digitStart]))
            {
                digitStart++;
            }

            if (digitStart > 0 && digitStart < word.Length)
            {
                words.Add(word[..digitStart]);
                words.Add(word[digitStart..]);
            }
            else
            {
                words.Add(word);
            }
        }

        return string.Join(
            " ",
            words.Select(word => char.ToUpperInvariant(word[0]) + word[1..])
        );
    }

    private static bool HasAnimation(EntityPlayer player, string code)
    {
        // Model metadata contains overrides, not every animation in the loaded rig.
        IAnimationManager? manager = player.TpAnimManager ?? player.AnimManager;
        return manager?.Animator?.GetAnimationState(code) != null;
    }

    private void TryStartClientLayAnimation(EntityPlayer player, string animation)
    {
        IAnimationManager? manager = player.TpAnimManager ?? player.AnimManager;
        if (manager == null)
        {
            LogAnimationFailureOnce(player, animation, "no third-person animation manager is available");
            return;
        }

        AnimationMetaData metadata = new AnimationMetaData
        {
            Code = "animalicacore-lay",
            Animation = animation,
            AnimationSpeed = 1f,
            Weight = 10f,
            EaseInSpeed = 4f,
            EaseOutSpeed = 4f,
            BlendMode = EnumAnimationBlendMode.Average,
            SupressDefaultAnimation = true,
            ClientSide = true
        }.Init();

        if (!manager.StartAnimation(metadata))
        {
            LogAnimationFailureOnce(player, animation, "the animation manager rejected the pose");
        }
    }

    private static bool IsClientLayAnimationActive(EntityPlayer player, string animation)
    {
        IAnimationManager? manager = player.TpAnimManager ?? player.AnimManager;
        return manager != null
            && (manager.IsAnimationActive(animation)
                || manager.IsAnimationActive("animalicacore-lay")
                || manager.ActiveAnimationsByAnimCode.ContainsKey(animation)
                || manager.ActiveAnimationsByAnimCode.ContainsKey("animalicacore-lay"));
    }

    private static void StopClientLayAnimation(EntityPlayer player)
    {
        player.TpAnimManager?.StopAnimation("animalicacore-lay");
        if (!ReferenceEquals(player.TpAnimManager, player.AnimManager))
        {
            player.AnimManager?.StopAnimation("animalicacore-lay");
        }
    }

    private void StopAllClientLayAnimations()
    {
        if (loadedPlayers == null || clientLayAnimationByEntityId.Count == 0)
        {
            return;
        }

        foreach (EntityPlayer player in loadedPlayers.Snapshot)
        {
            if (clientLayAnimationByEntityId.ContainsKey(player.EntityId))
            {
                StopClientLayAnimation(player);
            }
        }
    }

    private void LogAnimationFailureOnce(EntityPlayer player, string animation, string reason)
    {
        string key = $"{player.EntityId}|{animation}|{reason}";
        if (loggedAnimationFailures.Add(key))
        {
            clientApi?.Logger.Warning(
                "[AnimalicaCore] Could not play laying animation '{0}' for player entity {1}: {2}.",
                animation,
                player.EntityId,
                reason
            );
        }
    }
}

public readonly record struct AnimalicaVoiceOption(string Code, string DisplayName, AssetLocation Sound);

internal static class AnimalicaEyeHeightPatch
{
    private static readonly MethodInfo? EyeHeightMethod = AccessTools.Method(typeof(EntityPlayer), "updateEyeHeight");
    private static Harmony? harmony;

    public static void Start(Harmony owner)
    {
        if (EyeHeightMethod == null)
        {
            return;
        }

        harmony = owner;
        MethodInfo? postfix = AccessTools.Method(typeof(AnimalicaEyeHeightPatch), nameof(AfterUpdateEyeHeight));
        MethodInfo? prefix = AccessTools.Method(typeof(AnimalicaEyeHeightPatch), nameof(BeforeUpdateEyeHeight));
        if (postfix != null && prefix != null)
        {
            owner.Patch(
                EyeHeightMethod,
                prefix: new HarmonyMethod(prefix),
                postfix: new HarmonyMethod(postfix)
            );
        }
    }

    public static void Stop()
    {
        harmony = null;
    }

    private static void BeforeUpdateEyeHeight(EntityPlayer __instance, ref double __state)
    {
        if (AnimalicaCoreSystem.OldSitModeEnabled)
        {
            __state = double.NaN;
            return;
        }

        __state = IsLaying(__instance) || IsSitting(__instance)
            ? __instance.LocalEyePos.Y
            : double.NaN;
    }

    private static void AfterUpdateEyeHeight(EntityPlayer __instance, float dt, double __state)
    {
        if (AnimalicaCoreSystem.OldSitModeEnabled)
        {
            return;
        }

        double eyeHeight = __instance.Properties.EyeHeight;
        if (eyeHeight <= 0 || double.IsNaN(__state))
        {
            return;
        }

        double? target = null;
        if (IsLaying(__instance))
        {
            target = eyeHeight * AnimalicaActionSystemLayingEyeHeight.Multiplier;
        }
        else if (IsSitting(__instance))
        {
            target = eyeHeight * AnimalicaActionSystem.SittingEyeHeightMultiplier;
        }

        if (target is double desired)
        {
            double change = (desired - __state) * 5d * dt;
            __instance.LocalEyePos.Y = change >= 0
                ? Math.Min(__state + change, desired)
                : Math.Max(__state + change, desired);
        }
    }

    private static bool IsLaying(EntityPlayer player)
    {
        return !AnimalicaCoreSystem.OldSitModeEnabled
            && AnimalicaActionSystem.IsEligiblePlayer(player)
            && player.WatchedAttributes.GetInt(AnimalicaActionSystem.PostureAttribute, 0) == 2
            && !player.Controls.TriesToMove;
    }

    private static bool IsSitting(EntityPlayer player)
    {
        return !AnimalicaCoreSystem.OldSitModeEnabled
            && AnimalicaActionSystem.IsEligiblePlayer(player)
            && AnimalicaActionSystem.UsesRaisedSittingHeight(player)
            && player.WatchedAttributes.GetInt(AnimalicaActionSystem.PostureAttribute, 0) != 2
            && player.Controls.FloorSitting
            && !player.Controls.TriesToMove;
    }
}

internal static class AnimalicaSitInputPatch
{
    private static readonly Type? SystemPlayerControlType =
        AccessTools.TypeByName("Vintagestory.Client.NoObf.SystemPlayerControl");
    private static readonly FieldInfo? SittingKeyField = SystemPlayerControlType == null
        ? null
        : AccessTools.Field(SystemPlayerControlType, "sittingKey");
    private static readonly FieldInfo? NowFloorSittingField = SystemPlayerControlType == null
        ? null
        : AccessTools.Field(SystemPlayerControlType, "nowFloorSitting");
    private static AnimalicaActionSystem? actionSystem;

    public static void Start(Harmony owner, AnimalicaActionSystem system)
    {
        actionSystem = system;
        if (SystemPlayerControlType == null)
        {
            return;
        }

        MethodInfo? target = AccessTools.Method(SystemPlayerControlType, "OnKeyDown");
        MethodInfo? prefix = AccessTools.Method(typeof(AnimalicaSitInputPatch), nameof(BeforeOnKeyDown));
        if (target != null && prefix != null)
        {
            owner.Patch(target, prefix: new HarmonyMethod(prefix));
        }
    }

    public static void Stop()
    {
        actionSystem = null;
    }

    public static void SetInternalSitToggle(object controlSystem, bool sit)
    {
        NowFloorSittingField?.SetValue(controlSystem, sit);
    }

    private static bool BeforeOnKeyDown(object __instance, KeyEvent args)
    {
        if (AnimalicaCoreSystem.OldSitModeEnabled)
        {
            return true;
        }

        if (actionSystem == null
            || SittingKeyField?.GetValue(__instance) is not int sittingKey
            || args.KeyCode != sittingKey)
        {
            return true;
        }

        return !actionSystem.TryHandleSitKeyPress(__instance, args);
    }
}

internal static class AnimalicaActionSystemLayingEyeHeight
{
    public const float Multiplier = 0.25f;
}
