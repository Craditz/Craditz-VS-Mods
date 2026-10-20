#nullable enable
using System;
using System.Collections.Generic;
using ProtoBuf;

namespace FeralKinshipCompanions;

[ProtoContract] public sealed class NativeLinkRepair
{
    [ProtoMember(1)] public int Version = 1;
    [ProtoMember(2)] public string Id = string.Empty;
    [ProtoMember(3)] public string Stage = "Prepared";
    [ProtoMember(4)] public long OldId;
    [ProtoMember(5)] public long NewId;
    [ProtoMember(6)] public byte[] BeforeLedger = Array.Empty<byte>();
    [ProtoMember(7)] public byte[] BeforeNative = Array.Empty<byte>();
    [ProtoMember(8)] public string LedgerHash = string.Empty;
    [ProtoMember(9)] public string NativeHash = string.Empty;
    [ProtoMember(10)] public string Evidence = string.Empty;
    [ProtoMember(11)] public string Initiator = string.Empty;
    [ProtoMember(12)] public string Error = string.Empty;
    [ProtoMember(13)] public byte[] BeforeUndo = Array.Empty<byte>();
    [ProtoMember(14)] public string UndoHash = string.Empty;
}
public sealed partial class FoxPackRecordV2
{
    [ProtoMember(163)] public List<NativeLinkRepair> NativeLinks = new();
}
