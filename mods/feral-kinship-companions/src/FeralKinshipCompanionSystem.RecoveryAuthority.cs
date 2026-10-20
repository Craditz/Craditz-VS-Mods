#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ProtoBuf;
using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;

public sealed partial class FoxPackRecordV2
{
    // Durable quarantine, not a deletion list. A late original keeps its native
    // inventory but must never hydrate/register over the reconstructed animal.
    [ProtoMember(158)] public long[] RecoveryRetiredNativeIds = Array.Empty<long>();
    [ProtoMember(159)] public string MissingRecoveryState = string.Empty;
    [ProtoMember(160)] public byte[] MissingRecoveryNativeCheckpoint = Array.Empty<byte>();
    [ProtoMember(161)] public byte[] MissingRecoveryLedgerCheckpoint = Array.Empty<byte>();
}

public sealed partial class FeralKinshipCompanionSystem
{
    private void LogRecoveryAttempt(string foxId, string stage, string reason)
        => serverApi?.Logger.Notification("[CompanionRebuild] companion={0} stage={1} reason={2}",
            DiagnosticText(foxId, 64), DiagnosticText(stage, 40), DiagnosticText(reason, 500));

    private static void WriteRecoveryBytes(BinaryWriter writer, byte[]? value)
    {
        value ??= Array.Empty<byte>();
        writer.Write(value.Length);
        writer.Write(SHA256.HashData(value));
    }

    private static void WriteRecoveryAuthority(BinaryWriter w, FoxPackRecordV2 r)
    {
        w.Write(r.FoxId); w.Write(r.OwnerUid); w.Write(r.EntityId); w.Write(r.EntityCode);
        w.Write(r.Archived); w.Write(r.MissingContentArchive?.Active == true);
        w.Write(r.RetainedTrip); w.Write(r.RetainedPhase); w.Write(r.RetainedOutcome);
        w.Write(r.RetainedLegacyAuthority); w.Write(r.RetainedLegacyAdmissionHours);
        w.Write(r.PendingDepartureStatus); w.Write(r.PendingDepartureAtUtcMs); w.Write(r.PendingDepartureDeadlineUtcMs);
        w.Write(r.PendingReturnStatus); w.Write(r.PendingReturnAtUtcMs);
        w.Write(r.BackpackDeliveryActive); w.Write(r.BackpackDeliveryPhase);
        w.Write(r.BackpackDeliveryMovedItemCount); w.Write(r.BackpackDeliveryStartingItemCount);
        WriteRecoveryBytes(w, r.BackpackItem);
        WriteRecoveryBytes(w, r.DeveloperRecoveryNativeEscrow);
        WriteRecoveryBytes(w, r.CompletedHoldRecoveryNativeEscrow);
        w.Write(r.CompletedHoldRecoveryAudit);
        bool hasMigrationReceipt = r.SaveMigration != null && (r.SaveMigration.Trip.Length > 0 || r.SaveMigration.Token.Length > 0
            || r.SaveMigration.NativeEscrow.Length > 0 || r.SaveMigration.LedgerEscrow.Length > 0);
        w.Write(hasMigrationReceipt ? r.SaveMigration!.Version : 0); w.Write(r.SaveMigration?.Trip ?? string.Empty);
        w.Write(r.SaveMigration?.Token ?? string.Empty);
        WriteRecoveryBytes(w, r.SaveMigration?.NativeEscrow);
        WriteRecoveryBytes(w, r.SaveMigration?.LedgerEscrow);
        // Attempts, next retry, reason and migration classifier are volatile.
        // Receipt changes still invalidate; normal automatic classification does not.
        using var proof = new MemoryStream();
        if (r.CompletionProof != null) Serializer.Serialize(proof, r.CompletionProof);
        WriteRecoveryBytes(w, proof.ToArray());
        w.Write(r.MissingRecoveryState);
        w.Write(r.NativeLinks.Count);w.Write(LastNativeLink(r)?.Id ?? string.Empty);w.Write(LastNativeLink(r)?.Stage ?? string.Empty);
        w.Write(r.EmergencyRebuilds.Count);
        var emergency = LastEmergency(r);
        w.Write(emergency?.Id ?? string.Empty); w.Write(emergency?.Stage ?? string.Empty);
        w.Write(emergency?.BeforeLedgerHash ?? string.Empty); w.Write(emergency?.UndoDisplacedLedgerHash ?? string.Empty);
        foreach (long id in r.RecoveryRetiredNativeIds.OrderBy(x => x)) w.Write(id);
    }

    private static void WriteRecoveryNativeAuthority(BinaryWriter w, Entity e)
    {
        w.Write(e.EntityId); w.Write(e.Code?.ToString() ?? string.Empty);
        var status = GetDomesticationStatus(e);
        w.Write(status?.GetString("owner", string.Empty) ?? string.Empty);
        w.Write(status?.GetString(FoxIdKey, string.Empty) ?? string.Empty);
        w.Write(status?.GetBool(ExpeditionAwayKey) == true);
        using var marker = new MemoryStream();
        using (var mw = new BinaryWriter(marker, System.Text.Encoding.UTF8, true)) RetainedNativeSuspension.Marker(e)?.ToBytes(mw);
        WriteRecoveryBytes(w, marker.ToArray());
        // Inventory/provider cargo surfaces are stable transaction authority.
        // Sort names so insertion order cannot invalidate an otherwise equal tree.
        w.Write("watched-cargo");
        foreach (var a in e.WatchedAttributes.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (!RecoveryCargoKey(a.Key)) continue;
            w.Write(a.Key); w.Write(a.Value.GetAttributeId()); a.Value.ToBytes(w);
        }
        w.Write("native-cargo");
        foreach (var a in e.Attributes.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (!RecoveryCargoKey(a.Key)) continue;
            w.Write(a.Key); w.Write(a.Value.GetAttributeId()); a.Value.ToBytes(w);
        }
        w.Write("status-cargo");
        if (status != null) foreach (var a in status.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (!RecoveryCargoKey(a.Key)) continue;
            w.Write(a.Key); w.Write(a.Value.GetAttributeId()); a.Value.ToBytes(w);
        }
    }
    private static bool RecoveryCargoKey(string key) => key.Contains("inventory", StringComparison.OrdinalIgnoreCase)
        || key.Contains("backpack", StringComparison.OrdinalIgnoreCase) || key.Contains("cargo", StringComparison.OrdinalIgnoreCase)
        || key.Contains("pickup", StringComparison.OrdinalIgnoreCase);

    private static bool IsAbsentRecoveryOriginalReason(string reason) => reason.Length == 0
        || reason == "Original is not currently registered in the native world.";

    private bool IsRetiredRecoveryOriginal(Entity e)
    {
        if (IsEmergencySelected(e) || emergencySpawns.ContainsKey(e)) return false;
        string id = GetDomesticationStatus(e)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        return packRepository?.Loaded == true && packRepository.TryGetRecord(id, out var r) && r != null
            && (!HasForceHistory(r) || EmergencyIdentity(e, r)) && r.RecoveryRetiredNativeIds.Contains(e.EntityId);
    }
    private bool QuarantineRetiredRecoveryOriginal(Entity e)
    {
        if (GuardEmergencyEntity(e)) return true;
        if (!IsRetiredRecoveryOriginal(e)) return false;
        RetainedNativeSuspension.Hold(e, "Retired recovery original; native inventory preserved for admin inspection, never replayed");
        return true;
    }

    private string MissingRecoveryDataReason(FoxPackRecordV2 r)
    {
        if (r.SaveMigration is { NativeEscrow.Length: > 0, LedgerEscrow.Length: > 0 })
            return "A paired migration checkpoint exists; its identity, trip, inventory and progression must match before reconstruction";
        return "Native body absent after source-column lookup. Missing a paired full native entity/inventory snapshot and ledger checkpoint"
            + $" (developer-native={r.DeveloperRecoveryNativeEscrow.Length}, migration-native={r.SaveMigration?.NativeEscrow.Length ?? 0}, migration-ledger={r.SaveMigration?.LedgerEscrow.Length ?? 0}, manual-native={r.CompletedHoldRecoveryNativeEscrow.Length}, manual-ledger={r.CompletedHoldRecoveryLedgerEscrow.Length}). "
            + "Saved health/progression/backpack fields cannot prove the missing provider inventory; restore the original source chunk or a complete world backup";
    }
}
