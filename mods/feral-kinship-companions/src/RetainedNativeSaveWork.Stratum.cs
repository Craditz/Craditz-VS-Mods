#nullable enable
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
namespace FeralKinshipCompanions;
internal static partial class RetainedNativeSaveWork
{
    private static FieldInfo? stratumDropping;
    private static Action<object,int>? setStratumDropping;
    private static void ConfigureStratumDrop(Assembly native) {
        stratumDropping=null;setStratumDropping=null;
        if(!NativeChunkCompatibility.UsesStratum(native))return;
        Type request=native.GetType("Vintagestory.Server.ChunkColumnLoadRequest",true)!;
        stratumDropping=AccessTools.Field(request,"stratumDropping");
        if(stratumDropping==null||stratumDropping.FieldType!=typeof(int)||stratumDropping.IsStatic)
            throw new InvalidOperationException("Qualified Stratum drop flag unavailable");
        var method=new DynamicMethod("ReleaseStratumRequestDrop",typeof(void),new[]{typeof(object),typeof(int)},typeof(RetainedNativeSaveWork).Module,true);
        var il=method.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Castclass,request);il.Emit(OpCodes.Ldflda,stratumDropping);il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call,typeof(Volatile).GetMethod(nameof(Volatile.Write),new[]{typeof(int).MakeByRefType(),typeof(int)})!);il.Emit(OpCodes.Ret);
        setStratumDropping=(Action<object,int>)method.CreateDelegate(typeof(Action<object,int>));
    }
    private static int ClaimStratumDrop(ref int flag,int value,object request) {
        if(value!=1||stratumDropping==null)throw new InvalidOperationException("Unexpected Stratum drop claim");
        var receipt=CaptureRequest(request);
        if(receipt.StratumDropClaimed)throw new InvalidOperationException("Repeated Stratum request drop claim");
        // A prior owner is never overwritten. Native pins observe this flag
        // until the original DB calls and deferred request retirement finish.
        int prior=Interlocked.CompareExchange(ref flag,1,0);
        if(prior!=0)RetainedNativeSuspension.RefuseNativeSaveWork("Stratum request drop already owned");
        receipt.StratumDropClaimed=true;return prior;
    }
    private static void DeferStratumDropClear(ref int flag,int value,object request) {
        if(value!=0||!CaptureRequest(request).StratumDropClaimed)
            throw new InvalidOperationException("Unexpected Stratum request drop release");
        // The generation finalizer releases the same original request after
        // acknowledgement or retry restoration, including the pinned skip path.
    }
    private static bool StratumDropUnchanged(GenerationReceipt receipt)
        =>!receipt.StratumDropClaimed||stratumDropping!=null&&(int)stratumDropping.GetValue(receipt.Request)! == 1;
    private static void ReleaseStratumDrops(GenerationLease lease) {
        foreach(var receipt in lease.Requests.Values.Where(r=>r.StratumDropClaimed))
            setStratumDropping!(receipt.Request,0);
    }
}
