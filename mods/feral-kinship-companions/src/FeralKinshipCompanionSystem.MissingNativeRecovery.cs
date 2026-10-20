#nullable enable
using System;
using System.IO;
using System.Linq;
using ProtoBuf;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private static string MissingCheckpointLedgerFingerprint(FoxPackRecordV2 r)
    {
        var copy = ClonePackRecordForRebuild(r);
        copy.SaveMigration = null; // The paired migration payload excludes itself.
        return RecoveryUiFingerprint(copy, null) + ":" + string.Join(":", new object[] {
            r.Points, r.LifetimePoints, r.Level, r.CurrentLevelExperience, r.LifetimeExperience, r.BankedTalentPoints,
            r.CurrentHealth.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            r.MaxHealth.ToString("R", System.Globalization.CultureInfo.InvariantCulture), r.HealthState,
            r.PregnancyActive, r.PregnancyStartTotalHours, r.PregnancyDueTotalHours, r.IsJuvenile,
            string.Join(",", r.TalentRanks.OrderBy(x => x.Id, StringComparer.Ordinal).Select(x => x.Id + "=" + x.Rank)) });
    }

    private Entity? PrepareMissingRecoveryCheckpoint(IServerPlayer actor, FoxPackRecordV2 r, out string reason)
    {
        reason = MissingRecoveryDataReason(r);
        var q = r.SaveMigration;
        if (q == null || q.NativeEscrow.Length == 0 || q.LedgerEscrow.Length == 0) return null;
        if (r.Archived || r.MissingContentArchive?.Active == true || r.RecoveryRetiredNativeIds.Length >= 1024)
        { reason = "Restore the archived record/source content first, or inspect the recovery identity history bound"; return null; }
        if (!MayRepairCompletedHold(actor)) { reason = "Missing-original checkpoint reconstruction requires server admin privilege"; return null; }
        if (r.MissingRecoveryState == "Prepared" || r.DeveloperRecoveryNativeEscrow.Length > 0)
        { reason = "An interrupted reconstruction already owns the checkpoint; inspect /companiondiag"; return null; }
        if (q.Version != 1 || q.State != "Prepared" || q.NativeId != r.EntityId || q.Owner != r.OwnerUid
            || q.Trip != r.RetainedTrip || q.Outcome != r.RetainedOutcome || r.RetainedPhase != "Complete" || q.Token.Length != 32)
        { reason = "Native checkpoint is historical, already settled or has different identity/trip authority; it cannot prove the missing inventory"; return null; }
        if (q.NativeEscrow.Length > 8 * 1024 * 1024 || q.LedgerEscrow.Length > 1024 * 1024)
        { reason = "Native/ledger checkpoint exceeds reconstruction bounds"; return null; }
        using var ledgerBytes = new MemoryStream(q.LedgerEscrow, false);
        var saved = Serializer.Deserialize<FoxPackRecordV2>(ledgerBytes);
        if (ledgerBytes.Position != ledgerBytes.Length || MissingCheckpointLedgerFingerprint(saved) != MissingCheckpointLedgerFingerprint(r))
        { reason = "Checkpoint ledger identity, trip, cargo, health or progression differs from the current record; original data preserved"; return null; }
        var type = ResolveRecoveryEntityType(r, out string typeReason);
        if (type == null) { reason = "Missing native entity definition: " + typeReason; return null; }
        var e = ReconstructRecoveryNative(type, q.NativeEscrow);
        e.Api = serverApi!; e.World = serverApi!.World;
        if (e.EntityId != r.EntityId || e.Code?.ToString() != q.Code || GetCompanionOwnerUid(e) != r.OwnerUid
            || GetDomesticationStatus(e)?.GetString(FoxIdKey) != r.FoxId || !e.Alive
            || !float.IsFinite(r.CurrentHealth) || r.CurrentHealth <= 0 || !float.IsFinite(r.MaxHealth) || r.MaxHealth <= 0)
        { reason = "Full native checkpoint owner/identity/code or living-health authority is invalid"; return null; }
        if (RetainedNativeSuspension.Marker(e)?.GetString("phase") != "Hold"
            || RetainedNativeSuspension.Marker(e)?.GetString("reason") != StaleAuthorityHold)
        { reason = "Only an unsettled checkpoint of the verified completed-ledger mismatch Hold may replace a missing original"; return null; }
        PrepareCompletedHoldReplacement(e, e, r); // Detached projection; no outcome/reward callback.
        reason = string.Empty;
        return e;
    }

    private void StartMissingCompanionRecovery(IServerPlayer actor, FoxPackRecordV2 r, Action<RecoveryResult>? completed)
    {
        void Report(string kind, string reason) {
            LogRecoveryAttempt(r.FoxId, "missing-result-" + kind, reason);
            if (completed != null) completed(new(kind, reason)); else SendCompanionRecoveryState(actor, reason);
        }
        if (serverApi == null || packRepository?.CanCheckpointRecovery != true || !CanUseDeveloperTools(actor)
            || !DeveloperMayControlOwner(actor, r.OwnerUid) || !CanLookUpFullRecoveryOriginalUsingAuthority(r, CaptureExpeditionPresentationAuthority(), false))
        { Report("skipped", "Missing-original authority/checkpoint is no longer available; request a new preview"); return; }
        Entity? existing = FindArchiveOriginal(r, out string lookupReason);
        if (existing != null || !IsAbsentRecoveryOriginalReason(lookupReason))
        { Report("skipped", existing != null ? "An original appeared; request a new preview of that body" : lookupReason); return; }
        Entity? replacement;
        try { replacement = PrepareMissingRecoveryCheckpoint(actor, r, out string reason); if (replacement == null) { Report("skipped", reason); return; } }
        catch (Exception ex) { Report("failed", "Detached checkpoint validation failed: " + ex.GetType().Name + "; no body changed"); return; }
        if (actor.Entity == null || actor.Entity.Pos.Dimension != 0)
        { Report("skipped", "An administrator in dimension 0 is required for safe checkpoint placement"); return; }
        var type = serverApi.World.GetEntityType(replacement.Code);
        var position = type == null ? null : FindSafeEntityPosition(actor.Entity.Pos.AsBlockPos, type, null, 8, 3);
        if (position == null) { Report("skipped", "No safe loaded ground is available for checkpoint reconstruction"); return; }
        if (!pendingFullRebuilds.Add(r.FoxId)) { Report("skipped", "Rebuild is already pending"); return; }
        string forceEpoch = ForceEpoch(r);
        bool finished = false;
        long retired = r.EntityId;
        float checkpointHealth = r.CurrentHealth;
        void Finish(string kind, string reason) {
            if (finished) return; finished = true; pendingFullRebuilds.Remove(r.FoxId);
            rebuildingEntityIds.Remove(retired); rebuildingEntityIds.Remove(replacement.EntityId); Report(kind, reason);
        }
        int checks = 0;
        void CheckPublication() {
            if (finished || serverApi == null) return;
            if (ForceSuperseded(r, forceEpoch)) { GuardForceEmergency(replacement); Finish("skipped", "Missing-body publication superseded by confirmed force reconstruction; paired checkpoint retained"); return; }
            try {
                var authority = CaptureExpeditionPresentationAuthority();
                if (!DeveloperMayControlOwner(actor, r.OwnerUid) || !MayRepairCompletedHold(actor) || authority == null
                    || authority.Assigned.Contains(r.FoxId) || authority.Conflicts.Contains(r.FoxId) || authority.NativeConflicts.Contains(r.EntityId)
                    || !packRepository!.TryGetRecord(r.FoxId, out var current) || !ReferenceEquals(current, r))
                    throw new InvalidOperationException("Authority changed during missing-body publication");
                if (!NativeRecoveryPublished(replacement)) {
                    if (++checks >= 20) throw new InvalidOperationException(RecoveryPublicationReason(replacement));
                    serverApi.Event.RegisterCallback(_ => CheckPublication(), 100); return;
                }
                var health = replacement.GetBehavior<EntityBehaviorHealth>() ?? throw new InvalidOperationException("Native health unavailable");
                health.Health = Math.Clamp(checkpointHealth, 0, health.MaxHealth); health.MarkDirty();
                if (!replacement.Alive || health.Health <= 0) throw new InvalidOperationException("Checkpoint did not publish a living animal");
                RetainedNativeSuspension.AdmitPresent(replacement);
                replacement.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.RefreshExpeditionSuspension();
                retainedLoadedHealthApplied.Add(replacement); ledgerHydratedEntities.Add(replacement.EntityId);
                RegisterLoadedFox(replacement);
                if (RetainedNativeSuspension.Protected(replacement) || replacement.CollisionBox?.XSize is not > 0
                    || !loadedFoxes.TryGetValue(replacement.EntityId, out var registered) || !ReferenceEquals(registered, replacement))
                    throw new InvalidOperationException("Reconstructed animal has not completed presentation/admission");
                r.MissingRecoveryState = "Committed";
                r.Status = IsFoxMortallyWounded(replacement) ? "Mortally wounded"
                    : replacement.WatchedAttributes.GetInt(EntityHealthStateKey) == RecoveringHealthState ? "Recovering" : "Present";
                packRepository.Save();
                Finish("recovered", "Fresh body reconstructed from the verified complete native checkpoint; same companion/progression/native cargo. Old native identity is durably quarantined; client rendering needs verification");
            } catch (Exception ex) {
                RetainedNativeSuspension.Hold(replacement, "Missing-body recovery unresolved; paired checkpoint retained");
                Finish("failed", "Checkpoint publication unresolved: " + ex.Message + ". Do not repeat; inspect /companiondiag");
            }
        }
        try {
            // Persist retirement + replacement ID before crossing SpawnEntity.
            // A crash at any later point cannot authorize another replacement.
            var saved = ClonePackRecordForRebuild(r);
            long newId = serverApi.WorldManager.GetNextUniqueId();
            if (newId <= 0 || newId == retired || serverApi.World.GetEntityById(newId) != null) throw new InvalidOperationException("Native ID allocation failed");
            r.MissingRecoveryNativeCheckpoint = r.SaveMigration!.NativeEscrow.ToArray();
            r.MissingRecoveryLedgerCheckpoint = HoldRecoveryRecordBytes(saved);
            r.RecoveryRetiredNativeIds = r.RecoveryRetiredNativeIds.Append(retired).Distinct().ToArray();
            r.MissingRecoveryState = "Prepared"; r.EntityId = newId;
            replacement.EntityId = newId;
            replacement.Pos.SetPos(position.X, position.Y, position.Z);
            replacement.PositionBeforeFalling.Set(position.X, position.Y, position.Z);
            SanitizeCompletedRecovery(replacement, r);
            packRepository.Save();
            rebuildingEntityIds.Add(retired); rebuildingEntityIds.Add(newId);
            ledgerHydratedEntities.Add(newId); RetainedNativeSuspension.DeferAdmission(replacement);
            serverApi.World.SpawnEntity(replacement);
            CheckPublication();
        } catch (Exception ex) {
            RetainedNativeSuspension.Hold(replacement, "Missing-body checkpoint publication interrupted; admin inspection required");
            Finish("failed", "Missing-body reconstruction stopped: " + ex.Message + ". Checkpoints/retirement retained; do not repeat until inspected");
        }
    }
}
