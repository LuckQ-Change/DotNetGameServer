using Core;
using Core.Components;
using Core.Ecs;
using Microsoft.Extensions.Logging;

namespace BattleServer;

public class BattleGameServer(ILogger<BattleGameServer> logger) : BaseGameServer(nameof(BattleGameServer), logger)
{
    public override async Task StartAsync(int port)
    {

        var ecs = new EcsWorldComponent();
        AddComponent(ecs);
        _ = ecs.World.RegisterSystemAsync(new Ecs.BattleGcSystem(TimeSpan.FromMinutes(30)));

        AddComponent(new HeartbeatClientComponent(
            LogHeartbeat,
            type: "battle",
            portProvider: () => ListeningPort,
            baseDir: AppContext.BaseDirectory
        ));

        await base.StartAsync(port);
    }
}
