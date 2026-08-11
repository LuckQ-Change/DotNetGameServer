using Core.Directory;

namespace Core.Rpc;

/// <summary>直接基于已注入注册表解析节点，适合内嵌发现和测试场景。</summary>
public sealed class RegistryServiceResolver(IServerRegistry registry) : IServiceResolver
{
    public async Task<IReadOnlyList<ServerEndpoint>> ResolveAsync(
        string serverType,
        string? region,
        string? strategy,
        string? routingKey,
        int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registrations = await registry.QueryAsync(serverType, region, cancellationToken);
        var selected = ServerSelectionStrategyFactory.Create(strategy)
            .Select(registrations, limit, routingKey);
        return selected.Select(ToEndpoint).ToArray();
    }

    private static ServerEndpoint ToEndpoint(ServerRegistration registration) => new(
        registration.ServerId,
        registration.ServerType,
        registration.Host,
        registration.Port,
        string.IsNullOrWhiteSpace(registration.TokenAudience)
            ? registration.ServerType
            : registration.TokenAudience,
        registration.TlsEnabled,
        registration.TlsServerName);
}
