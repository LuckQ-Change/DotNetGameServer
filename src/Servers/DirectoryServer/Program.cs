using Core.Components;
using Core.Configuration;
using Core.Directory;

using DirectoryServer;

using Hosting;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

await GameServerHost.RunAsync(args, new GameServerDefinition
{
    Role = "directory",
    DefaultPort = 5500,
    RequiresProductionTokenSecret = true,
    ConfigureServices = (services, _) =>
    {
        services.AddSingleton<IServerRegistry>(sp =>
        {
            var dir = sp.GetRequiredService<IOptions<DirectoryOptions>>().Value;
            if (string.Equals(dir.Registry.Mode, "redis", StringComparison.OrdinalIgnoreCase))
                return new RedisServerRegistry(dir.Registry.Redis.ConnectionString, dir.Registry.Redis, dir.Registry.Redis.CleanupIntervalSeconds);
            return new MemoryServerRegistry();
        });
    },
    CreateServer = ctx =>
    {
        var registry = ctx.Services.GetRequiredService<IServerRegistry>();
        var logger = ctx.Services.GetRequiredService<ILogger<DirectoryGameServer>>();
        var dirOpts = ctx.Services.GetRequiredService<IOptions<DirectoryOptions>>().Value;

        var server = new DirectoryGameServer(logger, registry, ctx.ServerOptions);

        // 目录需持有真实密钥以校验心跳签名（默认 dev-secret 仅供本地）。
        var tokenSecret = string.IsNullOrWhiteSpace(dirOpts.TokenSecret) ? "dev-secret" : dirOpts.TokenSecret;
        server.AddComponent<ITokenSecretProvider>(new TokenSecretProviderComponent(tokenSecret));

        server.AddComponent<IServerRegistry>(registry);
        server.AddComponent(new DirectoryCleanupService(registry, dirOpts));
        return server;
    },
    ConfigureEndpoints = app =>
    {
        app.MapGet("/api/routing/query", RoutingApi.QueryAsync);
    }
});
