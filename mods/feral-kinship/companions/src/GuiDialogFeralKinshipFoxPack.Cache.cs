#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;

namespace FeralKinshipCompanions;

internal sealed partial class GuiElementFeralKinshipFoxPackSurface
{
    private const double CacheTop = ContentY + 70;
    private const double CacheRowsTop = ContentY + 158;
    private const double CacheRowsHeight = 406;
    private const double CacheRowHeight = 58;

    private static string CacheSelectionKey(int index, byte[] bytes) =>
        index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":"
            + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes ?? Array.Empty<byte>()));

    private int FindSelectedCacheIndex(FoxPackStatePacket current)
    {
        List<FoxPackLootItemPacket> items = current?.LootItems;
        if (items == null || items.Count == 0) return -1;
        for (int i = 0; i < items.Count; i++)
            if (selectedCacheKey == CacheSelectionKey(i, items[i].StackBytes)) return i;
        return 0;
    }

    private void DrawCacheOverview(Context ctx, FoxPackStatePacket current, int width)
    {
        List<FoxPackLootItemPacket> loot = current.LootItems ?? new();
        int total = loot.Sum(item => Math.Max(0, item.Count));
        bool blocked = loot.Count > 0 || current.RecruitmentReady;
        DrawText(ctx, "Expedition cache", 26, ContentY + 22, 29, TextWhite);
        DrawText(ctx, "Rewards stay here until claimed. New expeditions cannot depart while the cache contains items.",
            26, ContentY + 49, 15, TextMuted);
        DrawRect(ctx, 18, CacheTop, 242, 558, TextPanel);
        DrawRect(ctx, 270, CacheTop, 588, 558, TextPanel);
        DrawRect(ctx, 870, CacheTop, 292, 558, TextPanel);
        DrawText(ctx, "Cache summary", 32, CacheTop + 28, 20, TextGold);
        FoxGuiTheme.DrawSectionRule(ctx, 32, CacheTop + 38, 214);
        if (blocked)
        {
            DrawRect(ctx, 30, CacheTop + 56, 218, 116, TextRedPanel);
            DrawText(ctx, "Expeditions blocked", 42, CacheTop + 82, 17, TextRed);
            DrawWrapped(ctx, "Claim the cached rewards before sending another party.",
                42, CacheTop + 108, 192, 17, TextWhite, 3, 13);
        }
        else
            DrawWrapped(ctx, "The cache is empty. New expeditions may begin.",
                32, CacheTop + 82, 210, 18, TextWhite, 3, 14);
        DrawText(ctx, $"Total stacks: {loot.Count}", 32, CacheTop + 211, 16, TextWhite);
        DrawText(ctx, $"Total items: {total}", 32, CacheTop + 243, 16, TextWhite);
        DrawWrapped(ctx, "Last source: " + (current.LastExpedition?.Name ?? "None yet"),
            32, CacheTop + 285, 210, 19, TextMuted, 3, 14);
        DrawButton(ctx, "Claim all items", 32, CacheTop + 370, 214, 39, loot.Count > 0);
        if (cartAccess)
            DrawButton(ctx, current.CargoUnloadingActive ? "Stop unloading" : "Empty cart with pack",
                32, CacheTop + 418, 214, 39, loot.Count > 0 || current.CargoUnloadingActive);
        if (current.RecruitmentReady)
        {
            DrawWrapped(ctx, $"{current.PendingRecruitmentCount} companion recruitment reward(s) ready.",
                32, CacheTop + 474, 212, 18, TextGold, 2, 13);
            DrawButton(ctx, "Claim companion", 32, CacheTop + 511, 214, 35, true);
        }
        DrawText(ctx, "Cached items", 284, CacheTop + 28, 20, TextGold);
        DrawText(ctx, $"{loot.Count} stacks  ·  {total} items", 650, CacheTop + 28, 13, TextMuted);
        FoxGuiTheme.DrawSectionRule(ctx, 284, CacheTop + 38, 558);
        DrawText(ctx, "Select stacks to claim specific items.", 284, CacheTop + 67, 13, TextMuted);
        if (loot.Count == 0)
            DrawText(ctx, "No items are waiting in the cart.", 288, CacheRowsTop + 34, 16, TextMuted);
        ctx.Save();
        ctx.Rectangle(280, CacheRowsTop, 566, CacheRowsHeight);
        ctx.Clip();
        for (int i = 0; i < loot.Count; i++)
        {
            FoxPackLootItemPacket item = loot[i];
            double y = CacheRowsTop + i * CacheRowHeight - cacheScrollOffset;
            if (y + CacheRowHeight < CacheRowsTop || y > CacheRowsTop + CacheRowsHeight) continue;
            string key = CacheSelectionKey(i, item.StackBytes);
            bool selected = selectedCacheKeys.Contains(key);
            DrawRect(ctx, 280, y, 566, CacheRowHeight - 4,
                selected || selectedCacheKey == key ? TextSelectedPanel : TextPanel);
            DrawRect(ctx, 290, y + 17, 18, 18, selected ? TextGold : TextCheckbox);
            if (selected) DrawText(ctx, "✓", 292, y + 32, 16, TextDark);
            DrawText(ctx, Trim(item.Name, 42), 364, y + 24, 16, TextWhite);
            DrawText(ctx, $"Quantity: {item.Count}", 364, y + 45, 12, TextMuted);
        }
        ctx.Restore();
        DrawScrollbar(ctx, 846, CacheRowsTop, loot.Count * CacheRowHeight, cacheScrollOffset, CacheRowsHeight);
        DrawText(ctx, "Item details", 884, CacheTop + 28, 20, TextGold);
        FoxGuiTheme.DrawSectionRule(ctx, 884, CacheTop + 38, 264);
        int selectedIndex = FindSelectedCacheIndex(current);
        if (selectedIndex < 0)
        {
            DrawWrapped(ctx, "Select a cache row to inspect the item.", 884, CacheTop + 78,
                260, 18, TextMuted, 3, 14);
            return;
        }
        FoxPackLootItemPacket detail = loot[selectedIndex];
        DrawWrapped(ctx, detail.Name, 884, CacheTop + 83, 258, 22, TextWhite, 2, 18);
        DrawRect(ctx, 968, CacheTop + 93, 100, 100, TextSelectedPanel);
        DrawText(ctx, $"Quantity: {detail.Count}", 884, CacheTop + 224, 15, TextWhite);
        DrawWrapped(ctx, "Source: " + (current.LastExpedition?.Name ?? "Recent expedition"),
            884, CacheTop + 254, 258, 18, TextMuted, 3, 14);
        DrawText(ctx, $"Selected stacks: {selectedCacheKeys.Count}", 884, CacheTop + 325, 14, TextGold);
        DrawButton(ctx, selectedCacheKeys.Count == 1 ? "Claim selected item" : "Claim selected stacks",
            884, CacheTop + 430, 264, 42, selectedCacheKeys.Count > 0);
        DrawButton(ctx, "Open source report", 884, CacheTop + 480, 264, 38,
            GetExpeditionReports(current).Count > 0);
    }

    private void HandleCacheOverviewClick(FoxPackStatePacket current, double x, double y)
    {
        List<FoxPackLootItemPacket> loot = current.LootItems ?? new();
        if (x >= 280 && x <= 846 && y >= CacheRowsTop && y <= CacheRowsTop + CacheRowsHeight)
        {
            int index = (int)((y - CacheRowsTop + cacheScrollOffset) / CacheRowHeight);
            if (index >= 0 && index < loot.Count)
            {
                string key = CacheSelectionKey(index, loot[index].StackBytes);
                selectedCacheKey = key;
                if (!selectedCacheKeys.Add(key)) selectedCacheKeys.Remove(key);
                FoxGuiTheme.PlayChoice(api); Redraw();
            }
            return;
        }
        if (x >= 32 && x <= 246)
        {
            if (y >= CacheTop + 370 && y <= CacheTop + 409 && loot.Count > 0)
            {
                FoxGuiTheme.PlayAction(api);
                selectedCacheKeys.Clear();
                system.SendFoxSocialAction(sourceEntityId, FoxSocialRequestAction.ClaimPackLoot);
            }
            else if (cartAccess && y >= CacheTop + 418 && y <= CacheTop + 457
                && (loot.Count > 0 || current.CargoUnloadingActive))
            {
                FoxGuiTheme.PlayAction(api);
                system.SendFoxSocialAction(sourceEntityId, FoxSocialRequestAction.SendPackLootToStorage);
            }
            else if (current.RecruitmentReady && y >= CacheTop + 511 && y <= CacheTop + 546)
            {
                FoxGuiTheme.PlayAction(api);
                system.SendFoxSocialAction(sourceEntityId, FoxSocialRequestAction.ClaimRecruitment);
            }
            return;
        }
        if (x >= 884 && x <= 1148)
        {
            if (y >= CacheTop + 430 && y <= CacheTop + 472 && selectedCacheKeys.Count > 0)
            {
                FoxGuiTheme.PlayAction(api);
                string[] selection = selectedCacheKeys.ToArray();
                selectedCacheKeys.Clear();
                system.SendFoxSocialAction(sourceEntityId, FoxSocialRequestAction.ClaimSelectedPackLoot,
                    selectedFoxIds: selection);
            }
            else if (y >= CacheTop + 480 && y <= CacheTop + 518
                && GetExpeditionReports(current).Count > 0)
            {
                SelectTab(LastTripTab); Redraw();
            }
        }
    }
}
