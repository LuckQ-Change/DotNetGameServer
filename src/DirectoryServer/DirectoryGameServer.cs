using Core;
using DotNetGameServer;
using Microsoft.Extensions.Logging;

namespace DirectoryServer;

/// <summary>
/// 目录/网关服务器：提供服务器列表、状态查询与切服令牌签发
/// </summary>
public class DirectoryGameServer : BaseGameServer
{
    public DirectoryGameServer(Action<string> logger) : base("Directory", logger)
    {
        // 注册目录组件（内存注册表）
        var registry = new Components.InMemoryServerRegistryComponent();
        AddComponent<Components.IServerRegistryComponent>(registry);

        // 注册指标组件（定期汇总）
        var metrics = new Components.DirectoryMetricsComponent(message => Log(message), () => registry.ListServers().Count());
        AddComponent<Components.DirectoryMetricsComponent>(metrics);

        // 注册消息处理器（强类型）
        RegisterStrongMessageHandler<QueryServersRequest, QueryServersResponse>(new Handlers.QueryServersMessageHandler());
        RegisterStrongMessageHandler<QueryServerStatusRequest, QueryServerStatusResponse>(new Handlers.QueryServerStatusMessageHandler());
        RegisterStrongMessageHandler<SwitchServerRequest, SwitchServerResponse>(new Handlers.SwitchServerMessageHandler());
        RegisterStrongMessageHandler<HeartbeatRequest, HeartbeatResponse>(new Handlers.HeartbeatMessageHandler());
    }

    public DirectoryGameServer(ILogger logger) : base("Directory", logger)
    {
        var registry = new Components.InMemoryServerRegistryComponent();
        AddComponent<Components.IServerRegistryComponent>(registry);

        var metrics = new Components.DirectoryMetricsComponent(message => Log(message), () => registry.ListServers().Count());
        AddComponent<Components.DirectoryMetricsComponent>(metrics);

        RegisterStrongMessageHandler<QueryServersRequest, QueryServersResponse>(new Handlers.QueryServersMessageHandler());
        RegisterStrongMessageHandler<QueryServerStatusRequest, QueryServerStatusResponse>(new Handlers.QueryServerStatusMessageHandler());
        RegisterStrongMessageHandler<SwitchServerRequest, SwitchServerResponse>(new Handlers.SwitchServerMessageHandler());
        RegisterStrongMessageHandler<HeartbeatRequest, HeartbeatResponse>(new Handlers.HeartbeatMessageHandler());
    }
}