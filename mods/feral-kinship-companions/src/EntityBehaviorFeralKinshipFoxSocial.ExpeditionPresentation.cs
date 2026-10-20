#nullable enable
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
namespace FeralKinshipCompanions;

public sealed partial class EntityBehaviorFeralKinshipFoxSocial
{
    private bool expeditionPresentationListenerAttached;
    private int presentationMarkerUpdates,presentationStatusUpdates;
    private void OnPresentationMarkerUpdated(){presentationMarkerUpdates++;RefreshExpeditionSuspension();}
    private void OnPresentationStatusUpdated(){presentationStatusUpdates++;RefreshExpeditionSuspension();}
    internal string DescribePresentationUpdates()=> $"social-behavior=present listeners={expeditionPresentationListenerAttached} marker-updates={presentationMarkerUpdates} status-updates={presentationStatusUpdates} latch={expeditionSuspended}";
    internal bool HasExpeditionPresentationSuppression => expeditionSuspended;
    // Read-only evidence from this load's own suppression, never a fabricated
    // native checkpoint or an override of a retained/away marker.
    internal bool CanResumeNativeLinkAdmission => expeditionSuspended && savedServerState == EnumEntityState.Active
        && RetainedNativeSuspension.Marker(entity) == null
        && FeralKinshipCompanionSystem.GetDomesticationStatus(entity)?.GetBool("feralKinshipExpeditionAway") != true
        && ValidLinkBox(savedCollisionBox) && ValidLinkBox(savedOriginCollisionBox) && ValidLinkBox(savedSelectionBox) && ValidLinkBox(savedOriginSelectionBox);
    private static bool ValidLinkBox(Cuboidf? box) => box != null && box.XSize > 0 && box.YSize > 0 && box.ZSize > 0
        && float.IsFinite(box.XSize) && float.IsFinite(box.YSize) && float.IsFinite(box.ZSize);
    internal void ObserveExpeditionPresentationUpdates()
    {
        if(entity.Api.Side!=EnumAppSide.Client || expeditionPresentationListenerAttached)return;
        entity.WatchedAttributes.RegisterModifiedListener(RetainedNativeSuspension.Key,OnPresentationMarkerUpdated);
        entity.WatchedAttributes.RegisterModifiedListener("domesticationstatus",OnPresentationStatusUpdated);
        expeditionPresentationListenerAttached=true;
    }
    private void ForgetExpeditionPresentationUpdates()
    {
        if(!expeditionPresentationListenerAttached)return;
        entity.WatchedAttributes.UnregisterListener(OnPresentationMarkerUpdated);
        entity.WatchedAttributes.UnregisterListener(OnPresentationStatusUpdated);
        expeditionPresentationListenerAttached=false;
    }
}
