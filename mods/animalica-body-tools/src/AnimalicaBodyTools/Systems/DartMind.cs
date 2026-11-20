using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.Essentials;
using Vintagestory.GameContent;

namespace AnimalicaBodyTools.Systems;

internal sealed class DartMind : IDisposable
{
    private readonly EntityAgent animal;
    private readonly AiTaskManager? manager;
    private readonly WaypointsTraverser path;
    private readonly IAiTask? attack;
    private readonly Dictionary<IAiTask,float> limits = new();
    private readonly List<(object owner, FieldInfo field, float value)> ranges = new();
    private bool angry, sedated, forcing;
    private float nextDecision, nextPath;
    private Entity? enemy;
    private static readonly FieldInfo ModernTarget = AccessTools.Field(typeof(AiTaskBaseTargetableR),"targetEntity");
    private static readonly FieldInfo ModernConfig = AccessTools.Field(typeof(AiTaskBaseTargetableR),"baseConfig");
    private static readonly FieldInfo LastAttack = AccessTools.Field(typeof(AiTaskMeleeAttack),"lastCheckOrAttackMs");
    private static readonly FieldInfo DidDamage = AccessTools.Field(typeof(AiTaskMeleeAttack),"damageInflicted");

    public DartMind(EntityAgent animal)
    {
        this.animal = animal;
        var ai = animal.GetBehavior<EntityBehaviorTaskAI>();
        manager = ai?.TaskManager;
        path = ai?.PathTraverser ?? new WaypointsTraverser(animal);
        if (manager == null) return;
        attack = manager.AllTasks.FirstOrDefault(CanAttack);
        manager.OnShouldExecuteTask += AllowTask;
        manager.OnExecuteTask += AllowStart;
    }

    private static bool CanAttack(IAiTask task) => task switch
    {
        AiTaskMeleeAttack => (float)AccessTools.Field(typeof(AiTaskMeleeAttack),"damage").GetValue(task)! > 0,
        AiTaskThrowAtEntity => (float)AccessTools.Field(typeof(AiTaskThrowAtEntity),"projectileDamage").GetValue(task)! > 0,
        AiTaskMeleeAttackR => ModernConfig.GetValue(task) is AiTaskMeleeAttackConfig { Damage: > 0 },
        AiTaskShootAtEntityR => ModernConfig.GetValue(task) is AiTaskShootAtEntityConfig { ProjectileDamage: > 0 },
        _ => false
    };

    private static Entity? Target(IAiTask task) => task switch
    {
        AiTaskBaseTargetable legacy => legacy.TargetEntity,
        AiTaskBaseTargetableR modern => modern.TargetEntity,
        _ => null
    };
    private bool AllowTask(IAiTask task) => !angry && InRange(task);
    private bool AllowStart(IAiTask task, IAiTask? active) => angry ? forcing && task == attack : InRange(task);
    private bool InRange(IAiTask task) => !sedated || !limits.TryGetValue(task,out float limit)
        || Target(task) is not {} target || animal.Pos.SquareDistanceTo(target.Pos) <= limit * limit;

    private void Halve(IAiTask task, object owner, string fieldName, bool primary)
    {
        var field = AccessTools.Field(owner.GetType(),fieldName);
        if (field?.GetValue(owner) is not float value || value <= 0) return;
        ranges.Add((owner,field,value));
        field.SetValue(owner,value*.5f);
        if (primary) limits[task] = value*.5f;
    }
    private void SetSedated(bool enabled)
    {
        sedated = enabled;
        if (!enabled)
        {
            foreach (var (owner,field,value) in ranges) field.SetValue(owner,value);
            ranges.Clear();
            limits.Clear();
            return;
        }
        if (manager == null) return;
        foreach (var task in manager.AllTasks)
        {
            if (task is AiTaskSeekEntity)
            {
                Halve(task,task,"seekingRange",true);
                Halve(task,task,"belowTempSeekingRange",false);
            }
            else if (task is AiTaskThrowAtEntity) Halve(task,task,"maxDist",true);
            else if (task is AiTaskSeekEntityR or AiTaskShootAtEntityR && ModernConfig.GetValue(task) is AiTaskBaseTargetableConfig cfg)
            {
                Halve(task,cfg,"SeekingRange",true);
                if (cfg is AiTaskSeekEntityConfig) Halve(task,cfg,"BelowTemperatureSeekingRange",false);
            }
        }
    }

    internal void Update(bool rage, bool tranq, float dt)
    {
        if (sedated != tranq) SetSedated(tranq);
        if (rage != angry)
        {
            angry = rage;
            manager?.StopTasks();
            path.Stop();
            enemy = null;
            nextDecision = nextPath = 0;
        }
        if (angry && manager == null) path.OnGameTick(dt);
        nextPath -= dt;
        nextDecision -= dt;
        if (nextDecision > 0) return;
        nextDecision = .2f;
        if (!angry)
        {
            if (sedated && manager != null)
                foreach (var task in manager.ActiveTasksBySlot)
                    if (task != null && !InRange(task)) manager.StopTask(task.Slot);
            return;
        }
        if (attack == null || manager == null)
        {
            if (nextPath > 0 && path.Active) return;
            var rand = animal.World.Rand;
            double heading = rand.NextDouble()*Math.Tau, distance = 3+rand.NextDouble()*4;
            path.NavigateTo(animal.Pos.XYZ.Add(Math.Sin(heading)*distance,0,Math.Cos(heading)*distance),
                .035f,.35f,RetryPath,RetryPath,RetryPath,searchDepth:150);
            nextPath = 1.25f;
            return;
        }
        bool Eligible(Entity e) => e != animal && e is EntityAgent and not EntityPlayer && e.Alive && e.IsInteractable
            && e.Pos.Dimension == animal.Pos.Dimension && e.GetBehavior<EntityBehaviorHealth>() != null
            && !e.IsActivityRunning("invulnerable") && e.ShouldReceiveDamage(new DamageSource {SourceEntity=animal,Type=EnumDamageType.BluntAttack},1);
        Entity? nearest = animal.World.GetNearestEntity(animal.Pos.XYZ,32,32,Eligible);
        nearest ??= ((IServerWorldAccessor)animal.World).LoadedEntities.Values.Where(Eligible)
            .MinBy(e=>animal.Pos.SquareDistanceTo(e.Pos));
        if (nearest != enemy)
        {
            if (manager.ActiveTasksBySlot[attack.Slot] == attack) manager.StopTask(attack.Slot);
            path.Stop();
            enemy = nearest;
            nextPath = 0;
        }
        if (enemy == null || manager.ActiveTasksBySlot[attack.Slot] == attack) return;
        double reach = attack is AiTaskShootAtEntityR or AiTaskThrowAtEntity ? 10 : .65+(animal.SelectionBox.XSize+enemy.SelectionBox.XSize)*.5;
        if (animal.Pos.SquareDistanceTo(enemy.Pos) > reach*reach)
        {
            if (nextPath > 0 && path.Active) return;
            path.NavigateTo(enemy.Pos.XYZ,.035f,(float)reach*.7f,RetryPath,RetryPath,RetryPath,searchDepth:250);
            nextPath = .7f;
            return;
        }
        path.Stop();
        if (attack is AiTaskBaseTargetable legacy) legacy.targetEntity = enemy;
        else ModernTarget.SetValue(attack,enemy);
        if (attack is AiTaskMeleeAttack)
        {
            LastAttack.SetValue(attack,animal.World.ElapsedMilliseconds);
            DidDamage.SetValue(attack,false);
        }
        forcing = true;
        try { manager.ExecuteTask(attack,attack.Slot); }
        finally { forcing = false; }
    }
    private void RetryPath() => nextPath = 0;
    public void Dispose()
    {
        if (angry) { manager?.StopTasks(); path.Stop(); }
        if (sedated) SetSedated(false);
        if (manager != null)
        {
            manager.OnShouldExecuteTask -= AllowTask;
            manager.OnExecuteTask -= AllowStart;
        }
    }
}
