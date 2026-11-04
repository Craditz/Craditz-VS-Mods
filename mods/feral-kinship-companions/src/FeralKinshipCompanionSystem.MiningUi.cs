#nullable enable
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;
public sealed partial class FeralKinshipCompanionSystem
{
    internal void SendFoxWorkCartMining(FoxWorkCartAssignmentStatePacket state, bool enabled,
        int depth, int length, bool configure = false, bool rescue = false)
        => clientChannel?.SendPacket(new FoxWorkCartAssignmentRequestPacket {
            X = state.X, Y = state.Y, Z = state.Z, Dimension = state.Dimension,
            SetMining = !configure && !rescue, MiningEnabled = enabled, ConfigureMining = configure,
            MiningDepth = depth, MiningLength = length, ResumeRescuedWorkers = rescue });

    private void ApplyMiningRequest(BlockEntityFeralKinshipWorkCart cart, FoxWorkCartAssignmentRequestPacket packet)
    {
        var m = cart.Mining;
        if (m.UnreadablePayload != null) return;
        if (packet.ConfigureMining)
        {
            if (m.Started) { m.Status = "Depth and tunnel length are fixed once mining begins; range upgrades may add probes."; return; }
            if (packet.MiningDepth < 0 || packet.MiningDepth > 40 || packet.MiningLength < 8 || packet.MiningLength > 64)
            { m.Status = "Invalid probe dimensions."; return; }
            if (cart.Pos.Y - packet.MiningDepth - 16 < 1)
            { m.Status = "Reduce depth: allow clearance above the bottom of the world."; return; }
            m.Depth = packet.MiningDepth; m.Length = packet.MiningLength;
            m.Status = $"Ready: {m.Depth} down, {m.Length} main route, side probes every 8 blocks within the Work Cart area.";
        }
        else if (packet.MiningEnabled)
        {
            if (cart.Pos.Y - m.Depth - 16 < 1) { m.Status = "Reduce depth: too close to the bottom of the world."; return; }
            if (m.Blocked.Count > 0) { m.Blocked.Clear(); m.Cursor = 0; cart.NextMiningSurvey = cart.NextMiningDiscovery = 0; }
            m.Start(cart.Pos);
        }
        else
        {
            m.Enabled = false; cart.ClearQuarryRuntime();
            m.Status = "Mining paused. Tools released; deposits, probe progress and access remain saved.";
        }
        cart.MarkDirty(true);
    }
}
