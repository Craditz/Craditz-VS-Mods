#nullable enable
using Vintagestory.API.Common.Entities;
namespace FeralKinshipCompanions;
public sealed partial class FeralKinshipCompanionSystem
{
    private void CreditWellbeingRequest(Entity entity, int completed, string request, bool developer)
    {
        if (developer || packRepository?.Loaded != true) return;
        string id = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (!packRepository.TryGetRecord(id, out var record) || record == null) return;
        record.Wellbeing = CompanionWellbeingRules.Normalize(record.Wellbeing);
        CompanionWellbeingEvents.Request(record.Wellbeing, completed, request, developer);
    }
    private void CreditWellbeingProgress(Entity entity, int level, bool eligible)
    {
        if (!eligible || packRepository?.Loaded != true) return;
        string id = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (packRepository.TryGetRecord(id, out var record) && record != null) CreditWellbeingProgress(record, level, eligible);
    }
    private void CreditWellbeingProgress(FoxPackRecordV2 record, int level, bool eligible)
    {
        record.Wellbeing = CompanionWellbeingRules.Normalize(record.Wellbeing);
        CompanionWellbeingEvents.Progress(record.Wellbeing, level, eligible);
    }
}
