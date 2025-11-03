using System.Collections.Concurrent;

namespace Core.Ecs;

public class World
{
    private readonly ConcurrentDictionary<string, Entity> _entities = new();
    private readonly List<ISystem> _systems = new();

    public Entity GetOrCreateEntity(string id)
    {
        return _entities.GetOrAdd(id, k => new Entity(k));
    }

    public bool RemoveEntity(string id)
    {
        return _entities.TryRemove(id, out _);
    }

    public IEnumerable<Entity> Entities => _entities.Values;

    public async Task RegisterSystemAsync(ISystem system)
    {
        _systems.Add(system);
        await system.InitializeAsync(this);
    }

    public async Task UnregisterAllAsync()
    {
        foreach (var s in _systems)
        {
            await s.DestroyAsync(this);
        }
        _systems.Clear();
    }

    public async Task UpdateAsync(TimeSpan delta)
    {
        foreach (var s in _systems)
        {
            await s.UpdateAsync(this, delta);
        }
    }
}