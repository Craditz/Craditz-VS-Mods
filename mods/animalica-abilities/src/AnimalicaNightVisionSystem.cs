using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ScentTrails;

public sealed class AnimalicaNightVisionSystem : ModSystem
{
    public const string TuningHotkeyCode = "animalicaabilities-nightvision-tuning";

    private ICoreClientAPI? clientApi;
    private AnimalicaClientSettingsSystem? clientSettings;
    private AnimalicaNightVisionRenderer? renderer;
    private IShaderProgram? shader;
    private GuiDialogAnimalicaNightVision? dialog;
    private AnimalicaAbilityProfile abilityProfile = AnimalicaAbilityProfile.None;
    private long profileRefreshListenerId;

    public AnimalicaNightVisionTuning Tuning { get; } = new();

    public override bool ShouldLoad(EnumAppSide side)
    {
        return side == EnumAppSide.Client;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        clientApi = api;
        clientSettings = api.ModLoader.GetModSystem<AnimalicaClientSettingsSystem>();
        NightVisionStrengthProfile profile = clientSettings.Settings.NightSightStrength;
        Tuning.ApplyProfile(Enum.IsDefined(profile) ? profile : NightVisionStrengthProfile.Strong);
        Tuning.Enabled = false;
        RefreshAbilityProfile();
        renderer = new AnimalicaNightVisionRenderer(api, Tuning);
        dialog = new GuiDialogAnimalicaNightVision(api, this);

        api.Event.ReloadShader += LoadShader;
        LoadShader();
        api.Event.RegisterRenderer(renderer, EnumRenderStage.AfterBlit, "animalicaabilities-nightvision");

        api.Input.RegisterHotKey(
            TuningHotkeyCode,
            Lang.Get("animalicaabilities:hotkey-nightvision-tuning"),
            GlKeys.F8,
            HotkeyType.GUIOrOtherControls
        );
        api.Input.SetHotKeyHandler(TuningHotkeyCode, OnToggleTuningHotkey);
        profileRefreshListenerId = api.Event.RegisterGameTickListener(_ => RefreshAbilityProfile(), 250);

        api.Logger.Notification("[AnimalicaAbilities] Night Sight loaded. Press N for abilities or F8 to tune.");
    }

    public override void Dispose()
    {
        if (clientApi != null && renderer != null)
        {
            clientApi.Event.UnregisterRenderer(renderer, EnumRenderStage.AfterBlit);
            if (profileRefreshListenerId != 0)
            {
                clientApi.Event.UnregisterGameTickListener(profileRefreshListenerId);
            }
        }

        dialog?.TryClose();
        renderer?.Dispose();
        shader?.Dispose();
        clientApi = null;
        clientSettings = null;
        Tuning.Enabled = false;
        renderer = null;
        shader = null;
        dialog = null;
        profileRefreshListenerId = 0;
        abilityProfile = AnimalicaAbilityProfile.None;
    }

    public void ToggleNightVision()
    {
        RefreshAbilityProfile();
        if (!abilityProfile.HasNightSight || clientApi == null || clientSettings == null)
        {
            return;
        }

        Tuning.Enabled = !Tuning.Enabled;
        clientSettings.Settings.NightSightEnabled = Tuning.Enabled;
        clientSettings.Save(clientApi);
        clientApi.ShowChatMessage(Lang.Get(
            Tuning.Enabled
                ? "animalicaabilities:nightvision-on"
                : "animalicaabilities:nightvision-off"
        ));
    }

    public bool ApplyStrengthProfile(NightVisionStrengthProfile profile)
    {
        if (!HasNightSightAbility || clientApi == null || clientSettings == null)
        {
            return false;
        }

        Tuning.ApplyProfile(profile);
        clientSettings.Settings.NightSightStrength = profile;
        clientSettings.Save(clientApi);
        return true;
    }

    public void OpenTuningDialog()
    {
        RefreshAbilityProfile();
        if (abilityProfile.HasNightSight && dialog != null && !dialog.IsOpened())
        {
            dialog.TryOpen();
        }
    }

    private bool OnToggleTuningHotkey(KeyCombination combination)
    {
        if (!HasNightSightAbility || dialog == null)
        {
            return false;
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

    public bool LoadShader()
    {
        if (clientApi == null || renderer == null)
        {
            return false;
        }

        IShaderProgram newShader = clientApi.Shader.NewShaderProgram();
        newShader.AssetDomain = "animalicaabilities";
        newShader.VertexShader = clientApi.Shader.NewShader(EnumShaderType.VertexShader);
        newShader.FragmentShader = clientApi.Shader.NewShader(EnumShaderType.FragmentShader);
        clientApi.Shader.RegisterFileShaderProgram("nightvision", newShader);

        if (!newShader.Compile())
        {
            clientApi.Logger.Error("[AnimalicaAbilities] Night Sight shader failed to compile.");
            newShader.Dispose();
            return false;
        }

        IShaderProgram? previousShader = shader;
        shader = newShader;
        renderer.Shader = newShader;
        previousShader?.Dispose();
        return true;
    }

    public bool HasNightSightAbility
    {
        get
        {
            RefreshAbilityProfile();
            return abilityProfile.HasNightSight;
        }
    }

    public AnimalicaAbilityProfile AbilityProfile
    {
        get
        {
            RefreshAbilityProfile();
            return abilityProfile;
        }
    }

    private void RefreshAbilityProfile()
    {
        if (clientApi == null)
        {
            return;
        }

        AnimalicaAbilityProfile nextProfile = AnimalicaAbilityResolver.Resolve(clientApi.World.Player?.Entity);
        if (nextProfile.Race.Equals(abilityProfile.Race, StringComparison.OrdinalIgnoreCase)
            && nextProfile.Abilities == abilityProfile.Abilities)
        {
            return;
        }

        bool gainedNightSight = !abilityProfile.HasNightSight && nextProfile.HasNightSight;
        abilityProfile = nextProfile;
        if (!abilityProfile.HasNightSight)
        {
            Tuning.Enabled = false;
            dialog?.TryClose();
        }
        else if (gainedNightSight)
        {
            Tuning.Enabled = clientSettings?.Settings.NightSightEnabled ?? true;
        }
    }
}
