#nullable enable
using System;
using System.Collections.Generic;
using ProtoBuf;

namespace FeralKinshipCompanions;

[ProtoContract]
public sealed class EmergencyCompanionEscrow
{
    [ProtoMember(1)] public long NativeId;
    [ProtoMember(2)] public string Kind = string.Empty;
    [ProtoMember(3)] public byte[] Native = Array.Empty<byte>();
    [ProtoMember(4)] public string Sha256 = string.Empty;
    [ProtoMember(5)] public byte[] LedgerBackpack = Array.Empty<byte>();
    // Only our quarantine presentation is reversible on a late original.
    // Its live inventory/health/progression are never overwritten with escrow.
    [ProtoMember(6)] public byte[] PriorMarker = Array.Empty<byte>();
    [ProtoMember(7)] public bool PriorAway;
    [ProtoMember(8)] public int PriorState;
    [ProtoMember(9)] public float[] PriorBoxes = Array.Empty<float>();
    [ProtoMember(10)] public string EntityCode = string.Empty;
    [ProtoMember(11)] public string BodyKey = string.Empty;
}

[ProtoContract]
public sealed class EmergencyCompanionJournal
{
    [ProtoMember(1)] public int Version = 1;
    [ProtoMember(2)] public string Id = string.Empty;
    [ProtoMember(3)] public string FoxId = string.Empty;
    [ProtoMember(4)] public string Owner = string.Empty;
    [ProtoMember(5)] public string ApprovedBy = string.Empty;
    [ProtoMember(6)] public string Stage = "Prepared";
    [ProtoMember(7)] public byte[] BeforeLedger = Array.Empty<byte>();
    [ProtoMember(8)] public string BeforeLedgerHash = string.Empty;
    [ProtoMember(9)] public byte[] BeforeNative = Array.Empty<byte>();
    [ProtoMember(10)] public string BeforeNativeHash = string.Empty;
    [ProtoMember(11)] public long OriginalId;
    [ProtoMember(12)] public long TargetId;
    [ProtoMember(13)] public List<long> GenerationIds = new();
    [ProtoMember(14)] public bool SpawnAttempted;
    [ProtoMember(15)] public long AttemptUtcMs;
    [ProtoMember(16)] public double X;
    [ProtoMember(17)] public double Y;
    [ProtoMember(18)] public double Z;
    [ProtoMember(19)] public string EntityCode = string.Empty;
    [ProtoMember(20)] public List<string> Recovered = new();
    [ProtoMember(21)] public List<string> Defaulted = new();
    [ProtoMember(22)] public List<EmergencyCompanionEscrow> Cargo = new();
    [ProtoMember(23)] public byte[] UndoDisplacedLedger = Array.Empty<byte>();
    [ProtoMember(24)] public string UndoDisplacedLedgerHash = string.Empty;
    [ProtoMember(25)] public long UndoDisplacedId;
    [ProtoMember(26)] public long UndoTargetId;
    [ProtoMember(27)] public bool UndoReuseOriginal;
    [ProtoMember(28)] public bool UndoSpawnAttempted;
    [ProtoMember(29)] public byte[] UndoSourceNative = Array.Empty<byte>();
    [ProtoMember(30)] public string UndoSourceHash = string.Empty;
    [ProtoMember(31)] public string Error = string.Empty;
    [ProtoMember(32)] public long CreatedUtcMs;
    [ProtoMember(33)] public string UndoResult = string.Empty;
    [ProtoMember(34)] public EmergencyCompanionEscrow? OriginalPresentation;
    [ProtoMember(35)] public List<long> RetiredIds = new();
    [ProtoMember(36)] public long Revision;
    [ProtoMember(37)] public double UndoX;
    [ProtoMember(38)] public double UndoY;
    [ProtoMember(39)] public double UndoZ;
    // Version 2 is an explicitly confirmed force reconstruction. Legacy version
    // 1 journals retain their original resumable execution and Undo semantics.
    [ProtoMember(40)] public List<string> Unresolved = new();
    [ProtoMember(41)] public int Dimension;
    [ProtoMember(42)] public List<long> DetachedTrips = new();
    [ProtoMember(43)] public bool BeforeDeleted;
    [ProtoMember(44)] public long BeforeActiveId;
    [ProtoMember(45)] public string BeforeBodyKey = string.Empty;
    [ProtoMember(46)] public string BeforeBodyMarker = string.Empty;
    [ProtoMember(47)] public string ReplacementKey = string.Empty;
    [ProtoMember(48)] public List<long> ProvisionalIds = new();
    [ProtoMember(49)] public List<string> ResolvedNotes = new();
    [ProtoMember(50)] public bool SimpleRebuild;
    [ProtoMember(51)] public bool SimpleEmergency;
}

[ProtoContract]
public sealed class EmergencyBodyAuthority
{
    [ProtoMember(1)] public int Version = 1;
    [ProtoMember(2)] public string Operation = string.Empty;
    [ProtoMember(3)] public string BodyKey = string.Empty;
    [ProtoMember(4)] public string Marker = string.Empty;
    [ProtoMember(5)] public long NativeId;
    [ProtoMember(6)] public string Phase = "Selecting";
    [ProtoMember(7)] public string Reason = string.Empty;
    [ProtoMember(8)] public byte[] BeforeRepairLedger = Array.Empty<byte>();
    [ProtoMember(9)] public string BeforeRepairHash = string.Empty;
}

public sealed partial class FoxPackRecordV2
{
    // Append-only history, excluded from each BeforeLedger to avoid recursion.
    // Undo preserves this history and all uncertain cargo instead of replacing it.
    [ProtoMember(162)] public List<EmergencyCompanionJournal> EmergencyRebuilds = new();
    [ProtoMember(164)] public EmergencyBodyAuthority? EmergencyBody;
}
