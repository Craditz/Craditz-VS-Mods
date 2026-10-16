using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;

namespace AnimalicaBodyTools.HarmonyPatches;

internal sealed class HarmonyPatchBootstrap
{
    private const string HarmonyId = "animalicabodytools.emptyhand.directactionpoc";
    private const string RegistryKey = "animalicabodytools.harmony-lifetime.v1";
    private const string ResolverKey = RegistryKey + ".method-source";
    private static readonly MethodInfo ResolverTarget = AccessTools.Method(typeof(AccessTools), "GetMethodByModuleAndToken", new[] { typeof(string), typeof(int) });

    private readonly ICoreAPI api;
    private bool started;
    public bool IsStarted => started;

    public HarmonyPatchBootstrap(ICoreAPI api)
    {
        this.api = api;
    }

    public void Start()
    {
        // Engine methods and Harmony are shared by both sides and can also be
        // shared by separate mod load contexts. BCL-only AppDomain state and an
        // interned gate provide one process lifetime, rather than one static per
        // mod assembly load context. Harmony also caches context-specific
        // __state types, so pin one implementation assembly for the process,
        // even between sessions; only the active hooks follow the side leases.
        lock (string.Intern(RegistryKey))
        {
            if (started) return;
            var state = AppDomain.CurrentDomain.GetData(RegistryKey) as Dictionary<string, object?>;
            if (state == null)
            {
                state = new Dictionary<string, object?> { ["users"] = 0 };
                AppDomain.CurrentDomain.SetData(RegistryKey, state);
            }
            int users = (int)state["users"]!;
            string fingerprint = typeof(HarmonyPatchBootstrap).Assembly.ManifestModule.ModuleVersionId.ToString("D");
            if (state.TryGetValue("build", out object? knownBuild) && !Equals(knownBuild, fingerprint))
                throw new InvalidOperationException("Different Body Tools builds cannot share active engine patches. Restart with the same package on both sides.");
            if (!state.ContainsKey("assembly"))
            {
                state["assembly"] = typeof(HarmonyPatchBootstrap).Assembly;
                state["build"] = fingerprint;
                AppDomain.CurrentDomain.SetData(ResolverKey, new object[] { state["assembly"]!, fingerprint });
            }

            Harmony? installed = null;
            try
            {
                if (users == 0)
                {
                    if (Harmony.GetAllPatchedMethods().Any(method => Harmony.GetPatchInfo(method)?.Owners.Contains(HarmonyId) == true))
                        throw new InvalidOperationException("Body Tools patches exist without an active lifetime record. Restart the process before loading this build.");
                    installed = new Harmony(HarmonyId);
                    // Installed Harmony serializes PatchMethod by module GUID
                    // and token, which cannot distinguish identical assemblies
                    // in different load contexts. Canonicalize only this pinned
                    // mod build before installing or rebuilding its hooks.
                    var sourceAssembly = (Assembly)state["assembly"]!;
                    var resolverPrefix = sourceAssembly.GetType(typeof(HarmonyPatchBootstrap).FullName!)!
                        .GetMethod(nameof(ResolveOwnedPatchMethod), BindingFlags.Static | BindingFlags.NonPublic)!;
                    installed.Patch(ResolverTarget, prefix: new HarmonyMethod(resolverPrefix));
                    installed.PatchAll(sourceAssembly);
                }
                api.World.Logger.Notification("[animalicabodytools] Targeted wearable empty-hand Harmony patches active with cached equipped-tool resolution.");
            }
            catch
            {
                // PatchAll can fail after earlier classes have registered. Do
                // not leave partial hooks or acquire a lease on a failed Start.
                if (installed != null) RemoveOwnedPatches(installed);
                throw;
            }
            if (installed != null)
            {
                state["harmony"] = installed;
            }
            state["users"] = users + 1;
            started = true;
        }
    }

    public void Dispose()
    {
        lock (string.Intern(RegistryKey))
        {
            if (!started) return;
            var state = (Dictionary<string, object?>)AppDomain.CurrentDomain.GetData(RegistryKey)!;
            int users = (int)state["users"]! - 1;
            if (users == 0)
            {
                RemoveOwnedPatches((Harmony)state["harmony"]!);
                state.Remove("harmony");
            }
            state["users"] = users;
            started = false;
        }
    }

    private static void RemoveOwnedPatches(Harmony harmony)
    {
        // Remove strict transpilers first so a failed partial registration
        // cannot rerun them while rolling back other owned patch kinds.
        foreach (var method in Harmony.GetAllPatchedMethods().ToArray())
            if (Harmony.GetPatchInfo(method)?.Transpilers.Any(patch => patch.owner == HarmonyId) == true)
                harmony.Unpatch(method, HarmonyPatchType.Transpiler, HarmonyId);
        // Work from the shared engine's current method records, including
        // finalizers installed by a different mod load context. UnpatchAll's
        // convenience traversal left hooks behind in the cross-context reload
        // regression, so remove all owned kinds explicitly and verify cleanup.
        foreach (var method in Harmony.GetAllPatchedMethods().ToArray())
            if (!method.Equals(ResolverTarget) && Harmony.GetPatchInfo(method)?.Owners.Contains(HarmonyId) == true)
                harmony.Unpatch(method, HarmonyPatchType.All, HarmonyId);
        // Keep canonical resolution through all other unpatch operations,
        // including their private state signatures, then remove it last.
        harmony.Unpatch(ResolverTarget, HarmonyPatchType.All, HarmonyId);
        if (Harmony.GetAllPatchedMethods().Any(method => Harmony.GetPatchInfo(method)?.Owners.Contains(HarmonyId) == true))
            throw new InvalidOperationException("Body Tools could not remove all owned engine patches; restart the process before reloading.");
    }

    private static bool ResolveOwnedPatchMethod(string __0, int __1, ref MethodInfo __result)
    {
        if (AppDomain.CurrentDomain.GetData(ResolverKey) is object[] { Length: 2 } source
            && source[0] is Assembly assembly && source[1] is string moduleId
            && string.Equals(__0, moduleId, StringComparison.OrdinalIgnoreCase)
            && assembly.ManifestModule.ResolveMethod(__1) is MethodInfo method)
        {
            __result = method;
            return false;
        }
        return true;
    }
}
