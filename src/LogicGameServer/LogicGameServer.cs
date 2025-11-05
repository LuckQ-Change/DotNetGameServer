using Core;
using Core.Components;
using Core.Ecs;
using LogicServer.SignIn.Handlers;
using Microsoft.Extensions.Logging;

namespace LogicServer;

public class LogicGameServer(ILogger<LogicGameServer> logger) : BaseGameServer(nameof(LogicGameServer), logger)
{
    public override async Task StartAsync(int port)
    {
        RegisterStrongMessageHandler(new QuerySignInStatusMessageHandler());
        RegisterStrongMessageHandler(new ClaimSignInRewardMessageHandler());

        AddComponent(new EcsWorldComponent());
        AddComponent(new HeartbeatClientComponent(
            message => Log(message),
            type: "logic",
            portProvider: () => ListeningPort,
            playerCountProvider: () => 0,
            statusProvider: () => "online",
            baseDir: AppContext.BaseDirectory
        ));

        await base.StartAsync(port);
    }
}
