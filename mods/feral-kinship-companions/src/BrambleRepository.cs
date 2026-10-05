#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;

internal static class BrambleOnboardingState
{
    public const string Unresolved = "unresolved";
    public const string Accepted = "accepted";
    public const string NotNow = "not-now";
    public const string AskLater = "ask-later";

    public static bool IsResolved(string? value) => value is Accepted or NotNow or AskLater;
}

internal static class BrambleHintMode
{
    public const string Quiet = "quiet";
    public const string Occasional = "occasional";
    public const string Frequent = "frequent";

    public static bool IsValid(string? value) => value is Quiet or Occasional or Frequent;
    public static string Normalize(string? value) => IsValid(value) ? value! : Quiet;
}

/// <summary>
/// Bramble state is deliberately independent of the Companion pack ledger.
/// One record belongs to one player in one world, while a disposable entity
/// instance merely presents the selected form. Instance tokens make an old
/// entity harmless if a reconnect or form change materializes a replacement
/// before the old chunk is loaded again.
/// </summary>
internal sealed class BrambleRepository
{
    private const string SaveKey = "feralkinshipcompanions:bramble-v1";
    private const int CurrentVersion = 1;

    private readonly ICoreServerAPI api;
    private readonly Dictionary<string, BramblePlayerRecord> byOwner = new(StringComparer.Ordinal);

    public BrambleRepository(ICoreServerAPI api)
    {
        this.api = api;
    }

    public bool Loaded { get; private set; }

    public void Load()
    {
        byOwner.Clear();
        BrambleSaveData? saved = api.WorldManager.SaveGame.GetData<BrambleSaveData?>(SaveKey, null);
        if (saved?.Version > CurrentVersion)
        {
            api.Logger.Error(
                "[FeralKinshipCompanions] Bramble save version {0} is newer than supported version {1}; Bramble state will remain read-only.",
                saved.Version,
                CurrentVersion);
            Loaded = false;
            return;
        }

        foreach (BramblePlayerRecord record in saved?.Players ?? new List<BramblePlayerRecord>())
        {
            if (string.IsNullOrWhiteSpace(record.OwnerUid)) continue;
            Normalize(record);
            byOwner[record.OwnerUid] = record;
        }
        Loaded = true;
    }

    public void Save()
    {
        if (!Loaded) return;
        api.WorldManager.SaveGame.StoreData(SaveKey, new BrambleSaveData
        {
            Version = CurrentVersion,
            Players = byOwner.Values.OrderBy(record => record.OwnerUid).ToList()
        });
    }

    public BramblePlayerRecord GetOrCreate(string ownerUid)
    {
        if (!byOwner.TryGetValue(ownerUid, out BramblePlayerRecord? record))
        {
            record = new BramblePlayerRecord
            {
                OwnerUid = ownerUid,
                OnboardingState = BrambleOnboardingState.Unresolved,
                HintMode = BrambleHintMode.Quiet,
                SpeciesId = "fox",
                Follow = true,
                EntityToken = Guid.NewGuid().ToString("N")
            };
            byOwner[ownerUid] = record;
        }
        Normalize(record);
        return record;
    }

    public bool TryGet(string ownerUid, out BramblePlayerRecord record)
    {
        if (byOwner.TryGetValue(ownerUid, out BramblePlayerRecord? found))
        {
            record = found;
            return true;
        }
        record = null!;
        return false;
    }

    public IEnumerable<BramblePlayerRecord> All => byOwner.Values;

    public void Reset(string ownerUid)
    {
        byOwner.Remove(ownerUid);
    }

    private static void Normalize(BramblePlayerRecord record)
    {
        record.OnboardingState = BrambleOnboardingState.IsResolved(record.OnboardingState)
            ? record.OnboardingState
            : BrambleOnboardingState.Unresolved;
        record.HintMode = BrambleHintMode.Normalize(record.HintMode);
        if (string.IsNullOrWhiteSpace(record.SpeciesId)) record.SpeciesId = "fox";
        if (string.IsNullOrWhiteSpace(record.EntityToken)) record.EntityToken = Guid.NewGuid().ToString("N");
        record.SeenMilestones ??= new List<string>();
        record.ObservedCompanionStates ??= new List<BrambleObservedCompanionState>();
    }
}

[ProtoContract]
public sealed class BrambleSaveData
{
    [ProtoMember(1)] public int Version;
    [ProtoMember(2)] public List<BramblePlayerRecord> Players = new();
}

[ProtoContract]
public sealed class BramblePlayerRecord
{
    [ProtoMember(1)] public string OwnerUid = string.Empty;
    [ProtoMember(2)] public string OnboardingState = BrambleOnboardingState.Unresolved;
    [ProtoMember(3)] public string SpeciesId = "fox";
    [ProtoMember(4)] public string HintMode = BrambleHintMode.Quiet;
    [ProtoMember(5)] public bool Follow = true;
    [ProtoMember(6)] public string EntityToken = string.Empty;
    [ProtoMember(7)] public long CurrentEntityId;
    [ProtoMember(8)] public int LastKnownX;
    [ProtoMember(9)] public int LastKnownY;
    [ProtoMember(10)] public int LastKnownZ;
    [ProtoMember(11)] public int LastKnownDimension;
    [ProtoMember(12)] public bool HasLastKnownPosition;
    [ProtoMember(13)] public bool FirstCompanionOwned;
    [ProtoMember(14)] public bool PackExplored;
    [ProtoMember(15)] public bool FirstRequestCompleted;
    [ProtoMember(16)] public List<string> SeenMilestones = new();
    [ProtoMember(17)] public List<BrambleObservedCompanionState> ObservedCompanionStates = new();
    [ProtoMember(18)] public long LastHintUtcMs;
    [ProtoMember(19)] public long LastAmbientUtcMs;
    [ProtoMember(20)] public long LastGreetingUtcMs;
    [ProtoMember(21)] public long LastTaxesUtcMs;
    [ProtoMember(22)] public string LastOnboardingChoice = string.Empty;
    [ProtoMember(23)] public bool SpeciesChosen;
    [ProtoMember(24)] public bool FirstTamingStarted;
    [ProtoMember(25)] public long LastWildTargetEntityId;
    [ProtoMember(26)] public bool PackCartExplored;
    [ProtoMember(27)] public bool TalentsExplored;
    [ProtoMember(28)] public bool Dismissed;
    [ProtoMember(29)] public string HomeType = string.Empty;
    [ProtoMember(30)] public int HomeX;
    [ProtoMember(31)] public int HomeY;
    [ProtoMember(32)] public int HomeZ;
    [ProtoMember(33)] public int HomeDimension;
}

[ProtoContract]
public sealed class BrambleObservedCompanionState
{
    [ProtoMember(1)] public string FoxId = string.Empty;
    [ProtoMember(2)] public bool Juvenile;
    [ProtoMember(3)] public bool Mortal;
    [ProtoMember(4)] public bool Wounded;
    [ProtoMember(5)] public bool VeryHungry;
    [ProtoMember(6)] public bool HadRequest;
    [ProtoMember(7)] public bool HadTalentPoint;
}
