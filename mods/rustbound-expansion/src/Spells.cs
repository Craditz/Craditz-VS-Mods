using rustboundmagic.src.system.abstracts;
using rustboundmagic.src.system.enums;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace RustboundExpansion;

public abstract class ExpansionSpell : SpellBase
{
    internal static void NotifyFailure(EntityPlayer player)
    {
        if (player.World.Side == EnumAppSide.Server && player.Player is IServerPlayer sp)
            sp.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get("rustboundmagic:msg-world-spellfailed"), EnumChatType.Notification);
    }
    protected ExpansionSpell(string id, string school, string essence) : base(id)
    {
        Tier = 2;
        School = school;
        EssenceType = essence;
        BuffDurationType = EnumBuffDurationType.timed;
        RunParticlesCasting = "casting" + essence + "buff";
        SoundAssetLocationSpellCastingSuccessful = new("rustboundmagic:sounds/effects/castcomplete-" + school + ".ogg");
    }

    protected void Complete(EntityPlayer player, bool scroll, bool restoring)
    {
        if (restoring) return;
        player.World.PlaySoundAt(SoundAssetLocationSpellCastingSuccessful, player.Pos.X, player.Pos.Y, player.Pos.Z, null, 1, 16);
        ConsumeManaForSpell(player, Id, scroll);
    }

    public override void OnCastStart(ItemSlot slot, EntityAgent caster, BlockSelection block, EntitySelection entity,
        bool first, ref EnumHandHandling handling, bool scroll, bool magnification, bool duplication)
    { }
    public override bool OnCastStep(float seconds, ItemSlot slot, EntityAgent caster, BlockSelection block,
        EntitySelection entity, bool scroll, bool magnification, bool duplication) => false;
    public override bool OnCastCancel(float seconds, ItemSlot slot, EntityAgent caster, BlockSelection block,
        EntitySelection entity, EnumItemUseCancelReason reason, bool scroll, bool magnification, bool duplication) => true;
}

public sealed class ResonanceSpell : ExpansionSpell
{
    public const float Potency = 0.20f;
    public ResonanceSpell() : base(ExpansionSystem.ResonanceId, "alteration", "light")
    {
        ManaCost = 40;
        CastTime = 4;
        Duration = 60;
        Stats = Potency;
        EffectsOtherClosePlayers = true;
        BuffType = EnumBuffType.buff;
    }

    public override void OnCastStop(float seconds, ItemSlot slot, EntityAgent caster, BlockSelection block,
        EntitySelection entity, bool scroll, bool magnification, bool duplication, float remaining, bool infinite = false)
    {
        if (caster is not EntityPlayer player || player.World.Side != EnumAppSide.Server) return;
        var restoring = IsRestoringExistingBuff(remaining);
        if (StatusEffects.IsEffectActive(player, "stasis")) return;
        // Use the upstream helper unchanged, including its radius, recipient cap,
        // stasis exclusions, native companion eligibility, and native buff HUD.
        ApplyChangesTypeBuff(GetEffectiveDuration(player, remaining), StatusEffects,
            new() { ["genericmodifierstats"] = Potency }, player, scroll, infinite, !restoring);
        Complete(player, scroll, restoring);
    }
}

public sealed class BrittleSigilSpell : ExpansionSpell
{
    public BrittleSigilSpell() : base(ExpansionSystem.BrittleId, "evocation", "neutral")
    {
        ManaCost = 30;
        CastTime = 2;
        Duration = 15;
        Damage = 10;
        Range = 16;
        BuffType = EnumBuffType.debuff;
    }

    public override void OnCastStop(float seconds, ItemSlot slot, EntityAgent caster, BlockSelection block,
        EntitySelection entity, bool scroll, bool magnification, bool duplication, float remaining, bool infinite = false)
    {
        if (caster is not EntityPlayer player || player.World.Side != EnumAppSide.Server || remaining != 0) return;
        var target = RayTraceDestinationSelectedEntityAgent(player, Range);
        if (target is not EntityAgent || ControlEffects.Banished(target) || target == player || target.GetBehavior<EntityBehaviorHealth>() == null ||
            !CanDamageTarget(player, target) || CombatRules.IsAlliedSentry(player, target) || StatusEffects.IsEffectActive(target, "stasis")) return;
        var permissionSource = new DamageSource
        {
            Source = EnumDamageSource.Player,
            Type = EnumDamageType.BluntAttack,
            SourceEntity = player,
            CauseEntity = player
        };
        if (!target.ShouldReceiveDamage(permissionSource, 1)) return;
        if (target is not EntityPlayer && !player.Player.HasPrivilege("attackcreatures")) return;
        StatusEffects.ApplyStatusEffect(target, new(), "rustboundmagic-" + Id, GetModifiedDuration(player, Id), Id);
        if (!StatusEffects.IsEffectActive(target, Id)) return;
        target.WatchedAttributes.SetFloat(ExpansionSystem.BrittleBonusKey, GetModifiedDamage(player, Id));
        target.WatchedAttributes.MarkPathDirty(ExpansionSystem.BrittleBonusKey);
        if (target is EntityPlayer) StatusEffects.RefreshBuffForOtherPlayers(target, Id, GetModifiedDuration(player, Id));
        Complete(player, scroll, false);
    }
}
