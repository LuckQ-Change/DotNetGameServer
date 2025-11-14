using Core;
using Shared.Constants;
using Core.Components;
using DotNetGameServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ResourceServer;

public class ResourceGameServer(ILogger<ResourceGameServer> logger, IConfiguration configuration)
    : BaseGameServer(nameof(ResourceGameServer), logger)
{

    public override async Task StartAsync(int port)
    {
        RegisterStrongMessageHandler(new QueryVersionMessageHandler());
        RegisterStrongMessageHandler(new GetBundleMessageHandler());

        // 资源根目录可配置：优先读取 appsettings resource:root，其次环境变量 RESOURCE_ROOT，最后默认程序根目录下的 Resources
        var resourceRoot = configuration[ConfigKeys.ResourceRootConfigKey] ?? Environment.GetEnvironmentVariable(ConfigKeys.ResourceRootEnv);
        if (string.IsNullOrWhiteSpace(resourceRoot))
        {
            resourceRoot = Path.Combine(AppContext.BaseDirectory, "Resources");
        }
        AddComponent<IResourceRepositoryComponent>(new FileSystemResourceRepository(resourceRoot));
        Log($"Resource root directory: {resourceRoot}");

        AddComponent(new UrlSignerComponent());
        AddComponent(new HeartbeatClientComponent(
            message => LogHeartbeat(message),
            type: "resource",
            portProvider: () => ListeningPort,
            playerCountProvider: () => 0,
            statusProvider: () => "online",
            baseDir: AppContext.BaseDirectory
        ));

        await base.StartAsync(port);
    }
}
