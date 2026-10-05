using System.Collections.Generic;
using ProtoBuf;

namespace FeralKinshipCompanions;

[ProtoContract]
public sealed class BrambleRequestPacket
{
    public const int Open = 1;
    public const int OnboardingChoice = 2;
    public const int SelectSpecies = 3;
    public const int SetHintMode = 4;
    public const int Refresh = 5;
    public const int BeginDismissal = 6;
    public const int ConfirmDismissal = 7;
    public const int CancelDismissal = 8;

    [ProtoMember(1)] public int Action;
    [ProtoMember(2)] public long TargetEntityId;
    [ProtoMember(3)] public string Value = string.Empty;
}

[ProtoContract]
public sealed class BrambleStatePacket
{
    [ProtoMember(1)] public bool Open;
    [ProtoMember(2)] public bool ShowOnboarding;
    [ProtoMember(3)] public string OnboardingState = string.Empty;
    [ProtoMember(4)] public string SpeciesId = "fox";
    [ProtoMember(5)] public bool SpeciesChosen;
    [ProtoMember(6)] public bool FirstTamingChapter;
    [ProtoMember(7)] public string HintMode = "quiet";
    [ProtoMember(8)] public bool Follow;
    [ProtoMember(9)] public long BrambleEntityId;
    [ProtoMember(10)] public string PlayerSpeciesRelationship = "unknown";
    [ProtoMember(11)] public List<string> AcceptedFoods = new();
    [ProtoMember(12)] public bool NearbyWildTarget;
    [ProtoMember(13)] public string RecommendationId = string.Empty;
    [ProtoMember(14)] public string RecommendationHeading = string.Empty;
    [ProtoMember(15)] public string RecommendationBody = string.Empty;
    [ProtoMember(16)] public List<string> RecommendationSteps = new();
    [ProtoMember(17)] public int RecommendationAction;
    [ProtoMember(18)] public long RecommendationTargetEntityId;
    [ProtoMember(19)] public string ResponseLine = string.Empty;
    [ProtoMember(20)] public bool DismissalPending;
}
