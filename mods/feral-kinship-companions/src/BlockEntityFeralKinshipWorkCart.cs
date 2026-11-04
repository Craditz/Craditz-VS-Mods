#nullable enable

using System;
using System.Linq;
using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>Chunk-owned tool persistence, native inventory networking and tree locks.</summary>
public sealed class BlockEntityFeralKinshipWorkCart : BlockEntityContainer
{
    internal const int CloseToolsPacketId = 1001;
    internal WorkCartAxeInventory Tools = new();
    internal WorkCartQuarryState Quarry = new();
    internal WorkCartMiningState Mining = new();
    internal bool MiningMode;
    internal string RescueNotice = "";
    internal long RescueEpoch;
    internal long NextMiningSurvey, NextMiningDiscovery;
    internal readonly System.Collections.Generic.HashSet<QuarryPoint> MiningReach = new();
    internal System.Collections.Generic.List<CartFinding> Findings = new();
    internal readonly System.Collections.Generic.List<WorkCartQuarryJob> QuarryJobs = new();
    internal WorkCartQuarryJob? QuarryJob => QuarryJobs.Count == 0 ? null : QuarryJobs[0];
    internal long QuarrySupplyWorker;
    internal long QuarrySupplyLease;
    internal long NextQuarryPlanAt;
    internal long NextStairRoutePlan;
    internal int QuarryRemnantColumn;
    internal long NextQuarryDepositRecheck;
    internal long NextQuarryDebrisScan;
    internal readonly System.Collections.Generic.List<long> QuarryDebris = new();
    internal readonly System.Collections.Generic.Dictionary<QuarryPoint, long> QuarryFailedTargets = new();
    internal readonly System.Collections.Generic.Dictionary<long, long> QuarryFailedDebris = new();
    private bool removed;
    private long nextProgressSync;
    public override InventoryBase Inventory => Tools;
    public override string InventoryClassName => "feralkinshipworkcarttools";

    public override void Initialize(ICoreAPI api)
    {
        // Explicitly spawned legacy cart entities can carry an unset/stale Block.
        // The placed block is authoritative once its chunk is available.
        ConfigureForBlock(api.World.BlockAccessor.GetBlock(Pos));
        if (!IsCartBlock(api.World.BlockAccessor.GetBlock(Pos))) ConfigureForBlock(Block);
        base.Initialize(api);
        Tools.SlotModified += OnToolChanged;
        Tools.ReservationChanged = () => MarkDirty(true);
        Tools.AccessCheck = player => api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()
            .CanPlayerUseWorkCartTools(player, Pos);
        if (api.Side == EnumAppSide.Server) RegisterGameTickListener(SyncProgress, 1000);
    }

    private void OnToolChanged(int slot)
    {
        if (Api?.Side == EnumAppSide.Server) MarkDirty(true);
    }

    private void SyncProgress(float dt)
    {
        if ((Tools.ReservedMask == 0 && (!Tools.QuarryMode || !(MiningMode ? Mining.Enabled : Quarry.Enabled))) || Api.World.ElapsedMilliseconds < nextProgressSync) return;
        nextProgressSync = Api.World.ElapsedMilliseconds + 1000;
        MarkDirty(true);
    }

    internal byte[] Snapshot()
    {
        TreeAttribute tree = new();
        ToTreeAttributes(tree);
        return tree.ToBytes();
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetString("cartFindings", JsonSerializer.Serialize(Findings));
        tree.SetBool("miningTools", MiningMode);
        tree.SetString("cartRescueNotice", RescueNotice);
        tree.SetLong("cartRescueEpoch", RescueEpoch);
        if (MiningMode) tree.SetString("miningPlan", Mining.Save());
        tree.SetBool("quarryTools", Tools.QuarryMode);
        if (Tools.QuarryMode) tree.SetString("quarryPlan", Quarry.Save());
        tree.SetInt("reservedTools", Tools.ReservedMask);
        for (int i = 0; i < WorkCartAxeInventory.ToolSlotCount; i++)
        {
            string description = Tools.ReservationAt(i) is WorkCartLoggingPlan plan
                ? $"Tree {plan.Root.X}, {plan.Root.Y}, {plan.Root.Z} · {plan.WorkerHeartbeats.Count} animals · "
                    + (plan.Phase == WorkCartLoggingPhase.Felling ? "Felling"
                        : $"Chopping {(int)(100L * plan.BiteIndex / Math.Max(1, plan.BiteParts.Count))}%")
                : Tools.ReservationAt(i) is WorkCartQuarryJob job
                    ? $"{job.Kind} {job.Target.X}, {job.Target.Y}, {job.Target.Z} · {job.Workers.Count} animals"
                    : string.Empty;
            tree.SetString("toolJob" + i, description);
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        try { Findings = JsonSerializer.Deserialize<System.Collections.Generic.List<CartFinding>>(tree.GetString("cartFindings", "[]")) ?? new(); }
        catch (JsonException) { Findings = new(); }
        Findings = Findings.Where(f => f != null && f.Positions != null).Take(40).ToList();
        foreach (var finding in Findings) finding.Positions = finding.Positions.Take(128).ToList();
        MiningMode = tree.GetBool("miningTools") || Block?.Code?.Path.StartsWith("workcart-mining-", StringComparison.Ordinal) == true;
        if (MiningMode) Mining = WorkCartMiningState.Load(tree.GetString("miningPlan", ""));
        RescueNotice = tree.GetString("cartRescueNotice", "");
        RescueEpoch = tree.GetLong("cartRescueEpoch");
        ConfigureTools(MiningMode || tree.GetBool("quarryTools")
            || Block?.Code?.Path.StartsWith("workcart-quarrying-", StringComparison.Ordinal) == true);
        if (Tools.QuarryMode)
        {
            Quarry = WorkCartQuarryState.Load(tree.GetString("quarryPlan", ""));
            Tools.QuarryState = Quarry;
        }
        // Old placed carts have no inventory. Do not ask InWorldContainer to
        // decode a missing tree. Server-side locks deliberately never reload.
        if (tree.GetTreeAttribute("inventory") != null) base.FromTreeAttributes(tree, worldForResolving);
        if (worldForResolving.Side == EnumAppSide.Client)
        {
            Tools.ClientReservedMask = tree.GetInt("reservedTools") & 15;
            for (int i = 0; i < WorkCartAxeInventory.ToolSlotCount; i++)
                Tools.ClientDescriptions[i] = tree.GetString("toolJob" + i, string.Empty);
            worldForResolving.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()
                ?.RefreshWorkCartToolsDialog(Pos);
        }
    }

    public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
    {
        if (Api?.Side != EnumAppSide.Server) return;
        if (packetid == CloseToolsPacketId)
        {
            player.InventoryManager.CloseInventory(Tools);
            return;
        }
        FeralKinshipCompanionSystem system = Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        if (packetid < 0 || packetid >= 1000 || !Tools.HasOpened(player)
            || !system.CanPlayerUseWorkCartTools(player, Pos)) return;
        // Native handlers consult the slot restrictions for every transfer.
        Tools.InvNetworkUtil.HandleClientPacket(player, packetid, data);
        MarkDirty(true);
    }

    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        // Removal below also handles explosions/replacements. Drop exactly once.
        DropTools();
        // BlockEntityContainer's base would drop again; invoke the registered
        // behavior hooks directly after our guarded drop.
        foreach (BlockEntityBehavior behavior in Behaviors) behavior.OnBlockBroken(byPlayer);
    }

    public override void OnBlockRemoved()
    {
        DropTools();
        base.OnBlockRemoved();
    }

    public override void OnBlockUnloaded()
    {
        ClearQuarryRuntime();
        if (Api?.Side == EnumAppSide.Server)
            Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()?.ClearWorkCartLoggingQueue(Pos);
        base.OnBlockUnloaded();
    }

    private void DropTools()
    {
        if (removed || Api?.Side != EnumAppSide.Server) return;
        removed = true;
        ClearQuarryRuntime();
        Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>()?.ClearWorkCartLoggingQueue(Pos);
        Tools.DropAll(Pos.ToVec3d().Add(0.5, 0.5, 0.5));
    }

    private static bool IsCartBlock(Block? block) => block?.Code?.Domain == "feralkinshipcompanions"
        && block.Code.Path.StartsWith("workcart-", StringComparison.Ordinal);

    internal void ConfigureForBlock(Block? block)
    {
        if (!IsCartBlock(block)) return;
        MiningMode = block!.Code.Path.StartsWith("workcart-mining-", StringComparison.Ordinal);
        ConfigureTools(MiningMode || block.Code.Path.StartsWith("workcart-quarrying-", StringComparison.Ordinal));
    }

    // The server's dialog packet may precede its block exchange packet.
    internal void ConfigureForKind(string kind)
    {
        MiningMode = kind == "mining";
        ConfigureTools(MiningMode || kind == "quarrying");
    }

    private void ConfigureTools(bool quarry)
    {
        Tools.Configure(quarry);
        Tools.QuarryState = quarry ? Quarry : null;
        Tools.MiningState = MiningMode ? Mining : null;
    }

    internal void ClearQuarryRuntime()
    {
        foreach (var job in QuarryJobs.ToArray())
        {
            job.Done = true; Tools.Release(job);
        }
        QuarryJobs.Clear();
        QuarrySupplyWorker = 0; QuarrySupplyLease = 0;
        MiningReach.Clear(); NextMiningSurvey = NextMiningDiscovery = 0;
        QuarryFailedDebris.Clear(); QuarryDebris.Clear(); NextQuarryDebrisScan = 0;
    }
}
