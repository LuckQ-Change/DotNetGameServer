using Core.Configuration;

using Microsoft.Extensions.Logging;

namespace Core.Directory;

/// <summary>
/// 可选的服务发现协议端点。应用也可以通过其他传输方式暴露
/// <see cref="IServerRegistry"/>，无需使用该默认服务器实现。
/// </summary>
public sealed class ServiceDiscoveryServer : BaseGameServer
{
    public ServiceDiscoveryServer(
        ILogger<ServiceDiscoveryServer> logger,
        IServerRegistry registry,
        ServerOptions serverOptions,
        AuthenticationOptions authenticationOptions,
        ServiceDiscoveryOptions? discoveryOptions = null)
        : base(
            nameof(ServiceDiscoveryServer),
            logger,
            serverOptions,
            authenticationOptions,
            allowedChannels: [DotNetGameServer.CommunicationChannel.Internal])
    {
        AddComponent<IServerRegistry>(registry);
        AddComponent(new RegistryCleanupComponent(registry, discoveryOptions ?? new ServiceDiscoveryOptions()));
        RegisterInternalMessageHandler(new HeartbeatHandler(registry));
        RegisterInternalMessageHandler(new ServerUnregisterHandler(registry));
        RegisterInternalMessageHandler(new ServerQueryHandler(registry));
    }
}
