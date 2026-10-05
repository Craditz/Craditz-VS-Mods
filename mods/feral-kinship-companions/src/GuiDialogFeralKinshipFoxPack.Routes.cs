#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;

namespace FeralKinshipCompanions;

internal sealed partial class GuiElementFeralKinshipFoxPackSurface
{
    private const double RouteTop = ContentY + 72;
    private const double RouteRowTop = ContentY + 128;
    private const double RouteCardHeight = 120;

    private void DrawRoutesOverview(Context ctx, FoxPackStatePacket current, int width)
    {
        DrawText(ctx, "Unlock new expeditions", 26, ContentY + 21, 29, TextWhite);
        DrawText(ctx, "Spend shared pack points to unlock stronger expedition options.",
            26, ContentY + 49, 15, TextMuted);
        DrawRect(ctx, 18, RouteTop, 218, 558, TextPanel);
        DrawRect(ctx, 248, RouteTop, 574, 558, TextPanel);
        DrawRect(ctx, 834, RouteTop, 328, 558, TextPanel);
        DrawText(ctx, "How unlocks work", 32, RouteTop + 30, 19, TextGold);
        FoxGuiTheme.DrawSectionRule(ctx, 32, RouteTop + 39, 190);
        string[] steps = { "1  Spend pack points", "2  Unlock a route", "3  New mission appears" };
        for (int i = 0; i < steps.Length; i++)
        {
            DrawText(ctx, steps[i], 32, RouteTop + 85 + i * 82, 16, TextWhite);
            DrawWrapped(ctx, i switch
                {
                    0 => "Use the pack's shared point pool.",
                    1 => "Check the cost and prerequisites.",
                    _ => "Find it under the matching activity."
                }, 32, RouteTop + 108 + i * 82, 186, 16, TextMuted, 2, 12);
        }
        FoxGuiTheme.DrawSectionRule(ctx, 32, RouteTop + 348, 190);
        DrawText(ctx, "Available pack points", 32, RouteTop + 384, 15, TextGold);
        DrawText(ctx, current.PackPoints.ToString(), 32, RouteTop + 425, 36, TextWhite);
        DrawWrapped(ctx, "Unlocked routes are permanent and add missions to Expeditions.",
            32, RouteTop + 462, 186, 19, TextMuted, 4, 12);
        DrawText(ctx, "Available unlocks", 262, RouteTop + 30, 19, TextGold);
        DrawText(ctx, $"{FoxExpeditionCatalog.Unlockable.Count} routes", 702,
            RouteTop + 30, 13, TextMuted);
        FoxGuiTheme.DrawSectionRule(ctx, 262, RouteTop + 39, 546);
        HashSet<string> unlocked = (current.UnlockedExpeditionTypes ?? new()).ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<FoxExpeditionDefinition> routes = FoxExpeditionCatalog.Unlockable;
        FoxExpeditionDefinition selected = routes.FirstOrDefault(route => route.Id == selectedRouteId)
            ?? routes.FirstOrDefault();
        if (selected != null) selectedRouteId = selected.Id;
        ctx.Save();
        ctx.Rectangle(256, RouteRowTop, 558, 480);
        ctx.Clip();
        for (int i = 0; i < routes.Count; i++)
        {
            FoxExpeditionDefinition route = routes[i];
            int column = i % 2;
            int row = i / 2;
            double x = 256 + column * 280;
            double y = RouteRowTop + row * RouteCardHeight;
            bool bought = unlocked.Contains(route.Id);
            DrawRect(ctx, x, y, 272, 112, route.Id == selectedRouteId ? TextSelectedPanel : TextPanel);
            DrawSimpleMissionSketch(ctx, route, x + 9, y + 8, 72, 73);
            DrawText(ctx, Trim(route.Name, 19), x + 90, y + 25, 15, TextWhite);
            DrawText(ctx, route.TierLabel, x + 90, y + 44, 12, TextGold);
            DrawWrapped(ctx, route.Description, x + 90, y + 61, 171, 14, TextMuted, 2, 11);
            DrawText(ctx, bought ? "Unlocked" : $"Cost: {route.UnlockCost} points",
                x + 11, y + 101, 12, bought ? TextGold : TextWhite);
        }
        ctx.Restore();
        DrawText(ctx, "Unlock details", 848, RouteTop + 30, 19, TextGold);
        FoxGuiTheme.DrawSectionRule(ctx, 848, RouteTop + 39, 300);
        if (selected == null) return;
        DrawText(ctx, Trim(selected.Name, 27), 848, RouteTop + 80, 20, TextWhite);
        DrawSimpleMissionSketch(ctx, selected, 848, RouteTop + 94, 300, 154);
        DrawWrapped(ctx, selected.Description, 848, RouteTop + 276, 300, 18,
            TextWhite, 4, 13);
        DrawText(ctx, $"Cost: {selected.UnlockCost} pack points", 848, RouteTop + 358, 14, TextGold);
        DrawText(ctx, $"Party: {selected.MinimumFoxes}–{selected.MaximumFoxes}", 848,
            RouteTop + 383, 14, TextWhite);
        DrawText(ctx, $"Travel: {FormatExpeditionHours(selected.BaseDurationHours)}", 848,
            RouteTop + 408, 14, TextWhite);
        DrawWrapped(ctx, "Requires: " + GetRoutePrerequisiteNames(selected), 848,
            RouteTop + 436, 300, 16,
            AreRoutePrerequisitesMet(selected, unlocked) ? TextMuted : TextRed, 2, 12);
        bool canBuy = !unlocked.Contains(selected.Id) && current.PackPoints >= selected.UnlockCost
            && AreRoutePrerequisitesMet(selected, unlocked);
        DrawButton(ctx, unlocked.Contains(selected.Id) ? "Unlocked" : $"Unlock route — {selected.UnlockCost} points",
            848, RouteTop + 483, 300, 41, canBuy);
    }

    private void HandleRoutesOverviewClick(FoxPackStatePacket current, double x, double y)
    {
        IReadOnlyList<FoxExpeditionDefinition> routes = FoxExpeditionCatalog.Unlockable;
        if (x >= 256 && x < 814 && y >= RouteRowTop && y < RouteRowTop + 480)
        {
            int column = x < 536 ? 0 : 1;
            int row = (int)((y - RouteRowTop) / RouteCardHeight);
            int index = row * 2 + column;
            if (index >= 0 && index < routes.Count)
            {
                selectedRouteId = routes[index].Id;
                FoxGuiTheme.PlayChoice(api); Redraw();
            }
            return;
        }
        if (x >= 848 && x <= 1148 && y >= RouteTop + 483 && y <= RouteTop + 524)
        {
            FoxExpeditionDefinition selected = routes.FirstOrDefault(route => route.Id == selectedRouteId);
            if (selected == null) return;
            HashSet<string> unlocked = (current.UnlockedExpeditionTypes ?? new()).ToHashSet(StringComparer.Ordinal);
            if (unlocked.Contains(selected.Id) || current.PackPoints < selected.UnlockCost
                || !AreRoutePrerequisitesMet(selected, unlocked))
            {
                FoxGuiTheme.PlayUnavailable(api); return;
            }
            purchaseRouteId = selected.Id;
            FoxGuiTheme.PlayNavigation(api); Redraw();
        }
    }
}
