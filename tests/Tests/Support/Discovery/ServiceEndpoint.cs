namespace Client.Discovery;

public sealed class ServiceEndpoint
{
    public string ServiceId { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string Region { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public int PlayerCount { get; init; }
    public int Capacity { get; init; }
    public bool TlsEnabled { get; init; }
    public string TlsServerName { get; init; } = string.Empty;
    public IReadOnlyList<string> Responsibilities { get; init; } = [];
    public int HeartbeatIntervalSeconds { get; init; }

    public float Load => Capacity > 0 ? Math.Min(1f, (float)PlayerCount / Capacity) : 0f;

    public bool HasResponsibility(string? responsibility)
    {
        if (string.IsNullOrWhiteSpace(responsibility)) return true;
        return Responsibilities.Any(item => string.Equals(item, responsibility, StringComparison.OrdinalIgnoreCase))
            || string.Equals(Role, responsibility, StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString() =>
        $"{Role}@{Host}:{Port}{(TlsEnabled ? " (TLS)" : string.Empty)}";
}

public sealed class ServiceQuery
{
    public string Responsibility { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string RoutingKey { get; set; } = string.Empty;
    public int Limit { get; set; } = 8;
}

public interface IServiceDiscoveryProvider
{
    Task<IReadOnlyList<ServiceEndpoint>> ResolveAsync(ServiceQuery query, CancellationToken cancellationToken);
}

public sealed class StaticServiceDiscoveryProvider : IServiceDiscoveryProvider
{
    private readonly IReadOnlyList<ServiceEndpoint> _endpoints;

    public StaticServiceDiscoveryProvider(IEnumerable<ServiceEndpoint> endpoints)
    {
        _endpoints = endpoints.ToArray();
    }

    public Task<IReadOnlyList<ServiceEndpoint>> ResolveAsync(ServiceQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        query ??= new ServiceQuery();
        IReadOnlyList<ServiceEndpoint> result = _endpoints
            .Where(item => item.Port is > 0 and <= 65535)
            .Where(item => item.HasResponsibility(query.Responsibility))
            .Where(item => string.IsNullOrWhiteSpace(query.Region)
                || string.Equals(item.Region, query.Region, StringComparison.OrdinalIgnoreCase))
            .Take(Math.Max(1, query.Limit))
            .ToArray();
        return Task.FromResult(result);
    }
}
