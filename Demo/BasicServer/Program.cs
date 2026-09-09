using BasicServer;
using Hosting;

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
        context.ClientHeartbeatOptions),
    ConfigureEndpoints = app => app.MapGet("/", () => Results.Ok(new
    {
        service = "DotNetGameServer.BasicDemo",
        tcpPort = app.Configuration.GetValue<int?>("port") ?? 5500,
        health = "/health",
        readiness = "/ready"
    }))
});
