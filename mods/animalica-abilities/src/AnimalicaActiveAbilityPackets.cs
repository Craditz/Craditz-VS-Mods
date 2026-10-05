using ProtoBuf;

namespace ScentTrails;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class AnimalicaActiveAbilityRequestPacket
{
    public int ProtocolVersion { get; set; } = 1;
    public AnimalicaActiveAbility Ability { get; set; }
    public long TargetEntityId { get; set; }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class AnimalicaActiveAbilityResultPacket
{
    public bool Success { get; set; }
    public AnimalicaActiveAbility Ability { get; set; }
    public int CooldownMilliseconds { get; set; }
    public string MessageKey { get; set; } = string.Empty;
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class AnimalicaActiveAbilityPounceMotionPacket
{
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double OffsetZ { get; set; }
    public int DurationMilliseconds { get; set; }
    public bool Cancelled { get; set; }
}
