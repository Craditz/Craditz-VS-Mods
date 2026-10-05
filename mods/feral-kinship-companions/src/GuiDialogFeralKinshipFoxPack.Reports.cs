#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;

namespace FeralKinshipCompanions;

internal sealed partial class GuiElementFeralKinshipFoxPackSurface
{
    private const double ReportsTop = ContentY + 72;
    private const double ReportRowsTop = ContentY + 142;
    private const double ReportRowHeight = 110;
    private const double ReportRowsHeight = 420;

    private void DrawReportsOverview(Context ctx, FoxPackStatePacket current, int width)
    {
        if (lastTripDetailsOpen)
        {
            DrawLastTrip(ctx, current, width);
            return;
        }
        List<FoxExpeditionSummaryPacket> reports = GetExpeditionReports(current);
        FoxExpeditionSummaryPacket report = GetSelectedExpeditionReport(current);
        DrawText(ctx, "Expedition reports", 26, ContentY + 21, 29, TextWhite);
        DrawText(ctx, "Read what happened, then open the exact ledger for every calculation and cargo stack.",
            26, ContentY + 49, 15, TextMuted);
        DrawRect(ctx, 18, ReportsTop, 258, 558, TextPanel);
        DrawRect(ctx, 288, ReportsTop, 562, 558, TextPanel);
        DrawRect(ctx, 862, ReportsTop, 300, 558, TextPanel);
        DrawText(ctx, "Recent reports", 32, ReportsTop + 29, 19, TextGold);
        FoxGuiTheme.DrawSectionRule(ctx, 32, ReportsTop + 39, 230);
        if (reports.Count == 0)
        {
            DrawWrapped(ctx, "No completed expedition has been recorded for this pack yet.",
                32, ReportRowsTop + 31, 224, 20, TextMuted, 4, 15);
            return;
        }
        ctx.Save();
        ctx.Rectangle(28, ReportRowsTop, 240, ReportRowsHeight);
        ctx.Clip();
        for (int i = 0; i < reports.Count; i++)
        {
            FoxExpeditionSummaryPacket entry = reports[i];
            double y = ReportRowsTop + i * ReportRowHeight - reportListScrollOffset;
            if (y + ReportRowHeight < ReportRowsTop || y > ReportRowsTop + ReportRowsHeight) continue;
            DrawRect(ctx, 28, y, 240, ReportRowHeight - 5,
                entry.ExpeditionId == report?.ExpeditionId ? TextSelectedPanel : TextPanel);
            FoxExpeditionDefinition mission = FoxExpeditionCatalog.Get(entry.Type);
            if (entry.Type == FoxExpeditionType.Scavenge)
                DrawSimpleSiteSketch(ctx, entry.SceneSiteLabel, 35, y + 9, 79, 74);
            else if (mission != null)
                DrawSimpleMissionSketch(ctx, mission, 35, y + 9, 79, 74);
            DrawText(ctx, Trim(entry.Name, 17), 123, y + 24, 14, TextWhite);
            DrawText(ctx, Trim(ExpeditionName(entry.Type), 17), 123, y + 44, 12, TextGold);
            DrawText(ctx, $"Party: {entry.Members?.Count ?? 0}", 123, y + 65, 12, TextMuted);
            DrawText(ctx, Trim(FriendlyReportGrammar(entry.Result), 28), 35, y + 97, 11, TextMuted);
        }
        ctx.Restore();
        DrawScrollbar(ctx, 267, ReportRowsTop, reports.Count * ReportRowHeight,
            reportListScrollOffset, ReportRowsHeight);
        DrawButton(ctx, confirmDeleteReportId == report?.ExpeditionId ? "CONFIRM delete" : "Delete selected report",
            32, ReportsTop + 490, 230, 42, report != null);
        DrawText(ctx, "Field report", 302, ReportsTop + 29, 19, TextGold);
        FoxGuiTheme.DrawSectionRule(ctx, 302, ReportsTop + 39, 534);
        if (report == null) return;
        DrawText(ctx, Trim(report.Name, 34), 302, ReportsTop + 78, 22, TextWhite);
        FoxExpeditionDefinition reportMission = FoxExpeditionCatalog.Get(report.Type);
        if (report.Type == FoxExpeditionType.Scavenge)
            DrawSimpleSiteSketch(ctx, report.SceneSiteLabel, 302, ReportsTop + 91, 534, 127);
        else if (reportMission != null)
            DrawSimpleMissionSketch(ctx, reportMission, 302, ReportsTop + 91, 534, 127);
        List<FoxExpeditionMemberSummaryPacket> members = report.Members ?? new();
        Dictionary<string, string> labels = BuildSummaryLabels(members);
        FoxExpeditionMemberSummaryPacket lead = members.FirstOrDefault();
        string leadName = lead == null ? "The party" : labels[lead.FoxId];
        string opening = $"{leadName} led {members.Count} companion{(members.Count == 1 ? "" : "s")} to {report.Name}.";
        DrawWrapped(ctx, opening, 302, ReportsTop + 251, 534, 19,
            TextWhite, 3, 15);
        string timing = report.RanLate
            ? $"The party travelled {FormatExpeditionHours(report.PlannedDurationHours)} and returned {FormatExpeditionHours(report.LateDurationHours)} late."
            : $"The party travelled {FormatExpeditionHours(report.PlannedDurationHours)} and returned on time.";
        DrawWrapped(ctx, timing + (report.Prepared ? " They left prepared." : " They travelled without extra preparation."),
            302, ReportsTop + 312, 534, 19, TextMuted, 4, 14);
        List<ExpeditionStoryEventRecord> events = report.Story?.Events ?? new();
        string eventText = events.Count == 0 ? string.Empty
            : string.Join(" ", events.Take(2).Select(StoryEventNarrative));
        DrawWrapped(ctx, eventText + " " + FriendlyReportGrammar(report.Result),
            302, ReportsTop + 397, 534, 19, TextWhite, 4, 14);
        DrawText(ctx, "Quartermaster's note", 302, ReportsTop + 485, 16, TextGold);
        string cargo = report.LootItems?.Count > 0
            ? "The cart held " + string.Join(", ", report.LootItems.OrderByDescending(item => item.Count)
                .Take(4).Select(item => $"{item.Count} {item.Name}"))
                + (report.LootItems.Count > 4 ? ", and smaller finds." : ".")
            : "The pack cart came home empty.";
        DrawWrapped(ctx, cargo, 302, ReportsTop + 511, 534, 19, TextMuted, 3, 13);
        DrawText(ctx, "Report summary", 876, ReportsTop + 29, 19, TextGold);
        FoxGuiTheme.DrawSectionRule(ctx, 876, ReportsTop + 39, 272);
        DrawText(ctx, $"Mission: {Trim(ExpeditionName(report.Type), 19)}", 876,
            ReportsTop + 72, 13, TextWhite);
        DrawText(ctx, $"Party: {members.Count}", 876, ReportsTop + 95, 13, TextWhite);
        DrawText(ctx, $"Strength: {report.ExpeditionStrength:0.#}/{report.TargetStrength:0.#}",
            876, ReportsTop + 118, 13, TextWhite);
        DrawText(ctx, $"Travel: {FormatExpeditionHours(report.PlannedDurationHours)}", 876,
            ReportsTop + 141, 13, TextWhite);
        DrawText(ctx, $"Protection: {(report.Prepared || report.PatrolProtected ? "Yes" : "None")}",
            876, ReportsTop + 164, 13, TextWhite);
        DrawText(ctx, $"Cargo: {report.LootItems?.Sum(item => Math.Max(0, item.Count)) ?? 0} items",
            876, ReportsTop + 187, 13, TextWhite);
        FoxGuiTheme.DrawSectionRule(ctx, 876, ReportsTop + 205, 272);
        DrawText(ctx, "Party outcome", 876, ReportsTop + 232, 16, TextGold);
        for (int i = 0; i < Math.Min(7, members.Count); i++)
            DrawText(ctx, Trim(labels[members[i].FoxId] + " — " + members[i].Outcome, 34),
                876, ReportsTop + 257 + i * 22, 12, TextMuted);
        if (members.Count > 7)
            DrawText(ctx, $"and {members.Count - 7} more", 876,
                ReportsTop + 257 + 7 * 22, 12, TextMuted);
        DrawButton(ctx, "Open exact ledger", 876, ReportsTop + 443, 272, 42, true);
        DrawButton(ctx, "View cache", 876, ReportsTop + 493, 272, 38, true);
    }

    private void HandleReportsOverviewClick(FoxPackStatePacket current, double x, double y)
    {
        List<FoxExpeditionSummaryPacket> reports = GetExpeditionReports(current);
        FoxExpeditionSummaryPacket report = GetSelectedExpeditionReport(current);
        if (report == null) return;
        if (lastTripDetailsOpen)
        {
            if (x >= CanvasWidth - 688 && x <= CanvasWidth - 508
                && y >= ContentY - 8 && y <= ContentY + 22)
            {
                if (confirmDeleteReportId == report.ExpeditionId)
                {
                    confirmDeleteReportId = 0;
                    lastTripDetailsOpen = false;
                    system.SendFoxSocialAction(sourceEntityId, FoxSocialRequestAction.DeleteExpeditionReport,
                        expeditionId: report.ExpeditionId);
                }
                else confirmDeleteReportId = report.ExpeditionId;
                FoxGuiTheme.PlayAction(api); Redraw();
            }
            else if (x >= CanvasWidth - 210 && x <= CanvasWidth - 28
                && y >= ContentY - 8 && y <= ContentY + 22)
            {
                lastTripDetailsOpen = false;
                FoxGuiTheme.PlayNavigation(api); Redraw();
            }
            else if (x >= CanvasWidth - 480 && x <= CanvasWidth - 386
                && y >= ContentY - 8 && y <= ContentY + 22) SelectAdjacentReport(1);
            else if (x >= CanvasWidth - 378 && x <= CanvasWidth - 306
                && y >= ContentY - 8 && y <= ContentY + 22) SelectAdjacentReport(-1);
            return;
        }
        if (x >= 28 && x <= 268 && y >= ReportRowsTop && y <= ReportRowsTop + ReportRowsHeight)
        {
            int index = (int)((y - ReportRowsTop + reportListScrollOffset) / ReportRowHeight);
            if (index >= 0 && index < reports.Count)
            {
                selectedReportId = reports[index].ExpeditionId;
                confirmDeleteReportId = 0;
                FoxGuiTheme.PlayChoice(api); Redraw();
            }
            return;
        }
        if (x >= 32 && x <= 262 && y >= ReportsTop + 490 && y <= ReportsTop + 532)
        {
            if (confirmDeleteReportId == report.ExpeditionId)
            {
                confirmDeleteReportId = 0;
                system.SendFoxSocialAction(sourceEntityId, FoxSocialRequestAction.DeleteExpeditionReport,
                    expeditionId: report.ExpeditionId);
            }
            else confirmDeleteReportId = report.ExpeditionId;
            FoxGuiTheme.PlayAction(api); Redraw(); return;
        }
        if (x >= 876 && x <= 1148)
        {
            if (y >= ReportsTop + 443 && y <= ReportsTop + 485)
            {
                lastTripDetailsOpen = true;
                lastTripScrollOffset = 0;
                FoxGuiTheme.PlayNavigation(api); Redraw();
            }
            else if (y >= ReportsTop + 493 && y <= ReportsTop + 531)
            {
                SelectTab(CacheTab); Redraw();
            }
        }
    }
}
