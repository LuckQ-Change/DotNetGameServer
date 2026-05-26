using Hosting;

using LogicServer;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

await GameServerHost.RunAsync(args, new GameServerDefinition
{
    Role = "logic",
    DefaultPort = 5502,
    RequiresProductionTokenSecret = true,
    CreateServer = ctx =>
    {
        var logger = ctx.Services.GetRequiredService<ILogger<LogicGameServer>>();
        var server = new LogicGameServer(logger, ctx.ServerOptions);
        HeartbeatRole.Attach(server, "logic", ctx);
        return server;
    }
});
