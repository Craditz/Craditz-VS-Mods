using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace CustomMusicRadio;

public sealed partial class CustomMusicRadioSystem
{
    public readonly HashSet<BlockPos> RemovingRadios = new();
    private readonly Dictionary<string, AssetLocation> radioAssets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> radioPending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RadioAudioRequest> radioRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<BlockPos, RadioVoice> radioVoices = new();
    private readonly Dictionary<string, long> serverRequestTimes = new();
    private readonly Dictionary<string, ServerTrack> serverTracks = new();
    private readonly Dictionary<string, FileStamp> invalidTracks = new(StringComparer.Ordinal);
    private readonly HashSet<string> loggedInvalidTracks = new(StringComparer.OrdinalIgnoreCase);
    private RadioServerConfig serverConfig = new();
    private volatile bool radioDisposed;
    public float ServerRangeCap { get; private set; } = RadioControls.MaxListeningRange;

    private void LoadRadioConfig(ICoreServerAPI api)
    {
        const string file = "custommusicradio.json";
        RadioServerConfig config;
        try { config = api.LoadModConfig<RadioServerConfig>(file) ?? new RadioServerConfig(); }
        catch (Exception e)
        {
            api.Logger.Warning("[custommusicradio] Cannot read {0}; using range cap 256: {1}", file, e.Message);
            return; // Preserve invalid owner input for repair.
        }
        ServerRangeCap = config.MaxListeningRange = RadioControls.ClampCap(config.MaxListeningRange);
        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (config.PlayerPlaylistOwnerUids != null)
            foreach (var binding in config.PlayerPlaylistOwnerUids)
                if (RadioPlaylists.IsSafeFolder(binding.Key) && !string.IsNullOrWhiteSpace(binding.Value))
                    bindings.TryAdd(binding.Key, binding.Value);
        config.PlayerPlaylistOwnerUids = bindings;
        serverConfig = config;
        api.StoreModConfig(config, file);
    }

    public string EmptyLibraryMessage() => "No valid Ogg Vorbis .ogg tracks found in the server CustomMusicRadio/music folder. Invalid/corrupt .ogg files are skipped; MP3, WAV and Ogg Opus are unsupported.";
    public string EmptyLibraryMessage(string playlist) => playlist.Length == 0 ? EmptyLibraryMessage()
        : "No valid Ogg Vorbis .ogg tracks found in playlist " + playlist + ".";
    public double RadioTrackDuration(string playlist, string name) => serverTracks[TrackKey(playlist, name)].Duration;
    public double RadioTrackDuration(string name) => RadioTrackDuration(RadioPlaylists.Base, name);

    private static string TransferKey(string hash, bool radio) => (radio ? "radio:" : "probe:") + hash;
    private static bool ValidHash(string hash) => hash != null && hash.Length == 64
        && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string TrackKey(string playlist, string name) => playlist.Length == 0 ? name : playlist + "/" + name;

    private string? PlaylistDirectory(string playlist)
    {
        if (playlist.Length == 0) return ServerMusicFolder;
        if (!RadioPlaylists.IsSafeFolder(playlist)) return null;
        string path = Path.Combine(ServerMusicFolder, playlist);
        try
        {
            var info = new DirectoryInfo(path);
            return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0 ? path : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private string? PersonalPlaylistOwner(string playlist)
    {
        if (playlist.Length == 0 || serverApi == null) return null;
        if (serverConfig.PlayerPlaylistOwnerUids.TryGetValue(playlist, out string? savedUid)) return savedUid;
        var players = serverApi.PlayerData.PlayerDataByUid.Values;
        var player = players.FirstOrDefault(data => string.Equals(data.PlayerUID, playlist, StringComparison.Ordinal)
                || string.Equals(RadioPlaylists.UidFolderName(data.PlayerUID), playlist, StringComparison.Ordinal))
            ?? players.FirstOrDefault(data => string.Equals(data.LastKnownPlayername, playlist, StringComparison.OrdinalIgnoreCase));
        if (player == null) return null;
        serverConfig.PlayerPlaylistOwnerUids[playlist] = player.PlayerUID;
        try { serverApi.StoreModConfig(serverConfig, "custommusicradio.json"); }
        catch (Exception e) { serverApi.Logger.Warning("[custommusicradio] Could not persist playlist owner for {0}: {1}", playlist, e.Message); }
        return player.PlayerUID;
    }

    public bool CanUsePlaylist(string playlist, string radioOwnerUid)
        => PlaylistDirectory(playlist) != null && RadioPlaylists.CanSelect(PersonalPlaylistOwner(playlist), radioOwnerUid);

    public string[] PlaylistNames(string radioOwnerUid)
    {
        var names = new List<string>();
        if (TrackNames().Length > 0) names.Add(RadioPlaylists.Base);
        try
        {
            foreach (string path in Directory.EnumerateDirectories(ServerMusicFolder, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(path);
                if (CanUsePlaylist(name, radioOwnerUid) && TrackNames(name).Length > 0) names.Add(name);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { serverApi?.Logger.Warning("[custommusicradio] Cannot enumerate playlists: {0}", e.Message); }
        var folders = names.Where(n => n.Length > 0).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n, StringComparer.Ordinal);
        return names.Contains(RadioPlaylists.Base) ? folders.Prepend(RadioPlaylists.Base).ToArray() : folders.ToArray();
    }

    public string[] TrackNames(string playlist = RadioPlaylists.Base)
    {
        string? directory = PlaylistDirectory(playlist);
        if (directory == null) return Array.Empty<string>();
        try
        {
            var names = new List<string>();
            foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(p => string.Equals(Path.GetExtension(p), ".ogg", StringComparison.OrdinalIgnoreCase)))
            {
                string? name = Path.GetFileName(path);
                if (name == null) continue;
                string key = TrackKey(playlist, name);
                FileInfo info = new(path);
                FileStamp stamp = new(info.Length, info.LastWriteTimeUtc);
                string? error = (info.Attributes & FileAttributes.ReparsePoint) != 0 ? "Linked files are not supported."
                    : stamp.Length <= 0 || stamp.Length > MaxProbeBytes ? "Track must be between 1 byte and 128 MiB."
                    : null;
                if (error == null && invalidTracks.TryGetValue(key, out FileStamp invalid) && invalid == stamp)
                    continue;
                if (error == null)
                {
                    names.Add(name);
                    invalidTracks.Remove(key); // Changed content gets another validation attempt on Play.
                    loggedInvalidTracks.Remove(key);
                }
                else if (loggedInvalidTracks.Add(key))
                {
                    serverApi?.Logger.Warning("[custommusicradio] Skipping unusable track {0}: {1}", key, error);
                }
            }

            return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ThenBy(n => n, StringComparer.Ordinal).ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            serverApi?.Logger.Warning("[custommusicradio] Cannot enumerate music folder: {0}", e.Message);
            return Array.Empty<string>();
        }
    }

    public bool PrepareRadioTrack(string selected, out string name, out string hash, out string error)
        => PrepareRadioTrack(RadioPlaylists.Base, selected, out name, out hash, out error);

    public bool PrepareRadioTrack(string playlist, string selected, out string name, out string hash, out string error)
    {
        name = selected;
        hash = "";
        error = "";
        if (selected.Length > 0 && !string.Equals(Path.GetExtension(selected), ".ogg", StringComparison.OrdinalIgnoreCase))
        { error = "Unsupported input: only Ogg Vorbis .ogg files are supported; no MP3/WAV conversion is provided."; return false; }
        string[] names = TrackNames(playlist);
        if (name.Length == 0) name = names.FirstOrDefault() ?? "";
        if (name.Length == 0 || !names.Contains(name, StringComparer.Ordinal))
        { error = name.Length == 0 ? EmptyLibraryMessage(playlist) : "The selected track is unavailable or is not a valid Ogg Vorbis file: " + name; return false; }
        if (!TryPrepareServerTrack(playlist, name, out ServerTrack? track, out error))
        {
            error = "Cannot play " + name + ": " + error;
            serverApi?.Logger.Warning("[custommusicradio] {0}", error);
            return false;
        }

        hash = track!.Hash;
        return true;
    }

    private bool TryPrepareServerTrack(string playlist, string name, out ServerTrack? track, out string error)
    {
        track = null;
        error = "";
        try
        {
            if (name.Length == 0 || !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal))
            { error = "The track filename is invalid."; return false; }

            string? directory = PlaylistDirectory(playlist);
            if (directory == null) { error = "The playlist folder is invalid or missing."; return false; }
            string path = Path.Combine(directory, name);
            FileInfo info = new(path);
            if (!info.Exists)
            { error = "The file is missing."; return false; }
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            { error = "Linked files are not supported."; return false; }
            if (info.Length <= 0 || info.Length > MaxProbeBytes)
            { error = "Track must be between 1 byte and 128 MiB."; return false; }
            string key = TrackKey(playlist, name);
            if (serverTracks.TryGetValue(key, out track)
                && track.Length == info.Length && track.Modified == info.LastWriteTimeUtc)
            {
                track.LastUse = ++trackUseSequence;
                return true;
            }

            using FileStream stream = File.OpenRead(path);
            if (stream.Length <= 0 || stream.Length > MaxProbeBytes) throw new InvalidDataException("Track size changed.");
            byte[] bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            double duration = OggVorbisInfo.Duration(bytes);
            if (!double.IsFinite(duration) || duration <= 0) throw new InvalidDataException("Ogg Vorbis duration is invalid.");

            track = new ServerTrack(bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), info.LastWriteTimeUtc, duration);
            // Bound retained compressed data; older radios can re-read their selected files.
            CacheServerTrack(key, track);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            serverTracks.Remove(TrackKey(playlist, name));
            try
            {
                string? directory = PlaylistDirectory(playlist);
                if (directory != null)
                {
                    FileInfo failed = new(Path.Combine(directory, name));
                    if (failed.Exists)
                        invalidTracks[TrackKey(playlist, name)] = new FileStamp(failed.Length, failed.LastWriteTimeUtc);
                }
            }
            catch (Exception) { /* Report the original validation failure. */ }
            error = e.Message;
            return false;
        }
    }

    private void OnRadioAudioRequest(IServerPlayer player, RadioAudioRequest request)
    {
        if (serverApi == null || serverChannel == null || !ValidHash(request.Hash)) return;
        BlockPos pos = new(request.X, request.Y, request.Z, request.Dimension);
        if (serverApi.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityVintageRadio radio
            || radio.Upper || !radio.Playing || radio.Hash != request.Hash
            || radio.PlaybackGeneration != request.Generation
            || !CanUsePlaylist(radio.Playlist, radio.OwnerUid)
            || !IsWithinRadioRange(radio, player.Entity.Pos, RadioControls.RequestTolerance)) return;
        long now = serverApi.World.ElapsedMilliseconds;
        // A client needs only one transfer per hash even when multiple radios share it.
        string key = player.PlayerUID + ":" + request.Hash + ":" + request.Generation;
        if (serverRequestTimes.TryGetValue(key, out long last) && now - last < 5000) return;
        foreach (string old in serverRequestTimes.Where(p => now - p.Value > 60000).Select(p => p.Key).ToArray())
            serverRequestTimes.Remove(old);
        serverRequestTimes[key] = now;
        RequestPreparedTrack(radio.Playlist, radio.Track, (track, error) =>
        {
            if (!IsCurrentTransfer(player, request, radio)) return;
            if (track == null || track.Hash != request.Hash)
            {
                string message = error.Length > 0 ? error : "Track changed on the server; pause and press Play to reload it.";
                serverChannel?.SendPacket(new RadioFailure { Hash = request.Hash, RequestId = request.RequestId, Message = message }, player);
                return;
            }
            QueueRadioTransfer(player, request, radio, radio.Track, track);
        });
    }

    // Shared distance policy; server requests retain the existing four-block movement tolerance.
    private static bool IsWithinRadioRange(BlockEntityVintageRadio radio, EntityPos listener, float tolerance = 0)
    {
        double range = radio.ListeningRange + tolerance;
        return listener.Dimension == radio.Pos.dimension
            && listener.XYZ.SquareDistanceTo(radio.Pos.X + .5, radio.Pos.Y + 1.4, radio.Pos.Z + .5) <= range * range;
    }

    public void UpdateRadioAudio(BlockEntityVintageRadio radio)
    {
        if (clientApi == null || radioDisposed) return;
        var player = clientApi.World.Player?.Entity;
        bool near = player != null && IsWithinRadioRange(radio, player.Pos);
        foreach (var pending in radioRequests.Values.Where(r => r.X == radio.Pos.X && r.Y == radio.Pos.Y && r.Z == radio.Pos.Z
            && r.Dimension == radio.Pos.dimension && (!near || !radio.Playing || r.Hash != radio.Hash
                || r.Generation != radio.PlaybackGeneration || !ReferenceEquals(r.LocalRadio, radio))).ToArray())
        {
            radioRequests.Remove(pending.Hash);
            radioPending.Remove(pending.Hash);
            clientTransfers.TryRemove(TransferKey(pending.Hash, true), out _);
        }
        if (!near || !ValidHash(radio.Hash)) { RemoveRadioAudio(radio.Pos); return; }
        if (radioVoices.TryGetValue(radio.Pos, out RadioVoice? voice)
            && (voice.Hash != radio.Hash || voice.Range != radio.ListeningRange || voice.Generation != radio.PlaybackGeneration || voice.Sound.IsDisposed))
        { RemoveRadioAudio(radio.Pos); voice = null; }
        if (!radio.Playing)
        {
            if (voice != null) { voice.Sound.Pause(); voice.Sound.SetVolume(radio.Volume); }
            return;
        }
        Vec3f cabinet = new(radio.Pos.X + .5f, radio.Pos.InternalY + 1.4f, radio.Pos.Z + .5f);
        Vec3f listener = new((float)(player!.Pos.X + player.LocalEyePos.X),
            (float)(player.Pos.InternalY + player.LocalEyePos.Y), (float)(player.Pos.Z + player.LocalEyePos.Z));
        Vec3f audioPosition = RadioControls.AudioPosition(cabinet, listener);
        if (voice == null)
        {
            if (!radioAssets.TryGetValue(radio.Hash, out AssetLocation? location))
            { RequestRadioAudio(radio); return; }
            try
            {
                ILoadedSound? sound = clientApi.World.LoadSound(new SoundParams(location)
                {
                    RelativePosition = false,
                    Position = audioPosition,
                    Range = radio.ListeningRange, ReferenceDistance = RadioControls.ReferenceDistance, Volume = radio.Volume,
                    ShouldLoop = radio.Mode == RadioPlaybackMode.RepeatCurrent, DisposeOnFinish = false, SoundType = EnumSoundType.Sound
                });
                if (sound == null) throw new InvalidDataException("World.LoadSound returned no radio sound.");
                voice = new RadioVoice(radio.Hash, radio.ListeningRange, radio.PlaybackGeneration, sound);
                radioVoices[radio.Pos.Copy()] = voice;
            }
            catch (Exception e)
            {
                clientApi.Logger.Error("[custommusicradio] Radio sound failed: {0}", e.Message);
                clientApi.ShowChatMessage("[Radio] Cannot play audio: " + e.Message);
                SendProbeAck(false, radio.Hash, "Radio playback: " + e.Message);
                // Avoid retrying every tick after a backend failure.
                radioAssets.Remove(radio.Hash);
                radioPending[radio.Hash] = clientApi.World.ElapsedMilliseconds;
                return;
            }
        }
        voice.Sound.SetPosition(audioPosition);
        float length = voice.Sound.SoundLengthSeconds;
        if (radio.Mode != RadioPlaybackMode.RepeatCurrent && length > 0 && radio.PlaybackSeconds >= length)
        { voice.Sound.Pause(); return; } // Wait for the authoritative next track, never restart an ended source.
        float target = length > 0 ? (float)(radio.PlaybackSeconds % length) : 0;
        // Apply normal 0..1 gain, including changes from the physical +/- buttons.
        voice.Sound.SetVolume(radio.Volume);
        bool starting = !voice.Sound.IsPlaying;
        float drift = Math.Abs(voice.Sound.PlaybackPosition - target);
        if (length > 0) drift = Math.Min(drift, Math.Abs(length - drift));
        if (starting || drift > 2)
            voice.Sound.PlaybackPosition = target;
        if (starting) voice.Sound.Start();
    }

    private void RequestRadioAudio(BlockEntityVintageRadio radio)
    {
        if (clientApi == null) return;
        long now = clientApi.World.ElapsedMilliseconds;
        string hash = radio.Hash;
        if (radioRequests.TryGetValue(hash, out RadioAudioRequest? previous) && !HasActiveRadioRequest(previous))
        { radioRequests.Remove(hash); radioPending.Remove(hash); clientTransfers.TryRemove(TransferKey(hash, true), out _); }
        if (radioPending.TryGetValue(hash, out long since))
        {
            if (now - since < 60000) return;
            if (radioRequests.ContainsKey(hash)) ReportRadioFailure(hash, "Audio transfer timed out; retrying. Check the server file and connection.");
        }
        radioPending[hash] = now;
        var request = new RadioAudioRequest
        { X = radio.Pos.X, Y = radio.Pos.Y, Z = radio.Pos.Z, Dimension = radio.Pos.dimension, Hash = hash,
          Generation = radio.PlaybackGeneration, RequestId = Guid.NewGuid().ToString("N"), LocalRadio = radio };
        radioRequests[hash] = request;
        // Cache hash-check and disk read run off-thread. Main-thread completion never resurrects
        // a deleted/paused radio: the next live block-entity tick is the only playback entry point.
        _ = Task.Run(() =>
        {
            PreparedTransfer? cached = null;
            try
            {
                string path = Path.Combine(ClientAssetRoot, ClientAssetDomain, "sounds", "track-" + hash + ".ogg");
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 0 && info.Length <= MaxProbeBytes)
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() == hash)
                        cached = new PreparedTransfer(bytes, hash, path);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            clientApi?.Event.EnqueueMainThreadTask(() =>
            {
                if (radioDisposed || clientApi == null) return;
                if (!IsCurrentRadioRequest(hash, request.RequestId)) return;
                if (!HasActiveRadioRequest(request)) { radioRequests.Remove(hash); radioPending.Remove(hash); return; }
                if (cached == null)
                {
                    if (!acceptServerAudioFiles)
                    {
                        radioRequests.Remove(hash);
                        if (refusedAudioHashes.Add(hash))
                            clientApi.ShowChatMessage("[Radio] Server audio downloads are disabled; this track is not in the verified cache.");
                        return;
                    }
                    try
                    {
                        if (clientChannel == null) throw new InvalidOperationException("The server audio channel is unavailable.");
                        clientChannel.SendPacket(request);
                    }
                    catch (Exception e)
                    {
                        OnRadioFailure(new RadioFailure { Hash = hash, RequestId = request.RequestId,
                            Message = "Cannot request audio from the server: " + e.Message });
                    }
                }
                else RegisterRadioAsset(cached);
            }, "radio-cache-lookup");
        });
    }

    private bool HasActiveRadioRequest(RadioAudioRequest request)
    {
        if (clientApi == null) return false;
        BlockPos pos = new(request.X, request.Y, request.Z, request.Dimension);
        var player = clientApi.World.Player?.Entity;
        return player != null
            && clientApi.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityVintageRadio { Playing: true, Upper: false } radio
            && radio.Hash == request.Hash && radio.PlaybackGeneration == request.Generation
            && (request.LocalRadio == null || ReferenceEquals(radio, request.LocalRadio)) && IsWithinRadioRange(radio, player.Pos);
    }

    private bool IsCurrentRadioRequest(string hash, string id) => ValidHash(hash) && radioRequests.TryGetValue(hash, out var request) && request.RequestId == id;
    private bool AcceptRadioTransfer(string hash, string id) => IsCurrentRadioRequest(hash, id) && HasActiveRadioRequest(radioRequests[hash]);

    private void ReportRadioFailure(string hash, string message)
    {
        clientApi?.Logger.Error("[custommusicradio] Radio {0}: {1}", hash, message);
        clientApi?.ShowChatMessage("[Radio] " + message);
        try { SendProbeAck(false, hash, "Radio: " + message); }
        catch (Exception e) { clientApi?.Logger.Warning("[custommusicradio] Cannot send radio failure acknowledgement: {0}", e.Message); }
    }

    private void OnRadioFailure(RadioFailure failure)
    {
        if (!AcceptRadioTransfer(failure.Hash, failure.RequestId)) return;
        ReportRadioFailure(failure.Hash, failure.Message);
        radioRequests.Remove(failure.Hash);
        clientTransfers.TryRemove(TransferKey(failure.Hash, true), out _);
        // Keep the 60-second retry backoff to avoid flooding chat/server logs.
    }

    private void CompleteRadioTransfer(ClientTransfer transfer, PreparedTransfer? prepared, Exception? error)
    {
        string hash = transfer.Metadata.Sha256;
        if (!AcceptRadioTransfer(hash, transfer.Metadata.RequestId)) return;
        clientTransfers.TryRemove(TransferKey(hash, true), out _);
        if (error != null || prepared == null)
        {
            ReportRadioFailure(hash, "Audio transfer failed: " + (error?.Message ?? "No audio data received."));
            radioRequests.Remove(hash);
            return;
        }
        RegisterRadioAsset(prepared);
    }

    private void RegisterRadioAsset(PreparedTransfer prepared)
    {
        if (clientApi == null || radioAssets.ContainsKey(prepared.Sha256)) return;
        try
        {
            // Keep the probe's original stereo asset and decoded source unchanged.
            AssetLocation location = new(ClientAssetDomain, "sounds/radio-" + prepared.Sha256 + ".ogg");
            Asset asset = new(prepared.Bytes, location, null);
            clientApi.Assets.Add(location, asset);
            if (ScreenManager.LoadSound(asset) is not AudioMetaData decoded || !decoded.Load())
                throw new InvalidDataException("Radio audio catalog registration failed.");
            if (decoded.BitsPerSample != 16 || decoded.Channels < 1 || decoded.Channels > 2)
                throw new InvalidDataException("Radio requires mono or stereo 16-bit decoded Ogg audio.");
            if (decoded.Channels == 2)
            {
                byte[] mono = new byte[decoded.Pcm.Length / 2];
                for (int input = 0, output = 0; input + 3 < decoded.Pcm.Length; input += 4, output += 2)
                {
                    int left = BinaryPrimitives.ReadInt16LittleEndian(decoded.Pcm.AsSpan(input, 2));
                    int right = BinaryPrimitives.ReadInt16LittleEndian(decoded.Pcm.AsSpan(input + 2, 2));
                    BinaryPrimitives.WriteInt16LittleEndian(mono.AsSpan(output, 2), (short)((left + right) / 2));
                }
                decoded.Pcm = mono;
                decoded.Channels = 1;
            }
            // Engine sound instances reuse this decoded catalog entry; each radio has its own source.
            radioAssets[prepared.Sha256] = location;
            radioPending.Remove(prepared.Sha256);
            radioRequests.Remove(prepared.Sha256);
        }
        catch (Exception e)
        {
            clientApi.Logger.Error("[custommusicradio] Radio preparation failed: {0}", e);
            ReportRadioFailure(prepared.Sha256, "Cannot decode Ogg Vorbis audio: " + e.Message);
            radioRequests.Remove(prepared.Sha256);
        }
    }

    public void RemoveRadioAudio(BlockPos pos)
    {
        foreach (var request in radioRequests.Values.Where(r => r.X == pos.X && r.Y == pos.Y && r.Z == pos.Z && r.Dimension == pos.dimension).ToArray())
        {
            radioRequests.Remove(request.Hash);
            radioPending.Remove(request.Hash);
            clientTransfers.TryRemove(TransferKey(request.Hash, true), out _);
        }
        if (radioVoices.Remove(pos, out RadioVoice? voice))
        {
            if (!voice.Sound.IsDisposed) { voice.Sound.Stop(); voice.Sound.Dispose(); }
        }
    }

    private void DisposeRadioAudio()
    {
        radioDisposed = true;
        if (serverAudioTickId != 0) serverApi?.Event.UnregisterGameTickListener(serverAudioTickId);
        serverAudioTickId = 0;
        preparationQueue.Clear();
        pendingPreparations.Clear();
        radioTransfers.Clear();
        queuedTransferBytes = 0;
        foreach (BlockPos pos in radioVoices.Keys.ToArray())
        {
            try { RemoveRadioAudio(pos); } catch { /* Audio backend may already be shutting down. */ }
        }
        radioVoices.Clear();
        radioAssets.Clear();
        radioPending.Clear();
        radioRequests.Clear();
        clientTransfers.Clear();
        serverTracks.Clear();
        serverRequestTimes.Clear();
    }

    [ProtoContract]
    private sealed class RadioAudioRequest : INetworkMessage
    {
        [ProtoMember(1)] public int X { get; set; }
        [ProtoMember(2)] public int Y { get; set; }
        [ProtoMember(3)] public int Z { get; set; }
        [ProtoMember(4)] public int Dimension { get; set; }
        [ProtoMember(5)] public string Hash { get; set; } = "";
        [ProtoMember(6)] public string Generation { get; set; } = "";
        [ProtoMember(7)] public string RequestId { get; set; } = "";
        [ProtoIgnore] public BlockEntityVintageRadio? LocalRadio { get; set; }
    }

    [ProtoContract]
    private sealed class RadioFailure : INetworkMessage
    {
        [ProtoMember(1)] public string Hash { get; set; } = "";
        [ProtoMember(2)] public string RequestId { get; set; } = "";
        [ProtoMember(3)] public string Message { get; set; } = "";
    }

    private sealed record ServerTrack(byte[] Bytes, string Hash, DateTime Modified, double Duration)
    { public int Length => Bytes.Length; public long LastUse { get; set; } }
    private readonly record struct FileStamp(long Length, DateTime Modified);
    private sealed class RadioVoice(string hash, float range, string generation, ILoadedSound sound)
    {
        public string Hash { get; } = hash;
        public float Range { get; } = range;
        public string Generation { get; } = generation;
        public ILoadedSound Sound { get; } = sound;
    }
}
