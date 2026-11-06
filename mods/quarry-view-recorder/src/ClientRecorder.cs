using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;
namespace QuarryViewRecorder;

internal sealed class ClientRecorder : IDisposable
{
    internal const string Key = "quarryviewrecorder-toggle";
    readonly ICoreClientAPI api;
    readonly IClientNetworkChannel channel;
    readonly RecorderConfig config;
    readonly InputLock inputLock;
    readonly CaptureRenderer before, after;
    readonly long tick;
    readonly Stopwatch clock=Stopwatch.StartNew();
    bool running, awaiting, complete, finalRound, restoring;
    long nextRoundAt, stageAt, lastToggle=-1000, armedAt;
    int phase, viewIndex, round;
    long captureId;
    View? home, target;
    bool homeFlying,homeNoClip,homeHud;
    EnumFreeMovAxisLock homeLock;
    string folder="",roundStamp="";
    public bool Capturing => home!=null;
    long Now => clock.ElapsedMilliseconds;
    public ClientRecorder(ICoreClientAPI api)
    {
        this.api=api;
        config=api.LoadModConfig<RecorderConfig>("QuarryViewRecorder.json") ?? new RecorderConfig();
        api.StoreModConfig(config,"QuarryViewRecorder.json");
        inputLock=new InputLock(api,this);
        api.Input.RegisterHotKey(Key,"Quarry recorder: start/stop (10 minutes)",GlKeys.F10,HotkeyType.GUIOrOtherControls,ctrlPressed:true,shiftPressed:true);
        api.Input.SetHotKeyHandler(Key,_=>{ if(Now-lastToggle>500){lastToggle=Now;Toggle();}return true; });
        api.Input.GetHotKeyByCode(Key).IsGlobalHotkey=true;
        api.Input.InWorldAction+=BlockAction;
        api.Event.BeforeActiveSlotChanged+=SlotChanging;
        channel=api.Network.RegisterChannel("quarryviewrecorder")
            .RegisterMessageType<RecorderRequest>().RegisterMessageType<RecorderReply>()
            .SetMessageHandler<RecorderReply>(p=>api.Event.EnqueueMainThreadTask(()=>Reply(p),"quarry-recorder-reply"));
        before=new CaptureRenderer(this,false);after=new CaptureRenderer(this,true);
        api.Event.RegisterRenderer(before,EnumRenderStage.Before,"quarry-recorder-lock");
        api.Event.RegisterRenderer(after,EnumRenderStage.AfterFinalComposition,"quarry-recorder-shot");
        tick=api.Event.RegisterGameTickListener(_=>Tick(),50);
        api.Event.LeaveWorld+=Leave;
        api.ChatCommands.Create("qrec").WithDescription("Toggle the four-view quarry/mining recorder")
            .HandleWith(_=>{Toggle();return TextCommandResult.Success();})
            .BeginSubCommand("stop").HandleWith(_=>{Stop("Recorder stopped.");return TextCommandResult.Success();}).EndSubCommand()
            .BeginSubCommand("status").HandleWith(_=>TextCommandResult.Success(running?$"Recorder on; round {round}; next round in {Math.Max(0,(nextRoundAt-Now)/1000)} seconds. Images: {folder}":"Recorder off.")).EndSubCommand();
    }
    void Say(string text)=>api.ShowChatMessage("[Quarry recorder] "+text);
    void Command(string text)
    {
        // Use the same client-command dispatch as a typed dot command. A bare
        // low-level Caller lacks the engine's client privilege context.
        // Native flight eligibility checks still run in the command handlers.
        api.TriggerChatMessage(text);
    }
    void RequestFlight(bool flying,bool noclip,EnumFreeMovAxisLock axis)
    {
        if(api.World is not ClientMain game || api.World.Player.WorldData is not ClientWorldPlayerData data)
            throw new InvalidOperationException("Unsupported native client movement data.");
        if(flying && !game.AllowFreemove)throw new InvalidOperationException("Flying is not enabled for this player.");
        // Each separate native command sends a full mode packet using the old
        // acknowledged flags. Send all desired flags together so they cannot race.
        data.RequestMode(game,data.MoveSpeedMultiplier,data.PickingRange,data.CurrentGameMode,
            flying,noclip,axis,data.RenderMetablocks);
    }
    bool TerrainReady()
    {
        var pos=api.World.Player.Entity.Pos.AsBlockPos;
        var blocks=api.World.BlockAccessor;
        if(blocks.GetMapChunkAtBlockPos(pos)==null)return false;
        pos.Y=blocks.GetTerrainMapheightAt(pos);
        return blocks.GetChunkAtBlockPos(pos)!=null;
    }
    void Toggle()
    {
        if(running||awaiting){Stop("Recorder stopped.");return;}
        if(!api.IsSinglePlayer || api.OpenedToLan){Say("Use this recorder in your singleplayer world, with LAN closed.");return;}
        if(api.World.Player?.Entity==null)return;
        if(api.World.Player.WorldData.CurrentGameMode is not (EnumGameMode.Creative or EnumGameMode.Spectator))
        {Say("Creative or spectator mode is required for the saved flying views.");return;}
        if(!channel.Connected){Say("The local recorder server is not ready yet.");return;}
        if(config.Views is not {Length:4} || config.Views.Any(v=>v==null || !double.IsFinite(v.X+v.Y+v.Z+v.Pitch+v.Yaw+v.Roll)))
        {Say("The recorder needs its four valid saved views.");return;}
        if(config.Views.Any(v=>v.Dimension!=api.World.Player.Entity.Pos.Dimension))
        {Say("Return to the saved views' dimension before starting.");return;}
        folder=Path.Combine(GamePaths.Screenshots,"QuarryTimelapse",DateTime.Now.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N")[..4]);
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"views.json"),JsonSerializer.Serialize(config,new JsonSerializerOptions{WriteIndented=true}));
            round=0;complete=false;awaiting=true;armedAt=Now;
            channel.SendPacket(new RecorderRequest{Arm=true});
        }
        catch(Exception e){Fail(e);}
    }
    void Reply(RecorderReply reply)
    {
        if(!running&&!awaiting)return;
        Say(reply.Message);
        if(reply.State=="error"){Stop("");return;}
        if(reply.State=="failed"){Fail(new InvalidOperationException(reply.Message));return;}
        if(reply.State=="armed") {running=true;awaiting=false;nextRoundAt=Now;Say("Started: four views now, then every 10 minutes. Ctrl+Shift+F10 stops; Escape cancels a capture.");}
        if(reply.State=="complete"){running=true;awaiting=false;complete=true;nextRoundAt=Now;}
    }
    bool ReadyForRound()=>!api.IsGamePaused && api.World.Player?.Entity!=null
        && !api.Gui.OpenedGuis.Any(d=>d!=inputLock && d.IsOpened() && (d.DialogType==EnumDialogType.Dialog || d.CaptureAllInputs()));
    void Tick()
    {
        if(awaiting && Now-armedAt>15000){Stop("Recorder did not receive a local server reply.");return;}
        if(!running)return;
        try
        {
            if(Capturing)
            {
                if(api.IsGamePaused){Stop("Capture cancelled because the game was paused.");return;}
                if(Now-stageAt>30000)
                {
                    var mode=api.World.Player.WorldData;
                    throw new TimeoutException(phase==1
                        ? $"Flight acknowledgement timed out (fly={mode.FreeMove}, noclip={mode.NoClip}, axis={mode.FreeMovePlaneLock})."
                        : $"View {viewIndex+1} terrain/render wait timed out (stage {phase}).");
                }
                if(phase==1 && api.World.Player.WorldData.FreeMove && api.World.Player.WorldData.NoClip
                    && api.World.Player.WorldData.FreeMovePlaneLock==EnumFreeMovAxisLock.None)
                {api.Logger.Notification("[QuarryRecorder] Camera flight acknowledged.");Command(".gui false");MoveToView();}
            }
            else if(Now>=nextRoundAt && ReadyForRound())BeginRound();
        }
        catch(Exception e){Fail(e);}
    }
    void BeginRound()
    {
        var p=api.World.Player;
        if(p.WorldData.CurrentGameMode is not (EnumGameMode.Creative or EnumGameMode.Spectator))throw new InvalidOperationException("Flying views require creative or spectator mode.");
        captureId++;
        home=View.From(p.Entity.Pos);homeFlying=p.WorldData.FreeMove;homeNoClip=p.WorldData.NoClip;
        homeLock=p.WorldData.FreeMovePlaneLock;homeHud=!api.HideGuis;
        finalRound=complete;round++;viewIndex=0;target=home;
        nextRoundAt=Now+Math.Clamp(config.IntervalMinutes,1,1440)*60000L;
        roundStamp=DateTime.Now.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+$"-round{round:D5}";
        inputLock.TryOpen();ClearInput();
        phase=1;stageAt=Now;
        api.Logger.Notification("[QuarryRecorder] Requesting combined flight/noclip/axis mode for round {0}.",round);
        RequestFlight(true,true,EnumFreeMovAxisLock.None);
    }
    void MoveToView()
    {
        if(!Capturing || !running)return;
        target=config.Views[viewIndex];phase=2;stageAt=Now;Pin();
        api.Logger.Notification("[QuarryRecorder] Settling view {0}/4 at {1}, {2}, {3}.",viewIndex+1,target.X,target.Y,target.Z);
    }
    internal void Frame(bool capture)
    {
        if(!Capturing)return;
        try
        {
            if(!capture){Pin();return;}
            if(phase!=2 || api.IsGamePaused || Now-stageAt<Math.Clamp(config.SettleSeconds,2,30)*1000L)return;
            // An air chunk has no drawn geometry, so IsChunkRendered(cameraPos)
            // can stay false forever. Wait for loaded terrain, not sky geometry.
            if(!TerrainReady())return;
            string viewFolder=Path.Combine(folder,$"pic{viewIndex+1}");Directory.CreateDirectory(viewFolder);
            string file=Path.Combine(viewFolder,roundStamp+".png");
            using(var bitmap=api.Render.GrabScreenshot(api.Render.FrameWidth,api.Render.FrameHeight,false,true))bitmap.Save(file);
            File.AppendAllText(Path.Combine(folder,"captures.jsonl"),JsonSerializer.Serialize(new {utc=DateTime.UtcNow,round,view=viewIndex+1,file,finalRound,position=target})+Environment.NewLine);
            viewIndex++;
            // Move on the next Before frame so the new camera matrices and world match.
            if(viewIndex<config.Views.Length){phase=3;Queue(MoveToView,"quarry-recorder-next-view");}
            else {phase=4;Queue(FinishRound,"quarry-recorder-finish");}
        }
        catch(Exception e){phase=4;Queue(()=>Fail(e),"quarry-recorder-error");}
    }
    void Queue(Action action,string name)
    {
        long id=captureId;
        api.Event.EnqueueMainThreadTask(()=>{if(id==captureId && Capturing)action();},name);
    }
    void Pin()
    {
        if(target==null || api.World.Player?.Entity==null)return;
        var p=api.World.Player.Entity.Pos;
        p.SetPos(target.X,target.Y,target.Z);p.Dimension=target.Dimension;
        p.Pitch=target.Pitch;p.Yaw=target.Yaw;p.Roll=target.Roll;p.Motion.Set(0,0,0);
        api.Input.MousePitch=target.Pitch;api.Input.MouseYaw=target.Yaw;
        ClearInput();
    }
    void ClearInput()
    {
        api.World.Player?.Entity?.Controls.StopAllMovement();
        api.World.Player?.Entity?.ServerControls.StopAllMovement();
        api.Input.InWorldMouseButton.Clear();
    }
    void Restore()
    {
        if(home==null)return;
        restoring=true;
        Exception? failure=null;
        void TryRestore(Action action){try{action();}catch(Exception e){failure??=e;}}
        try
        {
            target=home;TryRestore(Pin);
            TryRestore(()=>RequestFlight(homeFlying,homeNoClip,homeLock));
            TryRestore(()=>Command(".gui "+homeHud.ToString().ToLowerInvariant()));
        }
        finally
        {
            home=null;target=null;phase=0;captureId++;
            try
            {
                inputLock.TryClose();ClearInput();
                Array.Clear(api.Input.KeyboardKeyState);
                Array.Clear(api.Input.KeyboardKeyStateRaw);
            }
            finally {restoring=false;}
        }
        if(failure!=null)throw new InvalidOperationException("Could not fully restore capture state.",failure);
    }
    void FinishRound()
    {
        if(!Capturing)return;
        bool finished=finalRound;
        try {Restore();Say($"Round {round}: four pictures saved.");}
        catch(Exception e){Fail(e);return;}
        if(finished)
        {
            Stop("Watched cart complete. Final views saved; game paused.");
            api.PauseGame(true);
        }
        // If completion arrived mid-round, the next round is a fresh final set.
        else if(complete)nextRoundAt=Now;
    }
    internal void Cancel()=>Stop("Capture cancelled; recorder stopped.");
    internal void LockClosed(){if(Capturing&&!restoring)Cancel();}
    void BlockAction(EnumEntityAction action,bool on,ref EnumHandling handling){if(Capturing)handling=EnumHandling.PreventSubsequent;}
    EnumHandling SlotChanging(ActiveSlotChangeEventArgs args)=>Capturing?EnumHandling.PreventSubsequent:EnumHandling.PassThrough;
    void Stop(string message)
    {
        running=false;awaiting=false;complete=false;captureId++;
        try {if(Capturing)Restore();}
        catch(Exception e){api.Logger.Error("[QuarryRecorder] Restore failed: "+e);}
        finally {home=null;target=null;phase=0;inputLock.TryClose();}
        if(channel.Connected)channel.SendPacket(new RecorderRequest{Arm=false});
        if(message.Length>0)Say(message);
    }
    void Fail(Exception e)
    {
        api.Logger.Error("[QuarryRecorder] "+e);
        Stop("Recording stopped: "+e.Message);
        if(api.IsSinglePlayer && api.World.Player?.Entity!=null)api.PauseGame(true);
    }
    void Leave()=>Stop("");
    public void Dispose()
    {
        Stop("");api.Event.UnregisterGameTickListener(tick);api.Event.LeaveWorld-=Leave;
        api.Input.InWorldAction-=BlockAction;api.Event.BeforeActiveSlotChanged-=SlotChanging;
        api.Event.UnregisterRenderer(before,EnumRenderStage.Before);api.Event.UnregisterRenderer(after,EnumRenderStage.AfterFinalComposition);
        inputLock.Dispose();
    }
}
internal sealed class CaptureRenderer(ClientRecorder recorder,bool capture) : IRenderer
{
    public double RenderOrder=>capture?10:-100;
    public int RenderRange=>9999;
    public void OnRenderFrame(float deltaTime,EnumRenderStage stage)=>recorder.Frame(capture);
    public void Dispose(){}
}
internal sealed class InputLock(ICoreClientAPI api,ClientRecorder recorder) : GuiDialog(api)
{
    public override string ToggleKeyCombinationCode=>null!;
    public override bool CaptureAllInputs()=>true;
    public override bool CaptureRawMouse()=>true;
    public override bool PrefersUngrabbedMouse=>false;
    public override bool DisableMouseGrab=>true;
    public override void OnKeyDown(KeyEvent args){args.Handled=true;if(args.KeyCode==(int)GlKeys.Escape)recorder.Cancel();}
    public override void OnKeyUp(KeyEvent args)=>args.Handled=true;
    public override void OnKeyPress(KeyEvent args)=>args.Handled=true;
    public override void OnMouseDown(MouseEvent args)=>args.Handled=true;
    public override void OnMouseUp(MouseEvent args)=>args.Handled=true;
    public override void OnMouseMove(MouseEvent args)=>args.Handled=true;
    public override void OnMouseWheel(MouseWheelEventArgs args)=>args.SetHandled();
    public override void OnGuiClosed(){base.OnGuiClosed();recorder.LockClosed();}
}
