using Core.Configuration;
using Core.Rpc;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Hosting;

/// <summary>注册服务发现解析器、跨服长连接池和类型安全 RPC 客户端。</summary>
public static class ServerRpcHostingExtensions
{
    public static IServiceCollection AddGameServerRpc(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ServerRpcOptions>()
            .Bind(configuration.GetSection("serverRpc"))
            .Validate(options => options.ConnectTimeoutMs > 0,
                "serverRpc:connectTimeoutMs 必须大于 0。")
            .Validate(options => options.RequestTimeoutMs > 0,
                "serverRpc:requestTimeoutMs 必须大于 0。")
            .Validate(options => options.MaxFrameSizeBytes >= 1024,
                "serverRpc:maxFrameSizeBytes 不能小于 1024。")
            .Validate(options => options.MaxAttempts is >= 1 and <= 10,
                "serverRpc:maxAttempts 必须在 1 到 10 之间。")
            .Validate(options => options.TokenTtlSeconds > 0,
                "serverRpc:tokenTtlSeconds 必须大于 0。")
            .Validate(options => options.FailureThreshold > 0 && options.FailureCooldownSeconds > 0,
                "serverRpc 熔断阈值和冷却时间必须大于 0。")
            .Validate(options => options.MaxPooledConnections > 0,
                "serverRpc:maxPooledConnections 必须大于 0。")
            .Validate(options => options.IdleConnectionTimeoutSeconds > 0
                && options.PoolCleanupIntervalSeconds > 0,
                "serverRpc 连接空闲超时和清理周期必须大于 0。")
            .ValidateOnStart();
        services.TryAddSingleton<IServerConnectionPool>(serviceProvider =>
        {
            var options = BuildOptions(serviceProvider);
            return new ServerConnectionPool(options);
        });
        services.TryAddSingleton<IServiceResolver>(serviceProvider =>
            new DiscoveryServiceResolver(
                serviceProvider.GetRequiredService<IServerConnectionPool>(),
                serviceProvider.GetRequiredService<IOptions<ServiceRegistrationOptions>>().Value,
                BuildOptions(serviceProvider)));
        services.TryAddSingleton<IServerRpcClient>(serviceProvider =>
            new ServerRpcClient(
                serviceProvider.GetRequiredService<IServiceResolver>(),
                serviceProvider.GetRequiredService<IServerConnectionPool>(),
                BuildOptions(serviceProvider)));
        return services;
    }

    private static ServerRpcOptions BuildOptions(IServiceProvider serviceProvider)
    {
        var configured = serviceProvider.GetRequiredService<IOptions<ServerRpcOptions>>().Value;
        var authentication = serviceProvider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        var registration = serviceProvider.GetRequiredService<IOptions<ServiceRegistrationOptions>>().Value;
        var sourceServerId = configured.SourceServerId;
        if (string.IsNullOrWhiteSpace(sourceServerId)) sourceServerId = registration.ServerId;
        if (string.IsNullOrWhiteSpace(sourceServerId))
            sourceServerId = $"{registration.ServerType}-{Environment.MachineName}-{Environment.ProcessId}";

        return new ServerRpcOptions
        {
            SourceServerId = sourceServerId,
            TokenSecret = string.IsNullOrWhiteSpace(configured.TokenSecret)
                ? authentication.TokenSecret
                : configured.TokenSecret,
            DefaultRegion = configured.DefaultRegion,
            DefaultStrategy = configured.DefaultStrategy,
            ConnectTimeoutMs = configured.ConnectTimeoutMs,
            RequestTimeoutMs = configured.RequestTimeoutMs,
            MaxFrameSizeBytes = configured.MaxFrameSizeBytes,
            MaxAttempts = configured.MaxAttempts,
            TokenTtlSeconds = configured.TokenTtlSeconds,
            CheckCertificateRevocation = configured.CheckCertificateRevocation,
            FailureThreshold = configured.FailureThreshold,
            FailureCooldownSeconds = configured.FailureCooldownSeconds,
            MaxPooledConnections = configured.MaxPooledConnections,
            IdleConnectionTimeoutSeconds = configured.IdleConnectionTimeoutSeconds,
            PoolCleanupIntervalSeconds = configured.PoolCleanupIntervalSeconds
        };
    }
}
