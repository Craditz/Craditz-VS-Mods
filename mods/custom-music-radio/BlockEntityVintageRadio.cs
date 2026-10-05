using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CustomMusicRadio;

public sealed class BlockEntityVintageRadio : BlockEntity
{
    public string OwnerUid { get; private set; } = "";
    public string Playlist { get; private set; } = RadioPlaylists.Base;
    public string Track { get; private set; } = "";
    public string Hash { get; private set; } = "";
    public bool Playing { get; private set; }
    public float Volume { get; private set; } = RadioControls.DefaultVolume;
    public float ListeningRange { get; private set; } = RadioControls.DefaultListeningRange;
    public float RangeCap { get; private set; } = RadioControls.MaxListeningRange;
    public RadioPlaybackMode Mode { get; private set; }
    public double Duration { get; private set; }
    public string PlaybackGeneration { get; private set; } = Guid.NewGuid().ToString("N");
    public string LastError { get; private set; } = "";
    public int NeedlePosition { get; private set; }
    private volatile MeshData? visualMesh;
    private int visualBlockId = -1;
    public int Revision { get; private set; }
    private double offset;
    private long startedMs;
    private long lastPressMs = -1000;
    private int ticks;
    private bool initialized;
    private bool preparing;
    private long preparationToken;
    private readonly RadioShuffleBag shuffle = new();
    public bool Upper => Block is BlockVintageRadio { Upper: true };
    public double PlaybackSeconds => offset + (Playing && initialized ? Math.Max(0, Api.World.ElapsedMilliseconds - startedMs) / 1000d : 0);
    private CustomMusicRadioSystem System => Api.ModLoader.GetModSystem<CustomMusicRadioSystem>();

    public void SetPlacer(string uid)
    {
        if (Api.Side != EnumAppSide.Server || Upper || OwnerUid.Length > 0 || string.IsNullOrWhiteSpace(uid)) return;
        OwnerUid = uid;
        MarkDirty();
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        initialized = true;
        startedMs = api.World.ElapsedMilliseconds;
        if (api.Side == EnumAppSide.Server)
        {
            float oldRange = ListeningRange, oldCap = RangeCap;
            ApplyRangeCap(System.ServerRangeCap);
            if (!Upper && Playing) Duration = 0; // Revalidate saved file/duration once after chunk load.
            if (oldRange != ListeningRange || oldCap != RangeCap) MarkDirty();
        }
        RegisterGameTickListener(Tick, api.Side == EnumAppSide.Client ? 100 : 500);
        RefreshVisualMesh();
    }

    private void Tick(float dt)
    {
        if (Api.Side == EnumAppSide.Client)
        {
            if (!Upper) System.UpdateRadioAudio(this);
            else if (visualBlockId != Api.World.BlockAccessor.GetBlock(Pos).Id) RefreshVisualMesh();
            return;
        }
        if (Block is not BlockVintageRadio block) return;
        BlockPos other = block.PartnerPosition(Pos);
        // Wait for the other chunk at vertical boundaries; an unloaded chunk is not air.
        if (Api.World.BlockAccessor.GetChunkAtBlockPos(other) == null) return;
        if (!block.IsPartner(Api.World.BlockAccessor.GetBlock(other)))
        {
            Api.World.BlockAccessor.SetBlock(0, Pos);
            return;
        }
        if (!Upper) ServerPlaybackTick();
        if (!Upper && ++ticks % 10 == 0)
        {
            UpdateVisualState();
            if (Playing) MarkDirty(); // Timeline snapshot, also corrects drift for entering clients.
        }
    }

    public void Press(int control, IPlayer player, float sliderRange = RadioControls.DefaultListeningRange)
    {
        if (Api.Side != EnumAppSide.Server || Upper || control < 0 || control > 7) return;
        long now = Api.World.ElapsedMilliseconds;
        if (now - lastPressMs < 180) return;
        lastPressMs = now;
        offset = PlaybackSeconds;
        startedMs = now;
        switch (control)
        {
            case 0:
            case 2:
                if (!System.CanUsePlaylist(Playlist, OwnerUid)) { FailPlayback("This playlist is not available on this radio.", player); return; }
                string[] names = System.TrackNames(Playlist); // Invalid/corrupt files are filtered before metadata cycling.
                if (names.Length == 0) { FailPlayback(System.EmptyLibraryMessage(Playlist), player); return; }
                SelectTrackMetadata(names, control == 0 ? -1 : 1);
                break;
            case 1:
                if (Playing || preparing) { Playing = false; preparing = false; preparationToken++; break; }
                if (Track.Length == 0 && Playlist.Length == 0 && System.TrackNames().Length == 0)
                {
                    string[] available = System.PlaylistNames(OwnerUid);
                    if (available.Length > 0) Playlist = available[0];
                }
                if (!System.CanUsePlaylist(Playlist, OwnerUid)) { FailPlayback("This playlist is not available on this radio.", player); return; }
                BeginPlaybackPreparation(Track, false, player);
                return;
            case 3: Volume = RadioControls.StepVolume(Volume, -1); break;
            case 4: Volume = RadioControls.StepVolume(Volume, 1); break;
            case 5: ListeningRange = RadioControls.ClampListeningRange(sliderRange, RangeCap); break;
            case 6:
                Mode = RadioPlayback.Next(Mode);
                shuffle.Reset(Playlist, Mode == RadioPlaybackMode.ShufflePlaylist ? Track : "");
                Notify(player, "Playback mode: " + RadioPlayback.Label(Mode));
                break;
            case 7:
                string[] playlists = System.PlaylistNames(OwnerUid);
                string? nextPlaylist = RadioPlaylists.Next(Playlist, playlists);
                if (nextPlaylist == null) { FailPlayback("No accessible playlists contain valid Ogg Vorbis tracks.", player); return; }
                Playlist = nextPlaylist;
                Track = "";
                shuffle.Reset(Playlist);
                SelectTrackMetadata(System.TrackNames(Playlist), 1);
                Notify(player, "Playlist: " + RadioPlaylists.Label(Playlist));
                break;
        }
        if (control is 0 or 1 or 2 or 6 or 7) PlaybackGeneration = Guid.NewGuid().ToString("N");
        if (control is 0 or 1 or 2 or 7) LastError = "";
        Revision++;
        UpdateVisualState();
        MarkDirty();
    }

    // Metadata only: no API, file access, transfer, hash or decode can occur here.
    public bool SelectTrackMetadata(string[] names, int direction)
    {
        if (names.Length == 0) return false;
        preparing = false;
        preparationToken++;
        int current = Array.IndexOf(names, Track);
        int next = current < 0 ? (direction < 0 ? names.Length - 1 : 0)
            : (current + (direction < 0 ? -1 : 1) + names.Length) % names.Length;
        if (Track != names[next]) NeedlePosition = RadioControls.AdvanceNeedle(NeedlePosition);
        Track = names[next];
        if (Mode == RadioPlaybackMode.ShufflePlaylist) shuffle.Record(Playlist, Track);
        Hash = "";
        Duration = 0;
        PlaybackGeneration = Guid.NewGuid().ToString("N");
        offset = 0;
        Playing = false;
        return true;
    }

    public void ApplyRangeCap(float cap)
    {
        RangeCap = RadioControls.ClampCap(cap);
        ListeningRange = RadioControls.ClampListeningRange(ListeningRange, RangeCap);
    }

    // Called only by loaded authoritative lower entities, never by a client end event.
    public void ServerPlaybackTick()
    {
        if (Api.Side != EnumAppSide.Server || Upper || !Playing) return;
        if (!System.CanUsePlaylist(Playlist, OwnerUid)) { FailPlayback("This playlist is not available on this radio."); return; }
        if (Duration <= 0)
        {
            BeginPlaybackPreparation(Track, false);
            return;
        }
        if (PlaybackSeconds < Duration) return;
        if (Mode == RadioPlaybackMode.RepeatCurrent)
        {
            offset = PlaybackSeconds % Duration;
            startedMs = Api.World.ElapsedMilliseconds;
            MarkDirty();
            return;
        }
        if (Mode == RadioPlaybackMode.NoLoop)
        {
            offset = 0; Playing = false;
        }
        else
        {
            string[] names = System.TrackNames(Playlist);
            string? next = Mode == RadioPlaybackMode.ShufflePlaylist
                ? shuffle.Next(Playlist, Track, names, Random.Shared)
                : RadioPlayback.NextTrack(Mode, Track, names);
            if (next == null) { FailPlayback(System.EmptyLibraryMessage(Playlist)); return; }
            BeginPlaybackPreparation(next, true);
            return;
        }
        startedMs = Api.World.ElapsedMilliseconds;
        PlaybackGeneration = Guid.NewGuid().ToString("N");
        Revision++;
        UpdateVisualState();
        MarkDirty();
    }

    private void BeginPlaybackPreparation(string selected, bool resetPosition, IPlayer? player = null)
    {
        offset = resetPosition ? 0 : PlaybackSeconds;
        Playing = false;
        preparing = true;
        long token = ++preparationToken;
        string playlist = Playlist;
        PlaybackGeneration = Guid.NewGuid().ToString("N");
        LastError = "";
        Revision++;
        UpdateVisualState();
        MarkDirty();
        System.PrepareRadioTrackAsync(playlist, selected, (name, hash, duration, error) =>
        {
            if (!preparing || token != preparationToken || playlist != Playlist
                || !ReferenceEquals(Api.World.BlockAccessor.GetBlockEntity(Pos), this)) return;
            preparing = false;
            if (!System.CanUsePlaylist(Playlist, OwnerUid))
            { FailPlayback("This playlist is not available on this radio.", player); return; }
            if (error.Length > 0) { FailPlayback(error, player); return; }
            if (Track != name || Hash != hash || offset >= duration) offset = 0;
            if (Track != name) NeedlePosition = RadioControls.AdvanceNeedle(NeedlePosition);
            Track = name;
            Hash = hash;
            Duration = duration;
            Playing = true;
            startedMs = Api.World.ElapsedMilliseconds;
            PlaybackGeneration = Guid.NewGuid().ToString("N");
            Revision++;
            UpdateVisualState();
            MarkDirty();
        });
    }

    private void FailPlayback(string error, IPlayer? player = null)
    {
        preparing = false;
        preparationToken++;
        LastError = error;
        Playing = false; offset = 0;
        PlaybackGeneration = Guid.NewGuid().ToString("N");
        Revision++;
        Api.Logger.Warning("[custommusicradio] Radio at {0}: {1}", Pos, error);
        if (player != null) Notify(player, error);
        UpdateVisualState();
        MarkDirty();
    }

    private static void Notify(IPlayer player, string message)
    {
        if (player is Vintagestory.API.Server.IServerPlayer serverPlayer)
            serverPlayer.SendMessage(0, "[Radio] " + message, EnumChatType.Notification);
    }

    private void UpdateVisualState()
    {
        if (Block is not BlockVintageRadio block) return;
        BlockPos upperPos = Pos.UpCopy();
        if (!block.IsPartner(Api.World.BlockAccessor.GetBlock(upperPos))) return;
        Block? upper = Api.World.GetBlock(block.CodeWithParts("upper", block.Facing, Playing ? "on" : "off"));
        if (upper != null && Api.World.BlockAccessor.GetBlock(upperPos).Id != upper.Id)
        {
            Api.World.BlockAccessor.ExchangeBlock(upper.Id, upperPos);
            Api.World.BlockAccessor.MarkBlockDirty(upperPos);
        }
        if (Api.World.BlockAccessor.GetBlockEntity(upperPos) is BlockEntityVintageRadio visual
            && (visual.ListeningRange != ListeningRange || visual.RangeCap != RangeCap || visual.Mode != Mode || visual.Volume != Volume || visual.NeedlePosition != NeedlePosition || visual.Playing != Playing))
        {
            visual.ListeningRange = ListeningRange;
            visual.RangeCap = RangeCap;
            visual.Mode = Mode;
            visual.Volume = Volume;
            visual.NeedlePosition = NeedlePosition;
            visual.Playing = Playing;
            visual.MarkDirty(true);
        }
    }

    private void RefreshVisualMesh()
    {
        if (!initialized || !Upper || Api is not ICoreClientAPI client) return;
        Block current = Api.World.BlockAccessor.GetBlock(Pos);
        if (current is not BlockVintageRadio radio) return;
        Shape? template = client.TesselatorManager.GetCachedShape(current.Shape.Base);
        if (template == null) return;
        Shape shape = template.Clone();
        RadioControls.ApplyVisualState(shape, radio.Facing, ListeningRange, NeedlePosition, RangeCap, Mode);
        client.Tesselator.TesselateShape(current, shape, out MeshData mesh);
        visualMesh = mesh; // Publish an immutable mesh to the chunk tesselation thread.
        visualBlockId = current.Id;
        client.World.BlockAccessor.MarkBlockDirty(Pos);
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        MeshData? mesh = visualMesh;
        if (!Upper || mesh == null) return false;
        mesher.AddMeshData(mesh);
        return true; // Replace the static upper mesh; never draw duplicate faces.
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetString("ownerUid", OwnerUid);
        tree.SetString("playlist", Playlist);
        tree.SetString("track", Track);
        tree.SetString("hash", Hash);
        tree.SetBool("playing", Playing);
        tree.SetFloat("volume", Volume);
        tree.SetFloat("listeningRange", ListeningRange);
        tree.SetFloat("rangeCap", RangeCap);
        tree.SetInt("playbackMode", (int)Mode);
        tree.SetDouble("duration", Duration);
        tree.SetString("playbackGeneration", PlaybackGeneration);
        tree.SetString("radioError", LastError);
        tree.SetInt("needlePosition", NeedlePosition);
        tree.SetDouble("offset", initialized ? PlaybackSeconds : offset);
        tree.SetInt("revision", Revision);
        tree.SetString("shufflePlaylist", shuffle.Playlist);
        tree["shufflePlayed"] = new StringArrayAttribute(shuffle.Played);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
    {
        base.FromTreeAttributes(tree, world);
        OwnerUid = tree.GetString("ownerUid", "");
        string savedPlaylist = tree.GetString("playlist", RadioPlaylists.Base);
        Playlist = savedPlaylist.Length == 0 || RadioPlaylists.IsSafeFolder(savedPlaylist) ? savedPlaylist : RadioPlaylists.Base;
        Track = tree.GetString("track", "");
        Hash = tree.GetString("hash", "");
        Playing = tree.GetBool("playing");
        float oldRange = ListeningRange;
        float oldCap = RangeCap;
        int oldNeedle = NeedlePosition;
        RadioPlaybackMode oldMode = Mode;
        Volume = RadioControls.ClampVolume(tree.GetFloat("volume", RadioControls.DefaultVolume));
        RangeCap = RadioControls.ClampCap(tree.GetFloat("rangeCap", RadioControls.MaxListeningRange));
        if (initialized && world.Side == EnumAppSide.Server) RangeCap = System.ServerRangeCap;
        ListeningRange = RadioControls.ClampListeningRange(tree.GetFloat("listeningRange", RadioControls.DefaultListeningRange), RangeCap);
        Mode = RadioPlayback.Sanitize(tree.GetInt("playbackMode"));
        double duration = tree.GetDouble("duration");
        Duration = double.IsFinite(duration) && duration > 0 ? duration : 0;
        PlaybackGeneration = tree.GetString("playbackGeneration", PlaybackGeneration);
        LastError = tree.GetString("radioError", "");
        NeedlePosition = RadioControls.ClampNeedle(tree.GetInt("needlePosition"));
        double seconds = tree.GetDouble("offset");
        offset = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
        Revision = tree.GetInt("revision");
        shuffle.Restore(tree.GetString("shufflePlaylist", Playlist),
            (tree["shufflePlayed"] as StringArrayAttribute)?.value ?? Array.Empty<string>());
        startedMs = world.ElapsedMilliseconds;
        if (initialized && world.Side == EnumAppSide.Client)
        {
            if (!Upper) System.UpdateRadioAudio(this);
            else if (oldRange != ListeningRange || oldCap != RangeCap || oldNeedle != NeedlePosition || oldMode != Mode
                || visualBlockId != world.BlockAccessor.GetBlock(Pos).Id) RefreshVisualMesh();
        }
    }

    public override void GetBlockInfo(IPlayer player, StringBuilder description)
    {
        if (Upper)
        {
            (Api.World.BlockAccessor.GetBlockEntity(Pos.DownCopy()) as BlockEntityVintageRadio)?.GetBlockInfo(player, description);
            return;
        }
        description.AppendLine("Playlist: " + RadioPlaylists.Label(Playlist));
        description.AppendLine(Track.Length == 0 ? "Select a track or press play" : Track);
        description.AppendLine($"{(Playing ? "Playing" : "Paused")} · Volume {Volume:P0} | Listening range {ListeningRange:0} blocks");
        description.AppendLine($"{RadioPlayback.Label(Mode)} | Range slider: 16–{RangeCap:0} blocks");
        if (LastError.Length > 0) description.AppendLine("Audio error: " + LastError);
    }

    public override void OnBlockRemoved()
    {
        preparing = false;
        preparationToken++;
        if (initialized && !Upper) System.RemoveRadioAudio(Pos);
        base.OnBlockRemoved();
    }

    public override void OnBlockUnloaded()
    {
        preparing = false;
        preparationToken++;
        if (initialized && !Upper) System.RemoveRadioAudio(Pos);
        base.OnBlockUnloaded();
    }
}
