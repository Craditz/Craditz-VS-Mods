#nullable enable
using Vintagestory.API.Common;
namespace FeralKinshipCompanions;

public sealed partial class EntityBehaviorFeralKinshipFoxSocial
{
    private bool expeditionPresentationListenerAttached;
    private int presentationMarkerUpdates,presentationStatusUpdates;
    private void OnPresentationMarkerUpdated(){presentationMarkerUpdates++;RefreshExpeditionSuspension();}
    private void OnPresentationStatusUpdated(){presentationStatusUpdates++;RefreshExpeditionSuspension();}
    internal string DescribePresentationUpdates()=> $"social-behavior=present listeners={expeditionPresentationListenerAttached} marker-updates={presentationMarkerUpdates} status-updates={presentationStatusUpdates} latch={expeditionSuspended}";
    internal bool HasExpeditionPresentationSuppression => expeditionSuspended;
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
