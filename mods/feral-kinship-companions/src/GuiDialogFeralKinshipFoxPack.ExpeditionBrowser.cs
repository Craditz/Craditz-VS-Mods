#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;

namespace FeralKinshipCompanions;

internal sealed partial class GuiElementFeralKinshipFoxPackSurface
{
    private const double MissionPanelTop = ContentY + 72;
    private const double MissionRowsTop = ContentY + 147;
    private const double MissionRowHeight = 110;
    private const double MissionRowsHeight = 474;
    private const double PlannerRowsTop = ContentY + 153;
    private const double PlannerRowHeight = 56;
    private const double PlannerRowsHeight = 471;

    private static float GetMemberBaseStrength(FoxPackMemberPacket member) =>
        (member.ExpeditionCoreStrength > 0f ? member.ExpeditionCoreStrength
            : member.MaxHealth > 0f && member.CurrentHealth < member.MaxHealth * 0.5f ? 0.5f : 1f)
        + member.ExpeditionStrengthBonus;

    private float GetMemberStrength(FoxPackMemberPacket member) => GetMemberBaseStrength(member)
        + CompanionSpeciesTraits.StrengthBonus(member.SpeciesId, selectedExpeditionType,
            GetMemberBaseStrength(member), member.SpeciesTraining);

    private CompanionSpeciesExpeditionBonus PreviewSpecies(FoxPackStatePacket current, string route) =>
        CompanionSpeciesTraits.Build(route, (current.Members ?? new()).Where(m =>
            selectedFoxIds.Contains(m.FoxId) && IsExpeditionEligible(m))
            .Select(m => (m.SpeciesId, GetMemberBaseStrength(m), m.SpeciesTraining)));

    private void DrawSpeciesPartyPreview(Context ctx, FoxPackStatePacket current, string route,
        double x, double top, double width, double height = 94)
    {
        var bonus = PreviewSpecies(current, route);
        double scale = FeralKinshipCompanionUiSettings.TextScale;
        double summaryHeight = Math.Min(height, 28 * scale + 6);
        int synergyLines = Math.Max(0, (int)Math.Floor((height - summaryHeight) / (17 * scale)));
        ctx.Save();
        ctx.Rectangle(x, top, width, height);
        ctx.Clip();
        DrawWrapped(ctx, $"Species: +{bonus.Strength:0.##} strength, -{bonus.TravelReduction:P0} travel",
            x, top + 12 * scale, width, 14, TextMuted, 2, 11);
        if (synergyLines > 0)
            DrawWrapped(ctx, CompanionSpeciesTraits.SynergySummary(bonus, route),
                x, top + summaryHeight + 12 * scale, width, 17, TextGold, synergyLines, 12);
        ctx.Restore();
    }

    private void DrawExpeditionBrowser(Context ctx, FoxPackStatePacket current, int width)
    {
        DrawText(ctx, "Expeditions", 26, ContentY + 21, 29, TextWhite);
        DrawText(ctx, "Choose an activity, review the mission, then plan a party.", 26, ContentY + 49, 15, TextMuted);
        DrawRect(ctx, 18, MissionPanelTop, 220, 558, TextPanel);
        DrawRect(ctx, 250, MissionPanelTop, 566, 558, TextPanel);
        DrawRect(ctx, 828, MissionPanelTop, 334, 558, TextPanel);
        DrawText(ctx, "Activity types", 32, MissionPanelTop + 29, 19, TextGold);
        FoxGuiTheme.DrawSectionRule(ctx, 32, MissionPanelTop + 39, 192);
        string[] categories = { "hunt", "forage", "utility" };
        for (int i = 0; i < categories.Length; i++)
            DrawChoiceButton(ctx, char.ToUpperInvariant(categories[i][0]) + categories[i][1..],
                32, MissionPanelTop + 61 + i * 55, 192, 48, true, expeditionCategory == categories[i]);
        DrawChoiceButton(ctx, "Scavenge sites", 32, MissionPanelTop + 226, 192, 48, true, false);
        DrawWrapped(ctx, "Unlocked routes expand the missions shown here. Site scavenging has its own discovery board.",
            32, MissionPanelTop + 309, 188, 19, TextMuted, 6, 13);
        DrawText(ctx, "Available missions", 264, MissionPanelTop + 29, 19, TextGold);
        List<FoxExpeditionDefinition> missions = GetCategoryMissions();
        DrawText(ctx, $"{missions.Count} missions", 694, MissionPanelTop + 29, 13, TextMuted);
        FoxGuiTheme.DrawSectionRule(ctx, 264, MissionPanelTop + 39, 538);
        HashSet<string> unlocked = (current.UnlockedExpeditionTypes ?? new()).ToHashSet(StringComparer.Ordinal);
        FoxExpeditionDefinition selected = missions.FirstOrDefault(m => m.Id == selectedExpeditionType)
            ?? missions.FirstOrDefault(m => m.DefaultUnlocked || unlocked.Contains(m.Id))
            ?? missions.FirstOrDefault();
        if (selected != null && selectedExpeditionType != selected.Id) selectedExpeditionType = selected.Id;
        ctx.Save();
        ctx.Rectangle(258, MissionRowsTop, 550, MissionRowsHeight);
        ctx.Clip();
        for (int i = 0; i < missions.Count; i++)
        {
            FoxExpeditionDefinition mission = missions[i];
            double y = MissionRowsTop + i * MissionRowHeight - expeditionScrollOffset;
            if (y + MissionRowHeight < MissionRowsTop || y > MissionRowsTop + MissionRowsHeight) continue;
            bool available = mission.DefaultUnlocked || unlocked.Contains(mission.Id);
            DrawRect(ctx, 258, y, 550, MissionRowHeight - 6,
                selected?.Id == mission.Id ? TextSelectedPanel : available ? TextPanel : TextLockedPanel);
            DrawSimpleMissionSketch(ctx, mission, 267, y + 9, 102, 84);
            DrawText(ctx, Trim(mission.Name, 31), 382, y + 27, 18,
                available ? TextWhite : TextLocked);
            DrawWrapped(ctx, mission.Description, 382, y + 49, 411, 15,
                available ? TextMuted : TextLocked, 2, 12);
            DrawText(ctx,
                $"Party {mission.MinimumFoxes}–{mission.MaximumFoxes}  ·  {mission.RiskLabel} risk  ·  {FormatExpeditionHours(mission.BaseDurationHours)}",
                382, y + 88, 12, available ? TextGold : TextLocked);
            if (!available) DrawText(ctx, "Locked", 734, y + 24, 12, TextRed);
        }
        ctx.Restore();
        DrawScrollbar(ctx, 806, MissionRowsTop, missions.Count * MissionRowHeight,
            expeditionScrollOffset, MissionRowsHeight);
        DrawText(ctx, "Mission details", 842, MissionPanelTop + 29, 19, TextGold);
        FoxGuiTheme.DrawSectionRule(ctx, 842, MissionPanelTop + 39, 306);
        if (selected == null)
        {
            DrawText(ctx, "No missions in this activity.", 842, MissionPanelTop + 78, 15, TextMuted);
            return;
        }
        bool routeUnlocked = selected.DefaultUnlocked || unlocked.Contains(selected.Id);
        DrawText(ctx, Trim(selected.Name, 28), 842, MissionPanelTop + 80, 21, TextWhite);
        DrawWrapped(ctx, selected.Description, 842, MissionPanelTop + 113, 305, 19,
            TextWhite, 4, 14);
        DrawText(ctx, $"Recommended party: {selected.MinimumFoxes}–{selected.MaximumFoxes}",
            842, MissionPanelTop + 209, 14, TextGold);
        DrawText(ctx, $"Risk: {selected.RiskLabel}", 842, MissionPanelTop + 235, 14, TextWhite);
        DrawText(ctx, $"Travel: {FormatExpeditionHours(selected.BaseDurationHours)}", 842,
            MissionPanelTop + 261, 14, TextWhite);
        DrawText(ctx, $"Route: {(routeUnlocked ? "Unlocked" : "Locked")}", 842,
            MissionPanelTop + 287, 14, routeUnlocked ? TextGold : TextRed);
        FoxGuiTheme.DrawSectionRule(ctx, 842, MissionPanelTop + 304, 306);
        int selectedCount = (current.Members ?? new()).Count(m => selectedFoxIds.Contains(m.FoxId)
            && IsExpeditionEligible(m));
        DrawText(ctx, "Party plan", 842, MissionPanelTop + 334, 18, TextGold);
        DrawText(ctx, $"Selected companions: {selectedCount}", 842, MissionPanelTop + 361, 14, TextWhite);
        DrawText(ctx, $"Effective strength: {GetSelectedStrength(current.Members ?? new()):0.#}",
            842, MissionPanelTop + 385, 14, TextWhite);
        if (current.LootItems?.Count > 0 || current.RecruitmentReady)
            DrawWrapped(ctx, "The cache must be cleared before departure.", 842,
                MissionPanelTop + 415, 305, 17, TextRed, 2, 13);
        DrawButton(ctx, routeUnlocked ? "Open party planner" : $"Review unlock — {selected.UnlockCost} points",
            842, MissionPanelTop + 469, 306, 46, true);
    }

    private void DrawExpeditionPartyPlanner(Context ctx, FoxPackStatePacket current, int width)
    {
        FoxExpeditionDefinition mission = FoxExpeditionCatalog.Get(selectedExpeditionType);
        if (mission == null) { expeditionPartyPlannerOpen = false; DrawExpeditionBrowser(ctx, current, width); return; }
        List<FoxPackMemberPacket> members = current.Members ?? new();
        int selectedCount = members.Count(m => selectedFoxIds.Contains(m.FoxId) && IsExpeditionEligible(m));
        float strength = GetSelectedStrength(members);
        HashSet<string> unlocked = (current.UnlockedExpeditionTypes ?? new()).ToHashSet(StringComparer.Ordinal);
        bool routeUnlocked = mission.DefaultUnlocked || unlocked.Contains(mission.Id);
        bool prerequisites = AreRoutePrerequisitesMet(mission, unlocked);
        bool hasTarget = members.Any(m => m.FoxId == selectedMissingFoxId && IsRescueTarget(m))
            && !IsSearchTargetActive(current, selectedMissingFoxId);
        bool canStart = routeUnlocked && prerequisites
            && GetActiveExpeditions(current).Count < Math.Max(1, current.ExpeditionCapacity)
            && current.LootItems?.Count == 0 && !current.RecruitmentReady
            && selectedCount >= mission.MinimumFoxes && selectedCount <= mission.MaximumFoxes
            && (!prepareSelected || current.PackPoints >= mission.PreparationCost)
            && (mission.Id != FoxExpeditionType.SearchLost || hasTarget);
        DrawText(ctx, "Party planner", 26, ContentY + 21, 29, TextWhite);
        DrawText(ctx, "Review your companions before departure. Injuries and pregnancy can reduce contribution.",
            26, ContentY + 49, 15, TextMuted);
        DrawRect(ctx, 18, MissionPanelTop, 220, 558, TextPanel);
        DrawRect(ctx, 250, MissionPanelTop, 566, 558, TextPanel);
        DrawRect(ctx, 828, MissionPanelTop, 334, 558, TextPanel);
        DrawText(ctx, "Mission summary", 32, MissionPanelTop + 29, 19, TextGold);
        FoxGuiTheme.DrawSectionRule(ctx, 32, MissionPanelTop + 39, 192);
        DrawSimpleMissionSketch(ctx, mission, 32, MissionPanelTop + 56, 190, 130);
        DrawText(ctx, Trim(mission.Name, 21), 32, MissionPanelTop + 218, 20, TextWhite);
        DrawWrapped(ctx, mission.Description, 32, MissionPanelTop + 249, 190, 18,
            TextMuted, 5, 13);
        DrawText(ctx, $"Party: {mission.MinimumFoxes}–{mission.MaximumFoxes}", 32,
            MissionPanelTop + 356, 13, TextGold);
        DrawText(ctx, $"Travel: {FormatExpeditionHours(mission.BaseDurationHours)}", 32,
            MissionPanelTop + 381, 13, TextWhite);
        DrawText(ctx, $"Risk: {mission.RiskLabel}", 32, MissionPanelTop + 406, 13, TextWhite);
        DrawButton(ctx, "Back to mission", 32, MissionPanelTop + 492, 190, 43, true);
        DrawText(ctx, "Available companions", 264, MissionPanelTop + 29, 19, TextGold);
        DrawText(ctx, $"{members.Count} companions", 688, MissionPanelTop + 29, 13, TextMuted);
        FoxGuiTheme.DrawSectionRule(ctx, 264, MissionPanelTop + 39, 538);
        DrawText(ctx, "Select a companion to add or remove them.", 264,
            MissionPanelTop + 67, 13, TextMuted);
        Dictionary<string, string> labels = BuildMemberLabels(members);
        ctx.Save();
        ctx.Rectangle(258, PlannerRowsTop, 550, PlannerRowsHeight);
        ctx.Clip();
        for (int i = 0; i < members.Count; i++)
        {
            FoxPackMemberPacket member = members[i];
            double y = PlannerRowsTop + i * PlannerRowHeight - expeditionScrollOffset;
            if (y + PlannerRowHeight < PlannerRowsTop || y > PlannerRowsTop + PlannerRowsHeight) continue;
            bool eligible = IsExpeditionEligible(member);
            bool chosen = eligible && selectedFoxIds.Contains(member.FoxId);
            bool target = mission.Id == FoxExpeditionType.SearchLost && IsRescueTarget(member);
            DrawRect(ctx, 258, y, 550, PlannerRowHeight - 4,
                chosen || member.FoxId == selectedMissingFoxId ? TextSelectedPanel : TextPanel);
            DrawRect(ctx, 270, y + 16, 18, 18, chosen || member.FoxId == selectedMissingFoxId
                ? TextGold : TextCheckbox);
            if (chosen || member.FoxId == selectedMissingFoxId)
                DrawText(ctx, "✓", 272, y + 31, 16, TextDark);
            animalArt.DrawFace(ctx, member.SpeciesId, member.AppearanceCode, 298, y + 4, 44, member.EntityId, member.FoxId);
            DrawText(ctx, Trim(labels[member.FoxId], 20), 350, y + 23, 15,
                eligible || target ? TextWhite : TextMuted);
            DrawText(ctx, target ? "Rescue target" : eligible ? $"Health {member.CurrentHealth:0.#}/{member.MaxHealth:0.#}"
                : FriendlyStatus(member.Status), 350, y + 43, 12, TextMuted);
            if (eligible) DrawText(ctx, $"+{GetMemberStrength(member):0.#}", 740, y + 32, 14, TextGold);
            var trait = CompanionSpeciesTraits.Get(member.SpeciesId);
            if (trait != null) DrawText(ctx, Trim(trait.Name, 23), 530, y + 43, 11, TextGold);
        }
        ctx.Restore();
        DrawScrollbar(ctx, 806, PlannerRowsTop, members.Count * PlannerRowHeight,
            expeditionScrollOffset, PlannerRowsHeight);
        DrawText(ctx, "Selected party", 842, MissionPanelTop + 29, 19, TextGold);
        FoxGuiTheme.DrawSectionRule(ctx, 842, MissionPanelTop + 39, 306);
        DrawText(ctx, $"Members: {selectedCount}/{mission.MaximumFoxes}", 842,
            MissionPanelTop + 76, 15, TextWhite);
        DrawText(ctx, $"Strength: {strength:0.#} / {mission.TargetStrength:0.#}", 842,
            MissionPanelTop + 102, 15, TextWhite);
        int row = 0;
        foreach (FoxPackMemberPacket member in members.Where(m => selectedFoxIds.Contains(m.FoxId)
            && IsExpeditionEligible(m)).Take(5))
            DrawText(ctx, Trim(CompanionDisplayName(member.Name), 28), 842,
                MissionPanelTop + 136 + row++ * 24, 13, TextMuted);
        if (selectedCount > 5) DrawText(ctx, $"and {selectedCount - 5} more", 842,
            MissionPanelTop + 136 + row * 24, 13, TextMuted);
        DrawSpeciesPartyPreview(ctx, current, mission.Id, 842, MissionPanelTop + 270, 305);
        double guidanceScale = FeralKinshipCompanionUiSettings.TextScale;
        ctx.Save();
        ctx.Rectangle(842, MissionPanelTop + 376, 305, 50);
        ctx.Clip();
        DrawWrapped(ctx, GetDepartureGuidance(current, mission, routeUnlocked, prerequisites,
                selectedCount, current.LootItems?.Count > 0, current.RecruitmentReady,
                hasTarget, !prepareSelected || current.PackPoints >= mission.PreparationCost),
            842, MissionPanelTop + 376 + 12 * guidanceScale, 305, 17, canStart ? TextGold : TextRed,
            Math.Max(1, (int)Math.Floor(50 / (17 * guidanceScale))), 12);
        ctx.Restore();
        DrawChoiceButton(ctx, $"Preparation — {mission.PreparationCost} points", 842,
            MissionPanelTop + 434, 306, 30, mission.PreparationCost > 0, prepareSelected);
        DrawButton(ctx, "Auto-fill healthy", 842, MissionPanelTop + 470, 146, 32, true);
        DrawButton(ctx, "Clear party", 998, MissionPanelTop + 470, 150, 32, selectedCount > 0);
        DrawButton(ctx, "Start expedition", 842, MissionPanelTop + 511, 306, 38, canStart);
    }

    private void HandleExpeditionBrowserClick(FoxPackStatePacket current, double x, double y)
    {
        if (x >= 32 && x <= 224 && y >= MissionPanelTop + 61 && y <= MissionPanelTop + 274)
        {
            int category = (int)((y - MissionPanelTop - 61) / 55);
            if (category >= 0 && category <= 2)
            {
                expeditionCategory = new[] { "hunt", "forage", "utility" }[category];
                selectedExpeditionType = string.Empty;
                expeditionScrollOffset = 0;
                FoxGuiTheme.PlayNavigation(api); Redraw();
            }
            else if (category == 3)
            {
                SelectTab(ScavengeTab); Redraw();
            }
            return;
        }
        List<FoxExpeditionDefinition> missions = GetCategoryMissions();
        if (x >= 258 && x <= 808 && y >= MissionRowsTop && y <= MissionRowsTop + MissionRowsHeight)
        {
            int index = (int)((y - MissionRowsTop + expeditionScrollOffset) / MissionRowHeight);
            if (index >= 0 && index < missions.Count)
            {
                selectedExpeditionType = missions[index].Id;
                FoxGuiTheme.PlayChoice(api); Redraw();
            }
            return;
        }
        if (x >= 842 && x <= 1148 && y >= MissionPanelTop + 469 && y <= MissionPanelTop + 515)
        {
            FoxExpeditionDefinition mission = FoxExpeditionCatalog.Get(selectedExpeditionType);
            if (mission == null) return;
            bool unlocked = mission.DefaultUnlocked || (current.UnlockedExpeditionTypes ?? new()).Contains(mission.Id);
            if (unlocked)
            {
                expeditionPartyPlannerOpen = true;
                expeditionScrollOffset = 0;
                FoxGuiTheme.PlayNavigation(api); Redraw();
            }
            else
            {
                purchaseRouteId = mission.Id;
                FoxGuiTheme.PlayNavigation(api); Redraw();
            }
        }
    }

    private void HandleExpeditionPartyPlannerClick(FoxPackStatePacket current, double x, double y)
    {
        FoxExpeditionDefinition mission = FoxExpeditionCatalog.Get(selectedExpeditionType);
        if (mission == null) return;
        if (x >= 32 && x <= 222 && y >= MissionPanelTop + 492 && y <= MissionPanelTop + 535)
        {
            expeditionPartyPlannerOpen = false; expeditionScrollOffset = 0;
            FoxGuiTheme.PlayNavigation(api); Redraw(); return;
        }
        List<FoxPackMemberPacket> members = current.Members ?? new();
        if (x >= 258 && x <= 808 && y >= PlannerRowsTop && y <= PlannerRowsTop + PlannerRowsHeight)
        {
            int index = (int)((y - PlannerRowsTop + expeditionScrollOffset) / PlannerRowHeight);
            if (index >= 0 && index < members.Count)
            {
                FoxPackMemberPacket member = members[index];
                if (mission.Id == FoxExpeditionType.SearchLost && IsRescueTarget(member))
                    selectedMissingFoxId = selectedMissingFoxId == member.FoxId ? string.Empty : member.FoxId;
                else if (IsExpeditionEligible(member))
                {
                    if (!selectedFoxIds.Add(member.FoxId)) selectedFoxIds.Remove(member.FoxId);
                }
                else { FoxGuiTheme.PlayUnavailable(api); return; }
                FoxGuiTheme.PlayChoice(api); Redraw();
            }
            return;
        }
        if (x < 842 || x > 1148) return;
        if (y >= MissionPanelTop + 434 && y <= MissionPanelTop + 464 && mission.PreparationCost > 0)
        {
            prepareSelected = !prepareSelected; FoxGuiTheme.PlayChoice(api); Redraw(); return;
        }
        if (y >= MissionPanelTop + 470 && y <= MissionPanelTop + 502)
        {
            if (x < 988) SelectHealthy(current);
            else selectedFoxIds.Clear();
            FoxGuiTheme.PlayChoice(api); Redraw(); return;
        }
        if (y < MissionPanelTop + 511 || y > MissionPanelTop + 549) return;
        HashSet<string> unlocked = (current.UnlockedExpeditionTypes ?? new()).ToHashSet(StringComparer.Ordinal);
        int count = members.Count(m => selectedFoxIds.Contains(m.FoxId) && IsExpeditionEligible(m));
        bool hasTarget = members.Any(m => m.FoxId == selectedMissingFoxId && IsRescueTarget(m))
            && !IsSearchTargetActive(current, selectedMissingFoxId);
        bool canStart = (mission.DefaultUnlocked || unlocked.Contains(mission.Id))
            && AreRoutePrerequisitesMet(mission, unlocked)
            && GetActiveExpeditions(current).Count < Math.Max(1, current.ExpeditionCapacity)
            && current.LootItems?.Count == 0 && !current.RecruitmentReady
            && count >= mission.MinimumFoxes && count <= mission.MaximumFoxes
            && (!prepareSelected || current.PackPoints >= mission.PreparationCost)
            && (mission.Id != FoxExpeditionType.SearchLost || hasTarget);
        if (!canStart) { FoxGuiTheme.PlayUnavailable(api); return; }
        FoxGuiTheme.PlayAction(api);
        system.SendFoxSocialAction(sourceEntityId, FoxSocialRequestAction.StartPackExpedition,
            selectedExpeditionType, selectedFoxIds, selectedMissingFoxId, prepareSelected);
    }

    private void DrawSimpleMissionSketch(Context ctx, FoxExpeditionDefinition mission,
        double x, double y, double width, double height)
    {
        if (sceneArt.DrawMission(ctx, mission.Id, x, y, width, height)) return;
        // Keep every mark within the card and put the subject on an obvious ground line.
        ctx.Save();
        ctx.Rectangle(x, y, width, height);
        ctx.Clip();
        double[] backdrop = { 0.09, 0.23, 0.17, 1d };
        double[] trees = { 0.20, 0.41, 0.28, 1d };
        double[] subject = { 0.56, 0.77, 0.59, 1d };
        DrawRect(ctx, x, y, width, height, backdrop);
        for (int i = 0; i < 3; i++)
        {
            double center = x + width * (.16 + i * .34);
            ctx.SetSourceRGBA(trees[0], trees[1], trees[2], trees[3]);
            ctx.MoveTo(center - width * .10, y + height * .79);
            ctx.LineTo(center, y + height * (.17 + (i % 2) * .12));
            ctx.LineTo(center + width * .10, y + height * .79);
            ctx.ClosePath(); ctx.Fill();
        }
        DrawRect(ctx, x, y + height * .79, width, height * .21,
            new[] { 0.12, 0.31, 0.22, 1d });
        bool isHunt = mission.Id is FoxExpeditionType.Hunt or FoxExpeditionType.GreatHunt
            or FoxExpeditionType.ApexHunt or FoxExpeditionType.DeepWilds;
        bool isForage = mission.Id is FoxExpeditionType.Forage or FoxExpeditionType.DistantForage
            or FoxExpeditionType.PrimevalReach;
        if (mission.Id == FoxExpeditionType.PrimevalReach)
        {
            ctx.SetSourceRGBA(.32, .52, .41, 1d);
            ctx.MoveTo(x + width * .13, y + height * .59);
            ctx.LineTo(x + width * .44, y + height * .08);
            ctx.LineTo(x + width * .70, y + height * .59);
            ctx.ClosePath(); ctx.Fill();
        }
        ctx.SetSourceRGBA(subject[0], subject[1], subject[2], subject[3]);
        if (isHunt)
        {
            double bodySize = mission.Id == FoxExpeditionType.ApexHunt ? .19 : .15;
            ctx.Arc(x + width * .50, y + height * .60, height * bodySize, 0, Math.PI * 2); ctx.Fill();
            DrawRect(ctx, x + width * .57, y + height * .55, width * .16, height * .07, subject);
            ctx.Arc(x + width * .74, y + height * .52, height * .065, 0, Math.PI * 2); ctx.Fill();
            DrawRect(ctx, x + width * .42, y + height * .69, width * .025, height * .13, subject);
            DrawRect(ctx, x + width * .58, y + height * .69, width * .025, height * .13, subject);
            DrawRect(ctx, x + width * .78, y + height * .49, width * .06, height * .018, subject);
            if (mission.Id == FoxExpeditionType.GreatHunt)
            {
                DrawRect(ctx, x + width * .72, y + height * .29, width * .015, height * .17, subject);
                DrawRect(ctx, x + width * .68, y + height * .33, width * .12, height * .014, subject);
            }
            else if (mission.Id == FoxExpeditionType.Hunt)
            {
                DrawRect(ctx, x + width * .70, y + height * .35, width * .02, height * .13, subject);
                DrawRect(ctx, x + width * .75, y + height * .34, width * .02, height * .13, subject);
            }
            else
            {
                ctx.MoveTo(x + width * .77, y + height * .55);
                ctx.LineTo(x + width * .87, y + height * .61);
                ctx.LineTo(x + width * .80, y + height * .62);
                ctx.ClosePath(); ctx.Fill();
            }
        }
        else if (isForage)
        {
            DrawRect(ctx, x + width * .49, y + height * .34, width * .025, height * .48, subject);
            ctx.Arc(x + width * .40, y + height * .50, height * .10, 0, Math.PI * 2); ctx.Fill();
            ctx.Arc(x + width * .59, y + height * .42, height * .10, 0, Math.PI * 2); ctx.Fill();
            ctx.Arc(x + width * .58, y + height * .61, height * .07, 0, Math.PI * 2); ctx.Fill();
            if (mission.Id != FoxExpeditionType.Forage)
            {
                DrawRect(ctx, x + width * .29, y + height * .48, width * .02, height * .34, subject);
                ctx.Arc(x + width * .26, y + height * .51, height * .06, 0, Math.PI * 2); ctx.Fill();
            }
        }
        else if (mission.Id is FoxExpeditionType.RuinDelve or FoxExpeditionType.ResonantDepths)
        {
            DrawRect(ctx, x + width * .28, y + height * .32, width * .44, height * .50, subject);
            ctx.SetSourceRGBA(backdrop[0], backdrop[1], backdrop[2], 1d);
            ctx.Arc(x + width * .50, y + height * .59, height * .14, Math.PI, Math.PI * 2); ctx.Fill();
            DrawRect(ctx, x + width * .39, y + height * .58, width * .22, height * .24, backdrop);
        }
        else if (mission.Id == FoxExpeditionType.PackPatrol)
        {
            DrawRect(ctx, x + width * .49, y + height * .22, width * .025, height * .59, subject);
            ctx.MoveTo(x + width * .52, y + height * .22);
            ctx.LineTo(x + width * .76, y + height * .30);
            ctx.LineTo(x + width * .52, y + height * .47);
            ctx.ClosePath(); ctx.Fill();
        }
        else
        {
            DrawRect(ctx, x + width * .36, y + height * .53, width * .38, height * .22, subject);
            DrawRect(ctx, x + width * .41, y + height * .42, width * .025, height * .13, subject);
            DrawRect(ctx, x + width * .66, y + height * .42, width * .025, height * .13, subject);
            ctx.SetSourceRGBA(backdrop[0], backdrop[1], backdrop[2], 1d);
            ctx.Arc(x + width * .43, y + height * .76, height * .055, 0, Math.PI * 2); ctx.Fill();
            ctx.Arc(x + width * .67, y + height * .76, height * .055, 0, Math.PI * 2); ctx.Fill();
        }
        ctx.SetSourceRGBA(.56, .79, .58, .85);
        ctx.LineWidth = 1.5;
        ctx.Rectangle(x + .75, y + .75, width - 1.5, height - 1.5);
        ctx.Stroke();
        ctx.Restore();
    }
}
