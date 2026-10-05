#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProtoBuf;
using Vintagestory.API.Common;

namespace FeralKinshipCompanions;

public sealed partial class FoxPackRecordV2
{
    [ProtoMember(141)]
    public FoxMissingContentArchive? MissingContentArchive;
}

/// <summary>Recovery evidence; never a deletion tombstone or an entity spawn request.</summary>
[ProtoContract]
public sealed class FoxMissingContentArchive
{
    [ProtoMember(1)] public bool Active;
    [ProtoMember(2)] public List<string> MissingModIds = new();
    [ProtoMember(3)] public long ArchivedAtUtcMs;
    [ProtoMember(4)] public string PreviousStatus = string.Empty;
    [ProtoMember(5)] public string PreviousRecoveryStatus = string.Empty;
    [ProtoMember(6)] public byte[] OriginalRecordBytes = Array.Empty<byte>();
}

internal static class CompanionContentIdentity
{
    // These are source domains supported by the two adapters' code builders.
    // Unknown/future formats deliberately do not guess a provider.
    private static readonly string[] FotsaDomains =
    {
        "caninae", "felinae", "vombatidae", "viverridae", "manidae", "spheniscidae",
        "meiolaniidae", "pantherinae", "machairodontinae", "thylacinidae", "sirenia",
        "iniidae", "elephantidae", "dinornithidae", "casuariidae", "cervinae",
        "rhinocerotidae", "bovinae", "capreolinae", "chelonioidea"
    };
    private static readonly string[] CrittersDomains =
    {
        "africanmonitorlizards", "asianmonitorlizards", "bandedgeckos", "beardeddragons",
        "ensatinas", "knobtailedgeckos", "leopardgeckos", "newworldgianttortoises",
        "newzealandfrogs", "pacificnewts", "pondfrogsi", "pondfrogsiii", "rainfrogs",
        "thecritterpack", "moreanimals"
    };

    internal static AssetLocation? ExactCode(string entityCode)
    {
        // Legacy blank/bare or malformed codes are insufficient archival evidence.
        if (string.IsNullOrWhiteSpace(entityCode) || entityCode.Count(c => c == ':') != 1)
            return null;
        int colon = entityCode.IndexOf(':');
        if (colon == 0 || colon == entityCode.Length - 1 || entityCode.Any(char.IsWhiteSpace))
            return null;
        return new AssetLocation(entityCode);
    }

    internal static IReadOnlyList<string> RequiredMods(AssetLocation? code)
    {
        if (code == null || !CompanionSpeciesCatalog.TryGetByTameEntityCode(code, out _))
            return Array.Empty<string>();
        if (code.Domain is "cats" or "wolftaming" or "foxtaming" or "feralkinship")
            return new[] { code.Domain };
        string[]? sourceDomains = code.Domain switch
        {
            "tamablesfotsa" => FotsaDomains,
            "tamablescritters" => CrittersDomains,
            _ => null
        };
        if (sourceDomains == null) return Array.Empty<string>();
        string? source = sourceDomains.FirstOrDefault(domain =>
            code.Path.StartsWith("tame-" + domain + "-", StringComparison.Ordinal));
        return source == null ? Array.Empty<string>() : new[] { code.Domain, source };
    }

    internal static bool RequiresExactRecovery(string speciesId, string entityCode) =>
        speciesId is "tamables-fotsa" or "tamables-critters"
        || ExactCode(entityCode)?.Domain is "tamablesfotsa" or "tamablescritters";
}

internal sealed partial class FoxPackRepository
{
    internal bool ArchiveMissingContent(FoxPackRecordV2 record, IReadOnlyList<string> missingMods, long nowUtcMs)
    {
        if (!Loaded || record.Archived || missingMods.Count == 0
            || !recordsById.TryGetValue(record.FoxId, out FoxPackRecordV2? stored)
            || !ReferenceEquals(stored, record)) return false;

        byte[] original = record.MissingContentArchive?.OriginalRecordBytes ?? Array.Empty<byte>();
        if (original.Length == 0)
        {
            using MemoryStream stream = new();
            Serializer.Serialize(stream, record);
            original = stream.ToArray();
        }
        record.MissingContentArchive = new FoxMissingContentArchive
        {
            Active = true,
            MissingModIds = missingMods.ToList(),
            ArchivedAtUtcMs = nowUtcMs,
            PreviousStatus = record.Status,
            PreviousRecoveryStatus = record.RecoveryStatus,
            OriginalRecordBytes = original
        };
        record.Archived = true;
        record.Status = "Archived - missing mod: " + string.Join(", ", missingMods);
        record.RecoveryStatus = CompanionRecoveryStatus.Unknown;
        return true;
    }

    internal bool RestoreMissingContent(FoxPackRecordV2 record)
    {
        if (!Loaded || !record.Archived || record.MissingContentArchive?.Active != true)
            return false;
        FoxMissingContentArchive archive = record.MissingContentArchive;
        record.Archived = false;
        record.Status = archive.PreviousStatus;
        record.RecoveryStatus = archive.PreviousRecoveryStatus;
        archive.Active = false;
        // Keep original serialized evidence and timestamps after restoration.
        return true;
    }
}

public sealed partial class FeralKinshipCompanionSystem
{
    private bool companionContentReady;

    private void OnCompanionContentReady()
    {
        using IDisposable? saveBatch = packRepository?.BatchSaves();
        companionContentReady = true;
        RefreshUnloadedRecoveryStates();
        packRepository?.Save();
    }

    private void ReconcileMissingCompanionContent(FoxPackRecordV2 record)
    {
        if (!companionContentReady || serverApi == null || packRepository?.Loaded != true) return;
        AssetLocation? code = CompanionContentIdentity.ExactCode(record.EntityCode);
        bool exactAvailable = code != null && serverApi.World.GetEntityType(code) != null;
        if (record.MissingContentArchive?.Active == true)
        {
            string latestStatus = record.Status;
            if (exactAvailable && packRepository.RestoreMissingContent(record))
            {
                // A restored original may have registered before RunGame.
                // Keep its live status instead of the old archived status.
                if (FindLoadedCompanionForRecovery(record.FoxId) != null
                    && !latestStatus.StartsWith("Archived", StringComparison.Ordinal))
                    record.Status = latestStatus;
                serverApi.Logger.Notification(
                    "[FeralKinshipCompanions] Restored companion {0} to the ledger after exact content {1} returned; no entity was spawned.",
                    record.FoxId, record.EntityCode);
            }
            return;
        }
        if (record.Archived || exactAvailable || string.IsNullOrWhiteSpace(record.OwnerUid)
            || string.Equals(record.Status, "Dead", StringComparison.OrdinalIgnoreCase)
            || string.Equals(record.Status, "Unowned", StringComparison.OrdinalIgnoreCase)
            || FindLoadedCompanionForRecovery(record.FoxId) != null) return;
        string[] missingMods = CompanionContentIdentity.RequiredMods(code)
            .Where(modId => !serverApi.ModLoader.IsModEnabled(modId)).ToArray();
        if (packRepository.ArchiveMissingContent(record, missingMods, UtcNowMs()))
            serverApi.Logger.Notification(
                "[FeralKinshipCompanions] Archived companion {0} ({1}): missing mod(s) {2}. Identity, original record and recovery state retained.",
                record.FoxId, record.EntityCode, string.Join(", ", missingMods));
    }

    private bool ExpeditionHasMissingContent(FoxExpeditionRecord expedition) =>
        packRepository != null && expedition.SelectedFoxIds.Append(expedition.TargetFoxId).Any(id =>
            packRepository.TryGetRecord(id, out FoxPackRecordV2? record)
            && record?.MissingContentArchive?.Active == true);
}
