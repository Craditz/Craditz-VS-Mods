using System;
using System.Linq;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private void CheckpointLoadedCompanionHealth()
    {
        try
        {
            var authority = CaptureCompanionPersistenceAuthority();
            if (authority == null) return;

            bool changed = false;
            foreach (Entity entity in loadedFoxes.Values.ToArray())
            {
                try
                {
                    if (!TryGetVerifiedCompanionRecord(entity, authority, out FoxPackRecordV2? record)
                        || record == null || !record.DurableStateInitialized
                        || string.IsNullOrWhiteSpace(record.OwnerUid)
                        || record.HealthState != entity.WatchedAttributes.GetInt(EntityHealthStateKey)) continue;

                    var health = entity.GetBehavior<EntityBehaviorHealth>();
                    var tree = entity.WatchedAttributes.GetTreeAttribute("health");
                    // Missing/malformed native attributes must never become a
                    // full-health default. Preserve exact valid floats, even
                    // when the change is only one representable float step.
                    if (health == null || tree?["currenthealth"] is not FloatAttribute current
                        || tree["maxhealth"] is not FloatAttribute maximum
                        || !float.IsFinite(current.value) || current.value < 0f
                        || !float.IsFinite(maximum.value) || maximum.value <= 0f
                        || health.Health != current.value || health.MaxHealth != maximum.value) continue;

                    if (record.CurrentHealth == current.value && record.MaxHealth == maximum.value) continue;
                    record.CurrentHealth = current.value;
                    record.MaxHealth = maximum.value;
                    changed = true;
                }
                catch (Exception exception)
                {
                    serverApi?.Logger.Warning("[Companions] Skipped unavailable shutdown health snapshot for entity {0}: {1}",
                        entity.EntityId, exception.GetType().Name);
                }
            }

            if (changed) packRepository!.Save();
        }
        catch (Exception exception)
        {
            // Keep normal engine shutdown/final-save running; an unsuccessful
            // checkpoint remains visible rather than silently refilling HP.
            serverApi?.Logger.Error("[Companions] Shutdown health checkpoint failed: {0}", exception);
        }
    }
}
