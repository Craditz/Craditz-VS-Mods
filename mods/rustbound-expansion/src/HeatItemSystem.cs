using System.Reflection.Emit;
using HarmonyLib;
using ProtoBuf;
using rustboundmagic.src.common.item.tool.spellcasting;
using rustboundmagic.src.system.effects;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace RustboundExpansion;

[ProtoContract]
public sealed class HeatChoice
{
    [ProtoMember(1)] public string Token { get; set; } = "";
    [ProtoMember(2)] public int Index { get; set; }
    [ProtoMember(3)] public bool Cancel { get; set; }
}
[ProtoContract]
public sealed class HeatCandidate
{
    [ProtoMember(1)] public string Inventory { get; set; } = "";
    [ProtoMember(2)] public int Slot { get; set; }
    [ProtoMember(3)] public int ItemId { get; set; }
    [ProtoMember(4)] public bool Block { get; set; }
    [ProtoMember(5)] public int Count { get; set; }
    [ProtoMember(6)] public float Current { get; set; }
    [ProtoMember(7)] public float Result { get; set; }
    [ProtoMember(8)] public float Cap { get; set; }
}
[ProtoContract]
public sealed class HeatOffer
{
    [ProtoMember(1)] public string Token { get; set; } = "";
    [ProtoMember(2)] public HeatCandidate[] Items { get; set; } = [];
    [ProtoMember(3)] public int Mana { get; set; }
    [ProtoMember(4)] public float TotalHeat { get; set; }
    [ProtoMember(5)] public bool Closed { get; set; }
}

public sealed class HeatItemSystem : ModSystem
{
    private const string Channel = "rustboundexpansion-heatitem";
    internal const int LifetimeMs = 30000;
    private static readonly Dictionary<IWorldAccessor, HeatItemSystem> Worlds = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Session> pending = new();
    private ICoreServerAPI? server;
    private ICoreClientAPI? client;
    private IWorldAccessor? world;
    private long listener;
    private HeatItemDialog? dialog;
    internal Action<EntityPlayer, HeatOffer>? OfferObserver;
    internal int PendingCount => pending.Count;
    private sealed record Target(IInventory Inventory, int Slot, ItemStack Stack, int Count, int ItemId, EnumItemClass Class);
    private sealed record Session(string Token, EntityPlayer Player, int Dimension, long End, HeatItemSpell Spell,
        float Heat, int Cost, Target[] Targets, ItemSlot? CastSlot, ItemStack? CastStack, int CastCount, bool Scroll)
    {
        internal Action? ScrollCharge;
    }

    internal static HeatItemSystem? For(IWorldAccessor world) => Worlds.GetValueOrDefault(world);
    internal void Bind(IWorldAccessor value) { world = value; Worlds[value] = this; }
    public override void Start(ICoreAPI api) => api.Network.RegisterChannel(Channel).RegisterMessageType<HeatOffer>().RegisterMessageType<HeatChoice>();
    public override void StartServerSide(ICoreServerAPI api)
    {
        server = api; Bind(api.World);
        api.Network.GetChannel(Channel).SetMessageHandler<HeatChoice>((from, packet) => Select(from.Entity, packet));
        listener = api.Event.RegisterGameTickListener(_ => Expire(), 250);
        api.Event.PlayerDisconnect += Disconnect;
        api.Event.OnEntityDeath += Death;
    }
    public override void StartClientSide(ICoreClientAPI api)
    {
        client = api;
        api.Network.GetChannel(Channel).SetMessageHandler<HeatOffer>(Receive);
        api.Event.LeaveWorld += CloseClient;
    }

    internal static IInventory[] PersonalInventories(EntityPlayer player)
    {
        var manager = player.Player?.InventoryManager;
        if (manager == null) return [];
        // GetOwnInventory resolves canonical player-owned roots; opened chests,
        // creative catalogues and arbitrary packet-supplied IDs are never used.
        return new[] { manager.GetOwnInventory("hotbar"), manager.GetOwnInventory("backpack") }
            .Where(i => i != null).Distinct(ReferenceEqualityComparer.Instance).Cast<IInventory>().ToArray();
    }

    private static bool CanAct(EntityPlayer player) => player.Alive && !player.ShouldDespawn && player.DespawnReason == null
        && !ControlEffects.Banished(player) && !new StatusEffectsRM().IsEffectActive(player, "stasis");

    internal bool Begin(EntityPlayer player, HeatItemSpell spell, ItemSlot castSlot, bool scroll)
    {
        if (world?.Side != EnumAppSide.Server || player.World != world) return false;
        Remove(player.PlayerUID, true);
        if (!CanAct(player)) return false;
        float heat = spell.Heating(player);
        int cost = scroll ? 0 : spell.GetModifiedManaCost(player, spell.Id);
        if (!float.IsFinite(heat) || heat <= 0 || cost < 0 || Mana(player) < cost) return false;
        var targets = new List<Target>();
        var entries = new List<HeatCandidate>();
        foreach (var inventory in PersonalInventories(player))
        {
            for (int slotId = 0; slotId < inventory.Count; slotId++)
            {
                var slot = inventory[slotId];
                if (slot?.Itemstack is not { } stack || !ItemHeating.Preview(world,slot,heat,out var preview)) continue;
                targets.Add(new(inventory, slotId, stack, stack.StackSize, stack.Id, stack.Class));
                entries.Add(new() { Inventory = inventory.InventoryID, Slot = slotId, ItemId = stack.Id,
                    Block = stack.Class == EnumItemClass.Block, Count = stack.StackSize,
                    Current = preview.Current, Result = preview.Result, Cap = preview.Cap });
            }
        }
        if (targets.Count == 0) return false;
        string token = Guid.NewGuid().ToString("N");
        pending[player.PlayerUID] = new(token, player, player.Pos.Dimension, world.ElapsedMilliseconds + LifetimeMs,
            spell, heat, cost, targets.ToArray(), castSlot, castSlot?.Itemstack, castSlot?.StackSize ?? 0, scroll);
        Send(player, new() { Token = token, Items = entries.ToArray(), Mana = cost, TotalHeat = heat });
        return true;
    }

    internal bool Select(EntityPlayer player, HeatChoice choice)
    {
        if (world?.Side != EnumAppSide.Server || player.World != world || choice.Token?.Length != 32
            || !pending.TryGetValue(player.PlayerUID, out var state) || state.Token != choice.Token) return false;
        // One authoritative result, including failures/cancel; replay cannot heat
        // twice and stale packets cannot invalidate a newer casting session.
        pending.Remove(player.PlayerUID);
        Send(player, new() { Token = state.Token, Closed = true });
        if (choice.Cancel) return false;
        bool Fail() { ExpansionSpell.NotifyFailure(player); return false; }
        if (state.Player != player || !CanAct(player) || world.ElapsedMilliseconds >= state.End
            || player.Pos.Dimension != state.Dimension || choice.Index < 0 || choice.Index >= state.Targets.Length
            || Mana(player) < state.Cost) return Fail();
        var target = state.Targets[choice.Index];
        if (!PersonalInventories(player).Any(i => ReferenceEquals(i, target.Inventory))
            || target.Slot < 0 || target.Slot >= target.Inventory.Count) return Fail();
        var slot = target.Inventory[target.Slot];
        if (slot?.Itemstack == null || !ReferenceEquals(slot.Itemstack, target.Stack) || slot.StackSize != target.Count
            || slot.Itemstack.Id != target.ItemId || slot.Itemstack.Class != target.Class
            || !ItemHeating.Preview(world,slot,state.Heat,out var preview)) return Fail();
        if (state.Scroll && (state.CastSlot == null || !ReferenceEquals(state.CastSlot.Itemstack, state.CastStack)
            || state.CastSlot.StackSize != state.CastCount)) return Fail();
        slot.Itemstack.Collectible.SetTemperature(world, slot.Itemstack, preview.Result);
        slot.MarkDirty();
        state.Spell.Pay(player, state.Cost);
        state.ScrollCharge?.Invoke();
        world.PlaySoundAt(state.Spell.SoundAssetLocationSpellCastingSuccessful, player.Pos.X, player.Pos.Y, player.Pos.Z, null, 1, 16);
        return true;
    }

    internal void ArmScroll(EntityPlayer player, ItemSlot slot, Action charge)
    {
        if (pending.TryGetValue(player.PlayerUID, out var state) && state.Scroll && ReferenceEquals(state.CastSlot, slot))
            state.ScrollCharge = charge;
    }
    internal void Expire()
    {
        if (world == null) return;
        foreach (var (uid, state) in pending.ToArray())
            if (world.ElapsedMilliseconds >= state.End || !CanAct(state.Player) || state.Player.Pos.Dimension != state.Dimension)
                Remove(uid, true);
    }
    private static int Mana(EntityPlayer player) => player.WatchedAttributes.GetInt("entitybehavior-resource-currentmana_rm");
    private void Send(EntityPlayer player, HeatOffer offer)
    {
        OfferObserver?.Invoke(player, offer);
        if (player.Player is IServerPlayer recipient) server?.Network.GetChannel(Channel).SendPacket(offer, recipient);
    }
    private void Remove(string uid, bool notify)
    {
        if (pending.Remove(uid, out var state) && notify) Send(state.Player, new() { Token = state.Token, Closed = true });
    }
    private void Disconnect(IServerPlayer player) => Remove(player.PlayerUID, false);
    private void Death(Entity entity, DamageSource source) { if (entity is EntityPlayer player) Remove(player.PlayerUID, true); }
    private void Receive(HeatOffer offer)
    {
        if (client == null) return;
        if (offer.Closed) { if (dialog?.Token == offer.Token) CloseClient(); return; }
        if (offer.Token.Length != 32 || offer.Items.Length == 0) return;
        CloseClient();
        dialog = new HeatItemDialog(client, offer, packet => client.Network.GetChannel(Channel).SendPacket(packet));
        dialog.TryOpen();
    }
    private void CloseClient() { dialog?.CloseFromServer(); dialog?.Dispose(); dialog = null; }
    public override void Dispose()
    {
        CloseClient(); pending.Clear();
        if (world != null && Worlds.GetValueOrDefault(world) == this) Worlds.Remove(world);
        if (server != null) { server.Event.UnregisterGameTickListener(listener); server.Event.PlayerDisconnect -= Disconnect; server.Event.OnEntityDeath -= Death; }
        if (client != null) client.Event.LeaveWorld -= CloseClient;
    }

    internal static void Install(Harmony harmony) => harmony.Patch(
        AccessTools.DeclaredMethod(typeof(ItemToolSpellScrollRM), nameof(ItemToolSpellScrollRM.OnHeldInteractStop)),
        transpiler: new(typeof(HeatItemSystem), nameof(ScrollConsumption)));

    private static void ScrollDamage(CollectibleObject item, IWorldAccessor world, Entity player, ItemSlot slot, int amount, bool dispose)
    {
        if (player is EntityPlayer caster && item is ItemToolSpellScrollRM scroll
            && scroll.SpellName == HeatItemSpell.SpellId)
        {
            For(world)?.ArmScroll(caster, slot, () => item.DamageItem(world, player, slot, amount, dispose));
            return;
        }
        item.DamageItem(world, player, slot, amount, dispose);
    }
    private static IEnumerable<CodeInstruction> ScrollConsumption(IEnumerable<CodeInstruction> instructions)
    {
        int count = 0;
        var method = AccessTools.Method(typeof(CollectibleObject), nameof(CollectibleObject.DamageItem));
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(method))
            {
                count++;
                yield return CodeInstruction.Call(typeof(HeatItemSystem), nameof(ScrollDamage)).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
            }
            else yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException("Unsupported Rustbound spell-scroll consumption layout.");
    }
}
