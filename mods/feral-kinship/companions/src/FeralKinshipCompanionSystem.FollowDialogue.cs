#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private const string NextFollowEasterEggKey = "feralKinshipNextFollowEasterEggUtcMs";
    private readonly Dictionary<string, long> nextFollowIdleLineByOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> nextFollowIdleVariantByOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FollowIdleWaitingWindow> followWaitingWindows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> lastFollowPriorityByOwner = new(StringComparer.Ordinal);
    private long nextFollowWindowScan;

    private void MaintainFollowWaitingWindows(long now)
    {
        if (serverApi == null || now < nextFollowWindowScan) return;
        nextFollowWindowScan = now + 1000;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in loadedFoxes.Values.Where(entity => entity.Alive && !IsFoxAwayFromWorld(entity)
                     && GetCompanionActivityMode(entity) == CompanionActivityMode.Follow)
                 .GroupBy(GetCompanionOwnerUid))
        {
            Entity companion = group.First();
            if (!TryGetOwnerPlayer(companion, out IServerPlayer? owner) || owner?.Entity == null) continue;
            seen.Add(group.Key);
            if (!followWaitingWindows.TryGetValue(group.Key, out FollowIdleWaitingWindow? window))
                followWaitingWindows[group.Key] = window = new FollowIdleWaitingWindow();
            bool harmless = !StfuModeEnabled && group.Any(candidate => IsHarmlessFollowWaiting(candidate, owner, now));
            window.Observe(now, owner.Entity.Pos.X, owner.Entity.Pos.Y, owner.Entity.Pos.Z,
                owner.Entity.Pos.Dimension, harmless);
        }
        foreach (string key in followWaitingWindows.Keys.Where(key => !seen.Contains(key)).ToArray())
            followWaitingWindows.Remove(key);
    }

    internal void TryEmitFollowIdleDialogue(Entity companion, bool attention, bool resting = false, bool pacing = false)
    {
        if (!TryGetOwnerPlayer(companion, out IServerPlayer? owner) || owner == null) return;
        long now = UtcNowMs();
        if (now < nextFollowIdleLineByOwner.GetValueOrDefault(owner.PlayerUID)
            || !IsHarmlessFollowWaiting(companion, owner, now)) return;
        // One opportunity per owner interval, even if suppression prevents speech; never queue/retry a joke.
        nextFollowIdleLineByOwner[owner.PlayerUID] = now + 90000;
        if (followWaitingWindows.TryGetValue(owner.PlayerUID, out FollowIdleWaitingWindow? window)
            && window.CanRoll(now, owner.Entity.WatchedAttributes.GetLong(NextFollowEasterEggKey, 0), Random.Shared.NextDouble()))
        {
            bool fakeSafe = IsSafeForFakeDanger(companion, owner, now);
            bool emittedEgg = EmitDialogueEvent(companion, owner, "idle.follow.easteregg", string.Empty,
                CompanionDialoguePriority.Low, "idle.follow",
                new Dictionary<string, string> { ["safeForFakeDanger"] = fakeSafe ? "true" : "false" });
            if (emittedEgg)
            {
                window.MarkEmitted();
                owner.Entity.WatchedAttributes.SetLong(NextFollowEasterEggKey, now + FollowIdleWaitingWindow.EasterCooldownMs);
                owner.Entity.WatchedAttributes.MarkPathDirty(NextFollowEasterEggKey);
                return;
            }
        }
        int variant = nextFollowIdleVariantByOwner.GetValueOrDefault(owner.PlayerUID) % 2;
        string eventId = resting ? "idle.follow.close_rest" : pacing ? "idle.follow.pacing" : attention ? "idle.follow.attention"
            : variant == 0 ? "idle.follow.waiting" : "idle.follow.restless";
        bool emitted = EmitDialogueEvent(companion, owner, eventId, string.Empty,
            CompanionDialoguePriority.Low, "idle.follow");
        if (emitted && !attention && !resting && !pacing) nextFollowIdleVariantByOwner[owner.PlayerUID] = variant + 1;
    }

    private bool IsHarmlessFollowWaiting(Entity companion, IServerPlayer owner, long now)
    {
        if (StfuModeEnabled || !owner.Entity.Alive || !companion.Alive || IsFoxAwayFromWorld(companion)
            || GetCompanionActivityMode(companion) != CompanionActivityMode.Follow
            || companion.Pos.Dimension != owner.Entity.Pos.Dimension
            || companion.Pos.SquareDistanceTo(owner.Entity.Pos) > 12*12
            || IsTemporalStormActive() || !IsHealthyCalmFollowSpeaker(companion, now)
            || IsConversationPriorityBlockedNear(companion, now)
            || activeConversationByOwner.ContainsKey(owner.PlayerUID)) return false;
        // Conservatively suppress even normal jokes while known threats are nearby.
        return !companion.World.GetEntitiesAround(owner.Entity.Pos.XYZ, 24, 16,
            other => other.Alive && other.Pos.Dimension == companion.Pos.Dimension
                && other.EntityId != companion.EntityId && !IsTamedFox(other) && IsKnownHostile(other)).Any();
    }

    private bool IsHealthyCalmFollowSpeaker(Entity companion, long now)
    {
        var status = GetDomesticationStatus(companion);
        return status != null && !IsFoxIncapacitated(companion) && GetHealthFraction(companion) >= 0.999f
            && !IsCompanionFoodRestricted(companion) && !IsRecentlyInCombat(companion, now)
            && !IsInConversationCombatAftermath(companion, now)
            && !status.GetBool(AutomaticRetreatActiveKey, false) && !status.GetBool(TargetedAttackActiveKey, false)
            && status.GetLong(EarlyWarningEndsUtcMsKey, 0) <= now
            && string.IsNullOrWhiteSpace(status.GetString(ActiveRequestKey, string.Empty))
            && GetCompanionCombatStyle(companion) != CompanionCombatStyle.Flee
            && GetMood(companion) is not ("alarmed" or "anxious")
            && !string.IsNullOrWhiteSpace(status.GetString("owner", string.Empty));
    }

    internal static bool IsFollowIdleBubbleContextValid(Entity companion, Entity owner)
    {
        var status = GetDomesticationStatus(companion);
        return owner.Alive && companion.Alive && status != null
            && owner.Pos.Dimension == companion.Pos.Dimension && !IsFoxAwayFromWorld(companion)
            && GetCompanionActivityMode(companion) == CompanionActivityMode.Follow
            && !IsFoxIncapacitated(companion) && !IsRecentlyInCombat(companion, UtcNowMs())
            && !status.GetBool(AutomaticRetreatActiveKey, false) && !status.GetBool(TargetedAttackActiveKey, false)
            && status.GetLong(EarlyWarningEndsUtcMsKey, 0) <= UtcNowMs()
            && GetMood(companion) is not ("alarmed" or "anxious");
    }

    private bool IsSafeForFakeDanger(Entity companion, IServerPlayer owner, long now)
    {
        if (!IsHarmlessFollowWaiting(companion, owner, now)
            || temporalStabilitySystem?.StormData == null
            || GetHealthFraction(owner.Entity) < 0.999f
            || now-lastFollowPriorityByOwner.GetValueOrDefault(owner.PlayerUID) < 120000) return false;
        // Fail closed for all other nearby agents, including unknown/modded creatures.
        // Only the owner and their healthy, calm companions qualify as established-safe company.
        foreach (Entity nearby in companion.World.GetEntitiesAround(owner.Entity.Pos.XYZ, 32, 24,
                     other => other.Alive && other is EntityAgent))
        {
            if (nearby.EntityId == owner.Entity.EntityId) continue;
            if (!IsTamedFox(nearby) || GetCompanionOwnerUid(nearby) != owner.PlayerUID
                || !IsHealthyCalmFollowSpeaker(nearby, now)
                || IsConversationPriorityBlockedNear(nearby, now)) return false;
        }
        return true;
    }
}
