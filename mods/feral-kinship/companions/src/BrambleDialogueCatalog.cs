#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace FeralKinshipCompanions;

internal sealed class BrambleDialogueDocument
{
    public string Source { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public List<BrambleDialogueEntry> Entries { get; set; } = new();
}

internal sealed class BrambleDialogueEntry
{
    public string Id { get; set; } = string.Empty;
    public string Layer { get; set; } = string.Empty;
    public string Line { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Cooldown { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
}

internal sealed class BrambleDialogueCatalog
{
    internal const string AssetPath = "config/bramble-dialogue.json";
    private readonly Dictionary<string, BrambleDialogueEntry> entries = new(StringComparer.OrdinalIgnoreCase);

    public int Count => entries.Count;

    public void Load(ICoreAPI api)
    {
        entries.Clear();
        try
        {
            IAsset? asset = api.Assets.TryGet(new AssetLocation("feralkinshipcompanions", AssetPath));
            BrambleDialogueDocument? document = asset?.ToObject<BrambleDialogueDocument>();
            foreach (BrambleDialogueEntry entry in document?.Entries ?? new List<BrambleDialogueEntry>())
            {
                if (!string.IsNullOrWhiteSpace(entry.Id) && !string.IsNullOrWhiteSpace(entry.Line))
                {
                    entries[entry.Id] = entry;
                }
            }
            api.Logger.Notification(
                "[FeralKinshipCompanions] Bramble dialogue loaded: {0} authored entries.",
                entries.Count);
        }
        catch (Exception exception)
        {
            api.Logger.Error(
                "[FeralKinshipCompanions] Bramble dialogue could not be loaded: {0}",
                exception.Message);
        }
    }

    public string Line(string id, string fallback = "")
    {
        return entries.TryGetValue(id, out BrambleDialogueEntry? entry)
            ? entry.Line
            : fallback;
    }

    public BrambleDialogueEntry? Get(string id)
    {
        return entries.TryGetValue(id, out BrambleDialogueEntry? entry) ? entry : null;
    }

    public IReadOnlyList<BrambleDialogueEntry> WithPrefix(string prefix)
    {
        return entries.Values
            .Where(entry => entry.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
