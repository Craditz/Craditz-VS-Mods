using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
namespace QuarryViewRecorder;

public sealed class RecorderMod : ModSystem
{
    ClientRecorder? client; ServerRecorder? server;
    public override void StartClientSide(ICoreClientAPI api) => client = new ClientRecorder(api);
    public override void StartServerSide(ICoreServerAPI api) => server = new ServerRecorder(api);
    public override void Dispose() { client?.Dispose(); server?.Dispose(); }
}
[ProtoContract] public sealed class RecorderRequest
{
    [ProtoMember(1)] public bool Arm;
}
[ProtoContract] public sealed class RecorderReply
{
    [ProtoMember(1)] public string State = "";
    [ProtoMember(2)] public string Message = "";
}
