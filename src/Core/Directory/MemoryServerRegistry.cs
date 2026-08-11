using System.Collections.Concurrent;

namespace Core.Directory;

public class MemoryServerRegistry : IServerRegistry
{
    private readonly ConcurrentDictionary<string, ServerRegistration> _servers = new(StringComparer.OrdinalIgnoreCase);

    public string Name => "MemoryServerRegistry";
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DestroyAsync() => Task.CompletedTask;

    public Task<bool> RegisterAsync(ServerRegistration registration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(registration);
        if (string.IsNullOrWhiteSpace(registration.ServerId)
            || string.IsNullOrWhiteSpace(registration.ServerType)
            || string.IsNullOrWhiteSpace(registration.Host)
            || registration.Port is <= 0 or > 65535
            || registration.PlayerCount < 0
            || registration.Capacity <= 0)
            return Task.FromResult(false);

        var stored = Clone(registration);
        stored.LastHeartbeatUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _servers[stored.ServerId] = stored;
        return Task.FromResult(true);
    }

    public Task<bool> UnregisterAsync(string serverId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_servers.TryRemove(serverId, out _));
    }

    public Task<ServerRegistration?> GetAsync(string serverId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _servers.TryGetValue(serverId, out var registration);
        return Task.FromResult(registration is null ? null : Clone(registration));
    }

    public Task<List<ServerRegistration>> QueryAsync(
        string? serverType = null,
        string? region = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = _servers.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(serverType))
            result = result.Where(s => string.Equals(s.ServerType, serverType, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(region))
            result = result.Where(s => string.Equals(s.Region, region, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(result.Select(Clone).ToList());
    }

    public Task<List<ServerRegistration>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_servers.Values.Select(Clone).ToList());
    }

    public Task<int> CleanupStaleAsync(
        int offlineTtlSeconds,
        int removeTtlSeconds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var removed = 0;
        foreach (var kv in _servers)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

    private static ServerRegistration Clone(ServerRegistration source) => new()
    {
        ServerId = source.ServerId,
        ServerType = source.ServerType,
        Host = source.Host,
        Port = source.Port,
        PlayerCount = source.PlayerCount,
        Capacity = source.Capacity,
        Region = source.Region,
        Version = source.Version,
        LastHeartbeatUnix = source.LastHeartbeatUnix,
        Status = source.Status,
        TokenAudience = source.TokenAudience,
        TlsEnabled = source.TlsEnabled,
        TlsServerName = source.TlsServerName
    };
}
