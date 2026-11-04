using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Druidry;

public sealed class DruidrySystem : ModSystem
{
    public const string MenuHotkeyCode = "druidry-circle-of-forms";
    private const string NetworkChannelName = "druidry:main";
    private const string UnlockedAttribute = "druidryUnlockedForms";
    private const string ActiveFormAttribute = "druidryActiveForm";
    private const string TrueFormAttribute = "druidryTrueForm";
    private const int ShiftCooldownMs = 3000;

    private readonly DruidAppearanceCatalog appearanceCatalog = new();
    private readonly Dictionary<string, long> nextShiftAtByPlayer = new(StringComparer.Ordinal);
    private ICoreServerAPI? serverApi;
    private ICoreClientAPI? clientApi;
    private IServerNetworkChannel? serverChannel;
    private IClientNetworkChannel? clientChannel;
    private DruidPlayerModelAdapter? modelAdapter;
    private GuiDialogDruidCircle? dialog;
    private readonly DruidClientState clientState = new();

    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockClass("DruidRitualAltar", typeof(BlockDruidRitualAltar));
        api.Network.RegisterChannel(NetworkChannelName)
            .RegisterMessageType<DruidStateRequestPacket>()
            .RegisterMessageType<DruidShiftRequestPacket>()
            .RegisterMessageType<DruidRitualEffectPacket>()
            .RegisterMessageType<DruidStatePacket>();
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        appearanceCatalog.Load(api);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        clientApi = api;
        clientChannel = api.Network.GetChannel(NetworkChannelName)
            .SetMessageHandler<DruidStatePacket>(OnClientState)
            .SetMessageHandler<DruidRitualEffectPacket>(OnClientRitualEffect);
        api.Input.RegisterHotKey(
            MenuHotkeyCode,
            Lang.Get("druidry:hotkey-circle"),
            GlKeys.R,
            HotkeyType.CharacterControls,
            shiftPressed: true
        );
        api.Input.SetHotKeyHandler(MenuHotkeyCode, ToggleCircle);
        dialog = new GuiDialogDruidCircle(api, this);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        serverApi = api;
        modelAdapter = new DruidPlayerModelAdapter(api);
        serverChannel = api.Network.GetChannel(NetworkChannelName)
            .SetMessageHandler<DruidStateRequestPacket>(OnStateRequest)
            .SetMessageHandler<DruidShiftRequestPacket>(OnShiftRequest);
        api.Event.PlayerNowPlaying += OnPlayerReady;
        api.Event.PlayerReady += OnPlayerReady;
        api.Event.PlayerRespawn += OnPlayerReady;
        api.Event.PlayerDeath += OnPlayerDeath;
        api.Event.PlayerDisconnect += OnPlayerDisconnect;

        var parsers = api.ChatCommands.Parsers;
        api.ChatCommands.Create("druid")
            .RequiresPlayer()
            .RequiresPrivilege(Privilege.chat)
            .WithDescription("Shows Druidry status or manages the recorded true form.")
            .WithArgs(parsers.OptionalWord("action"), parsers.OptionalWord("form"))
            .HandleWith(HandleDruidCommand);
    }

    public override void Dispose()
    {
        if (serverApi != null)
        {
            serverApi.Event.PlayerNowPlaying -= OnPlayerReady;
            serverApi.Event.PlayerReady -= OnPlayerReady;
            serverApi.Event.PlayerRespawn -= OnPlayerReady;
            serverApi.Event.PlayerDeath -= OnPlayerDeath;
            serverApi.Event.PlayerDisconnect -= OnPlayerDisconnect;
        }
        dialog?.TryClose();
        dialog?.Dispose();
        dialog = null;
        clientChannel = null;
        serverChannel = null;
        clientApi = null;
        serverApi = null;
        modelAdapter = null;
        nextShiftAtByPlayer.Clear();
    }

    internal DruidClientState ClientState => clientState;

    internal void RequestState()
    {
        clientChannel?.SendPacket(new DruidStateRequestPacket());
    }

    internal void RequestShift(string formCode)
    {
        clientChannel?.SendPacket(new DruidShiftRequestPacket { FormCode = formCode ?? string.Empty });
    }

    public void OpenRites(IServerPlayer player)
    {
        SendState(player, string.Empty, openRites: true);
    }

    public void PerformRitual(IServerPlayer player, string formCode, ItemSlot tokenSlot, BlockPos altarPosition)
    {
        if (serverApi?.World.BlockAccessor.GetRainMapHeightAt(altarPosition.X, altarPosition.Z) > altarPosition.Y + 1)
        {
            SendState(player, "The rite needs open sky above the altar.", openRites: true);
            return;
        }
        if (!DruidForms.TryGet(formCode, out DruidFormDefinition form))
        {
            SendState(player, "That token carries no spirit Druidry recognizes.");
            return;
        }
        if (!IsFormPackAvailable(form))
        {
            SendState(player, $"The {form.Name} spirit cannot answer until {form.PackName} is loaded.", openRites: true);
            return;
        }
        if (modelAdapter?.GetModelAvailability(form.ModelCode) == DruidModelAvailability.Missing)
        {
            SendState(player, $"The {form.Name} model is not available in the loaded {form.PackName} pack.", openRites: true);
            return;
        }

        HashSet<string> unlocked = GetUnlocked(player.Entity);
        if (unlocked.Contains(form.Code))
        {
            SendState(player, $"You already know the shape of {form.Name}.", openRites: true);
            return;
        }

        if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            tokenSlot.TakeOut(1);
            tokenSlot.MarkDirty();
        }
        unlocked.Add(form.Code);
        SetUnlocked(player.Entity, unlocked);
        serverApi?.Logger.Notification(
            "[Druidry] {0} completed the {1} rite at {2} and unlocked {3}.",
            player.PlayerName,
            form.Title,
            altarPosition,
            form.Code
        );
        serverChannel?.SendPacket(new DruidRitualEffectPacket
        {
            X = altarPosition.X,
            Y = altarPosition.Y,
            Z = altarPosition.Z
        }, player);
        SendState(player, $"The rite is answered. The {form.Name} shape is now part of you.", openRites: true);
    }

    private bool ToggleCircle(KeyCombination keyCombination)
    {
        if (dialog == null) return false;
        if (dialog.IsOpened()) dialog.TryClose();
        else
        {
            RequestState();
            dialog.OpenForms();
            dialog.TryOpen();
        }
        return true;
    }

    private void OnClientState(DruidStatePacket packet)
    {
        if (clientApi == null) return;
        clientApi.Event.EnqueueMainThreadTask(() =>
        {
            clientState.Update(packet);
            dialog?.OnStateUpdated();
            if (!string.IsNullOrWhiteSpace(packet.Message))
            {
                clientApi.TriggerIngameError(this, "druidry-state", packet.Message);
            }
            if (packet.OpenRites && dialog != null)
            {
                dialog.OpenRites();
                if (!dialog.IsOpened()) dialog.TryOpen();
            }
        }, "druidry-state");
    }

    private void OnClientRitualEffect(DruidRitualEffectPacket packet)
    {
        if (clientApi == null) return;
        clientApi.Event.EnqueueMainThreadTask(() =>
        {
            Vec3d altar = new(packet.X + 0.5, packet.Y + 0.9, packet.Z + 0.5);
            clientApi.ModLoader.GetModSystem<WeatherSystemClient>()?.SpawnLightningFlash(altar);

            int gold = ColorUtil.ToRgba(255, 255, 206, 92);
            SimpleParticleProperties sparks = new(
                18f,
                28f,
                gold,
                altar.AddCopy(-0.38, -0.15, -0.38),
                altar.AddCopy(0.38, 0.25, 0.38),
                new Vec3f(-0.7f, 0.4f, -0.7f),
                new Vec3f(0.7f, 1.4f, 0.7f),
                1.15f,
                -0.15f,
                0.12f,
                0.28f,
                EnumParticleModel.Quad
            )
            {
                IgnoreUserConfig = true,
                LightEmission = ColorUtil.ToRgba(255, 255, 220, 120),
                VertexFlags = 255
            };
            clientApi.World.SpawnParticles(sparks);
            clientApi.World.PlaySoundAt(
                new AssetLocation("sounds/effect/translocate"),
                altar.X,
                altar.Y,
                altar.Z,
                null,
                false,
                24,
                0.7f
            );
        }, "druidry-ritual-effect");
    }

    private void OnStateRequest(IServerPlayer player, DruidStateRequestPacket packet)
    {
        SendState(player);
    }

    private void OnShiftRequest(IServerPlayer player, DruidShiftRequestPacket packet)
    {
        string formCode = (packet.FormCode ?? string.Empty).Trim().ToLowerInvariant();
        if (formCode.Length > 48) return;
        if (string.IsNullOrEmpty(formCode)) RestoreTrueForm(player);
        else ShiftInto(player, formCode);
    }

    private void ShiftInto(IServerPlayer player, string formCode)
    {
        if (serverApi == null || modelAdapter == null || player.Entity == null || !player.Entity.Alive)
        {
            return;
        }
        if (!DruidForms.TryGet(formCode, out DruidFormDefinition form))
        {
            SendState(player, "That wildshape is unknown.");
            return;
        }
        if (!GetUnlocked(player.Entity).Contains(form.Code))
        {
            SendState(player, $"You have not completed the {form.Name} rite.");
            return;
        }
        if (GetRemainingCooldown(player) > 0)
        {
            SendState(player, "Your shape is still settling.");
            return;
        }
        if (player.Entity.MountedOn != null)
        {
            SendState(player, "Dismount before changing shape.");
            return;
        }
        if (!appearanceCatalog.TryGet(form.ModelCode, out IReadOnlyDictionary<string, string>? appearance))
        {
            SendState(player, $"The {form.Name} appearance ritual is missing from {form.PackName}.");
            return;
        }

        if (!EnsureTrueForm(player, out string captureError))
        {
            SendState(player, captureError);
            return;
        }
        if (!modelAdapter.ApplyWildshape(player.Entity, form, appearance, out string error))
        {
            SendState(player, error);
            return;
        }

        player.Entity.WatchedAttributes.SetString(ActiveFormAttribute, form.Code);
        player.Entity.WatchedAttributes.MarkPathDirty(ActiveFormAttribute);
        nextShiftAtByPlayer[player.PlayerUID] = serverApi.World.ElapsedMilliseconds + ShiftCooldownMs;
        SendState(player, $"You assume the shape of {form.Name}.");
    }

    private void RestoreTrueForm(IServerPlayer player, bool ignoreCooldown = false)
    {
        if (serverApi == null || modelAdapter == null || player.Entity == null) return;
        string active = player.Entity.WatchedAttributes.GetString(ActiveFormAttribute, string.Empty);
        if (string.IsNullOrEmpty(active))
        {
            SendState(player, "You are already in your true form.");
            return;
        }
        if (!ignoreCooldown && GetRemainingCooldown(player) > 0)
        {
            SendState(player, "Your shape is still settling.");
            return;
        }
        ITreeAttribute? trueForm = player.Entity.WatchedAttributes.GetTreeAttribute(TrueFormAttribute);
        string error = string.Empty;
        if (trueForm == null || !modelAdapter.RestoreTrueForm(player.Entity, trueForm, out error))
        {
            SendState(player, error.Length == 0 ? "No true form has been recorded." : error);
            return;
        }

        player.Entity.WatchedAttributes.SetString(ActiveFormAttribute, string.Empty);
        player.Entity.WatchedAttributes.MarkPathDirty(ActiveFormAttribute);
        nextShiftAtByPlayer[player.PlayerUID] = serverApi.World.ElapsedMilliseconds + ShiftCooldownMs;
        SendState(player, "You return to your true form.");
    }

    private bool EnsureTrueForm(IServerPlayer player, out string error)
    {
        if (player.Entity.WatchedAttributes.GetTreeAttribute(TrueFormAttribute) != null)
        {
            error = string.Empty;
            return true;
        }
        string currentModel = player.Entity.WatchedAttributes.GetString("skinModel", "seraph");
        if (DruidForms.LooksLikeAnimalicaModel(currentModel))
        {
            error = "Choose your non-Animalica true form first, then use /druid trueform to record it.";
            return false;
        }

        player.Entity.WatchedAttributes.SetAttribute(TrueFormAttribute, DruidPlayerModelAdapter.CaptureTrueForm(player.Entity));
        player.Entity.WatchedAttributes.MarkPathDirty(TrueFormAttribute);
        error = string.Empty;
        return true;
    }

    private void OnPlayerReady(IServerPlayer player)
    {
        if (serverApi == null) return;
        serverApi.Event.RegisterCallback(deltaTime =>
        {
            if (player.Entity == null) return;
            string active = player.Entity.WatchedAttributes.GetString(ActiveFormAttribute, string.Empty);
            if (DruidForms.TryGet(active, out DruidFormDefinition form)
                && GetUnlocked(player.Entity).Contains(form.Code)
                && appearanceCatalog.TryGet(form.ModelCode, out IReadOnlyDictionary<string, string>? appearance)
                && modelAdapter?.ApplyWildshape(player.Entity, form, appearance, out string _) == true)
            {
                SendState(player);
                return;
            }
            if (!string.IsNullOrEmpty(active))
            {
                ITreeAttribute? trueForm = player.Entity.WatchedAttributes.GetTreeAttribute(TrueFormAttribute);
                if (trueForm != null)
                {
                    modelAdapter?.RestoreTrueForm(player.Entity, trueForm, out string _);
                }
                player.Entity.WatchedAttributes.SetString(ActiveFormAttribute, string.Empty);
                player.Entity.WatchedAttributes.MarkPathDirty(ActiveFormAttribute);
            }
            SendState(player);
        }, 350);
    }

    private void OnPlayerDeath(IServerPlayer player, DamageSource damageSource)
    {
        if (player.Entity?.WatchedAttributes.GetString(ActiveFormAttribute, string.Empty).Length > 0)
        {
            RestoreTrueForm(player, ignoreCooldown: true);
        }
    }

    private void OnPlayerDisconnect(IServerPlayer player)
    {
        nextShiftAtByPlayer.Remove(player.PlayerUID);
    }

    private TextCommandResult HandleDruidCommand(TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player || player.Entity == null)
        {
            return TextCommandResult.Error("This command must be used by a player.");
        }
        string action = (args[0] as string ?? "status").ToLowerInvariant();
        string formCode = (args[1] as string ?? string.Empty).ToLowerInvariant();
        switch (action)
        {
            case "status":
                HashSet<string> unlocked = GetUnlocked(player.Entity);
                string active = player.Entity.WatchedAttributes.GetString(ActiveFormAttribute, string.Empty);
                return TextCommandResult.Success($"Druidry: {unlocked.Count}/{DruidForms.All.Count} spirits unlocked; current shape: {(active.Length == 0 ? "true form" : active)}.");
            case "trueform":
                if (player.Entity.WatchedAttributes.GetString(ActiveFormAttribute, string.Empty).Length > 0)
                    return TextCommandResult.Error("Return to your true form before recording it.");
                string currentModel = player.Entity.WatchedAttributes.GetString("skinModel", "seraph");
                if (DruidForms.LooksLikeAnimalicaModel(currentModel))
                    return TextCommandResult.Error("Your true form must use a non-Animalica model.");
                player.Entity.WatchedAttributes.SetAttribute(TrueFormAttribute, DruidPlayerModelAdapter.CaptureTrueForm(player.Entity));
                player.Entity.WatchedAttributes.MarkPathDirty(TrueFormAttribute);
                SendState(player);
                return TextCommandResult.Success("Your current appearance is now recorded as your true form.");
            case "unlock":
                if (!player.HasPrivilege(Privilege.controlserver)) return TextCommandResult.Error("This test command requires controlserver privilege.");
                if (!DruidForms.TryGet(formCode, out DruidFormDefinition form)) return TextCommandResult.Error("Unknown form code.");
                HashSet<string> current = GetUnlocked(player.Entity);
                current.Add(form.Code);
                SetUnlocked(player.Entity, current);
                SendState(player);
                return TextCommandResult.Success($"Unlocked {form.Name} for testing.");
            case "unlockall":
                if (!player.HasPrivilege(Privilege.controlserver)) return TextCommandResult.Error("This test command requires controlserver privilege.");
                SetUnlocked(player.Entity, DruidForms.All.Select(formValue => formValue.Code));
                SendState(player);
                return TextCommandResult.Success("Unlocked all Druidry forms for testing.");
            case "reset":
                if (!player.HasPrivilege(Privilege.controlserver)) return TextCommandResult.Error("This test command requires controlserver privilege.");
                if (player.Entity.WatchedAttributes.GetString(ActiveFormAttribute, string.Empty).Length > 0) RestoreTrueForm(player, ignoreCooldown: true);
                SetUnlocked(player.Entity, Array.Empty<string>());
                SendState(player);
                return TextCommandResult.Success("Reset all Druidry unlocks.");
            default:
                return TextCommandResult.Error("Use /druid status, /druid trueform, or the privileged test actions unlock, unlockall, and reset.");
        }
    }

    private void SendState(IServerPlayer player, string message = "", bool openRites = false)
    {
        if (serverChannel == null || player.Entity == null) return;
        serverChannel.SendPacket(new DruidStatePacket
        {
            UnlockedForms = GetUnlocked(player.Entity).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            AvailableForms = DruidForms.All.Where(IsFormPackAvailable).Select(form => form.Code).ToArray(),
            ActiveForm = player.Entity.WatchedAttributes.GetString(ActiveFormAttribute, string.Empty),
            HasTrueForm = player.Entity.WatchedAttributes.GetTreeAttribute(TrueFormAttribute) != null,
            CooldownMilliseconds = GetRemainingCooldown(player),
            Message = message,
            OpenRites = openRites
        }, player);
    }

    private int GetRemainingCooldown(IServerPlayer player)
    {
        if (serverApi == null || !nextShiftAtByPlayer.TryGetValue(player.PlayerUID, out long next)) return 0;
        long remaining = next - serverApi.World.ElapsedMilliseconds;
        return remaining <= 0 ? 0 : (int)Math.Min(int.MaxValue, remaining);
    }

    private bool IsFormPackAvailable(DruidFormDefinition form)
    {
        return serverApi?.ModLoader.IsModEnabled(form.RequiredModId) == true
            && appearanceCatalog.TryGet(form.ModelCode, out _);
    }

    private static HashSet<string> GetUnlocked(EntityPlayer player)
    {
        return new HashSet<string>(
            player.WatchedAttributes.GetStringArray(UnlockedAttribute, Array.Empty<string>())
                .Where(code => DruidForms.TryGet(code, out _)),
            StringComparer.OrdinalIgnoreCase
        );
    }

    private static void SetUnlocked(EntityPlayer player, IEnumerable<string> forms)
    {
        string[] values = forms.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(code => DruidForms.TryGet(code, out _))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
        player.WatchedAttributes.SetStringArray(UnlockedAttribute, values);
        player.WatchedAttributes.MarkPathDirty(UnlockedAttribute);
    }
}
