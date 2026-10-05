#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;

namespace FeralKinshipCompanions;

internal sealed partial class GuiElementFeralKinshipFoxPackSurface
{
    private const double ScavengePartyRowHeight = 42d;
    private const double ScavengePartyViewportHeight = 336d;
    private string scavengePage = "sites";
    private long pendingAbandonScavengeSiteId;
    private double scavengePartyScrollOffset;

    private void DrawScavengeMenu(Context ctx, FoxPackStatePacket current, int width)
    {
        List<FoxScavengeSitePacket> sites = current.ScavengeSites ?? new List<FoxScavengeSitePacket>();
        int activeCount = GetActiveExpeditions(current).Count;
        DrawText(ctx, "Scavenge", 18, ContentY + 13, 22, TextGold);
        DrawText(ctx, $"Remembered sites {sites.Count}/{FoxScavengeSites.MaximumRememberedSites}    ·    Parties away {activeCount}/{Math.Max(1, current.ExpeditionCapacity)}",
            160, ContentY + 12, 14, TextMuted);
        string[] pages = { "sites", "scout", "search" };
        string[] labels = { "Discovered sites", "Scout", "Search a site" };
        for (int i = 0; i < pages.Length; i++)
            DrawChoiceButton(ctx, labels[i], 18 + i * 382, ContentY + 29, 374, 38,
                pages[i] != "search" || SelectedScavengeSite(current) is { Barren: false }, scavengePage == pages[i]);

        if (scavengePage == "sites") DrawScavengeSitesOverview(ctx, current, sites);
        else DrawScavengePlanner(ctx, current);
    }

    private void DrawScavengeSitesPage(Context ctx, FoxPackStatePacket current, List<FoxScavengeSitePacket> sites)
    {
        DrawText(ctx, "Choose a lead to read the field notes. A site may be searched repeatedly until it runs dry.",
            18, ContentY + 91, 14, TextMuted);
        const double cardWidth = 306d;
        for (int i = 0; i < 3; i++)
        {
            double x = 18 + i * 314d;
            double y = ContentY + 107;
            FoxScavengeSitePacket site = i < sites.Count ? sites[i] : null;
            DrawRect(ctx, x, y, cardWidth, 108, site != null && site.SiteId == selectedScavengeSiteId
                ? TextSelectedPanel : TextPanel);
            if (site == null)
            {
                DrawText(ctx, "Undiscovered", x + 15, y + 32, 17, TextMuted);
                DrawText(ctx, "Scout to find another lead.", x + 15, y + 63, 13, TextMuted);
                continue;
            }
            DrawText(ctx, Trim(site.Label, 29), x + 15, y + 30, 18, TextGold);
            DrawText(ctx, site.Busy ? "Party at site" : site.Barren ? "Barren"
                    : site.OutsideSurveyComplete ? "Outside survey complete" : "Available to search",
                x + 15, y + 57, 14, site.Busy || site.Barren || site.OutsideSurveyComplete ? TextGold : TextWhite);
            DrawText(ctx,
                $"{site.SearchesStarted} search{(site.SearchesStarted == 1 ? "" : "es")} inside · {site.Clues?.Count ?? 0} field notes",
                x + 15, y + 84, 13, TextMuted);
        }

        FoxScavengeSitePacket selected = SelectedScavengeSite(current);
        DrawRect(ctx, 18, ContentY + 229, 934, 281, TextPanel);
        if (selected == null)
        {
            DrawText(ctx, "Field notes", 34, ContentY + 261, 19, TextGold);
            DrawWrapped(ctx,
                "Scouts may find an ordinary building, a ruin with an uncertain purpose, or something stranger. Longer scouts bring back more detail. A second scout can reveal more about a site already in memory.",
                34, ContentY + 299, 870, 23, TextWhite, 5, 15);
        }
        else
        {
            DrawText(ctx, $"Field notes — {selected.Label}", 34, ContentY + 261, 19, TextGold);
            DrawText(ctx, $"Searches sent inside: {selected.SearchesStarted}",
                34, ContentY + 283, 14, TextWhite);
            DrawText(ctx, selected.ProspectHint, 34, ContentY + 307, 14, TextGold);
            List<string> clues = selected.Clues ?? new List<string>();
            int pages = Math.Max(1, (int)Math.Ceiling(clues.Count / 3d));
            scavengeIntelPage = Math.Clamp(scavengeIntelPage, 0, pages - 1);
            DrawButton(ctx, "Previous", 703, ContentY + 239, 105, 28, scavengeIntelPage > 0);
            DrawButton(ctx, $"{scavengeIntelPage + 1}/{pages}  Next", 816, ContentY + 239, 119, 28,
                scavengeIntelPage < pages - 1);
            for (int i = 0; i < 3; i++)
            {
                int clueIndex = scavengeIntelPage * 3 + i;
                if (clueIndex >= clues.Count) break;
                DrawWrapped(ctx, $"• {clues[clueIndex]}", 34, ContentY + 332 + i * 38,
                    884, 15, TextWhite, 2, 13);
            }
            string[] knownHints = new[]
                {
                    ("Layout", selected.LayoutHint),
                    ("Danger", selected.DangerHint),
                    ("Condition", selected.ConditionHint)
                }
                .Where(hint => !string.IsNullOrWhiteSpace(hint.Item2))
                .Select(hint => $"{hint.Item1}: {hint.Item2}")
                .ToArray();
            DrawWrapped(ctx, knownHints.Length == 0 ? "No site details have been confirmed yet."
                    : string.Join("   ", knownHints),
                34, ContentY + 451, 875, 17, TextMuted, 3, 13);
        }

        DrawButton(ctx, "Scout for a new site", 18, ContentY + 527, 220, 39,
            sites.Count < FoxScavengeSites.MaximumRememberedSites);
        DrawButton(ctx, "Scout selected site", 250, ContentY + 527, 220, 39,
            selected is { Busy: false, OutsideSurveyComplete: false });
        DrawButton(ctx, "Search selected site", 482, ContentY + 527, 220, 39,
            selected is { Busy: false, Barren: false });
        DrawButton(ctx, selected != null && selected.SiteId == pendingAbandonScavengeSiteId
                ? "CONFIRM abandon" : "Abandon site", 714, ContentY + 527, 220, 39,
            selected is { Busy: false });
        DrawWrapped(ctx, selected != null && selected.SiteId == pendingAbandonScavengeSiteId
                ? "This site may still contain salvage. Click CONFIRM abandon to forget this lead permanently."
                : selected?.OutsideSurveyComplete == true
                ? "The scouts think they have found everything they can from outside this site. A search inside may still find useful salvage."
                : "Abandoning removes this lead from memory. Search focus changes ordinary finds; breakthroughs are independent of focus.",
            18, ContentY + 591, 918, 16, selected?.OutsideSurveyComplete == true ? TextGold : TextMuted, 2, 13);
    }

    private void DrawScavengePlanner(Context ctx, FoxPackStatePacket current)
    {
        List<FoxPackMemberPacket> members = current.Members ?? new List<FoxPackMemberPacket>();
        Dictionary<string, string> labels = BuildMemberLabels(members);
        int selectedCount = members.Count(member => selectedFoxIds.Contains(member.FoxId) && IsExpeditionEligible(member));
        DrawRect(ctx, 18, ContentY + 83, 378, 433, TextPanel);
        DrawText(ctx, $"Party — {selectedCount} selected", 32, ContentY + 111, 18, TextGold);
        DrawText(ctx, "Choose any number of available companions.", 32, ContentY + 133, 13, TextMuted);
        double rowTop = ContentY + 151;
        ctx.Save();
        ctx.Rectangle(32, rowTop, 346, ScavengePartyViewportHeight);
        ctx.Clip();
        for (int i = 0; i < members.Count; i++)
        {
            double y = rowTop + i * ScavengePartyRowHeight - scavengePartyScrollOffset;
            if (y + ScavengePartyRowHeight < rowTop || y >= rowTop + ScavengePartyViewportHeight) continue;
            FoxPackMemberPacket member = members[i];
            bool eligible = IsExpeditionEligible(member);
            bool selected = eligible && selectedFoxIds.Contains(member.FoxId);
            DrawRect(ctx, 32, y, 346, ScavengePartyRowHeight - 4, selected ? TextSelectedPanel : TextPanel);
            DrawRect(ctx, 42, y + 9, 18, 18, selected ? TextGold : TextCheckbox);
            if (selected) DrawText(ctx, "✓", 44, y + 25, 16, TextDark);
            animalArt.DrawFace(ctx, member.SpeciesId, member.AppearanceCode, 68, y + 3, 32, member.EntityId, member.FoxId);
            DrawText(ctx, Trim(labels[member.FoxId], 22), 108, y + 18, 14, eligible ? TextWhite : TextMuted);
            DrawText(ctx, eligible ? CompanionSpeciesTraits.Get(member.SpeciesId)?.Name ?? "Available" : "Unavailable",
                108, y + 34, 11, TextMuted);
        }
        ctx.Restore();
        if (members.Count * ScavengePartyRowHeight > ScavengePartyViewportHeight)
            DrawScrollbar(ctx, 382, rowTop, members.Count * ScavengePartyRowHeight,
                scavengePartyScrollOffset, ScavengePartyViewportHeight);

        DrawButton(ctx, "Select all available", 32, ContentY + 528, 170, 36, members.Any(IsExpeditionEligible));
        DrawButton(ctx, "Clear party", 210, ContentY + 528, 168, 36, selectedFoxIds.Count > 0);

        const double rightX = 418d;
        const double rightWidth = 534d;
        DrawRect(ctx, rightX, ContentY + 83, rightWidth, 433, TextPanel);
        FoxScavengeSitePacket site = SelectedScavengeSite(current);
        DrawRect(ctx, 966, ContentY + 83, 196, 433, TextPanel);
        DrawText(ctx, "Site image", 978, ContentY + 112, 16, TextGold);
        DrawSimpleSiteSketch(ctx, site?.Label ?? "Unknown site", 978, ContentY + 129, 172, 171);
        DrawWrapped(ctx, site == null
                ? "Choose a known site, or scout for a new one."
                : $"{site.Label}. {site.ProspectHint}",
            978, ContentY + 326, 171, 17, TextMuted, 3, 12);
        DrawSpeciesPartyPreview(ctx, current, scavengePage == "scout" ? FoxExpeditionType.Scout
            : FoxExpeditionType.Scavenge, 978, ContentY + 382, 171, 124);
        if (scavengePage == "scout")
        {
            DrawText(ctx, site == null ? "Scout for a new site" : $"Scout {Trim(site.Label, 31)}",
                rightX + 16, ContentY + 112, 19, TextGold);
            DrawWrapped(ctx, site?.OutsideSurveyComplete == true
                    ? "The scouts think they have found everything they can from outside this site. Search inside for more."
                    : "Longer scouting takes more time and can uncover more clues. Scouts can revisit a site already in memory.",
                rightX + 16, ContentY + 145, rightWidth - 32, 17,
                site?.OutsideSurveyComplete == true ? TextGold : TextMuted, 3, 14);
            string[] durations = { FoxScavengeSites.ShortScout, FoxScavengeSites.MediumScout, FoxScavengeSites.LongScout };
            string[] descriptions = { "Quick look", "Careful survey", "Thorough reconnaissance" };
            for (int i = 0; i < durations.Length; i++)
            {
                double y = ContentY + 192 + i * 79;
                DrawChoiceButton(ctx, $"{descriptions[i]} — {FormatExpeditionHours(FoxScavengeSites.ScoutHours(durations[i]))}",
                    rightX + 16, y, rightWidth - 32, 64, true, selectedScoutDuration == durations[i]);
            }
        }
        else if (scavengePage == "search")
        {
            DrawText(ctx, site == null ? "Choose a site" : $"Search {Trim(site.Label, 28)}",
                rightX + 16, ContentY + 112, 19, TextGold);
            DrawText(ctx, site == null ? "Choose a discovered site."
                    : $"Searches sent inside: {site.SearchesStarted} · {site.ProspectHint}",
                rightX + 16, ContentY + 135, 13, TextGold);
            DrawText(ctx, "Choose a focus. Breakthroughs roll separately.",
                rightX + 16, ContentY + 154, 12, TextMuted);
            string[] focuses = { FoxScavengeSites.Useful, FoxScavengeSites.Furniture,
                FoxScavengeSites.Mixed, FoxScavengeSites.Walls };
            string[] focusLabels = { "Grab what's useful", "Focus on furniture", "Leave no stone unturned", "Take the walls, too" };
            string[] descriptions = { "Chests, clothing, vessels and easy finds", "Chairs, tables, shelves and other placed objects",
                "Useful finds and furniture in an even mix", "Wood, stone, fences, windows and building parts" };
            for (int i = 0; i < focuses.Length; i++)
            {
                double y = ContentY + 161 + i * 73;
                DrawChoiceButton(ctx, focusLabels[i], rightX + 16, y, rightWidth - 32, 65,
                    site is { Busy: false, Barren: false }, selectedScavengeFocus == focuses[i]);
                DrawText(ctx, descriptions[i], rightX + 29, y + 50, 12, TextMuted);
            }
            int cost = FoxExpeditionCatalog.Get(FoxExpeditionType.Scavenge)?.PreparationCost ?? 0;
            DrawChoiceButton(ctx, prepareSelected ? $"Prepared — {cost} pack point, safer" : $"Prepare search — {cost} pack point, safer",
                rightX + 16, ContentY + 467, rightWidth - 32, 32,
                current.PackPoints >= cost && cost > 0, prepareSelected);
        }

        string guidance = ScavengeDepartureGuidance(current, selectedCount);
        DrawText(ctx, guidance, 418, ContentY + 548, 13, TextMuted);
        DrawButton(ctx, "Back to sites", 18, ContentY + 565, 186, 42, true);
        DrawButton(ctx, "Send party", 714, ContentY + 565, 238, 42, ScavengeCanDepart(current, selectedCount));
    }

    private string ScavengeDepartureGuidance(FoxPackStatePacket current, int selectedCount)
    {
        if (selectedCount == 0) return "Select at least one available companion.";
        if (current.LootItems?.Count > 0 || current.RecruitmentReady) return "Claim the saved cart reward before departure.";
        if (GetActiveExpeditions(current).Count >= Math.Max(1, current.ExpeditionCapacity)) return "Every expedition slot is occupied.";
        FoxScavengeSitePacket site = SelectedScavengeSite(current);
        if (scavengePage == "scout")
        {
            if (selectedScavengeSiteId > 0 && site == null) return "Choose an available site.";
            if (site == null && (current.ScavengeSites?.Count ?? 0) >= FoxScavengeSites.MaximumRememberedSites)
                return "Memory is full. Scout a known site or abandon a lead.";
            if (site?.Busy == true) return "A party is already at this site.";
            if (site?.OutsideSurveyComplete == true)
                return "The scouts have found everything they can from outside this site.";
        }
        else if (scavengePage == "search")
        {
            if (site == null) return "Choose a discovered site.";
            if (site.Busy) return "A party is already at this site.";
            if (site.Barren) return "This site is barren.";
        }
        if (scavengePage == "search" && prepareSelected
            && current.PackPoints < (FoxExpeditionCatalog.Get(FoxExpeditionType.Scavenge)?.PreparationCost ?? 0))
            return "Not enough pack points to prepare.";
        return "Ready to depart.";
    }

    private bool ScavengeCanDepart(FoxPackStatePacket current, int selectedCount) =>
        scavengePage is "scout" or "search"
        && ScavengeDepartureGuidance(current, selectedCount) == "Ready to depart.";

    private void HandleScavengeMenuClick(FoxPackStatePacket current, double x, double y)
    {
        List<FoxScavengeSitePacket> sites = current.ScavengeSites ?? new List<FoxScavengeSitePacket>();
        string[] pages = { "sites", "scout", "search" };
        for (int i = 0; i < pages.Length; i++)
        {
            if (x < 18 + i * 382 || x >= 392 + i * 382 || y < ContentY + 29 || y > ContentY + 67) continue;
            if (pages[i] == "search" && SelectedScavengeSite(current) is not { Barren: false })
                FoxGuiTheme.PlayUnavailable(api);
            else
            {
                scavengePage = pages[i];
                pendingAbandonScavengeSiteId = 0;
                FoxGuiTheme.PlayNavigation(api);
            }
            Redraw();
            return;
        }

        if (scavengePage == "sites")
        {
            HandleScavengeSitesOverviewClick(current, sites, x, y);
            return;
        }

        List<FoxPackMemberPacket> members = current.Members ?? new List<FoxPackMemberPacket>();
        double rowTop = ContentY + 151;
        if (x >= 32 && x <= 378 && y >= rowTop && y < rowTop + ScavengePartyViewportHeight)
        {
            int index = (int)Math.Floor((y - rowTop + scavengePartyScrollOffset) / ScavengePartyRowHeight);
            if (index >= 0 && index < members.Count)
            {
                FoxPackMemberPacket member = members[index];
                if (IsExpeditionEligible(member))
                {
                    if (!selectedFoxIds.Add(member.FoxId)) selectedFoxIds.Remove(member.FoxId);
                    FoxGuiTheme.PlayChoice(api);
                }
                else FoxGuiTheme.PlayUnavailable(api);
                Redraw();
            }
            return;
        }
        if (y >= ContentY + 528 && y <= ContentY + 564)
        {
            if (x >= 32 && x <= 202)
            {
                selectedFoxIds.Clear();
                foreach (FoxPackMemberPacket member in members.Where(IsExpeditionEligible)) selectedFoxIds.Add(member.FoxId);
            }
            else if (x >= 210 && x <= 378) selectedFoxIds.Clear();
            else return;
            FoxGuiTheme.PlayChoice(api);
            Redraw();
            return;
        }
        if (x >= 434 && x <= 936)
        {
            if (scavengePage == "scout" && y >= ContentY + 192 && y < ContentY + 414)
            {
                int index = (int)((y - (ContentY + 192)) / 79d);
                string[] durations = { FoxScavengeSites.ShortScout, FoxScavengeSites.MediumScout, FoxScavengeSites.LongScout };
                if (index >= 0 && index < durations.Length)
                {
                    selectedScoutDuration = durations[index];
                    FoxGuiTheme.PlayChoice(api);
                    Redraw();
                    return;
                }
            }
            else if (scavengePage == "search" && y >= ContentY + 161 && y < ContentY + 445)
            {
                int index = (int)((y - (ContentY + 161)) / 73d);
                string[] focuses = { FoxScavengeSites.Useful, FoxScavengeSites.Furniture,
                    FoxScavengeSites.Mixed, FoxScavengeSites.Walls };
                if (index >= 0 && index < focuses.Length)
                {
                    selectedScavengeFocus = focuses[index];
                    FoxGuiTheme.PlayChoice(api);
                    Redraw();
                    return;
                }
            }
            if (scavengePage == "search" && y >= ContentY + 467 && y <= ContentY + 499)
            {
                FoxExpeditionDefinition definition = FoxExpeditionCatalog.Get(FoxExpeditionType.Scavenge);
                if (definition != null && definition.PreparationCost > 0 && current.PackPoints >= definition.PreparationCost)
                {
                    prepareSelected = !prepareSelected;
                    FoxGuiTheme.PlayChoice(api);
                    Redraw();
                }
                else FoxGuiTheme.PlayUnavailable(api);
                return;
            }
        }
        if (y >= ContentY + 565 && y <= ContentY + 607)
        {
            if (x >= 18 && x <= 204)
            {
                scavengePage = "sites";
                FoxGuiTheme.PlayNavigation(api);
            }
            else if (x >= 714 && x <= 952)
            {
                int selectedCount = members.Count(member => selectedFoxIds.Contains(member.FoxId) && IsExpeditionEligible(member));
                if (ScavengeCanDepart(current, selectedCount))
                {
                    string expeditionType = scavengePage == "scout"
                        ? FoxExpeditionType.Scout : FoxExpeditionType.Scavenge;
                    system.SendFoxSocialAction(sourceEntityId, FoxSocialRequestAction.StartPackExpedition,
                        expeditionType, selectedFoxIds, string.Empty, scavengePage != "scout" && prepareSelected,
                        scavengeSiteId: selectedScavengeSiteId,
                        scavengeFocus: selectedScavengeFocus, scoutDuration: selectedScoutDuration);
                    FoxGuiTheme.PlayAction(api);
                }
                else FoxGuiTheme.PlayUnavailable(api);
            }
            Redraw();
        }
    }
}
