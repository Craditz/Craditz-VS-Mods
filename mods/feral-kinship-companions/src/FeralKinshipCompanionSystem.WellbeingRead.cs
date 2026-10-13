#nullable enable
using System;
using System.Collections.Generic;
using Vintagestory.API.Server;
namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    internal event Action<CompanionWellbeingReadResponse>? WellbeingReadReceived;
    private readonly Dictionary<string, long> wellbeingReadAtMs = new();
    internal void RequestWellbeingRead(string foxId, string requestId) =>
        clientChannel?.SendPacket(new CompanionWellbeingReadRequest { FoxId = foxId, RequestId = requestId });
    private void OnWellbeingReadResponse(CompanionWellbeingReadResponse response) =>
        clientApi?.Event.EnqueueMainThreadTask(() => WellbeingReadReceived?.Invoke(response),
            "feralkinshipcompanions-wellbeing-read");
    private void OnWellbeingReadRequest(IServerPlayer owner, CompanionWellbeingReadRequest request)
    {
        if (serverApi == null || packRepository?.Loaded != true || !IsOnlineServerPlayer(owner)
            || string.IsNullOrWhiteSpace(request.FoxId) || request.FoxId.Length > 128
            || !Guid.TryParseExact(request.RequestId, "N", out _)) return;
        var response = new CompanionWellbeingReadResponse { FoxId = request.FoxId, RequestId = request.RequestId };
        if (!packRepository.TryGetRecord(request.FoxId, out var record) || record == null
            || record.Archived || !string.Equals(record.OwnerUid, owner.PlayerUID, StringComparison.Ordinal))
        {
            response.Message = "This companion is not available in your active pack.";
            serverChannel?.SendPacket(response, owner);
            return;
        }
        long now = serverApi.World.ElapsedMilliseconds;
        if (wellbeingReadAtMs.TryGetValue(owner.PlayerUID, out long previous) && now - previous < 250)
        {
            response.Message = "Please reopen Mood details in a moment.";
            serverChannel?.SendPacket(response, owner);
            return;
        }
        wellbeingReadAtMs[owner.PlayerUID] = now;
        var entity = FindLoadedCompanionByFoxId(record.FoxId);
        if (entity == null || !entity.Alive || IsFoxAwayFromWorld(entity)
            || !string.Equals(GetCompanionOwnerUid(entity), owner.PlayerUID, StringComparison.Ordinal))
        {
            response.Message = "Companion unavailable: wellbeing is paused. Reopen when they are nearby.";
            serverChannel?.SendPacket(response, owner);
            return;
        }
        // Re-read authoritative entity and block state on every opening. No client values are accepted.
        if (!IsFoxIncapacitated(entity)) UpdateCompanionFood(entity);
        RegisterFoxInPack(entity);
        ObserveWellbeingSafety(entity, record, force: true);
        CompanionWellbeingRecovery.Update(record.Wellbeing);
        packRepository.Save();
        if (record.Wellbeing?.Version != CompanionWellbeingRules.Version)
        {
            response.Message = "This wellbeing record needs a compatible mod version.";
            serverChannel?.SendPacket(response, owner);
            return;
        }
        response.Live = true;
        response.Mood = GetMood(entity);
        response.Wellbeing = record.Wellbeing?.Copy();
        response.Message = "Food and bedding";
        serverChannel?.SendPacket(response, owner);
        if (packViewers.Contains(owner.PlayerUID)) SendPackState(owner);
    }
}
