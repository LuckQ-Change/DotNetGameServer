using Core;
using Core.Components;
using DotNetGameServer;
using LoginServer.Components;
using LoginServer.Handlers;
using Microsoft.Extensions.Logging;

namespace LoginServer;

public class LoginGameServer : BaseGameServer
{
    private readonly IUserAuthComponent _userAuthComponent;

    // ILogger and IUserAuthComponent will be provided by the DI container
    public LoginGameServer(ILogger<LoginGameServer> logger, IUserAuthComponent userAuthComponent)
        : base("Login", logger)
    {
        _userAuthComponent = userAuthComponent;
    }

    public override async Task StartAsync(int port)
    {
        // Register strong-typed message handlers
        // The handler now requires the auth component
        RegisterStrongMessageHandler<LoginRequest, LoginResponse>(new LoginMessageHandler(_userAuthComponent));

        // Add the auth component that was injected
        AddComponent(_userAuthComponent);
        Log($"Using '{_userAuthComponent.GetType().Name}' for user authentication.");

        // Add heartbeat component
        AddComponent(new HeartbeatClientComponent(
            message => Log(message),
            type: "login",
            portProvider: () => ListeningPort,
            playerCountProvider: () => 0, // Login server doesn't track players
            statusProvider: () => "online",
            baseDir: AppContext.BaseDirectory
        ));

        // Call the base StartAsync to start the server listener
        await base.StartAsync(port);
    }
}
