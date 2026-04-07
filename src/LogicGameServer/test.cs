using Core;
using Core.Components;
using Core.Ecs;
using Microsoft.Extensions.Logging;

namespace LogicServer;

public class TestReview(ILogger<LogicGameServer> logger) : BaseGameServer(nameof(LogicGameServer), logger)
{
    public override async Task StartAsync(int port)
    {
        AddComponent(new EcsWorldComponent());
        AddComponent(new HeartbeatClientComponent(
            message => LogHeartbeat(message),
            type: "logic",
            portProvider: () => ListeningPort,
            playerCountProvider: () => 0,
            statusProvider: () => "online",
            baseDir: AppContext.BaseDirectory
        ));

        await base.StartAsync(port);
    }
}
