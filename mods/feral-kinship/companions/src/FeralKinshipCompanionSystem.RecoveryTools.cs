#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private readonly HashSet<string> pendingRecoveryChecks = new(StringComparer.Ordinal);
    private readonly HashSet<string> pendingAdminCompanionTeleports = new(StringComparer.Ordinal);
    private readonly HashSet<string> pendingDeveloperLedgerOpens = new(StringComparer.Ordinal);

    private void OpenDeveloperCompanion(IServerPlayer owner, string foxId)
    {
        if (serverApi == null || packRepository?.Loaded != true
            || !packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
            || record == null
            || record.Archived
            || !string.Equals(record.OwnerUid, owner.PlayerUID, StringComparison.Ordinal))
        {
            return;
        }

        Entity? loaded = FindLoadedCompanionForRecovery(record.FoxId);
        if (loaded != null)
        {
            socialViewByOwner[owner.PlayerUID] = loaded.EntityId;
            SendState(loaded, owner, string.Empty);
            return;
        }

        if (!record.HasLastKnownPosition)
        {
            SendDeveloperRecordState(
                owner,
                record,
                "Saved record only. The last-known location is unavailable; live-only tools cannot be used yet.");
            return;
        }

        string pendingKey = owner.PlayerUID + ":" + record.FoxId;
        if (!pendingDeveloperLedgerOpens.Add(pendingKey))
        {
            SendDeveloperRecordState(owner, record, "Loading the saved location. Teleport and rebuild remain available.");
            return;
        }

        int chunkX = (int)Math.Floor(record.LastKnownX / (double)GlobalConstants.ChunkSize);
        int chunkZ = (int)Math.Floor(record.LastKnownZ / (double)GlobalConstants.ChunkSize);
        int checks = 0;
        const int MaximumChecks = 20;

        void Finish(string message)
        {
            pendingDeveloperLedgerOpens.Remove(pendingKey);
            SendDeveloperRecordState(owner, record, message);
        }

        void CheckLoadedEntity()
        {
            if (!pendingDeveloperLedgerOpens.Contains(pendingKey) || serverApi == null)
            {
                return;
            }

            RefreshLoadedFoxPackRecords();
            Entity? existing = FindLoadedCompanionForRecovery(record.FoxId);
            if (existing != null)
            {
                pendingDeveloperLedgerOpens.Remove(pendingKey);
                socialViewByOwner[owner.PlayerUID] = existing.EntityId;
                SendState(existing, owner, string.Empty);
                return;
            }

            if (++checks >= MaximumChecks)
            {
                Finish("Saved record only. The original Companion was not found in its last-known chunk; teleport or rebuild can be tried explicitly.");
                return;
            }

            serverApi.World.RegisterCallback(_ => CheckLoadedEntity(), 500);
        }

        void BeginChecks()
        {
            if (serverApi == null || !pendingDeveloperLedgerOpens.Contains(pendingKey))
            {
                return;
            }

            // OnLoaded runs while the source column is definitely resident.
            // Checking from that callback avoids missing a distant entity when
            // a non-forced column is unloaded again before a timer fires.
            CheckLoadedEntity();
        }

        try
        {
            SendDeveloperRecordState(owner, record, "Loading the saved location. Teleport and rebuild remain available.");
            if (record.LastKnownDimension == 0)
            {
                serverApi.WorldManager.LoadChunkColumnPriority(
                    chunkX,
                    chunkZ,
                    new ChunkLoadOptions
                    {
                        KeepLoaded = false,
                        OnLoaded = BeginChecks
                    });
            }
            else
            {
                serverApi.WorldManager.LoadChunkColumnForDimension(chunkX, chunkZ, record.LastKnownDimension);
                serverApi.World.RegisterCallback(_ => BeginChecks(), 1000);
            }
        }
        catch (Exception exception)
        {
            pendingDeveloperLedgerOpens.Remove(pendingKey);
            serverApi.Logger.Warning(
                "[FeralKinshipCompanions] Developer Ledger could not load companion {0}: {1}",
                record.FoxId,
                exception.Message);
            SendDeveloperRecordState(owner, record, "The saved chunk could not be loaded. The ledger record remains intact.");
        }
    }

    private void SendDeveloperRecordState(IServerPlayer owner, FoxPackRecordV2 record, string message)
    {
        serverChannel?.SendPacket(new FoxSocialStatePacket
        {
            TargetEntityId = record.EntityId,
            FoxId = record.FoxId,
            Number = record.Number,
            Name = string.IsNullOrWhiteSpace(record.Name) ? "Unnamed companion" : record.Name,
            Personality = GetPersonalityLabel(record.Personality),
            RequestsGenerated = record.RequestsGenerated,
            RequestsCompleted = record.RequestsCompleted,
            Points = record.Points,
            Mood = string.IsNullOrWhiteSpace(record.Mood) ? "Unassigned" : record.Mood,
            ActiveRequest = string.IsNullOrWhiteSpace(record.ActiveRequest) ? "None" : record.ActiveRequest,
            ProgressSeconds = Math.Max(0f, record.RequestProgress),
            DurationSeconds = FoxRequestCatalog.GetDurationSeconds(record.ActiveRequestId),
            LastCompleted = string.IsNullOrWhiteSpace(record.LastCompleted) ? "None" : record.LastCompleted,
            RequestCooldownSeconds = Math.Max(0f, (record.RequestCooldownEndsUtcMs - UtcNowMs()) / 1000f),
            CancelCooldownSeconds = Math.Max(0f, (record.CancelCooldownEndsUtcMs - UtcNowMs()) / 1000f),
            Message = message,
            SpeciesId = string.IsNullOrWhiteSpace(record.SpeciesId) ? "fox" : record.SpeciesId,
            SpeciesDisplayName = GetCompanionSpeciesDisplayName(record),
            IsJuvenile = record.IsJuvenile,
            CurrentHealth = record.CurrentHealth,
            MaxHealth = record.MaxHealth,
            Level = Math.Max(CompanionProgressionRules.StartingLevel, record.Level),
            CurrentLevelExperience = Math.Max(0L, record.CurrentLevelExperience),
            RequiredLevelExperience = CompanionProgressionRules.GetRequiredExperience(record.Level),
            LifetimeExperience = Math.Max(record.CurrentLevelExperience, record.LifetimeExperience),
            OwnedCompanionCount = packRepository?.GetRecordsForOwner(owner.PlayerUID).Count ?? 0,
            DeveloperEntityLoaded = false
        }, owner);
    }

    private void TeleportCompanionDeveloper(IServerPlayer admin, string foxId)
    {
        if (serverApi == null || packRepository?.Loaded != true
            || !packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
            || record == null
            || !string.Equals(record.OwnerUid, admin.PlayerUID, StringComparison.Ordinal))
        {
            SendAdminCompanionMessage(admin, "Teleport refused: that companion is not in your pack ledger.");
            return;
        }
        string validationError = ValidateAdminTeleportRecord(record);
        if (!string.IsNullOrEmpty(validationError)) { SendAdminCompanionMessage(admin, validationError); return; }

        Entity? loaded = FindLoadedCompanionForRecovery(record.FoxId);
        if (loaded != null)
        {
            if (TryTeleportExistingCompanionToPlayer(loaded, record, admin, out string error))
                SendAdminCompanionMessage(admin, $"Moving {GetFoxRecordLabel(record)} to a safe spot beside you...");
            else SendAdminCompanionMessage(admin, "Teleport failed: " + error);
            return;
        }
        if (!record.HasLastKnownPosition) { SendAdminCompanionMessage(admin, "Teleport refused: no last-known location is saved. No replacement was spawned."); return; }
        if (record.LastKnownDimension != admin.Entity?.Pos.Dimension) { SendAdminCompanionMessage(admin, "Teleport refused: the saved companion is in another dimension."); return; }

        string pendingKey = admin.PlayerUID + ":" + record.FoxId;
        if (pendingRecoveryChecks.Contains(record.FoxId) || !pendingAdminCompanionTeleports.Add(pendingKey))
        { SendAdminCompanionMessage(admin, "Teleport refused: a saved-location lookup is already in progress."); return; }

        int chunkX = (int)Math.Floor(record.LastKnownX / (double)GlobalConstants.ChunkSize);
        int chunkZ = (int)Math.Floor(record.LastKnownZ / (double)GlobalConstants.ChunkSize);
        int checks = 0;
        const int MaximumChecks = 20;
        void Finish(string message) { pendingAdminCompanionTeleports.Remove(pendingKey); SendAdminCompanionMessage(admin, message); }
        void CheckLoadedEntity()
        {
            if (!pendingAdminCompanionTeleports.Contains(pendingKey) || serverApi == null) return;
            RefreshLoadedFoxPackRecords();
            Entity? existing = FindLoadedCompanionForRecovery(record.FoxId);
            if (existing != null)
            {
                pendingAdminCompanionTeleports.Remove(pendingKey);
                if (TryTeleportExistingCompanionToPlayer(existing, record, admin, out string error))
                    SendAdminCompanionMessage(admin, $"Teleported the existing companion {GetFoxRecordLabel(record)} to you.");
                else SendAdminCompanionMessage(admin, "Teleport failed: " + error);
                return;
            }
            if (++checks >= MaximumChecks) { Finish("The saved chunk was checked, but the original companion was not loaded. No replacement was spawned."); return; }
            serverApi.World.RegisterCallback(_ => CheckLoadedEntity(), 500);
        }

        void BeginChecks()
        {
            if (serverApi == null || !pendingAdminCompanionTeleports.Contains(pendingKey)) return;
            CheckLoadedEntity();
        }

        try
        {
            if (record.LastKnownDimension == 0)
                serverApi.WorldManager.LoadChunkColumnPriority(
                    chunkX,
                    chunkZ,
                    new ChunkLoadOptions
                    {
                        KeepLoaded = false,
                        OnLoaded = BeginChecks
                    });
            else serverApi.WorldManager.LoadChunkColumnForDimension(chunkX, chunkZ, record.LastKnownDimension);
            if (record.LastKnownDimension != 0)
                serverApi.World.RegisterCallback(_ => BeginChecks(), 1000);
            SendAdminCompanionMessage(admin, "Loading the existing companion's last-known chunk. No replacement will be spawned.");
        }
        catch (Exception exception)
        {
            pendingAdminCompanionTeleports.Remove(pendingKey);
            serverApi.Logger.Warning("[FeralKinshipCompanions] Developer teleport lookup could not load the saved chunk for {0}: {1}", record.FoxId, exception.Message);
            SendAdminCompanionMessage(admin, "The saved chunk could not be loaded. No replacement was spawned.");
        }
    }

    private string ValidateAdminTeleportRecord(FoxPackRecordV2 record)
    {
        if (record.Archived) return "Teleport refused: that companion is archived.";
        if (string.Equals(record.Status, "Dead", StringComparison.OrdinalIgnoreCase) || string.Equals(record.Status, "Unowned", StringComparison.OrdinalIgnoreCase)) return $"Teleport refused: that companion's ledger status is {record.Status}.";
        if (!string.IsNullOrWhiteSpace(record.PendingReturnStatus) || string.Equals(record.Status, "Awaiting safe return", StringComparison.OrdinalIgnoreCase) || packRepository?.GetExpeditionForMember(record.OwnerUid, record.FoxId) != null) return "Teleport refused: that companion is assigned to an expedition or pending return.";
        return string.Empty;
    }

    private bool TryTeleportExistingCompanionToPlayer(Entity companion, FoxPackRecordV2 record, IServerPlayer admin, out string error)
    {
        error = string.Empty;
        if (serverApi == null || admin.Entity == null) { error = "The administrator's player entity is unavailable."; return false; }
        ITreeAttribute? status = GetDomesticationStatus(companion);
        if (!companion.Alive || !IsTamedFox(companion) || status == null || !string.Equals(status.GetString(FoxIdKey, string.Empty), record.FoxId, StringComparison.Ordinal) || !string.Equals(status.GetString("owner", string.Empty), record.OwnerUid, StringComparison.Ordinal)) { error = "The loaded entity does not match that owned companion record."; return false; }
        if (companion.Pos.Dimension != admin.Entity.Pos.Dimension) { error = "The existing companion is in another dimension."; return false; }
        Vec3d? safePosition = FindSafeEntityPosition(admin.Entity.Pos.AsBlockPos, companion.Properties, companion, 8, 3);
        if (safePosition == null) { error = "No safe open space was found near the administrator."; return false; }
        ClearAutomaticRetreat(companion);
        CancelTargetedAttack(companion);
        if (companion is EntityAgent agent) { agent.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.StopTasks(); agent.Controls.StopAllMovement(); agent.Pos.Motion.Set(0, 0, 0); }
        SetFoxAwayFromWorld(companion, false);
        SetCompanionActivityState(companion, CompanionActivityMode.AtEase);
        companion.TeleportToDouble(safePosition.X, safePosition.Y, safePosition.Z, () =>
        {
            if (serverApi == null || !companion.Alive) return;
            RegisterLoadedFox(companion); RegisterFoxInPack(companion); packRepository?.Save();
            SendAdminCompanionMessage(admin, $"Teleported the existing companion {GetFoxRecordLabel(record)} to you.");
            SendState(companion, admin, "Teleport complete.");
        });
        return true;
    }

    private void SendAdminCompanionMessage(IServerPlayer player, string message) => player.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);

    private Entity? FindLoadedCompanionForRecovery(string foxId)
    {
        Entity? known = FindLoadedCompanionByFoxId(foxId);
        if (known?.Alive == true) return known;
        if (serverApi == null) return null;
        foreach (Entity candidate in serverApi.World.LoadedEntities.Values)
        {
            if (!candidate.Alive || !IsTamedFox(candidate)) continue;
            if (!string.Equals(GetDomesticationStatus(candidate)?.GetString(FoxIdKey, string.Empty), foxId, StringComparison.Ordinal)) continue;
            RegisterLoadedFox(candidate); return candidate;
        }
        return null;
    }

    private bool CanRestoreFromLedger(FoxPackRecordV2 record, out string reason)
    {
        if (!record.DurableStateInitialized) { reason = "the persistent companion record is incomplete"; return false; }
        if (ResolveRecoveryEntityType(record, out reason) == null) return false;
        reason = string.Empty; return true;
    }

    private void SendRecoveryLookupResult(FoxPackRecordV2 record, Entity? recovered)
    {
        if (serverApi == null || packRepository == null) return;
        if (recovered?.Alive == true)
        {
            record.RecoveryStatus = CompanionRecoveryStatus.Recoverable; SetFoxAwayFromWorld(recovered, true); SetExpeditionMemberStatus(record.OwnerUid, record.FoxId, "Returned healthy"); packRepository.Save(); SendPackStateIfOwnerOnline(record.OwnerUid, $"{GetFoxRecordLabel(record)} was recovered and brought home."); return;
        }
        if (CanRestoreFromLedger(record, out string reason))
        {
            record.RecoveryStatus = CompanionRecoveryStatus.Recoverable; record.Status = "Recovery available"; packRepository.Save(); SendPackStateIfOwnerOnline(record.OwnerUid, "The saved chunk did not load the original companion. Its complete pack record remains recoverable through the Pack Ledger."); return;
        }
        MarkCompanionUnrecoverable(record, reason); packRepository.Save(); SendPackStateIfOwnerOnline(record.OwnerUid, "The rescue trail was found, but restoration failed: " + reason + ".");
    }
}
