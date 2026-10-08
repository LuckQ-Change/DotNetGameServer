using System.Data.Common;

using BasicServer;

using Hosting;

using Microsoft.Data.Sqlite;

if (!DbProviderFactories.TryGetFactory("Microsoft.Data.Sqlite", out _))
    DbProviderFactories.RegisterFactory("Microsoft.Data.Sqlite", SqliteFactory.Instance);

await GameServerHost.RunAsync(args, new GameServerDefinition
{
    Role = "demo",
    DefaultPort = 5500,
    RequiresProductionTokenSecret = false,
    Responsibilities = ["gameplay", "demo", "ping"],
    ProtocolVersion = "1.0.0",
    CreateServer = context => new DemoGameServer(
        context.Services.GetRequiredService<ILogger<DemoGameServer>>(),
        context.ServerOptions,
        context.AuthenticationOptions,
        context.ClientHeartbeatOptions,
        context.Services.GetService<Core.Persistence.IGameDatabase>()),
    ConfigureEndpoints = app => app.MapGet("/", () => Results.Ok(new
    {
        service = "DotNetGameServer.BasicDemo",
        tcpPort = app.Configuration.GetValue<int?>("port") ?? 5500,
        health = "/health",
        readiness = "/ready"
    }))
});
