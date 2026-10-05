using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private const string AutoNameHandledKey = "feralKinshipAutoNameHandled";

    private bool EnsureCompanionAutoName(Entity entity, out string assignedName)
    {
        assignedName = string.Empty;
        if (!IsTamedFox(entity))
        {
            return false;
        }

        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        if (status.GetBool(AutoNameHandledKey, false))
        {
            return false;
        }

        ITreeAttribute? nameTag = entity.WatchedAttributes.GetTreeAttribute("nametag");
        if (!string.IsNullOrWhiteSpace(nameTag?.GetString("name")))
        {
            status.SetBool(AutoNameHandledKey, true);
            MarkSocialStateDirty(entity);
            return false;
        }

        assignedName = GenerateCompanionName(entity, blacklistedNames: GetUsedCompanionNames(entity));
        AssignCompanionName(entity, assignedName);
        return true;
    }

    private string GenerateCompanionName(
        Entity entity,
        string? avoidName = null,
        ISet<string>? blacklistedNames = null)
    {
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        string personality = status.GetString(PersonalityKey, string.Empty);
        string gender = CompanionBreedingCatalog.TryGetGender(entity, out string detectedGender)
            ? detectedGender
            : string.Empty;
        string[] personalityPool = companionNameConfig.GetEligiblePool(personality, gender);
        string[] generalPool = companionNameConfig.GetGeneralEligiblePool(gender);
        string[] pool = personalityPool;
        if (pool.Length > 1 && entity.World.Rand.NextDouble() >= 0.70d)
        {
            pool = generalPool;
        }

        string[] available = pool
            .Where(name => !IsExcludedName(name, avoidName, blacklistedNames))
            .ToArray();
        if (available.Length == 0 && pool.Length != generalPool.Length)
        {
            available = generalPool
                .Where(name => !IsExcludedName(name, avoidName, blacklistedNames))
                .ToArray();
        }

        if (available.Length == 0)
        {
            available = companionNameConfig.GetAllEligibleNames(gender)
                .Where(name => !IsExcludedName(name, avoidName, blacklistedNames))
                .ToArray();
        }

        // If every configured name is already in use, keep the feature usable
        // by relaxing the blacklist before relaxing the current-name guard.
        if (available.Length == 0)
        {
            available = pool
                .Where(name => !string.Equals(name, avoidName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        if (available.Length == 0)
        {
            available = pool.Length > 0 ? pool : generalPool;
        }

        return available[entity.World.Rand.Next(available.Length)];
    }

    private HashSet<string> GetUsedCompanionNames(Entity subject)
    {
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
        string ownerUid = GetCompanionOwnerUid(subject);
        string subjectFoxId = GetDomesticationStatus(subject)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;

        foreach (Entity other in loadedFoxes.Values)
        {
            if (other.EntityId != subject.EntityId
                && IsTamedFox(other)
                && IsOwner(other, ownerUid))
            {
                AddUsedName(used, GetFoxDisplayName(other));
            }
        }

        if (packRepository != null && !string.IsNullOrWhiteSpace(ownerUid))
        {
            foreach (FoxPackRecordV2 record in packRepository.GetRecordsForOwner(ownerUid)
                .Concat(packRepository.GetArchivedRecordsForOwner(ownerUid)))
            {
                if (record.EntityId != subject.EntityId
                    && !string.Equals(record.FoxId, subjectFoxId, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(record.Name))
                {
                    AddUsedName(used, record.Name);
                }
            }
        }

        return used;
    }

    private static bool IsExcludedName(
        string name,
        string? avoidName,
        ISet<string>? blacklistedNames)
    {
        return string.Equals(name, avoidName, StringComparison.OrdinalIgnoreCase)
            || (blacklistedNames?.Contains(name) == true);
    }

    private static void AddUsedName(ISet<string> names, string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            names.Add(name.Trim());
        }
    }

    private static void AssignCompanionName(Entity entity, string name)
    {
        SetCompanionName(entity, name);
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        status.SetBool(AutoNameHandledKey, true);
        MarkSocialStateDirty(entity);
    }

    private static void SetCompanionName(Entity entity, string name)
    {
        EntityBehaviorNameTag? behavior = entity.GetBehavior<EntityBehaviorNameTag>();
        if (behavior != null)
        {
            behavior.SetName(name);
        }
        else
        {
            ITreeAttribute nameTag = entity.WatchedAttributes.GetTreeAttribute("nametag")
                ?? new TreeAttribute();
            nameTag.SetString("name", name);
            entity.WatchedAttributes.SetAttribute("nametag", nameTag);
            entity.WatchedAttributes.MarkPathDirty("nametag");
        }
    }
}
