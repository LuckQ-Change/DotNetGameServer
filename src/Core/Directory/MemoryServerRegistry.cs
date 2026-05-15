using System.Collections.Concurrent;

namespace Core.Directory;

public class MemoryServerRegistry : IServerRegistry
{
    private readonly ConcurrentDictionary<string, ServerRegistration> _servers = new(StringComparer.OrdinalIgnoreCase);

    public string Name => "MemoryServerRegistry";
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DestroyAsync() => Task.CompletedTask;

    public Task<bool> RegisterAsync(ServerRegistration registration)
    {
        registration.LastHeartbeatUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _servers[registration.ServerId] = registration;
        return Task.FromResult(true);
    }

    public Task<bool> UnregisterAsync(string serverId)
    {
        return Task.FromResult(_servers.TryRemove(serverId, out _));
    }

    public Task<ServerRegistration?> GetAsync(string serverId)
    {
        _servers.TryGetValue(serverId, out var reg);
        return Task.FromResult(reg);
    }

    public Task<List<ServerRegistration>> QueryAsync(string? serverType = null, string? region = null)
    {
        var result = _servers.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(serverType))
            result = result.Where(s => string.Equals(s.ServerType, serverType, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(region))
            result = result.Where(s => string.Equals(s.Region, region, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(result.ToList());
    }

    public Task<List<ServerRegistration>> GetAllAsync()
    {
        return Task.FromResult(_servers.Values.ToList());
    }

    public Task<int> CleanupStaleAsync(int offlineTtlSeconds, int removeTtlSeconds)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var removed = 0;
        foreach (var kv in _servers)
        {
            var age = now - kv.Value.LastHeartbeatUnix;
            if (age > removeTtlSeconds)
            {
                if (_servers.TryRemove(kv.Key, out _)) removed++;
            }
            else if (age > offlineTtlSeconds)
            {
                kv.Value.Status = "offline";
            }
        }
        return Task.FromResult(removed);
    }
}
