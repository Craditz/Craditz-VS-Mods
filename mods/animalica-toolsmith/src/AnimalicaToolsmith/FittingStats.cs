using Toolsmith.Config;

namespace AnimalicaToolsmith;

public readonly record struct FittingStats(float Base, float Self, float Support, float Speed, float WearChance)
{
    public static FittingStats Compose(HandleStatDefines seat, HandleStatDefines brace, GripStatDefines lining)
        => new(brace.baseHPfactor, brace.selfHPBonus, seat.bindingHPBonus,
               seat.speedBonus + lining.speedBonus, lining.chanceToDamage);

    public int FittingLife(int toolBase, float treatment, BindingStatDefines binding)
        => Math.Max(1, (int)(toolBase * Base * (1 + Self) * (1 + treatment) * (1 + binding.handleHPBonus)));

    public int BindingLife(int toolBase, BindingStatDefines binding)
        => Math.Max(1, (int)(toolBase * binding.baseHPfactor * (1 + binding.selfHPBonus) * (1 + Support)));

    public static int Rebase(int maximum, double fraction)
        => Math.Clamp((int)Math.Floor(maximum * Math.Clamp(fraction, 0, 1)), 0, maximum);
}
