using Core;
using Core.Configuration;
using Core.Persistence;

namespace BasicServer;

public sealed class DemoGameServer : BaseGameServer
{
    public DemoGameServer(
        ILogger<DemoGameServer> logger,
        ServerOptions serverOptions,
        AuthenticationOptions authenticationOptions,
        ClientHeartbeatOptions clientHeartbeatOptions,
        string pluginDirectory,
        IGameDatabase? database = null)
        : base(
            nameof(DemoGameServer),
            logger,
            serverOptions,
            authenticationOptions,
            clientHeartbeat: clientHeartbeatOptions)
    {
        AddComponent(new DemoLifecycleComponent());
        AddComponent(new HotReloadDirectory(this, pluginDirectory));
        RegisterClientMessageHandler(new PingHandler());
        RegisterAnonymousClientMessageHandler(new GuestLoginHandler());
        RegisterAnonymousClientMessageHandler(new RefreshSessionHandler());
        if (database is null) return;

        AddComponent(new NoteSchemaComponent(database));
        RegisterClientMessageHandler(new SaveNoteHandler(database));
        RegisterClientMessageHandler(new LoadNoteHandler(database));
    }
}
