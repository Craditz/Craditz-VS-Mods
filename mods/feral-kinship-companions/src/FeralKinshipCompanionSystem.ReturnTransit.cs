#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private const long ReturnRetryDelayMs = 60000, ReturnTeleportTimeoutMs = 30000;
    private sealed class ReturnAttempt
    {
        internal required Entity Body;
        internal required string Trip, Outcome, Kind;
        internal required BlockPos Anchor;
        internal required Vec3d Safe;
        internal long Started;
        internal bool CallbackSeen, Expired;
    }
    private readonly Dictionary<string, ReturnAttempt> returnAttempts = new(StringComparer.Ordinal);
    private readonly HashSet<string> offeredReturnChoices = new(StringComparer.Ordinal);
    private readonly Queue<CompanionReturnChoiceOffer> returnChoiceQueue = new();
    private readonly HashSet<string> clientReturnOffers = new(StringComparer.Ordinal);
    private GuiDialogCompanionMissingCart? missingCartDialog;

    private static bool ReturnSupersededByRebuild(FoxPackRecordV2 r)
        => LastEmergency(r) is { SimpleRebuild: true } j && EmergencyPending(j);

    private void CancelSupersededReturn(string id)
    {
        if (!returnAttempts.TryGetValue(id, out var attempt)) return;
        attempt.Expired = true;
        returnAttempts.Remove(id);
        // The native request owns its teleport flags. Its eventual callback
        // may finish, but cannot apply an old outcome to a rebuilt generation.
    }

    private CompanionReturnTransit EnsureReturnTransit(FoxPackRecordV2 r)
    {
        if (r.ReturnTransit is { } legacy && string.IsNullOrEmpty(legacy.Trip) && !string.IsNullOrEmpty(r.RetainedTrip)
            && legacy.NativeId == r.EntityId && legacy.Outcome == r.PendingReturnStatus) legacy.Trip = r.RetainedTrip;
        if (r.ReturnTransit == null || r.ReturnTransit.Trip != r.RetainedTrip || r.ReturnTransit.NativeId != r.EntityId)
            r.ReturnTransit = new() { Trip = r.RetainedTrip, NativeId = r.EntityId, Outcome = r.PendingReturnStatus };
        return r.ReturnTransit;
    }

    private void WaitForReturn(FoxPackRecordV2 r, Entity? e, string stage, string failure)
    {
        var journal = EnsureReturnTransit(r);
        bool report = journal.Stage != stage || journal.Failure != failure;
        journal.Stage = stage; journal.Failure = failure;
        r.PendingReturnAtUtcMs = UtcNowMs() + ReturnRetryDelayMs;
        r.Status = "Awaiting safe return"; r.StatusExpiresDay = 0;
        if (e != null) RetainedNativeSuspension.Wait(e);
        if (report) serverApi?.Logger.Warning("[FeralKinshipCompanions] Return pending {0}/{1}, trip {2}: {3}: {4}; retry in 60 seconds.", r.OwnerUid, r.FoxId, r.RetainedTrip, stage, failure);
    }

    // Return a validated cart or uncertainty. Only a loaded, checked absence
    // may produce the missing-cart owner choice. Legacy markers are checked too.
    private BlockPos? ResolveReturnCart(string owner, out bool unknown)
    {
        unknown = false;
        if (serverApi == null || packRepository?.Loaded != true) { unknown = true; return null; }
        foreach (var marker in packRepository.GetCairnsForOwner(owner).ToArray())
        {
            var pos = new BlockPos(marker.X, marker.Y, marker.Z, marker.Dimension);
            if (pos.dimension != 0) { unknown = true; continue; }
            if (serverApi.World.BlockAccessor.GetChunkAtBlockPos(pos) == null)
            {
                RequestReturnDestinationColumn(pos); unknown = true; continue;
            }
            var code = serverApi.World.BlockAccessor.GetBlock(pos).Code;
            if (code?.Domain != "feralkinshipcompanions" || !code.Path.StartsWith(PackCartBlockPathPrefix, StringComparison.Ordinal)) continue;
            var ownership = ResolvePackCartOwnership(pos, applyRepairs: true);
            if (ownership.OwnerUid == owner) return pos;
            if (string.IsNullOrEmpty(ownership.OwnerUid)) unknown = true;
        }
        return null;
    }

    private bool ReturnPlayerInsideCart(string owner, BlockPos cart)
    {
        var player = serverApi?.World.PlayerByUid(owner)?.Entity;
        if (player == null || !player.Alive || player.Pos.Dimension != cart.dimension) return false;
        double dx = player.Pos.X - (cart.X + .5), dz = player.Pos.Z - (cart.Z + .5);
        double radius = GetOwnerWorkCartRadius(owner);
        return dx * dx + dz * dz <= radius * radius;
    }

    private bool TrySelectReturnDestination(Entity e, FoxPackRecordV2 r, out BlockPos anchor, out Vec3d safe, out string kind)
    {
        anchor = null!; safe = null!; kind = string.Empty;
        var journal = EnsureReturnTransit(r);
        BlockPos? cart = ResolveReturnCart(r.OwnerUid, out bool cartUnknown);
        if (cart == null && !cartUnknown)
        {
            if (journal.MissingCartChoice != "Player")
            {
                WaitForReturn(r, e, "Missing cart", journal.MissingCartChoice == "Wait" ? "Waiting for replacement pack cart by owner choice" : "Owner choice required");
                OfferMissingReturnCart(r); return false;
            }
            return TrySelectReturnPlayer(e, r, null, out anchor, out safe, out kind);
        }
        // Replaced cart restores the normal hierarchy, superseding old consent.
        if (cart != null) { journal.MissingCartChoice = string.Empty; journal.ChoiceToken = string.Empty; }
        if (cart != null && journal.FallbackStart == 0)
        {
            Vec3d? spot = FindSafeEntityPosition(cart, e.Properties, e, 8, 4);
            if (spot != null) { anchor = cart; safe = spot; kind = "Cart"; return true; }
        }
        if (journal.FallbackStart <= 1 && r.HasHome && !string.Equals(r.HomeType, "workcart", StringComparison.OrdinalIgnoreCase))
        {
            var bed = new BlockPos(r.HomeX, r.HomeY, r.HomeZ, r.HomeDimension);
            if (bed.dimension == 0)
            {
                if (serverApi!.World.BlockAccessor.GetChunkAtBlockPos(bed) == null) RequestReturnDestinationColumn(bed);
                else
                {
                    var code = serverApi.World.BlockAccessor.GetBlock(bed).Code;
                    var assignment = packRepository!.GetBed(bed);
                    if (code != null && IsFoxBedCode(code) && assignment?.OwnerUid == r.OwnerUid && assignment.FoxId == r.FoxId)
                    {
                        Vec3d? spot = FindSafeEntityPosition(bed, e.Properties, e, 4, 4);
                        if (spot != null) { anchor = bed; safe = spot; kind = "Bed"; return true; }
                    }
                }
            }
        }
        if (cart != null && ReturnPlayerInsideCart(r.OwnerUid, cart))
            return TrySelectReturnPlayer(e, r, cart, out anchor, out safe, out kind);
        WaitForReturn(r, e, "No destination", cartUnknown ? "Cart verification pending; no usable assigned bed" : "Cart and bed placement unavailable; owner outside cart territory or offline");
        journal.FallbackStart = 0;
        return false;
    }

    private bool TrySelectReturnPlayer(Entity e, FoxPackRecordV2 r, BlockPos? cart, out BlockPos anchor, out Vec3d safe, out string kind)
    {
        anchor = null!; safe = null!; kind = string.Empty;
        var player = serverApi?.World.PlayerByUid(r.OwnerUid)?.Entity;
        if (player == null || !player.Alive || player.Pos.Dimension != 0)
        { WaitForReturn(r, e, "Waiting for player", "Owner is offline, dead or in an unsupported dimension"); return false; }
        anchor = player.Pos.AsBlockPos.Copy();
        Vec3d? spot = FindSafeEntityPosition(anchor, e.Properties, e, 6, 4);
        if (spot == null) { WaitForReturn(r, e, "No safe ground", "No safe placement beside the owner"); return false; }
        safe = spot; kind = cart == null ? "Consented player" : "Player"; return true;
    }

    private bool ReturnDestinationStillValid(FoxPackRecordV2 r, ReturnAttempt attempt)
    {
        if (serverApi == null || serverApi.World.BlockAccessor.GetChunkAtBlockPos(attempt.Safe.AsBlockPos) == null) return false;
        if (attempt.Kind == "Bed")
        {
            var code = serverApi.World.BlockAccessor.GetBlock(attempt.Anchor).Code;
            var assignment = packRepository?.GetBed(attempt.Anchor);
            return r.HasHome && r.HomeType != "workcart" && r.HomeDimension == attempt.Anchor.dimension
                && r.HomeX == attempt.Anchor.X && r.HomeY == attempt.Anchor.Y && r.HomeZ == attempt.Anchor.Z
                && code != null && IsFoxBedCode(code) && assignment?.OwnerUid == r.OwnerUid && assignment.FoxId == r.FoxId;
        }
        BlockPos? cart = ResolveReturnCart(r.OwnerUid, out bool unknown);
        if (attempt.Kind == "Cart") return cart != null && cart.Equals(attempt.Anchor);
        var player = serverApi.World.PlayerByUid(r.OwnerUid)?.Entity;
        if (player == null || !player.Alive || player.Pos.Dimension != 0 || player.Pos.XYZ.SquareDistanceTo(attempt.Safe) > 100) return false;
        return attempt.Kind == "Player" ? cart != null && ReturnPlayerInsideCart(r.OwnerUid, cart)
            : cart == null && !unknown && r.ReturnTransit?.MissingCartChoice == "Player";
    }

    private void OfferMissingReturnCart(FoxPackRecordV2 r)
    {
        var journal = EnsureReturnTransit(r);
        if (journal.MissingCartChoice is "Wait" or "Player" || serverApi?.World.PlayerByUid(r.OwnerUid) is not IServerPlayer owner
            || owner.ConnectionState != EnumClientState.Playing) return;
        if (string.IsNullOrEmpty(journal.ChoiceToken))
        {
            string token = Guid.NewGuid().ToString("N");
            foreach (var pending in packRepository!.GetPendingReturnRecords().Where(x => x.OwnerUid == r.OwnerUid))
            {
                var j = EnsureReturnTransit(pending);
                if (string.IsNullOrEmpty(j.MissingCartChoice) && string.IsNullOrEmpty(j.ChoiceToken)) j.ChoiceToken = token;
            }
            packRepository.Save(); // Persist authority before presenting the choice.
        }
        string key = r.OwnerUid + ":" + journal.ChoiceToken;
        if (offeredReturnChoices.Contains(key)) return;
        int count = packRepository!.GetPendingReturnRecords().Count(x => x.OwnerUid == r.OwnerUid && x.ReturnTransit?.ChoiceToken == journal.ChoiceToken);
        serverChannel?.SendPacket(new CompanionReturnChoiceOffer { Token = journal.ChoiceToken, Count = count }, owner);
        offeredReturnChoices.Add(key);
    }

    private void OnReturnChoiceResponse(IServerPlayer owner, CompanionReturnChoiceResponse packet)
    {
        if (packRepository?.Loaded != true || packet.Token == null || packet.Token.Length != 32 || !Guid.TryParseExact(packet.Token, "N", out _)) return;
        if (Vintagestory.API.Config.RuntimeEnv.ServerMainThreadId <= 0) return;
        if (Environment.CurrentManagedThreadId != Vintagestory.API.Config.RuntimeEnv.ServerMainThreadId) {
            var copy = new CompanionReturnChoiceResponse { Token = packet.Token, PlaceNearPlayer = packet.PlaceNearPlayer };
            serverApi?.Event.EnqueueMainThreadTask(() => OnReturnChoiceResponse(owner, copy), "companion-return-consent"); return;
        }
        BlockPos? cart = ResolveReturnCart(owner.PlayerUID, out bool unknown);
        if (cart != null) return; // Consent is specific to confirmed absence.
        bool changed = false;
        foreach (var r in packRepository.GetPendingReturnRecords().Where(x => x.OwnerUid == owner.PlayerUID))
        {
            var j = r.ReturnTransit;
            if (ReturnSupersededByRebuild(r) || j == null || j.Trip != r.RetainedTrip || j.NativeId != r.EntityId || j.ChoiceToken != packet.Token || !string.IsNullOrEmpty(j.MissingCartChoice)) continue;
            if (unknown && packet.PlaceNearPlayer) {
                j.ChoiceToken = string.Empty; offeredReturnChoices.Remove(owner.PlayerUID + ":" + packet.Token); changed = true; continue;
            }
            j.MissingCartChoice = packet.PlaceNearPlayer ? "Player" : "Wait";
            j.Stage = packet.PlaceNearPlayer ? "Owner approved placement" : "Waiting for replacement cart";
            r.PendingReturnAtUtcMs = UtcNowMs(); changed = true;
        }
        if (changed) packRepository.Save();
    }

    private void OnReturnChoiceOffer(CompanionReturnChoiceOffer packet)
    {
        if (clientApi == null || packet.Count <= 0 || !clientReturnOffers.Add(packet.Token)) return;
        returnChoiceQueue.Enqueue(packet);
        clientApi.Event.EnqueueMainThreadTask(ShowNextReturnChoice, "companion-return-choice");
    }
    private void ShowNextReturnChoice()
    {
        if (clientApi == null || missingCartDialog?.IsOpened() == true || !returnChoiceQueue.TryDequeue(out var offer)) return;
        missingCartDialog?.Dispose();
        missingCartDialog = new GuiDialogCompanionMissingCart(clientApi, place => {
            clientChannel?.SendPacket(new CompanionReturnChoiceResponse { Token = offer.Token, PlaceNearPlayer = place });
        });
        missingCartDialog.OnClosed += () => clientApi.Event.EnqueueMainThreadTask(ShowNextReturnChoice, "companion-return-next-choice");
        missingCartDialog.TryOpen();
    }

    private void MaintainReturnTransits()
    {
        long now = UtcNowMs();
        foreach (var pair in returnAttempts.ToArray())
        {
            var attempt = pair.Value;
            if (packRepository?.TryGetRecord(pair.Key, out var r) != true || r == null || ReturnSupersededByRebuild(r) || r.EntityId != attempt.Body.EntityId || r.RetainedTrip != attempt.Trip || r.PendingReturnStatus != attempt.Outcome)
            { attempt.Expired = true; returnAttempts.Remove(pair.Key); RetainedNativeSuspension.Wait(attempt.Body); continue; }
            if (now < attempt.Started + ReturnTeleportTimeoutMs) continue;
            // A missing callback can still have placed the body. Verify the native
            // result; never stack a second teleport over a live native request.
            if (!attempt.Body.Teleporting)
            {
                if (attempt.Body.Pos.XYZ.SquareDistanceTo(attempt.Safe) <= .01)
                    VerifyRetainedPlacement(attempt.Body, r, attempt.Outcome, attempt.Anchor, attempt.Safe, attempt.Trip, 0);
                else { attempt.Expired = true; returnAttempts.Remove(pair.Key); WaitForReturn(r, attempt.Body, "Teleport timeout", "Teleport ended without verified destination"); }
            }
            else
            {
                WaitForReturn(r, null, "Native teleport pending", "Native load/teleport has not acknowledged; preserving original without overlapping requests");
                attempt.Started = now; // Watch again after the bounded interval.
            }
        }
        if (serverApi != null)
            offeredReturnChoices.RemoveWhere(key => !serverApi.World.AllOnlinePlayers.Any(p => key.StartsWith(p.PlayerUID + ":", StringComparison.Ordinal)));
    }

    private void VerifyReturnAttempt(FoxPackRecordV2 r, ReturnAttempt attempt, int count)
    {
        if (attempt.Expired || !returnAttempts.TryGetValue(r.FoxId, out var active) || !ReferenceEquals(active, attempt)
            || ReturnSupersededByRebuild(r) || r.EntityId != attempt.Body.EntityId || r.RetainedTrip != attempt.Trip || r.PendingReturnStatus != attempt.Outcome
            || packRepository?.TryGetRecord(r.FoxId, out var current) != true || !ReferenceEquals(current,r)) return;
        VerifyRetainedPlacement(attempt.Body, r, attempt.Outcome, attempt.Anchor, attempt.Safe, attempt.Trip, count);
    }

    private void FailReturnAttempt(FoxPackRecordV2 r, ReturnAttempt attempt, string reason)
    {
        if (attempt.Expired) return;
        attempt.Expired = true;
        if (returnAttempts.TryGetValue(r.FoxId, out var active) && ReferenceEquals(active, attempt)) returnAttempts.Remove(r.FoxId);
        WaitForReturn(r, attempt.Body, "Placement retry", reason);
        var journal = EnsureReturnTransit(r);
        journal.FallbackStart = attempt.Kind == "Cart" ? 1 : attempt.Kind == "Bed" ? 2 : 0;
        if (journal.FallbackStart != 0 && !attempt.Body.Teleporting && RetainedNativeSuspension.Marker(attempt.Body)?.GetString("phase") != "Hold")
            r.PendingReturnAtUtcMs = UtcNowMs() + ExpeditionReturnStaggerMs;
    }

    // Narrow reconciliation for the new journal only: a native/ledger phase
    // skew from save ordering is retryable when exact identity/trip/outcome agree.
    private bool ReconcileReturnJournal(Entity e, FoxPackRecordV2 r)
    {
        var j = r.ReturnTransit; var m = RetainedNativeSuspension.Marker(e);
        if (j != null && m != null && j.Trip == r.RetainedTrip && j.NativeId == e.EntityId && j.OutcomePrepared
            && string.IsNullOrEmpty(r.PendingReturnStatus) && r.RetainedPhase == "Complete"
            && m.GetString("phase") is "Suspended" or "Returning" or "Resuming"
            && RetainedNativeSuspension.Matches(e,r) && RetainedNativeSuspension.SafeSnapshot(e)
            && r.CompletionProof is { } proof && proof.Trip == j.Trip && proof.NativeId == e.EntityId && proof.Outcome == j.Outcome
            && proof.Owner == r.OwnerUid && proof.Companion == r.FoxId && proof.Code == e.Code?.ToString()
            && proof.CapturedHours == m.GetDouble("capturedHours") && proof.ResumeHours == j.OutcomeHours
            && (string.IsNullOrEmpty(m.GetString("outcome")) || m.GetString("outcome") == j.Outcome))
        {
            r.PendingReturnStatus = j.Outcome; r.PendingReturnAtUtcMs = UtcNowMs();
            r.Status = "Returning";
        }
        if (j == null || j.Trip != r.RetainedTrip || j.NativeId != e.EntityId || j.Outcome != r.PendingReturnStatus
            || string.IsNullOrEmpty(j.Outcome) || !RetainedNativeSuspension.Matches(e, r) || !RetainedNativeSuspension.SafeSnapshot(e) || m == null) return false;
        string phase = m.GetString("phase");
        if (phase == "Hold" && r.RetainedPhase == "Returning" && m.GetString("outcome") == j.Outcome
            && m.GetString("reason") is "Destination chunk membership did not complete within bounded wait" or "Placement completion authority or position could not be verified"
            && RetainedCanonicalAdmission(e))
        { m.SetString("previousWaitReason", m.GetString("reason")); m.SetString("phase", "Returning"); RetainedNativeSuspension.Wait(e); phase = "Returning"; }
        if (phase is not ("Suspended" or "Returning" or "Resuming" or "Complete") || r.RetainedPhase is not ("Suspended" or "Returning" or "Resuming" or "Complete")) return false;
        if (!string.IsNullOrEmpty(m.GetString("outcome")) && m.GetString("outcome") != j.Outcome) return false;
        if (phase == r.RetainedPhase && phase is "Suspended" or "Returning") return true;
        m.SetString("phase", "Returning"); m.SetString("outcome", j.Outcome);
        r.RetainedPhase = "Returning"; r.RetainedOutcome = j.Outcome;
        GetDomesticationStatus(e, true)!.SetBool(ExpeditionAwayKey, true);
        RetainedNativeSuspension.Wait(e); return true;
    }
}
