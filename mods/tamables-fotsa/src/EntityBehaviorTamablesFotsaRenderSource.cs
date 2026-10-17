#nullable enable

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace TamablesFotsa;

/// <summary>
/// Binds a dedicated client's render data to the selected native type after
/// client assets have loaded. Client RegisterEntityClass is a no-op and the
/// engine's Client.Init reads LoadedShape from the registered private type.
/// </summary>
internal sealed class EntityBehaviorTamablesFotsaRenderSource(Entity entity) : EntityBehavior(entity)
{
    public const string BehaviorCode = "tamablesfotsarendersource";

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);
        if (entity.Api.Side != EnumAppSide.Client) return;

        string? sourceCode = attributes["sourceEntityCode"].AsString();
        if (string.IsNullOrWhiteSpace(sourceCode)) return;
        EntityClientProperties? source = entity.World.GetEntityType(new AssetLocation(sourceCode))?.Client;
        EntityClientProperties? registered = entity.World.GetEntityType(entity.Code)?.Client;
        if (source == null || !BindRenderSource(registered, source))
        {
            entity.World.Logger.Warning(
                "[tamablesfotsa] Native render source not loaded for {0}: {1}.",
                entity.Code, sourceCode);
            return;
        }

        // EntityProperties is cloned per entity. Bind both it and the registry:
        // the later Client.Init overwrites its LoadedShape from that registry.
        BindRenderSource(properties.Client, source);
    }

    internal static bool BindRenderSource(EntityClientProperties? target, EntityClientProperties source)
    {
        if (target == null || source.LoadedShape == null || source.Shape == null) return false;

        // Loaded shapes are engine-owned immutable references. The mutable
        // shape description and texture dictionary must remain private so
        // wearable tessellation cannot change the native animal's definition.
        target.Shape = source.Shape.Clone();
        target.LoadedShape = source.LoadedShape;
        target.LoadedAlternateShapes = source.LoadedAlternateShapes;
        target.Textures = source.Textures == null
            ? null
            : new FastSmallDictionary<string, CompositeTexture>(source.Textures);
        target.TexturesAlternatesCount = source.TexturesAlternatesCount;
        target.Size = source.Size;
        target.SizeGrowthFactor = source.SizeGrowthFactor;
        return true;
    }

    public override string PropertyName() => BehaviorCode;
}
