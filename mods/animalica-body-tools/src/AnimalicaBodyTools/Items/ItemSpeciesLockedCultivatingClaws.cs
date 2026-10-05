using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace AnimalicaBodyTools.Items;

/// <summary>
/// Animal cultivators that retain the vanilla hoe interaction path. Trait
/// Restrictions owns permission to use or equip them.
/// </summary>
public sealed class ItemSpeciesLockedCultivatingClaws : ItemHoe
{
    public override void OnLoaded(ICoreAPI api)
    {
        // Vanilla ItemHoe initializes its interaction help but does not call the
        // collectible base method that initializes attached behaviors.
        base.OnLoaded(api);
        foreach (CollectibleBehavior behavior in CollectibleBehaviors)
        {
            behavior.OnLoaded(api);
        }
    }

}
