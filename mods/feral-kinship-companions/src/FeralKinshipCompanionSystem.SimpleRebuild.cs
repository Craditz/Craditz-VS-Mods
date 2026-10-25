#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private const string SimpleRebuildMode = "Rebuild";
    private const string SimpleEmergencyMode = "Emergency rebuild";
    private static bool SimpleRebuildAction(string mode) => mode is SimpleRebuildMode or SimpleEmergencyMode;

    private void PreviewSimpleRebuild(IServerPlayer actor, string owner, string id, bool all, bool emergency)
    {
        if (!CanUseDeveloperTools(actor) || !DeveloperMayControlOwner(actor, owner)
            || emergency && !MayRepairCompletedHold(actor)) { SendCompanionRecoveryState(actor, "Rebuild permission expired; select the owner's cart again."); return; }
        var rows = packRepository!.GetAllRecords().Where(r => r.OwnerUid == owner && (all || r.FoxId == id)).Take(8193).ToArray();
        if (rows.Length == 0 || rows.Length > 8192) { SendCompanionRecoveryState(actor, "No selected companions, or roster exceeds 8192 animals."); return; }
        string mode = emergency ? SimpleEmergencyMode : SimpleRebuildMode;
        string token = Guid.NewGuid().ToString("N");
        // Bind consent to the selected owner, stable IDs and species only. The
        // operation captures the latest data at execution, not a stale preview.
        recoveryUiConfirmations[actor.PlayerUID] = new(actor, token, owner, serverApi!.World.ElapsedMilliseconds + 60000, all,
            rows.Select(r => new RecoveryUiTarget(r.FoxId, r.EntityId, null, r.EntityCode ?? string.Empty, mode)).ToArray(), 0);
        serverChannel?.SendPacket(new CompanionRecoveryStatePacket {
            TargetOwnerUid = owner, PreviewTitle = mode + (all ? " all" : string.Empty), PreviewToken = token,
            Message = "Rebuild beside you, fully fed and healthy. Clear away/MIA, expedition, pending actions and movement/animation state. Backups and old-body protection are automatic.",
            PreviewEntries = rows.Select(r => new CompanionRecoveryPreviewEntry { Name = GetFoxRecordLabel(r), Decision = mode,
                Reason = emergency ? "Use all available saved data; default missing or invalid fields. Preserve uncertain cargo separately."
                    : "Preserve recorded identity, appearance, progression, relationships and available inventory. An incomplete record can use Emergency rebuild." }).ToList()
        }, actor);
    }

    private string SimpleRebuildPermission(IServerPlayer actor, RecoveryUiTarget target, FoxPackRecordV2 r)
    {
        if (target.Mode == SimpleEmergencyMode && !MayRepairCompletedHold(actor)) return "Emergency rebuild requires server admin privilege";
        return (r.EntityCode ?? string.Empty) == target.Fingerprint ? string.Empty : "Animal type changed; select Rebuild again";
    }

    private EmergencyCompanionJournal? StartSimpleRebuild(IServerPlayer actor, FoxPackRecordV2 r, bool emergency)
    {
        if (!CanUseDeveloperTools(actor) || actor.ConnectionState != EnumClientState.Playing || !DeveloperMayControlOwner(actor, r.OwnerUid)
            || emergency && !MayRepairCompletedHold(actor)) throw new InvalidOperationException("Selected-owner rebuild authorization expired");
        if (!emergency && !r.DurableStateInitialized) throw new InvalidOperationException("Companion record is incomplete; use Emergency rebuild");
        var j = new EmergencyCompanionJournal { Version = 3, SimpleRebuild = true, SimpleEmergency = emergency,
            Id = Guid.NewGuid().ToString("N"), FoxId = r.FoxId, Owner = r.OwnerUid, ApprovedBy = actor.PlayerUID,
            OriginalId = r.EntityId, EntityCode = r.EntityCode ?? string.Empty, CreatedUtcMs = UtcNowMs(), BeforeDeleted = packRepository!.IsPermanentlyDeleted(r.FoxId) };
        if (actor.Entity == null) throw new InvalidOperationException("Operator position unavailable");
        j.X = actor.Entity.Pos.X + 1.5; j.Y = actor.Entity.Pos.Y; j.Z = actor.Entity.Pos.Z; j.Dimension = actor.Entity.Pos.Dimension;
        var bodies = RecoveryBodies(r, j);
        var exact = bodies.Where(e => ExactRecoveryCodeEquals(r.EntityCode, e.Code)).ToArray();
        var source = r.EmergencyBody is { } authority ? exact.FirstOrDefault(e => AuthorityMatches(r, authority, e)) : null;
        source ??= exact.FirstOrDefault(e => e.EntityId == r.EntityId && ReferenceEquals(serverApi!.World.GetEntityById(e.EntityId), e));
        source ??= exact.Length == 1 ? exact[0] : null;
        if (string.IsNullOrWhiteSpace(j.EntityCode) && emergency && bodies.Select(e => e.Code.ToString()).Distinct().Count() == 1) {
            j.EntityCode = bodies[0].Code.ToString(); source = bodies[0];
        }
        // Provider disappearance is a concrete creation failure, never a reason
        // to silently substitute another animal or destroy the current one.
        var type = string.IsNullOrWhiteSpace(j.EntityCode) ? null : serverApi!.World.GetEntityType(new AssetLocation(j.EntityCode));
        if (type == null) throw new InvalidOperationException("Animal definition unavailable: " + j.EntityCode);
        var safe = FindSafeEntityPosition(actor.Entity.Pos.AsBlockPos, type, null, 8, 3);
        if (safe != null) { j.X = safe.X; j.Y = safe.Y; j.Z = safe.Z; }
        else ForceNote(j, "No verified nearby ground; used the operator's nearby placement as a best effort");
        if (source != null) {
            try {
                var bytes = SnapshotRecoveryNative(source);
                ReconstructRecoveryNative(type, bytes); // Prove deserialization before any old-body change.
                j.BeforeNative = bytes; j.BeforeNativeHash = EmergencyHash(bytes);
                j.BeforeActiveId = source.EntityId; j.BeforeBodyKey = ForceBodyKey(source);
                j.BeforeBodyMarker = source.WatchedAttributes.GetString(EmergencyBodyKey) ?? string.Empty;
                j.Recovered.Add("native appearance, equipment and provider inventory");
            } catch (Exception ex) {
                if (!emergency) throw new InvalidOperationException("Native data could not be restored; use Emergency rebuild. " + ex.Message);
                ForceNote(j, "Native snapshot unavailable; saved data and fresh provider defaults used: " + ex.Message);
            }
        }
        if (source == null) {
            // A missing live body may still have a usable full snapshot. Stale
            // journal phase/receipts do not gate it; only the snapshot's actual
            // companion, owner, native ID and animal type must match.
            var snapshots = new[] { r.DeveloperRecoveryNativeEscrow, r.MissingRecoveryNativeCheckpoint,
                r.CompletedHoldRecoveryNativeEscrow, r.SaveMigration?.NativeEscrow ?? Array.Empty<byte>() }
                .Concat(r.EmergencyRebuilds.AsEnumerable().Reverse().SelectMany(h => h.Cargo.AsEnumerable().Reverse()
                    .Where(x => x.NativeId == r.EntityId).Select(x => x.Native)));
            foreach (var bytes in snapshots.Where(x => x.Length > 0)) {
                try {
                    var saved = ReconstructRecoveryNative(type, bytes);
                    if (saved.EntityId != r.EntityId || !EmergencyIdentity(saved, r) || !ExactRecoveryCodeEquals(j.EntityCode, saved.Code)) continue;
                    j.BeforeNative = bytes.ToArray(); j.BeforeNativeHash = EmergencyHash(bytes); j.BeforeActiveId = saved.EntityId;
                    j.Recovered.Add("saved full native snapshot, including appearance and inventory"); break;
                } catch { /* Keep unreadable/other-identity backups in BeforeLedger; use remaining data. */ }
            }
        }
        j.BeforeLedger = ForceLedgerBytes(r); j.BeforeLedgerHash = EmergencyHash(j.BeforeLedger);
        foreach (var body in bodies) ForceCapture(r, j, body, "original before force");
        foreach (var trip in packRepository.GetActiveExpeditions().Where(t => t.OwnerUid == r.OwnerUid && t.SelectedFoxIds.Contains(r.FoxId))) j.DetachedTrips.Add(trip.ExpeditionId);
        j.Recovered.Add("available saved identity, progression, talents, relationships and settings");
        j.Defaulted.Add("full hunger/health; cleared temporary actions, movement and presence state");
        // Append a new intent even if the last operation was interrupted. Its
        // old snapshots remain intact and its callbacks see a superseding epoch.
        r.EmergencyRebuilds.Add(j);
        ForceCheckpoint(r, j); // Actual backup/storage failure still prevents mutation.
        CancelSupersededReturn(r.FoxId);
        AdvanceEmergencyOperation(r);
        return j;
    }

    private void ResetSimpleRebuildRecord(FoxPackRecordV2 work, FoxPackRecordV2 original)
    {
        work.BackpackItem = original.BackpackItem?.ToArray() ?? Array.Empty<byte>();
        work.BreedingEnabled = original.BreedingEnabled;
        work.FoodStateInitialized = true; work.FoodLevel = 1;
        work.FoodLastUpdateTotalHours = serverApi!.World.Calendar.TotalHours;
        work.CurrentHealth = float.IsFinite(work.MaxHealth) && work.MaxHealth > 0 ? work.MaxHealth : 0;
        work.HealthState = 0; work.MortallyWoundedStartHours = 0; work.StabilizedWindowAppliedRank = 0;
        work.StabilizedWindowAdjustedStartHours = work.DeathlessRecoveryAtHours = work.DeathlessFallbackAtHours = -1;
        work.Status = "Present"; work.StatusExpiresDay = 0;
        work.ActivityMode = CompanionActivityMode.Follow; work.ActivityStartedUtcMs = work.ActivityArrivedUtcMs = 0;
        work.RequestCooldownEndsUtcMs = work.CancelCooldownEndsUtcMs = 0;
    }

    private void RecoverSimpleNativeFields(FoxPackRecordV2 work, FoxPackRecordV2 original, EmergencyCompanionJournal j)
    {
        if (!j.SimpleEmergency || original.DurableStateInitialized || j.BeforeNative.Length == 0) return;
        try {
            var type = serverApi!.World.GetEntityType(new AssetLocation(j.EntityCode));
            if (type == null) return;
            var native = ReconstructRecoveryNative(type, j.BeforeNative);
            var s = GetDomesticationStatus(native);
            if (s == null) return;
            if (string.IsNullOrWhiteSpace(work.Name)) work.Name = native.WatchedAttributes.GetTreeAttribute("nametag")?.GetString("name") ?? string.Empty;
            if (original.Number <= 0 && s.GetInt(NumberKey) > 0) work.Number = s.GetInt(NumberKey);
            if (string.IsNullOrWhiteSpace(work.Personality)) work.Personality = s.GetString(PersonalityKey, string.Empty);
            if (work.Points <= 0) work.Points = Math.Max(0, s.GetInt(FoxPointsKey));
            work.LifetimePoints = Math.Max(work.Points, Math.Max(work.LifetimePoints, s.GetInt(FoxLifetimePointsKey)));
            if (work.Level <= 1) work.Level = Math.Max(1, s.GetInt(CompanionLevelKey, 1));
            if (work.CurrentLevelExperience <= 0) work.CurrentLevelExperience = Math.Max(0, s.GetLong(CompanionCurrentExperienceKey));
            if (work.LifetimeExperience <= 0) work.LifetimeExperience = Math.Max(work.CurrentLevelExperience, s.GetLong(CompanionLifetimeExperienceKey));
            if (work.BankedTalentPoints <= 0) work.BankedTalentPoints = Math.Max(0, s.GetInt(BankedTalentPointsKey));
            var perks = GetFoxPerkTree(native, false);
            work.TalentRanks ??= new();
            foreach (var definition in FoxPerkCatalog.All) {
                int rank = perks == null ? 0 : FoxPerkCatalog.GetRank(perks, definition);
                if (rank <= 0) continue;
                var existing = work.TalentRanks.FirstOrDefault(t => t.Id == definition.Id);
                if (existing == null) work.TalentRanks.Add(new() { Id = definition.Id, Rank = rank });
                else if (existing.Rank <= 0) existing.Rank = rank;
            }
            if (string.IsNullOrWhiteSpace(work.BondedPartnerId)) { work.BondedPartnerId = s.GetString(BondedPartnerIdKey, string.Empty); work.BondedPartnerName = s.GetString(BondedPartnerNameKey, string.Empty); }
            if (string.IsNullOrWhiteSpace(work.ParentMotherId)) { work.ParentMotherId = s.GetString(ParentMotherIdKey, string.Empty); work.ParentMotherName = s.GetString(ParentMotherNameKey, string.Empty); }
            if (string.IsNullOrWhiteSpace(work.ParentFatherId)) { work.ParentFatherId = s.GetString(ParentFatherIdKey, string.Empty); work.ParentFatherName = s.GetString(ParentFatherNameKey, string.Empty); }
            if (s.GetBool(JuvenileKey)) { work.IsJuvenile = true; work.AdulthoodExperienceGranted = false; }
            if (string.IsNullOrWhiteSpace(work.SpeciesId)) work.SpeciesId = s.GetString(JuvenileSpeciesKey, string.Empty);
            if (string.IsNullOrWhiteSpace(work.ChildAdultEntityCode)) work.ChildAdultEntityCode = s.GetString(ChildAdultEntityCodeKey, string.Empty);
            j.Recovered.Add("missing ledger progression/relationships filled from available native data");
        } catch (Exception ex) { ForceNote(j, "Partial native field recovery used remaining saved/default fields: " + ex.Message); }
    }

    private void ResetSimpleRebuildBody(Entity e)
    {
        e.Alive = true; e.State = EnumEntityState.Active;
        e.WatchedAttributes.SetInt(EntityHealthStateKey, 0);
        e.WatchedAttributes.SetDouble("mortallyWoundedTotalHours", 0); e.WatchedAttributes.SetFloat("regenSpeed", 1);
        e.WatchedAttributes.RemoveAttribute(RetainedNativeSuspension.Key);
        e.WatchedAttributes.SetAttribute("animations", new Vintagestory.API.Datastructures.TreeAttribute());
        e.Attributes.RemoveAttribute("emotionstates");
        e.WatchedAttributes.SetFloat("stressLevel", 0);
        e.WatchedAttributes.SetBool("onFire", false);
        e.WatchedAttributes.RemoveAttribute("damageovertime-activeeffects");
        // A native factory may not attach Properties/World until SpawnEntity.
        // Attribute resets apply before Initialize; behavior resets apply again
        // after publication, when those behavior instances actually exist.
        if (e.Properties != null && e.World != null) {
            e.GetBehavior<EntityBehaviorEmotionStates>()?.ActiveStatesByCode.Clear();
            if (e.GetBehavior<EntityBehaviorHealth>() is { } health) { health.ActiveDoTEffects.Clear(); health.FutureHealth = null; }
        }
        var hunger = e.WatchedAttributes.GetTreeAttribute("hunger");
        if (hunger != null && hunger.GetFloat("maxsaturation") > 0) hunger.SetFloat("saturation", hunger.GetFloat("maxsaturation"));
        var s = GetDomesticationStatus(e, true)!;
        s.SetBool(ExpeditionAwayKey, false); s.SetBool(BackpackDeliveryActiveKey, false); s.SetString(BackpackDeliveryPhaseKey, string.Empty);
        s.SetString(ExpeditionStatusKey, string.Empty); s.SetDouble(ExpeditionStatusExpiresDayKey, 0);
        s.SetBool(FoodStateInitializedKey, true); s.SetFloat(FoodLevelKey, 1); s.SetDouble(FoodLastUpdateTotalHoursKey, serverApi!.World.Calendar.TotalHours);
        if (e.AnimManager != null) {
            foreach (var animation in e.AnimManager.ActiveAnimationsByAnimCode.Keys.ToArray()) e.AnimManager.StopAnimation(animation);
        }
        if (e is EntityAgent agent) { agent.Controls.StopAllMovement(); agent.ServerControls.StopAllMovement(); }
        e.Pos.Motion.Set(0, 0, 0);
    }

    private void RetireSimpleRebuildBody(FoxPackRecordV2 r, EmergencyCompanionJournal j, Entity old)
    {
        ForceHold(r, j, old, "late original");
        // Full snapshots are durable before removal. If a provider refuses a
        // snapshot or removal, leave its body held; it cannot become a second pet.
        if (!j.Cargo.Any(x => x.BodyKey == ForceBodyKey(old) && x.Native.Length > 0)) return;
        if (!ReferenceEquals(serverApi!.World.GetEntityById(old.EntityId), old)) return;
        rebuildingEntityIds.Add(old.EntityId);
        try { serverApi.World.DespawnEntity(old, new EntityDespawnData { Reason = EnumDespawnReason.Removed }); }
        finally { rebuildingEntityIds.Remove(old.EntityId); }
    }

    private void RunSimpleRebuilds(IServerPlayer actor, RecoveryUiConfirmation c)
    {
        int index = 0, rebuilt = 0, failed = 0, pending = 0;
        void Next() {
            if (serverApi == null) return;
            if (index == c.Targets.Length) { SendCompanionRecoveryState(actor, $"Rebuild results: {rebuilt} rebuilt, {failed} failed, {pending} continuing in background."); return; }
            var target = c.Targets[index++];
            try {
                string error = RevalidateRecoveryUi(actor, c, target, true, out var record, out _);
                if (error.Length > 0) throw new InvalidOperationException(error);
                var j = StartSimpleRebuild(actor, record!, target.Mode == SimpleEmergencyMode)!;
                int checks = 0;
                void WaitForResult() {
                    if (serverApi == null) return;
                    packRepository!.TryGetRecord(target.FoxId, out var current);
                    if (current == null || LastEmergency(current)?.Id != j.Id) { failed++; SendAdminCompanionMessage(actor, target.FoxId + ": superseded by a newer rebuild"); }
                    else if (j.Stage == "Rebuilt") { rebuilt++; SendAdminCompanionMessage(actor, target.FoxId + ": rebuilt"); }
                    else if (j.Stage == "ForceFailed") { failed++; SendAdminCompanionMessage(actor, target.FoxId + ": " + j.Error); }
                    else if (++checks < 40) { AdvanceEmergencyOperation(current); serverApi.Event.RegisterCallback(_ => WaitForResult(), 100); return; }
                    else { pending++; SendAdminCompanionMessage(actor, target.FoxId + ": saved operation continues in background. " + j.Error); }
                    serverApi.Event.RegisterCallback(_ => Next(), 50);
                }
                WaitForResult();
            } catch (Exception ex) { failed++; SendAdminCompanionMessage(actor, target.FoxId + ": " + ex.Message); serverApi.Event.RegisterCallback(_ => Next(), 50); }
        }
        Next();
    }
}
