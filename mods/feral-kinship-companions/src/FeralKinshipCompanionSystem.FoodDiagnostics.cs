#nullable enable
using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
namespace FeralKinshipCompanions;
public sealed partial class FeralKinshipCompanionSystem
{
    private TextCommandResult OnCompanionFoodListCommand(TextCommandCallingArgs args)
    {
        if (serverApi == null || args.Caller.Player is not IServerPlayer player || player.Entity == null || packRepository?.Loaded != true)
            return TextCommandResult.Error("Use this in a loaded world.");
        if (!CanUseDeveloperTools(player)) return TextCommandResult.Error("Food inspection is available in Creative mode or to server administrators.");
        string pageText = args.Parsers.Count > 0 && !args.Parsers[0].IsMissing ? (string?)args[0] ?? "1" : "1";
        string filter = args.Parsers.Count > 1 && !args.Parsers[1].IsMissing ? ((string?)args[1] ?? "").Trim() : "";
        if (!int.TryParse(pageText, out int page) || page < 1 || filter.Length > 100)
            return TextCommandResult.Error("Usage: /companionfoodlist [page number] [display filter]");
        var entity = serverApi.World.GetEntitiesAround(player.Entity.Pos.XYZ,16,16)
            .Where(e => e.Alive && e.Pos.Dimension == player.Entity.Pos.Dimension && !IsFoxAwayFromWorld(e)
                && string.Equals(GetCompanionOwnerUid(e),player.PlayerUID,StringComparison.Ordinal))
            .OrderBy(e => e.Pos.SquareDistanceTo(player.Entity.Pos)).FirstOrDefault();
        if (entity == null) return TextCommandResult.Error("No loaded, owned companion within16blocks. Stand beside the companion to inspect.");
        var options = GetWellbeingFoodOptions(entity);
        string foxId = GetDomesticationStatus(entity)?.GetString(FoxIdKey,string.Empty) ?? string.Empty;
        packRepository.TryGetRecord(foxId,out var record);
        string favorite = record?.Wellbeing?.FavoriteFoodCode ?? string.Empty;
        var matching = options.Where(o => string.IsNullOrEmpty(filter) || FavoriteCode(o.Stack).Contains(filter,StringComparison.OrdinalIgnoreCase)).ToList();
        const int pageSize = 12;
        int pages = Math.Max(1,(matching.Count+pageSize-1)/pageSize);
        if (page > pages) return TextCommandResult.Error($"Page {page} does not exist. Available pages:1-{pages}.");
        string rows = string.Join("\n",matching.Skip((page-1)*pageSize).Take(pageSize).Select(o =>
            $"{FavoriteCode(o.Stack)} | accepted | {(o.Favorite ? "favorite-eligible" : "not favorite-eligible")}"));
        string result = $"Food inspection: {record?.Name ?? GetFoxDisplayName(entity)}\n"
            + $"Accepted:{options.Count}; favorite-eligible:{options.Count(o=>o.Favorite)}; page:{page}/{pages}; display matches:{matching.Count}\n"
            + $"Candidate patterns:{string.Join(",",companionConfig.FavoriteFoodCandidates ?? Array.Empty<string>())}\n"
            + $"Saved favorite:{(string.IsNullOrEmpty(favorite) ? "none" : favorite)}\n"
            + (string.IsNullOrEmpty(filter) ? "" : $"Display filter:{filter} (selection rules unchanged)\n") + rows;
        serverApi.Logger.Notification("[Companion food inspection] {0}",result);
        return TextCommandResult.Success(result);
    }
}
