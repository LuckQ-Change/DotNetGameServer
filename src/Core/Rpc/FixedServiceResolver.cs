namespace Core.Rpc;

/// <summary>使用固定节点集合的解析器，适合单体部署、测试或外部配置中心适配。</summary>
public sealed class FixedServiceResolver : IServiceResolver
{
    private readonly ServerEndpoint[] _endpoints;

    public FixedServiceResolver(IEnumerable<ServerEndpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        _endpoints = endpoints.ToArray();
    }

    public Task<IReadOnlyList<ServerEndpoint>> ResolveAsync(
        string serverType,
        string? region,
        string? strategy,
        string? routingKey,
        int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = _endpoints
            .Where(endpoint => string.Equals(endpoint.ServerType, serverType, StringComparison.OrdinalIgnoreCase))
            .Take(Math.Max(1, limit))
            .ToArray();
        return Task.FromResult<IReadOnlyList<ServerEndpoint>>(result);
    }
}
