using Hosting;
using LoginServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

await GameServerHost.RunAsync(args, new GameServerDefinition
{
    Role = "login",
    DefaultPort = 5501,
    RequiresProductionTokenSecret = true,
    CreateServer = ctx =>
    {
        var logger = ctx.Services.GetRequiredService<ILogger<LoginGameServer>>();
        var server = new LoginGameServer(logger, ctx.ServerOptions);
        HeartbeatRole.Attach(server, "login", ctx);
        return server;
    }
});
