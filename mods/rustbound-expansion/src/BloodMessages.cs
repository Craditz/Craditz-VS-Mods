using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace RustboundExpansion;

// Only the cost transaction creates this source. No persistent player flag can
// accidentally relabel a later death, and ordinary suicide sources pass through.
internal sealed class BloodCostDamageSource : DamageSource
{
    internal BloodCostDamageSource(EntityPlayer caster)
    {
        Source=EnumDamageSource.Suicide;Type=EnumDamageType.Injury;
        CauseEntity=SourceEntity=caster;KnockbackStrength=0;
    }
}

internal static class BloodMessages
{
    internal const int Variants=12;
    internal static void Install(Harmony harmony)
    {
        var type=AccessTools.TypeByName("Vintagestory.Server.ServerSystemEntitySimulation")
            ?? System.Reflection.Assembly.Load("VintagestoryLib").GetType("Vintagestory.Server.ServerSystemEntitySimulation");
        var method=AccessTools.Method(type,"GetDeathMessage")
            ?? throw new MissingMethodException("Vintage Story 1.22.7 death-message lookup was not found.");
        harmony.Patch(method,prefix:new(typeof(BloodMessages),nameof(DeathMessage)));
    }
    internal static string Key(string kind,Random random)=>$"rustboundexpansion:blood-{kind}-{random.Next(Variants)+1}";

    // Reuse the native broadcast so its visibility/settings and death lifecycle
    // remain authoritative. Do not independently announce the player's death.
    internal static bool DeathMessage(DamageSource? __1,ref string __result)
    {
        if(__1 is not BloodCostDamageSource || __1.CauseEntity is not EntityPlayer p
            || p.World.Side!=EnumAppSide.Server)return true;
        __result=Lang.Get(Key("death",p.World.Rand),p.Player.PlayerName);
        return false;
    }
    internal static void Grace(EntityPlayer player)
    {
        if(player.World.Side==EnumAppSide.Server && player.Alive && player.Player is IServerPlayer sp)
            sp.SendMessage(GlobalConstants.GeneralChatGroup,Lang.Get(Key("grace",player.World.Rand)),EnumChatType.Notification);
    }
}
