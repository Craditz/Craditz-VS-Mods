#nullable disable

using System;
using System.Collections.Generic;
using Cairo;

namespace FeralKinshipCompanions;

internal sealed partial class GuiElementFeralKinshipFoxPackSurface
{
    private const double SiteTop = ContentY + 80;
    private const double SiteRowsTop = ContentY + 145;
    private const double SiteRowHeight = 135;
    private const double SiteActionsTop = ContentY + 590;

    private void DrawScavengeSitesOverview(Context ctx, FoxPackStatePacket current,
        List<FoxScavengeSitePacket> sites)
    {
        DrawRect(ctx, 18, SiteTop, 240, 499, TextPanel);
        DrawRect(ctx, 270, SiteTop, 540, 499, TextPanel);
        DrawRect(ctx, 822, SiteTop, 340, 499, TextPanel);
        DrawText(ctx, "Scavenge workflow", 32, SiteTop + 30, 18, TextGold);
        FoxGuiTheme.DrawSectionRule(ctx, 32, SiteTop + 40, 212);
        string[] steps = { "1  Discover a site", "2  Scout for clues", "3  Search for finds", "4  Forget it when dry" };
        string[] descriptions = { "Find new locations in the world.", "Learn more about the outside.",
            "Send a party inside for salvage.", "Free a memory slot when done." };
        for (int i = 0; i < steps.Length; i++)
        {
            double y = SiteTop + 68 + i * 92;
            DrawRect(ctx, 30, y, 216, 82, TextSelectedPanel);
            DrawText(ctx, steps[i], 40, y + 26, 15, TextWhite);
            DrawWrapped(ctx, descriptions[i], 40, y + 50, 190, 17, TextMuted, 2, 12);
        }
        DrawText(ctx, $"Remembered sites: {sites.Count}/{FoxScavengeSites.MaximumRememberedSites}",
            32, SiteTop + 465, 14, TextGold);
        DrawText(ctx, "Known sites", 284, SiteTop + 30, 18, TextGold);
        DrawText(ctx, $"{sites.Count} sites", 724, SiteTop + 30, 12, TextMuted);
        FoxGuiTheme.DrawSectionRule(ctx, 284, SiteTop + 40, 512);
        for (int i = 0; i < FoxScavengeSites.MaximumRememberedSites; i++)
        {
            FoxScavengeSitePacket site = i < sites.Count ? sites[i] : null;
            double y = SiteRowsTop + i * SiteRowHeight;
            DrawRect(ctx, 280, y, 520, SiteRowHeight - 7,
                site != null && site.SiteId == selectedScavengeSiteId ? TextSelectedPanel : TextPanel);
            if (site == null)
            {
                DrawText(ctx, "Undiscovered", 302, y + 40, 17, TextMuted);
                DrawText(ctx, "Send scouts to find a new lead.", 302, y + 69, 13, TextMuted);
                continue;
            }
            DrawSimpleSiteSketch(ctx, site.Label, 289, y + 10, 153, 106);
            DrawText(ctx, Trim(site.Label, 28), 454, y + 34, 17, TextWhite);
            DrawText(ctx, site.Busy ? "Party at site" : site.Barren ? "Barren"
                    : site.OutsideSurveyComplete ? "Outside survey complete" : "Available to search",
                454, y + 58, 13, site.Barren || site.Busy ? TextRed : TextGold);
            DrawText(ctx, $"{site.SearchesStarted} searches inside  ·  {site.Clues?.Count ?? 0} field notes",
                454, y + 84, 12, TextMuted);
            DrawText(ctx, Trim(site.ProspectHint, 43), 454, y + 106, 12, TextMuted);
        }
        DrawText(ctx, "Site details", 836, SiteTop + 30, 18, TextGold);
        FoxGuiTheme.DrawSectionRule(ctx, 836, SiteTop + 40, 312);
        FoxScavengeSitePacket selected = SelectedScavengeSite(current);
        if (selected == null)
        {
            DrawWrapped(ctx, "Select a known site to read its field notes, or discover a new one.",
                836, SiteTop + 85, 306, 20, TextMuted, 5, 14);
        }
        else
        {
            DrawText(ctx, Trim(selected.Label, 27), 836, SiteTop + 75, 19, TextWhite);
            DrawSimpleSiteSketch(ctx, selected.Label, 836, SiteTop + 90, 312, 132);
            DrawText(ctx, $"Searches sent inside: {selected.SearchesStarted}",
                836, SiteTop + 245, 13, TextGold);
            int pages = Math.Max(1, (int)Math.Ceiling((selected.Clues?.Count ?? 0) / 3d));
            scavengeIntelPage = Math.Clamp(scavengeIntelPage, 0, pages - 1);
            DrawButton(ctx, "Previous", 836, SiteTop + 262, 102, 27, scavengeIntelPage > 0);
            DrawButton(ctx, $"{scavengeIntelPage + 1}/{pages} Next", 948, SiteTop + 262, 116, 27,
                scavengeIntelPage < pages - 1);
            for (int i = 0; i < 3; i++)
            {
                int clueIndex = scavengeIntelPage * 3 + i;
                if (clueIndex >= (selected.Clues?.Count ?? 0)) break;
                DrawWrapped(ctx, "• " + selected.Clues[clueIndex], 836,
                    SiteTop + 315 + i * 37, 310, 15, TextWhite, 2, 12);
            }
            string info = string.Join("  ", new[] { selected.LayoutHint, selected.DangerHint,
                selected.ConditionHint });
            DrawWrapped(ctx, info, 836, SiteTop + 435, 310, 17, TextMuted, 3, 12);
        }
        double actionWidth = 276;
        DrawButton(ctx, "Discover new site", 18, SiteActionsTop, actionWidth, 42,
            sites.Count < FoxScavengeSites.MaximumRememberedSites);
        DrawButton(ctx, "Scout selected site", 308, SiteActionsTop, actionWidth, 42,
            selected is { Busy: false, OutsideSurveyComplete: false });
        DrawButton(ctx, "Search selected site", 598, SiteActionsTop, actionWidth, 42,
            selected is { Busy: false, Barren: false });
        DrawButton(ctx, selected != null && pendingAbandonScavengeSiteId == selected.SiteId
                ? "CONFIRM abandon" : "Abandon site", 888, SiteActionsTop, actionWidth, 42,
            selected is { Busy: false });
        DrawText(ctx, selected != null && pendingAbandonScavengeSiteId == selected.SiteId
                ? "This site may still contain salvage. Click Abandon again to forget it."
                : selected?.OutsideSurveyComplete == true
                ? "Scouts have found everything they can outside. A search inside may still find salvage."
                : "Search focus changes ordinary finds. Breakthroughs roll separately.",
            24, SiteActionsTop + 56, 12,
            selected?.OutsideSurveyComplete == true ? TextGold : TextMuted);
    }

    private void HandleScavengeSitesOverviewClick(FoxPackStatePacket current,
        List<FoxScavengeSitePacket> sites, double x, double y)
    {
        if (x >= 280 && x <= 800 && y >= SiteRowsTop
            && y < SiteRowsTop + FoxScavengeSites.MaximumRememberedSites * SiteRowHeight)
        {
            int index = (int)((y - SiteRowsTop) / SiteRowHeight);
            if (index >= 0 && index < sites.Count)
            {
                selectedScavengeSiteId = sites[index].SiteId;
                pendingAbandonScavengeSiteId = 0;
                scavengeIntelPage = 0;
                FoxGuiTheme.PlayChoice(api); Redraw();
            }
            return;
        }
        if (x >= 836 && x <= 1064 && y >= SiteTop + 262 && y <= SiteTop + 289)
        {
            FoxScavengeSitePacket selected = SelectedScavengeSite(current);
            if (selected == null) return;
            int pages = Math.Max(1, (int)Math.Ceiling((selected.Clues?.Count ?? 0) / 3d));
            scavengeIntelPage = x < 938 ? Math.Max(0, scavengeIntelPage - 1)
                : Math.Min(pages - 1, scavengeIntelPage + 1);
            FoxGuiTheme.PlayChoice(api); Redraw(); return;
        }
        if (y < SiteActionsTop || y > SiteActionsTop + 42) return;
        FoxScavengeSitePacket site = SelectedScavengeSite(current);
        if (x >= 18 && x <= 294 && sites.Count < FoxScavengeSites.MaximumRememberedSites)
        {
            selectedScavengeSiteId = 0; scavengePage = "scout";
        }
        else if (x >= 308 && x <= 584 && site is { Busy: false, OutsideSurveyComplete: false })
            scavengePage = "scout";
        else if (x >= 598 && x <= 874 && site is { Busy: false, Barren: false })
            scavengePage = "search";
        else if (x >= 888 && x <= 1164 && site is { Busy: false })
        {
            if (!site.Barren && pendingAbandonScavengeSiteId != site.SiteId)
            {
                pendingAbandonScavengeSiteId = site.SiteId;
                FoxGuiTheme.PlayChoice(api); Redraw(); return;
            }
            pendingAbandonScavengeSiteId = 0;
            system.SendFoxSocialAction(sourceEntityId, FoxSocialRequestAction.AbandonScavengeSite,
                scavengeSiteId: site.SiteId);
            selectedScavengeSiteId = 0;
        }
        else { FoxGuiTheme.PlayUnavailable(api); return; }
        if (x < 888) pendingAbandonScavengeSiteId = 0;
        FoxGuiTheme.PlayAction(api); Redraw();
    }

    private void DrawSimpleSiteSketch(Context ctx, string label,
        double x, double y, double width, double height)
    {
        if (sceneArt.DrawSite(ctx, label, x, y, width, height)) return;
        double[] sky = { 0.10, 0.24, 0.18, 1d };
        double[] structure = { 0.45, 0.59, 0.47, 1d };
        double[] dark = { 0.07, 0.15, 0.12, 1d };
        DrawRect(ctx, x, y, width, height, sky);
        ctx.SetSourceRGBA(structure[0], structure[1], structure[2], 1d);
        if (label.Contains("unknow", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Arc(x + width * .50, y + height * .60, height * .32, 0, Math.PI * 2); ctx.Fill();
            ctx.SetSourceRGBA(dark[0], dark[1], dark[2], 1d);
            ctx.Arc(x + width * .50, y + height * .60, height * .24, 0, Math.PI * 2); ctx.Fill();
        }
        else if (label.Contains("mine", StringComparison.OrdinalIgnoreCase)
            || label.Contains("crypt", StringComparison.OrdinalIgnoreCase)
            || label.Contains("adit", StringComparison.OrdinalIgnoreCase)
            || label.Contains("ossuary", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Arc(x + width * .50, y + height * .74, width * .33, Math.PI, Math.PI * 2); ctx.Fill();
            DrawRect(ctx, x + width * .17, y + height * .73, width * .66, height * .20, structure);
            ctx.SetSourceRGBA(dark[0], dark[1], dark[2], 1d);
            ctx.Arc(x + width * .50, y + height * .78, width * .21, Math.PI, Math.PI * 2); ctx.Fill();
            DrawRect(ctx, x + width * .29, y + height * .77, width * .42, height * .16, dark);
        }
        else if (label.Contains("camp", StringComparison.OrdinalIgnoreCase)
            || label.Contains("yard", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < 2; i++)
            {
                double tx = x + width * (.12 + i * .42);
                ctx.MoveTo(tx, y + height * .78);
                ctx.LineTo(tx + width * .17, y + height * .31);
                ctx.LineTo(tx + width * .34, y + height * .78);
                ctx.ClosePath(); ctx.Fill();
                DrawRect(ctx, tx + width * .15, y + height * .60, width * .04, height * .18, dark);
            }
        }
        else if (label.Contains("tower", StringComparison.OrdinalIgnoreCase)
            || label.Contains("fort", StringComparison.OrdinalIgnoreCase)
            || label.Contains("gate", StringComparison.OrdinalIgnoreCase))
        {
            DrawRect(ctx, x + width * .29, y + height * .18, width * .42, height * .69, structure);
            for (int i = 0; i < 4; i++)
                DrawRect(ctx, x + width * (.29 + i * .105), y + height * .11,
                    width * .055, height * .14, structure);
            DrawRect(ctx, x + width * .45, y + height * .60, width * .10, height * .27, dark);
        }
        else
        {
            DrawRect(ctx, x + width * .22, y + height * .44, width * .56, height * .40, structure);
            ctx.MoveTo(x + width * .18, y + height * .45);
            ctx.LineTo(x + width * .50, y + height * .16);
            ctx.LineTo(x + width * .82, y + height * .45);
            ctx.ClosePath(); ctx.Fill();
            DrawRect(ctx, x + width * .44, y + height * .62, width * .12, height * .22, dark);
            DrawRect(ctx, x + width * .27, y + height * .54, width * .10, height * .11, dark);
            DrawRect(ctx, x + width * .64, y + height * .54, width * .10, height * .11, dark);
            if (label.Contains("farm", StringComparison.OrdinalIgnoreCase)
                || label.Contains("orchard", StringComparison.OrdinalIgnoreCase)
                || label.Contains("vineyard", StringComparison.OrdinalIgnoreCase))
                for (int i = 0; i < 4; i++)
                    DrawRect(ctx, x + 8 + i * width * .21, y + height * .83, 3, height * .17, structure);
            if (label.Contains("smith", StringComparison.OrdinalIgnoreCase)
                || label.Contains("foundry", StringComparison.OrdinalIgnoreCase)
                || label.Contains("kiln", StringComparison.OrdinalIgnoreCase))
                DrawRect(ctx, x + width * .67, y + height * .10, width * .11, height * .37, structure);
        }
    }
}
