using Core;
using Core.Components;
using DotNetGameServer;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;

namespace ResourceServer;

public class ResourceGameServer : BaseGameServer
{
    public ResourceGameServer(ILogger<ResourceGameServer> logger) : base("Resource", logger)
    {
    }

    public override async Task StartAsync(int port)
    {
        // Register message handlers
        RegisterStrongMessageHandler<QueryVersionRequest, QueryVersionResponse>(new QueryVersionMessageHandler());
        RegisterStrongMessageHandler<GetBundleRequest, GetBundleResponse>(new GetBundleMessageHandler());

        // TODO: Make the resource root path configurable
        var resourceRoot = Path.Combine(AppContext.BaseDirectory, "Resources");
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