using Core;
using Core.Components;
using LoginServer.Components;
using LoginServer.Handlers;
using Microsoft.Extensions.Logging;

namespace LoginServer;

public class LoginGameServer(ILogger<LoginGameServer> logger, IUserAuthComponent userAuthComponent) : BaseGameServer(nameof(LoginGameServer), logger)
{
    public override async Task StartAsync(int port)
    {
        RegisterMessageHandler(new LoginMessageHandler(userAuthComponent));

        AddComponent(userAuthComponent);
        Log($"Using '{userAuthComponent.GetType().Name}' for user authentication.");

        AddComponent(new HeartbeatClientComponent(
            message => LogHeartbeat(message),
            type: "login",
            portProvider: () => ListeningPort,
            playerCountProvider: () => 0,
            statusProvider: () => "online",
            baseDir: AppContext.BaseDirectory
        ));

        await base.StartAsync(port);
    }
}
