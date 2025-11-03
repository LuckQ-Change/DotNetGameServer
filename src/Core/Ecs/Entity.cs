namespace Core.Ecs;

public class Entity
{
    public string Id { get; }

    private readonly Dictionary<Type, object> _components = new();

    public Entity(string id)
    {
        Id = id;
    }

    public T AddComponent<T>(T component) where T : class
    {
        _components[typeof(T)] = component;
        return component;
    }

    public bool RemoveComponent<T>() where T : class
    {
        return _components.Remove(typeof(T));
    }

    public T? GetComponent<T>() where T : class
    {
        return _components.TryGetValue(typeof(T), out var c) ? c as T : null;
    }
}