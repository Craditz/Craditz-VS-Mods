#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;

namespace FeralKinshipCompanions;

internal sealed partial class GuiElementFeralKinshipFoxPackSurface
{
    private const double TalentTop = ContentY + 72;
    private const double TalentNodeTop = TalentTop + 53;
    private const double TalentNodeHeight = 82;
    private const double TalentViewportHeight = 480;
    private double packTalentScrollOffset;

    private List<(PackTalentDefinition Talent, double X, double Y)> PackTalentCards(PackTalentGroup group)
    {
        var cards = new List<(PackTalentDefinition, double, double)>();
        double cursor = TalentNodeTop;
        foreach (IGrouping<int, PackTalentDefinition> tier in group.Talents
            .OrderBy(talent => talent.Column).ThenBy(talent => talent.Row)
            .GroupBy(talent => talent.Column))
        {
            cursor += 31;
            int index = 0;
            foreach (PackTalentDefinition talent in tier)
            {
                cards.Add((talent, 274 + index % 2 * 278, cursor + index / 2 * 90));
                index++;
            }
            cursor += Math.Max(1, (index + 1) / 2) * 90 + 9;
        }
        return cards;
    }

    private double PackTalentContentHeight(PackTalentGroup group)
    {
        List<(PackTalentDefinition Talent, double X, double Y)> cards = PackTalentCards(group);
        return cards.Count == 0 ? 0 : cards.Max(card => card.Y + TalentNodeHeight) - TalentNodeTop;
    }

    private void DrawPackTalentsOverview(Context ctx, FoxPackStatePacket current, int width)
    {
        DrawText(ctx, "Pack talents", 26, ContentY + 22, 29, TextWhite);
        DrawText(ctx, "Permanent upgrades for the whole pack, bought with shared pack points.",
            26, ContentY + 49, 15, TextMuted);
        DrawRect(ctx, 18, TalentTop, 236, 558, TextPanel);
        DrawRect(ctx, 266, TalentTop, 580, 558, TextPanel);
        DrawRect(ctx, 858, TalentTop, 304, 558, TextPanel);
        DrawText(ctx, "Talent paths", 32, TalentTop + 29, 19, TextGold);
        FoxGuiTheme.DrawSectionRule(ctx, 32, TalentTop + 39, 208);
        for (int i = 0; i < PackTalentCatalog.Groups.Count; i++)
        {
            PackTalentGroup group = PackTalentCatalog.Groups[i];
            int unlocked = group.Talents.Count(talent => IsPackTalentUnlocked(current, talent.Id));
            DrawChoiceButton(ctx, $"{group.Name}  {unlocked}/{group.Talents.Count}",
                32, TalentTop + 56 + i * 53, 208, 47, true, activePackTalentGroup == group.Id,
                GetPackTalentAccent(group.Id));
        }
        FoxGuiTheme.DrawSectionRule(ctx, 32, TalentTop + 438, 208);
        DrawText(ctx, "Pack points", 32, TalentTop + 469, 16, TextGold);
        DrawText(ctx, current.PackPoints.ToString(), 32, TalentTop + 508, 31, TextWhite);
        PackTalentGroup selectedGroup = PackTalentCatalog.Get(activePackTalentGroup);
        double[] accent = GetPackTalentAccent(selectedGroup.Id);
        DrawText(ctx, selectedGroup.Name, 280, TalentTop + 29, 20, accent);
        FoxGuiTheme.DrawSectionRule(ctx, 280, TalentTop + 39, 552);
        ctx.Save();
        ctx.Rectangle(270, TalentNodeTop, 572, TalentViewportHeight);
        ctx.Clip();
        int previousTier = -1;
        foreach (var card in PackTalentCards(selectedGroup))
        {
            PackTalentDefinition talent = card.Talent;
            double x = card.X;
            double y = card.Y - packTalentScrollOffset;
            if (talent.Column != previousTier)
            {
                DrawText(ctx, $"Tier {talent.Column + 1}", 280, y - 12, 14, TextGold);
                previousTier = talent.Column;
            }
            if (y + TalentNodeHeight < TalentNodeTop || y > TalentNodeTop + TalentViewportHeight) continue;
            bool unlocked = IsPackTalentUnlocked(current, talent.Id);
            int rank = GetPackTalentRank(current, talent);
            int cost = talent.Repeatable ? GetNextRepeatableTalentCost(rank) : talent.UnlockCost;
            bool prerequisite = string.IsNullOrWhiteSpace(talent.ParentId)
                || IsPackTalentUnlocked(current, talent.ParentId);
            bool canBuy = talent.Implemented && (talent.Repeatable || !unlocked)
                && prerequisite && current.PackPoints >= cost;
            DrawRect(ctx, x, y, 268, TalentNodeHeight - 5,
                talent.Id == selectedPackTalentId ? TextSelectedPanel : unlocked ? TextAvailable : TextCard);
            DrawPackTalentGlyph(ctx, x + 6, y + 7, accent);
            DrawWrapped(ctx, talent.Name, x + 52, y + 23, 204, 15,
                TextWhite, 2, 13);
            DrawText(ctx, !talent.Implemented ? "Coming later"
                    : unlocked && !talent.Repeatable ? "Unlocked"
                    : talent.Repeatable ? $"Rank {rank} · {cost} points"
                    : $"Cost: {cost} points",
                x + 8, y + 59, 12, canBuy ? accent : TextMuted);
            if (!prerequisite)
                DrawText(ctx, "Needs prerequisite", x + 140, y + 59, 11, TextRed);
        }
        ctx.Restore();
        DrawScrollbar(ctx, 840, TalentNodeTop, PackTalentContentHeight(selectedGroup),
            packTalentScrollOffset, TalentViewportHeight);
        DrawText(ctx, "Talent details", 872, TalentTop + 29, 19, TextGold);
        FoxGuiTheme.DrawSectionRule(ctx, 872, TalentTop + 39, 276);
        PackTalentDefinition selected = selectedGroup.Talents.FirstOrDefault(t => t.Id == selectedPackTalentId)
            ?? selectedGroup.Talents.FirstOrDefault();
        if (selected == null) return;
        if (selected.Id != selectedPackTalentId) selectedPackTalentId = selected.Id;
        int selectedRank = GetPackTalentRank(current, selected);
        int nextCost = selected.Repeatable ? GetNextRepeatableTalentCost(selectedRank) : selected.UnlockCost;
        bool selectedUnlocked = IsPackTalentUnlocked(current, selected.Id);
        bool requirement = string.IsNullOrWhiteSpace(selected.ParentId)
            || IsPackTalentUnlocked(current, selected.ParentId);
        bool ready = selected.Implemented && (selected.Repeatable || !selectedUnlocked)
            && requirement && current.PackPoints >= nextCost;
        DrawText(ctx, Trim(selected.Name, 28), 872, TalentTop + 85, 20, TextWhite);
        DrawPackTalentGlyph(ctx, 872, TalentTop + 100, accent);
        DrawText(ctx, selected.Repeatable ? $"Current rank: {selectedRank}"
            : selectedUnlocked ? "Unlocked" : "Not unlocked", 872,
            TalentTop + 172, 15, TextGold);
        DrawText(ctx, selected.Repeatable ? $"Next rank: {selectedRank + 1}"
            : "Single rank", 872, TalentTop + 198, 14, TextWhite);
        DrawText(ctx, $"Cost: {nextCost} pack points", 872, TalentTop + 225, 14, TextWhite);
        FoxGuiTheme.DrawSectionRule(ctx, 872, TalentTop + 241, 276);
        DrawText(ctx, "What changes", 872, TalentTop + 269, 16, TextGold);
        DrawWrapped(ctx, selected.Repeatable ? BuildRepeatableTalentDescription(selected, selectedRank)
                : selected.Description, 872, TalentTop + 294, 276, 18,
            TextWhite, 7, 13);
        DrawText(ctx, "Requirements", 872, TalentTop + 430, 16, TextGold);
        DrawWrapped(ctx, !selected.Implemented ? "Coming later"
            : !requirement ? "Unlock " + (PackTalentCatalog.GetTalent(selected.ParentId)?.Name ?? selected.ParentId) + " first."
            : current.PackPoints < nextCost ? $"Need {nextCost - current.PackPoints} more pack points."
            : "Ready to unlock.", 872, TalentTop + 453, 276, 17,
            ready ? TextWhite : TextRed, 2, 12);
        DrawButton(ctx, selectedUnlocked && !selected.Repeatable ? "Unlocked"
                : selected.Repeatable ? $"Upgrade to rank {selectedRank + 1}"
                : $"Unlock — {nextCost} points",
            872, TalentTop + 487, 276, 41, ready);
    }

    private void HandlePackTalentsOverviewClick(FoxPackStatePacket current, double x, double y)
    {
        if (x >= 32 && x <= 240 && y >= TalentTop + 56
            && y < TalentTop + 56 + PackTalentCatalog.Groups.Count * 53)
        {
            int index = (int)((y - TalentTop - 56) / 53);
            activePackTalentGroup = PackTalentCatalog.Groups[Math.Clamp(index, 0,
                PackTalentCatalog.Groups.Count - 1)].Id;
            selectedPackTalentId = string.Empty;
            packTalentScrollOffset = 0;
            FoxGuiTheme.PlayNavigation(api); Redraw(); return;
        }
        PackTalentGroup group = PackTalentCatalog.Get(activePackTalentGroup);
        if (x >= 274 && x < 830 && y >= TalentNodeTop && y <= TalentNodeTop + TalentViewportHeight)
        {
            PackTalentDefinition talent = PackTalentCards(group).FirstOrDefault(card =>
                x >= card.X && x < card.X + 268 &&
                y >= card.Y - packTalentScrollOffset && y < card.Y - packTalentScrollOffset + TalentNodeHeight).Talent;
            if (talent != null)
            {
                selectedPackTalentId = talent.Id;
                FoxGuiTheme.PlayChoice(api); Redraw();
            }
            return;
        }
        if (x < 872 || x > 1148 || y < TalentTop + 487 || y > TalentTop + 528) return;
        PackTalentDefinition selected = group.Talents.FirstOrDefault(t => t.Id == selectedPackTalentId);
        if (selected == null) return;
        int rank = GetPackTalentRank(current, selected);
        int cost = selected.Repeatable ? GetNextRepeatableTalentCost(rank) : selected.UnlockCost;
        bool prerequisite = string.IsNullOrWhiteSpace(selected.ParentId)
            || IsPackTalentUnlocked(current, selected.ParentId);
        if (!selected.Implemented || (!selected.Repeatable && IsPackTalentUnlocked(current, selected.Id))
            || !prerequisite || current.PackPoints < cost)
        {
            FoxGuiTheme.PlayUnavailable(api); return;
        }
        FoxGuiTheme.PlayAction(api);
        system.SendFoxSocialAction(sourceEntityId, FoxSocialRequestAction.UnlockPackTalent, selected.Id);
    }
}
