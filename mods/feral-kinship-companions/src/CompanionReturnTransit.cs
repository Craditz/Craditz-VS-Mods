#nullable enable
using ProtoBuf;

namespace FeralKinshipCompanions;

// This journal owns normal returns only. It never grants reconstruction or
// reward authority. Schema60 prevents an older reader discarding owner consent.
[ProtoContract]
public sealed class CompanionReturnTransit
{
    [ProtoMember(1)] public string Trip = string.Empty;
    [ProtoMember(2)] public string Outcome = string.Empty;
    [ProtoMember(3)] public string Stage = string.Empty;
    [ProtoMember(4)] public string Failure = string.Empty;
    [ProtoMember(5)] public long LastAttemptUtcMs;
    [ProtoMember(6)] public int Attempts;
    [ProtoMember(7)] public string MissingCartChoice = string.Empty;
    [ProtoMember(8)] public string ChoiceToken = string.Empty;
    [ProtoMember(9)] public string Destination = string.Empty;
    [ProtoMember(10)] public long NativeId;
    // Persisted before native clocks/outcome change, so resume is idempotent.
    [ProtoMember(11)] public bool OutcomePrepared;
    [ProtoMember(12)] public bool WasMortallyWounded;
    [ProtoMember(13)] public double OutcomeHours;
    [ProtoMember(14)] public int FallbackStart;
}

public partial class FoxPackRecordV2
{
    [ProtoMember(180)] public CompanionReturnTransit? ReturnTransit;
}

[ProtoContract]
public sealed class CompanionReturnChoiceOffer
{
    [ProtoMember(1)] public string Token = string.Empty;
    [ProtoMember(2)] public int Count;
}

[ProtoContract]
public sealed class CompanionReturnChoiceResponse
{
    [ProtoMember(1)] public string Token = string.Empty;
    [ProtoMember(2)] public bool PlaceNearPlayer;
}
