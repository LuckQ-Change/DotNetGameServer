using Core.Directory;

using Microsoft.AspNetCore.Http;

namespace DirectoryServer;

/// <summary>
/// 目录路由查询 API：基于注册表与分流策略返回候选服务器。
/// </summary>
internal static class RoutingApi
{
    public static async Task<IResult> QueryAsync(HttpContext context, IServerRegistry registry)
    {
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
