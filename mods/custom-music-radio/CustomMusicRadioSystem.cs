using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Client;
using Vintagestory.Common;

#nullable enable

namespace CustomMusicRadio;

public sealed partial class CustomMusicRadioSystem : ModSystem
{
    private const string ChannelName = "custommusicradio";
    private const string ClientAssetDomain = "custommusicradio";
    private const string BundledDefaultAssetPath = "sounds/that-flying-rag.ogg";
    private const string BundledDefaultFileName = "000-that-flying-rag.ogg";
    private const string BundledDefaultMarkerName = "default-track-seeded.marker";
    private const int ChunkSize = 32 * 1024;
    private const int MaxProbeBytes = 128 * 1024 * 1024;

    private ICoreServerAPI? serverApi;
    private ICoreClientAPI? clientApi;
    private IServerNetworkChannel? serverChannel;
    private IClientNetworkChannel? clientChannel;
    private bool acceptServerAudioFiles = true;
    private readonly HashSet<string> refusedAudioHashes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ClientTransfer> clientTransfers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ILoadedSound> loadedSounds = new(StringComparer.OrdinalIgnoreCase);
    private ILoadedSound? activeSound;

    private string ServerMusicFolder => Path.Combine(GamePaths.DataPath, "CustomMusicRadio", "music");
    private string ClientAssetPackageRoot => Path.Combine(GamePaths.Cache, "CustomMusicRadio");
    private string ClientAssetRoot => Path.Combine(ClientAssetPackageRoot, "assets");

    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockClass("VintageRadio", typeof(BlockVintageRadio));
        api.RegisterBlockEntityClass("VintageRadio", typeof(BlockEntityVintageRadio));
        if (api.Side == EnumAppSide.Server)
        {
            serverChannel = ((ICoreServerAPI)api).Network
                .RegisterChannel(ChannelName)
                .RegisterMessageType<ProbeRequest>()
                .RegisterMessageType<ProbeMetadata>()
                .RegisterMessageType<ProbeChunk>()
                .RegisterMessageType<ProbeAck>()
                .RegisterMessageType<RadioAudioRequest>()
                .RegisterMessageType<RadioFailure>();
        }
        else
        {
            clientChannel = ((ICoreClientAPI)api).Network
                .RegisterChannel(ChannelName)
                .RegisterMessageType<ProbeRequest>()
                .RegisterMessageType<ProbeMetadata>()
                .RegisterMessageType<ProbeChunk>()
                .RegisterMessageType<ProbeAck>()
                .RegisterMessageType<RadioAudioRequest>()
                .RegisterMessageType<RadioFailure>();
        }
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server) return;

        if (EnsureHostMusicFolder(api)) SeedBundledDefaultTrack((ICoreServerAPI)api);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        serverApi = api;
        LoadRadioConfig(api);
        serverAudioTickId = api.Event.RegisterGameTickListener(PumpRadioTransfers, 50);
        bool musicFolderReady = EnsureHostMusicFolder(api);
        serverChannel = api.Network.GetChannel(ChannelName);
        serverChannel
            .SetMessageHandler<ProbeRequest>(OnProbeRequest)
            .SetMessageHandler<ProbeAck>(OnProbeAck)
            .SetMessageHandler<RadioAudioRequest>(OnRadioAudioRequest);

        api.RegisterCommand(
            "cra-probe",
            "Send the first test Ogg file to yourself",
            "cra-probe",
            (player, groupId, args) => SendProbe(player, groupId),
            Privilege.chat
        );

        if (musicFolderReady) api.Logger.Notification("[custommusicradio] Server host music folder: {0}", ServerMusicFolder);
    }

    private bool EnsureHostMusicFolder(ICoreAPI api)
    {
        string? musicFolder = null;
        try
        {
            // GamePaths.DataPath is also ICoreAPI.DataBasePath in 1.22.7.
            // Single-player's embedded server shares the active client data path.
            // CreateDirectory is idempotent and leaves existing owner files intact.
            musicFolder = ServerMusicFolder;
            Directory.CreateDirectory(musicFolder);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException)
        {
            api.Logger.Error("[custommusicradio] Cannot create host music folder '{0}': {1}. Check the active dataPath, folder permissions, and whether a file blocks the directory.", musicFolder ?? GamePaths.DataPath, e.Message);
            return false;
        }
    }

    private void SeedBundledDefaultTrack(ICoreServerAPI api)
    {
        string markerPath = Path.Combine(Path.GetDirectoryName(ServerMusicFolder)!, BundledDefaultMarkerName);
        if (File.Exists(markerPath)) return;

        try
        {
            bool alreadyHasOwnerMusic = Directory.EnumerateFiles(ServerMusicFolder, "*", SearchOption.TopDirectoryOnly)
                .Any(path => string.Equals(Path.GetExtension(path), ".ogg", StringComparison.OrdinalIgnoreCase))
                || Directory.EnumerateDirectories(ServerMusicFolder, "*", SearchOption.TopDirectoryOnly)
                    .Where(path => (new DirectoryInfo(path).Attributes & FileAttributes.ReparsePoint) == 0)
                    .Any(path => Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)
                        .Any(file => string.Equals(Path.GetExtension(file), ".ogg", StringComparison.OrdinalIgnoreCase)));
            if (alreadyHasOwnerMusic)
            {
                File.WriteAllText(markerPath, "Owner music was already present; the bundled default was not seeded.\n");
                api.Logger.Notification("[custommusicradio] Existing owner music found; bundled default was not seeded.");
                return;
            }

            IAsset? asset = api.Assets.TryGet(new AssetLocation(ClientAssetDomain, BundledDefaultAssetPath));
            if (asset == null || asset.Data.Length == 0)
            {
                api.Logger.Error("[custommusicradio] Bundled default track asset is missing or empty.");
                return;
            }

            string destination = Path.Combine(ServerMusicFolder, BundledDefaultFileName);
            string temporary = destination + ".partial";
            File.WriteAllBytes(temporary, asset.Data);
            File.Move(temporary, destination);
            File.WriteAllText(markerPath, "Seeded the bundled default track once. Delete this marker only to allow a new first-run seed.\n");
            api.Logger.Notification("[custommusicradio] Seeded bundled default track {0} into {1}.", BundledDefaultFileName, ServerMusicFolder);
        }
        catch (Exception e)
        {
            try
            {
                string temporary = Path.Combine(ServerMusicFolder, BundledDefaultFileName + ".partial");
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch
            {
                // Preserve the original seed failure in the server log.
            }

            api.Logger.Error("[custommusicradio] Could not seed bundled default track: {0}", e);
        }
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        clientApi = api;
        // A remote listener needs only its cache. A single-player client is also
        // the local host, so create its owner music folder explicitly on load.
        // Seeding stays server-only to avoid two sides writing the same song.
        if (api.IsSinglePlayer && EnsureHostMusicFolder(api))
            api.Logger.Notification("[custommusicradio] Single-player host music folder: {0}", ServerMusicFolder);
        const string clientConfigFile = "custommusicradio-client.json";
        try
        {
            RadioClientConfig config = api.LoadModConfig<RadioClientConfig>(clientConfigFile) ?? new RadioClientConfig();
            acceptServerAudioFiles = config.AcceptServerAudioFiles;
            api.StoreModConfig(config, clientConfigFile);
        }
        catch (Exception e)
        {
            acceptServerAudioFiles = false;
            api.Logger.Warning("[custommusicradio] Cannot read client audio setting; refusing server audio files until repaired: {0}", e.Message);
        }
        Directory.CreateDirectory(Path.Combine(ClientAssetRoot, ClientAssetDomain, "sounds"));
        api.Assets.AddModOrigin(ClientAssetDomain, Path.Combine(ClientAssetRoot, ClientAssetDomain));

        clientChannel = api.Network.GetChannel(ChannelName);
        clientChannel
            .SetMessageHandler<ProbeMetadata>(OnProbeMetadata)
            .SetMessageHandler<ProbeChunk>(OnProbeChunk)
            .SetMessageHandler<RadioFailure>(OnRadioFailure);

        api.Logger.Notification("[custommusicradio] Client runtime asset root: {0}", ClientAssetRoot);
    }

    public override void Dispose()
    {
        DisposeRadioAudio();
        foreach (ILoadedSound sound in loadedSounds.Values)
        {
            try
            {
                if (!sound.IsDisposed) sound.Dispose();
            }
            catch
            {
                // The client audio system may already be shutting down.
            }
        }

        loadedSounds.Clear();
        activeSound = null;
        base.Dispose();
    }

    private void SendProbe(IServerPlayer player, int groupId)
    {
        if (serverApi == null || serverChannel == null) return;

        string? playlist = PlaylistNames(RadioPlaylists.Base).FirstOrDefault();
        string? fileName = playlist == null ? null : TrackNames(playlist).FirstOrDefault();
        string? file = fileName == null ? null : Path.Combine(ServerMusicFolder, playlist!, fileName);

        if (file == null)
        {
            player.SendMessage(groupId, "[custommusicradio] No public playlist has a valid Ogg Vorbis track for the probe.", EnumChatType.CommandError);
            return;
        }

        FileInfo info = new(file);
        if (info.Length > MaxProbeBytes)
        {
            player.SendMessage(groupId, "[custommusicradio] Test file is larger than 128 MiB.", EnumChatType.CommandError);
            return;
        }

        byte[] bytes = File.ReadAllBytes(file);
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string safeName = Path.GetFileName(file);
        int totalChunks = Math.Max(1, (bytes.Length + ChunkSize - 1) / ChunkSize);

        serverChannel.SendPacket(new ProbeMetadata
        {
            FileName = safeName,
            Sha256 = hash,
            ByteCount = bytes.Length,
            TotalChunks = totalChunks
        }, player);

        for (int index = 0; index < totalChunks; index++)
        {
            int offset = index * ChunkSize;
            int length = Math.Min(ChunkSize, bytes.Length - offset);
            byte[] chunk = new byte[length];
            Buffer.BlockCopy(bytes, offset, chunk, 0, length);

            serverChannel.SendPacket(new ProbeChunk
            {
                Sha256 = hash,
                Index = index,
                TotalChunks = totalChunks,
                Data = chunk
            }, player);
        }

        serverApi.Logger.Notification("[custommusicradio] Sent {0} ({1} bytes, {2} chunks) to {3}.", safeName, bytes.Length, totalChunks, player.PlayerName);
        player.SendMessage(groupId, "[custommusicradio] Sent " + safeName + ". Waiting for the client cache/playback result.", EnumChatType.Notification);
    }

    private void OnProbeRequest(IServerPlayer player, ProbeRequest request)
    {
        SendProbe(player, 0);
    }

    private void OnProbeAck(IServerPlayer player, ProbeAck ack)
    {
        serverApi?.Logger.Notification("[custommusicradio] Client result from {0}: success={1}, hash={2}, message={3}", player.PlayerName, ack.Success, ack.Sha256, ack.Message);
    }

    private void OnProbeMetadata(ProbeMetadata metadata)
    {
        if (!acceptServerAudioFiles)
        {
            if (!metadata.Radio) SendProbeAck(false, metadata.Sha256, "Client setting refuses server audio files.");
            return;
        }
        if (metadata.Radio && !AcceptRadioTransfer(metadata.Sha256, metadata.RequestId)) return;
        if (clientApi == null || !ValidHash(metadata.Sha256) || metadata.ByteCount <= 0 || metadata.ByteCount > MaxProbeBytes
            || metadata.TotalChunks != (metadata.ByteCount + ChunkSize - 1) / ChunkSize)
        {
            clientApi?.Logger.Error("[custommusicradio] Rejected invalid probe metadata.");
            if (metadata.Radio) OnRadioFailure(new RadioFailure { Hash = metadata.Sha256, RequestId = metadata.RequestId, Message = "Server supplied invalid audio transfer metadata." });
            return;
        }

        clientTransfers[TransferKey(metadata.Sha256, metadata.Radio)] = new ClientTransfer(metadata);
        clientApi.Logger.Notification("[custommusicradio] Receiving {0} ({1} bytes, {2} chunks, sha256 {3}).", metadata.FileName, metadata.ByteCount, metadata.TotalChunks, metadata.Sha256);
    }

    private void OnProbeChunk(ProbeChunk chunk)
    {
        if (!acceptServerAudioFiles) return;
        if (clientApi == null || !clientTransfers.TryGetValue(TransferKey(chunk.Sha256, chunk.Radio), out ClientTransfer? transfer)) return;
        if (chunk.Radio && (chunk.RequestId != transfer.Metadata.RequestId || !AcceptRadioTransfer(chunk.Sha256, chunk.RequestId))) return;
        if (chunk.Index < 0 || chunk.Index >= transfer.Chunks.Length || chunk.Data == null)
        {
            if (chunk.Radio) OnRadioFailure(new RadioFailure { Hash = chunk.Sha256, RequestId = chunk.RequestId, Message = "Audio transfer contained an invalid chunk." });
            return;
        }
        int expectedSize = Math.Min(ChunkSize, transfer.Metadata.ByteCount - chunk.Index * ChunkSize);
        if (chunk.TotalChunks != transfer.Chunks.Length || chunk.Data.Length != expectedSize)
        {
            if (chunk.Radio) OnRadioFailure(new RadioFailure { Hash = chunk.Sha256, RequestId = chunk.RequestId, Message = "Audio transfer chunk size/count mismatch." });
            return;
        }

        if (Interlocked.CompareExchange(ref transfer.Chunks[chunk.Index], chunk.Data, null) == null)
        {
            // Pacing can extend a large transfer; time out inactivity, not progress.
            if (chunk.Radio) radioPending[chunk.Sha256] = clientApi.World.ElapsedMilliseconds;
            int receivedChunks = Interlocked.Increment(ref transfer.ReceivedChunks);
            if (receivedChunks == transfer.Chunks.Length && Interlocked.Exchange(ref transfer.CompletionQueued, 1) == 0)
            {
                QueueTransferPreparation(transfer);
            }
        }
    }

    private void QueueTransferPreparation(ClientTransfer transfer)
    {
        _ = Task.Run(() =>
        {
            PreparedTransfer? prepared = null;
            Exception? preparationException = null;

            try
            {
                prepared = PrepareTransfer(transfer);
            }
            catch (Exception exception)
            {
                preparationException = exception;
            }

            clientApi?.Event.EnqueueMainThreadTask(
                () => CompleteTransfer(transfer, prepared, preparationException),
                "custommusicradio-complete"
            );
        });
    }

    private PreparedTransfer PrepareTransfer(ClientTransfer transfer)
    {
        byte[] bytes = new byte[transfer.Metadata.ByteCount];
        int offset = 0;
        foreach (byte[]? chunk in transfer.Chunks)
        {
            if (chunk == null) throw new InvalidDataException("A transfer chunk was missing.");
            Buffer.BlockCopy(chunk, 0, bytes, offset, chunk.Length);
            offset += chunk.Length;
        }

        if (offset != transfer.Metadata.ByteCount)
            throw new InvalidDataException("The transfer byte count did not match its metadata.");

        string actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actualHash, transfer.Metadata.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA-256 verification failed.");

        string fileName = "track-" + actualHash + ".ogg";
        string soundFolder = Path.Combine(ClientAssetRoot, ClientAssetDomain, "sounds");
        Directory.CreateDirectory(soundFolder);
        string finalPath = Path.Combine(soundFolder, fileName);
        string tempPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".partial";
        File.WriteAllBytes(tempPath, bytes);
        File.Move(tempPath, finalPath, true);

        return new PreparedTransfer(bytes, actualHash, finalPath);
    }

    private void CompleteTransfer(ClientTransfer transfer, PreparedTransfer? prepared, Exception? preparationException)
    {
        if (clientApi == null || radioDisposed) return;

        if (transfer.Metadata.Radio)
        {
            CompleteRadioTransfer(transfer, prepared, preparationException);
            return;
        }

        try
        {
            if (preparationException != null) throw preparationException;
            if (prepared == null) throw new InvalidDataException("The background transfer preparation returned no result.");

            AssetLocation location = new(ClientAssetDomain + ":sounds/track-" + prepared.Sha256 + ".ogg");
            bool reused = loadedSounds.TryGetValue(prepared.Sha256, out ILoadedSound? loadedSound)
                && loadedSound != null
                && !loadedSound.IsDisposed;

            if (!reused)
            {
                Asset runtimeAsset = new(prepared.Bytes, location, null);
                if (clientApi.Assets.TryGet(location, true) == null)
                    clientApi.Assets.Add(location, runtimeAsset);

                IAsset? registeredAsset = clientApi.Assets.TryGet(location, true);
                if (registeredAsset == null) throw new InvalidDataException("The runtime asset manager did not retain the added sound.");

                // World.LoadSound uses a separate startup catalog from IAssetManager.
                // Register the runtime asset in that catalog, then play it as a
                // listener-relative ordinary sound instead of going through the
                // global music engine and its fade/priority rules.
                if (ScreenManager.LoadSound(runtimeAsset) == null)
                    throw new InvalidDataException("The runtime sound catalog rejected the added asset.");

                SoundParams soundParams = new(location)
                {
                    RelativePosition = true,
                    Position = new Vec3f(),
                    DisposeOnFinish = false,
                    SoundType = EnumSoundType.Sound,
                    Volume = 1f,
                    Range = 32f
                };

                loadedSound = clientApi.World.LoadSound(soundParams);
                if (loadedSound == null) throw new InvalidDataException("World.LoadSound returned no sound.");
                loadedSounds[prepared.Sha256] = loadedSound;
            }

            if (activeSound != null && activeSound != loadedSound && !activeSound.IsDisposed)
                activeSound.Stop();

            ILoadedSound soundToPlay = loadedSound!;
            soundToPlay.Stop();
            soundToPlay.PlaybackPosition = 0;
            soundToPlay.SetVolume(1f);
            soundToPlay.Start();
            activeSound = soundToPlay;
            clientApi.Logger.Notification("[custommusicradio] {0} cached, registered, and played as listener-relative sound {1}; {2:0.00}s; ready={3}; playing={4}.", reused ? "Reused" : "Cached", prepared.FinalPath, soundToPlay.SoundLengthSeconds, soundToPlay.IsReady, soundToPlay.IsPlaying);
            clientApi.ShowChatMessage("[custommusicradio] Automatic transfer, runtime asset registration, and ordinary-sound playback succeeded.");
            SendProbeAck(true, prepared.Sha256, reused ? "reused decoded ordinary sound" : "cached, registered, and played as ordinary sound");
        }
        catch (Exception exception)
        {
            clientApi.Logger.Error("[custommusicradio] Automatic audio probe failed: {0}", exception);
            clientApi.ShowChatMessage("[custommusicradio] Audio probe failed; see client-main.log.");
            SendProbeAck(false, prepared?.Sha256 ?? transfer.Metadata.Sha256, exception.Message);
        }
        finally
        {
            clientTransfers.TryRemove(TransferKey(transfer.Metadata.Sha256, false), out _);
        }
    }

    private void SendProbeAck(bool success, string sha256, string message)
    {
        clientChannel?.SendPacket(new ProbeAck { Success = success, Sha256 = sha256, Message = message });
    }

    [ProtoContract]
    private sealed class ProbeRequest : INetworkMessage
    {
        [ProtoMember(1)] public string TrackId { get; set; } = "first";
    }

    [ProtoContract]
    private sealed class ProbeMetadata : INetworkMessage
    {
        [ProtoMember(1)] public string FileName { get; set; } = "";
        [ProtoMember(2)] public string Sha256 { get; set; } = "";
        [ProtoMember(3)] public int ByteCount { get; set; }
        [ProtoMember(4)] public int TotalChunks { get; set; }
        [ProtoMember(5)] public bool Radio { get; set; }
        [ProtoMember(6)] public string RequestId { get; set; } = "";
    }

    [ProtoContract]
    private sealed class ProbeChunk : INetworkMessage
    {
        [ProtoMember(1)] public string Sha256 { get; set; } = "";
        [ProtoMember(2)] public int Index { get; set; }
        [ProtoMember(3)] public int TotalChunks { get; set; }
        [ProtoMember(4)] public byte[] Data { get; set; } = Array.Empty<byte>();
        [ProtoMember(5)] public bool Radio { get; set; }
        [ProtoMember(6)] public string RequestId { get; set; } = "";
    }

    [ProtoContract]
    private sealed class ProbeAck : INetworkMessage
    {
        [ProtoMember(1)] public bool Success { get; set; }
        [ProtoMember(2)] public string Sha256 { get; set; } = "";
        [ProtoMember(3)] public string Message { get; set; } = "";
    }

    private sealed class ClientTransfer
    {
        public ClientTransfer(ProbeMetadata metadata)
        {
            Metadata = metadata;
            Chunks = new byte[metadata.TotalChunks][];
        }

        public ProbeMetadata Metadata { get; }
        public byte[][] Chunks { get; }
        public int ReceivedChunks;
        public int CompletionQueued;
    }

    private sealed class PreparedTransfer
    {
        public PreparedTransfer(byte[] bytes, string sha256, string finalPath)
        {
            Bytes = bytes;
            Sha256 = sha256;
            FinalPath = finalPath;
        }

        public byte[] Bytes { get; }
        public string Sha256 { get; }
        public string FinalPath { get; }
    }
}
