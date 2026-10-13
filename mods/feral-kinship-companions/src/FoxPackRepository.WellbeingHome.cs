#nullable enable
using System;
using System.Collections.Generic;
using ProtoBuf;
namespace FeralKinshipCompanions;

[ProtoContract]
public sealed class CompanionOwnerHomeRecord
{
    [ProtoMember(1)] public string OwnerUid = string.Empty;
    [ProtoMember(2)] public bool EverEstablished;
}
internal sealed partial class FoxPackRepository
{
    private readonly Dictionary<string, CompanionOwnerHomeRecord> wellbeingHomes = new(StringComparer.Ordinal);
    public CompanionOwnerHomeRecord GetWellbeingHome(string ownerUid)
    {
        if (!wellbeingHomes.TryGetValue(ownerUid, out var home))
            wellbeingHomes[ownerUid] = home = new CompanionOwnerHomeRecord { OwnerUid = ownerUid };
        return home;
    }
    public bool MarkWellbeingHomeEstablished(string ownerUid)
    {
        if (string.IsNullOrWhiteSpace(ownerUid)) return false;
        var home = GetWellbeingHome(ownerUid);
        bool changed = !home.EverEstablished;
        home.EverEstablished = true;
        return changed;
    }
}
