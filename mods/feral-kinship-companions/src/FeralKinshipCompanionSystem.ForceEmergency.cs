#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private const string ForceEmergencyMode = "Emergency force rebuild";
    private const string ForceUndoMode = "Emergency force undo";
    private sealed class ForceCreationException : Exception { internal ForceCreationException(Exception inner) : base(inner.Message, inner) { } }
    private const string ForceBodyIdentityKey = "feralKinshipForceEscrowBody";
    private sealed class ForceBodyIdentity { internal string Key = Guid.NewGuid().ToString("N"); }
    private readonly ConditionalWeakTable<Entity, ForceBodyIdentity> forceBodyIdentities = new();
    private string ForceBodyKey(Entity e) => forceBodyIdentities.GetValue(e, body => new ForceBodyIdentity {
        Key = body.WatchedAttributes.GetString(ForceBodyIdentityKey) is { Length: > 0 } saved ? saved : Guid.NewGuid().ToString("N") }).Key;
    private EmergencyCompanionEscrow? ForceOriginalPresentation(EmergencyCompanionJournal j, Entity e)
    {
        var entries = j.Cargo.Where(x => x.NativeId == e.EntityId && (x.Kind == "original before force" || x.Kind == "late original")).ToArray();
        var exact = entries.FirstOrDefault(x => x.BodyKey == ForceBodyKey(e));
        return exact ?? (entries.Select(x => x.BodyKey).Distinct().Count() == 1 ? entries.FirstOrDefault() : null);
    }
    private static bool ForceMode(string mode) => mode is ForceEmergencyMode or ForceUndoMode;
    private static string ForceEpoch(FoxPackRecordV2 r) => LastEmergency(r) is { Version: 2 or 3 } j ? j.Id : string.Empty;
    private bool ForceSuperseded(FoxPackRecordV2 r, string epoch) => packRepository?.TryGetRecord(r.FoxId, out var current) == true
        && current != null && ForceEpoch(current) != epoch;
    private static void ForceNote(EmergencyCompanionJournal j, string text) { if (!j.Unresolved.Contains(text)) j.Unresolved.Add(text); }

    private void PreviewForceEmergency(IServerPlayer actor, string owner, FoxPackRecordV2 r, bool undo)
    {
        var entries = new List<CompanionRecoveryPreviewEntry> {
            new() { Name = undo ? "Undo" : "Force reconstruction", Decision = "Admin confirmation required", Reason = undo
                ? "Restore the prior per-animal ledger and one intended original generation with its actual native ID. Other bodies and cargo remain separately held. If the intended body is unloaded or needs selection, Undo stays pending and keeps the current replacement active."
                : "Create a fresh animal from the exact saved species beside you. Identity disagreements, incomplete checkpoints, unknown originals and conflicting history do not block this action. Old bodies and uncertain cargo remain separate." },
            new() { Name = "Saved species", Decision = "Exact code", Reason = string.IsNullOrWhiteSpace(r.EntityCode) ? "unavailable; creation will report a concrete missing-definition failure" : r.EntityCode },
            new() { Name = "Available record", Decision = "Recovered fields", Reason = $"Owner, companion ID, name, number, available progression, commands, food and health. Saved native ID {r.EntityId} is replaced; no original identity match is required." },
            new() { Name = "Missing native data", Decision = "Defaulted fields", Reason = "Fresh species appearance, growth and native/provider inventory defaults. Missing/invalid record values use defaults. Active requests, journeys, deliveries and pregnancy are reset without replaying rewards." },
            new() { Name = "Original cargo", Decision = "Separate escrow", Reason = "Original native inventory is UNKNOWN until captured. Available ledger, old bodies, checkpoints and cargo are preserved for Undo and inspection; uncertain cargo is never copied into the fresh body." }
        };
        string mode = undo ? ForceUndoMode : ForceEmergencyMode;
        string token = Guid.NewGuid().ToString("N");
        recoveryUiConfirmations[actor.PlayerUID] = new(actor, token, owner, serverApi!.World.ElapsedMilliseconds + 60000, false,
            new[] { new RecoveryUiTarget(r.FoxId, r.EntityId, null, string.Empty, mode) }, 0);
        serverChannel?.SendPacket(new CompanionRecoveryStatePacket { TargetOwnerUid = owner, PreviewTitle = mode, PreviewToken = token,
            PreviewEntries = entries, Message = "ADMIN FORCE ACTION. Review the defaults and separate cargo, then confirm once within 60 seconds. A persistent per-animal Undo snapshot is saved before mutation. " + (undo ? EmergencyReport(r) : "Creation failures and unresolved old data will be reported afterward.") }, actor);
    }

    // Serialize all available ledger fields without recursively embedding prior
    // emergency journals. Those remain append-only on the live record.
    private static byte[] ForceLedgerBytes(FoxPackRecordV2 r)
    {
        var copy = new FoxPackRecordV2();
        foreach (var field in typeof(FoxPackRecordV2).GetFields(BindingFlags.Public | BindingFlags.Instance)) field.SetValue(copy, field.GetValue(r));
        // Retained journey receipts are protobuf properties, rather than
        // fields. Preserve them in the backup even though the new body clears
        // its active journey. Only the live history is excluded for recursion.
        foreach (var property in typeof(FoxPackRecordV2).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0
                && property.GetCustomAttribute<ProtoMemberAttribute>() != null)
                property.SetValue(copy, property.GetValue(r));
        copy.EmergencyRebuilds = new();
        using var bytes = new MemoryStream(); Serializer.Serialize(bytes, copy); return bytes.ToArray();
    }

    private void ForceCheckpoint(FoxPackRecordV2 r, EmergencyCompanionJournal j, string? stage = null)
    {
        // Historical integrity/size/scan diagnostics never gate this path. A real
        // storage failure is different: do not mutate without a durable Undo.
        string prior = j.Stage;
        if (stage != null) j.Stage = stage;
        j.Revision++;
        try {
            if (packRepository?.CanCheckpointRecovery != true) throw new IOException("Undo snapshot write cannot complete during an open save batch; retry is pending");
            packRepository.Save();
        }
        catch {
            // Retain nonterminal intent after an uncertain write. Saving an
            // error must never overwrite a possibly persisted Undo approval
            // with the previous Rebuilt stage. Terminal phases remain pending
            // until their write is confirmed and can be replayed safely.
            if (stage is "Rebuilt" or "Undone" or "ForceFailed") j.Stage = prior;
            throw;
        }
        emergencyCheckpointedRevisions[j.Id] = j.Revision;
        if (stage != null) LogRecoveryAttempt(r.FoxId, "force-emergency-" + stage, $"operation={j.Id}; old-native={j.OriginalId}; new-native={j.TargetId}; unresolved={string.Join("; ",j.Unresolved)}");
    }

    private IEnumerable<Entity> ForceBodies(FoxPackRecordV2 r, EmergencyCompanionJournal j)
    {
        var all = new HashSet<Entity>(ReferenceEqualityComparer.Instance);
        void Add(IEnumerable<Entity> bodies, int limit, string source) {
            try { int n = 0; foreach (var e in bodies) { if (++n > limit) { ForceNote(j, source + " scan incomplete; late bodies will be held when observed"); break; } if (e != null) all.Add(e); } }
            catch (Exception ex) { ForceNote(j, source + " lookup unavailable: " + ex.Message); }
        }
        try { var direct = serverApi!.World.GetEntityById(j.OriginalId); if (direct != null) all.Add(direct); } catch (Exception ex) { ForceNote(j, "Old native ID lookup: " + ex.Message); }
        Add(serverApi!.World.LoadedEntities.Values, 131072, "world-loaded");
        Add(loadedFoxes.Values, 131072, "registered"); Add(retainedAdmissionBodies.Values, 131072, "observed");
        try {
            int n = 0;
            foreach (var chunk in serverApi.WorldManager.AllLoadedChunks.Values) {
                if (++n > 32768) { ForceNote(j, "Loaded-chunk scan incomplete; absence is unverified"); break; }
                if (!chunk.Disposed) Add(chunk.Entities.Take(chunk.EntitiesCount), 131072, "loaded-chunk");
            }
        } catch (Exception ex) { ForceNote(j, "Loaded-chunk lookup unavailable: " + ex.Message); }
        foreach (var e in all) {
            if (EmergencyIdentity(e, r)) yield return e;
            else if (e.EntityId == j.OriginalId) ForceNote(j, "Old native ID is occupied by a different owner/companion; that unrelated body is untouched");
        }
    }

    private EmergencyCompanionEscrow ForceCapture(FoxPackRecordV2 r, EmergencyCompanionJournal j, Entity e, string kind)
    {
        string key = ForceBodyKey(e);
        var existing = j.Cargo.FirstOrDefault(x => x.BodyKey == key && x.NativeId == e.EntityId && x.Kind == kind);
        if (existing != null) return existing;
        var entry = new EmergencyCompanionEscrow { NativeId = e.EntityId, BodyKey = key, Kind = kind, EntityCode = e.Code?.ToString() ?? string.Empty,
            LedgerBackpack = r.BackpackItem?.ToArray() ?? Array.Empty<byte>(), PriorAway = GetDomesticationStatus(e)?.GetBool(ExpeditionAwayKey) == true, PriorState = (int)e.State };
        // Capture reversible presentation even when a provider refuses ToBytes.
        try {
            using var marker = new MemoryStream(); using var w = new BinaryWriter(marker);
            RetainedNativeSuspension.Marker(e)?.ToBytes(w); entry.PriorMarker = marker.ToArray();
            entry.PriorBoxes = new[] { e.CollisionBox, e.OriginCollisionBox, e.SelectionBox, e.OriginSelectionBox }
                .SelectMany(b => b == null ? new float[6] : new[] { b.X1, b.Y1, b.Z1, b.X2, b.Y2, b.Z2 }).ToArray();
        } catch (Exception ex) { ForceNote(j, $"Body {e.EntityId} presentation snapshot incomplete: {ex.Message}"); }
        try { using var native = new MemoryStream(); using var w = new BinaryWriter(native); e.ToBytes(w, false); entry.Native = native.ToArray(); }
        catch (Exception ex) { ForceNote(j, $"Body {e.EntityId} native cargo snapshot unavailable: {ex.Message}; live body retained without inventory changes"); }
        entry.Sha256 = EmergencyHash(entry.Native); j.Cargo.Add(entry); return entry;
    }

    private void StartForceEmergency(IServerPlayer actor, FoxPackRecordV2 r)
    {
        if (!MayRepairCompletedHold(actor) || actor.ConnectionState != EnumClientState.Playing || !DeveloperMayControlOwner(actor, r.OwnerUid)) { SendCompanionRecoveryState(actor, "Connected server admin and selected-owner authorization required"); return; }
        if (LastEmergency(r) is { Version: 2 or 3 } pending && EmergencyPending(pending) && !pending.Stage.StartsWith("Undo", StringComparison.Ordinal)) { AdvanceEmergencyOperation(r); SendCompanionRecoveryState(actor, EmergencyReport(r)); return; }
        var j = new EmergencyCompanionJournal { Version = 3, Id = Guid.NewGuid().ToString("N"), FoxId = r.FoxId, Owner = r.OwnerUid,
            ApprovedBy = actor.PlayerUID, OriginalId = r.EntityId, EntityCode = r.EntityCode ?? string.Empty, CreatedUtcMs = UtcNowMs(), BeforeDeleted = packRepository!.IsPermanentlyDeleted(r.FoxId) };
        if (actor.Entity != null) { j.X = actor.Entity.Pos.X + 1.5; j.Y = actor.Entity.Pos.Y; j.Z = actor.Entity.Pos.Z; j.Dimension = actor.Entity.Pos.Dimension; }
        else ForceNote(j, "Admin body unavailable at approval; creation requires a placement position");
        try { j.BeforeLedger = ForceLedgerBytes(r); }
        catch (Exception ex) {
            ForceNote(j, "Full ledger serialization failed: " + ex.Message + "; Undo retains available identity/progression/cargo fallback");
            j.BeforeLedger = ForceLedgerBytes(new FoxPackRecordV2 { FoxId = r.FoxId, OwnerUid = r.OwnerUid, EntityId = r.EntityId, EntityCode = r.EntityCode ?? string.Empty,
                Name = r.Name ?? string.Empty, Number = r.Number, Level = r.Level, CurrentHealth = r.CurrentHealth, MaxHealth = r.MaxHealth, BackpackItem = r.BackpackItem?.ToArray() ?? Array.Empty<byte>() });
        }
        j.BeforeLedgerHash = EmergencyHash(j.BeforeLedger);
        j.Recovered.Add("saved owner, stable companion ID and exact species code");
        if (!string.IsNullOrWhiteSpace(r.Name)) j.Recovered.Add("saved name"); else j.Defaulted.Add("missing name: native name default");
        if (r.Number > 0) j.Recovered.Add("saved companion number"); else j.Defaulted.Add("missing number: newly allocated companion number");
        if (!string.IsNullOrWhiteSpace(r.Personality)) j.Recovered.Add("saved personality"); else j.Defaulted.Add("missing personality: native personality default");
        if (r.Level > 0) j.Recovered.Add($"saved level {r.Level}, available XP and talents"); else j.Defaulted.Add("missing level: level 1, available nonnegative XP/talents");
        if (float.IsFinite(r.CurrentHealth) && r.CurrentHealth > 0 && float.IsFinite(r.MaxHealth) && r.MaxHealth > 0) j.Recovered.Add($"saved health {r.CurrentHealth}/{r.MaxHealth}");
        else j.Defaulted.Add("missing/dead/invalid health: living full native health");
        if (r.FoodStateInitialized && float.IsFinite(r.FoodLevel)) j.Recovered.Add("saved food level"); else j.Defaulted.Add("missing food state: species defaults");
        j.Recovered.Add("available saved points, request history and command/duty settings; invalid negative values normalized");
        j.Defaulted.Add("fresh appearance and provider/native inventory; active requests, delivery, pregnancy and journey reset; beside approving admin");
        ForceNote(j, "Unknown original cargo is never inferred or merged; unloaded old bodies remain unresolved until observed");
        foreach (var e in ForceBodies(r, j)) ForceCapture(r, j, e, "original before force");
        foreach (var trip in packRepository.GetActiveExpeditions().Where(t => t.OwnerUid == r.OwnerUid && t.SelectedFoxIds.Contains(r.FoxId))) j.DetachedTrips.Add(trip.ExpeditionId);
        var beforeBody = r.EmergencyBody is { } authority ? RecoveryBodies(r, j).FirstOrDefault(e => AuthorityMatches(r, authority, e)) : LegacyEmergencyChoice(r, j);
        if (beforeBody != null) { j.BeforeActiveId = beforeBody.EntityId; j.BeforeBodyKey = ForceBodyKey(beforeBody); j.BeforeBodyMarker = beforeBody.WatchedAttributes.GetString(EmergencyBodyKey) ?? string.Empty; }
        r.EmergencyRebuilds.Add(j);
        try { ForceCheckpoint(r, j); SendCompanionRecoveryState(actor, "Force reconstruction accepted; Undo checkpoint saved. " + EmergencyReport(r)); }
        catch (Exception ex) { SetEmergencyError(r, j, "Undo snapshot write failed: " + ex.Message); SendCompanionRecoveryState(actor, EmergencyReport(r)); }
    }

    private FoxPackRecordV2 ForceWorking(FoxPackRecordV2 r, EmergencyCompanionJournal j)
    {
        var work = EmergencyWorkingRecord(r, j, force: true);
        if (j.SimpleRebuild) RecoverSimpleNativeFields(work, r, j);
        if (j.SimpleRebuild) ResetSimpleRebuildRecord(work, r);
        work.EmergencyBody = new() { Operation = j.Id, NativeId = j.TargetId, BodyKey = j.ReplacementKey, Marker = j.Id + ":force", Phase = "Spawning" };
        work.NativeLinks = new(); // Original pending journals remain in BeforeLedger.
        work.PendingDepartureStatus = work.PendingReturnStatus = string.Empty;
        work.PendingDepartureAtUtcMs = work.PendingDepartureDeadlineUtcMs = work.PendingReturnAtUtcMs = 0;
        work.PendingDepartureMoment = false;
        work.Level = Math.Max(1, work.Level);
        if (!float.IsFinite(work.FoodLevel)) { work.FoodStateInitialized = false; work.FoodLevel = 1; }
        if (!work.DurableStateInitialized) ForceNote(j, "Durable-state marker incomplete; available saved progression used, missing values defaulted");
        work.DurableStateInitialized = true;
        work.HealthState = 0; work.MortallyWoundedStartHours = 0;
        work.LastKnownDimension = j.Dimension;
        return work;
    }

    private bool IsForceBody(Entity e, FoxPackRecordV2 r, EmergencyCompanionJournal j) => EmergencyIdentity(e, r)
        && e.WatchedAttributes.GetString(EmergencyBodyKey) == j.Id + ":force" && ExactRecoveryCodeEquals(j.EntityCode, e.Code)
        && (j.ReplacementKey.Length == 0 || ForceBodyKey(e) == j.ReplacementKey);

    private void ForceHold(FoxPackRecordV2 r, EmergencyCompanionJournal j, Entity e, string kind)
    {
        if (r.EmergencyBody is { } active && AuthorityMatches(r, active, e)) {
            if (active.NativeId == e.EntityId && ReferenceEquals(serverApi!.World.GetEntityById(e.EntityId), e)) return;
            // A copied generation token on another native object is not the
            // selected canonical object. Give its separate escrow a separate key.
            forceBodyIdentities.GetValue(e, _ => new()).Key = Guid.NewGuid().ToString("N");
        }
        ForceCapture(r, j, e, kind);
        ForceCheckpoint(r, j); // Never change native inventory or delete this body.
        bool currentIdConflict = e.EntityId == j.TargetId && j.Stage != "UndoPrepared" && j.Stage != "Undone"
            && (r.EmergencyBody == null || r.EmergencyBody.NativeId == j.TargetId);
        if (!currentIdConflict) {
            r.RecoveryRetiredNativeIds = r.RecoveryRetiredNativeIds.Append(e.EntityId).Distinct().ToArray();
            if (!j.RetiredIds.Contains(e.EntityId)) j.RetiredIds.Add(e.EntityId);
        } else ForceNote(j, "A separate old object claims the replacement ID; held separately without retiring the canonical replacement");
        ForceCheckpoint(r, j);
        e.WatchedAttributes.SetString(ForceBodyIdentityKey, ForceBodyKey(e));
        RetainedNativeSuspension.Hold(e, EmergencyQuarantineReason);
        if (loadedFoxes.TryGetValue(e.EntityId, out var registered) && ReferenceEquals(registered, e)) loadedFoxes.Remove(e.EntityId);
        if (retainedAdmissionBodies.TryGetValue(e.EntityId, out var observed) && ReferenceEquals(observed, e)) retainedAdmissionBodies.Remove(e.EntityId);
    }

    private bool GuardForceEmergency(Entity e) => GuardEmergencyLifecycle(e);

    private void AdvanceForceEmergency(FoxPackRecordV2 r, EmergencyCompanionJournal j)
    {
        if (r.OwnerUid != j.Owner || r.FoxId != j.FoxId) { SetEmergencyError(r, j, "Operation owner/companion changed; preserve snapshot for authorized admin"); return; }
        try {
            if (!emergencyCheckpointedRevisions.TryGetValue(j.Id, out long rev) || rev != j.Revision) ForceCheckpoint(r, j);
            if (j.Stage is "UndoPrepared" or "UndoWaitingBody") { CompleteForceUndo(r, j); return; }
            if (j.Stage == "Prepared") {
                // No diagnostic proof of necessity, source identity or old-body
                // publication is consulted. Allocation/type/factory are actual
                // creation operations performed only after confirmed consent.
                j.TargetId = serverApi!.WorldManager.GetNextUniqueId();
                if (j.TargetId <= 0) throw new InvalidOperationException("Native ID allocator returned a nonpositive ID");
                j.ProvisionalIds.Add(j.TargetId);
                ForceCheckpoint(r, j, "ForceCreating");
            }
            if (j.Stage == "ForceCreating") {
                var existing = serverApi!.World.GetEntityById(j.TargetId);
                if (existing == null || !IsForceBody(existing, r, j)) existing = RecoveryBodies(r, j).FirstOrDefault(e => IsForceBody(e, r, j));
                Entity? entity = existing != null && IsForceBody(existing, r, j) ? existing : null;
                if (entity == null) {
                    if (j.SpawnAttempted || existing != null) {
                        // An interrupted publication may still arrive later.
                        // Never reuse its ID or clone its uncertain cargo.
                        if (!j.RetiredIds.Contains(j.TargetId)) j.RetiredIds.Add(j.TargetId);
                        j.TargetId = serverApi.WorldManager.GetNextUniqueId(); j.ProvisionalIds.Add(j.TargetId); j.SpawnAttempted = false;
                        if (j.TargetId <= 0 || serverApi.World.GetEntityById(j.TargetId) != null) throw new InvalidOperationException("Native ID allocator failed to supply an unused creation ID");
                        ForceCheckpoint(r, j);
                    }
                    try {
                        if (string.IsNullOrWhiteSpace(j.EntityCode)) throw new InvalidOperationException("Saved entity code is unavailable; exact species definition cannot be created");
                        var type = serverApi.World.GetEntityType(new AssetLocation(j.EntityCode)) ?? throw new InvalidOperationException("Exact saved entity code is not registered: " + j.EntityCode);
                        if (!ExactRecoveryCodeEquals(j.EntityCode, type.Code)) throw new InvalidOperationException("Registered definition returned a different species code: " + type.Code);
                        entity = serverApi.World.ClassRegistry.CreateEntity(type) ?? throw new InvalidOperationException("Native factory returned null for " + j.EntityCode);
                        if (j.SimpleRebuild && j.BeforeNative.Length > 0) {
                            try { entity = ReconstructRecoveryNative(type, j.BeforeNative); }
                            catch (Exception ex) {
                                if (!j.SimpleEmergency) throw;
                                ForceNote(j, "Native snapshot no longer readable; fresh defaults used: " + ex.Message);
                            }
                        }
                        if (j.SimpleRebuild) ResetSimpleRebuildBody(entity);
                    } catch (Exception ex) { throw new ForceCreationException(ex); }
                    j.ReplacementKey = Guid.NewGuid().ToString("N");
                    entity.WatchedAttributes.SetString(ForceBodyIdentityKey, j.ReplacementKey);
                    entity.EntityId = j.TargetId;
                    var status = GetDomesticationStatus(entity, true)!;
                    status.SetString("owner", j.Owner); status.SetString(FoxIdKey, j.FoxId); status.SetString("domesticationLevel", "DOMESTICATED"); status.SetFloat("obedience", 1);
                    entity.WatchedAttributes.SetString(EmergencyBodyKey, j.Id + ":force");
                    entity.Pos.Dimension = j.Dimension; entity.Pos.SetPos(j.X, j.Y, j.Z);
                    entity.PositionBeforeFalling.Set(j.X, j.Y, j.Z);
                    var work = ForceWorking(r, j);
                    // Detach only this owner's animal; never replay/cancel a
                    // whole trip or alter the other members/rewards.
                    foreach (var trip in packRepository!.GetActiveExpeditions().Where(t => t.OwnerUid == j.Owner && j.DetachedTrips.Contains(t.ExpeditionId))) trip.SelectedFoxIds.RemoveAll(x => x == j.FoxId);
                    packRepository.RestoreRecord(work); r = work;
                    packRepository.SetForceReconstructedIdentity(r.FoxId, false);
                    j.SpawnAttempted = true; ForceCheckpoint(r, j);
                    var spawn = new EmergencySpawn(r, j);
                    emergencySpawns[entity] = spawn;
                    try {
                        serverApi.World.SpawnEntity(entity); BindEmergencySpawn(entity, spawn);
                        if (!ReferenceEquals(serverApi.World.GetEntityById(entity.EntityId), entity))
                            throw new InvalidOperationException("SpawnEntity returned without publishing the approved body in the native world index; no successful reconstruction is claimed");
                    }
                    catch (Exception ex) { BindEmergencySpawn(entity, spawn); throw new ForceCreationException(ex); }
                    finally { emergencySpawns.Remove(entity); }
                }
                // Finalization may be retried against the same operation marker.
                ForcePublish(r, j, entity);
            }
            if (j.Stage == "ForceHolding") {
                foreach (var old in ForceBodies(r, j).Where(e => !IsForceBody(e, r, j))) {
                    try { if (j.SimpleRebuild) RetireSimpleRebuildBody(r, j, old); else ForceHold(r, j, old, "late original"); }
                    catch (Exception ex) { ForceNote(j, $"Body {old.EntityId} remains unresolved: {ex.Message}; replacement retained"); }
                }
                ResolveEmergencyTransientNotes(j);
                ForceCheckpoint(r, j, "Rebuilt");
                NotifyForceResult(r, j);
            }
        } catch (Exception ex) {
            // Spawn may throw after publication. Keep the existing generation
            // and resume finalization instead of creating another copy.
            var published = serverApi?.World.GetEntityById(j.TargetId);
            j.Error = ex.GetType().Name + ": " + ex.Message;
            if (published == null || !IsForceBody(published, r, j)) {
                if (j.Stage == "ForceCreating" && ex is ForceCreationException) { try { ForceCheckpoint(r, j, "ForceFailed"); } catch { /* retry persistence, never erase pre-change data */ } }
            }
            LogRecoveryAttempt(r.FoxId, "force-emergency-failure", j.Error);
            NotifyForceResult(r, j);
        }
    }

    private void ForcePublish(FoxPackRecordV2 r, EmergencyCompanionJournal j, Entity e)
    {
        BindEmergencySpawn(e, new EmergencySpawn(r, j));
        var status = GetDomesticationStatus(e, true)!;
        status.SetInt(NumberKey, r.Number);
        if (!string.IsNullOrWhiteSpace(r.Name)) { var name = e.WatchedAttributes.GetTreeAttribute("nametag") ?? new TreeAttribute(); name.SetString("name", r.Name); e.WatchedAttributes.SetAttribute("nametag", name); e.WatchedAttributes.MarkPathDirty("nametag"); }
        try {
            HydrateFoxFromLedger(e, status, r);
            if (j.SimpleRebuild) {
                ResetSimpleRebuildBody(e);
                if (j.BeforeNative.Length == 0) HydrateCompanionBackpackFromRecord(e, status, r);
            }
            EnsureFoxPersonality(e);
            status.SetBool(ExpeditionAwayKey, false); e.WatchedAttributes.RemoveAttribute(RetainedNativeSuspension.Key);
            ApplyFoxDerivedStats(e, preserveCurrentHealth: true);
            var h = e.GetBehavior<EntityBehaviorHealth>();
            if (h != null) {
                if (float.IsFinite(r.MaxHealth) && r.MaxHealth > 0) {
                    // Retain the saved maximum in serialized native health,
                    // including across reload; no species substitution.
                    float difference = r.MaxHealth - h.MaxHealth;
                    if (Math.Abs(difference) > .001f) { h.BaseMaxHealth = Math.Max(.01f, h.BaseMaxHealth + difference); h.UpdateMaxHealth(); }
                }
                h.Health = float.IsFinite(r.CurrentHealth) && r.CurrentHealth > 0 ? Math.Clamp(r.CurrentHealth, .01f, h.MaxHealth) : h.MaxHealth;
                r.CurrentHealth = h.Health; r.MaxHealth = h.MaxHealth; h.MarkDirty();
            } else ForceNote(j, "Native health behavior unavailable; fresh species health defaults remain");
        } catch (Exception ex) { ForceNote(j, "Available saved-field hydration incomplete: " + ex.Message + "; fresh native defaults retained"); }
        // The replacement exists even if a provider cannot hydrate every field.
        // Registration is direct, rather than another normal recovery preflight.
        e.Alive = true; ActivateEmergencyBody(e);
        RetainedNativeSuspension.AdmitPresent(e);
        try { e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.RefreshExpeditionSuspension(); }
        catch (Exception ex) { ForceNote(j, "Fresh body presentation refresh incomplete: " + ex.Message); }
        ledgerHydratedEntities.Add(e.EntityId); retainedLoadedHealthApplied.Add(e);
        loadedFoxes[e.EntityId] = e; retainedAdmissionBodies[e.EntityId] = e; ForgetPendingCompanionAdmission(e);
        r.EntityId = j.TargetId; r.EntityCode = j.EntityCode; r.Status = "Present"; r.RecoveryStatus = CompanionRecoveryStatus.Recoverable;
        r.RecoveryRetiredNativeIds = r.RecoveryRetiredNativeIds.Where(x => x != j.TargetId).Concat(j.RetiredIds).Distinct().ToArray();
        r.EmergencyBody!.Phase = "Ready";
        if (j.SimpleRebuild && j.BeforeNative.Length > 0) {
            var backpack = e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.CompanionBackpackStack
                ?? e.WatchedAttributes.GetItemstack(CompanionBackpackItemKey);
            r.BackpackItem = backpack?.ToBytes() ?? Array.Empty<byte>();
        }
        ResolveEmergencyTransientNotes(j);
        ForceCheckpoint(r, j, "ForceHolding");
    }

    private void StartForceUndo(IServerPlayer actor, FoxPackRecordV2 r)
    {
        var j = LastEmergency(r)!;
        if (j.Stage == "Undone") { SendCompanionRecoveryState(actor, "This force operation is already undone; escrow retained"); return; }
        try {
            if (j.Stage != "UndoPrepared") {
                j.UndoDisplacedLedger = ForceLedgerBytes(r); j.UndoDisplacedLedgerHash = EmergencyHash(j.UndoDisplacedLedger); j.UndoDisplacedId = r.EntityId;
                foreach (var e in ForceBodies(r, j).Where(e => j.GenerationIds.Contains(e.EntityId))) ForceCapture(r, j, e, "replacement before undo");
                ForceCheckpoint(r, j, "UndoPrepared");
            }
            AdvanceEmergencyOperation(r); SendCompanionRecoveryState(actor, EmergencyReport(r));
        } catch (Exception ex) { SetEmergencyError(r, j, "Undo write/restore failed: " + ex.Message); SendCompanionRecoveryState(actor, EmergencyReport(r)); }
    }

    private void CompleteForceUndo(FoxPackRecordV2 r, EmergencyCompanionJournal j) => CompleteForceUndoBody(r, j);

    private void NotifyForceResult(FoxPackRecordV2 r, EmergencyCompanionJournal j)
    {
        if (serverApi?.World.PlayerByUid(j.ApprovedBy) is IServerPlayer actor) SendCompanionRecoveryState(actor,
            j.SimpleRebuild ? j.Stage == "Rebuilt" ? GetFoxRecordLabel(r) + ": rebuilt. Backup retained."
                : GetFoxRecordLabel(r) + ": " + j.Stage + (j.Error.Length > 0 ? ": " + j.Error : string.Empty) : EmergencyReport(r));
        LogRecoveryAttempt(r.FoxId, "force-emergency-result", EmergencyReport(r));
    }
}
