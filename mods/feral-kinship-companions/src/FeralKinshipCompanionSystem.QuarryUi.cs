#nullable enable
using Vintagestory.API.MathTools;

namespace FeralKinshipCompanions;
public sealed partial class FeralKinshipCompanionSystem
{
    internal void SendFoxWorkCartQuarry(FoxWorkCartAssignmentStatePacket state, bool enabled,
        int width, int depth, bool configure = false)
    {
        clientChannel?.SendPacket(new FoxWorkCartAssignmentRequestPacket {
            X = state.X, Y = state.Y, Z = state.Z, Dimension = state.Dimension,
            SetQuarry = !configure, QuarryEnabled = enabled, ConfigureQuarry = configure,
            QuarryWidth = width, QuarryDepth = depth });
    }
    private string ApplyQuarryRequest(BlockEntityFeralKinshipWorkCart cart, FoxWorkCartAssignmentRequestPacket packet)
    {
        var q = cart.Quarry;
        if (q.UnreadablePayload != null) return q.Status;
        if (packet.ConfigureQuarry)
        {
            if (q.Started) return "This quarry's footprint and depth were fixed when work began.";
            if (packet.QuarryWidth < 7 || packet.QuarryWidth > 21 || packet.QuarryWidth % 2 == 0
                || packet.QuarryDepth < 1 || packet.QuarryDepth > 40) return "Invalid quarry dimensions.";
            q.Width = packet.QuarryWidth; q.Depth = packet.QuarryDepth;
            q.Status = $"Ready: {q.Width}×{q.Width}, {q.Depth} layers, always south of the cart.";
        }
        else if (packet.QuarryEnabled)
        {
            if (q.Stage == "complete") return q.Status;
            if (!q.Started && cart.Pos.Y - q.Depth - 4 < 1) return "Reduce depth: the staircase would reach the bottom of the world.";
            q.Start(cart.Pos);
            q.Status = q.Stage == "survey" ? "Surveying the south quarry footprint." : "Quarrying resumed.";
        }
        else
        {
            q.Enabled = false; cart.ClearQuarryRuntime();
            q.Status = "Paused. Picks released; the saved quarry and staircase remain in place.";
        }
        cart.MarkDirty(true);
        return q.Status;
    }
}
