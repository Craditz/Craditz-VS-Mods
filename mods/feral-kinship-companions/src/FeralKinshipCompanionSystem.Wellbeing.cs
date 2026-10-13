#nullable enable
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private void ObserveCompanionWellbeing(Entity entity, FoxPackRecordV2 record)
    {
        record.Wellbeing = CompanionWellbeingRules.Normalize(record.Wellbeing);
        bool available = entity.Alive && !IsFoxAwayFromWorld(entity) ;
        if (IsCompanionOwnerOffline(entity) || !available) { wellbeingSafetySamples.Remove(entity.EntityId); return; }
        FoxBedRecord? bed = packRepository?.GetBedForFox(record.FoxId);
        string bedText = "No assigned bed";
        if (bed != null)
        {
            BlockPos pos = new(bed.X, bed.Y, bed.Z, bed.Dimension);
            var accessor = entity.World.BlockAccessor;
            bedText = pos.dimension != entity.Pos.Dimension ? "Assigned; different dimension"
                : accessor.GetChunkAtBlockPos(pos) == null ? "Assigned; area unloaded"
                : accessor.GetBlock(pos).Code?.ToShortString() == bed.BlockCode
                    ? "Assigned; block present" : "Assigned; block changed";
        }
        string rest = GetCompanionActivityMode(entity) == CompanionActivityMode.Rest
            ? HasCompanionRestArrival(entity) ? "Resting at destination" : "Heading to rest"
            : GetMood(entity) == "rested" ? "Rested (current mood)" : "Not currently resting";
        CompanionWellbeingRules.Observe(record.Wellbeing, true, available,
            entity.World.Calendar.TotalHours,
            FoodSystemEnabled ? GetFoodStateLabel(GetCompanionFoodLevel(entity), true) : "Hunger disabled",
            bedText, rest, GetCompanionFoodLevel(entity), FoodSystemEnabled, ResolveWellbeingHome(record.OwnerUid));
        ObserveWellbeingSafety(entity, record);
        UpdateReseedingReadiness(record);
    }
}
