using BattleServer.Handlers;
using Core;
using Core.Components;
using Core.Ecs;
using Microsoft.Extensions.Logging;

namespace BattleServer;

public class BattleGameServer(ILogger<BattleGameServer> logger) : BaseGameServer(nameof(BattleGameServer), logger)
{
    public override async Task StartAsync(int port)
    {
        RegisterStrongMessageHandler(new StartBattleMessageHandler());
        RegisterStrongMessageHandler(new AttackMessageHandler());

        var ecs = new EcsWorldComponent();
        AddComponent(ecs);
        _ = ecs.World.RegisterSystemAsync(new Ecs.BattleGcSystem(TimeSpan.FromMinutes(30)));

        AddComponent(new HeartbeatClientComponent(
            message => Log(message),
            type: "battle",
            portProvider: () => ListeningPort,
            playerCountProvider: () => 0, // Replace with actual player count
            statusProvider: () => "online",
            baseDir: AppContext.BaseDirectory
        ));

        await base.StartAsync(port);
    }
}
