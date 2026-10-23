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
