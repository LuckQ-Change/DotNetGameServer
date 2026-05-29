using Core.Components;
using Core.Configuration;
using Core.Directory;

using DirectoryServer;

using Hosting;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using StackExchange.Redis;

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

        // Redis 连接复用：Registry 和 RevocationStore 共享同一 Multiplexer。
        // 使用 RedisConnectionHolder 封装可选连接，避免 DI 注册 nullable 服务类型。
        services.AddSingleton(sp =>
        {
            var dir = sp.GetRequiredService<IOptions<DirectoryOptions>>().Value;
            if (!string.Equals(dir.Registry.Mode, "redis", StringComparison.OrdinalIgnoreCase))
                return new RedisConnectionHolder(null);
            try { return new RedisConnectionHolder(ConnectionMultiplexer.Connect(dir.Registry.Redis.ConnectionString)); }
            catch (Exception ex)
            {
                sp.GetRequiredService<ILogger<RedisTokenRevocationStore>>()
                  .LogWarning("Redis 吊销存储初始化失败，退化为内存模式: {Message}", ex.Message);
                return new RedisConnectionHolder(null);
            }
        });
    },
    CreateServer = ctx =>
    {
        var registry = ctx.Services.GetRequiredService<IServerRegistry>();
        var logger = ctx.Services.GetRequiredService<ILogger<DirectoryGameServer>>();
        var dirOpts = ctx.Services.GetRequiredService<IOptions<DirectoryOptions>>().Value;

        var server = new DirectoryGameServer(logger, registry, ctx.ServerOptions);

        var tokenSecret = string.IsNullOrWhiteSpace(dirOpts.TokenSecret) ? "dev-secret" : dirOpts.TokenSecret;
        server.AddComponent<ITokenSecretProvider>(new TokenSecretProviderComponent(tokenSecret));

        // Redis 模式下使用分布式吊销存储，内存模式退化为单机存储。
        var redisConn = ctx.Services.GetRequiredService<RedisConnectionHolder>().Connection;
        ITokenRevocationStore revocationStore = redisConn != null
            ? new RedisTokenRevocationStore(redisConn)
            : new InMemoryTokenRevocationStore();
        server.AddComponent<ITokenRevocationStore>(revocationStore);

        server.AddComponent<IServerRegistry>(registry);
        server.AddComponent(new DirectoryCleanupService(registry, dirOpts));
        return server;
    },
    ConfigureEndpoints = app =>
    {
        app.MapGet("/api/routing/query", RoutingApi.QueryAsync);
    }
});
