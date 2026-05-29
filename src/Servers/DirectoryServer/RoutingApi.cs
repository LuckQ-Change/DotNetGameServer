using Core.Directory;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DirectoryServer;

/// <summary>
/// 目录路由查询 API：基于注册表与分流策略返回候选服务器。
/// 生产环境需在配置中设置 DIRECTORY_API_TOKEN，并通过 Authorization: Bearer &lt;token&gt; 访问。
/// </summary>
internal static class RoutingApi
{
    public static async Task<IResult> QueryAsync(HttpContext context, IServerRegistry registry,
        IConfiguration cfg, IHostEnvironment env)
    {
        if (!env.IsDevelopment())
        {
            var expectedToken = cfg["DIRECTORY_API_TOKEN"];
            if (!string.IsNullOrWhiteSpace(expectedToken))
            {
                var auth = context.Request.Headers["Authorization"].ToString();
                if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    return Results.Unauthorized();
                var provided = auth["Bearer ".Length..].Trim();
                if (!string.Equals(provided, expectedToken, StringComparison.Ordinal))
                    return Results.Unauthorized();
            }
        }

        var serverType = context.Request.Query["type"].FirstOrDefault();
        var region = context.Request.Query["region"].FirstOrDefault();
        var strategy = context.Request.Query["strategy"].FirstOrDefault() ?? "load";
        var limitStr = context.Request.Query["limit"].FirstOrDefault();
        int.TryParse(limitStr, out var limit);
        if (limit <= 0) limit = 10;

        var candidates = await registry.QueryAsync(serverType, region);
        var selector = ServerSelectionStrategyFactory.Create(strategy);
        var selected = selector.Select(candidates, limit);

        var result = selected.Select(s => new
        {
            id = s.ServerId,
            type = s.ServerType,
            region = s.Region,
            host = s.Host,
            port = s.Port,
            playerCount = s.PlayerCount,
            capacity = s.Capacity,
            load = s.Load,
            status = s.Status,
            lastHeartbeat = s.LastHeartbeatUnix,
            version = s.Version
        }).ToArray();

        return Results.Json(new { servers = result, count = result.Length, strategy });
    }
}
