using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace RustboundExpansion;

public sealed partial class ExpansionRuntime
{
    internal readonly Dictionary<(long Target, string Kind), MemeState> Memes = new();
    internal sealed class MemeState
    {
        internal required EntityPlayer Caster;
        internal required Entity Target;
        internal required SpellDefinition Definition;
        internal long End, Next, LastCast;
        internal Vec3d LastPosition = new();
        internal int Dimension, Growth;
    }
    internal void ApplyMeme(EntityPlayer caster, Entity target, SpellDefinition d)
    {
        if (d.Kind == "spite")
        {
            // Corpse Explosion uses radius 5.5 + 3 in native CreateExplosion,
            // selecting mediumexplosion at unmodified pitch. Presentation only.
            var at = SpellTargeting.BodyCenter(target);
            World.PlaySoundAt(new AssetLocation("game:sounds/effect/mediumexplosion"), at.X, at.Y, at.Z, null, false, (float)(24 * Math.Sqrt(8.5)), 1);
            MemeVisualSystem.Emit(World, target, "spite", at); return;
        }
        RemoveMeme(target, d.Kind, false);
        Memes[(target.EntityId, d.Kind)] = new() { Caster = caster, Target = target, Definition = d, End = Now + (long)(d.Duration * 1000),
            Dimension = target.Pos.Dimension, LastPosition = target.Pos.XYZ, Next = Now + (d.Kind == "duck" ? 3000 : 0), LastCast = long.MinValue / 2 };
        target.WatchedAttributes.SetBool(MemeCatalog.Key(d.Kind), true);
        if (d.Kind == "hat") target.WatchedAttributes.SetInt("rbx-meme-hatlevel", 1);
        if (d.Kind == "hat") target.WatchedAttributes.SetInt("rbx-meme-hatseed", World.Rand.Next());
        if (d.Kind == "weather") { target.WatchedAttributes.SetFloat("wetness", 1); MemeSound(target, "tinythunder"); }
        Marker(target, d.Id, d.Duration);
    }
    internal void MemeEffectRemoved(Entity target, string id)
    {
        var d = MemeCatalog.All.FirstOrDefault(d => d.Id == id);
        if (d != null) RemoveMeme(target, d.Kind, Memes.TryGetValue((target.EntityId, d.Kind), out var state) && Now >= state.End);
    }
    internal void RemoveMeme(Entity target, string kind, bool expiry)
    {
        bool removed = Memes.Remove((target.EntityId, kind));
        target.WatchedAttributes.RemoveAttribute(MemeCatalog.Key(kind));
        if (kind == "hat") { target.WatchedAttributes.RemoveAttribute("rbx-meme-hatlevel"); target.WatchedAttributes.RemoveAttribute("rbx-meme-hatseed"); }
        if (removed && expiry && kind == "entrance" && target.Alive && !target.ShouldDespawn)
        {
            MemeVisualSystem.Emit(World, target, "reveal");
            // The native client lightning effect supplies thunder too.
        }
        Unmark(target, MemeCatalog.All.Single(d => d.Kind == kind).Id);
    }
    internal void MemeSpellCast(EntityPlayer p)
    {
        if (!Memes.TryGetValue((p.EntityId, "hat"), out var state) || Now >= state.End || Now - state.LastCast < 350) return;
        state.LastCast = Now;
        if (++state.Growth >= 8) { state.Growth = 0; MemeVisualSystem.Emit(World, p, "paper"); }
        p.WatchedAttributes.SetInt("rbx-meme-hatlevel", state.Growth + 1);
    }
    internal void MemeContainerOpened(EntityPlayer p)
    {
        if (!Memes.TryGetValue((p.EntityId, "applause"), out var state) || Now >= state.End) return;
        MemeSound(p, "applause", pitch: .88f + (float)World.Rand.NextDouble() * .24f);
    }
    private void MemeSound(Entity e, string sound, Vec3d? at = null, float pitch = 1)
    {
        var p = at ?? e.Pos.XYZ;
        World.PlaySoundAt(new AssetLocation("rustboundexpansion:sounds/mischief/" + sound + ".ogg"), p.X, p.Y, p.Z, null, pitch, 16, .65f);
    }
    internal void TickMemes()
    {
        foreach (var state in Memes.Values.ToArray())
        {
            var e = state.Target; string kind = state.Definition.Kind;
            if (!e.Alive || e.ShouldDespawn || !state.Caster.Alive || state.Caster.ShouldDespawn || e.Pos.Dimension != state.Dimension)
            { RemoveMeme(e, kind, false); continue; }
            if (Now >= state.End) { RemoveMeme(e, kind, true); continue; }
            if (kind == "weather")
            {
                e.WatchedAttributes.SetFloat("wetness", 1);
            }
            if (kind == "duck" && Now >= state.Next)
            {
                state.Next = Now + World.Rand.Next(4000, 8001);
                MemeSound(e, "duck", e.Pos.XYZ.AddCopy(Math.Sin(e.Pos.Yaw) * 1.5, 1, Math.Cos(e.Pos.Yaw) * 1.5));
            }
            if (kind == "squeak")
            {
                var delta = e.Pos.XYZ - state.LastPosition;
                if (Now >= state.Next && e.OnGround && delta.X * delta.X + delta.Z * delta.Z >= .36)
                { state.Next = Now + 350; state.LastPosition = e.Pos.XYZ; MemeSound(e, "squeak"); }
            }
        }
    }
    internal void ClearMemeTarget(Entity e)
    { foreach (var d in MemeCatalog.All.Where(d => d.Duration > 0)) RemoveMeme(e, d.Kind, false); }
    internal void ClearMemeOwner(EntityPlayer p)
    { foreach (var state in Memes.Values.Where(s => s.Caster == p).ToArray()) RemoveMeme(state.Target, state.Definition.Kind, false); }
    internal void DisposeMemes()
    { foreach (var state in Memes.Values.ToArray()) RemoveMeme(state.Target, state.Definition.Kind, false); }
}
