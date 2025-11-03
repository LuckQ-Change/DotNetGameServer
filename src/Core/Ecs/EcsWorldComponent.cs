using Core;

namespace Core.Ecs;

public class EcsWorldComponent : IServerComponent
{
    public string Name => "EcsWorld";

    public World World { get; } = new World();

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public async Task DestroyAsync()
    {
        await World.UnregisterAllAsync();
    }
}