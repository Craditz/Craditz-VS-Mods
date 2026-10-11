#nullable enable

using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    // The existing milestones belong to nearby Companions, not to the player
    // who placed or lit a machine. Reuse exactly the block-discovery volume.
    internal void RecordProductionCompletion(BlockPos pos, string machineEvent, bool refractory)
    {
        if (serverApi == null || StfuModeEnabled || dialogueService == null) return;
        string[] candidates = refractory
            ? new[] { machineEvent, "milestone.refractory" }
            : new[] { machineEvent };
        foreach (Entity companion in loadedFoxes.Values.ToArray())
        {
            if (!IsDialogueCompanionPresent(companion)
                || !CompanionProductionMilestoneRules.IsInDiscoveryVolume(companion.Pos.AsBlockPos, pos)) continue;
            var status = GetDomesticationStatus(companion);
            if (status == null || string.IsNullOrWhiteSpace(GetCompanionOwnerUid(companion))) continue;
            IServerPlayer? owner = serverApi.World.PlayerByUid(GetCompanionOwnerUid(companion)) as IServerPlayer;
            ApplyFirstSeenMilestones(companion, status, owner, candidates);
        }
    }
}

internal static class CompanionProductionMilestoneRules
{
    internal static bool IsInDiscoveryVolume(BlockPos companion, BlockPos production) =>
        companion.dimension == production.dimension
        && production.X - (long)companion.X is >= -5 and <= 5
        && production.Y - (long)companion.Y is >= -2 and <= 3
        && production.Z - (long)companion.Z is >= -5 and <= 5;

    internal static bool WasConverted(ItemStack? before, ItemStack? after) =>
        before?.StackSize > 0 && after?.StackSize > 0
        && !ReferenceEquals(before, after)
        && before.Collectible?.Code != null && after.Collectible?.Code != null
        && !before.Collectible.Code.Equals(after.Collectible.Code);

    internal static bool IsRefractoryConversion(ItemStack? before, ItemStack? after)
    {
        if (!WasConverted(before, after)) return false;
        var raw = before!.Collectible.Code;
        var fired = after!.Collectible.Code;
        if (raw.Domain != "game" || fired.Domain != "game") return false;
        return new[] { "tier1", "tier2", "tier3" }.Any(tier =>
            raw.Path == "refractorybrick-raw-" + tier && fired.Path == "refractorybrick-fired-" + tier);
    }
}
