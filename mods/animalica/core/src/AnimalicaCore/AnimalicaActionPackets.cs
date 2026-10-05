using ProtoBuf;

namespace AnimalicaCore;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class AnimalicaPosturePacket
{
    public byte Action { get; set; }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class AnimalicaVocalizePacket
{
    public string VoiceCode { get; set; } = string.Empty;
}
