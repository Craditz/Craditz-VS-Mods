#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace FeralKinshipCompanions;

internal static class NativeChunkCompatibility
{
    private static readonly Dictionary<Assembly, (bool Accepted, string Reason)> Results = new();
    private static readonly HashSet<Assembly> optimum = new();
    internal static bool UsesOptimum(Assembly native) { lock(Results) return optimum.Contains(native); }
    private static NativeChunkContractSnapshot? expected;
    internal static NativeChunkContractSnapshot Expected
    {
        get
        {
            if (expected != null) return expected;
            using Stream stream = typeof(NativeChunkCompatibility).Assembly.GetManifestResourceStream("FeralKinshipCompanions.NativeChunkContract1227.json") ?? throw new InvalidOperationException("Native compatibility profile is missing");
            return expected = JsonSerializer.Deserialize<NativeChunkContractSnapshot>(stream) ?? throw new InvalidOperationException("Native compatibility profile is invalid");
        }
    }
    internal static bool TryQualify(Assembly native, out string reason)
    {
        lock (Results)
        {
            if (Results.TryGetValue(native, out var prior)) { reason = prior.Reason; return prior.Accepted; }
            bool accepted;
            try {
                var actual=NativeChunkContractReader.Capture(native);
                accepted=NativeChunkContractReader.Compare(Expected,actual,out reason);
                if(!accepted && native.GetType("Vintagestory.Server.ServerSystemLoadAndSaveGame")?.GetField("optimumSaveBatch",BindingFlags.NonPublic|BindingFlags.Instance)!=null) {
                    accepted=NativeOptimumCompatibility.TryQualify(native,actual,out reason);
                    if(accepted)optimum.Add(native);
                }
            }
            catch (Exception error) { accepted = false; reason = "Native contract inspection failed: " + error.GetType().Name + ": " + error.Message; }
            Results[native] = (accepted, reason); return accepted;
        }
    }
    internal static void Require(Assembly native)
    {
        if (!TryQualify(native, out string reason)) throw new InvalidOperationException(reason);
    }
    // Disk identity is evidence only. An unreadable/repacked native file cannot
    // turn a successful loaded-method compatibility result into a rejection.
    internal static string DescribeProvenance(Assembly native)
    {
        try { return "path=" + native.Location + " sha256=" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(native.Location))).ToLowerInvariant(); }
        catch (Exception error) { return "native file fingerprint unavailable: " + error.GetType().Name; }
    }
}
