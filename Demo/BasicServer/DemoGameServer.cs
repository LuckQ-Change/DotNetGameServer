using Core;
using Core.Configuration;

namespace BasicServer;

public sealed class DemoGameServer : BaseGameServer
{
    public DemoGameServer(
        ILogger<DemoGameServer> logger,
        ServerOptions serverOptions,
        AuthenticationOptions authenticationOptions,
        ClientHeartbeatOptions clientHeartbeatOptions)
        : base(
            nameof(DemoGameServer),
            logger,
            serverOptions,
            authenticationOptions,
            clientHeartbeat: clientHeartbeatOptions)
    {
        AddComponent(new DemoLifecycleComponent());
        RegisterClientMessageHandler(new PingHandler());
    }
}
