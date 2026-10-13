#nullable enable
using ProtoBuf;
namespace FeralKinshipCompanions;
[ProtoContract]
public sealed class CompanionWellbeingReadRequest
{
    [ProtoMember(1)] public string FoxId = string.Empty;
    [ProtoMember(2)] public string RequestId = string.Empty;
}
[ProtoContract]
public sealed class CompanionWellbeingReadResponse
{
    [ProtoMember(1)] public string FoxId = string.Empty;
    [ProtoMember(2)] public string RequestId = string.Empty;
    [ProtoMember(3)] public bool Live;
    [ProtoMember(4)] public string Message = string.Empty;
    [ProtoMember(5)] public string Mood = string.Empty;
    [ProtoMember(6)] public CompanionWellbeingState? Wellbeing;
}
