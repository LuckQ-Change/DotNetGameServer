using System.Collections.Concurrent;
using Core;

namespace ServerHost;

public sealed class CurrentServerAccessor
{
    private readonly ConcurrentDictionary<string, BaseGameServer> _servers = new(StringComparer.OrdinalIgnoreCase);

    public BaseGameServer? Server => _servers.Values.FirstOrDefault();

    public IReadOnlyCollection<BaseGameServer> GetAllServers() => _servers.Values.ToArray();

    public void Add(string name, BaseGameServer server)
    {
        _servers[name] = server;
    }

    public void Set(BaseGameServer? server)
    {
        if (server != null)
            _servers[server.ServerName] = server;
    }

    public BaseGameServer? Get(string name)
    {
        _servers.TryGetValue(name, out var server);
        return server;
    }

    public void Remove(string name)
    {
        _servers.TryRemove(name, out _);
    }

    public void Clear()
    {
        _servers.Clear();
    }
}
