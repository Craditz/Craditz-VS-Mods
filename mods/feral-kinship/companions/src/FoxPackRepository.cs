#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;

/// <summary>
/// Owns persistent fox roster and shared pack-point state. Entity attributes
/// may be read once for migration, but this repository is the only authority
/// after version 2 data has been loaded.
/// </summary>
internal sealed partial class FoxPackRepository
{
    private const string SaveKey = "feralkinshipcompanions:fox-pack-v2";
    private const string LegacyRecordsSaveKey = "feralkinshipcompanions:fox-pack-records";
    private const string LegacyPointsSaveKey = "feralkinshipcompanions:fox-pack-points";
    private const string LegacyNextNumberSaveKey = "feralkinshipcompanions:next-fox-number";
    internal const int BaseExpeditionCapacity = 2;
    private const int CurrentVersion = 33;
    private const int MaximumExpeditionReportHistory = 50;

    private readonly ICoreServerAPI api;
    private readonly Dictionary<string, FoxPackRecordV2> recordsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> pointsByOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<byte[]>> lootByOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<long, FoxExpeditionRecord> expeditionsById = new();
    private readonly Dictionary<long, FoxScavengeSiteRecord> scavengeSitesById = new();
    private readonly Dictionary<string, List<FoxRecruitmentRewardRecord>> recruitmentRewardsByOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FoxPackCairnRecord> cairnsByPosition = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FoxBedRecord> bedsByPosition = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FoxWorkCartRecord> workCartsByPosition = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FoxPackAmenityRecord> amenitiesByPosition = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FoxPackProgressRecord> progressByOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<FoxExpeditionSummaryPacket>> reportsByOwner = new(StringComparer.Ordinal);
    private readonly HashSet<string> permanentlyDeletedFoxIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> ownersWithAuthoritativePoints = new(StringComparer.Ordinal);
    private int nextFoxNumber = 1;
    private long nextCairnSequence = 1;
    private long nextExpeditionSequence = 1;
    private long nextScavengeSiteSequence = 1;
    private int saveBatchDepth;
    private bool savePending;

    public FoxPackRepository(ICoreServerAPI api)
    {
        this.api = api;
    }

    public bool Loaded { get; private set; }
    public bool MigratedLegacyData { get; private set; }
    public bool MigratedExpeditionData { get; private set; }
    public bool MigratedRetiredRoutes { get; private set; }

    public void Load()
    {
        Loaded = false;
        recordsById.Clear();
        pointsByOwner.Clear();
        lootByOwner.Clear();
        expeditionsById.Clear();
        scavengeSitesById.Clear();
        recruitmentRewardsByOwner.Clear();
        cairnsByPosition.Clear();
        bedsByPosition.Clear();
        workCartsByPosition.Clear();
        amenitiesByPosition.Clear();
        progressByOwner.Clear();
        reportsByOwner.Clear();
        permanentlyDeletedFoxIds.Clear();
        ownersWithAuthoritativePoints.Clear();
        nextFoxNumber = 1;
        nextCairnSequence = 1;
        nextExpeditionSequence = 1;
        nextScavengeSiteSequence = 1;
        MigratedLegacyData = false;
        MigratedExpeditionData = false;
        MigratedRetiredRoutes = false;

        FoxPackSaveData? saved = api.WorldManager.SaveGame.GetData<FoxPackSaveData?>(SaveKey, null);
        if (saved?.Version > CurrentVersion)
        {
            api.Logger.Error(
                "[FeralKinshipCompanions] Companion pack save version {0} is newer than supported version {1}. "
                + "The pack ledger will remain read-only so newer data is not overwritten.",
                saved.Version,
                CurrentVersion
            );
            return;
        }

        if (saved?.Version >= 2)
        {
            LoadVersion2(saved);
        }
        else
        {
            MigratedLegacyData = LoadLegacyData();
        }

        int highestKnownNumber = recordsById.Values.Select(record => record.Number).DefaultIfEmpty(0).Max();
        nextFoxNumber = Math.Max(nextFoxNumber, highestKnownNumber + 1);
        Loaded = true;
    }

    public void Save()
    {
        if (!Loaded)
        {
            return;
        }
        if (saveBatchDepth > 0)
        {
            savePending = true;
            return;
        }

        api.WorldManager.SaveGame.StoreData(
            SaveKey,
            new FoxPackSaveData
            {
                Version = CurrentVersion,
                NextFoxNumber = nextFoxNumber,
                Records = recordsById.Values
                    .OrderBy(record => record.OwnerUid)
                    .ThenBy(record => record.Number)
                    .ToList(),
                PackPoints = pointsByOwner
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new FoxPackPointsRecord
                    {
                        OwnerUid = pair.Key,
                        Points = pair.Value
                    })
                    .ToList(),
                Loot = lootByOwner
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new FoxPackLootRecord
                    {
                        OwnerUid = pair.Key,
                        Items = pair.Value.ToList()
                    })
                    .ToList(),
                Expeditions = expeditionsById.Values
                    .OrderBy(record => record.OwnerUid)
                    .ThenBy(record => record.StartedTotalHours)
                    .ThenBy(record => record.ExpeditionId)
                    .ToList(),
                ScavengeSites = scavengeSitesById.Values
                    .OrderBy(site => site.OwnerUid)
                    .ThenBy(site => site.SiteId)
                    .ToList(),
                NextScavengeSiteSequence = nextScavengeSiteSequence,
                RecruitmentRewards = recruitmentRewardsByOwner
                    .OrderBy(pair => pair.Key)
                    .SelectMany(pair => pair.Value)
                    .ToList(),
                Cairns = cairnsByPosition.Values
                    .OrderBy(record => record.OwnerUid)
                    .ThenBy(record => record.PlacedSequence)
                    .ToList(),
                Beds = bedsByPosition.Values
                    .OrderBy(record => record.OwnerUid)
                    .ThenBy(record => record.Dimension)
                    .ThenBy(record => record.X)
                    .ThenBy(record => record.Y)
                    .ThenBy(record => record.Z)
                    .ToList(),
                WorkCarts = workCartsByPosition.Values
                    .OrderBy(record => record.OwnerUid)
                    .ThenBy(record => record.Dimension)
                    .ThenBy(record => record.X)
                    .ThenBy(record => record.Y)
                    .ThenBy(record => record.Z)
                    .ToList(),
                Amenities = amenitiesByPosition.Values
                    .OrderBy(record => record.OwnerUid)
                    .ThenBy(record => record.Kind)
                    .ToList(),
                NextCairnSequence = nextCairnSequence,
                PackProgress = progressByOwner.Values
                    .OrderBy(record => record.OwnerUid)
                    .ToList(),
                LastExpeditionReports = reportsByOwner
                    .OrderBy(pair => pair.Key)
                    .Select(pair => pair.Value
                        .OrderByDescending(report => report.CompletedTotalHours)
                        .ThenByDescending(report => report.ExpeditionId)
                        .First())
                    .ToList(),
                NextExpeditionSequence = nextExpeditionSequence,
                ExpeditionReportHistory = reportsByOwner
                    .OrderBy(pair => pair.Key)
                    .SelectMany(pair => pair.Value
                        .OrderBy(report => report.CompletedTotalHours)
                        .ThenBy(report => report.ExpeditionId))
                    .ToList(),
                PermanentlyDeletedFoxIds = permanentlyDeletedFoxIds
                    .OrderBy(foxId => foxId, StringComparer.Ordinal)
                    .ToList()
            }
        );
        savePending = false;
    }

    // Only use around synchronous ledger-only operations. Inventory transfers,
    // entity creation/removal and world-save callbacks retain immediate Save().
    internal IDisposable BatchSaves()
    {
        saveBatchDepth++;
        return new SaveBatch(this);
    }

    private sealed class SaveBatch : IDisposable
    {
        private FoxPackRepository? repository;
        public SaveBatch(FoxPackRepository repository) => this.repository = repository;
        public void Dispose()
        {
            FoxPackRepository? owner = repository;
            if (owner == null) return;
            repository = null;
            if (--owner.saveBatchDepth == 0 && owner.savePending) owner.Save();
        }
    }

    public FoxPackCairnRecord RegisterCairn(string ownerUid, BlockPos pos)
    {
        string key = PositionKey(pos.X, pos.Y, pos.Z, pos.dimension);
        FoxPackCairnRecord record = new()
        {
            OwnerUid = ownerUid,
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            Dimension = pos.dimension,
            PlacedSequence = nextCairnSequence++
        };
        cairnsByPosition[key] = record;
        return record;
    }

    public bool RemoveCairn(BlockPos pos)
    {
        return cairnsByPosition.Remove(PositionKey(pos.X, pos.Y, pos.Z, pos.dimension));
    }

    public FoxPackCairnRecord? GetCairn(BlockPos pos)
    {
        return cairnsByPosition.TryGetValue(PositionKey(pos.X, pos.Y, pos.Z, pos.dimension), out FoxPackCairnRecord? cairn)
            ? cairn
            : null;
    }

    public IReadOnlyList<FoxPackCairnRecord> GetCairnsForOwner(string ownerUid)
    {
        return cairnsByPosition.Values
            .Where(record => string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal))
            .OrderByDescending(record => record.PlacedSequence)
            .ToList();
    }

    public FoxBedRecord RegisterBed(string ownerUid, BlockPos pos, string blockCode)
    {
        string key = PositionKey(pos.X, pos.Y, pos.Z, pos.dimension);
        if (bedsByPosition.TryGetValue(key, out FoxBedRecord? previous)
            && !string.IsNullOrWhiteSpace(previous.FoxId))
        {
            ClearFoxHome(previous.FoxId);
        }

        FoxBedRecord record = new()
        {
            OwnerUid = ownerUid,
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            Dimension = pos.dimension,
            BlockCode = blockCode ?? string.Empty
        };
        bedsByPosition[key] = record;
        return record;
    }

    public bool RemoveBed(BlockPos pos)
    {
        string key = PositionKey(pos.X, pos.Y, pos.Z, pos.dimension);
        if (!bedsByPosition.Remove(key, out FoxBedRecord? bed))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(bed.FoxId))
        {
            ClearFoxHome(bed.FoxId);
        }
        return true;
    }

    public FoxBedRecord? GetBed(BlockPos pos)
    {
        return bedsByPosition.TryGetValue(PositionKey(pos.X, pos.Y, pos.Z, pos.dimension), out FoxBedRecord? bed)
            ? bed
            : null;
    }

    public FoxBedRecord? GetBedForFox(string foxId)
    {
        if (string.IsNullOrWhiteSpace(foxId))
        {
            return null;
        }

        return bedsByPosition.Values.FirstOrDefault(bed =>
            string.Equals(bed.FoxId, foxId, StringComparison.Ordinal));
    }

    public IReadOnlyList<FoxBedRecord> GetBedsForOwner(string ownerUid) => bedsByPosition.Values
        .Where(record => string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal))
        .ToList();

    public void ClearBrambleBedAssignment(string ownerUid)
    {
        foreach (FoxBedRecord bed in bedsByPosition.Values.Where(candidate =>
                     string.Equals(candidate.OwnerUid, ownerUid, StringComparison.Ordinal)
                     && string.Equals(candidate.BrambleOwnerUid, ownerUid, StringComparison.Ordinal)))
        {
            bed.BrambleOwnerUid = string.Empty;
        }
    }

    public bool TryAssignBrambleBed(string ownerUid, BlockPos pos, out string refusal)
    {
        refusal = string.Empty;
        if (!bedsByPosition.TryGetValue(PositionKey(pos.X, pos.Y, pos.Z, pos.dimension), out FoxBedRecord? bed)
            || !string.Equals(bed.OwnerUid, ownerUid, StringComparison.Ordinal))
        {
            refusal = "Only the player who placed this companion bed can assign it.";
            return false;
        }

        ClearBrambleBedAssignment(ownerUid);
        if (!string.IsNullOrWhiteSpace(bed.FoxId))
        {
            ClearFoxHome(bed.FoxId);
            bed.FoxId = string.Empty;
        }
        bed.BrambleOwnerUid = ownerUid;
        return true;
    }

    public FoxWorkCartRecord RegisterWorkCart(string ownerUid, BlockPos pos, string kind = "generic")
    {
        string key = PositionKey(pos.X, pos.Y, pos.Z, pos.dimension);
        if (workCartsByPosition.TryGetValue(key, out FoxWorkCartRecord? existing)
            && string.Equals(existing.OwnerUid, ownerUid, StringComparison.Ordinal))
        {
            if (string.Equals(existing.Kind, "generic", StringComparison.Ordinal)
                && !string.Equals(kind, "generic", StringComparison.Ordinal))
            {
                existing.Kind = kind;
                if (string.Equals(kind, "logging", StringComparison.Ordinal)) existing.LoggingEnabled = true;
            }
            return existing;
        }

        if (workCartsByPosition.TryGetValue(key, out FoxWorkCartRecord? previous))
        {
            foreach (string foxId in previous.AssignedFoxIds.ToList())
            {
                ClearFoxHome(foxId);
            }
        }

        FoxWorkCartRecord record = new()
        {
            OwnerUid = ownerUid,
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            Dimension = pos.dimension,
            Kind = kind,
            LoggingEnabled = string.Equals(kind, "logging", StringComparison.Ordinal)
        };
        workCartsByPosition[key] = record;
        return record;
    }

    public bool RemoveWorkCart(BlockPos pos)
    {
        if (!workCartsByPosition.Remove(PositionKey(pos.X, pos.Y, pos.Z, pos.dimension), out FoxWorkCartRecord? cart))
        {
            return false;
        }

        foreach (string foxId in cart.AssignedFoxIds.ToList())
        {
            ClearFoxHome(foxId);
        }
        return true;
    }

    public FoxWorkCartRecord? GetWorkCart(BlockPos pos) =>
        workCartsByPosition.TryGetValue(PositionKey(pos.X, pos.Y, pos.Z, pos.dimension), out FoxWorkCartRecord? cart)
            ? cart
            : null;

    public IReadOnlyList<FoxWorkCartRecord> GetWorkCartsForOwner(string ownerUid) => workCartsByPosition.Values
        .Where(record => string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal))
        .OrderBy(record => record.Dimension)
        .ThenBy(record => record.X)
        .ThenBy(record => record.Y)
        .ThenBy(record => record.Z)
        .ToList();

    public int WorkCartCount => workCartsByPosition.Count;

    public IEnumerable<FoxWorkCartRecord> GetAllWorkCarts() => workCartsByPosition.Values;

    public FoxWorkCartRecord? GetWorkCartForFox(string foxId) =>
        workCartsByPosition.Values.FirstOrDefault(cart => cart.AssignedFoxIds.Contains(foxId, StringComparer.Ordinal));

    public bool TryAssignWorkCart(string ownerUid, BlockPos pos, string foxId, bool assign, out string refusal)
    {
        refusal = string.Empty;
        if (!workCartsByPosition.TryGetValue(PositionKey(pos.X, pos.Y, pos.Z, pos.dimension), out FoxWorkCartRecord? cart)
            || !string.Equals(cart.OwnerUid, ownerUid, StringComparison.Ordinal))
        {
            refusal = "Only the player who placed this Work Cart can assign it.";
            return false;
        }

        if (!assign)
        {
            if (!string.IsNullOrWhiteSpace(foxId))
            {
                cart.AssignedFoxIds.RemoveAll(id => string.Equals(id, foxId, StringComparison.Ordinal));
                if (GetWorkCartForFox(foxId) == null)
                {
                    ClearFoxHome(foxId);
                }
            }
            return true;
        }

        if (!recordsById.TryGetValue(foxId, out FoxPackRecordV2? fox)
            || fox.Archived
            || !string.Equals(fox.OwnerUid, ownerUid, StringComparison.Ordinal))
        {
            refusal = "That companion is not an active member of your pack.";
            return false;
        }

        foreach (FoxBedRecord bed in bedsByPosition.Values.Where(bed => string.Equals(bed.FoxId, foxId, StringComparison.Ordinal)))
        {
            bed.FoxId = string.Empty;
        }
        foreach (FoxWorkCartRecord other in workCartsByPosition.Values)
        {
            other.AssignedFoxIds.RemoveAll(id => string.Equals(id, foxId, StringComparison.Ordinal));
        }

        cart.AssignedFoxIds.Add(foxId);
        fox.HasHome = true;
        fox.HomeType = "workcart";
        fox.HomeX = cart.X;
        fox.HomeY = cart.Y;
        fox.HomeZ = cart.Z;
        fox.HomeDimension = cart.Dimension;
        return true;
    }

    public FoxPackAmenityRecord RegisterAmenity(string ownerUid, BlockPos pos, string kind)
    {
        FoxPackAmenityRecord record = new()
        {
            OwnerUid = ownerUid,
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            Dimension = pos.dimension,
            Kind = kind ?? string.Empty,
            StorageRoutingEnabled = string.Equals(kind, "storage", StringComparison.Ordinal),
            StorageRoutingMask = string.Equals(kind, "storage", StringComparison.Ordinal)
                ? (int)FoxStorageRouting.Category.General
                : 0
        };
        amenitiesByPosition[PositionKey(pos.X, pos.Y, pos.Z, pos.dimension)] = record;
        return record;
    }

    public FoxPackAmenityRecord RegisterStorageTarget(string ownerUid, BlockPos pos, int categoryMask)
    {
        string key = PositionKey(pos.X, pos.Y, pos.Z, pos.dimension);
        if (amenitiesByPosition.TryGetValue(key, out FoxPackAmenityRecord? existing))
        {
            // Preserve amenity identity for Dining Boards. They remain usable
            // as communal pack furniture while also acting as configured
            // routing destinations.
            existing.Kind = existing.Kind is "storage" or "dining" ? existing.Kind : "storage-target";
            existing.StorageRoutingEnabled = true;
            existing.StorageRoutingMask = categoryMask;
            existing.StorageAdvancedMode = false;
            return existing;
        }

        FoxPackAmenityRecord record = new()
        {
            OwnerUid = ownerUid,
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            Dimension = pos.dimension,
            Kind = "storage-target",
            StorageRoutingEnabled = true,
            StorageRoutingMask = categoryMask,
            StorageAdvancedMode = false
        };
        amenitiesByPosition[key] = record;
        return record;
    }

    public bool RemoveStorageTarget(string ownerUid, BlockPos pos, out string refusal)
    {
        refusal = string.Empty;
        string key = PositionKey(pos.X, pos.Y, pos.Z, pos.dimension);
        if (!amenitiesByPosition.TryGetValue(key, out FoxPackAmenityRecord? record)
            || !string.Equals(record.Kind, "storage-target", StringComparison.Ordinal))
        {
            refusal = "That container is not a configured pack destination.";
            return false;
        }

        if (!string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal))
        {
            refusal = "Only the player who configured this destination can change it.";
            return false;
        }

        amenitiesByPosition.Remove(key);
        return true;
    }

    public bool RemoveAmenity(BlockPos pos) => amenitiesByPosition.Remove(PositionKey(pos.X, pos.Y, pos.Z, pos.dimension));

    public FoxPackAmenityRecord? GetAmenity(BlockPos pos) =>
        amenitiesByPosition.TryGetValue(PositionKey(pos.X, pos.Y, pos.Z, pos.dimension), out FoxPackAmenityRecord? record) ? record : null;

    public IReadOnlyList<FoxPackAmenityRecord> GetAmenitiesForOwner(string ownerUid) => amenitiesByPosition.Values
        .Where(record => string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal))
        .ToList();

    public bool TryAssignBed(string ownerUid, BlockPos pos, string foxId, out string refusal)
    {
        refusal = string.Empty;
        if (!bedsByPosition.TryGetValue(PositionKey(pos.X, pos.Y, pos.Z, pos.dimension), out FoxBedRecord? bed)
            || !string.Equals(bed.OwnerUid, ownerUid, StringComparison.Ordinal))
        {
            refusal = "Only the player who placed this companion bed can assign it.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(foxId))
        {
            if (!string.IsNullOrWhiteSpace(bed.FoxId))
            {
                ClearFoxHome(bed.FoxId);
                bed.FoxId = string.Empty;
            }
            bed.BrambleOwnerUid = string.Empty;
            return true;
        }

        if (!recordsById.TryGetValue(foxId, out FoxPackRecordV2? fox)
            || fox.Archived
            || !string.Equals(fox.OwnerUid, ownerUid, StringComparison.Ordinal))
        {
            refusal = "That companion is not an active member of your pack.";
            return false;
        }

        foreach (FoxBedRecord other in bedsByPosition.Values.Where(candidate =>
                     !ReferenceEquals(candidate, bed)
                     && string.Equals(candidate.FoxId, foxId, StringComparison.Ordinal)))
        {
            other.FoxId = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(bed.FoxId)
            && !string.Equals(bed.FoxId, foxId, StringComparison.Ordinal))
        {
            ClearFoxHome(bed.FoxId);
        }

        bed.FoxId = foxId;
        bed.BrambleOwnerUid = string.Empty;
        foreach (FoxWorkCartRecord cart in workCartsByPosition.Values)
        {
            cart.AssignedFoxIds.RemoveAll(id => string.Equals(id, foxId, StringComparison.Ordinal));
        }
        fox.HasHome = true;
        fox.HomeType = "bed";
        fox.HomeX = bed.X;
        fox.HomeY = bed.Y;
        fox.HomeZ = bed.Z;
        fox.HomeDimension = bed.Dimension;
        return true;
    }

    public string ResolveOrCreateFoxId(string existingId, string ownerUid, int number, string speciesId = "fox")
    {
        if (!string.IsNullOrWhiteSpace(existingId))
        {
            if (!recordsById.TryGetValue(existingId, out FoxPackRecordV2? existing)
                || (string.Equals(existing.OwnerUid, ownerUid, StringComparison.Ordinal)
                    && existing.Number == number
                    && (string.IsNullOrWhiteSpace(existing.SpeciesId)
                        || string.Equals(existing.SpeciesId, speciesId, StringComparison.Ordinal))))
            {
                return existingId;
            }

            string replacement = Guid.NewGuid().ToString("N");
            api.Logger.Error(
                "[FeralKinshipCompanions] Replaced duplicate companion identity {0} on owner {1}, companion #{2}, "
                + "because that identity already belongs to owner {3}, companion #{4}.",
                existingId,
                ownerUid,
                number,
                existing.OwnerUid,
                existing.Number
            );
            return replacement;
        }

        string legacyId = GetLegacyFoxId(ownerUid, number);
        if (!recordsById.TryGetValue(legacyId, out FoxPackRecordV2? legacyRecord)
            || string.IsNullOrWhiteSpace(legacyRecord.SpeciesId)
            || string.Equals(legacyRecord.SpeciesId, speciesId, StringComparison.Ordinal))
        {
            return recordsById.ContainsKey(legacyId)
                ? legacyId
                : Guid.NewGuid().ToString("N");
        }
        return Guid.NewGuid().ToString("N");
    }

    public FoxPackRecordV2 GetOrCreateRecord(string foxId, string ownerUid, int number, string speciesId = "fox")
    {
        if (!recordsById.TryGetValue(foxId, out FoxPackRecordV2? record))
        {
            record = new FoxPackRecordV2
            {
                FoxId = foxId,
                OwnerUid = ownerUid,
                Number = number,
                SpeciesId = string.IsNullOrWhiteSpace(speciesId) ? "fox" : speciesId,
                ActivityMode = CompanionActivityMode.AtEase,
                FollowDistance = CompanionFollowDistance.Normal,
                CombatStyle = CompanionCombatStyle.Defensive,
                RiskTolerance = CompanionRiskTolerance.Steady
            };
            recordsById[foxId] = record;
        }

        record.FoxId = foxId;
        record.OwnerUid = ownerUid;
        record.Number = number;
        if (string.IsNullOrWhiteSpace(record.SpeciesId))
        {
            record.SpeciesId = string.IsNullOrWhiteSpace(speciesId) ? "fox" : speciesId;
        }
        ObserveNumber(number);
        return record;
    }

    public bool TryGetRecord(string foxId, out FoxPackRecordV2? record)
    {
        return recordsById.TryGetValue(foxId, out record);
    }

    public bool RemoveRecord(string ownerUid, string foxId)
    {
        return recordsById.TryGetValue(foxId, out FoxPackRecordV2? record)
            && string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal)
            && recordsById.Remove(foxId);
    }

    public bool IsPermanentlyDeleted(string foxId)
    {
        return !string.IsNullOrWhiteSpace(foxId)
            && permanentlyDeletedFoxIds.Contains(foxId);
    }

    public int PermanentlyDeleteCompanionsForOwner(string ownerUid)
    {
        if (string.IsNullOrWhiteSpace(ownerUid))
        {
            return 0;
        }

        List<FoxPackRecordV2> records = recordsById.Values
            .Where(record => string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal))
            .ToList();
        HashSet<string> removedIds = records
            .Select(record => record.FoxId)
            .Where(foxId => !string.IsNullOrWhiteSpace(foxId))
            .ToHashSet(StringComparer.Ordinal);

        permanentlyDeletedFoxIds.UnionWith(removedIds);
        foreach (string foxId in removedIds)
        {
            recordsById.Remove(foxId);
        }

        foreach (long expeditionId in expeditionsById
                     .Where(pair => string.Equals(pair.Value.OwnerUid, ownerUid, StringComparison.Ordinal))
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            expeditionsById.Remove(expeditionId);
        }

        reportsByOwner.Remove(ownerUid);
        foreach (FoxBedRecord bed in bedsByPosition.Values)
        {
            if (removedIds.Contains(bed.FoxId))
            {
                bed.FoxId = string.Empty;
            }
        }
        foreach (FoxWorkCartRecord cart in workCartsByPosition.Values)
        {
            cart.AssignedFoxIds.RemoveAll(removedIds.Contains);
        }

        // Relationship labels are meaningful only with their stable companion
        // identity. Do not let any surviving record point back to a companion
        // that was permanently deleted.
        foreach (FoxPackRecordV2 survivor in recordsById.Values)
        {
            if (removedIds.Contains(survivor.BondedPartnerId))
            {
                survivor.BondedPartnerId = string.Empty;
                survivor.BondedPartnerName = string.Empty;
            }
            if (removedIds.Contains(survivor.PregnancyFatherId))
            {
                survivor.PregnancyFatherId = string.Empty;
                survivor.PregnancyFatherName = string.Empty;
            }
            if (removedIds.Contains(survivor.ParentMotherId))
            {
                survivor.ParentMotherId = string.Empty;
                survivor.ParentMotherName = string.Empty;
            }
            if (removedIds.Contains(survivor.ParentFatherId))
            {
                survivor.ParentFatherId = string.Empty;
                survivor.ParentFatherName = string.Empty;
            }
        }

        return records.Count;
    }

    public void RestoreRecord(FoxPackRecordV2 record)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.FoxId))
        {
            return;
        }

        recordsById[record.FoxId] = record;
        ObserveNumber(record.Number);
    }

    public IReadOnlyList<FoxPackRecordV2> GetRecordsForOwner(string ownerUid)
    {
        return recordsById.Values
            .Where(record => string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal)
                && !record.Archived)
            .OrderBy(record => record.Number)
            .ToList();
    }

    public IReadOnlyList<FoxPackRecordV2> GetAllRecords()
    {
        return recordsById.Values.ToList();
    }

    public IReadOnlyList<FoxPackRecordV2> GetArchivedRecordsForOwner(string ownerUid)
    {
        return recordsById.Values
            .Where(record => string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal)
                && record.Archived)
            .OrderBy(record => record.Number)
            .ToList();
    }

    public IReadOnlyList<FoxPackRecordV2> GetPendingReturnRecords()
    {
        return recordsById.Values
            .Where(record => record.MissingContentArchive?.Active != true
                && !string.IsNullOrWhiteSpace(record.PendingReturnStatus))
            .OrderBy(record => record.PendingReturnAtUtcMs)
            .ToList();
    }

    public IReadOnlyList<FoxPackRecordV2> GetPendingDepartureRecords()
    {
        return recordsById.Values
            .Where(record => record.MissingContentArchive?.Active != true
                && !string.IsNullOrWhiteSpace(record.PendingDepartureStatus))
            .OrderBy(record => record.PendingDepartureAtUtcMs)
            .ToList();
    }

    public bool ArchiveRecord(string ownerUid, string foxId)
    {
        if (!recordsById.TryGetValue(foxId, out FoxPackRecordV2? record)
            || !string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal)
            || record.Archived
            || !IsArchivableStatus(record.Status))
        {
            return false;
        }

        record.Archived = true;
        record.Status = "Archived";
        return true;
    }

    private static bool IsArchivableStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status)
            || string.Equals(status, "Present", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Available", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Returned healthy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Returned injured", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Unowned", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.Equals(status, "Running late", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(status, "Awaiting safe return", StringComparison.OrdinalIgnoreCase)
            && !status.StartsWith("Away —", StringComparison.OrdinalIgnoreCase)
            && !status.StartsWith("Leaving —", StringComparison.OrdinalIgnoreCase);
    }

    public bool UnarchiveRecord(string ownerUid, string foxId)
    {
        if (!recordsById.TryGetValue(foxId, out FoxPackRecordV2? record)
            || !string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal)
            || !record.Archived
            || record.MissingContentArchive?.Active == true)
        {
            return false;
        }

        record.Archived = false;
        record.Status = "Present";
        return true;
    }

    public void MarkAllUnloaded()
    {
        foreach (FoxPackRecordV2 record in recordsById.Values)
        {
            if (!record.Archived
                && !string.Equals(record.Status, "Dead", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(record.Status, "Unowned", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(record.Status, "MIA", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(record.Status, "Mortally wounded", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(record.Status, "Running late", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(record.Status, "Awaiting safe return", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(record.Status, "Unrecoverable", StringComparison.OrdinalIgnoreCase)
                && !record.Status.StartsWith("Away —", StringComparison.OrdinalIgnoreCase)
                && !record.Status.StartsWith("Leaving —", StringComparison.OrdinalIgnoreCase))
            {
                record.Status = "Not currently loaded";
            }
        }
    }

    public void MarkStatus(string foxId, string status, double lastSeenDay)
    {
        if (!recordsById.TryGetValue(foxId, out FoxPackRecordV2? record))
        {
            return;
        }

        if (string.Equals(status, "Not currently loaded", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(record.Status, "MIA", StringComparison.OrdinalIgnoreCase)
                || string.Equals(record.Status, "Mortally wounded", StringComparison.OrdinalIgnoreCase)
                || string.Equals(record.Status, "Running late", StringComparison.OrdinalIgnoreCase)
                || string.Equals(record.Status, "Awaiting safe return", StringComparison.OrdinalIgnoreCase)
                || record.Status.StartsWith("Away —", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        record.Status = status;
        record.LastSeenDay = lastSeenDay;
        if (string.Equals(status, "Unowned", StringComparison.OrdinalIgnoreCase))
        {
            record.OwnerUid = string.Empty;
        }
    }

    public int AllocateNumber()
    {
        return nextFoxNumber++;
    }

    public void ObserveNumber(int number)
    {
        if (number >= nextFoxNumber)
        {
            nextFoxNumber = number + 1;
        }
    }

    public int GetPackPoints(string ownerUid)
    {
        if (pointsByOwner.TryGetValue(ownerUid, out int points))
        {
            return points;
        }

        int migratedFallback = recordsById.Values
            .Where(record => string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal)
                && !record.Archived)
            .Sum(record => Math.Max(0, record.Points));
        SetPackPoints(ownerUid, migratedFallback);
        return migratedFallback;
    }

    public bool HasLoot(string ownerUid)
    {
        return lootByOwner.TryGetValue(ownerUid, out List<byte[]>? items)
            && items.Count > 0;
    }

    public bool HasPendingRecruitment(string ownerUid)
    {
        return GetPendingRecruitmentCount(ownerUid) > 0;
    }

    public int GetPendingRecruitmentCount(string ownerUid)
    {
        return recruitmentRewardsByOwner.TryGetValue(ownerUid, out List<FoxRecruitmentRewardRecord>? rewards)
            ? rewards.Count
            : 0;
    }

    public FoxRecruitmentRewardRecord? GetPendingRecruitment(string ownerUid)
    {
        return recruitmentRewardsByOwner.TryGetValue(ownerUid, out List<FoxRecruitmentRewardRecord>? rewards)
            ? rewards.FirstOrDefault()
            : null;
    }

    public void SetPendingRecruitment(string ownerUid, string speciesId, string wildEntityCode)
    {
        if (string.IsNullOrWhiteSpace(ownerUid))
        {
            return;
        }

        bool legacyArctic = string.Equals(speciesId, "fox", StringComparison.OrdinalIgnoreCase)
            && wildEntityCode.Contains("fox-arctic-", StringComparison.OrdinalIgnoreCase);
        bool legacyFemale = string.Equals(speciesId, "fox", StringComparison.OrdinalIgnoreCase)
            && wildEntityCode.EndsWith("-female", StringComparison.OrdinalIgnoreCase);
        if (!recruitmentRewardsByOwner.TryGetValue(ownerUid, out List<FoxRecruitmentRewardRecord>? rewards))
        {
            rewards = new List<FoxRecruitmentRewardRecord>();
            recruitmentRewardsByOwner[ownerUid] = rewards;
        }
        rewards.Add(new FoxRecruitmentRewardRecord
        {
            OwnerUid = ownerUid,
            SpeciesId = string.IsNullOrWhiteSpace(speciesId) ? "fox" : speciesId,
            WildEntityCode = wildEntityCode ?? string.Empty,
            Arctic = legacyArctic,
            Female = legacyFemale
        });
    }

    public void ClearPendingRecruitment(string ownerUid)
    {
        if (!recruitmentRewardsByOwner.TryGetValue(ownerUid, out List<FoxRecruitmentRewardRecord>? rewards)
            || rewards.Count == 0)
        {
            return;
        }
        rewards.RemoveAt(0);
        if (rewards.Count == 0)
        {
            recruitmentRewardsByOwner.Remove(ownerUid);
        }
    }

    public IReadOnlyList<byte[]> GetLootBytes(string ownerUid)
    {
        return lootByOwner.TryGetValue(ownerUid, out List<byte[]>? items)
            ? items.ToList()
            : Array.Empty<byte[]>();
    }

    public void AddLoot(string ownerUid, ItemStack stack)
    {
        if (string.IsNullOrWhiteSpace(ownerUid) || stack == null || stack.StackSize <= 0)
        {
            return;
        }

        if (!lootByOwner.TryGetValue(ownerUid, out List<byte[]>? items))
        {
            items = new List<byte[]>();
            lootByOwner[ownerUid] = items;
        }

        items.Add(stack.ToBytes());
    }

    public void ReplaceLoot(string ownerUid, IEnumerable<byte[]> items)
    {
        List<byte[]> replacement = items
            .Where(item => item != null && item.Length > 0)
            .Select(item => item.ToArray())
            .ToList();

        if (replacement.Count == 0)
        {
            lootByOwner.Remove(ownerUid);
            return;
        }

        lootByOwner[ownerUid] = replacement;
    }

    public FoxExpeditionRecord? GetExpedition(string ownerUid)
    {
        return GetExpeditions(ownerUid).FirstOrDefault();
    }

    public FoxExpeditionRecord? GetExpedition(long expeditionId)
    {
        return expeditionsById.TryGetValue(expeditionId, out FoxExpeditionRecord? expedition)
            ? expedition
            : null;
    }

    public IReadOnlyList<FoxExpeditionRecord> GetExpeditions(string ownerUid)
    {
        return expeditionsById.Values
            .Where(record => string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal))
            .OrderBy(record => record.StartedTotalHours)
            .ThenBy(record => record.ExpeditionId)
            .ToList();
    }

    public FoxExpeditionRecord? GetExpeditionForMember(string ownerUid, string foxId)
    {
        return expeditionsById.Values.FirstOrDefault(record =>
            string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal)
            && record.SelectedFoxIds.Contains(foxId, StringComparer.Ordinal));
    }

    public bool IsSearchTargetActive(string ownerUid, string targetFoxId)
    {
        return !string.IsNullOrWhiteSpace(targetFoxId)
            && expeditionsById.Values.Any(record =>
                string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal)
                && string.Equals(record.Type, FoxExpeditionType.SearchLost, StringComparison.Ordinal)
                && string.Equals(record.TargetFoxId, targetFoxId, StringComparison.Ordinal));
    }

    public int GetExpeditionCapacity(string ownerUid)
    {
        int rank = GetPackTalentRank(ownerUid, "many-trails");
        return rank >= int.MaxValue - BaseExpeditionCapacity
            ? int.MaxValue
            : BaseExpeditionCapacity + rank;
    }

    public IEnumerable<FoxExpeditionRecord> GetActiveExpeditions() => expeditionsById.Values;

    public IReadOnlyList<FoxScavengeSiteRecord> GetScavengeSites(string ownerUid) =>
        scavengeSitesById.Values
            .Where(site => string.Equals(site.OwnerUid, ownerUid, StringComparison.Ordinal))
            .OrderBy(site => site.SiteId)
            .ToList();

    public bool TryGetScavengeSite(string ownerUid, long siteId, out FoxScavengeSiteRecord? site)
    {
        bool found = scavengeSitesById.TryGetValue(siteId, out site)
            && string.Equals(site.OwnerUid, ownerUid, StringComparison.Ordinal);
        if (!found) site = null;
        return found;
    }

    public bool IsScavengeSiteBusy(string ownerUid, long siteId) =>
        expeditionsById.Values.Any(expedition =>
            string.Equals(expedition.OwnerUid, ownerUid, StringComparison.Ordinal)
            && expedition.ScavengeSiteId == siteId
            && siteId > 0);

    public bool TryCreateScavengeSite(string ownerUid, Random random, out FoxScavengeSiteRecord? site,
        bool favorDifficultSites = false)
    {
        site = null;
        if (string.IsNullOrWhiteSpace(ownerUid)
            || GetScavengeSites(ownerUid).Count >= FoxScavengeSites.MaximumRememberedSites)
            return false;
        site = FoxScavengeSites.Create(random, ownerUid, nextScavengeSiteSequence++, favorDifficultSites);
        scavengeSitesById[site.SiteId] = site;
        return true;
    }

    public bool TryAbandonScavengeSite(string ownerUid, long siteId)
    {
        if (!TryGetScavengeSite(ownerUid, siteId, out _)
            || IsScavengeSiteBusy(ownerUid, siteId)) return false;
        return scavengeSitesById.Remove(siteId);
    }

    public bool TryRerollScavengeSites(string ownerUid, Random random, bool preScout)
    {
        if (string.IsNullOrWhiteSpace(ownerUid) || random == null
            || expeditionsById.Values.Any(expedition =>
                string.Equals(expedition.OwnerUid, ownerUid, StringComparison.Ordinal)
                && (expedition.Type is FoxExpeditionType.Scout or FoxExpeditionType.Scavenge)))
            return false;

        List<FoxScavengeSiteRecord> replacements = new();
        for (int i = 0; i < FoxScavengeSites.MaximumRememberedSites; i++)
        {
            FoxScavengeSiteRecord site = FoxScavengeSites.Create(
                random, ownerUid, nextScavengeSiteSequence++);
            if (preScout) FoxScavengeSites.RevealAllFromOutside(site);
            replacements.Add(site);
        }
        foreach (FoxScavengeSiteRecord old in GetScavengeSites(ownerUid))
            scavengeSitesById.Remove(old.SiteId);
        foreach (FoxScavengeSiteRecord site in replacements)
            scavengeSitesById.Add(site.SiteId, site);
        return true;
    }

    public bool TryReserveScavengeSite(
        string ownerUid, long siteId, FoxExpeditionRecord expedition, string focus,
        bool blockedAtDoor)
    {
        if (!FoxScavengeSites.IsFocus(focus)
            || expedition == null
            || !string.Equals(expedition.OwnerUid, ownerUid, StringComparison.Ordinal)
            || !string.Equals(expedition.Type, FoxExpeditionType.Scavenge, StringComparison.Ordinal)
            || !TryGetScavengeSite(ownerUid, siteId, out FoxScavengeSiteRecord? site)
            || site == null
            || site.RemainingVisits <= 0
            || IsScavengeSiteBusy(ownerUid, siteId)) return false;

        expedition.ScavengeSiteId = siteId;
        expedition.ScavengeFocus = focus;
        expedition.BlockedAtDoor = blockedAtDoor;
        // The point is spent at departure. The already-decided blocked-door
        // result refunds it immediately, while the trip still occupies the site.
        site.RemainingVisits--;
        if (blockedAtDoor) site.RemainingVisits++;
        else site.SearchesStarted++;
        return true;
    }

    public void CancelNewExpedition(FoxExpeditionRecord expedition, int preparationCost)
    {
        if (expedition == null || !expeditionsById.Remove(expedition.ExpeditionId)) return;
        if (preparationCost > 0)
            SetPackPoints(expedition.OwnerUid, GetPackPoints(expedition.OwnerUid) + preparationCost);
    }

    public bool TryStartExpedition(
        string ownerUid,
        string type,
        IEnumerable<string> selectedFoxIds,
        string targetFoxId,
        float expeditionStrength,
        float targetStrength,
        long startedUtcMs,
        long completesUtcMs,
        double startedTotalHours,
        double completesTotalHours,
        float baseDurationHours,
        float plannedDurationHours,
        IEnumerable<FoxExpeditionMemberSummaryPacket> memberSnapshots,
        bool prepared,
        int preparationCost,
        float patrolRiskReduction,
        out FoxExpeditionRecord? startedExpedition)
    {
        startedExpedition = null;
        List<string> selected = selectedFoxIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (string.IsNullOrWhiteSpace(ownerUid)
            || selected.Count == 0
            || expeditionStrength <= 0f
            || targetStrength <= 0f
            || preparationCost < 0
            || GetPackPoints(ownerUid) < preparationCost
            || HasLoot(ownerUid)
            || HasPendingRecruitment(ownerUid)
            || GetExpeditions(ownerUid).Count >= GetExpeditionCapacity(ownerUid)
            || selected.Any(foxId => GetExpeditionForMember(ownerUid, foxId) != null)
            || string.Equals(type, FoxExpeditionType.SearchLost, StringComparison.Ordinal)
                && IsSearchTargetActive(ownerUid, targetFoxId))
        {
            return false;
        }

        FoxExpeditionRecord record = new()
        {
            ExpeditionId = nextExpeditionSequence++,
            OwnerUid = ownerUid,
            Type = type,
            TargetFoxId = targetFoxId ?? string.Empty,
            StartedUtcMs = startedUtcMs,
            CompletesUtcMs = completesUtcMs,
            SelectedFoxIds = selected,
            ExpeditionStrength = expeditionStrength,
            TargetStrength = targetStrength,
            StartedTotalHours = startedTotalHours,
            CompletesTotalHours = completesTotalHours,
            BaseDurationHours = Math.Max(0f, baseDurationHours),
            PlannedDurationHours = Math.Max(0f, plannedDurationHours),
            MemberSnapshots = memberSnapshots?.ToList() ?? new List<FoxExpeditionMemberSummaryPacket>(),
            Prepared = prepared,
            PreparationRiskReduction = prepared ? FoxExpeditionCatalog.PreparationRiskReduction : 0f,
            PatrolRiskReduction = Math.Max(0f, patrolRiskReduction)
        };
        expeditionsById[record.ExpeditionId] = record;
        startedExpedition = record;
        if (preparationCost > 0)
        {
            SetPackPoints(ownerUid, GetPackPoints(ownerUid) - preparationCost);
        }
        return true;
    }

    public bool MarkExpeditionLate(long expeditionId, long completesUtcMs, double completesTotalHours, float lateDurationHours)
    {
        if (!expeditionsById.TryGetValue(expeditionId, out FoxExpeditionRecord? record))
        {
            return false;
        }

        record.RunningLate = true;
        record.CompletesUtcMs = completesUtcMs;
        record.CompletesTotalHours = completesTotalHours;
        record.LateDurationHours = Math.Max(0f, lateDurationHours);
        return true;
    }

    public bool MakeExpeditionDue(long expeditionId, long nowUtcMs, double nowTotalHours)
    {
        if (!expeditionsById.TryGetValue(expeditionId, out FoxExpeditionRecord? record))
        {
            return false;
        }

        record.CompletesUtcMs = Math.Max(1, nowUtcMs);
        record.CompletesTotalHours = Math.Max(0d, nowTotalHours);
        record.DebugForceCompletion = true;
        return true;
    }

    public FoxExpeditionRecord? TakeCompletedExpedition(long expeditionId, long nowUtcMs, double nowTotalHours)
    {
        if (!expeditionsById.TryGetValue(expeditionId, out FoxExpeditionRecord? record)
            || (record.CompletesTotalHours > 0d
                ? record.CompletesTotalHours > nowTotalHours
                : record.CompletesUtcMs > nowUtcMs))
        {
            return null;
        }

        expeditionsById.Remove(expeditionId);
        return record;
    }

    public FoxExpeditionSummaryPacket? GetLastExpeditionReport(string ownerUid)
    {
        return GetExpeditionReports(ownerUid).FirstOrDefault();
    }

    public IReadOnlyList<FoxExpeditionSummaryPacket> GetExpeditionReports(string ownerUid)
    {
        return reportsByOwner.TryGetValue(ownerUid, out List<FoxExpeditionSummaryPacket>? reports)
            ? reports.OrderByDescending(report => report.CompletedTotalHours)
                .ThenByDescending(report => report.ExpeditionId)
                .ToList()
            : Array.Empty<FoxExpeditionSummaryPacket>();
    }

    public IReadOnlyList<FoxExpeditionSummaryPacket> GetPendingExpeditionReports(string ownerUid)
    {
        return GetExpeditionReports(ownerUid).Where(report => report.NotificationPending).ToList();
    }

    public bool RemoveExpeditionReport(string ownerUid, long expeditionId)
    {
        if (string.IsNullOrWhiteSpace(ownerUid)
            || expeditionId <= 0
            || !reportsByOwner.TryGetValue(ownerUid, out List<FoxExpeditionSummaryPacket>? reports))
        {
            return false;
        }

        int removed = reports.RemoveAll(report => report.ExpeditionId == expeditionId);
        if (reports.Count == 0)
        {
            reportsByOwner.Remove(ownerUid);
        }
        return removed > 0;
    }

    public void SetLastExpeditionReport(string ownerUid, FoxExpeditionSummaryPacket report)
    {
        if (string.IsNullOrWhiteSpace(ownerUid) || report == null)
        {
            return;
        }

        report.OwnerUid = ownerUid;
        if (!reportsByOwner.TryGetValue(ownerUid, out List<FoxExpeditionSummaryPacket>? reports))
        {
            reports = new List<FoxExpeditionSummaryPacket>();
            reportsByOwner[ownerUid] = reports;
        }
        reports.RemoveAll(existing => existing.ExpeditionId > 0 && existing.ExpeditionId == report.ExpeditionId);
        reports.Add(report);
        if (reports.Count > MaximumExpeditionReportHistory)
        {
            reports.RemoveRange(0, reports.Count - MaximumExpeditionReportHistory);
        }
    }

    public int AdjustPackPoints(string ownerUid, int delta)
    {
        int points = (int)Math.Clamp((long)GetPackPoints(ownerUid) + delta, 0L, int.MaxValue);
        SetPackPoints(ownerUid, points);
        return points;
    }

    public bool TrySpendPackPoints(string ownerUid, int cost)
    {
        cost = Math.Max(0, cost);
        int available = GetPackPoints(ownerUid);
        if (available < cost)
        {
            return false;
        }

        SetPackPoints(ownerUid, available - cost);
        return true;
    }

    public IReadOnlyList<string> GetUnlockedExpeditionTypes(string ownerUid)
    {
        HashSet<string> unlocked = FoxExpeditionCatalog.All
            .Where(definition => definition.DefaultUnlocked)
            .Select(definition => definition.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (progressByOwner.TryGetValue(ownerUid, out FoxPackProgressRecord? progress))
        {
            foreach (string id in progress.UnlockedExpeditionTypes ?? new List<string>())
            {
                if (FoxExpeditionCatalog.Get(id) is { Retired: false })
                {
                    unlocked.Add(id);
                }
            }
        }
        return unlocked.ToList();
    }

    public bool IsExpeditionUnlocked(string ownerUid, string type)
    {
        FoxExpeditionDefinition? definition = FoxExpeditionCatalog.Get(type);
        return definition is { Retired: false }
            && (definition.DefaultUnlocked
                || GetProgress(ownerUid).UnlockedExpeditionTypes.Contains(type, StringComparer.Ordinal));
    }

    public bool AreExpeditionPrerequisitesMet(string ownerUid, string type)
    {
        FoxExpeditionDefinition? definition = FoxExpeditionCatalog.Get(type);
        return definition is { Retired: false }
            && definition.RequiredRouteIds.All(required => IsExpeditionUnlocked(ownerUid, required));
    }

    public bool TryUnlockExpedition(string ownerUid, string type, int cost)
    {
        FoxExpeditionDefinition? definition = FoxExpeditionCatalog.Get(type);
        if (definition == null || definition.Retired
            || definition.DefaultUnlocked
            || IsExpeditionUnlocked(ownerUid, type)
            || !AreExpeditionPrerequisitesMet(ownerUid, type))
        {
            return false;
        }
        if (!TrySpendPackPoints(ownerUid, cost))
        {
            return false;
        }

        GetProgress(ownerUid).UnlockedExpeditionTypes.Add(type);
        return true;
    }

    public IReadOnlyList<string> GetUnlockedPackTalents(string ownerUid)
    {
        if (!progressByOwner.TryGetValue(ownerUid, out FoxPackProgressRecord? progress))
        {
            return Array.Empty<string>();
        }

        return (progress.UnlockedPackTalents ?? new List<string>())
            .Where(id => PackTalentCatalog.GetTalent(id)?.Implemented == true)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public bool IsPackTalentUnlocked(string ownerUid, string id)
    {
        if (PackTalentCatalog.GetTalent(id)?.Repeatable == true)
        {
            return GetPackTalentRank(ownerUid, id) > 0;
        }
        return !string.IsNullOrWhiteSpace(id)
            && GetUnlockedPackTalents(ownerUid).Contains(id, StringComparer.Ordinal);
    }

    public int GetPackTalentRank(string ownerUid, string id)
    {
        if (PackTalentCatalog.GetTalent(id)?.Repeatable == true)
        {
            if (!progressByOwner.TryGetValue(ownerUid, out FoxPackProgressRecord? progress)) return 0;
            return string.Equals(id, "far-reaching-pack", StringComparison.Ordinal)
                ? Math.Max(0, progress.PermanentRangeRank)
                : string.Equals(id, "many-trails", StringComparison.Ordinal)
                    ? Math.Max(0, progress.PermanentExpeditionCapacityRank)
                    : 0;
        }

        return !string.IsNullOrWhiteSpace(id)
            && GetUnlockedPackTalents(ownerUid).Contains(id, StringComparer.Ordinal) ? 1 : 0;
    }

    public bool ArePackTalentPrerequisitesMet(string ownerUid, string id)
    {
        PackTalentDefinition? talent = PackTalentCatalog.GetTalent(id);
        return talent?.Implemented == true
            && (string.IsNullOrWhiteSpace(talent.ParentId)
                || IsPackTalentUnlocked(ownerUid, talent.ParentId));
    }

    public bool TryUnlockPackTalent(string ownerUid, string id, int cost)
    {
        PackTalentDefinition? talent = PackTalentCatalog.GetTalent(id);
        if (talent?.Implemented != true
            || (!talent.Repeatable && IsPackTalentUnlocked(ownerUid, id))
            || !ArePackTalentPrerequisitesMet(ownerUid, id))
        {
            return false;
        }

        FoxPackProgressRecord progress = GetProgress(ownerUid);
        int rank = GetPackTalentRank(ownerUid, id);
        if (talent.Repeatable && rank == int.MaxValue)
        {
            return false;
        }
        int expectedCost = talent.Repeatable
            ? Math.Max(1, rank + 1)
            : talent.UnlockCost;
        if (cost != expectedCost || !TrySpendPackPoints(ownerUid, expectedCost))
        {
            return false;
        }
        if (talent.Repeatable)
        {
            if (string.Equals(id, "far-reaching-pack", StringComparison.Ordinal))
            {
                progress.PermanentRangeRank++;
            }
            else if (string.Equals(id, "many-trails", StringComparison.Ordinal))
            {
                progress.PermanentExpeditionCapacityRank++;
            }
            else
            {
                return false;
            }
            return true;
        }

        progress.UnlockedPackTalents ??= new List<string>();
        progress.UnlockedPackTalents.Add(id);
        return true;
    }

    public bool IsPatrolPrepared(string ownerUid)
    {
        return progressByOwner.TryGetValue(ownerUid, out FoxPackProgressRecord? progress)
            && progress.PatrolPrepared;
    }

    public void SetPatrolPrepared(string ownerUid, bool prepared)
    {
        GetProgress(ownerUid).PatrolPrepared = prepared;
    }

    public bool IsCargoUnloadingActive(string ownerUid)
    {
        return progressByOwner.TryGetValue(ownerUid, out FoxPackProgressRecord? progress)
            && progress.CargoUnloadingActive;
    }

    public void SetCargoUnloadingActive(string ownerUid, bool active)
    {
        GetProgress(ownerUid).CargoUnloadingActive = active;
    }

    public IReadOnlyList<string> GetCargoUnloadingOwners()
    {
        return progressByOwner.Values
            .Where(progress => progress.CargoUnloadingActive && !string.IsNullOrWhiteSpace(progress.OwnerUid))
            .Select(progress => progress.OwnerUid)
            .ToList();
    }

    private FoxPackProgressRecord GetProgress(string ownerUid)
    {
        if (!progressByOwner.TryGetValue(ownerUid, out FoxPackProgressRecord? progress))
        {
            progress = new FoxPackProgressRecord { OwnerUid = ownerUid };
            progressByOwner[ownerUid] = progress;
        }
        return progress;
    }

    public void TryMigrateEntityPackPoints(string ownerUid, int entityPoints)
    {
        if (entityPoints < 0 || ownersWithAuthoritativePoints.Contains(ownerUid))
        {
            return;
        }

        SetPackPoints(ownerUid, entityPoints);
    }

    private void SetPackPoints(string ownerUid, int points)
    {
        if (string.IsNullOrWhiteSpace(ownerUid))
        {
            return;
        }

        pointsByOwner[ownerUid] = Math.Max(0, points);
        ownersWithAuthoritativePoints.Add(ownerUid);
    }

    private void LoadVersion2(FoxPackSaveData saved)
    {
        nextFoxNumber = Math.Max(1, saved.NextFoxNumber);
        nextCairnSequence = Math.Max(1, saved.NextCairnSequence);
        nextExpeditionSequence = Math.Max(1, saved.NextExpeditionSequence);
        nextScavengeSiteSequence = Math.Max(1, saved.NextScavengeSiteSequence);
        permanentlyDeletedFoxIds.UnionWith((saved.PermanentlyDeletedFoxIds ?? new List<string>())
            .Where(foxId => !string.IsNullOrWhiteSpace(foxId)));
        foreach (FoxPackRecordV2 record in saved.Records ?? new List<FoxPackRecordV2>())
        {
            if (record.Number <= 0)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(record.FoxId))
            {
                record.FoxId = GetLegacyFoxId(record.OwnerUid, record.Number);
            }
            if (permanentlyDeletedFoxIds.Contains(record.FoxId))
            {
                continue;
            }

            record.LifetimePoints = Math.Max(record.LifetimePoints, Math.Max(0, record.Points));
            record.Level = Math.Max(CompanionProgressionRules.StartingLevel, record.Level);
            record.CurrentLevelExperience = Math.Max(0L, record.CurrentLevelExperience);
            record.LifetimeExperience = Math.Max(
                record.CurrentLevelExperience,
                Math.Max(0L, record.LifetimeExperience));
            record.CleanupDutyUnits = Math.Clamp(
                record.CleanupDutyUnits,
                0,
                CompanionProgressionRules.CleanupActionsPerExperience - 1);
            if (string.IsNullOrWhiteSpace(record.SpeciesId))
            {
                record.SpeciesId = "fox";
            }
            else if (!CompanionSpeciesCatalog.TryGetById(record.SpeciesId, out _))
            {
                api.Logger.Warning(
                    "[FeralKinshipCompanions] Preserved companion {0} with unknown species id '{1}'; it will remain unavailable until that profile exists.",
                    record.FoxId,
                    record.SpeciesId
                );
            }

            // Blank command fields are deliberately preserved until the entity
            // next loads. That lets a v15 animal migrate its old entity-owned
            // follow/aggression values even if the world was saved before its
            // chunk was visited under v16.
            if (!string.IsNullOrWhiteSpace(record.ActivityMode))
                record.ActivityMode = CompanionActivityMode.Normalize(record.ActivityMode);
            if (!string.IsNullOrWhiteSpace(record.FollowDistance))
                record.FollowDistance = CompanionFollowDistance.Normalize(record.FollowDistance);
            if (!string.IsNullOrWhiteSpace(record.CombatStyle))
                record.CombatStyle = CompanionCombatStyle.Normalize(record.CombatStyle);
            if (!string.IsNullOrWhiteSpace(record.RiskTolerance))
                record.RiskTolerance = CompanionRiskTolerance.Normalize(record.RiskTolerance);
            record.ActivityStartedUtcMs = Math.Max(0, record.ActivityStartedUtcMs);
            record.ActivityArrivedUtcMs = Math.Max(0, record.ActivityArrivedUtcMs);
            record.BondedPartnerId ??= string.Empty;
            record.BondedPartnerName ??= string.Empty;
            record.BreedingFeedback ??= string.Empty;
            record.PregnancyFatherId ??= string.Empty;
            record.PregnancyFatherName ??= string.Empty;
            record.ParentMotherId ??= string.Empty;
            record.ParentMotherName ??= string.Empty;
            record.ParentFatherId ??= string.Empty;
            record.ParentFatherName ??= string.Empty;
            record.ChildAdultEntityCode ??= string.Empty;
            if (!record.DutyOptionsInitialized)
            {
                // Older saves used true as the in-memory default for the
                // optional sub-duty flags. Protobuf omits false scalar values,
                // so a disabled option could otherwise come back as enabled
                // after a restart. Preserve the historical all-inclusive
                // behavior once, then let the new explicit marker protect
                // future false values.
                record.GroundDroppedItemsEnabled = true;
                record.GroundCattailsEnabled = true;
                record.GroundFlintEnabled = true;
                record.GroundSticksEnabled = true;
                record.GroundBouldersEnabled = true;
                record.GroundRocksEnabled = true;
                record.FinishedCropsEnabled = true;
                record.FinishedBerriesEnabled = true;
                record.FinishedMushroomsEnabled = true;
                record.DutyOptionsInitialized = true;
            }
            if (saved.Version < 24)
            {
                record.GeneralStorageSortingEnabled = true;
            }
            record.BankedTalentPoints = Math.Max(0, record.BankedTalentPoints);
            if (!record.PregnancyActive)
            {
                record.PendingBirth = false;
                record.PregnancyStartTotalHours = 0d;
                record.PregnancyDueTotalHours = 0d;
            }

            recordsById[record.FoxId] = record;
        }

        foreach (FoxPackPointsRecord record in saved.PackPoints ?? new List<FoxPackPointsRecord>())
        {
            if (!string.IsNullOrWhiteSpace(record.OwnerUid) && record.Points >= 0)
            {
                SetPackPoints(record.OwnerUid, record.Points);
            }
        }

        foreach (FoxPackLootRecord record in saved.Loot ?? new List<FoxPackLootRecord>())
        {
            if (!string.IsNullOrWhiteSpace(record.OwnerUid) && record.Items != null && record.Items.Count > 0)
            {
                lootByOwner[record.OwnerUid] = record.Items
                    .Where(item => item != null && item.Length > 0)
                    .Select(item => item.ToArray())
                    .ToList();
            }
        }

        foreach (FoxScavengeSiteRecord site in saved.ScavengeSites ?? new List<FoxScavengeSiteRecord>())
        {
            if (site.SiteId <= 0 || string.IsNullOrWhiteSpace(site.OwnerUid)
                || scavengeSitesById.ContainsKey(site.SiteId)
                || GetScavengeSites(site.OwnerUid).Count >= FoxScavengeSites.MaximumRememberedSites)
                continue;
            site.TotalVisits = Math.Clamp(site.TotalVisits, 1, 12);
            site.RemainingVisits = Math.Clamp(site.RemainingVisits, 0, site.TotalVisits);
            if (saved.Version < 32) FoxScavengeSites.ExpandLegacyUnknowableVisits(site);
            site.Clues ??= new List<string>();
            site.RevealedClues = Math.Clamp(site.RevealedClues, 0, site.Clues.Count);
            scavengeSitesById[site.SiteId] = site;
            nextScavengeSiteSequence = Math.Max(nextScavengeSiteSequence, site.SiteId + 1);
        }

        Dictionary<string, HashSet<string>> expeditionMembersByOwner = new(StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> searchTargetsByOwner = new(StringComparer.Ordinal);
        foreach (FoxExpeditionRecord record in saved.Expeditions ?? new List<FoxExpeditionRecord>())
        {
            if (!string.IsNullOrWhiteSpace(record.OwnerUid)
                && !string.IsNullOrWhiteSpace(record.Type)
                && (record.CompletesUtcMs > 0 || record.CompletesTotalHours > 0d))
            {
                if (string.Equals(record.Type, "searchlost", StringComparison.Ordinal)
                    && string.IsNullOrWhiteSpace(record.TargetFoxId))
                {
                    record.TargetFoxId = recordsById.Values
                        .FirstOrDefault(candidate =>
                            string.Equals(candidate.OwnerUid, record.OwnerUid, StringComparison.Ordinal)
                            && string.Equals(candidate.Status, "MIA", StringComparison.OrdinalIgnoreCase))?
                        .FoxId ?? string.Empty;
                }
                foreach (FoxExpeditionMemberSummaryPacket member in record.MemberSnapshots
                             ?? new List<FoxExpeditionMemberSummaryPacket>())
                {
                    if (string.IsNullOrWhiteSpace(member.SpeciesId)) member.SpeciesId = "fox";
                }
                record.SelectedFoxIds = (record.SelectedFoxIds ?? new List<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (record.Story != null)
                {
                    ExpeditionStoryResolver.NormalizeStory(record.Story);
                }
                if (!expeditionMembersByOwner.TryGetValue(record.OwnerUid, out HashSet<string>? usedMembers))
                {
                    usedMembers = new HashSet<string>(StringComparer.Ordinal);
                    expeditionMembersByOwner[record.OwnerUid] = usedMembers;
                }
                if (record.SelectedFoxIds.Count == 0 || record.SelectedFoxIds.Any(usedMembers.Contains))
                {
                    api.Logger.Error("[FeralKinshipCompanions] Skipped corrupt overlapping expedition for owner {0}; no valid legacy save can contain this overlap.", record.OwnerUid);
                    continue;
                }
                if (string.Equals(record.Type, FoxExpeditionType.SearchLost, StringComparison.Ordinal))
                {
                    if (!searchTargetsByOwner.TryGetValue(record.OwnerUid, out HashSet<string>? targets))
                    {
                        targets = new HashSet<string>(StringComparer.Ordinal);
                        searchTargetsByOwner[record.OwnerUid] = targets;
                    }
                    if (!string.IsNullOrWhiteSpace(record.TargetFoxId) && !targets.Add(record.TargetFoxId))
                    {
                        api.Logger.Error("[FeralKinshipCompanions] Skipped corrupt duplicate Search for Lost expedition for owner {0}.", record.OwnerUid);
                        continue;
                    }
                }
                if (record.ExpeditionId <= 0 || expeditionsById.ContainsKey(record.ExpeditionId))
                {
                    record.ExpeditionId = nextExpeditionSequence++;
                    MigratedExpeditionData = true;
                }
                nextExpeditionSequence = Math.Max(nextExpeditionSequence, record.ExpeditionId + 1);
                expeditionsById[record.ExpeditionId] = record;
                usedMembers.UnionWith(record.SelectedFoxIds);
            }
        }

        foreach (FoxRecruitmentRewardRecord reward in saved.RecruitmentRewards ?? new List<FoxRecruitmentRewardRecord>())
        {
            if (!string.IsNullOrWhiteSpace(reward.OwnerUid))
            {
                if (string.IsNullOrWhiteSpace(reward.SpeciesId)) reward.SpeciesId = "fox";
                if (string.IsNullOrWhiteSpace(reward.WildEntityCode)
                    && string.Equals(reward.SpeciesId, "fox", StringComparison.Ordinal))
                {
                    reward.WildEntityCode = $"game:fox-{(reward.Arctic ? "arctic" : "red")}-adult-{(reward.Female ? "female" : "male")}";
                }
                if (!recruitmentRewardsByOwner.TryGetValue(reward.OwnerUid, out List<FoxRecruitmentRewardRecord>? rewards))
                {
                    rewards = new List<FoxRecruitmentRewardRecord>();
                    recruitmentRewardsByOwner[reward.OwnerUid] = rewards;
                }
                rewards.Add(reward);
            }
        }

        foreach (FoxPackProgressRecord progress in saved.PackProgress ?? new List<FoxPackProgressRecord>())
        {
            if (string.IsNullOrWhiteSpace(progress.OwnerUid))
            {
                continue;
            }

            if (saved.Version < 31)
            {
                int refund = (progress.UnlockedExpeditionTypes ?? new List<string>())
                    .Distinct(StringComparer.Ordinal)
                    .Select(FoxExpeditionCatalog.Get)
                    .Where(definition => definition is { Retired: true })
                    .Sum(definition => definition!.UnlockCost);
                if (refund > 0)
                {
                    AdjustPackPoints(progress.OwnerUid, refund);
                    MigratedRetiredRoutes = true;
                }
            }
            progress.UnlockedExpeditionTypes = (progress.UnlockedExpeditionTypes ?? new List<string>())
                .Where(id => FoxExpeditionCatalog.Get(id) is { DefaultUnlocked: false, Retired: false })
                .Distinct(StringComparer.Ordinal)
                .ToList();
            progress.UnlockedPackTalents = (progress.UnlockedPackTalents ?? new List<string>())
                .Where(id => PackTalentCatalog.GetTalent(id)?.Implemented == true)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            progress.PermanentRangeRank = Math.Max(0, progress.PermanentRangeRank);
            progress.PermanentExpeditionCapacityRank = Math.Max(0, progress.PermanentExpeditionCapacityRank);
            progressByOwner[progress.OwnerUid] = progress;
        }

        List<FoxExpeditionSummaryPacket> savedReports = saved.ExpeditionReportHistory?.Count > 0
            ? saved.ExpeditionReportHistory
            : saved.LastExpeditionReports ?? new List<FoxExpeditionSummaryPacket>();
        foreach (FoxExpeditionSummaryPacket report in savedReports)
        {
            if (!string.IsNullOrWhiteSpace(report.OwnerUid))
            {
                foreach (FoxExpeditionMemberSummaryPacket member in report.Members
                             ?? new List<FoxExpeditionMemberSummaryPacket>())
                {
                    if (string.IsNullOrWhiteSpace(member.SpeciesId)) member.SpeciesId = "fox";
                }
                if (report.ExpeditionId <= 0)
                {
                    report.ExpeditionId = nextExpeditionSequence++;
                    MigratedExpeditionData = true;
                }
                nextExpeditionSequence = Math.Max(nextExpeditionSequence, report.ExpeditionId + 1);
                SetLastExpeditionReport(report.OwnerUid, report);
            }
        }

        foreach (FoxPackCairnRecord cairn in saved.Cairns ?? new List<FoxPackCairnRecord>())
        {
            if (string.IsNullOrWhiteSpace(cairn.OwnerUid) || cairn.PlacedSequence <= 0)
            {
                continue;
            }

            cairnsByPosition[PositionKey(cairn.X, cairn.Y, cairn.Z, cairn.Dimension)] = cairn;
            nextCairnSequence = Math.Max(nextCairnSequence, cairn.PlacedSequence + 1);
        }

        foreach (FoxBedRecord bed in saved.Beds ?? new List<FoxBedRecord>())
        {
            if (string.IsNullOrWhiteSpace(bed.OwnerUid))
            {
                continue;
            }

            string key = PositionKey(bed.X, bed.Y, bed.Z, bed.Dimension);
            if (!string.IsNullOrWhiteSpace(bed.FoxId)
                && (!recordsById.TryGetValue(bed.FoxId, out FoxPackRecordV2? fox)
                    || fox.Archived
                    || !string.Equals(fox.OwnerUid, bed.OwnerUid, StringComparison.Ordinal)))
            {
                bed.FoxId = string.Empty;
            }
            bedsByPosition[key] = bed;
        }
        foreach (FoxWorkCartRecord cart in saved.WorkCarts ?? new List<FoxWorkCartRecord>())
        {
            if (string.IsNullOrWhiteSpace(cart.OwnerUid))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(cart.Kind)) cart.Kind = "generic";

            cart.AssignedFoxIds = (cart.AssignedFoxIds ?? new List<string>())
                .Where(foxId => recordsById.TryGetValue(foxId, out FoxPackRecordV2? fox)
                    && !fox.Archived
                    && string.Equals(fox.OwnerUid, cart.OwnerUid, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            workCartsByPosition[PositionKey(cart.X, cart.Y, cart.Z, cart.Dimension)] = cart;
            foreach (string foxId in cart.AssignedFoxIds)
            {
                FoxPackRecordV2 fox = recordsById[foxId];
                fox.HasHome = true;
                fox.HomeType = "workcart";
                fox.HomeX = cart.X;
                fox.HomeY = cart.Y;
                fox.HomeZ = cart.Z;
                fox.HomeDimension = cart.Dimension;
            }
        }
        foreach (FoxPackAmenityRecord amenity in saved.Amenities ?? new List<FoxPackAmenityRecord>())
        {
            if (string.IsNullOrWhiteSpace(amenity.OwnerUid) || string.IsNullOrWhiteSpace(amenity.Kind)) continue;
            amenity.StorageAdvancedIncludedItemCodes ??= new List<string>();
            amenity.StorageAdvancedExcludedItemCodes ??= new List<string>();
            amenity.StorageAdvancedIncludedItemCodes = amenity.StorageAdvancedIncludedItemCodes
                .Where(code => !string.IsNullOrWhiteSpace(code) && code.Length <= 256)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(FoxStorageRouting.MaxExactItemRules)
                .ToList();
            amenity.StorageAdvancedExcludedItemCodes = amenity.StorageAdvancedExcludedItemCodes
                .Where(code => !string.IsNullOrWhiteSpace(code) && code.Length <= 256)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(FoxStorageRouting.MaxExactItemRules)
                .ToList();
            if (amenity.StorageAdvancedExcludedItemCodes.Count > 0)
            {
                HashSet<string> excluded = new(amenity.StorageAdvancedExcludedItemCodes, StringComparer.OrdinalIgnoreCase);
                amenity.StorageAdvancedIncludedItemCodes.RemoveAll(excluded.Contains);
            }
            if (amenity.StorageAdvancedExcludedItemCodes.Count + amenity.StorageAdvancedIncludedItemCodes.Count
                > FoxStorageRouting.MaxExactItemRules)
            {
                amenity.StorageAdvancedExcludedItemCodes = amenity.StorageAdvancedExcludedItemCodes
                    .Take(FoxStorageRouting.MaxExactItemRules)
                    .ToList();
                HashSet<string> excluded = new(amenity.StorageAdvancedExcludedItemCodes, StringComparer.OrdinalIgnoreCase);
                amenity.StorageAdvancedIncludedItemCodes = amenity.StorageAdvancedIncludedItemCodes
                    .Where(code => !excluded.Contains(code))
                    .Take(Math.Max(0, FoxStorageRouting.MaxExactItemRules - amenity.StorageAdvancedExcludedItemCodes.Count))
                    .ToList();
            }
            if (string.Equals(amenity.Kind, "storage", StringComparison.Ordinal))
            {
                // Existing pack crates predate routing and remain general
                // destinations when their saved record has no new fields.
                amenity.StorageRoutingEnabled = true;
                if (amenity.StorageRoutingMask == 0)
                {
                    amenity.StorageRoutingMask = (int)FoxStorageRouting.Category.General;
                }
            }
            amenitiesByPosition[PositionKey(amenity.X, amenity.Y, amenity.Z, amenity.Dimension)] = amenity;
        }
    }

    private void ClearFoxHome(string foxId)
    {
        if (!recordsById.TryGetValue(foxId, out FoxPackRecordV2? fox))
        {
            return;
        }

        fox.HasHome = false;
        fox.HomeType = string.Empty;
        fox.HomeX = 0;
        fox.HomeY = 0;
        fox.HomeZ = 0;
        fox.HomeDimension = 0;
    }

    private static string PositionKey(int x, int y, int z, int dimension)
    {
        return $"{dimension}:{x}:{y}:{z}";
    }

    private bool LoadLegacyData()
    {
        List<FoxPackRecord> records = api.WorldManager.SaveGame.GetData(
            LegacyRecordsSaveKey,
            new List<FoxPackRecord>()
        );
        foreach (FoxPackRecord legacyRecord in records)
        {
            if (legacyRecord.Number <= 0 || string.IsNullOrWhiteSpace(legacyRecord.OwnerUid))
            {
                continue;
            }

            FoxPackRecordV2 record = ConvertLegacyRecord(legacyRecord);
            record.FoxId = GetLegacyFoxId(record.OwnerUid, record.Number);
            recordsById[record.FoxId] = record;
        }

        // The dedicated legacy ledger wins over redundant values copied into
        // roster rows. This preserves deliberate pack-point adjustments.
        List<FoxPackPointsRecord> dedicatedPoints = api.WorldManager.SaveGame.GetData(
            LegacyPointsSaveKey,
            new List<FoxPackPointsRecord>()
        );
        foreach (FoxPackPointsRecord record in dedicatedPoints)
        {
            if (!string.IsNullOrWhiteSpace(record.OwnerUid) && record.Points >= 0)
            {
                SetPackPoints(record.OwnerUid, record.Points);
            }
        }

        foreach (IGrouping<string, FoxPackRecordV2> ownerRecords in recordsById.Values
                     .Where(record => !string.IsNullOrWhiteSpace(record.OwnerUid))
                     .GroupBy(record => record.OwnerUid))
        {
            if (ownersWithAuthoritativePoints.Contains(ownerRecords.Key))
            {
                continue;
            }

            FoxPackRecordV2? redundantPointRecord = ownerRecords.FirstOrDefault(record => record.HasPackPoints);
            int points = redundantPointRecord != null
                ? Math.Max(0, redundantPointRecord.PackPoints)
                : ownerRecords.Sum(record => Math.Max(0, record.Points));
            SetPackPoints(ownerRecords.Key, points);
        }

        int legacyNextNumber = api.WorldManager.SaveGame.GetData(LegacyNextNumberSaveKey, 1);
        nextFoxNumber = Math.Max(1, legacyNextNumber);
        return records.Count > 0 || dedicatedPoints.Count > 0 || legacyNextNumber > 1;
    }

    private static FoxPackRecordV2 ConvertLegacyRecord(FoxPackRecord legacy)
    {
        return new FoxPackRecordV2
        {
            Number = legacy.Number,
            OwnerUid = legacy.OwnerUid,
            EntityId = legacy.EntityId,
            Name = legacy.Name,
            Personality = legacy.Personality,
            Mood = legacy.Mood,
            Status = legacy.Status,
            CurrentHealth = legacy.CurrentHealth,
            MaxHealth = legacy.MaxHealth,
            RequestsGenerated = legacy.RequestsGenerated,
            RequestsCompleted = legacy.RequestsCompleted,
            Points = legacy.Points,
            PackPoints = legacy.PackPoints,
            HasPackPoints = legacy.HasPackPoints,
            ActiveRequest = legacy.ActiveRequest,
            LastCompleted = legacy.LastCompleted,
            LastSeenDay = legacy.LastSeenDay,
            LastKnownX = legacy.LastKnownX,
            LastKnownY = legacy.LastKnownY,
            LastKnownZ = legacy.LastKnownZ,
            HasLastKnownPosition = legacy.HasLastKnownPosition,
            SpeciesId = "fox",
            ActivityMode = CompanionActivityMode.AtEase,
            FollowDistance = CompanionFollowDistance.Normal,
            CombatStyle = CompanionCombatStyle.Defensive,
            RiskTolerance = CompanionRiskTolerance.Steady
        };
    }

    private static string GetLegacyFoxId(string ownerUid, int number)
    {
        return $"legacy:{ownerUid}:{number}";
    }
}
