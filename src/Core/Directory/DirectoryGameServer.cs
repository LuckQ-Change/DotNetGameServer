using Core.Configuration;

using DotNetGameServer;

using Microsoft.Extensions.Logging;

namespace Core.Directory;

public class DirectoryGameServer : BaseGameServer
{
    public DirectoryGameServer(ILogger<DirectoryGameServer> logger, IServerRegistry registry, ServerOptions options)
        : base(nameof(DirectoryGameServer), logger, options)
    {
        RegisterMessageHandler(new HeartbeatHandler(registry));
        RegisterMessageHandler(new ServerQueryHandler(registry));
    }
}
