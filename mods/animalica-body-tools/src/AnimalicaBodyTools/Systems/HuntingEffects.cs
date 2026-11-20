using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace AnimalicaBodyTools.Systems;

internal sealed class HuntingEffects(Entity animal) : EntityBehavior(animal)
{
    internal const string StateKey = "animalicaHuntingEffects", Bound = "animalicaBolas", Slow = "animalicaBolasSlow";
    internal const string DartSlow = "abtDartSlow";
    private float nextTrail, cueIn;
    private DartMind? mind;
    public override string PropertyName() => "animalicahuntingeffects";

    internal static HuntingEffects Ensure(Entity animal)
    {
        if (animal.GetBehavior<HuntingEffects>() is {} existing) return existing;
        var behavior = new HuntingEffects(animal);
        animal.AddBehavior(behavior);
        return behavior;
    }

    internal void Bind(ItemStack stack, bool intact)
    {
        var state = entity.Attributes.GetOrAddTreeAttribute(StateKey);
        var random = entity.World.Rand;
        float min = stack.ItemAttributes["snareMin"].AsFloat(), max = stack.ItemAttributes["snareMax"].AsFloat();
        state.SetFloat("snare",5+5*random.NextSingle());
        if (intact) state.SetItemstack("recovery",stack);
        entity.WatchedAttributes.SetFloat(Slow,min+(max-min)*random.NextSingle());
        entity.WatchedAttributes.SetString(Bound,stack.Collectible.Code.Path);
    }

    internal void Mark(ItemStack dart, long shooter)
    {
        var state = entity.Attributes.GetOrAddTreeAttribute(StateKey);
        var code = dart.Collectible.Code.Path;
        if (code == "dart-slow")
        {
            state.SetFloat("slow",5);
            entity.WatchedAttributes.SetFloat(DartSlow,.5f);
        }
        if (code == "dart-berserk") state.SetFloat("berserk",10);
        if (code == "dart-tranq") state.SetFloat("tranq",30);
        cueIn = 0;
        if (code == "dart-trail") state.SetFloat("trail",30);
        if (code == "dart-poison")
        {
            if (!state.HasAttribute("pulses")) state.SetFloat("pulseIn",1);
            state.SetInt("pulses",ItemPoisonDart.Pulses);
            state.SetFloat("pulseDamage",ItemPoisonDart.Total(dart) / ItemPoisonDart.Pulses);
            state.SetLong("source",shooter);
        }
    }

    private void Release(ITreeAttribute state)
    {
        var stack = state.GetItemstack("recovery");
        state.RemoveAttribute("recovery");
        state.RemoveAttribute("snare");
        entity.WatchedAttributes.RemoveAttribute(Bound);
        entity.WatchedAttributes.RemoveAttribute(Slow);
        if (stack?.ResolveBlockOrItem(entity.World) == true)
            entity.World.SpawnItemEntity(stack,entity.Pos.XYZ.Add(0,.12,0));
    }

    public override void OnEntityDeath(DamageSource source)
    {
        if (entity.Attributes.GetTreeAttribute(StateKey) is not {} state) return;
        if (state.HasAttribute("snare")) Release(state);
        mind?.Dispose();
        mind = null;
        entity.WatchedAttributes.RemoveAttribute(DartSlow);
        entity.Attributes.RemoveAttribute(StateKey);
    }

    private static float Countdown(ITreeAttribute state, string key, float dt)
    {
        if (!state.HasAttribute(key)) return 0;
        float left = state.GetFloat(key) - dt;
        if (left <= 0) state.RemoveAttribute(key);
        else state.SetFloat(key,left);
        return left;
    }

    public override void OnEntityDespawn(EntityDespawnData data)
    {
        mind?.Dispose();
        mind = null;
    }

    public override void OnGameTick(float deltaTime)
    {
        if (!entity.Alive || entity.Attributes.GetTreeAttribute(StateKey) is not {} state) return;
        if (state.HasAttribute("snare"))
        {
            float left = state.GetFloat("snare")-deltaTime;
            if (left <= 0) Release(state);
            else state.SetFloat("snare",left);
        }
        if (state.HasAttribute("slow"))
        {
            float left = Countdown(state,"slow",deltaTime);
            if (left > 0) entity.WatchedAttributes.SetFloat(DartSlow,.1f * left);
            else entity.WatchedAttributes.RemoveAttribute(DartSlow);
        }
        if (state.HasAttribute("berserk") || state.HasAttribute("tranq"))
        {
            bool rage = Countdown(state,"berserk",deltaTime) > 0;
            bool tranq = Countdown(state,"tranq",deltaTime) > 0;
            mind ??= new DartMind((EntityAgent)entity);
            mind.Update(rage,tranq,deltaTime);
            if (!rage && !tranq) { mind.Dispose(); mind = null; }
        }
        if (state.HasAttribute("slow") || state.HasAttribute("berserk") || state.HasAttribute("tranq"))
        {
            cueIn -= deltaTime;
            if (cueIn <= 0)
            {
                cueIn = .45f;
                int color = state.HasAttribute("berserk") ? ColorUtil.ToRgba(220,240,62,35)
                    : state.HasAttribute("tranq") ? ColorUtil.ToRgba(220,83,148,246) : ColorUtil.ToRgba(220,230,183,51);
                var at = entity.Pos.XYZ.Add(0,entity.SelectionBox.Y2*.8,0);
                entity.World.SpawnParticles(3,color,at.AddCopy(-.16,0,-.16),at.AddCopy(.16,.12,.16),
                    new Vec3f(-.025f,.02f,-.025f),new Vec3f(.025f,.08f,.025f),.7f,0,.4f,EnumParticleModel.Cube);
            }
        }
        if (state.HasAttribute("trail"))
        {
            nextTrail -= deltaTime;
            if (nextTrail <= 0)
            {
                nextTrail = .35f;
                var at = entity.Pos.XYZ.Add(0,.18,0);
                entity.World.SpawnParticles(3,ColorUtil.ToRgba(255,65,240,190),at,at.AddCopy(.06,.08,.06),
                    new Vec3f(-.015f,.005f,-.015f),new Vec3f(.015f,.045f,.015f),5,0,.5f,EnumParticleModel.Cube);
            }
            float left = state.GetFloat("trail")-deltaTime;
            if (left <= 0) state.RemoveAttribute("trail");
            else state.SetFloat("trail",left);
        }
        if (state.HasAttribute("pulses"))
        {
            float wait = state.GetFloat("pulseIn")-deltaTime;
            int pulses = state.GetInt("pulses");
            while (wait <= 0 && pulses > 0 && entity.Alive)
            {
                entity.ReceiveDamage(new DamageSource {Type=EnumDamageType.Poison, Source=EnumDamageSource.Player,
                    SourceEntity=entity.World.GetEntityById(state.GetLong("source")), KnockbackStrength=0},state.GetFloat("pulseDamage",1));
                pulses--;
                wait++;
            }
            if (pulses == 0)
            {
                state.RemoveAttribute("pulses");
                state.RemoveAttribute("pulseIn");
                state.RemoveAttribute("pulseDamage");
                state.RemoveAttribute("source");
            }
            else
            {
                state.SetInt("pulses",pulses);
                state.SetFloat("pulseIn",wait);
            }
        }
        if (state.Count == 0) entity.Attributes.RemoveAttribute(StateKey);
    }
}
