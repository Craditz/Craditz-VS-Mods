#nullable enable
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.Server;

// No client types are eagerly referenced by the server startup path;
// dedicated hosts never resolve these members. Native UI bindings are specific
// to the inspected 1.22.7 host, not a promise about arbitrary engine forks.
namespace FeralKinshipCompanions;

internal static class IntegratedRejectionFrontend
{
    internal const string ErrorText = "Companions could not safely initialize its native save protections. The session was stopped. Keep your backup and send the server and client logs with the exact game/mod versions to the mod author.";
    internal static void QueueClient(object game, ILogger logger)
    {
        try
        {
            Assembly native = typeof(ServerMain).Assembly;
            Type managerType = native.GetType("Vintagestory.Client.ScreenManager", true)!;
            RequiredMethod(managerType, "EnqueueMainThreadTask", new[] { typeof(Action) }).Invoke(null, new object[] { (Action)(() => CompleteClient(native, game, null, logger, 0)) });
            logger.Notification("[Companions Startup:UI] Queued matching client startup rejection handoff.");
        }
        catch (Exception error) { Report(logger, error); }
    }

    private static void CompleteClient(Assembly native, object expectedGame, object? expectedExit, ILogger logger, int waitCount)
    {
        try
        {
            Type managerType = native.GetType("Vintagestory.Client.ScreenManager", true)!;
            int ownerThread = (int)(managerType.GetField("MainThreadId", BindingFlags.Static | BindingFlags.Public)!.GetValue(null) ?? -1);
            if (Environment.CurrentManagedThreadId != ownerThread) throw new InvalidOperationException("Native frontend callback is not on its UI thread.");
            object platform = managerType.GetField("Platform", BindingFlags.Static | BindingFlags.Public)!.GetValue(null) ?? throw new InvalidOperationException("Native client platform is unavailable.");
            logger = Read(platform, "Logger") as ILogger ?? logger;
            object manager = native.GetType("Vintagestory.Client.ClientProgram", true)!.GetField("screenManager", BindingFlags.Static | BindingFlags.Public)!.GetValue(null) ?? throw new InvalidOperationException("Native screen manager is unavailable.");
            object? screen = Read(manager, "CurrentScreen");
            if (screen?.GetType().FullName != "Vintagestory.Client.GuiScreenConnectingToServer" || !SameIdentity(expectedGame, Read(screen, "runningGame")))
            {
                logger.Error("[Companions Startup:UI] Rejected client is no longer owned by the active startup screen; automatic teardown refused.");
                return;
            }
            if (AlreadyExiting(expectedGame)) { logger.Notification("[Companions Startup:UI] Client session already exiting; skipped."); return; }
            bool singleplayer = (bool)(Read(expectedGame, "IsSingleplayer") ?? false);
            if (singleplayer)
            {
                object currentExit = Read(platform, "ServerExitState") ?? throw new MissingMemberException("Integrated server exit identity");
                if (expectedExit == null)
                {
                    expectedExit = currentExit;
                    // Native nonblocking exit request. Never join a worker while
                    // holding the ScreenManager main-thread task queue monitor.
                    MethodInfo exit = platform.GetType().GetMethod("ExitSinglePlayerServer", BindingFlags.Instance | BindingFlags.Public) ?? throw new MissingMethodException("Native integrated exit request");
                    ParameterInfo[] ep = exit.GetParameters();
                    if (ep.Length != 1 || !ep[0].ParameterType.IsEnum || exit.ReturnType != typeof(void)) throw new MissingMethodException("Native integrated exit request signature");
                    exit.Invoke(platform, new[] { Enum.Parse(ep[0].ParameterType, "HardExit") });
                }
                else if (!SameIdentity(expectedExit, currentExit)) { logger.Error("[Companions Startup:UI] Skipped stale client startup exit identity."); return; }
                if ((bool)(Read(platform, "IsServerRunning") ?? false))
                {
                    if (waitCount >= 400) { logger.Error("[Companions Startup:UI] Rejected integrated worker did not retire within the bounded frontend wait; automatic teardown refused."); return; }
                    object capturedExit = expectedExit;
                    RequiredMethod(managerType, "EnqueueCallBack", new[] { typeof(Action), typeof(int), typeof(string) }).Invoke(null, new object[] { (Action)(() => CompleteClient(native, expectedGame, capturedExit, logger, waitCount + 1)), 50, "CompanionsClientStartupRejection" });
                    return;
                }
            }
            // A remote client never requests shutdown of a local server and uses
            // the native multiplayer disconnect title/action.
            TeardownClient(expectedGame, singleplayer, logger);
        }
        catch (Exception error) { Report(logger, error); }
    }

    private static bool AlreadyExiting(object game) => (bool)(Read(game, "disposed") ?? false) || (bool)(Read(game, "exitToMainMenu") ?? false) || (bool)(Read(game, "exitToDisconnectScreen") ?? false);

    private static void TeardownClient(object game, bool singleplayer, ILogger logger)
    {
        Write(game, "disconnectReason", ErrorText);
        Write(game, "disconnectAction", singleplayer ? "disconnectSP" : "disconnect");
        Write(game, "exitReason", "Companions native compatibility rejection");
        MethodInfo destroy = game.GetType().GetMethod("DestroyGameSession", BindingFlags.Instance | BindingFlags.Public) ?? throw new MissingMethodException("Native loading-session cancellation");
        ParameterInfo[] parameters = destroy.GetParameters();
        if (parameters.Length != 2 || parameters[0].ParameterType != typeof(bool) || !parameters[1].ParameterType.IsEnum || destroy.ReturnType != typeof(void)) throw new MissingMethodException("Native session cancellation signature");
        logger.Notification("[Companions Startup:UI] Matched native startup; requesting disconnect screen with HardExit (singleplayer={0}).", singleplayer);
        destroy.Invoke(game, new object[] { true, Enum.Parse(parameters[1].ParameterType, "HardExit") });
        logger.Notification("[Companions Startup:UI] Native client teardown returned; disconnect screen flag={0}.", Read(game, "exitToDisconnectScreen"));
    }
    internal static void Queue(ServerMain server, ILogger logger)
    {
        if (server.IsDedicatedServer) return;
        if (!server.DidExit()) { logger.Error("[Companions Startup:UI] Server has not exited; frontend handoff refused."); return; }
        try
        {
            Assembly native = typeof(ServerMain).Assembly;
            object exit = Read(server, "exitState") ?? throw new MissingFieldException("Native startup exit identity");
            object args = Read(server, "serverStartArgs") ?? throw new MissingFieldException("Native startup arguments");
            MethodInfo enqueue = RequiredMethod(native.GetType("Vintagestory.Client.ScreenManager", true)!, "EnqueueMainThreadTask", new[] { typeof(Action) });
            enqueue.Invoke(null, new object[] { (Action)(() => Complete(native, exit, args, logger, 0)) });
            logger.Notification("[Companions Startup:UI] Queued native integrated startup rejection handoff.");
        }
        catch (Exception error) { Report(logger, error); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Complete(Assembly native, object expectedExit, object expectedArgs, ILogger logger, int waitCount)
    {
        try
        {
            Type managerType = native.GetType("Vintagestory.Client.ScreenManager", true)!;
            int ownerThread = (int)(managerType.GetField("MainThreadId", BindingFlags.Static | BindingFlags.Public)!.GetValue(null) ?? -1);
            if (Environment.CurrentManagedThreadId != ownerThread) throw new InvalidOperationException("Native frontend callback is not on its UI thread.");
            object platform = managerType.GetField("Platform", BindingFlags.Static | BindingFlags.Public)!.GetValue(null) ?? throw new InvalidOperationException("Native client platform is unavailable.");
            logger = Read(platform, "Logger") as ILogger ?? logger;
            if (!SameIdentity(expectedExit, Read(platform, "ServerExitState"))) { logger.Notification("[Companions Startup:UI] Skipped stale startup exit identity."); return; }
            // Stop has returned, but the native server launch worker can still
            // finish asset definitions before its exit branch. Never join that
            // worker from a callback holding the native UI queue monitor.
            if ((bool)(Read(platform, "IsServerRunning") ?? false))
            {
                if (waitCount >= 400) { logger.Error("[Companions Startup:UI] Rejected server worker did not retire within the bounded frontend wait; automatic teardown refused."); return; }
                if (waitCount == 0) logger.Notification("[Companions Startup:UI] Waiting for the rejected native server worker to retire.");
                MethodInfo later = RequiredMethod(managerType, "EnqueueCallBack", new[] { typeof(Action), typeof(int), typeof(string) });
                later.Invoke(null, new object[] { (Action)(() => Complete(native, expectedExit, expectedArgs, logger, waitCount + 1)), 50, "CompanionsStartupRejection" });
                return;
            }
            Type program = native.GetType("Vintagestory.Client.ClientProgram", true)!;
            object manager = program.GetField("screenManager", BindingFlags.Static | BindingFlags.Public)!.GetValue(null) ?? throw new InvalidOperationException("Native screen manager is unavailable.");
            object? screen = Read(manager, "CurrentScreen");
            if (screen?.GetType().FullName != "Vintagestory.Client.GuiScreenConnectingToServer") { logger.Notification("[Companions Startup:UI] Current screen is no longer the matching startup screen; skipped."); return; }
            object? parent = Read(screen, "ParentScreen");
            if (parent?.GetType().FullName != "Vintagestory.Client.GuiScreenRunningGame" || !SameIdentity(expectedArgs, Read(parent, "serverargs"))) { logger.Notification("[Companions Startup:UI] Skipped unrelated startup arguments."); return; }
            object game = Read(screen, "runningGame") ?? throw new InvalidOperationException("Native loading screen has no client session.");
            if (!(bool)(Read(game, "IsSingleplayer") ?? false)) throw new InvalidOperationException("Matched startup session is not integrated singleplayer.");
            if (AlreadyExiting(game)) { logger.Notification("[Companions Startup:UI] Client session already exiting; skipped."); return; }
            TeardownClient(game, true, logger);
        }
        catch (Exception error) { Report(logger, error); }
    }

    internal static bool SameIdentity(object? expected, object? actual) => expected != null && ReferenceEquals(expected, actual);
    private static MethodInfo RequiredMethod(Type type, string name, Type[] parameters) => type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, parameters, null) ?? throw new MissingMethodException(type.FullName, name);
    private static object? Read(object owner, string name)
    {
        for (Type? type = owner.GetType(); type != null; type = type.BaseType)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            FieldInfo? field = type.GetField(name, flags); if (field != null) return field.GetValue(owner);
            PropertyInfo? property = type.GetProperty(name, flags); if (property != null) return property.GetValue(owner);
        }
        throw new MissingMemberException(owner.GetType().FullName, name);
    }
    private static void Write(object owner, string name, string value)
    {
        FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? throw new MissingFieldException(owner.GetType().FullName, name);
        if (field.FieldType != typeof(string)) throw new InvalidOperationException("Native error field type differs: " + name);
        field.SetValue(owner, value);
    }
    private static void Report(ILogger logger, Exception error)
    {
        Exception inner = error is TargetInvocationException invocation ? invocation.InnerException ?? error : error;
        logger.Error("[Companions Startup:UI] Frontend handoff unavailable: {0}: {1}. Server rejection remains in force.", inner.GetType().Name, inner.Message);
    }
}
