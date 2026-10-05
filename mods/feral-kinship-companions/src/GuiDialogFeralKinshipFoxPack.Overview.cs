#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;

namespace FeralKinshipCompanions;

internal sealed partial class GuiElementFeralKinshipFoxPackSurface
{
    private const double OverviewTop = ContentY + 72;
    private const double OverviewListX = 288;
    private const double OverviewListWidth = 548;
    private const double OverviewDetailX = 846;
    private const double OverviewDetailWidth = 316;
    private const double OverviewRowHeight = 91;
    private const double OverviewListHeight = 500;
    private static readonly string[] RosterFilters =
        { "All", "Healthy", "Injured", "Pregnant", "Has backpack", "Needs points", "Has requests", "At cart" };
    private static readonly string[] ArchiveFilters =
        { "All", "Dead", "Missing", "Invalid", "Restorable" };

    private List<FoxPackMemberPacket> VisibleRoster(FoxPackStatePacket current) =>
        (current?.Members ?? new List<FoxPackMemberPacket>()).Where(member => rosterFilter switch
        {
            "Healthy" => member.MaxHealth > 0 && member.CurrentHealth >= member.MaxHealth - 0.01f
                && !IsArchivableStatus(member.Status),
            "Injured" => member.MaxHealth > 0 && member.CurrentHealth < member.MaxHealth - 0.01f,
            "Pregnant" => member.PregnancyActive,
            "Has backpack" => member.BackpackEquipped,
            "Needs points" => member.Points > 0,
            "Has requests" => !string.IsNullOrWhiteSpace(member.ActiveRequest),
            "At cart" => member.AtCart,
            _ => true
        }).ToList();

    private List<FoxPackMemberPacket> VisibleArchive(FoxPackStatePacket current) =>
        (current?.ArchivedMembers ?? new List<FoxPackMemberPacket>()).Where(member => archivedFilter switch
        {
            "Dead" => member.Status.Contains("dead", StringComparison.OrdinalIgnoreCase),
            "Missing" => member.Status.Contains("missing", StringComparison.OrdinalIgnoreCase)
                || member.Status.Contains("MIA", StringComparison.OrdinalIgnoreCase),
            "Invalid" => !member.EntityLoaded && !member.Status.Contains("dead", StringComparison.OrdinalIgnoreCase),
            "Restorable" => member.EntityLoaded && !member.Status.Contains("dead", StringComparison.OrdinalIgnoreCase),
            _ => true
        }).ToList();

    private void DrawRosterOverview(Context ctx, FoxPackStatePacket current, int width)
    {
        List<FoxPackMemberPacket> all = current.Members ?? new List<FoxPackMemberPacket>();
        List<FoxPackMemberPacket> visible = VisibleRoster(current);
        DrawText(ctx, "Active roster", 26, ContentY + 22, 29, TextWhite);
        DrawText(ctx, "Check health, mood, requests, and readiness at a glance.", 26, ContentY + 49, 15, TextMuted);
        DrawRect(ctx, 18, OverviewTop, 260, 556, TextPanel);
        DrawRect(ctx, OverviewListX, OverviewTop, OverviewListWidth, 556, TextPanel);
        DrawRect(ctx, OverviewDetailX, OverviewTop, OverviewDetailWidth, 556, TextPanel);
        DrawText(ctx, "Roster summary", 30, OverviewTop + 28, 20, TextGold);
        DrawText(ctx, $"Total companions: {all.Count}", 30, OverviewTop + 64, 15, TextWhite);
        DrawText(ctx, $"Healthy: {all.Count(m => m.MaxHealth > 0 && m.CurrentHealth >= m.MaxHealth - 0.01f)}", 30, OverviewTop + 88, 15, TextWhite);
        DrawText(ctx, $"Injured: {all.Count(m => m.MaxHealth > 0 && m.CurrentHealth < m.MaxHealth - 0.01f)}", 30, OverviewTop + 112, 15, TextWhite);
        DrawText(ctx, $"Pregnant: {all.Count(m => m.PregnancyActive)}", 30, OverviewTop + 136, 15, TextWhite);
        DrawText(ctx, $"Backpacks: {all.Count(m => m.BackpackEquipped)}", 30, OverviewTop + 160, 15, TextWhite);
        DrawText(ctx, $"Pending requests: {all.Count(m => !string.IsNullOrWhiteSpace(m.ActiveRequest))}", 30, OverviewTop + 184, 15, TextWhite);
        FoxGuiTheme.DrawSectionRule(ctx, 30, OverviewTop + 202, 236);
        DrawText(ctx, "Quick filters", 30, OverviewTop + 230, 18, TextGold);
        for (int i = 0; i < RosterFilters.Length; i++)
            DrawChoiceButton(ctx, RosterFilters[i], 30, OverviewTop + 242 + i * 35, 236, 30,
                true, rosterFilter == RosterFilters[i]);
        DrawText(ctx, "Companion roster", OverviewListX + 14, OverviewTop + 28, 20, TextGold);
        DrawText(ctx, $"{visible.Count} of {all.Count} shown", OverviewListX + 412, OverviewTop + 28, 13, TextMuted);
        FoxGuiTheme.DrawSectionRule(ctx, OverviewListX + 14, OverviewTop + 37, OverviewListWidth - 28);
        DrawOverviewRows(ctx, visible, selectedRosterFoxId, rosterScrollOffset, false);
        DrawText(ctx, "Companion details", OverviewDetailX + 14, OverviewTop + 28, 20, TextGold);
        FoxGuiTheme.DrawSectionRule(ctx, OverviewDetailX + 14, OverviewTop + 37, OverviewDetailWidth - 28);
        FoxPackMemberPacket selected = all.FirstOrDefault(m => m.FoxId == selectedRosterFoxId)
            ?? visible.FirstOrDefault();
        DrawMemberDetails(ctx, selected, false);
    }

    private void DrawArchiveOverview(Context ctx, FoxPackStatePacket current, int width)
    {
        List<FoxPackMemberPacket> all = current.ArchivedMembers ?? new List<FoxPackMemberPacket>();
        List<FoxPackMemberPacket> visible = VisibleArchive(current);
        DrawText(ctx, "Archived companions", 26, ContentY + 22, 29, TextWhite);
        DrawText(ctx, "Records of companions no longer on the active roster.", 26, ContentY + 49, 15, TextMuted);
        DrawRect(ctx, 18, OverviewTop, 260, 556, TextPanel);
        DrawRect(ctx, OverviewListX, OverviewTop, OverviewListWidth, 556, TextPanel);
        DrawRect(ctx, OverviewDetailX, OverviewTop, OverviewDetailWidth, 556, TextPanel);
        DrawText(ctx, "Archive summary", 30, OverviewTop + 28, 20, TextGold);
        DrawText(ctx, $"Archived records: {all.Count}", 30, OverviewTop + 64, 15, TextWhite);
        DrawText(ctx, $"Dead: {all.Count(m => m.Status.Contains("dead", StringComparison.OrdinalIgnoreCase))}", 30, OverviewTop + 88, 15, TextWhite);
        DrawText(ctx, $"Missing: {all.Count(m => m.Status.Contains("missing", StringComparison.OrdinalIgnoreCase) || m.Status.Contains("MIA", StringComparison.OrdinalIgnoreCase))}", 30, OverviewTop + 112, 15, TextWhite);
        DrawText(ctx, $"Restorable: {all.Count(m => m.EntityLoaded && !m.Status.Contains("dead", StringComparison.OrdinalIgnoreCase))}", 30, OverviewTop + 136, 15, TextWhite);
        FoxGuiTheme.DrawSectionRule(ctx, 30, OverviewTop + 160, 236);
        DrawText(ctx, "Filter records", 30, OverviewTop + 188, 18, TextGold);
        for (int i = 0; i < ArchiveFilters.Length; i++)
            DrawChoiceButton(ctx, ArchiveFilters[i], 30, OverviewTop + 200 + i * 39, 236, 34,
                true, archivedFilter == ArchiveFilters[i]);
        DrawText(ctx, "Archived records", OverviewListX + 14, OverviewTop + 28, 20, TextGold);
        DrawText(ctx, $"{visible.Count} shown", OverviewListX + 435, OverviewTop + 28, 13, TextMuted);
        FoxGuiTheme.DrawSectionRule(ctx, OverviewListX + 14, OverviewTop + 37, OverviewListWidth - 28);
        DrawOverviewRows(ctx, visible, selectedArchivedFoxId, archivedScrollOffset, true);
        DrawText(ctx, "Companion details", OverviewDetailX + 14, OverviewTop + 28, 20, TextGold);
        FoxGuiTheme.DrawSectionRule(ctx, OverviewDetailX + 14, OverviewTop + 37, OverviewDetailWidth - 28);
        FoxPackMemberPacket selected = all.FirstOrDefault(m => m.FoxId == selectedArchivedFoxId)
            ?? visible.FirstOrDefault();
        DrawMemberDetails(ctx, selected, true);
    }

    private void DrawOverviewRows(Context ctx, List<FoxPackMemberPacket> members, string selectedId,
        double offset, bool archived)
    {
        if (members.Count == 0)
        {
            DrawWrapped(ctx, archived ? "No archived records match this filter." : "No active companions match this filter.",
                OverviewListX + 22, OverviewTop + 79, OverviewListWidth - 44, 20, TextMuted, 3, 16);
            return;
        }
        Dictionary<string, string> labels = BuildMemberLabels(members);
        ctx.Save();
        ctx.Rectangle(OverviewListX + 8, OverviewTop + 46, OverviewListWidth - 16, OverviewListHeight);
        ctx.Clip();
        for (int i = 0; i < members.Count; i++)
        {
            FoxPackMemberPacket member = members[i];
            double y = OverviewTop + 50 + i * OverviewRowHeight - offset;
            if (y + OverviewRowHeight < OverviewTop + 46 || y > OverviewTop + 572) continue;
            DrawRect(ctx, OverviewListX + 8, y, OverviewListWidth - 16, OverviewRowHeight - 5,
                member.FoxId == selectedId ? TextSelectedPanel : TextPanel);
            animalArt.DrawFace(ctx, member.SpeciesId, member.AppearanceCode,
                OverviewListX + 14, y + 6, 72, member.EntityId, member.FoxId);
            DrawText(ctx, Trim(labels[member.FoxId], 27), OverviewListX + 96, y + 22, 17, TextWhite);
            DrawText(ctx, Trim(member.SpeciesDisplayName, 28), OverviewListX + 96, y + 42, 13, TextMuted);
            DrawText(ctx, archived ? FriendlyStatus(member.Status)
                    : $"Health {member.CurrentHealth:0.#}/{member.MaxHealth:0.#}  ·  {Trim(member.Mood, 14)}",
                OverviewListX + 96, y + 61, 13, archived ? TextGold : TextWhite);
            DrawText(ctx, archived ? "Historical record"
                    : $"Points {member.Points}  ·  Requests {member.RequestsCompleted}/{member.RequestsGenerated}",
                OverviewListX + 96, y + 78, 12, TextMuted);
        }
        ctx.Restore();
        DrawScrollbar(ctx, OverviewListX + OverviewListWidth - 13, OverviewTop + 47,
            members.Count * OverviewRowHeight, offset, OverviewListHeight);
    }

    private void DrawMemberDetails(Context ctx, FoxPackMemberPacket member, bool archived)
    {
        double x = OverviewDetailX + 15;
        if (member == null)
        {
            DrawWrapped(ctx, archived ? "Select a record to inspect its history."
                : "Select a companion to inspect their status.", x, OverviewTop + 80,
                OverviewDetailWidth - 30, 18, TextMuted, 3, 15);
            return;
        }
        animalArt.DrawFace(ctx, member.SpeciesId, member.AppearanceCode,
            x, OverviewTop + 52, 90, member.EntityId, member.FoxId);
        DrawText(ctx, Trim(CompanionDisplayName(member.Name), 17), x + 100, OverviewTop + 79, 20, TextWhite);
        DrawText(ctx, Trim(member.SpeciesDisplayName, 23), x + 100, OverviewTop + 101, 13, TextMuted);
        DrawText(ctx, Trim(FriendlyStatus(member.Status), 35), x, OverviewTop + 165, 15, TextGold);
        FoxGuiTheme.DrawSectionRule(ctx, x, OverviewTop + 178, OverviewDetailWidth - 30);
        string[] lines = archived
            ? new[] {
                $"Status: {FriendlyStatus(member.Status)}",
                $"Last known: {(member.HasLastKnownPosition ? CompanionLocationText.Format(api, member.LastKnownX, member.LastKnownY, member.LastKnownZ) : "Unknown")}",
                $"Restorable: {(member.EntityLoaded ? "Possibly" : "Not currently")}",
                $"Lifetime points: {member.LifetimePoints}",
                $"Level: {member.Level}" }
            : new[] {
                $"Health: {member.CurrentHealth:0.#}/{member.MaxHealth:0.#}",
                $"Mood: {member.Mood}",
                $"Personality: {member.Personality}",
                $"Pregnant: {(member.PregnancyActive ? "Yes" : "No")}",
                $"Perk points: {member.Points}",
                $"Requests: {member.RequestsCompleted}/{member.RequestsGenerated}",
                $"Backpack: {(member.BackpackEquipped ? member.BackpackName : "None equipped")}",
                $"Lifetime points: {member.LifetimePoints}",
                $"Level: {member.Level} ({member.CurrentLevelExperience}/{member.RequiredLevelExperience} EXP)" };
        for (int i = 0; i < lines.Length; i++)
            DrawText(ctx, Trim(lines[i], 40), x, OverviewTop + 207 + i * 22, 13, TextWhite);
        if (!archived)
            DrawButton(ctx, member.AtCart ? "Already at cart" : "Call to cart", x,
                OverviewTop + 401, OverviewDetailWidth - 30, 38, member.EntityLoaded && !member.AtCart);
        DrawButton(ctx, archived ? "Restore record" : "Open full profile", x,
            OverviewTop + 446, OverviewDetailWidth - 30, 38,
            archived ? member.EntityLoaded && !member.Status.Contains("dead", StringComparison.OrdinalIgnoreCase)
                : member.EntityLoaded && member.EntityId > 0);
        DrawButton(ctx, archived ? "Locate record" : "Locate companion", x,
            OverviewTop + 491, OverviewDetailWidth - 30, 34, member.HasLastKnownPosition);
    }

    private void HandleRosterOverviewClick(FoxPackStatePacket current, double x, double y)
    {
        if (x >= 30 && x <= 266 && y >= OverviewTop + 242 && y < OverviewTop + 242 + RosterFilters.Length * 35)
        {
            int index = (int)((y - OverviewTop - 242) / 35);
            rosterFilter = RosterFilters[Math.Clamp(index, 0, RosterFilters.Length - 1)];
            rosterScrollOffset = 0;
            FoxGuiTheme.PlayChoice(api); Redraw(); return;
        }
        List<FoxPackMemberPacket> visible = VisibleRoster(current);
        if (x >= OverviewListX + 8 && x <= OverviewListX + OverviewListWidth - 8
            && y >= OverviewTop + 50 && y < OverviewTop + 50 + OverviewListHeight)
        {
            int index = (int)((y - OverviewTop - 50 + rosterScrollOffset) / OverviewRowHeight);
            if (index >= 0 && index < visible.Count)
            {
                selectedRosterFoxId = visible[index].FoxId;
                FoxGuiTheme.PlayChoice(api); Redraw();
            }
            return;
        }
        FoxPackMemberPacket member = (current.Members ?? new()).FirstOrDefault(m => m.FoxId == selectedRosterFoxId)
            ?? visible.FirstOrDefault();
        if (member == null || x < OverviewDetailX + 15 || x > OverviewDetailX + OverviewDetailWidth - 15) return;
        if (y >= OverviewTop + 401 && y <= OverviewTop + 439 && member.EntityLoaded && !member.AtCart)
        {
            FoxGuiTheme.PlayAction(api);
            system.SendFoxSocialAction(sourceEntityId, FoxSocialRequestAction.CallPackFoxToCart, member.FoxId);
        }
        else if (y >= OverviewTop + 446 && y <= OverviewTop + 484 && member.EntityLoaded && member.EntityId > 0)
        {
            FoxGuiTheme.PlayAction(api); system.TryOpenFoxSocialGui(member.EntityId);
        }
        else if (y >= OverviewTop + 491 && y <= OverviewTop + 525 && member.HasLastKnownPosition)
        {
            FoxGuiTheme.PlayAction(api);
            system.SendFoxSocialAction(sourceEntityId, FoxSocialRequestAction.LocatePackFox, member.FoxId);
        }
    }

    private void HandleArchiveOverviewClick(FoxPackStatePacket current, double x, double y)
    {
        if (x >= 30 && x <= 266 && y >= OverviewTop + 200 && y < OverviewTop + 200 + ArchiveFilters.Length * 39)
        {
            int index = (int)((y - OverviewTop - 200) / 39);
            archivedFilter = ArchiveFilters[Math.Clamp(index, 0, ArchiveFilters.Length - 1)];
            archivedScrollOffset = 0;
            FoxGuiTheme.PlayChoice(api); Redraw(); return;
        }
        List<FoxPackMemberPacket> visible = VisibleArchive(current);
        if (x >= OverviewListX + 8 && x <= OverviewListX + OverviewListWidth - 8
            && y >= OverviewTop + 50 && y < OverviewTop + 50 + OverviewListHeight)
        {
            int index = (int)((y - OverviewTop - 50 + archivedScrollOffset) / OverviewRowHeight);
            if (index >= 0 && index < visible.Count)
            {
                selectedArchivedFoxId = visible[index].FoxId;
                FoxGuiTheme.PlayChoice(api); Redraw();
            }
            return;
        }
        FoxPackMemberPacket member = (current.ArchivedMembers ?? new()).FirstOrDefault(m => m.FoxId == selectedArchivedFoxId)
            ?? visible.FirstOrDefault();
        if (member == null || x < OverviewDetailX + 15 || x > OverviewDetailX + OverviewDetailWidth - 15) return;
        if (y >= OverviewTop + 446 && y <= OverviewTop + 484 && member.EntityLoaded
            && !member.Status.Contains("dead", StringComparison.OrdinalIgnoreCase))
        {
            FoxGuiTheme.PlayAction(api);
            system.SendFoxSocialAction(sourceEntityId, FoxSocialRequestAction.UnarchivePackFox, member.FoxId);
        }
        else if (y >= OverviewTop + 491 && y <= OverviewTop + 525 && member.HasLastKnownPosition)
        {
            FoxGuiTheme.PlayAction(api);
            system.SendFoxSocialAction(sourceEntityId, FoxSocialRequestAction.LocatePackFox, member.FoxId);
        }
    }
}
