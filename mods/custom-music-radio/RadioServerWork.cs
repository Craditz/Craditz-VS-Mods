using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CustomMusicRadio;

public sealed partial class CustomMusicRadioSystem
{
    private const int MaxPreparationQueue = 8;
    private const int MaxPreparationWaiters = 32;
    private const int MaxRadioTransfers = 16;
    private const int TransferChunksPerTick = 8;
    private readonly Queue<TrackPreparation> preparationQueue = new();
    private readonly Dictionary<string, TrackPreparation> pendingPreparations = new(StringComparer.Ordinal);
    private readonly List<RadioTransfer> radioTransfers = new();
    private bool preparationRunning;
    private long serverAudioTickId;
    private long trackUseSequence;
    private long queuedTransferBytes;
    private int transferCursor;

    public void PrepareRadioTrackAsync(string playlist, string selected, Action<string, string, double, string> completed)
    {
        if (selected.Length > 0 && !string.Equals(Path.GetExtension(selected), ".ogg", StringComparison.OrdinalIgnoreCase))
        {
            completed(selected, "", 0, "Unsupported input: only Ogg Vorbis .ogg files are supported; no MP3/WAV conversion is provided.");
            return;
        }
        string[] names = TrackNames(playlist);
        string name = selected.Length == 0 ? names.FirstOrDefault() ?? "" : selected;
        if (name.Length == 0 || !names.Contains(name, StringComparer.Ordinal))
        {
            completed(name, "", 0, name.Length == 0 ? EmptyLibraryMessage(playlist) : "The selected track is unavailable: " + name);
            return;
        }
        RequestPreparedTrack(playlist, name, (track, error) =>
            completed(name, track?.Hash ?? "", track?.Duration ?? 0, error));
    }

    private void RequestPreparedTrack(string playlist, string name, Action<ServerTrack?, string> completed)
    {
        if (radioDisposed || serverApi == null) return;
        string key = TrackKey(playlist, name);
        try
        {
            if (name.Length == 0 || Path.GetFileName(name) != name ||
                !string.Equals(Path.GetExtension(name), ".ogg", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The track filename is invalid.");
            string? directory = PlaylistDirectory(playlist);
            if (directory == null) throw new InvalidDataException("The playlist folder is invalid or missing.");
            string path = Path.Combine(directory, name);
            FileInfo info = new(path);
            ValidateTrackFile(info);
            if (serverTracks.TryGetValue(key, out ServerTrack? cached) &&
                cached.Length == info.Length && cached.Modified == info.LastWriteTimeUtc)
            {
                cached.LastUse = ++trackUseSequence;
                completed(cached, "");
                return;
            }
            if (pendingPreparations.TryGetValue(key, out TrackPreparation? pending))
            {
                if (pending.Completed.Count >= MaxPreparationWaiters)
                    completed(null, "Music preparation is busy; try again shortly.");
                else pending.Completed.Add(completed);
                return;
            }
            if (pendingPreparations.Count >= MaxPreparationQueue)
            {
                completed(null, "Music preparation is busy; try again shortly.");
                return;
            }
            TrackPreparation preparation = new(key, path);
            preparation.Completed.Add(completed);
            pendingPreparations.Add(key, preparation);
            preparationQueue.Enqueue(preparation);
            StartNextPreparation();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            completed(null, e.Message);
        }
    }

    private void StartNextPreparation()
    {
        if (radioDisposed || preparationRunning || preparationQueue.Count == 0 || serverApi == null) return;
        preparationRunning = true;
        TrackPreparation preparation = preparationQueue.Dequeue();
        ICoreServerAPI api = serverApi;
        // Only file I/O, Ogg metadata parsing and hashing run on the worker.
        _ = Task.Run(() =>
        {
            ServerTrack? track = null;
            string error = "";
            FileStamp? inputStamp = null;
            bool invalidContent = false;
            try
            {
                FileInfo input = new(preparation.Path);
                if (input.Exists) inputStamp = new(input.Length, input.LastWriteTimeUtc);
                track = ReadServerTrack(preparation.Path);
            }
            catch (Exception e) { error = e.Message; invalidContent = e is InvalidDataException; }
            if (radioDisposed) return;
            api.Event.EnqueueMainThreadTask(() =>
            {
                if (radioDisposed || !ReferenceEquals(serverApi, api)) return;
                preparationRunning = false;
                pendingPreparations.Remove(preparation.Key);
                if (track != null)
                {
                    CacheServerTrack(preparation.Key, track);
                    invalidTracks.Remove(preparation.Key);
                }
                else
                {
                    serverTracks.Remove(preparation.Key);
                    try
                    {
                        FileInfo failed = new(preparation.Path);
                        // A failed read must not blacklist replacement content or a
                        // temporarily locked file. Only remember unchanged invalid data.
                        if (invalidContent && failed.Exists && inputStamp is FileStamp original
                            && original == new FileStamp(failed.Length, failed.LastWriteTimeUtc))
                            invalidTracks[preparation.Key] = original;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                foreach (Action<ServerTrack?, string> callback in preparation.Completed)
                {
                    try { callback(track, error); }
                    catch (Exception e) { api.Logger.Warning("[custommusicradio] Preparation callback failed: {0}", e.Message); }
                }
                StartNextPreparation();
            }, "custommusicradio-prepare");
        });
    }

    private static void ValidateTrackFile(FileInfo info)
    {
        if (!info.Exists) throw new FileNotFoundException("The file is missing.");
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked files are not supported.");
        if (info.Length <= 0 || info.Length > MaxProbeBytes) throw new InvalidDataException("Track must be between 1 byte and 128 MiB.");
    }

    private static ServerTrack ReadServerTrack(string path)
    {
        FileInfo info = new(path);
        ValidateTrackFile(info);
        long length = info.Length;
        DateTime modified = info.LastWriteTimeUtc;
        using FileStream stream = File.OpenRead(path);
        if (stream.Length != length) throw new InvalidDataException("Track size changed.");
        byte[] bytes = new byte[(int)length];
        stream.ReadExactly(bytes);
        double duration = OggVorbisInfo.Duration(bytes);
        if (!double.IsFinite(duration) || duration <= 0) throw new InvalidDataException("Ogg Vorbis duration is invalid.");
        info.Refresh();
        if (!info.Exists || info.Length != length || info.LastWriteTimeUtc != modified)
            throw new InvalidDataException("Track changed while being prepared; try again.");
        return new ServerTrack(bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), modified, duration);
    }

    private void CacheServerTrack(string key, ServerTrack track)
    {
        serverTracks.Remove(key);
        while (serverTracks.Count >= 4 || serverTracks.Values.Sum(value => (long)value.Length) + track.Length > MaxProbeBytes)
        {
            if (serverTracks.Count == 0) break;
            serverTracks.Remove(serverTracks.MinBy(pair => pair.Value.LastUse).Key);
        }
        track.LastUse = ++trackUseSequence;
        serverTracks[key] = track;
    }

    private bool IsCurrentTransfer(IServerPlayer player, RadioAudioRequest request, BlockEntityVintageRadio radio)
    {
        return !radioDisposed && player.Entity != null && serverApi?.World.PlayerByUid(player.PlayerUID) == player
            && ReferenceEquals(serverApi.World.BlockAccessor.GetBlockEntity(radio.Pos), radio)
            && !radio.Upper && radio.Playing && radio.Hash == request.Hash
            && radio.PlaybackGeneration == request.Generation
            && CanUsePlaylist(radio.Playlist, radio.OwnerUid)
            && IsWithinRadioRange(radio, player.Entity.Pos, RadioControls.RequestTolerance);
    }

    private void QueueRadioTransfer(IServerPlayer player, RadioAudioRequest request, BlockEntityVintageRadio radio, string name, ServerTrack track)
    {
        // Supersede retries for this listener/hash without retaining both byte arrays.
        for (int i = radioTransfers.Count - 1; i >= 0; i--)
        {
            if (radioTransfers[i].Player.PlayerUID != player.PlayerUID || radioTransfers[i].Request.Hash != request.Hash) continue;
            queuedTransferBytes -= radioTransfers[i].Track.Length;
            radioTransfers.RemoveAt(i);
        }
        if (radioTransfers.Count >= MaxRadioTransfers || queuedTransferBytes + track.Length > 2L * MaxProbeBytes)
        {
            serverChannel?.SendPacket(new RadioFailure { Hash = request.Hash, RequestId = request.RequestId,
                Message = "Music transfers are busy; retrying shortly." }, player);
            return;
        }
        RadioTransfer transfer = new(player, request, radio, track);
        radioTransfers.Add(transfer);
        queuedTransferBytes += track.Length;
        serverChannel?.SendPacket(new ProbeMetadata { FileName = name, Sha256 = track.Hash, ByteCount = track.Length,
            TotalChunks = transfer.TotalChunks, Radio = true, RequestId = request.RequestId }, player);
    }

    private void PumpRadioTransfers(float dt)
    {
        for (int work = 0; work < TransferChunksPerTick && radioTransfers.Count > 0; work++)
        {
            transferCursor %= radioTransfers.Count;
            RadioTransfer transfer = radioTransfers[transferCursor];
            if (IsCurrentTransfer(transfer.Player, transfer.Request, transfer.Radio))
            {
                int offset = transfer.NextChunk * ChunkSize;
                byte[] chunk = new byte[Math.Min(ChunkSize, transfer.Track.Length - offset)];
                Buffer.BlockCopy(transfer.Track.Bytes, offset, chunk, 0, chunk.Length);
                serverChannel?.SendPacket(new ProbeChunk { Sha256 = transfer.Track.Hash, Index = transfer.NextChunk,
                    TotalChunks = transfer.TotalChunks, Data = chunk, Radio = true, RequestId = transfer.Request.RequestId }, transfer.Player);
                transfer.NextChunk++;
                if (transfer.NextChunk < transfer.TotalChunks) { transferCursor++; continue; }
            }
            queuedTransferBytes -= transfer.Track.Length;
            radioTransfers.RemoveAt(transferCursor);
        }
    }

    private sealed class TrackPreparation(string key, string path)
    {
        public string Key { get; } = key;
        public string Path { get; } = path;
        public List<Action<ServerTrack?, string>> Completed { get; } = new();
    }

    private sealed class RadioTransfer(IServerPlayer player, RadioAudioRequest request, BlockEntityVintageRadio radio, ServerTrack track)
    {
        public IServerPlayer Player { get; } = player;
        public RadioAudioRequest Request { get; } = request;
        public BlockEntityVintageRadio Radio { get; } = radio;
        public ServerTrack Track { get; } = track;
        public int TotalChunks { get; } = (track.Length + ChunkSize - 1) / ChunkSize;
        public int NextChunk;
    }
}
