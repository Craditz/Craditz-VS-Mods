#nullable enable

using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace TamablesFotsa;

/// <summary>
/// Gives an unnamed dedicated tamed variant the same localized name as its
/// original FotSA entity. This behavior is inserted after nametag, allowing a
/// non-empty player-assigned name to remain authoritative.
/// </summary>
internal sealed class EntityBehaviorTamablesFotsaSourceName : EntityBehavior
{
    public const string BehaviorCode = "tamablesfotsasourcename";

    private AssetLocation? sourceEntityCode;

    public EntityBehaviorTamablesFotsaSourceName(Entity entity) : base(entity)
    {
    }

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);

        string? sourceCode = attributes["sourceEntityCode"].AsString();
        if (!string.IsNullOrWhiteSpace(sourceCode))
        {
            sourceEntityCode = new AssetLocation(sourceCode);
        }
    }

    public override string? GetName(ref EnumHandling handling)
    {
        string? customName = entity.GetBehavior<EntityBehaviorNameTag>()?.DisplayName;
        if (!string.IsNullOrWhiteSpace(customName) || sourceEntityCode == null)
        {
            handling = EnumHandling.PassThrough;
            return null;
        }

        handling = EnumHandling.PreventDefault;
        string keyPrefix = entity.Alive ? ":item-creature-" : ":item-dead-creature-";
        return Lang.GetMatching(sourceEntityCode.Domain + keyPrefix + sourceEntityCode.Path);
    }

    public override string PropertyName() => BehaviorCode;
}
