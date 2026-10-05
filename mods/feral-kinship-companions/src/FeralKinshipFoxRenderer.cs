#nullable enable

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

/// <summary>
/// Uses the normal animated shape renderer while the fox is present, and
/// deliberately emits no world, shadow, nametag, or GUI draw calls while the
/// authoritative pack ledger has the fox away from the world.
/// </summary>
public sealed class FeralKinshipFoxRenderer : EntityShapeRenderer
{
    private readonly EntityAgent? agent;
    private DummySlot? cargoSlot;

    public FeralKinshipFoxRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
    {
        agent = entity as EntityAgent;
    }

    private bool Hidden => FeralKinshipCompanionSystem.IsFoxAwayFromWorld(entity);

    protected override void RenderHeldItem(float dt, bool isShadowPass, bool right)
    {
        if (right || agent == null || FeralKinshipCompanionSystem.IsCompanionJuvenile(entity))
        {
            base.RenderHeldItem(dt, isShadowPass, right);
            return;
        }

        ItemStack? cargo = FeralKinshipCompanionSystem.GetFoxStorageCargoForRender(entity);
        if (cargo == null)
        {
            base.RenderHeldItem(dt, isShadowPass, right);
            return;
        }

        cargoSlot ??= new DummySlot();
        // EntityShapeRenderer reads LeftHandItemSlot only inside RenderHeldItem.
        // Keep the cargo override scoped to that draw so a later deposit cannot
        // leave a stale dummy slot on the entity between frames.
        using (new CompanionMouthCargoRenderScope(agent, cargoSlot, cargo))
        {
            base.RenderHeldItem(dt, isShadowPass, right);
        }
    }

    public override void DoRender3DOpaque(float dt, bool isShadowPass)
    {
        if (!Hidden) base.DoRender3DOpaque(dt, isShadowPass);
    }

    public override void DoRender3DOpaqueBatched(float dt, bool isShadowPass)
    {
        if (!Hidden) base.DoRender3DOpaqueBatched(dt, isShadowPass);
    }

    public override void DoRender3DOIT(float dt)
    {
        if (!Hidden) base.DoRender3DOIT(dt);
    }

    public override void DoRender3DOITBatched(float dt)
    {
        if (!Hidden) base.DoRender3DOITBatched(dt);
    }

    public override void DoRender3DAfterOIT(float dt, bool isShadowPass)
    {
        if (!Hidden) base.DoRender3DAfterOIT(dt, isShadowPass);
    }

    public override void DoRender2D(float dt)
    {
        if (!Hidden) base.DoRender2D(dt);
    }

    public override void RenderToGui(float dt, double posX, double posY, double posZ, float yawDelta, float size)
    {
        if (!Hidden) base.RenderToGui(dt, posX, posY, posZ, yawDelta, size);
    }
}

internal readonly struct CompanionMouthCargoRenderScope : System.IDisposable
{
    private readonly EntityAgent agent;
    private readonly DummySlot cargoSlot;
    private readonly ItemSlot? originalSlot;

    internal CompanionMouthCargoRenderScope(EntityAgent agent, DummySlot cargoSlot, ItemStack cargo)
    {
        this.agent = agent;
        this.cargoSlot = cargoSlot;
        originalSlot = agent.LeftHandItemSlot;
        cargoSlot.Itemstack = cargo;
        agent.LeftHandItemSlot = cargoSlot;
    }

    public void Dispose()
    {
        agent.LeftHandItemSlot = originalSlot;
        cargoSlot.Itemstack = null;
    }
}
