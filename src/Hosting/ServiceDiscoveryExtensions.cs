using Core;
using Core.Components;
using Core.Configuration;
using Core.Heartbeat;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hosting;

/// <summary>将应用服务器注册到服务发现节点的便捷扩展。</summary>
public static class ServiceDiscoveryExtensions
{
    public static void AddServiceRegistration(
        this BaseGameServer server,
        HostServerContext context,
        string serverType,
        Action<ServiceRegistrationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverType);

        var source = context.Services.GetRequiredService<IOptions<ServiceRegistrationOptions>>().Value;
        var options = Clone(source);
        options.ServerType = serverType;
        options.Port = context.Port;
        configure?.Invoke(options);
        if (string.IsNullOrWhiteSpace(options.TokenAudience))
            options.TokenAudience = server.TokenAudience;

        var loadProvider = server.GetComponent<IServerLoadProvider>()
            ?? new MutableServerLoadProvider(options.Capacity);
        server.AddComponent<IServerLoadProvider>(loadProvider);

        var logger = context.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger($"ServiceRegistration.{serverType}");
        server.AddComponent(new HeartbeatSenderService(
            options,
            context.AuthenticationOptions.TokenSecret,
            loadProvider,
            logger));
    }

    private static ServiceRegistrationOptions Clone(ServiceRegistrationOptions source) => new()
    {
        Region = source.Region,
        Capacity = source.Capacity,
        IntervalSeconds = source.IntervalSeconds,
        Host = source.Host,
        Port = source.Port,
        ServerType = source.ServerType,
        ServerId = source.ServerId,
        DiscoveryHost = source.DiscoveryHost,
        DiscoveryPort = source.DiscoveryPort,
        DiscoveryAudience = source.DiscoveryAudience,
        DiscoveryTlsEnabled = source.DiscoveryTlsEnabled,
        DiscoveryTlsServerName = source.DiscoveryTlsServerName,
        Version = source.Version,
        TokenAudience = source.TokenAudience,
        TlsEnabled = source.TlsEnabled,
        TlsServerName = source.TlsServerName
    };
}
