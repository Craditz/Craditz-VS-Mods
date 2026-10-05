using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace AnimalicaBodyTools.Items;

/// <summary>
/// Animal claw tool. Trait Restrictions owns permission to use or equip it.
///
/// This derives from the base game's ItemKnife so right-click corpse harvesting
/// follows vanilla knife behavior.
/// </summary>
public sealed class ItemSpeciesLockedClaws : ItemKnife
{
}
