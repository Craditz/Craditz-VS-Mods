using ProtoBuf;

namespace AnimalicaEmotes;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class AnimalEmotePacket
{
    public string Pose { get; set; } = string.Empty;
}
