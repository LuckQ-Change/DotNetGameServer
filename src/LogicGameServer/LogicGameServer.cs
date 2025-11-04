using Core;
using Core.Components;
using Core.Ecs;
using DotNetGameServer;
using LogicServer.SignIn.Handlers;
using Microsoft.Extensions.Logging;

namespace LogicServer;

public class LogicGameServer : BaseGameServer
{
    public LogicGameServer(ILogger<LogicGameServer> logger) : base("Logic", logger)
    {
    }

    public override async Task StartAsync(int port)
    {
        // Register message handlers
        RegisterStrongMessageHandler<QuerySignInStatusRequest, QuerySignInStatusResponse>(new QuerySignInStatusMessageHandler());
        RegisterStrongMessageHandler<ClaimSignInRewardRequest, ClaimSignInRewardResponse>(new ClaimSignInRewardMessageHandler());

        // Add essential components
        AddComponent(new EcsWorldComponent());
        AddComponent(new HeartbeatClientComponent(
            message => Log(message),
            type: "logic",
            portProvider: () => ListeningPort,
            playerCountProvider: () => 0, // Replace with actual player count if available
            statusProvider: () => "online",
            baseDir: AppContext.BaseDirectory
        ));

        await base.StartAsync(port);
    }
}
