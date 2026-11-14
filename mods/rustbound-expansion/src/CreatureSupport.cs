using System.Reflection.Emit;
using System.Collections.Concurrent;
using HarmonyLib;
using rustboundmagic.src;
using rustboundmagic.src.common.entity;
using rustboundmagic.src.common.entitybehavior;
using rustboundmagic.src.system.abstracts;
using rustboundmagic.src.system.effects;
using rustboundmagic.src.system.enums;
using rustboundmagic.src.system.enums.spells;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace RustboundExpansion;

// Ownership is a runtime contract, not a species/mod/tag allowlist. In particular,
// Draconis' species tags also occur on wild dragons. No optional DLL is referenced.
internal static class CreatureSupport
{
    private static readonly StatusEffectsRM Effects = new();
    private static readonly ConcurrentDictionary<Type, bool> BaseMovement = new();

    internal static string? Owner(Entity? entity)
    {
        if (entity is not EntityAgent || entity is EntityPlayer) return null;
        string? owner = null;
        bool conflict = false;
        void Add(string? uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return;
            if (owner != null && owner != uid) conflict = true;
            owner = uid;
        }
        if (entity is EntityCompanionRM companion) Add(companion.OwnerPlayerUID);
        if (entity is SummonedSentry sentry) Add(sentry.OwnerUid);
        if (entity is BloodGoober blood) Add(blood.OwnerUid);
        var domestication = entity.WatchedAttributes.GetTreeAttribute("domesticationstatus");
        var level = domestication?.GetString("domesticationLevel");
        // A lingering owner field on a WILD animal is not ownership evidence.
        if (level is "DOMESTICATED" or "TAMING") Add(domestication?.GetString("owner"));
        Add(entity.WatchedAttributes.GetTreeAttribute("ownedby")?.GetString("uid"));
        return conflict ? null : owner;
    }

    internal static bool IsOwned(Entity? entity) => Owner(entity) != null;

    internal static bool IsFriendly(EntityPlayer caster, Entity target)
    {
        var uid = Owner(target);
        if (uid == null) return false;
        if (uid == caster.PlayerUID) return true;
        // An offline/unknown owner is not inferred to be friendly. The player's
        // existing PvP rule is authoritative for another online player's pets.
        var owner = caster.World.PlayerByUid(uid)?.Entity;
        return owner != null && owner.PlayerUID == uid && BurstOfSpeedSpell.IsAlly(caster, owner);
    }

    internal static bool Eligible(EntityPlayer caster, Entity entity) => entity is EntityAgent
        && entity != caster && entity.Alive && !entity.ShouldDespawn && entity.DespawnReason == null
        && entity.Pos.Dimension == caster.Pos.Dimension && !ControlEffects.Banished(entity)
        && IsFriendly(caster, entity);

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(SpellBase), "AOEGetFriendlyBuffTargets"),
            postfix: new(typeof(CreatureSupport), nameof(AfterBuffTargets)));
        harmony.Patch(AccessTools.Method(typeof(SpellBase), "DamageEntitiesAndHealPlayersWithArea"),
            transpiler: new(typeof(CreatureSupport), nameof(HealAreaTranspiler)));
        harmony.Patch(AccessTools.Method(typeof(EntityAgent), nameof(EntityAgent.GetWalkSpeedMultiplier)),
            postfix: new(typeof(CreatureSupport), nameof(AfterWalkSpeed)));
        foreach (var (tier, name) in new[] { (2, "Dispel1"), (3, "Dispel2") })
        {
            var type = typeof(SpellBase).Assembly.GetType($"rustboundmagic.src.system.spells.researched.tier{tier}.incantation.{name}", true)!;
            harmony.Patch(AccessTools.Method(type, nameof(SpellBase.OnCastStop)),
                transpiler: new(typeof(CreatureSupport), nameof(DispelTranspiler)));
        }
        CreatureBuffPersistence.Install(harmony);
    }

    private static void AfterBuffTargets(EntityAgent __0, ref Entity[] __result)
    {
        if (__0 is not EntityPlayer caster) return;
        var center = caster.Pos.XYZ.AddCopy(.5, .5, .5);
        var found = caster.World.GetEntitiesAround(center, 4.5f, 4.5f,
            e => Eligible(caster, e));
        // Append after vanilla recipients: adding pets must not displace players
        // in Rustbound's existing area-buff cap/order. Preserve its query shape.
        __result = __result.Concat(found.Where(e => Eligible(caster, e)
                && Math.Abs(e.Pos.Y - center.Y) <= 4.5
                && Math.Pow(e.Pos.X - center.X, 2) + Math.Pow(e.Pos.Z - center.Z, 2) <= 4.5 * 4.5))
            .DistinctBy(e => e.EntityId).ToArray();
    }

    private static bool HealsAreaTarget(Entity entity, EntityPlayer caster) => entity is EntityPlayer
        || Eligible(caster, entity);

    private static IEnumerable<CodeInstruction> HealAreaTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        int count = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Isinst && Equals(instruction.operand, typeof(EntityPlayer)))
            {
                count++;
                // The original branch consumes an object/null; a boolean has the
                // same branch shape. Leave the native Animate Object branch intact.
                yield return new CodeInstruction(OpCodes.Ldarg_1).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                yield return CodeInstruction.Call(typeof(CreatureSupport), nameof(HealsAreaTarget));
            }
            else yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException("Unsupported Rustbound Healing Surge recipient layout.");
    }

    private static void DispelCreature(EntityBehaviorBuffSystemRM system, int amount, EntityAgent caster)
    {
        if (caster is EntityPlayer player && Eligible(player, system.entity)) system.ClearRandomActiveDebuffFromPlayer(amount);
        else system.ClearRandomActiveBuffFromTarget(amount);
    }

    private static IEnumerable<CodeInstruction> DispelTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var method = AccessTools.Method(typeof(EntityBehaviorBuffSystemRM), nameof(EntityBehaviorBuffSystemRM.ClearRandomActiveBuffFromTarget));
        int count = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(method))
            {
                count++;
                yield return new CodeInstruction(OpCodes.Ldarg_3).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                yield return CodeInstruction.Call(typeof(CreatureSupport), nameof(DispelCreature));
            }
            else yield return instruction;
        }
        if (count == 0) throw new InvalidOperationException("Unsupported Rustbound Dispel recipient layout.");
    }

    internal static float StatBonus(Entity entity, string category)
    {
        if (entity.Stats == null) return 0;
        foreach (var entry in entity.Stats)
        {
            if (entry.Key != category) continue;
            float bonus = 0;
            foreach (var contribution in entry.Value.ValuesByKey)
                if (contribution.Key.StartsWith("rustboundmagic-", StringComparison.Ordinal)
                    && BridgesStat(contribution.Key[15..], category)
                    && Effects.IsEffectActive(entity, contribution.Key[15..]) && float.IsFinite(contribution.Value.Value))
                    bonus += contribution.Value.Value;
            return bonus;
        }
        return 0;
    }

    private static bool BridgesStat(string id, string category) => category == "walkspeed" && id == ExpansionSystem.RecoveryId
        || id != "slow1" && RustboundMagic.FullSpellDictionary.TryGetValue(id, out var spell) && spell.BuffType == EnumBuffType.buff;

    private static void AfterWalkSpeed(EntityAgent __instance, ref double __result)
    {
        // An override may call base and already blend stats itself. Do not apply
        // the same bonus twice; native/optional custom movement keeps its rules.
        if (IsOwned(__instance) && BaseMovement.GetOrAdd(__instance.GetType(),
            type => type.GetMethod(nameof(EntityAgent.GetWalkSpeedMultiplier))?.DeclaringType == typeof(EntityAgent)))
            __result *= Math.Max(0, 1 + StatBonus(__instance, "walkspeed"));
    }

    internal static bool Physical(EnumDamageType type) => type is EnumDamageType.BluntAttack
        or EnumDamageType.SlashingAttack or EnumDamageType.PiercingAttack or EnumDamageType.Injury
        or EnumDamageType.Crushing;

    internal static void NormalizeHealing(Entity target, ref DamageSource source)
    {
        if (!IsOwned(target) || source is not ElementalDamageSource { Type: EnumDamageType.Heal, ElementalType: EnumMagicElement.none }) return;
        // Native Healing Surge emits an elemental "none" heal. NPC elemental
        // delegates match that to their default "none" weakness/strength and can
        // erase or reverse the heal. Keep every base field and use the ordinary
        // healing path, including any deliberate "heal" weakness on the creature.
        source = new DamageSource
        {
            Source = source.Source, Type = source.Type, HitPosition = source.HitPosition,
            SourceEntity = source.SourceEntity, CauseEntity = source.CauseEntity,
            SourceBlock = source.SourceBlock, SourcePos = source.SourcePos,
            DamageTier = source.DamageTier, KnockbackStrength = source.KnockbackStrength,
            Duration = source.Duration, TicksPerDuration = source.TicksPerDuration,
            DamageOverTimeType = source.DamageOverTimeType, YDirKnockbackDiv = source.YDirKnockbackDiv,
            IgnoreInvFrames = source.IgnoreInvFrames
        };
    }

    internal static void BeforeDamage(Entity target, DamageSource source, ref float damage)
    {
        if (!float.IsFinite(damage) || damage <= 0 || source.Type is EnumDamageType.Heal or EnumDamageType.Hunger) return;
        var attacker = source.CauseEntity ?? source.SourceEntity;
        // Vanilla and PetAI melee AI do not consult meleeWeaponsDamage. Only
        // bridge Rustbound's live contributions; don't reapply other mods' stats.
        if (attacker != target && IsOwned(attacker) && Physical(source.Type) && source.Duration <= TimeSpan.Zero
            && source is not ElementalDamageSource && (source.SourceEntity == null || source.SourceEntity == attacker))
            damage *= Math.Max(0, 1 + StatBonus(attacker!, "meleeWeaponsDamage"));
        if (!IsOwned(target)) return;
        if (source.Type == EnumDamageType.Gravity && Effects.IsEffectActive(target, "featherfall1")) { damage = 0; return; }
        if (source.Type == EnumDamageType.Suffocation && Effects.IsEffectActive(target, "endlessbreath1")) { damage = 0; return; }
        if (Physical(source.Type))
            foreach (var id in new[] { "stoneskin1", "stoneskin2" })
                if (Effects.IsEffectActive(target, id) && RustboundMagic.FullSpellDictionary.TryGetValue(id, out var spell))
                    damage *= Math.Clamp(1 - spell.Stats, 0, 1);
        string? element = source is ElementalDamageSource elemental && elemental.ElementalType != EnumMagicElement.none
            ? elemental.ElementalType.ToString()
            : source.Type switch { EnumDamageType.Fire => "fire", EnumDamageType.Frost => "frost", EnumDamageType.Electricity => "lightning", _ => null };
        if (element != null)
        {
            foreach (var (id, amount) in new[] { ("resistelements1", .15f), ("resistelements2", .30f), ("resist" + element + "1", .25f), ("resist" + element + "2", .50f) })
                if (Effects.IsEffectActive(target, id)) damage *= 1 - amount;
        }
        if (attacker == null || attacker == target || !attacker.Alive || attacker.GetBehavior<EntityBehaviorHealth>() == null) return;
        foreach (var id in new[] { "spikeshield1", "spikeshield2" })
            if (Effects.IsEffectActive(target, id) && RustboundMagic.FullSpellDictionary.TryGetValue(id, out var spell))
                // No entity source, as in native Spike Shield: reflected damage
                // cannot recursively trigger the other creature's Spike Shield.
                attacker.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.Injury }, spell.Damage);
    }

    internal static void AfterDamageDelegates(Entity target, DamageSource source, ref float damage)
    {
        if (!IsOwned(target) || !float.IsFinite(damage) || damage <= 0 || source.Type is EnumDamageType.Heal or EnumDamageType.Gravity or EnumDamageType.Suffocation) return;
        // Native NPC delegates already consume barriers on most creatures. This
        // fallback covers health behaviors with no elemental-modifier delegate;
        // an already absorbed hit has zero damage or an empty pool here.
        if (!Effects.IsEffectActive(target, "barrier1") && !Effects.IsEffectActive(target, "barrier2")) return;
        const string pool = "entitybehavior-spell-healthbarrier_rm";
        float barrier = target.WatchedAttributes.GetFloat(pool);
        if (!float.IsFinite(barrier) || barrier <= 0) return;
        float used = Math.Min(barrier, damage);
        damage -= used;
        target.WatchedAttributes.SetFloat(pool, barrier - used);
        target.WatchedAttributes.MarkPathDirty(pool);
        if (barrier <= used)
            foreach (var id in new[] { "barrier1", "barrier2" })
                if (Effects.IsEffectActive(target, id)) Effects.RemoveEffect(target, id);
    }
}
