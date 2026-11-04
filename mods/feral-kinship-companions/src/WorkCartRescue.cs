#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

internal sealed class WorkCartRescueWatch
{
    internal long Since, LastTravel, LastSample;
    internal int Failures;
    internal bool ReturnRequested;
    internal Vec3d Position = new(), Goal = new();
    internal double BestDistance = double.PositiveInfinity;
    internal void Sample(long now, Vec3d actual)
    {
        double remaining = actual.DistanceTo(Goal);
        if (!double.IsFinite(BestDistance)) { BestDistance = remaining; return; }
        if (remaining > BestDistance - .75) return;
        BestDistance = remaining; Since = now; Failures = 0;
    }
    internal string Cart = "", Task = "";
    internal static bool Due(long now, long since, long lastTravel, int failures)
        => failures >= 2 && now >= since && now - since >= 60000 && now - lastTravel <= 30000;
    internal static bool Repeat(long nowUtc, long lastUtc)
        => lastUtc > 0 && nowUtc >= lastUtc && nowUtc - lastUtc <= 300000;
}

public sealed partial class FeralKinshipCompanionSystem
{
    private const string CartRescuePausedKey = "feralKinshipCartRescuePaused";
    private const string CartRescueLastKey = "feralKinshipCartRescueLastUtc";
    private const string CartRescueCartKey = "feralKinshipCartRescueCart";
    private readonly Dictionary<long, WorkCartRescueWatch> cartRescueWatches = new();
    private readonly Dictionary<long, (long Since, double BestDistance)> cartIdleReturnChecks = new();

    internal bool IsCartRescuePaused(Entity fox, BlockPos cart)
        => fox.WatchedAttributes.GetBool(CartRescuePausedKey)
            && fox.WatchedAttributes.GetString(CartRescueCartKey) == WorkCartPositionKey(cart)
            && (serverApi?.World.BlockAccessor.GetBlockEntity(cart) is not BlockEntityFeralKinshipWorkCart block
                || fox.WatchedAttributes.GetLong("feralKinshipCartRescueEpoch") == block.RescueEpoch);

    internal void ObserveCartNavigation(Entity fox, string task, bool failed = false, bool arrived = false, Vec3d? goal = null)
    {
        if (serverApi == null || !CartNavigationKind(task)
            || !TryGetAssignedWorkCart(fox, false, out var record, out var pos, false)
            || pos == null || record?.Kind is not ("mining" or "quarrying" or "logging" or "generic")
            || ((record.Kind is "logging" or "generic") && !record.LoggingEnabled)) return;
        if (arrived) { cartRescueWatches.Remove(fox.EntityId); return; }
        long now = serverApi.World.ElapsedMilliseconds;
        string key = WorkCartPositionKey(pos);
        if (!cartRescueWatches.TryGetValue(fox.EntityId, out var watch) || watch.Cart != key
            || now - watch.LastTravel > 30000 || now < watch.LastTravel)
        {
            watch = new() { Since = now, Position = fox.Pos.XYZ.Clone(), Cart = key, Goal = (goal ?? pos.ToVec3d()).Clone() };
            cartRescueWatches[fox.EntityId] = watch;
        }
        if (goal != null && watch.Goal.SquareDistanceTo(goal) > .25)
        { watch.Goal = goal.Clone(); watch.BestDistance = fox.Pos.XYZ.DistanceTo(goal); }
        watch.Sample(now, fox.Pos.XYZ);
        watch.LastTravel = now; watch.Task = task;
        if (failed) watch.Failures++;
    }

    internal static bool CartRescueIdleTask(string task) => task is "AiTaskFeralKinshipPackIdle" or "AiTaskFeralKinshipMoodIdle" or "AiTaskLookAround" or "AiTaskIdle" or "AiTaskWander";
    internal static bool CartNavigationKind(string task) => task is nameof(AiTaskFeralKinshipLogging)
        or nameof(AiTaskFeralKinshipQuarry) or nameof(AiTaskFeralKinshipFetchDroppedItem) or nameof(AiTaskFeralKinshipCartRecovery);

    private static bool CartReturnEnabled(BlockEntityFeralKinshipWorkCart c, FoxWorkCartRecord r)
        => r.Kind == "mining" ? c.Mining.Enabled : r.Kind == "quarrying" ? c.Quarry.Enabled || c.Quarry.Started && c.Quarry.Stage == "complete" : r.LoggingEnabled;

    internal bool TryCartRecoveryGoal(Entity fox, out Vec3d? goal)
    {
        goal = null;
        if (serverApi == null || !cartRescueWatches.TryGetValue(fox.EntityId, out var watch)
            || (!watch.ReturnRequested && watch.Failures == 0) || serverApi.World.ElapsedMilliseconds - watch.Since < 12000
            || !fox.Alive || IsFoxIncapacitated(fox) || IsFoxAwayFromWorld(fox)
            || GetCompanionActivityMode(fox) != CompanionActivityMode.AtEase || ShouldFoxSeekDenShelter(fox)
            || !TryGetAssignedWorkCart(fox, false, out var record, out var pos, false) || pos == null
            || IsCartRescuePaused(fox, pos) || watch.Cart != WorkCartPositionKey(pos)) return false;
        var cart = EnsureWorkCartTools(pos);
        if (cart == null || (!CartReturnEnabled(cart, record!))) return false;
        return TryGetFoxStorageApproachTarget(fox, pos, out goal);
    }

    private void MaintainCartRescue(Entity[] companions)
    {
        if (serverApi == null || packRepository?.Loaded != true) return;
        long now = serverApi.World.ElapsedMilliseconds;
        var ids = companions.Select(e => e.EntityId).ToHashSet();
        foreach (long stale in cartIdleReturnChecks.Keys.Where(id => !ids.Contains(id)).ToArray()) cartIdleReturnChecks.Remove(stale);
        // A worker can become stranded immediately after a successful cut,
        // before another job has a navigable approach. Request a REAL return
        // journey after settled idle; only its actual failures can earn rescue.
        foreach (var fox in companions.Where(e => !cartRescueWatches.ContainsKey(e.EntityId)))
        {
            var active = fox.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.ActiveTasksBySlot;
            if (!fox.Alive || IsFoxIncapacitated(fox) || IsFoxAwayFromWorld(fox) || IsCompanionOwnerOffline(fox)
                || GetCompanionActivityMode(fox) != CompanionActivityMode.AtEase || ShouldFoxSeekDenShelter(fox)
                || active?.Any(t => t != null && !CartRescueIdleTask(t.GetType().Name)) == true
                || !TryGetAssignedWorkCart(fox, false, out var r, out var p, false) || p == null
                || fox.Pos.SquareDistanceTo(p.ToVec3d()) < 25 || IsCartRescuePaused(fox,p))
            { cartIdleReturnChecks.Remove(fox.EntityId); continue; }
            var c = EnsureWorkCartTools(p);
            bool enabled = c != null && CartReturnEnabled(c,r!);
            bool finished = c != null && (r!.Kind == "mining" ? c.Mining.Mode == "complete" : c.Quarry.Stage == "complete");
            if (!enabled || !finished && c!.Tools.FindAvailable(0) < 0 && fox.WatchedAttributes.GetItemstack(QuarryCarryKey) == null)
            { cartIdleReturnChecks.Remove(fox.EntityId); continue; }
            double remaining = fox.Pos.XYZ.DistanceTo(p.ToVec3d());
            if (!cartIdleReturnChecks.TryGetValue(fox.EntityId,out var idle) || remaining <= idle.BestDistance - .75)
            { cartIdleReturnChecks[fox.EntityId] = (now,remaining); continue; }
            if (now-idle.Since < 30000) continue;
            cartRescueWatches[fox.EntityId] = new() { Since=now,LastTravel=now,Position=fox.Pos.XYZ.Clone(),
                Cart=WorkCartPositionKey(p),Goal=p.ToVec3d(),BestDistance=remaining,ReturnRequested=true,Task="Cart return check" };
            cartIdleReturnChecks.Remove(fox.EntityId);
        }
        foreach (long stale in cartRescueWatches.Where(p => !ids.Contains(p.Key) || now - p.Value.LastTravel > 30000)
            .Select(p => p.Key).ToArray()) cartRescueWatches.Remove(stale);
        foreach (var fox in companions)
        {
            if (!cartRescueWatches.TryGetValue(fox.EntityId, out var watch)) continue;
            if (!fox.Alive || fox.State != EnumEntityState.Active || !IsTamedFox(fox)
                || IsFoxIncapacitated(fox) || IsFoxAwayFromWorld(fox) || fox.Teleporting
                || fox is EntityAgent mounted && mounted.MountedOn != null
                || GetCompanionActivityMode(fox) != CompanionActivityMode.AtEase || ShouldFoxSeekDenShelter(fox)
                || !TryGetAssignedWorkCart(fox, false, out var record, out var pos, false) || pos == null
                || WorkCartPositionKey(pos) != watch.Cart || IsCartRescuePaused(fox, pos))
            { cartRescueWatches.Remove(fox.EntityId); continue; }
            var tasks = fox.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.ActiveTasksBySlot;
            // An actual meal, sleep or unrelated command invalidates a stale
            // failure watch. Successful arrival clears it in Navigation.Cancel.
            if (tasks?.Any(t => t is AiTaskFeralKinshipSeekFood or AiTaskFeralKinshipRestAtDen
                or AiTaskFeralKinshipCommandRest) == true)
            { cartRescueWatches.Remove(fox.EntityId); continue; }
            watch.Sample(now, fox.Pos.XYZ);
            var cart = EnsureWorkCartTools(pos);
            if (cart == null || (!CartReturnEnabled(cart, record!)))
            { cartRescueWatches.Remove(fox.EntityId); continue; }
            if (!WorkCartRescueWatch.Due(now, watch.Since, watch.LastTravel, watch.Failures)) continue;
            if (cart == null || !TryCartRescueLanding(fox, pos, out var safe) || safe == null) continue;
            string foxId = GetDomesticationStatus(fox)?.GetString(FoxIdKey, "") ?? "";
            if (!packRepository.TryGetRecord(foxId, out var member) || member == null) continue;
            long utc = UtcNowMs();
            bool repeated = WorkCartRescueWatch.Repeat(utc, fox.WatchedAttributes.GetLong(CartRescueLastKey));
            var oldPosition = fox.Pos.XYZ.Clone();
            fox.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.StopTasks();
            if (fox is EntityAgent animal) animal.Controls.StopAllMovement();
            if (workCartLoggingJobs.TryGetValue(fox.EntityId, out var logging)) RemoveWorkCartLoggingWorker(fox.EntityId, logging.Plan);
            foreach (var job in cart.QuarryJobs.Where(j => j.Workers.ContainsKey(fox.EntityId)).ToArray()) ReleaseQuarryJob(fox, job, false);
            foreach (long item in droppedItemReservations.Where(p => p.Value == fox.EntityId).Select(p => p.Key).ToArray())
                droppedItemReservations.Remove(item);
            fox.Pos.Motion.Set(0, 0, 0); fox.TeleportTo(safe);
            fox.PositionBeforeFalling.Set(safe.X, safe.Y, safe.Z);
            member.Wellbeing = CompanionWellbeingRules.Normalize(member.Wellbeing);
            CompanionWellbeingWork.Rescue(member.Wellbeing);
            fox.WatchedAttributes.SetLong(CartRescueLastKey, utc);
            fox.WatchedAttributes.SetString(CartRescueCartKey, watch.Cart);
            fox.WatchedAttributes.SetBool(CartRescuePausedKey, repeated);
            fox.WatchedAttributes.SetLong("feralKinshipCartRescueEpoch", cart.RescueEpoch);
            fox.WatchedAttributes.MarkPathDirty("feralKinshipCartRescueEpoch");
            fox.WatchedAttributes.MarkPathDirty(CartRescuePausedKey);
            fox.WatchedAttributes.MarkPathDirty(CartRescueLastKey);
            fox.WatchedAttributes.MarkPathDirty(CartRescueCartKey);
            cart.RescueNotice = $"Rescued {GetFoxDisplayName(fox)}: stuck travelling (+20 exhaustion)."
                + (repeated ? " Repeated rescue: cart work suspended; check access and resume workers." : "");
            cart.Quarry.Status = cart.Mining.Status = cart.RescueNotice;
            serverApi.Logger.Notification("[FeralKinshipCompanions] Cart rescue {0} ({1}) task {2} from {3} to {4}; +20 exhaustion; suspended={5}",
                foxId, fox.EntityId, watch.Task, oldPosition, safe, repeated);
            if (serverApi.World.PlayerByUid(record!.OwnerUid) is IServerPlayer owner)
                owner.SendMessage(GlobalConstants.GeneralChatGroup, cart.RescueNotice, EnumChatType.Notification);
            packRepository.Save(); cart.MarkDirty(true); cartRescueWatches.Remove(fox.EntityId);
        }
    }

    private bool TryCartRescueLanding(Entity fox, BlockPos cart, out Vec3d? safe)
    {
        safe = null;
        var blocks = serverApi!.World.BlockAccessor;
        for (int r = 1; r <= 4; r++) for (int y = 0; y <= 2; y++)
        for (int x = -r; x <= r; x++) for (int z = -r; z <= r; z++)
        {
            if (Math.Max(Math.Abs(x), Math.Abs(z)) != r) continue;
            var feet = cart.AddCopy(x, y, z);
            if (!MiningStand(fox, feet)) continue;
            var point = new Vec3d(feet.X + .5, feet.InternalY, feet.Z + .5);
            if (serverApi.World.GetEntitiesAround(point, 1.2f, 2,
                e => e.EntityId != fox.EntityId && e.Alive && e is EntityAgent).Length > 0) continue;
            safe = point; return true;
        }
        return false;
    }

    private void ResumeCartRescueWorkers(BlockPos cart)
    {
        foreach (var fox in loadedFoxes.Values.Where(e => IsCartRescuePaused(e, cart)))
        {
            fox.WatchedAttributes.SetBool(CartRescuePausedKey, false);
            fox.WatchedAttributes.MarkPathDirty(CartRescuePausedKey);
            cartRescueWatches.Remove(fox.EntityId);
        }
        var block = EnsureWorkCartTools(cart);
        if (block != null) { block.RescueEpoch++; block.RescueNotice = "Rescued workers resumed. Check that their route is clear."; block.MarkDirty(true); }
    }
}
