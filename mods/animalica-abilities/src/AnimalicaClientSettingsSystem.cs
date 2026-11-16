using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ScentTrails;

public sealed class AnimalicaClientSettingsSystem : ModSystem
{
    private const string FileName = "AnimalicaAbilities.Client.json";

    internal ClientSettings Settings { get; private set; } = new();

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    // Load before the client systems that apply these preferences.
    public override double ExecuteOrder() => 0;

    public override void StartClientSide(ICoreClientAPI api)
    {
        Settings = new ClientSettings();
        try
        {
            Settings = api.LoadModConfig<ClientSettings>(FileName) ?? Settings;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            api.Logger.Warning("[AnimalicaAbilities] Could not read {0}: {1}", FileName, error.Message);
        }
    }

    internal void Save(ICoreClientAPI api)
    {
        try
        {
            api.StoreModConfig(Settings, FileName);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            api.Logger.Warning("[AnimalicaAbilities] Could not save {0}: {1}", FileName, error.Message);
        }
    }

    internal sealed class ClientSettings
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public NightVisionStrengthProfile NightSightStrength { get; set; } = NightVisionStrengthProfile.Strong;

        [JsonConverter(typeof(StringEnumConverter))]
        public ScentStrengthProfile ScentStrength { get; set; } = ScentStrengthProfile.Strong;

        public bool NightSightEnabled { get; set; } = true;
        public bool ScentEnabled { get; set; }
    }
}
