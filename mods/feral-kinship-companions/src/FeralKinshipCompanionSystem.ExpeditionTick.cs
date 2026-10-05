using System;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VintageStoryConfigMigration;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private void OnExpeditionTick(float _)
    {
        if (!companionContentReady || serverApi == null || packRepository?.Loaded != true)
        {
            return;
        }

        PruneJuvenileThreats(UtcNowMs());
        Entity[] loadedCompanions = loadedFoxes.Values.ToArray();
        MaintainUndergroundCompanionRecovery(loadedCompanions);
        MaintainTargetedAttackOrders(loadedCompanions);
        MaintainWorkCartAssignments(loadedCompanions);
        MaintainWorkCartLoggingQueues();
        MaintainWorkCartChoreQueues();
        MaintainAmbientLife();
        MaintainCampAreaNotifications();

        long nowUtcMs = UtcNowMs();
        MaintainBackpackDeliveries(nowUtcMs);
        MaintainDialogueRuntime(nowUtcMs);
        double nowTotalHours = serverApi.World.Calendar.TotalHours;
        if (TryProcessPendingExpeditionDepartures(nowUtcMs)
            | TryProcessPendingExpeditionReturns(nowUtcMs))
        {
            packRepository.Save();
        }
        if (serverApi.World.ElapsedMilliseconds >= nextCargoDispatchAtMs)
        {
            nextCargoDispatchAtMs = serverApi.World.ElapsedMilliseconds + 1000;
            MaintainPackCargoUnloading();
        }
        Dictionary<string, int> returnNotifications = new(StringComparer.Ordinal);
        foreach (FoxExpeditionRecord expedition in packRepository.GetActiveExpeditions().ToArray())
        {
            // Preserve the entire party and its reward snapshots while any
            // automatically archived member is missing its source content.
            if (ExpeditionHasMissingContent(expedition)) continue;
            bool due = expedition.CompletesTotalHours > 0d
                ? expedition.CompletesTotalHours <= nowTotalHours
                : expedition.CompletesUtcMs <= nowUtcMs;
            if (!due)
            {
                continue;
            }

            bool shouldRunLate = !expedition.DebugForceCompletion && (expedition.Story != null
                ? string.Equals(expedition.Story.ReturnState, "late", StringComparison.Ordinal)
                : ShouldExpeditionRunLate(expedition));
            if (!expedition.RunningLate && shouldRunLate)
            {
                float lateDurationHours = expedition.CompletesTotalHours > 0d
                    ? expedition.Story?.PlannedLateDurationHours > 0f
                        ? expedition.Story.PlannedLateDurationHours
                        : Math.Max(0.25f, expedition.BaseDurationHours
                            * (0.20f + serverApi.World.Rand.NextSingle() * 0.30f))
                    : 0f;
                packRepository.MarkExpeditionLate(
                    expedition.ExpeditionId,
                    nowUtcMs + SecondsToMilliseconds(serverApi.World.Rand.Next(10, 61)),
                    expedition.CompletesTotalHours > 0d
                        ? expedition.CompletesTotalHours + lateDurationHours
                        : 0d,
                    lateDurationHours
                );
                foreach (string foxId in expedition.SelectedFoxIds)
                {
                    SetExpeditionMemberStatus(expedition.OwnerUid, foxId, "Running late");
                }

                packRepository.Save();
                if (serverApi.World.PlayerByUid(expedition.OwnerUid) is IServerPlayer lateOwner
                    && packViewers.Contains(expedition.OwnerUid))
                {
                    string cause = expedition.Story?.DelayCause ?? string.Empty;
                    SendPackState(lateOwner,
                        $"{GetExpeditionDisplayName(expedition.Type)} expedition is running late. "
                        + (string.IsNullOrWhiteSpace(cause)
                            ? "The companions will return when they can."
                            : "Its field report will explain what delayed the party."));
                }
                continue;
            }

            FoxExpeditionRecord? completed = packRepository.TakeCompletedExpedition(
                expedition.ExpeditionId,
                nowUtcMs,
                nowTotalHours
            );
            if (completed == null)
            {
                continue;
            }

            List<FoxPackLootItemPacket> lootBefore = GetPackLootItems(completed.OwnerUid);
            int lootBatchStartIndex = packRepository.GetLootBytes(completed.OwnerUid).Count;
            ExpeditionResolution resolution = ResolveCompletedExpedition(completed, lootBatchStartIndex);
            AwardExpeditionExperience(completed);
            List<FoxPackLootItemPacket> reportLoot = GetAddedPackLoot(
                lootBefore,
                GetPackLootItems(completed.OwnerUid));
            PlayExpeditionReturnSounds(completed.OwnerUid);
            FoxExpeditionSummaryPacket report = BuildLastExpeditionReport(
                completed,
                resolution.Message,
                nowTotalHours,
                reportLoot
            );
            packRepository.SetLastExpeditionReport(completed.OwnerUid, report);
            if (serverApi.World.PlayerByUid(completed.OwnerUid) is IServerPlayer owner)
            {
                if (string.Equals(completed.Type, FoxExpeditionType.Recruitment, StringComparison.Ordinal))
                {
                    SendOwnerSound(
                        owner,
                        resolution.RecruitmentSucceeded
                            ? CompanionSoundCue.RecruitmentSuccess
                            : CompanionSoundCue.RecruitmentFailure
                    );
                }
                returnNotifications[completed.OwnerUid] = returnNotifications.TryGetValue(completed.OwnerUid, out int count)
                    ? count + 1
                    : 1;
                if (packViewers.Contains(completed.OwnerUid))
                {
                    SendPackState(owner, resolution.Message);
                }
            }
            packRepository.Save();
        }
        foreach ((string ownerUid, int count) in returnNotifications)
        {
            if (serverApi.World.PlayerByUid(ownerUid) is IServerPlayer owner)
            {
                SendExpeditionReturnNotification(owner, count);
                foreach (FoxExpeditionSummaryPacket report in packRepository.GetPendingExpeditionReports(ownerUid))
                {
                    report.NotificationPending = false;
                }
                packRepository.Save();
            }
        }
        FlushPendingPackStates();
    }

}
