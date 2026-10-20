#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private static void DescribeEmergencyFields(FoxPackRecordV2 before, IList<string> recovered, IList<string> defaulted)
    {
        recovered.Add("owner, companion ID, saved type and family identity");
        recovered.Add("saved points and request history (invalid negative values normalized to zero)");
        if (before.Number > 0) recovered.Add("saved companion number"); else defaulted.Add("missing companion number: new ledger number");
        if (!string.IsNullOrWhiteSpace(before.Name)) recovered.Add("saved name"); else defaulted.Add("missing name: native companion naming default");
        if (!string.IsNullOrWhiteSpace(before.Personality)) recovered.Add("saved personality"); else defaulted.Add("missing personality: native random personality");
        if (before.DurableStateInitialized) recovered.Add("saved level/XP/talents, commands/duties and food level");
        else defaulted.Add("incomplete level/XP/talents: level 1, zero XP and untrained talents");
        if (float.IsFinite(before.CurrentHealth) && before.CurrentHealth > 0 && float.IsFinite(before.MaxHealth) && before.MaxHealth > 0)
            recovered.Add("saved current health, bounded by the initialized native maximum");
        else defaulted.Add("unavailable/dead health: living native full-health default");
        defaulted.Add("body location: safe ground near the approving admin");
        defaulted.Add("native appearance, provider state, age and growth clocks: fresh type defaults");
        defaulted.Add("native inventories/backpack: fresh provider defaults; original cargo stays in separate escrow");
        defaulted.Add("breeding/pregnancy, active request, delivery and retreat: reset");
        defaulted.Add("completed expedition presentation: cleared without replaying outcomes or rewards");
    }

    private long NewEmergencyNativeId()
    {
        long id = serverApi!.WorldManager.GetNextUniqueId();
        if (id <= 0 || EmergencyNative(id, out string reason) != null || reason.Length > 0)
            throw new InvalidOperationException("Emergency native ID allocation is unavailable or conflicting");
        return id;
    }

    private void StartEmergencyRebuild(IServerPlayer actor, FoxPackRecordV2 r)
    {
        string blocker = EmergencyAdminBlocker(actor, r, false);
        if (blocker.Length > 0) { SendCompanionRecoveryState(actor, blocker); return; }
        var old = LastEmergency(r);
        if (EmergencyPending(old)) {
            AdvanceEmergencyOperation(r);
            SendCompanionRecoveryState(actor, "Resumed the same emergency operation; the undo checkpoint was not replaced. " + EmergencyReport(r));
            return;
        }
        var original = FindArchiveOriginal(r, out string lookup);
        if (original == null && !IsAbsentRecoveryOriginalReason(lookup)) { SendCompanionRecoveryState(actor, lookup); return; }
        if (original != null && (!NativeRecoveryPublished(original) || EmergencyBodyBlocker(original).Length > 0 || original.Pos.Dimension != 0))
        { SendCompanionRecoveryState(actor, "Original publication/dimension is not safe: " + RecoveryPublicationReason(original)); return; }
        var type = ResolveEmergencyEntityType(r, out string typeReason);
        if (actor.Entity == null || actor.Entity.Pos.Dimension != 0 || type == null)
        { SendCompanionRecoveryState(actor, "A connected admin and available entity type in dimension 0 are required. " + typeReason); return; }
        var position = FindSafeEntityPosition(actor.Entity.Pos.AsBlockPos, type, null, 8, 3);
        if (position == null) { SendCompanionRecoveryState(actor, "No safe loaded ground for Emergency rebuild"); return; }
        // Serialize every undo authority before mutating either record or body.
        var ledger = EmergencyLedgerBytes(r);
        var presentation = original == null ? null : DescribeEmergencyNative(original, "original at approval");
        var native = presentation?.Native ?? Array.Empty<byte>();
        if (presentation != null) { presentation.Native = Array.Empty<byte>(); presentation.Sha256 = EmergencyHash(presentation.Native); }
        var j = new EmergencyCompanionJournal {
            Id = Guid.NewGuid().ToString("N"), FoxId = r.FoxId, Owner = r.OwnerUid, ApprovedBy = actor.PlayerUID,
            BeforeLedger = ledger, BeforeLedgerHash = EmergencyHash(ledger), BeforeNative = native, BeforeNativeHash = EmergencyHash(native),
            OriginalId = r.EntityId, TargetId = NewEmergencyNativeId(), EntityCode = type.Code.ToString(),
            X = position.X, Y = position.Y, Z = position.Z, CreatedUtcMs = UtcNowMs()
        };
        j.OriginalPresentation = presentation;
        DescribeEmergencyFields(r, j.Recovered, j.Defaulted);
        j.GenerationIds.Add(j.TargetId);
        r.EmergencyRebuilds.Add(j);
        try {
            CheckpointEmergency(r, j); // Approval + complete undo record, before even retirement.
            if (original != null) RetainedNativeSuspension.DeferAdmission(original);
            SendCompanionRecoveryState(actor, "Emergency rebuild checkpoint saved; execution is resumable. " + EmergencyReport(r));
        } catch (Exception ex) { SetEmergencyError(r, j, "Checkpoint pending; native body unchanged: " + ex.Message); SendCompanionRecoveryState(actor, EmergencyReport(r)); }
    }

    private void StartEmergencyUndo(IServerPlayer actor, FoxPackRecordV2 r)
    {
        string blocker = EmergencyAdminBlocker(actor, r, true);
        if (blocker.Length > 0) { SendCompanionRecoveryState(actor, blocker); return; }
        var j = LastEmergency(r)!;
        if (j.Stage.StartsWith("Undo", StringComparison.Ordinal)) {
            AdvanceEmergencyOperation(r); SendCompanionRecoveryState(actor, "Resumed the same Undo; no snapshot was overwritten. " + EmergencyReport(r)); return;
        }
        var before = ReadEmergencyLedger(j.BeforeLedger, j.BeforeLedgerHash);
        if (before.OwnerUid != r.OwnerUid || before.FoxId != r.FoxId) { SendCompanionRecoveryState(actor, "Undo owner/identity checkpoint differs"); return; }
        // Capture replacement state/cargo separately before withdrawing it. It is
        // never merged into the original or discarded by restoring BeforeLedger.
        long displaced = r.EntityId != j.OriginalId ? r.EntityId : j.TargetId;
        var current = EmergencyNative(displaced, out string currentReason);
        if (currentReason.Length > 0) { SendCompanionRecoveryState(actor, currentReason); return; }
        var original = EmergencyNative(j.OriginalId, out string originalReason);
        if (originalReason.Length > 0) { SendCompanionRecoveryState(actor, originalReason); return; }
        if (original != null && (!EmergencyIdentity(original, before) || !NativeRecoveryPublished(original)))
        { SendCompanionRecoveryState(actor, "Late original is conflicting or not canonical; Undo is paused with both inventories retained"); return; }
        if (current != null && (!EmergencyIdentity(current, r) || !NativeRecoveryPublished(current)))
        { SendCompanionRecoveryState(actor, "Replacement native identity/publication is conflicting; Undo is paused"); return; }
        foreach (var body in new[] { current, original }) {
            if (body != null && EmergencyBodyBlocker(body) is { Length: > 0 } bodyBlocker)
            { SendCompanionRecoveryState(actor, bodyBlocker); return; }
        }
        if (current != null) CaptureEmergencyCargo(r, j, current, "replacement before undo", true);
        j.UndoDisplacedLedger = EmergencyLedgerBytes(r); j.UndoDisplacedLedgerHash = EmergencyHash(j.UndoDisplacedLedger);
        j.UndoDisplacedId = displaced;
        j.UndoReuseOriginal = original != null;
        j.UndoSourceNative = j.UndoReuseOriginal ? Array.Empty<byte>() : j.BeforeNative.ToArray();
        j.UndoSourceHash = EmergencyHash(j.UndoSourceNative);
        if (j.UndoReuseOriginal) {
            j.UndoTargetId = j.OriginalId;
            j.UndoResult = "Undo reuses the actual original and preserves its current native inventory; prior snapshots remain separate.";
        } else if (j.UndoSourceNative.Length > 0) {
            var type = ResolveEmergencyEntityType(before, out string reason) ?? throw new InvalidOperationException(reason);
            var detached = ReconstructRecoveryNative(type, j.UndoSourceNative);
            if (detached.Pos.Dimension != 0) throw new InvalidOperationException("Undo snapshot dimension cannot be loaded safely");
            j.UndoX = detached.Pos.X; j.UndoY = detached.Pos.Y; j.UndoZ = detached.Pos.Z;
            j.UndoTargetId = NewEmergencyNativeId(); j.GenerationIds.Add(j.UndoTargetId);
            j.UndoResult = "Undo reconstructs the original full native checkpoint with a new native generation; replacement cargo remains separately escrowed.";
        } else {
            j.UndoTargetId = j.OriginalId;
            j.UndoResult = "Undo restores the original missing-body ledger state. No original native inventory existed in the checkpoint; a late original can load naturally.";
        }
        CheckpointEmergency(r, j, "UndoPrepared");
        if (current != null) RetainedNativeSuspension.DeferAdmission(current);
        SendCompanionRecoveryState(actor, "Undo checkpoint saved; execution is resumable. " + EmergencyReport(r));
    }

    private void RetireEmergencyId(FoxPackRecordV2 r, EmergencyCompanionJournal j, long id)
    {
        if (id <= 0) return;
        if (r.RecoveryRetiredNativeIds.Length >= 1024 && !r.RecoveryRetiredNativeIds.Contains(id)) throw new InvalidOperationException("Retired native identity bound reached");
        r.RecoveryRetiredNativeIds = r.RecoveryRetiredNativeIds.Append(id).Distinct().ToArray();
        if (!j.RetiredIds.Contains(id)) j.RetiredIds.Add(id);
    }

    private void WithdrawEmergencyBody(FoxPackRecordV2 r, EmergencyCompanionJournal j, long id, string kind)
    {
        var e = EmergencyNative(id, out string reason);
        if (reason.Length > 0) throw new InvalidOperationException(reason);
        if (e == null) return; // Durable retirement already protects a later arrival.
        if (!EmergencyIdentity(e, r) || !NativeRecoveryPublished(e) || EmergencyBodyBlocker(e).Length > 0) throw new InvalidOperationException("Native removal is not canonical or cargo work is pending: " + RecoveryPublicationReason(e));
        CaptureEmergencyCargo(r, j, e, kind, true); // Latest native cargo, append-only and saved BEFORE despawn.
        rebuildingEntityIds.Add(id);
        try {
            serverApi!.World.DespawnEntity(e, new EntityDespawnData { Reason = EnumDespawnReason.Removed });
            UnregisterLoadedFox(e, "Emergency recovery native handoff"); ForgetRetainedAdmissionBody(e);
            if (EmergencyNative(id, out reason) != null || reason.Length > 0) throw new InvalidOperationException("Native removal is unconfirmed; escrow retained");
        } finally { rebuildingEntityIds.Remove(id); }
    }

    private FoxPackRecordV2 EmergencyWorkingRecord(FoxPackRecordV2 r, EmergencyCompanionJournal j, bool force = false)
    {
        var work = ReadEmergencyLedger(j.BeforeLedger, j.BeforeLedgerHash);
        work.EmergencyRebuilds = r.EmergencyRebuilds;
        work.RecoveryRetiredNativeIds = r.RecoveryRetiredNativeIds.ToArray();
        work.EntityId = j.TargetId; work.EntityCode = j.EntityCode;
        if (work.Number <= 0) work.Number = packRepository!.AllocateNumber();
        work.Archived = false; if (work.MissingContentArchive != null) work.MissingContentArchive.Active = false;
        work.RetainedTrip = work.RetainedPhase = work.RetainedOutcome = work.RetainedLegacyAuthority = string.Empty;
        work.RetainedLegacyAdmissionHours = 0; work.SaveMigration = null; work.CompletionProof = null;
        work.DeveloperRecoveryNativeEscrow = work.CompletedHoldRecoveryNativeEscrow = work.CompletedHoldRecoveryLedgerEscrow = Array.Empty<byte>();
        work.CompletedHoldRecoveryAudit = string.Empty; work.MissingRecoveryState = string.Empty;
        work.MissingRecoveryNativeCheckpoint = work.MissingRecoveryLedgerCheckpoint = Array.Empty<byte>();
        work.BackpackItem = Array.Empty<byte>(); work.BackpackDeliveryActive = false; work.BackpackDeliveryPhase = string.Empty;
        work.BackpackReturnSnapshotTaken = false;
        work.ActiveRequestId = string.Empty; work.ActiveRequest = "None"; work.RequestProgress = 0; work.RequestQualifiedSinceUtcMs = 0;
        work.RequestExpiresUtcMs = 0; work.RequestedItemCode = string.Empty; work.RequestedItemCount = 0;
        work.PredatorTargetEntityId = 0; work.PredatorTargetKilled = false;
        work.BreedingEnabled = work.PregnancyActive = work.PendingBirth = false;
        work.PregnancyStartTotalHours = work.PregnancyDueTotalHours = 0;
        work.AutomaticRetreatActive = false;
        if (!force && !work.DurableStateInitialized) {
            work.Level = 1; work.CurrentLevelExperience = work.LifetimeExperience = 0; work.BankedTalentPoints = 0; work.TalentRanks.Clear();
        }
        work.AdulthoodExperienceGranted = !work.IsJuvenile; // Do not award adulthood XP again.
        work.HasLastKnownPosition = true; work.LastKnownX = (int)Math.Floor(j.X); work.LastKnownY = (int)Math.Floor(j.Y); work.LastKnownZ = (int)Math.Floor(j.Z); work.LastKnownDimension = 0;
        work.FoodLastUpdateTotalHours = serverApi!.World.Calendar.TotalHours;
        if (!float.IsFinite(work.CurrentHealth) || work.CurrentHealth <= 0 || !float.IsFinite(work.MaxHealth) || work.MaxHealth <= 0) {
            work.CurrentHealth = work.MaxHealth = 0; work.HealthState = 0; work.MortallyWoundedStartHours = 0;
            work.StabilizedWindowAppliedRank = 0; work.StabilizedWindowAdjustedStartHours = work.DeathlessRecoveryAtHours = work.DeathlessFallbackAtHours = -1;
        }
        work.Status = "Emergency rebuild pending";
        return work;
    }

    private bool RequireEmergencyColumn(EmergencyCompanionJournal j, bool undo)
    {
        double x = undo ? j.UndoX : j.X, z = undo ? j.UndoZ : j.Z;
        string key = j.Id + (undo ? ":undo" : ":rebuild");
        if (emergencyColumnLoads.TryGetValue(key, out var probe)) return Volatile.Read(ref probe.Acknowledged) == 1;
        if (emergencyColumnLoads.Count >= 128) throw new InvalidOperationException("Emergency source-column request bound reached");
        probe = new(); emergencyColumnLoads[key] = probe;
        serverApi!.WorldManager.LoadChunkColumnPriority((int)Math.Floor(x / GlobalConstants.ChunkSize), (int)Math.Floor(z / GlobalConstants.ChunkSize),
            new ChunkLoadOptions { KeepLoaded = false, OnLoaded = () => Interlocked.Exchange(ref probe.Acknowledged, 1) });
        return Volatile.Read(ref probe.Acknowledged) == 1;
    }

    private void SpawnEmergencyGeneration(FoxPackRecordV2 r, EmergencyCompanionJournal j, bool undo)
    {
        long target = undo ? j.UndoTargetId : j.TargetId;
        var existing = EmergencyNative(target, out string reason);
        if (reason.Length > 0) throw new InvalidOperationException(reason);
        if (existing != null) {
            if (!EmergencyIdentity(existing, r) || existing.WatchedAttributes.GetString(EmergencyBodyKey) != j.Id + (undo ? ":undo" : ":rebuild"))
                throw new InvalidOperationException("Emergency generation identity/operation conflicts; no native body chosen");
            if (!NativeRecoveryPublished(existing)) throw new InvalidOperationException("Emergency publication pending: " + RecoveryPublicationReason(existing));
            CheckpointEmergency(r, j, undo ? "UndoReady" : "RebuildReady"); return;
        }
        if (!RequireEmergencyColumn(j, undo)) { SetEmergencyError(r, j, "Waiting for the persisted generation's source-column acknowledgement"); return; }
        bool attempted = undo ? j.UndoSpawnAttempted : j.SpawnAttempted;
        if (attempted) {
            if (UtcNowMs() - j.AttemptUtcMs < 2000) return;
            // Never reuse an uncertain native spawn ID. A late copy will enter
            // durable quarantine before it can register or expose its cargo.
            if (j.GenerationIds.Count >= 16) throw new InvalidOperationException("Emergency generation retry bound reached; inspect retained checkpoints");
            RetireEmergencyId(r, j, target);
            target = NewEmergencyNativeId(); j.GenerationIds.Add(target);
            if (undo) j.UndoTargetId = target; else j.TargetId = target;
            r.EntityId = target;
            CheckpointEmergency(r, j);
        }
        Entity entity;
        if (undo) {
            if (EmergencyHash(j.UndoSourceNative) != j.UndoSourceHash) throw new InvalidOperationException("Undo native checkpoint hash mismatch");
            var before = ReadEmergencyLedger(j.BeforeLedger, j.BeforeLedgerHash);
            var type = ResolveEmergencyEntityType(before, out reason) ?? throw new InvalidOperationException(reason);
            entity = ReconstructRecoveryNative(type, j.UndoSourceNative);
        } else {
            var type = serverApi!.World.GetEntityType(new AssetLocation(j.EntityCode)) ?? throw new InvalidOperationException("Emergency entity definition is unavailable");
            entity = serverApi.World.ClassRegistry.CreateEntity(type) ?? throw new InvalidOperationException("Emergency native factory returned null");
            var status = GetDomesticationStatus(entity, true)!;
            status.SetString("owner", r.OwnerUid); status.SetString(FoxIdKey, r.FoxId); status.SetInt(NumberKey, r.Number);
            status.SetString("domesticationLevel", "DOMESTICATED"); status.SetFloat("obedience", 1);
            if (r.Name.Length > 0) { var name = new TreeAttribute(); name.SetString("name", r.Name); entity.WatchedAttributes.SetAttribute("nametag", name); }
        }
        entity.EntityId = target;
        entity.WatchedAttributes.SetString(EmergencyBodyKey, j.Id + (undo ? ":undo" : ":rebuild"));
        entity.Pos.SetPos(undo ? j.UndoX : j.X, undo ? j.UndoY : j.Y, undo ? j.UndoZ : j.Z);
        entity.PositionBeforeFalling.Set(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
        var placementType = entity.Properties ?? serverApi!.World.GetEntityType(new AssetLocation(j.EntityCode))
            ?? throw new InvalidOperationException("Emergency placement type is unavailable");
        if (!IsSafeEntityPosition(entity.Pos.AsBlockPos, entity.Pos.XYZ, placementType, null))
            throw new InvalidOperationException("The persisted emergency/undo location is obstructed; clear or load it before resuming");
        if (undo) j.UndoSpawnAttempted = true; else j.SpawnAttempted = true;
        j.AttemptUtcMs = UtcNowMs();
        CheckpointEmergency(r, j); // Persist intent BEFORE SpawnEntity, including operation/native ID.
        ledgerHydratedEntities.Add(target); RetainedNativeSuspension.DeferAdmission(entity);
        serverApi!.World.SpawnEntity(entity);
    }

    private void PublishEmergencyGeneration(FoxPackRecordV2 r, EmergencyCompanionJournal j, bool undo)
    {
        long target = undo ? j.UndoTargetId : j.TargetId;
        var e = EmergencyNative(target, out string reason);
        if (reason.Length > 0) throw new InvalidOperationException(reason);
        if (e == null) { CheckpointEmergency(r, j, undo ? "UndoSpawning" : "RebuildSpawning"); return; }
        if (!EmergencyIdentity(e, r) || !NativeRecoveryPublished(e)) throw new InvalidOperationException("Emergency body is not canonical for publication");
        emergencyAdmissionAllowed.Add(e);
        try {
            ledgerHydratedEntities.Add(e.EntityId); retainedLoadedHealthApplied.Add(e);
            if (!undo) {
                var status = GetDomesticationStatus(e)!;
                HydrateFoxFromLedger(e, status, r); // No backpack/native inventory replay.
                status.SetBool(ExpeditionAwayKey, false); e.WatchedAttributes.RemoveAttribute(RetainedNativeSuspension.Key);
                var health = e.GetBehavior<EntityBehaviorHealth>() ?? throw new InvalidOperationException("Emergency native health is not initialized");
                ApplyFoxDerivedStats(e, preserveCurrentHealth: true);
                health.Health = r.MaxHealth > 0 && r.CurrentHealth > 0 ? Math.Clamp(r.CurrentHealth, 0, health.MaxHealth) : health.MaxHealth;
                health.MarkDirty();
                RetainedNativeSuspension.AdmitPresent(e);
                e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.RefreshExpeditionSuspension();
                e.State = EnumEntityState.Active;
            } else if (j.UndoReuseOriginal) {
                var presentation = j.Cargo.FirstOrDefault(x => x.NativeId == e.EntityId && x.Kind == "late original or retired generation") ?? j.OriginalPresentation;
                if (presentation == null) throw new InvalidOperationException("Undo cannot identify the original quarantine presentation");
                // Reuse this actual body's current cargo. Restore only our hold.
                CaptureEmergencyCargo(r, j, e, "actual original at undo", true);
                RestoreEmergencyPresentation(e, presentation);
            } else if (!RetainedNativeSuspension.NativeAuthority(e) && GetDomesticationStatus(e)?.GetBool(ExpeditionAwayKey) != true)
                RetainedNativeSuspension.AdmitPresent(e);
            RegisterLoadedFox(e);
            if (!undo && (!e.Alive || RetainedNativeSuspension.Protected(e) || e.CollisionBox?.XSize is not > 0)) throw new InvalidOperationException("Emergency body remains suppressed/unhealthy");
            r.Status = undo ? r.Status : IsFoxMortallyWounded(e) ? "Mortally wounded" : "Present";
            CheckpointEmergency(r, j, undo ? "Undone" : "Rebuilt");
        } catch {
            RetainedNativeSuspension.DeferAdmission(e); throw;
        } finally { emergencyAdmissionAllowed.Remove(e); }
    }

    private void AdvanceEmergencyOperation(FoxPackRecordV2 r)
    {
        var j = LastEmergency(r);
        if (!EmergencyPending(j) || !emergencyProcessing.Add(r.FoxId)) return;
        try {
            if (j!.Version is 2 or 3) { AdvanceForceEmergency(r, j); return; }
            string blocked = EmergencyAuthorityBlocker(r);
            if (blocked.Length > 0) { SetEmergencyError(r, j!, blocked); return; }
            if (r.OwnerUid != j!.Owner || r.FoxId != j.FoxId) throw new InvalidOperationException("Emergency journal owner/identity changed");
            if (!emergencyCheckpointedRevisions.TryGetValue(j.Id, out long revision) || revision != j.Revision)
                CheckpointEmergency(r, j); // Retry an unconfirmed persistence boundary before doing any work.
            switch (j.Stage) {
                case "Prepared":
                    RetireEmergencyId(r, j, j.OriginalId);
                    CheckpointEmergency(r, j, "RebuildRetiring"); break;
                case "RebuildRetiring":
                    WithdrawEmergencyBody(r, j, j.OriginalId, "original before removal");
                    var work = EmergencyWorkingRecord(r, j);
                    packRepository!.RestoreRecord(work);
                    CheckpointEmergency(work, j, "RebuildSpawning"); break;
                case "RebuildSpawning": SpawnEmergencyGeneration(r, j, false); break;
                case "RebuildReady": PublishEmergencyGeneration(r, j, false); break;
                case "UndoPrepared":
                    RetireEmergencyId(r, j, j.UndoDisplacedId);
                    CheckpointEmergency(r, j, "UndoRetiring"); break;
                case "UndoRetiring":
                    WithdrawEmergencyBody(r, j, j.UndoDisplacedId, "replacement withdrawn by undo");
                    var before = ReadEmergencyLedger(j.BeforeLedger, j.BeforeLedgerHash);
                    before.EmergencyRebuilds = r.EmergencyRebuilds;
                    before.RecoveryRetiredNativeIds = r.RecoveryRetiredNativeIds.Concat(j.RetiredIds).Distinct().ToArray();
                    before.EntityId = j.UndoTargetId;
                    if (j.UndoReuseOriginal || j.UndoSourceNative.Length == 0) {
                        before.RecoveryRetiredNativeIds = before.RecoveryRetiredNativeIds.Where(x => x != j.OriginalId).ToArray();
                        j.RetiredIds.Remove(j.OriginalId);
                    }
                    packRepository!.RestoreRecord(before);
                    CheckpointEmergency(before, j, j.UndoReuseOriginal ? "UndoReady" : j.UndoSourceNative.Length > 0 ? "UndoSpawning" : "Undone"); break;
                case "UndoSpawning": SpawnEmergencyGeneration(r, j, true); break;
                case "UndoReady":
                    if (j.UndoReuseOriginal && EmergencyNative(j.UndoTargetId, out _) == null) {
                        // The actual original may simply have unloaded. Never
                        // clone its escrow while it can load naturally.
                        CheckpointEmergency(r, j, "Undone"); break;
                    }
                    PublishEmergencyGeneration(r, j, true); break;
                default: throw new InvalidOperationException("Unknown emergency operation stage; retained unchanged");
            }
        } catch (Exception ex) {
            if (j != null) {
                string error = ex.GetType().Name + ": " + ex.Message;
                if (j.Error != error) serverApi?.Logger.Error("[CompanionEmergency] operation={0} phase={1}: {2}", j.Id, j.Stage, ex);
                SetEmergencyError(r, j, error);
            }
        }
        finally { emergencyProcessing.Remove(r.FoxId); }
    }

    private void MaintainEmergencyRebuilds()
    {
        if (serverApi == null || packRepository?.Loaded != true || !companionContentReady || RuntimeEnv.ServerMainThreadId != Environment.CurrentManagedThreadId) return;
        long now = serverApi.World.ElapsedMilliseconds;
        if (now < nextEmergencyTick) return; nextEmergencyTick = now + 500;
        MaintainEmergencyBodies();
        var rows = packRepository.GetAllRecords().Where(r => r.EmergencyRebuilds.Count > 0).Take(8192).ToArray();
        var pending = rows.Where(r => EmergencyPending(LastEmergency(r))).ToArray();
        if (pending.Length > 0) {
            int start = emergencyTickCursor % pending.Length;
            for (int i = 0; i < Math.Min(8, pending.Length); i++) AdvanceEmergencyOperation(pending[(start + i) % pending.Length]);
            emergencyTickCursor = (start + Math.Min(8, pending.Length)) % pending.Length;
        }
        // Also preserve late inventories after the operation has completed/undone.
        foreach (var e in serverApi.World.LoadedEntities.Values.Where(e => e != null).Take(131072))
            if (IsRetiredRecoveryOriginal(e) || RetainedNativeSuspension.Marker(e)?.GetString("reason") == EmergencyQuarantineReason) GuardEmergencyEntity(e);
    }
}
