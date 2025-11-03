using Core;
using DotNetGameServer;
using Microsoft.Extensions.Logging;

namespace ResourceServer;

/// <summary>
/// 资源服务器：负责版本与AB获取（演示）
/// </summary>
public class ResourceGameServer : BaseGameServer
{
    /// <summary>
    /// 初始化资源服务器并注册版本与资源获取处理器。
    /// </summary>
    /// <param name="log">日志输出委托。</param>
    /// <param name="resourceRoot">资源根目录，默认为 <c>BaseDirectory/Resources</c>。</param>
    public ResourceGameServer(Action<string> log, string? resourceRoot = null) : base("Resource", log)
    {
        RegisterStrongMessageHandler<QueryVersionRequest, QueryVersionResponse>(new QueryVersionMessageHandler());
        RegisterStrongMessageHandler<GetBundleRequest, GetBundleResponse>(new GetBundleMessageHandler());

        var root = resourceRoot ?? Path.Combine(AppContext.BaseDirectory, "Resources");
        AddComponent<IResourceRepositoryComponent>(new FileSystemResourceRepository(root));
        Log($"资源根目录: {root}");
        // URL签名组件
        AddComponent<UrlSignerComponent>(new UrlSignerComponent());
        AddComponent<Core.Components.HeartbeatClientComponent>(
            new Core.Components.HeartbeatClientComponent(
                message => Log(message),
                type: "resource",
                portProvider: () => ListeningPort,
                playerCountProvider: null,
                statusProvider: () => "online",
                baseDir: AppContext.BaseDirectory
            )
        );
    }

    public ResourceGameServer(ILogger logger, string? resourceRoot = null) : base("Resource", logger)
    {
        RegisterStrongMessageHandler<QueryVersionRequest, QueryVersionResponse>(new QueryVersionMessageHandler());
        RegisterStrongMessageHandler<GetBundleRequest, GetBundleResponse>(new GetBundleMessageHandler());

        var root = resourceRoot ?? Path.Combine(AppContext.BaseDirectory, "Resources");
        AddComponent<IResourceRepositoryComponent>(new FileSystemResourceRepository(root));
        Log($"资源根目录: {root}");
        AddComponent<UrlSignerComponent>(new UrlSignerComponent());
        AddComponent<Core.Components.HeartbeatClientComponent>(
            new Core.Components.HeartbeatClientComponent(
                message => Log(message),
                type: "resource",
                portProvider: () => ListeningPort,
                playerCountProvider: null,
                statusProvider: () => "online",
                baseDir: AppContext.BaseDirectory
            )
        );
    }
}