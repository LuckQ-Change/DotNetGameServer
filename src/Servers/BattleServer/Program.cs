using Hosting;

using BattleServer;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

await GameServerHost.RunAsync(args, new GameServerDefinition
{
    Role = "battle",
    DefaultPort = 5503,
    RequiresProductionTokenSecret = true,
    CreateServer = ctx =>
    {
        var logger = ctx.Services.GetRequiredService<ILogger<BattleGameServer>>();
        var server = new BattleGameServer(logger, ctx.ServerOptions);
        HeartbeatRole.Attach(server, "battle", ctx);
        return server;
    }
});
