#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private const string AdultRecoveryReason = "confirmed retained adult recovery with current progression";
    private sealed record AdultRecoveryConsent(string Token, string Owner, string Id, long Child, long Adult, string Fingerprint, long Expires);
    private readonly Dictionary<string, AdultRecoveryConsent> adultRecoveryConsents = new(StringComparer.Ordinal);

    private string DescribeForceRecoveryBlocker(IServerPlayer actor, FoxPackRecordV2 r)
    {
        if (!MayRepairCompletedHold(actor) || actor.ConnectionState != EnumClientState.Playing || !DeveloperMayControlOwner(actor, r.OwnerUid))
            return "Connected admin and selected-owner authorization required";
        if (r.EmergencyRebuilds.Any(j => j.Version is not (1 or 2 or 3))) return "Unknown emergency journal version; preserve checkpoints";
        if (packRepository?.CanCheckpointRecovery != true) return "Durable checkpoint unavailable; no force operation may start";
        return string.Empty;
    }

    private string AdultRecoveryPreflight(IServerPlayer actor, FoxPackRecordV2 r, out Entity? child, out Entity? adult)
    {
        child = adult = null;
        if (!MayRepairCompletedHold(actor) || actor.ConnectionState != EnumClientState.Playing || !DeveloperMayControlOwner(actor, r.OwnerUid)) return "Connected server admin and selected-owner authorization required";
        if (serverApi == null || !companionContentReady || packRepository?.CanCheckpointRecovery != true
            || RuntimeEnv.ServerMainThreadId != Environment.CurrentManagedThreadId) return "Initialized server main thread and durable checkpoint required";
        if (r.EmergencyBody is { Version: 2, Reason: AdultRecoveryReason, Phase: "Selecting" }) return "Adult selection is pending; let its durable resume finish";
        if (r.EmergencyRebuilds.Any(j => j.Version is not (1 or 2 or 3))) return "Unknown recovery version; preserve checkpoints";
        var journal = LastEmergency(r);
        if (journal == null || !HasForceHistory(r) || EmergencyPending(journal) || NativeLinkPending(r)
            || r.Archived || packRepository.IsPermanentlyDeleted(r.FoxId) || r.BackpackDeliveryActive
            || r.PendingDepartureStatus.Length > 0 || r.PendingReturnStatus.Length > 0 || r.RetainedTrip.Length > 0
            || pendingFullRebuilds.Contains(r.FoxId) || pendingRecoveryChecks.Contains(r.FoxId)) return "Journey, cargo or recovery authority is busy or unavailable";
        if (!CompanionFotsaBreedingCatalog.TryGetGrowth(new AssetLocation(r.EntityCode), out var pair)) return "The current type is not a registered tame juvenile with one verified adult destination";
        child = EmergencyNative(r.EntityId, out string childLookup);
        if (childLookup.Length > 0 || child == null || !child.Alive || !EmergencyIdentity(child, r)
            || !ExactRecoveryCodeEquals(r.EntityCode, child.Code) || !IsEmergencySelected(child)
            || !NativeRecoveryPublished(child) || EmergencyBodyBlocker(child).Length > 0) return "Current child is not one canonical loaded selected body";
        var authority = CaptureExpeditionPresentationAuthority();
        if (authority == null || authority.Assigned.Contains(r.FoxId) || authority.NativeConflicts.Contains(r.EntityId)) return "Expedition or native identity authority is unresolved";
        // Evidence must be a saved capture of the ORIGINAL adult, never a
        // fresh body inferred merely from its species or matching owner.
        var candidates = new List<Entity>();
        var capturedIds = journal.Cargo.Where(c => c.EntityCode == pair.AdultCode
            && c.Kind is "original before force" or "late original").Select(c => c.NativeId).Distinct().Take(33).ToArray();
        if (capturedIds.Length > 32) return "Adult candidate scan exceeds its bound";
        foreach (long id in capturedIds) {
            var e = EmergencyNative(id, out string lookup);
            if (lookup.Length > 0) return "Adult native lookup is ambiguous or incomplete";
            if (e == null) continue;
            if (e.Alive && EmergencyIdentity(e, r) && ExactRecoveryCodeEquals(pair.AdultCode, e.Code)
                && journal.Cargo.Any(c => c.NativeId == id && c.BodyKey == ForceBodyKey(e) && c.EntityCode == pair.AdultCode)
                && RecoveryHold(e) && NativeRecoveryPublished(e) && EmergencyBodyBlocker(e).Length == 0
                && e.Pos.Dimension == child.Pos.Dimension) candidates.Add(e);
        }
        if (candidates.Count != 1) return "Need exactly one loaded, captured original adult; none was created or substituted";
        adult = candidates[0];
        if (GetDomesticationStatus(child)?.GetInt(FoxPointsKey) != r.Points
            || GetDomesticationStatus(child)?.GetInt(BankedTalentPointsKey) != r.BankedTalentPoints) return "Current child points differ from the ledger; refresh before recovery";
        if ((long)r.Points + r.BankedTalentPoints > int.MaxValue) return "Point total exceeds supported range; no values changed";
        return string.Empty;
    }

    private string AdultRecoveryFingerprint(FoxPackRecordV2 r, Entity child, Entity adult) =>
        RecoveryUiFingerprint(r, child) + ":" + RecoveryUiFingerprint(r, adult);

    private TextCommandResult HandleAdultRecoveryCommand(IServerPlayer actor, string text)
    {
        try {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 2 && words[0] == "confirm") {
                if (!adultRecoveryConsents.Remove(actor.PlayerUID, out var consent) || consent.Token != words[1]
                    || serverApi == null || serverApi.World.ElapsedMilliseconds > consent.Expires) return TextCommandResult.Error("Adult recovery confirmation expired or already used; request a new preview");
                if (packRepository?.TryGetRecord(consent.Id, out var r) != true || r == null || r.OwnerUid != consent.Owner) return TextCommandResult.Error("Selected record changed; no animal changed");
                string blocked = AdultRecoveryPreflight(actor, r, out var child, out var adult);
                if (blocked.Length > 0 || child?.EntityId != consent.Child || adult?.EntityId != consent.Adult
                    || AdultRecoveryFingerprint(r, child!, adult!) != consent.Fingerprint) return TextCommandResult.Error(blocked.Length > 0 ? blocked : "Bodies or cargo changed; request a new preview");
                StartAdultRecovery(r, child!, adult!);
                return TextCommandResult.Success("Adult recovery checkpoint saved. " + (r.EmergencyBody?.Phase == "Ready"
                    ? $"Selected existing adult {adult!.EntityId}; current points preserved. Adult inventory stays with the adult; child and its cargo remain separately held. Inspect /companiondiag; client arrival/gameplay remains to be checked."
                    : "Selection is pending durable resume; both bodies and cargo remain preserved."));
            }
            string owner = words.Contains("cart", StringComparer.OrdinalIgnoreCase) ? DeveloperTargetOwnerUid(actor) : actor.PlayerUID;
            string id = words.FirstOrDefault(s => s.StartsWith("id:", StringComparison.Ordinal))?[3..] ?? string.Empty;
            if (!DeveloperMayControlOwner(actor, owner) || packRepository?.TryGetRecord(id, out var record) != true || record == null || record.OwnerUid != owner) return TextCommandResult.Error("Use /companionadult [cart] id:<companion-id> for the authorized owner");
            string reason = AdultRecoveryPreflight(actor, record, out var source, out var target);
            if (reason.Length > 0) return TextCommandResult.Error(reason);
            string token = Guid.NewGuid().ToString("N");
            adultRecoveryConsents[actor.PlayerUID] = new(token, owner, id, source!.EntityId, target!.EntityId,
                AdultRecoveryFingerprint(record, source, target), serverApi!.World.ElapsedMilliseconds + 60000);
            return TextCommandResult.Success($"Adult recovery PREVIEW: {record.Name}; child {source.EntityId} -> retained original adult {target.EntityId}. Preserve {record.Points} available and {record.BankedTalentPoints} banked points, XP and talents. Adult native inventory remains intact; child/cargo and all existing checkpoints stay separate. No spawn or cargo merge. Confirm within 60 seconds: /companionadult confirm {token}");
        } catch (Exception error) {
            serverApi?.Logger.Error("[CompanionAdultRecovery] {0}", error);
            return TextCommandResult.Error("Adult recovery stopped; checkpoints and bodies retained. Inspect /companiondiag and server-main: " + error.Message);
        }
    }

    private void StartAdultRecovery(FoxPackRecordV2 r, Entity child, Entity adult)
    {
        var j = LastEmergency(r)!;
        byte[] before = ForceLedgerBytes(r);
        // ForceCapture serializes both bodies before changing their native
        // presentation. Existing captures and the original force Undo survive.
        string capture = Guid.NewGuid().ToString("N");
        var childSnapshot = ForceCapture(r, j, child, "child before adult recovery " + capture);
        var adultSnapshot = ForceCapture(r, j, adult, "adult before adult recovery " + capture);
        if (childSnapshot.Native.Length == 0 || adultSnapshot.Native.Length == 0)
            throw new InvalidOperationException("Both current native bodies/cargo must serialize before adult selection; live bodies remain unchanged");
        var previous = r.EmergencyBody;
        string oldCode = r.EntityCode;
        r.EntityCode = adult.Code.ToString();
        r.EmergencyBody = new() { Version = 2, Operation = j.Id, NativeId = adult.EntityId, BodyKey = ForceBodyKey(adult),
            Marker = adult.WatchedAttributes.GetString(EmergencyBodyKey) ?? string.Empty, Reason = AdultRecoveryReason,
            BeforeRepairLedger = before, BeforeRepairHash = EmergencyHash(before) };
        try { ForceCheckpoint(r, j); }
        catch { r.EntityCode = oldCode; r.EmergencyBody = previous; throw; }
        CompleteEmergencyChoice(r, j);
        LogRecoveryAttempt(r.FoxId, "adult-recovery", $"child={child.EntityId}; adult={adult.EntityId}; phase={r.EmergencyBody.Phase}; points={r.Points}; separate-cargo=true");
    }

    private void ApplyAdultRecoveryProgress(FoxPackRecordV2 r, Entity adult, EmergencyBodyAuthority selection)
    {
        var before = ReadEmergencyLedger(selection.BeforeRepairLedger, selection.BeforeRepairHash);
        if (before.OwnerUid != r.OwnerUid || before.FoxId != r.FoxId || !EmergencyIdentity(adult, r)) throw new InvalidOperationException("Adult recovery snapshot identity differs");
        var status = GetDomesticationStatus(adult, true)!;
        bool applied = status.GetString("feralKinshipAdultRecoveryApplied") == selection.BeforeRepairHash;
        if (!applied) {
        int points = checked(before.Points + before.BankedTalentPoints);
        status.SetInt(FoxPointsKey, points); status.SetInt(FoxLifetimePointsKey, Math.Max(before.LifetimePoints, points));
        status.SetInt(BankedTalentPointsKey, 0); status.SetBool(JuvenileKey, false);
        status.SetBool(JuvenilePointsTransferredKey, true); status.SetBool(GrowthInProgressKey, false);
        status.SetString(ChildAdultEntityCodeKey, adult.Code.ToString());
        status.SetInt(CompanionLevelKey, before.Level); status.SetLong(CompanionCurrentExperienceKey, before.CurrentLevelExperience);
        status.SetLong(CompanionLifetimeExperienceKey, before.LifetimeExperience);
        // Do not award adulthood a second time after a repair.
        status.SetBool(AdulthoodExperienceGrantedKey, true);
        var perks = GetFoxPerkTree(adult, true)!;
        foreach (var perk in FoxPerkCatalog.All) perks.SetInt(perk.Id, before.TalentRanks?.FirstOrDefault(t => t.Id == perk.Id)?.Rank ?? 0);
        status.SetString("feralKinshipAdultRecoveryApplied", selection.BeforeRepairHash);
        }
        r.Points = status.GetInt(FoxPointsKey); r.LifetimePoints = status.GetInt(FoxLifetimePointsKey); r.BankedTalentPoints = 0;
        r.Level = status.GetInt(CompanionLevelKey); r.CurrentLevelExperience = status.GetLong(CompanionCurrentExperienceKey);
        r.LifetimeExperience = status.GetLong(CompanionLifetimeExperienceKey);
        GetHealth(adult, out r.CurrentHealth, out r.MaxHealth);
        r.IsJuvenile = false; r.JuvenilePointsTransferred = true; r.AdulthoodExperienceGranted = true;
        r.ChildAdultEntityCode = adult.Code.ToString();
        PersistCompanionBackpack(adult);
        // This immutable snapshot is applied only until the initial admission
        // checkpoint. Later presentation repair must not rewind earned points.
        adult.WatchedAttributes.MarkPathDirty(DomesticationStatusPath);
        // No native health, inventory, backpack, naming or provider state is
        // hydrated from the child. Native adult cargo remains authoritative.
    }
}
