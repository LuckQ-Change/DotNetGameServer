using Core;
using Core.Components;
using Core.Ecs;

using Microsoft.Extensions.Logging;

namespace LogicServer;

public class LogicGameServer(ILogger<LogicGameServer> logger) : BaseGameServer(nameof(LogicGameServer), logger)
{
    public override async Task StartAsync(int port)
    {
        AddComponent(new EcsWorldComponent());
        AddComponent(new HeartbeatClientComponent(
            LogHeartbeat,
            type: "logic",
            portProvider: () => ListeningPort,
            baseDir: AppContext.BaseDirectory
        ));

        await base.StartAsync(port);
    }
}
