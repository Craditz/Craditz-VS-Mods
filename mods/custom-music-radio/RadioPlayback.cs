using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomMusicRadio;

public enum RadioPlaybackMode { RepeatCurrent, LoopPlaylist, ShufflePlaylist, NoLoop }

public sealed class RadioServerConfig
{
    public float MaxListeningRange { get; set; } = RadioControls.MaxListeningRange;
    public Dictionary<string, string> PlayerPlaylistOwnerUids { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class RadioClientConfig
{
    public bool AcceptServerAudioFiles { get; set; } = true;
}

public static class RadioPlaylists
{
    public const string Base = "";
    public static string Label(string name) => name.Length == 0 ? "Base music" : name;
    // Player UIDs are Base64 and may contain '/', which cannot be a folder name.
    public static string UidFolderName(string uid) => uid.Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static bool IsSafeFolder(string name) => name.Length > 0 && name is not "." and not ".."
        && name.IndexOfAny(new[] { '/', '\\', ':', '\0' }) < 0
        && string.Equals(System.IO.Path.GetFileName(name), name, StringComparison.Ordinal);

    public static bool CanSelect(string? personalOwnerUid, string radioOwnerUid)
        => personalOwnerUid == null || (radioOwnerUid.Length > 0 && personalOwnerUid == radioOwnerUid);

    public static string? Next(string current, string[] available)
    {
        if (available.Length == 0) return null;
        int index = Array.IndexOf(available, current);
        return available[(index + 1) % available.Length];
    }
}

public static class RadioPlayback
{
    public static RadioPlaybackMode Sanitize(int value) => value is >= 0 and <= 3 ? (RadioPlaybackMode)value : RadioPlaybackMode.RepeatCurrent;
    public static RadioPlaybackMode Next(RadioPlaybackMode mode) => (RadioPlaybackMode)(((int)Sanitize((int)mode) + 1) % 4);
    public static string Label(RadioPlaybackMode mode) => mode switch
    {
        RadioPlaybackMode.LoopPlaylist => "Loop playlist",
        RadioPlaybackMode.ShufflePlaylist => "Shuffle playlist",
        RadioPlaybackMode.NoLoop => "No loop",
        _ => "Repeat current"
    };
    public static string? NextTrack(RadioPlaybackMode mode, string current, string[] names)
    {
        if (mode == RadioPlaybackMode.NoLoop) return null;
        if (mode == RadioPlaybackMode.RepeatCurrent) return current;
        if (mode != RadioPlaybackMode.LoopPlaylist) return null; // Shuffle uses the per-radio bag.
        string[] sorted = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ThenBy(n => n, StringComparer.Ordinal).ToArray();
        if (sorted.Length == 0) return null;
        int index = Array.IndexOf(sorted, current);
        return sorted[(index + 1) % sorted.Length];
    }
}

// One history per placed radio. The current song counts as heard when shuffle begins.
public sealed class RadioShuffleBag
{
    private readonly HashSet<string> played = new(StringComparer.Ordinal);
    public string Playlist { get; private set; } = RadioPlaylists.Base;
    public string[] Played => played.OrderBy(name => name, StringComparer.Ordinal).ToArray();

    public void Reset(string playlist, string current = "")
    {
        Playlist = playlist;
        played.Clear();
        Record(playlist, current);
    }

    public void Restore(string playlist, IEnumerable<string> names)
    {
        Playlist = playlist;
        played.Clear();
        foreach (string name in names.Take(4096))
            if (name.Length > 0 && string.Equals(System.IO.Path.GetFileName(name), name, StringComparison.Ordinal))
                played.Add(name);
    }

    public void Record(string playlist, string name)
    {
        if (Playlist != playlist) Reset(playlist);
        if (name.Length > 0) played.Add(name);
    }

    public string? Next(string playlist, string current, string[] names, Random random)
    {
        if (Playlist != playlist) Reset(playlist);
        string[] available = names.Distinct(StringComparer.Ordinal).ToArray();
        if (available.Length == 0) return null;
        var present = new HashSet<string>(available, StringComparer.Ordinal);
        played.RemoveWhere(name => !present.Contains(name));
        if (present.Contains(current)) played.Add(current);
        string[] remaining = available.Where(name => !played.Contains(name)).ToArray();
        if (remaining.Length == 0)
        {
            played.Clear();
            if (available.Length > 1 && present.Contains(current)) played.Add(current);
            remaining = available.Where(name => !played.Contains(name)).ToArray();
        }
        string next = remaining[random.Next(remaining.Length)];
        played.Add(next);
        return next;
    }
}
