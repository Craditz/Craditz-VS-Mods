using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private bool refreshingRosterPresence;

    // This is presentation reconciliation from admitted native authority, not
    // registration/hydration. Never replay health, inventory, XP or journeys.
    private bool RefreshVerifiedCompanionPresence(Entity e, ExpeditionPresentationAuthority? authority)
    {
        if (serverApi == null || packRepository?.Loaded != true || authority == null || !IsTamedFox(e) || IsGuideFox(e)
            || !ReferenceEquals(e.Api, serverApi) || !ReferenceEquals(e.World, serverApi.World)
            || !e.Alive || e.ShouldDespawn || e.Teleporting || e.State == EnumEntityState.Despawned
            || IsFoxAwayFromWorld(e) || e.CollisionBox?.XSize is not > 0 || e.SelectionBox?.XSize is not > 0
            || e.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.HasExpeditionPresentationSuppression == true
            || !NativeRecoveryPublished(e) || !loadedFoxes.TryGetValue(e.EntityId, out var registered) || !ReferenceEquals(registered,e)) return false;
        var native = GetDomesticationStatus(e);
        string id = native?.GetString(FoxIdKey) ?? string.Empty;
        if (!authority.Records.TryGetValue(id, out var r) || r.EntityId != e.EntityId || r.OwnerUid != GetCompanionOwnerUid(e)
            || !ExactRecoveryCodeEquals(r.EntityCode,e.Code) || r.Archived || r.MissingContentArchive?.Active == true
            || packRepository.IsPermanentlyDeleted(id) || IsRetiredRecoveryOriginal(e)
            || authority.Conflicts.Contains(id) || authority.NativeConflicts.Contains(e.EntityId) || authority.Assigned.Contains(id)
            || !authority.NativeBodies.TryGetValue(id,out var body) || !ReferenceEquals(body,e)
            || NativeLinkPending(r) || EmergencyPending(LastEmergency(r))
            || r.PendingDepartureStatus.Length > 0 || r.PendingDepartureAtUtcMs > 0 || r.PendingDepartureDeadlineUtcMs > 0
            || r.PendingReturnStatus.Length > 0 || r.PendingReturnAtUtcMs > 0 || r.MissingRecoveryState == "Prepared") return false;
        if (r.Status is not ("Present" or "Available" or "Not currently loaded" or "Returned healthy" or "Returned injured" or "Mortally wounded" or "Recovering")) return false;
        if (HasForceHistory(r) && !IsEmergencySelected(e)) return false;
        if (LastNativeLink(r) is { } link) {
            if (link.Stage != "Linked" || link.NewId != e.EntityId) return false;
            try { ReadNativeLinkLedger(link); } catch { return false; }
        }
        var marker = RetainedNativeSuspension.Marker(e);
        if (marker != null || r.RetainedTrip.Length > 0) {
            if (PresentationRepairBlocker(e,authority) != "eligible") return false;
        }
        string persisted = native?.GetString(ExpeditionStatusKey) ?? string.Empty;
        if (persisted.StartsWith("Away ",StringComparison.OrdinalIgnoreCase) || persisted.StartsWith("Leaving ",StringComparison.OrdinalIgnoreCase)
            || persisted is "Returning" or "MIA" or "Running late" or "Awaiting safe return") return false;
        double expiry = native?.GetDouble(ExpeditionStatusExpiresDayKey,0) ?? 0;
        string next = IsFoxMortallyWounded(e) ? "Mortally wounded"
            : e.WatchedAttributes.GetInt(EntityHealthStateKey) == RecoveringHealthState ? "Recovering"
            : persisted is "Returned healthy" or "Returned injured" && expiry > serverApi.World.Calendar.TotalDays ? persisted
            : "Present";
        r.Status = next;
        r.StatusExpiresDay = next is "Returned healthy" or "Returned injured" ? expiry : 0;
        r.RecoveryStatus = CompanionRecoveryStatus.Recoverable;
        r.LastSeenDay = serverApi.World.Calendar.TotalDays;
        return true;
    }
}
