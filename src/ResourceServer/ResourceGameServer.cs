using Core;
using DotNetGameServer;
using Microsoft.Extensions.Logging;
using Core.Db;

namespace ResourceServer;

public class ResourceGameServer : BaseGameServer
{
    private readonly GameDbContext _dbContext;

    public ResourceGameServer(GameDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public override void Start()
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