using System;
using ProtoBuf;

namespace ScentTrails;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class ScentRequestPacket
{
    public int ProtocolVersion { get; set; } = 1;
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class ScentResponsePacket
{
    public ScentNodePacket[] Nodes { get; set; } = Array.Empty<ScentNodePacket>();
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class ScentNodePacket
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public int AgeMilliseconds { get; set; }
    public long TrailId { get; set; }
}
