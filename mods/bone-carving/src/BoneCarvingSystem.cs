using Vintagestory.API.Common;

namespace BoneCarving;

public sealed class BoneCarvingSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterItemClass("BoneCarvingKnife", typeof(BoneCarvingKnife));
    }
}
