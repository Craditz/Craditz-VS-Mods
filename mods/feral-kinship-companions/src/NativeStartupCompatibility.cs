#nullable enable
using System;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;

namespace FeralKinshipCompanions;

internal static class NativeStartupCompatibility
{
    internal static bool Check(ICoreAPI api)
    {
        Assembly native = typeof(WorldChunk).Assembly;
        bool accepted = NativeChunkCompatibility.TryQualify(native, out string reason);
        // A remote client never performs server chunk saves. Review its actual
        // shared/client hooks separately; the server path always stays strict.
        if(!accepted&&api.Side==EnumAppSide.Client&&NativeOptimum013ClientCompatibility.TryQualify(native,out string clientReason)) {
            api.Logger.Notification("[Companions Startup] Native1.22.7 compatibility=qualified for remote client only (Optimum0.3.13); local server/save support is unavailable; {0}",NativeChunkCompatibility.DescribeProvenance(native));
            return true;
        }
        api.Logger.Notification("[Companions Startup] Native1.22.7 compatibility={0}; {1}", accepted ? "qualified" : "rejected: " + reason, NativeChunkCompatibility.DescribeProvenance(native));
        if (accepted) return true;
        Reject(api, reason); return false;
    }
    internal static void Reject(ICoreAPI api, string reason)
    {
        api.Logger.Error("[Companions Startup] Cannot safely initialize native save protections: {0}. Startup is rejected before companion registrations.", reason);
        if (api is ICoreServerAPI && api.World is ServerMain server)
        {
            MethodInfo stop = typeof(ServerMain).GetMethod(nameof(ServerMain.Stop))!;
            stop.Invoke(server, new object?[] { "Companions native compatibility rejection", Enum.Parse(stop.GetParameters()[1].ParameterType, "HardExit"), null, EnumLogType.Notification });
            IntegratedRejectionFrontend.Queue(server, api.Logger);
        }
        else IntegratedRejectionFrontend.QueueClient(api.World, api.Logger);
    }
}
