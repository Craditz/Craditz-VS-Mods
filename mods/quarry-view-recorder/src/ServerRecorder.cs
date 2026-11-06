using System.IO.Compression;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
namespace QuarryViewRecorder;

internal sealed class ServerRecorder : IDisposable
{
    readonly ICoreServerAPI api;
    readonly IServerNetworkChannel channel;
    readonly long tick;
    BlockPos? watchedCart;
    string? owner, watchedKind;
    sealed record CartState(BlockEntity Cart, string Kind, bool Started, bool Enabled, bool Complete);
    public ServerRecorder(ICoreServerAPI api)
    {
        this.api=api;
        channel=api.Network.RegisterChannel("quarryviewrecorder")
            .RegisterMessageType<RecorderRequest>().RegisterMessageType<RecorderReply>()
            .SetMessageHandler<RecorderRequest>(Request);
        tick=api.Event.RegisterGameTickListener(_ => Poll(),2000);
        api.Event.PlayerDisconnect += Disconnect;
    }
    void Request(IServerPlayer player, RecorderRequest request)
    {
        if (!player.HasPrivilege(Privilege.controlserver)) { Reply(player,"error","Recorder requires singleplayer admin privileges.");return; }
        if (!request.Arm) { if(owner==player.PlayerUID) ClearWatch(); return; }
        try
        {
            // Prefer current work over an old completed quarry, then use the
            // player's starting position. Camera moves never change this choice.
            var state=api.WorldManager.AllLoadedChunks.Values
                .Where(c=>c.BlockEntities!=null).SelectMany(c=>c.BlockEntities.Values).Distinct()
                .Where(be=>be.Pos.dimension==player.Entity.Pos.Dimension)
                .Select(ReadCart).OfType<CartState>()
                .OrderBy(s=>s.Complete?2:s.Started&&s.Enabled?0:1)
                .ThenBy(s=>player.Entity.Pos.SquareDistanceTo(s.Cart.Pos.ToVec3d()))
                .FirstOrDefault();
            if(state==null) {Reply(player,"error","No loaded quarrying or mining cart found. Stand near the cart you want to record and start again.");return;}
            DisableStorms();
            owner=player.PlayerUID;watchedCart=state.Cart.Pos.Copy();watchedKind=state.Kind;
            Reply(player,"armed",$"Watching {watchedKind} cart at {watchedCart}. Temporal storms are off for this world.");
            Poll();
        }
        catch(Exception e) {ClearWatch();api.Logger.Error("[QuarryRecorder] "+e);Reply(player,"error",e.Message);}
    }
    static CartState? ReadCart(BlockEntity be)
    {
        if(be.GetType().FullName!="FeralKinshipCompanions.BlockEntityFeralKinshipWorkCart")return null;
        TreeAttribute tree=new();be.ToTreeAttributes(tree);
        string path=be.Block?.Code?.Path??"";
        bool mining=tree.GetBool("miningTools")||path.StartsWith("workcart-mining-",StringComparison.Ordinal);
        if(!mining && !tree.GetBool("quarryTools") && !path.StartsWith("workcart-quarrying-",StringComparison.Ordinal))return null;
        using var plan=ReadPlan(tree.GetString(mining?"miningPlan":"quarryPlan","{}"),mining?2*1024*1024:1024*1024);
        var root=plan.RootElement;
        if(root.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Cart work state is not an object.");
        bool started=root.TryGetProperty("Started",out var start)&&start.GetBoolean();
        bool enabled=root.TryGetProperty("Enabled",out var enable)&&enable.GetBoolean();
        bool complete=started&&root.TryGetProperty(mining?"Mode":"Stage",out var phase)&&phase.GetString()=="complete";
        if(mining&&complete)
        {
            // The finite probe endpoint is distinct from blocked/paused work;
            // continuous future ore watching is not itself unfinished probing.
            int cursor=root.GetProperty("Cursor").GetInt32();
            int count=root.TryGetProperty("ProbePlan",out var route)&&route.ValueKind==JsonValueKind.Array&&route.GetArrayLength()>0
                ?route.GetArrayLength()
                :root.GetProperty("Depth").GetInt32()+1+root.GetProperty("Length").GetInt32()
                    +root.GetProperty("Length").GetInt32()/8*2*root.GetProperty("BranchLength").GetInt32();
            complete=cursor>=count&&(!root.TryGetProperty("Blocked",out var blocked)||blocked.GetArrayLength()==0);
        }
        // Don't declare completion while the last shared cut/support job is
        // still running. No Companions assembly reference or mutation needed.
        if(complete && be.GetType().GetField("QuarryJobs",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
            ?.GetValue(be) is ICollection jobs && jobs.Count>0)complete=false;
        return new(be,mining?"mining":"quarrying",started,enabled,complete);
    }
    void DisableStorms()
    {
        var system=api.ModLoader.GetModSystem<SystemTemporalStability>();
        // Vanilla caches this setting at world load. Update that one cache too so no restart is needed.
        FieldInfo? enabled=typeof(SystemTemporalStability).GetField("stormsEnabled",BindingFlags.Instance|BindingFlags.NonPublic);
        if(system!=null && enabled==null)throw new InvalidOperationException("Cannot disable the live storm setting on this engine build.");
        api.World.Config.SetString("temporalStorms","off");
        api.WorldManager.SaveGame.WorldConfiguration.SetString("temporalStorms","off");
        if(system!=null)
        {
            enabled!.SetValue(system,false);
            var data=system.StormData;
            if(data!=null)
            {
                data.nowStormActive=false;data.stormGlitchStrength=0;data.stormActiveTotalDays=0;data.stormDayNotify=99;
                api.Network.GetChannel("temporalstability").BroadcastPacket(data);
            }
        }
        api.Logger.Notification("[QuarryRecorder] Temporal storms disabled for this world.");
    }
    void Poll()
    {
        if(owner==null || watchedCart==null)return;
        var player=api.World.PlayerByUid(owner) as IServerPlayer;
        if(player==null) {ClearWatch();return;}
        var cart=api.World.BlockAccessor.GetBlockEntity(watchedCart);
        if(cart==null)return; // An unloaded cart is not a completed cart.
        try
        {
            var state=ReadCart(cart);
            if(state==null||state.Kind!=watchedKind)throw new InvalidDataException("The watched cart's work type changed. Start the recorder again to choose a cart.");
            if(state.Complete)
            {
                string label=state.Kind=="mining"?"Mining probe":"Quarry";
                ClearWatch();
                Reply(player,"complete",$"{label} complete. Taking final views, then pausing.");
            }
        }
        catch(Exception e)
        {
            ClearWatch();
            api.Logger.Warning("[QuarryRecorder] Completion read failed: {0}",e.Message);
            Reply(player,"failed","Cart status could not be read; recording stopped and the game will pause.");
        }
    }
    static JsonDocument ReadPlan(string saved,int limit)
    {
        if(saved.Length>limit*2)throw new InvalidDataException("Cart state exceeds its native limit.");
        if(!saved.StartsWith("gz1:",StringComparison.Ordinal))
        {
            if(saved.Length>limit)throw new InvalidDataException("Cart state exceeds its native limit.");
            return JsonDocument.Parse(saved);
        }
        using var input=new MemoryStream(Convert.FromBase64String(saved[4..]));
        using var gzip=new GZipStream(input,CompressionMode.Decompress);
        using var output=new MemoryStream();
        byte[] buffer=new byte[4096];int n;
        while((n=gzip.Read(buffer))>0)
        {
            if(output.Length+n>limit)throw new InvalidDataException("Cart state exceeds its native limit.");
            output.Write(buffer,0,n);
        }
        return JsonDocument.Parse(Encoding.UTF8.GetString(output.ToArray()));
    }
    void Reply(IServerPlayer p,string state,string text)=>channel.SendPacket(new RecorderReply{State=state,Message=text},p);
    void ClearWatch(){owner=null;watchedCart=null;watchedKind=null;}
    void Disconnect(IServerPlayer p) {if(owner==p.PlayerUID) ClearWatch();}
    public void Dispose() {api.Event.UnregisterGameTickListener(tick);api.Event.PlayerDisconnect-=Disconnect;}
}
