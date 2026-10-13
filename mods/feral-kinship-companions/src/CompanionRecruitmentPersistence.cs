#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProtoBuf;

namespace FeralKinshipCompanions;

// Schema 52 is a reader barrier, not a reuse of held journal schemas 47-49.
// The recruitment section has its own format and version authority. This
// combined reader admits retained50/51 and explicitly scoped base46/base51.
internal static class CompanionLedgerSchema
{
    internal const int RecruitmentLedgerVersion = 52;
    internal const int RecruitmentSectionTag = 100;

    internal static bool SupportedHere(int version) => version is >= 1 and <= 46
        || version is 50 or 51 || version == RecruitmentLedgerVersion;

    // Walk wire framing only; reject missing/duplicate/wrong-wire authority
    // before interpreting any potentially incompatible journal/record payload.
    internal static bool TryReadAuthority(byte[] data, int versionTag, int sectionTag,
        out int version, out byte[]? section)
    {
        version = 0; section = null;
        bool found = false, foundSection = false;
        int index = 0;
        bool Varint(out ulong value)
        {
            value = 0;
            for (int shift = 0; shift < 64; shift += 7)
            {
                if (index >= data.Length) return false;
                byte b = data[index++];
                if (shift == 63 && b > 1) return false;
                value |= (ulong)(b & 127) << shift;
                if ((b & 128) == 0) return true;
            }
            return false;
        }
        while (index < data.Length)
        {
            if (!Varint(out ulong key) || key >> 3 is 0 or > 536870911) return false;
            int tag = (int)(key >> 3), wire = (int)(key & 7);
            if (tag == versionTag)
            {
                if (found || wire != 0 || !Varint(out ulong value) || value is 0 or > int.MaxValue) return false;
                version = (int)value; found = true; continue;
            }
            if (tag == sectionTag && (foundSection || wire != 2)) return false;
            ulong length;
            switch (wire)
            {
                case 0: if (!Varint(out _)) return false; continue;
                case 1: length = 8; break;
                case 2: if (!Varint(out length)) return false; break;
                case 5: length = 4; break;
                default: return false;
            }
            if (length > (ulong)(data.Length - index)) return false;
            if (tag == sectionTag)
            {
                section = new byte[(int)length];
                Array.Copy(data, index, section, 0, (int)length); foundSection = true;
            }
            index += (int)length;
        }
        return found;
    }

    internal static bool CanDecode(byte[]? raw, out int? version)
    {
        version = null;
        if (raw == null) return true; // No ledger yet; legacy keys remain separate.
        if (!TryReadAuthority(raw, 1, RecruitmentSectionTag, out int found, out byte[]? section)) return false;
        version = found;
        if (!SupportedHere(found)) return false;
        if (found != RecruitmentLedgerVersion) return section == null;
        return section != null && TryReadAuthority(section, 2, 1, out int extensionVersion, out byte[]? format)
            && extensionVersion == CompanionRecruitmentPersistence.SectionVersion
            && format != null && format.SequenceEqual(Encoding.UTF8.GetBytes(CompanionRecruitmentPersistence.FormatId))
            && TryReadAuthority(section, 4, -1, out int baseVersion, out _)
            && baseVersion is 46 or CompanionRecruitmentPersistence.BaseLedgerVersion;
    }
}

internal static class CompanionRecruitmentPersistence
{
    internal const string FormatId = "feralkinshipcompanions/recruitment-receipts";
    internal const int SectionVersion = 1;
    internal const int BaseLedgerVersion = 51; // Combined writer always preserves retained semantics.

    internal static int EffectiveBaseVersion(FoxPackSaveData saved) =>
        saved.Version == CompanionLedgerSchema.RecruitmentLedgerVersion
            ? saved.RecruitmentExtension!.BaseLedgerVersion : saved.Version;

    internal static FoxRecruitmentExtension Capture(IReadOnlyList<FoxRecruitmentRewardRecord> rewards) => new()
    {
        FormatId = FormatId, Version = SectionVersion, BaseLedgerVersion = BaseLedgerVersion,
        Receipts = rewards.Select((reward, index) => new FoxRecruitmentReceipt
        {
            RewardIndex = index, OwnerUid = reward.OwnerUid, SpeciesId = reward.SpeciesId,
            WildEntityCode = reward.WildEntityCode, ClaimFoxId = reward.ClaimFoxId,
            ClaimStarted = reward.ClaimStarted, TameEntityCode = reward.TameEntityCode
        }).ToList()
    };

    internal static bool Restore(FoxPackSaveData saved)
    {
        // A declared baseline payload cannot carry retained-native authority. Do
        // not reinterpret retained fields merely because the outer header is52.
        int basis = saved.Version == CompanionLedgerSchema.RecruitmentLedgerVersion
            ? saved.RecruitmentExtension?.BaseLedgerVersion ?? 0 : saved.Version;
        if (basis <= 46 && saved.Records != null && saved.Records.Any(record => record != null
            && (!string.IsNullOrEmpty(record.RetainedTrip) || !string.IsNullOrEmpty(record.RetainedPhase)
                || !string.IsNullOrEmpty(record.RetainedOutcome) || !string.IsNullOrEmpty(record.RetainedLegacyAuthority)
                || record.RetainedLegacyAdmissionHours != 0))) return false;
        if (saved.Version != CompanionLedgerSchema.RecruitmentLedgerVersion)
            return saved.RecruitmentExtension == null;
        var extension = saved.RecruitmentExtension;
        var rewards = saved.RecruitmentRewards;
        if (extension == null || extension.FormatId != FormatId || extension.Version != SectionVersion
            || extension.BaseLedgerVersion is not (46 or BaseLedgerVersion)
            || rewards == null || extension.Receipts == null || extension.Receipts.Count != rewards.Count) return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < rewards.Count; index++)
        {
            var reward = rewards[index]; var receipt = extension.Receipts[index];
            if (reward == null || receipt == null || receipt.RewardIndex != index
                || string.IsNullOrWhiteSpace(reward.OwnerUid) || receipt.OwnerUid != reward.OwnerUid
                || receipt.SpeciesId != reward.SpeciesId || receipt.WildEntityCode != reward.WildEntityCode
                || !Guid.TryParseExact(receipt.ClaimFoxId, "N", out _) || !ids.Add(receipt.ClaimFoxId)
                || (receipt.ClaimStarted && !CompanionNativeRecruitmentCatalog.Concrete(receipt.TameEntityCode))) return false;
        }
        // Validate the complete section first; a partial import must not become
        // a new unstarted receipt and permit a duplicate spawn.
        for (int index = 0; index < rewards.Count; index++)
        {
            rewards[index].ClaimFoxId = extension.Receipts[index].ClaimFoxId;
            rewards[index].ClaimStarted = extension.Receipts[index].ClaimStarted;
            rewards[index].TameEntityCode = extension.Receipts[index].TameEntityCode;
        }
        return true;
    }
}

[ProtoContract]
public sealed class FoxRecruitmentExtension
{
    [ProtoMember(1)] public string FormatId = string.Empty;
    [ProtoMember(2)] public int Version;
    [ProtoMember(3)] public List<FoxRecruitmentReceipt> Receipts = new();
    [ProtoMember(4)] public int BaseLedgerVersion;
}

[ProtoContract]
public sealed class FoxRecruitmentReceipt
{
    [ProtoMember(1)] public int RewardIndex;
    [ProtoMember(2)] public string OwnerUid = string.Empty;
    [ProtoMember(3)] public string SpeciesId = string.Empty;
    [ProtoMember(4)] public string WildEntityCode = string.Empty;
    [ProtoMember(5)] public string ClaimFoxId = string.Empty;
    [ProtoMember(6)] public bool ClaimStarted;
    [ProtoMember(7)] public string TameEntityCode = string.Empty;
}
