using Core;
using Core.Components;
using Core.Ecs;
using DotNetGameServer;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace BattleServer;

public class BattleGameServer : BaseGameServer
{
    public BattleGameServer(ILogger<BattleGameServer> logger) : base("Battle", logger)
    {
    }

    public override async Task StartAsync(int port)
    {
        RegisterStrongMessageHandler<StartBattleRequest, StartBattleResponse>(new StartBattleMessageHandler());
        RegisterStrongMessageHandler<AttackRequest, AttackResponse>(new AttackMessageHandler());

        var ecs = new EcsWorldComponent();
        AddComponent(ecs);
        // Register the ECS system
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