using Core;
using Core.Components;
using DotNetGameServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ResourceServer;

public class ResourceGameServer : BaseGameServer
{
    private readonly IConfiguration? _configuration;

    public ResourceGameServer(ILogger<ResourceGameServer> logger) : base("Resource", logger)
    {
        _configuration = null;
    }

    public ResourceGameServer(ILogger<ResourceGameServer> logger, IConfiguration configuration) : base("Resource", logger)
    {
        _configuration = configuration;
    }

    public override async Task StartAsync(int port)
    {
        // Register message handlers
        RegisterStrongMessageHandler<QueryVersionRequest, QueryVersionResponse>(new QueryVersionMessageHandler());
        RegisterStrongMessageHandler<GetBundleRequest, GetBundleResponse>(new GetBundleMessageHandler());

        // 资源根目录可配置：优先读取 appsettings resource:root，其次环境变量 RESOURCE_ROOT，最后默认程序根目录下的 Resources
        var resourceRoot = _configuration?["resource:root"] ?? Environment.GetEnvironmentVariable("RESOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(resourceRoot))
        {
            resourceRoot = Path.Combine(AppContext.BaseDirectory, "Resources");
        }
        AddComponent<IResourceRepositoryComponent>(new FileSystemResourceRepository(resourceRoot));
        Log($"Resource root directory: {resourceRoot}");

        AddComponent(new UrlSignerComponent());
        AddComponent(new HeartbeatClientComponent(
            message => Log(message),
            type: "resource",
            portProvider: () => ListeningPort,
            playerCountProvider: () => 0,
            statusProvider: () => "online",
            baseDir: AppContext.BaseDirectory
        ));

        await base.StartAsync(port);
    }
}
