using Newtonsoft.Json;

namespace AnimalicaCore;

public sealed class AnimalicaFirstPersonConfig
{
    public const string DefaultDescription = "Client-only Seraph first-person animation proxy for Animalica models. Disabled by default. Enable or disable it in AnimalicaFirstPerson.json; when enabled, use F9 to tune independent camera-space hand offsets.";
    public const float DefaultMainHandOffsetX = 1.05f;
    public const float DefaultMainHandOffsetY = -0.55f;
    public const float DefaultMainHandOffsetZ = 0f;
    public const float DefaultOffHandOffsetX = -0.45f;
    public const float DefaultOffHandOffsetY = -0.65f;
    public const float DefaultOffHandOffsetZ = 0.80f;
    public const float DefaultPositionStep = 0.05f;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonProperty("seraphYawDegrees")]
    public float SeraphYawDegrees { get; set; } = -90f;

    [JsonProperty("mainHandOffsetX")]
    public float MainHandOffsetX { get; set; } = DefaultMainHandOffsetX;

    [JsonProperty("mainHandOffsetY")]
    public float MainHandOffsetY { get; set; } = DefaultMainHandOffsetY;

    [JsonProperty("mainHandOffsetZ")]
    public float MainHandOffsetZ { get; set; } = DefaultMainHandOffsetZ;

    [JsonProperty("offHandOffsetX")]
    public float OffHandOffsetX { get; set; } = DefaultOffHandOffsetX;

    [JsonProperty("offHandOffsetY")]
    public float OffHandOffsetY { get; set; } = DefaultOffHandOffsetY;

    [JsonProperty("offHandOffsetZ")]
    public float OffHandOffsetZ { get; set; } = DefaultOffHandOffsetZ;

    [JsonProperty("positionStep")]
    public float PositionStep { get; set; } = DefaultPositionStep;

    [JsonProperty("description")]
    public string Description { get; set; } = DefaultDescription;
}
