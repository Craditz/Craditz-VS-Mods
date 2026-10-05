using Vintagestory.API.Client;

namespace ScentTrails;

public sealed class AnimalicaPounceRenderer : IRenderer
{
    private readonly AnimalicaActiveAbilitySystem system;

    public AnimalicaPounceRenderer(AnimalicaActiveAbilitySystem system)
    {
        this.system = system;
    }

    public double RenderOrder => 1.1;

    public int RenderRange => 9999;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage == EnumRenderStage.Before)
        {
            system.UpdateClientPounce(deltaTime);
        }
    }

    public void Dispose()
    {
    }
}
