using ProtoBuf;

namespace Druidry;

[ProtoContract]
public sealed class DruidStateRequestPacket
{
}

[ProtoContract]
public sealed class DruidShiftRequestPacket
{
    [ProtoMember(1)]
    public string FormCode { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class DruidRitualEffectPacket
{
    [ProtoMember(1)]
    public int X { get; set; }

    [ProtoMember(2)]
    public int Y { get; set; }

    [ProtoMember(3)]
    public int Z { get; set; }
}

[ProtoContract]
public sealed class DruidStatePacket
{
    [ProtoMember(1)]
    public string[] UnlockedForms { get; set; } = System.Array.Empty<string>();

    [ProtoMember(2)]
    public string[] AvailableForms { get; set; } = System.Array.Empty<string>();

    [ProtoMember(3)]
    public string ActiveForm { get; set; } = string.Empty;

    [ProtoMember(4)]
    public bool HasTrueForm { get; set; }

    [ProtoMember(5)]
    public int CooldownMilliseconds { get; set; }

    [ProtoMember(6)]
    public string Message { get; set; } = string.Empty;

    [ProtoMember(7)]
    public bool OpenRites { get; set; }
}
