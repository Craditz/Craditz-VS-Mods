using System.Text.Json;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace RustboundExpansion;

internal static class GrenadeCatalog
{
    internal static readonly SpellDefinition[] All=Read();
    private static SpellDefinition[] Read()
    {
        using var stream=typeof(GrenadeCatalog).Assembly.GetManifestResourceStream("RustboundExpansion.grenade-spell-definitions.json")!;
        return JsonSerializer.Deserialize<SpellDefinition[]>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    }
}

public sealed class HolyGrenadeSpell : ExpansionSpell
{
    internal const string SpellId="rbxholyhandgrenade1";
    public HolyGrenadeSpell():base(SpellId,"conjuration","light")
    {Tier=2;ManaCost=400;CastTime=20;Damage=30;Range=6;}
    public override void OnCastStop(float seconds,ItemSlot slot,EntityAgent caster,BlockSelection block,EntitySelection selected,
        bool scroll,bool magnification,bool duplication,float remaining,bool infinite=false)
    {
        if(caster is not EntityPlayer p || p.World.Side!=EnumAppSide.Server || remaining!=0 || !p.Alive || p.ShouldDespawn
            || ControlEffects.Banished(p) || StatusEffects.IsEffectActive(p,"stasis"))return;
        var system=HolyGrenadeSystem.For(p.World);
        if(!Allows(p) || (!scroll && p.WatchedAttributes.GetInt("entitybehavior-resource-currentmana_rm")<GetModifiedManaCost(p,Id))
            || system==null || !system.Conjure(p,GetModifiedDamage(p,Id))) {NotifyFailure(p);return;}
        Complete(p,scroll,false);
    }
    internal static bool Allows(EntityPlayer p)
    {
        var inv=p.Player?.InventoryManager?.GetOwnInventory("character");
        var s=inv is {Count:>7}?inv[7]?.Itemstack:null;
        return s?.StackSize==1 && s.Attributes.GetBool("item-attribute-soulbound_rm")
            && s.Attributes.GetString("item-attribute-soulbound-playerid_rm","")==p.PlayerUID
            && s.Collectible?.Code?.ToString() is "rustboundmagic:attunementcore-basic-conjuration" or "rustboundmagic:attunementcore-advanced-conjuration";
    }
}

internal sealed class GrenadeLedger
{
    internal sealed record Claim(string Token,string Stage,long EntityId=0);
    internal Dictionary<string,Claim> Claims=new();
    internal bool Valid(string owner,string token)=>Claims.TryGetValue(owner,out var c) && c.Token==token;
    internal bool Reserve(string owner,string token)
    {if(string.IsNullOrEmpty(owner)||string.IsNullOrEmpty(token)||Claims.ContainsKey(owner))return false;Claims[owner]=new(token,"item");return true;}
    internal bool Release(string owner,string token)
    {if(!Valid(owner,token))return false;Claims.Remove(owner);return true;}
    internal bool Set(string owner,string token,string stage,long id=0)
    {if(!Valid(owner,token))return false;Claims[owner]=new(token,stage,id);return true;}
    internal byte[] Serialize()=>JsonSerializer.SerializeToUtf8Bytes(Claims);
    internal void Restore(byte[] bytes)=>Claims=JsonSerializer.Deserialize<Dictionary<string,Claim>>(bytes)??new();
}

// The countdown is serialized as seconds remaining, never an old world's clock.
internal sealed class GrenadeRest
{
    internal float Quiet,Remaining=-1;
    internal bool Step(float dt,bool still,Random random)
    {
        if(!float.IsFinite(dt)||dt<=0)return false;
        if(!still){Quiet=0;Remaining=-1;return false;}
        if(Quiet<.3f){Quiet+=dt;if(Quiet<.3f)return false;Remaining=(float)random.NextDouble()*3;return Remaining<=0;}
        Remaining-=dt;return Remaining<=0;
    }
}

public sealed class HolyGrenadeSystem : ModSystem
{
    internal const string Owner="rbx-grenade-owner",Token="rbx-grenade-token",Power="rbx-grenade-power";
    private const string SaveKey="rustboundexpansion-holy-grenades-v1";
    private static readonly Dictionary<IWorldAccessor,HolyGrenadeSystem> Worlds=new(ReferenceEqualityComparer.Instance);
    internal static HolyGrenadeSystem? For(IWorldAccessor world)=>Worlds.GetValueOrDefault(world);
    internal readonly GrenadeLedger Ledger=new();
    private readonly Dictionary<ItemSlot,List<(string Owner,string Token)>> slots=new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<EntityItem,List<(string Owner,string Token)>> drops=new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<HolyGrenadeEntity> flights=new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ItemSlot> cleaning=new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string,long> absentSince=new();
    internal IWorldAccessor World=null!;
    private ICoreServerAPI? server;
    private long listener;
    internal void Bind(IWorldAccessor world){World=world;Worlds[world]=this;}
    public override void StartServerSide(ICoreServerAPI api)
    {
        server=api;Bind(api.World);Load();
        api.Event.SaveGameLoaded+=Load;api.Event.GameWorldSave+=Save;
        api.Event.OnEntityLoaded+=Loaded;api.Event.OnEntitySpawn+=Loaded;api.Event.OnEntityDespawn+=Despawned;
        api.Event.PlayerNowPlaying+=PlayerLoaded;
        api.Event.PlayerDisconnect+=CreatorDisconnected;
        listener=api.Event.RegisterGameTickListener(_=>Sweep(),200);
    }
    private void Load()
    {
        var bytes=server?.WorldManager.SaveGame?.GetData(SaveKey);if(bytes!=null)Ledger.Restore(bytes);
        // No creator's previous login session survives a server restart/crash.
        ResetSession();
    }
    internal void ResetSession(){foreach(var owner in Ledger.Claims.Keys.ToArray())ExpireOwner(owner);}
    internal void Save()=>server?.WorldManager.SaveGame?.StoreData(SaveKey,Ledger.Serialize());
    internal static bool Identity(ItemStack? stack,out string owner,out string token)
    {
        owner=stack?.Attributes.GetString(Owner,"")??"";token=stack?.Attributes.GetString(Token,"")??"";
        return stack?.Collectible is HolyGrenadeItem && stack.StackSize==1 && owner.Length>0 && token.Length>0;
    }
    internal void Observe(ItemSlot slot,bool prune=true)
    {
        if(cleaning.Contains(slot))return;
        if(prune)CleanupSlot(slot);
        var ids=Identities(slot.Itemstack).Where(id=>Ledger.Valid(id.Owner,id.Token)).ToList();
        if(ids.Count==0)return;
        slots[slot]=ids;
        foreach(var id in ids){absentSince.Remove(id.Owner);if(Ledger.Claims[id.Owner].Stage=="unloaded")Ledger.Set(id.Owner,id.Token,"item");}
    }
    private (ItemStack? Stack,bool Changed) PruneStack(ItemStack? stack,InventoryBase? inventory,int depth=0)
    {
        if(stack?.Collectible is HolyGrenadeItem && Identity(stack,out var owner,out var token) && !Ledger.Valid(owner,token))return(null,true);
        if(depth>=8 || stack?.Collectible is not IHeldBag bag)return(stack,false);
        bool changed=false;var contents=bag.GetContents(stack,World)??[];
        for(int i=0;i<contents.Length;i++)
        {
            var result=PruneStack(contents[i],inventory,depth+1);if(!result.Changed)continue;
            // Store is IHeldBag's persistence contract. Never clear an entire bag
            // or mark its temporary child slots dirty against the parent inventory.
            bag.Store(stack,new ItemSlotBagContent(inventory!,0,i,EnumItemStorageFlags.General){Itemstack=result.Stack});
            changed=true;
        }
        return(stack,changed);
    }
    internal bool CleanupSlot(ItemSlot slot)
    {
        if(World.Side!=EnumAppSide.Server || !cleaning.Add(slot))return false;
        try
        {
            var result=PruneStack(slot.Itemstack,slot.Inventory);if(!result.Changed)return false;
            slot.Itemstack=result.Stack;slot.MarkDirty();return true;
        }
        finally{cleaning.Remove(slot);}
    }
    internal bool CleanupDrop(EntityItem item)
    {
        if(World.Side!=EnumAppSide.Server)return false;
        var result=PruneStack(item.Itemstack,item.Slot.Inventory);if(!result.Changed)return false;
        item.Itemstack=result.Stack!;item.WatchedAttributes.MarkPathDirty("itemstack");
        if(result.Stack==null)item.Die(EnumDespawnReason.Removed);
        return true;
    }
    private void CreatorDisconnected(IServerPlayer p)=>ExpireOwner(p.PlayerUID);
    internal void ExpireOwner(string owner)
    {
        if(World.Side!=EnumAppSide.Server)return;
        var affectedSlots=slots.Where(pair=>pair.Value.Any(id=>id.Owner==owner)).Select(pair=>pair.Key).ToArray();
        var affectedDrops=drops.Where(pair=>pair.Value.Any(id=>id.Owner==owner)).Select(pair=>pair.Key).ToArray();
        var affectedFlights=flights.Where(e=>e.WatchedAttributes.GetString(Owner,"")==owner).ToArray();
        // Invalidating first prevents cleanup callbacks or a fast rejoin from
        // resurrecting a hidden grenade or releasing a newly created replacement.
        if(Ledger.Claims.TryGetValue(owner,out var claim))Release(owner,claim.Token);
        foreach(var slot in affectedSlots)CleanupSlot(slot);
        foreach(var item in affectedDrops)CleanupDrop(item);
        foreach(var grenade in affectedFlights)grenade.Dissolve();
    }
    internal void TrackFlight(HolyGrenadeEntity grenade)=>flights.Add(grenade);
    internal IEnumerable<(string Owner,string Token)> Identities(ItemStack? stack,int depth=0)
    {
        if(Identity(stack,out var owner,out var token))yield return(owner,token);
        if(depth>=8 || stack?.Collectible is not IHeldBag bag)yield break;
        foreach(var child in bag.GetContents(stack,World)??[])
            foreach(var id in Identities(child,depth+1))yield return id;
    }
    private void PlayerLoaded(IServerPlayer p)
    {foreach(var inv in p.InventoryManager.Inventories.Values)foreach(var slot in inv)Observe(slot);}
    private void Loaded(Entity e)
    {
        if(e is HolyGrenadeEntity grenade){TrackFlight(grenade);return;}
        if(e is not EntityItem item)return;
        CleanupDrop(item);if(item.ShouldDespawn || item.Itemstack==null)return;
        var ids=Identities(item.Itemstack).Where(id=>Ledger.Valid(id.Owner,id.Token)).ToList();if(ids.Count==0)return;
        drops[item]=ids;
        foreach(var id in ids){absentSince.Remove(id.Owner);if(Ledger.Claims[id.Owner].Stage=="unloaded")Ledger.Set(id.Owner,id.Token,"item");}
    }
    private void Despawned(Entity e,EntityDespawnData data)
    {
        if(e is HolyGrenadeEntity grenade)flights.Remove(grenade);
        if(e is HolyGrenadeEntity && data.Reason!=EnumDespawnReason.Unload)
        {
            string owner=e.WatchedAttributes.GetString(Owner,"");string token=e.WatchedAttributes.GetString(Token,"");
            if(Ledger.Claims.GetValueOrDefault(owner) is {Stage:"flight"} claim && claim.EntityId==e.EntityId)Release(owner,token);
        }
        if(e is not EntityItem item || !drops.Remove(item,out var ids))return;
        // Chunk unload is not destruction. An unloaded item's claim must survive.
        foreach(var id in ids)
            if(data.Reason==EnumDespawnReason.Unload){Ledger.Set(id.Owner,id.Token,"unloaded");absentSince.Remove(id.Owner);}
            else absentSince.TryAdd(id.Owner,World.ElapsedMilliseconds);
        Save();
    }
    internal void Sweep()
    {
        foreach(var slot in slots.Keys.ToArray())CleanupSlot(slot);
        foreach(var item in drops.Keys.ToArray())if(!item.ShouldDespawn)CleanupDrop(item);
        foreach(var grenade in flights.ToArray())
            if(grenade.ShouldDespawn)flights.Remove(grenade);
            else if(!Ledger.Valid(grenade.WatchedAttributes.GetString(Owner,""),grenade.WatchedAttributes.GetString(Token,"")))grenade.Dissolve();
        var observed=slots.Values.SelectMany(s=>s).Select(s=>s.Owner).Concat(drops.Values.SelectMany(s=>s).Select(s=>s.Owner)).Concat(absentSince.Keys).Distinct().ToArray();
        foreach(var owner in observed)
        {
            if(!Ledger.Claims.TryGetValue(owner,out var claim)||claim.Stage!="item")continue;
            bool present=slots.Any(pair=>pair.Value.Any(id=>id.Owner==owner) && Identities(pair.Key.Itemstack).Any(id=>id.Owner==owner && id.Token==claim.Token))
                || drops.Any(pair=>pair.Value.Any(id=>id.Owner==owner) && !pair.Key.ShouldDespawn && Identities(pair.Key.Itemstack).Any(id=>id.Owner==owner && id.Token==claim.Token));
            if(present){absentSince.Remove(owner);continue;}
            if(!absentSince.TryGetValue(owner,out var since)){absentSince[owner]=World.ElapsedMilliseconds;continue;}
            // Transfers and entity pickup finish before this deferred destruction check.
            if(World.ElapsedMilliseconds-since>=1000){Release(owner,claim.Token);}
        }
        foreach(var slot in slots.Keys.Where(s=>!Identities(s.Itemstack).Any()).ToArray())slots.Remove(slot);
        foreach(var item in drops.Keys.Where(e=>e.ShouldDespawn).ToArray())drops.Remove(item);
        foreach(var slot in slots.Keys.Where(s=>slots[s].Count==0).ToArray())slots.Remove(slot);
    }
    internal void Release(string owner,string token)
    {
        if(!Ledger.Release(owner,token))return;
        absentSince.Remove(owner);
        foreach(var ids in slots.Values)ids.RemoveAll(id=>id.Owner==owner);
        foreach(var ids in drops.Values)ids.RemoveAll(id=>id.Owner==owner);
        Save();
    }
    internal bool Conjure(EntityPlayer p,float power)
    {
        if(World.Side!=EnumAppSide.Server || !float.IsFinite(power)||power<=0 || Ledger.Claims.ContainsKey(p.PlayerUID))return false;
        var item=World.GetItem(new AssetLocation("rustboundexpansion:holygrenade-sealed"));if(item==null)return false;
        string token=Guid.NewGuid().ToString("N");if(!Ledger.Reserve(p.PlayerUID,token))return false;
        var stack=new ItemStack(item);stack.Attributes.SetString(Owner,p.PlayerUID);stack.Attributes.SetString(Token,token);stack.Attributes.SetFloat(Power,power);
        if(!p.Player.InventoryManager.TryGiveItemstack(stack)){Ledger.Release(p.PlayerUID,token);return false;}
        foreach(var inventory in p.Player.InventoryManager.Inventories.Values)
            foreach(var slot in inventory)Observe(slot);
        Save();return true;
    }
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(ItemSlot),nameof(ItemSlot.MarkDirty)),prefix:new(typeof(HolyGrenadeSystem),nameof(SlotChanging)));
        harmony.Patch(AccessTools.Method(typeof(ItemSlot),nameof(ItemSlot.TakeOutWhole)),prefix:new(typeof(HolyGrenadeSystem),nameof(BeforeTakeOut)));
        harmony.Patch(AccessTools.Method(typeof(InventoryBase),nameof(InventoryBase.DidModifyItemSlot)),prefix:new(typeof(HolyGrenadeSystem),nameof(InventoryChanging)));
        harmony.Patch(AccessTools.Method(typeof(InventoryBase),nameof(InventoryBase.AfterBlocksLoaded)),postfix:new(typeof(HolyGrenadeSystem),nameof(InventoryLoaded)));
        foreach(var type in new[]{typeof(rustboundmagic.src.common.item.tool.spellcasting.ItemToolStaffRM),typeof(rustboundmagic.src.common.item.tool.spellcasting.ItemToolSpellScrollRM)})
            harmony.Patch(AccessTools.DeclaredMethod(type,"OnHeldInteractStart"),prefix:new(typeof(HolyGrenadeSystem),
                type.Name.Contains("Scroll")?nameof(ScrollStart):nameof(StaffStart)));
    }
    private static bool CanStart(EntityAgent e,string id,ref EnumHandHandling handling)
    {
        if(id!=HolyGrenadeSpell.SpellId || e is not EntityPlayer p)return true;
        if(HolyGrenadeSpell.Allows(p) && (p.World.Side!=EnumAppSide.Server || For(p.World)?.Ledger.Claims.ContainsKey(p.PlayerUID)==false))return true;
        handling=EnumHandHandling.NotHandled;if(p.World.Side==EnumAppSide.Server)ExpansionSpell.NotifyFailure(p);return false;
    }
    private static bool StaffStart(EntityAgent byEntity,ref EnumHandHandling handling)
        =>CanStart(byEntity,byEntity.WatchedAttributes.GetString("entitybehavior-castingtool-currentlyselectedspell-name_rm",""),ref handling);
    private static bool ScrollStart(ItemSlot itemslot,EntityAgent byEntity,ref EnumHandHandling handling)
        =>CanStart(byEntity,itemslot.Itemstack?.Collectible.Code.Path.Split('-').Last()??"",ref handling);
    private static void SlotChanging(ItemSlot __instance)
    {if(__instance.Inventory?.Api?.World is {} w && w.Side==EnumAppSide.Server)For(w)?.Observe(__instance);}
    private static void BeforeTakeOut(ItemSlot __instance)
    {if(__instance.Inventory?.Api?.World is {} w && w.Side==EnumAppSide.Server)For(w)?.Observe(__instance,false);}
    private static void InventoryChanging(InventoryBase __instance,ItemSlot slot)=>SlotChanging(slot);
    private static void InventoryLoaded(InventoryBase __instance)
    {if(__instance.Api?.World is {} w && w.Side==EnumAppSide.Server && For(w) is {} system)foreach(var slot in __instance)system.Observe(slot);}
    public override void Dispose()
    {
        if(server!=null){Save();server.Event.SaveGameLoaded-=Load;server.Event.GameWorldSave-=Save;server.Event.OnEntityLoaded-=Loaded;
            server.Event.OnEntitySpawn-=Loaded;server.Event.OnEntityDespawn-=Despawned;server.Event.PlayerNowPlaying-=PlayerLoaded;server.Event.PlayerDisconnect-=CreatorDisconnected;server.Event.UnregisterGameTickListener(listener);}
        if(World!=null)Worlds.Remove(World);slots.Clear();drops.Clear();flights.Clear();cleaning.Clear();absentSince.Clear();
    }
}

public sealed class HolyGrenadeItem : Item
{
    private const string Action="rbx-grenade-action";
    internal static bool Armed(ItemStack? s)=>s?.Collectible.Code?.Path=="holygrenade-armed";
    public override string GetHeldTpUseAnimation(ItemSlot slot,Entity byEntity)=>null!;
    public override void OnModifiedInInventorySlot(IWorldAccessor world,ItemSlot slot,ItemStack extracted)=>HolyGrenadeSystem.For(world)?.Observe(slot);
    public override void OnGroundIdle(EntityItem e)
    {
        if(e.World.Side!=EnumAppSide.Server)return;
        var system=HolyGrenadeSystem.For(e.World);
        system?.CleanupDrop(e);
        if(e.ShouldDespawn || !Armed(e.Itemstack))return;
        // An armed grenade dropped instead of thrown is still live once released.
        if(system==null || !HolyGrenadeSystem.Identity(e.Itemstack,out var owner,out var token)||!system.Ledger.Valid(owner,token)
            || system.Ledger.Claims[owner].Stage is not ("item" or "unloaded"))return;
        var grenade=Create(e.World,e.Itemstack,e.Pos.XYZ,e.Pos.Motion,owner);
        if(grenade==null)return;
        system.Ledger.Set(owner,token,"flight");system.Save();
        e.World.SpawnEntity(grenade);system.TrackFlight(grenade);system.Ledger.Set(owner,token,"flight",grenade.EntityId);system.Save();
        e.Itemstack=null!;e.Die(EnumDespawnReason.Removed);
    }
    internal static HolyGrenadeEntity? Create(IWorldAccessor world,ItemStack stack,Vec3d position,Vec3d velocity,string thrower)
    {
        var type=world.GetEntityType(new("rustboundexpansion:holygrenade"));
        if(type==null || world.ClassRegistry.CreateEntity(type) is not HolyGrenadeEntity e)return null;
        e.World=world;e.Pos.SetPos(position);e.Pos.Motion.Set(velocity);
        e.WatchedAttributes.SetString(HolyGrenadeSystem.Owner,stack.Attributes.GetString(HolyGrenadeSystem.Owner));
        e.WatchedAttributes.SetString(HolyGrenadeSystem.Token,stack.Attributes.GetString(HolyGrenadeSystem.Token));
        e.WatchedAttributes.SetFloat(HolyGrenadeSystem.Power,stack.Attributes.GetFloat(HolyGrenadeSystem.Power,30));
        e.WatchedAttributes.SetBool("rbx-can-hurt-creatures",world.PlayerByUid(thrower)?.HasPrivilege("attackcreatures")==true);
        e.WatchedAttributes.SetString("rbx-grenade-thrower",thrower);return e;
    }
    public override void OnHeldInteractStart(ItemSlot slot,EntityAgent e,BlockSelection b,EntitySelection s,bool first,ref EnumHandHandling handling)
    {
        handling=EnumHandHandling.PreventDefault;if(!first || slot.Empty)return;
        if(e.World.Side==EnumAppSide.Server && HolyGrenadeSystem.For(e.World)?.CleanupSlot(slot)==true && slot.Empty)return;
        e.Attributes.SetInt(Action,0);e.Attributes.SetInt("aimingCancel",0);
        if(!Armed(slot.Itemstack))
        {
            e.Attributes.SetInt(Action,1);
            if(e.World.Side==EnumAppSide.Server && e is EntityPlayer p)PullPin(p,slot);
            return;
        }
        e.Attributes.SetInt(Action,2);e.Attributes.SetInt("aiming",1);
        if(e.Api!=null)e.StartAnimation("aim");
    }
    internal static bool PullPin(EntityPlayer p,ItemSlot slot)
    {
        var system=HolyGrenadeSystem.For(p.World);
        if(system==null || !HolyGrenadeSystem.Identity(slot.Itemstack,out var owner,out var token)||!system.Ledger.Valid(owner,token))return false;
        var armed=p.World.GetItem(new AssetLocation("rustboundexpansion:holygrenade-armed"));if(armed==null)return false;
        var attrs=slot.Itemstack!.Attributes.Clone();slot.Itemstack=new ItemStack(armed){Attributes=attrs};slot.MarkDirty();system.Observe(slot);
        p.World.PlaySoundAt(new("game:sounds/block/heavymetal-hit"),p.Pos.X,p.Pos.Y+p.LocalEyePos.Y,p.Pos.Z,null,1.5f,12,.35f);
        var type=p.World.GetEntityType(new("rustboundexpansion:grenadepin"));
        if(type!=null && p.World.ClassRegistry.CreateEntity(type) is GrenadePinEntity pin)
        {
            var direction=p.Pos.GetViewVector();pin.Pos.SetPos(p.Pos.XYZ.AddCopy(direction.X*.35,p.LocalEyePos.Y-.25,direction.Z*.35));
            pin.Pos.Motion.Set(.025,.035,-.015);p.World.SpawnEntity(pin);
        }
        return true;
    }
    public override bool OnHeldInteractStep(float seconds,ItemSlot slot,EntityAgent e,BlockSelection b,EntitySelection s)
        =>!slot.Empty && e.Attributes.GetInt(Action)==2 && e.Attributes.GetInt("aimingCancel")==0;
    public override bool OnHeldInteractCancel(float seconds,ItemSlot slot,EntityAgent e,BlockSelection b,EntitySelection s,EnumItemUseCancelReason reason)
    {EndAim(e);if(reason!=EnumItemUseCancelReason.ReleasedMouse)e.Attributes.SetInt("aimingCancel",1);return true;}
    private static void EndAim(EntityAgent e){e.Attributes.SetInt("aiming",0);if(e.Api!=null)e.StopAnimation("aim");}
    public override void OnHeldInteractStop(float seconds,ItemSlot slot,EntityAgent e,BlockSelection b,EntitySelection s)
    {
        EndAim(e);
        if(e.World.Side!=EnumAppSide.Server || e is not EntityPlayer p || !p.Alive || ControlEffects.Banished(p)
            || e.Attributes.GetInt(Action)!=2 || e.Attributes.GetInt("aimingCancel")!=0 || seconds<.15f || !Armed(slot.Itemstack))return;
        e.Attributes.SetInt(Action,0);
        if(!Throw(p,slot,seconds))ExpansionSpell.NotifyFailure(p);
    }
    internal static bool Throw(EntityPlayer p,ItemSlot slot,float seconds)
    {
        var system=HolyGrenadeSystem.For(p.World);
        if(system==null)return RejectThrow(p,"missing server system");
        if(!Armed(slot.Itemstack))return RejectThrow(p,"item is not armed");
        if(!HolyGrenadeSystem.Identity(slot.Itemstack,out var owner,out var token))return RejectThrow(p,"missing conjured item identity");
        if(!system.Ledger.Valid(owner,token))return RejectThrow(p,"expired conjuration");
        // A valid armed grenade can return from unloaded storage without the
        // inventory callbacks that normally reconcile this claim. The held slot
        // is now observed; an existing flight claim still cannot be reclaimed.
        system.Observe(slot);
        if(slot.Empty || !system.Ledger.Valid(owner,token))return RejectThrow(p,"item expired during reconciliation");
        if(system.Ledger.Claims[owner].Stage!="item")return RejectThrow(p,"claim stage "+system.Ledger.Claims[owner].Stage);
        if(!float.IsFinite(seconds))return RejectThrow(p,"invalid aim time");
        var direction=p.Pos.GetViewVector();float speed=.22f+.36f*Math.Clamp(seconds/1.5f,0,1);
        var from=p.Pos.XYZ.AddCopy(direction.X*.35,p.LocalEyePos.Y-.2,direction.Z*.35);
        var velocity=new Vec3d(direction.X*speed,direction.Y*speed+.045,direction.Z*speed);
        var grenade=Create(p.World,slot.Itemstack!,from,velocity,p.PlayerUID);if(grenade==null)return RejectThrow(p,"entity type/factory unavailable");
        // Publish the flight claim before native item removal/SpawnEntity callbacks.
        system.Ledger.Set(owner,token,"flight");
        var stack=slot.TakeOut(1);slot.MarkDirty();
        try{p.World.SpawnEntity(grenade);}
        catch{slot.Itemstack=stack;slot.MarkDirty();system.Ledger.Set(owner,token,"item");system.Save();throw;}
        system.TrackFlight(grenade);system.Ledger.Set(owner,token,"flight",grenade.EntityId);system.Save();
        p.World.PlaySoundAt(new("game:sounds/player/throw"),p.Pos.X,p.Pos.Y,p.Pos.Z,null,false,12,.8f);
        if(p.Api!=null)p.StartAnimation("throw");return true;
    }
    private static bool RejectThrow(EntityPlayer player,string reason)
    {if(player.World.Side==EnumAppSide.Server)player.World.Logger?.Warning("[Crooked Codex] Holy Hand Grenade throw rejected: {0}",reason);return false;}
    public override void GetHeldItemInfo(ItemSlot inSlot,System.Text.StringBuilder dsc,IWorldAccessor world,bool debug)
    {
        base.GetHeldItemInfo(inSlot,dsc,world,debug);
        dsc.AppendLine(Vintagestory.API.Config.Lang.Get("rustboundexpansion:grenade-"+(Armed(inSlot.Itemstack)?"armed":"sealed")));
    }
    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot slot)=>[new(){ActionLangCode="rustboundexpansion:grenade-help-"+(Armed(slot.Itemstack)?"throw":"pin"),MouseButton=EnumMouseButton.Right}];
}

public class HolyGrenadeEntity : Entity
{
    private readonly Vec3d previousMotion=new();
    private readonly GrenadeRest rest=new();
    private bool collidedThisTick,finished;
    public override bool IsInteractable=>false;
    private string Owner=>WatchedAttributes.GetString(HolyGrenadeSystem.Owner,"");
    private string Token=>WatchedAttributes.GetString(HolyGrenadeSystem.Token,"");
    public override void Initialize(EntityProperties props,ICoreAPI api,long chunk)
    {
        base.Initialize(props,api,chunk);previousMotion.Set(Pos.Motion);
        rest.Quiet=Attributes.GetFloat("rbx-quiet");rest.Remaining=Attributes.GetFloat("rbx-wait",-1);
        if(api.Side==EnumAppSide.Server && HolyGrenadeSystem.For(World) is {} system)
        {
            if(!system.Ledger.Valid(Owner,Token) || system.Ledger.Claims[Owner].Stage!="flight"
                || system.Ledger.Claims[Owner].EntityId is var id && id!=0 && id!=EntityId)
            {finished=true;Die(EnumDespawnReason.Removed);return;}
            system.Ledger.Set(Owner,Token,"flight",EntityId);system.Save();
            system.TrackFlight(this);
        }
    }
    public override void OnGameTick(float dt)
    {
        if(World.Side==EnumAppSide.Server && HolyGrenadeSystem.For(World) is {} system && !system.Ledger.Valid(Owner,Token))
        {Dissolve();return;}
        base.OnGameTick(dt);if(ShouldDespawn||finished)return;
        if(World.Side==EnumAppSide.Server)
        {
            bool supported=OnGround||Swimming;
            if(!supported && World.CollisionTester!=null && CollisionBox!=null)
                supported=World.CollisionTester.IsColliding(World.BlockAccessor,CollisionBox,Pos.XYZ.AddCopy(0,-.025,0),true);
            if(rest.Step(dt,supported && Pos.Motion.LengthSq()<.0001,World.Rand)){Explode();return;}
            Attributes.SetFloat("rbx-quiet",rest.Quiet);Attributes.SetFloat("rbx-wait",rest.Remaining);
        }
        if(Pos.Motion.LengthSq()>.0001){Pos.Pitch+=dt*6;Pos.Roll+=dt*3;}
        previousMotion.Set(Pos.Motion);collidedThisTick=false;
    }
    internal void Dissolve(){finished=true;Die(EnumDespawnReason.Removed);}
    public override void OnCollided()
    {
        if(World.Side!=EnumAppSide.Server || collidedThisTick)return;collidedThisTick=true;
        Bounce(Pos.Motion,previousMotion,CollidedHorizontally,CollidedVertically,World.Rand);
        if(previousMotion.LengthSq()>.0025)World.PlaySoundAt(new("game:sounds/thud"),Pos.X,Pos.Y,Pos.Z,null,1.2f,20,.5f);
    }
    internal static void Bounce(Vec3d motion,Vec3d before,bool horizontal,bool vertical,Random random)
    {
        bool blockX=horizontal&&Math.Abs(motion.X)<.000001,blockZ=horizontal&&Math.Abs(motion.Z)<.000001;
        if(horizontal){motion.X=(blockX?-1:1)*before.X*.6;motion.Z=(blockZ?-1:1)*before.Z*.6;}
        if(vertical && before.Y<-.012)
        {
            double retention=.25+random.NextDouble()*.55;
            double x=(horizontal?motion.X:before.X)*retention,z=(horizontal?motion.Z:before.Z)*retention;
            double angle=(random.NextDouble()-.5)*2.4;
            if(random.NextDouble()<.18)angle+=Math.PI;
            // Uneven ends can kick a vertical drop sideways as well as turn a roll.
            double kick=Math.Min(.08,-before.Y*.2)*random.NextDouble();
            double side=random.NextDouble()*Math.PI*2;
            motion.X=x*Math.Cos(angle)-z*Math.Sin(angle)+Math.Cos(side)*kick;
            motion.Z=x*Math.Sin(angle)+z*Math.Cos(angle)+Math.Sin(side)*kick;
            motion.Y=Math.Min(.22,-before.Y*(.18+random.NextDouble()*.6));
        }
        else if(vertical){motion.Y=0;motion.X*=.35;motion.Z*=.35;}
        else if(horizontal)
        {
            double x=motion.X,z=motion.Z,angle=(random.NextDouble()-.5)*.8;
            motion.X=x*Math.Cos(angle)-z*Math.Sin(angle);motion.Z=x*Math.Sin(angle)+z*Math.Cos(angle);
        }
        // Keep corner rebounds on the free side of the wall. Energetic impacts
        // can receive a deliberately unreasonable kick; quiet contacts cannot.
        if(blockX)motion.X=-Math.Sign(before.X)*Math.Abs(motion.X);
        if(blockZ)motion.Z=-Math.Sign(before.Z)*Math.Abs(motion.Z);
        double incoming=before.Length(),length=motion.Length();
        bool active=incoming>.05 && (horizontal || vertical && before.Y<-.012);
        if(active && random.NextDouble()<.4 && length>.000001)
            motion.Mul(Math.Min(1.5,incoming*(1.15+random.NextDouble()*.5))/length);
        else
        {
            double maximum=incoming*.85;
            if(length>maximum && length>0)motion.Mul(maximum/length);
        }
    }
    internal static float Falloff(float damage,double distance)=>float.IsFinite(damage)&&damage>0 && double.IsFinite(distance)
        ?damage*(1-(float)Math.Clamp((distance-1)/5,0,1)):0;
    internal void Explode()
    {
        if(finished||World.Side!=EnumAppSide.Server)return;finished=true;
        var system=HolyGrenadeSystem.For(World);
        if(system==null || !system.Ledger.Valid(Owner,Token)){Die(EnumDespawnReason.Removed);return;}
        try
        {
            World.PlaySoundAt(new("rustboundexpansion:sounds/holygrenade/hallelujah.ogg"),Pos.X,Pos.Y,Pos.Z,null,false,64,1);
            Vfx.Explosion(World,Pos.XYZ.AddCopy(0,.15,0),6);
            var caster=World.PlayerByUid(WatchedAttributes.GetString("rbx-grenade-thrower",Owner))?.Entity;
            Blast(caster);
        }
        finally{system.Release(Owner,Token);Die(EnumDespawnReason.Removed);}
    }
    internal void Blast(EntityPlayer? caster)
    {
        float power=WatchedAttributes.GetFloat(HolyGrenadeSystem.Power,30);
        foreach(var target in World.GetEntitiesAround(Pos.XYZ,6,6).DistinctBy(e=>e.EntityId))
        {
            if(!target.Alive || target.ShouldDespawn || target.Pos.Dimension!=Pos.Dimension || target.GetBehavior<EntityBehaviorHealth>()==null)continue;
            if(caster!=null)
            {if(target!=caster && !CombatRules.Enemy(caster,target))continue;}
            // A saved grenade can outlive its thrower's connection. Never bypass
            // player/pet permissions when the native player context is unavailable.
            else if(!WatchedAttributes.GetBool("rbx-can-hurt-creatures") || target is not EntityAgent
                || target is EntityPlayer || CreatureSupport.IsOwned(target) || ControlEffects.Banished(target))continue;
            double distance=target.SelectionBox?.ToDouble().Translate(target.Pos.X,target.Pos.Y,target.Pos.Z).ShortestDistanceFrom(Pos.X,Pos.Y,Pos.Z)??target.Pos.DistanceTo(Pos);
            float damage=Falloff(power,distance);if(damage<=0)continue;
            var source=caster!=null?CombatRules.Source(caster,"none",EnumDamageType.BluntAttack)
                :new DamageSource{Source=EnumDamageSource.Entity,Type=EnumDamageType.BluntAttack,IgnoreInvFrames=true};
            source.SourceEntity=this;source.DamageTier=2;source.KnockbackStrength=1.5f;
            if(target.ShouldReceiveDamage(source,damage))target.ReceiveDamage(source,damage);
        }
    }
    public override void Die(EnumDespawnReason reason=EnumDespawnReason.Death,DamageSource? source=null)
    {
        if(World?.Side==EnumAppSide.Server && reason!=EnumDespawnReason.Unload && !finished)
        {
            var system=HolyGrenadeSystem.For(World);
            if(system?.Ledger.Claims.GetValueOrDefault(Owner)?.EntityId==EntityId)system.Release(Owner,Token);
        }
        base.Die(reason,source);
    }
}

public sealed class GrenadePinEntity : Entity
{
    private float age;
    public override bool IsInteractable=>false;
    public override void OnGameTick(float dt)
    {
        base.OnGameTick(dt);Pos.Roll+=dt*6;age+=dt;
        RenderColor=ColorUtil.ToRgba((int)(255*Math.Clamp((2.5f-age)/.75f,0,1)),255,255,255);
        if(World.Side!=EnumAppSide.Server)return;
        if(age>=2.5f)Die(EnumDespawnReason.Removed);
    }
}
