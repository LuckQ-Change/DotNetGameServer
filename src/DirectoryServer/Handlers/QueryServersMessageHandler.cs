using Core;
using DotNetGameServer;
using DirectoryServer.Components;

namespace DirectoryServer.Handlers;

public class QueryServersMessageHandler : IMessageHandler<QueryServersRequest, QueryServersResponse>
{
    private static readonly TimeSpan _throttleWindow = TimeSpan.FromSeconds(5);
    private static readonly int _throttleMaxRequests = 10; // 每窗口最多请求数
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime windowStartUtc, int count)> _throttle = new();

    public Task<QueryServersResponse?> HandleAsync(QueryServersRequest request, IServerContext context)
    {
        // 按IP限流（轻量本地实现）
        var ip = context.RemoteAddress ?? "unknown";
        var nowUtc = DateTime.UtcNow;
        var entry = _throttle.GetOrAdd(ip, _ => (nowUtc, 0));
        if (nowUtc - entry.windowStartUtc > _throttleWindow)
        {
            _throttle[ip] = (nowUtc, 1);
        }
        else
        {
            if (entry.count + 1 > _throttleMaxRequests)
            {
                // 超限：返回空列表，避免打爆目录
                var limitedResp = new QueryServersResponse();
                return Task.FromResult<QueryServersResponse?>(limitedResp);
            }
            _throttle[ip] = (entry.windowStartUtc, entry.count + 1);
        }

        var reg = context.GetComponent<IServerRegistryComponent>();
        if (reg == null)
        {
            var respFail = new QueryServersResponse();
            return Task.FromResult<QueryServersResponse?>(respFail);
        }

        var all = reg.ListServers();
        all = all.Where(s => string.Equals(s.Type, "logic", StringComparison.OrdinalIgnoreCase));

        // 区域过滤
        if (!string.IsNullOrWhiteSpace(request.Region))
        {
            all = all.Where(s => string.Equals(s.Region, request.Region, StringComparison.OrdinalIgnoreCase));
        }

        // 智能策略排序
        IEnumerable<ServerRecord> ordered;
        var now = DateTime.UtcNow;
        var strategy = (request.Strategy ?? string.Empty).ToLowerInvariant();
        if (strategy == "load")
        {
            ordered = all
                .OrderBy(s => s.PlayerCount * 1.0 / Math.Max(1, s.Capacity))
                .ThenByDescending(s => s.LastHeartbeatUtc); // 心跳越新越前
        }
        else if (strategy == "latency")
        {
            // 兼容：优先使用 AvgRttMs，否则以心跳新鲜度估算
            ordered = all
                .OrderBy(s => s.AvgRttMs > 0 ? s.AvgRttMs : (int)(now - s.LastHeartbeatUtc).TotalMilliseconds)
                .ThenBy(s => s.PlayerCount * 1.0 / Math.Max(1, s.Capacity));
        }
        else if (strategy == "weighted" || strategy == "load_latency")
        {
            // 组合权重：load 与 latency 按 0.6/0.4 计算综合分，候选按分值升序
            var list = all.ToList();
            double minRtt = list.Where(s => s.AvgRttMs > 0).Select(s => (double)s.AvgRttMs).DefaultIfEmpty(0).Min();
            double maxRtt = list.Where(s => s.AvgRttMs > 0).Select(s => (double)s.AvgRttMs).DefaultIfEmpty(0).Max();
            if (maxRtt <= minRtt) { maxRtt = minRtt + 1; }

            double wLoad = 0.6, wLatency = 0.4;
            ordered = list
                .Select(s =>
                {
                    var load = s.PlayerCount * 1.0 / Math.Max(1, s.Capacity);
                    var rtt = s.AvgRttMs > 0 ? s.AvgRttMs : (int)(now - s.LastHeartbeatUtc).TotalMilliseconds;
                    var rttNorm = (rtt - minRtt) / (maxRtt - minRtt);
                    var score = wLoad * load + wLatency * rttNorm;
                    return (s, score);
                })
                .OrderBy(t => t.score)
                .ThenByDescending(t => t.s.LastHeartbeatUtc)
                .Select(t => t.s);
        }
        else
        {
            ordered = all.OrderByDescending(s => s.LastHeartbeatUtc); // 默认：最新心跳靠前
        }

        // 限制数量
        if (request.Limit > 0)
        {
            ordered = ordered.Take(request.Limit);
        }

        var resp = new QueryServersResponse();
        foreach (var s in ordered)
        {
            resp.Servers.Add(new ServerInfo
            {
                Id = s.Id,
                Type = s.Type,
                Region = s.Region,
                Host = s.Host,
                Port = s.Port,
                PlayerCount = s.PlayerCount,
                Capacity = s.Capacity,
                Status = s.Status,
                LastHeartbeat = new DateTimeOffset(s.LastHeartbeatUtc).ToUnixTimeMilliseconds(),
                AvgRttMs = s.AvgRttMs
            });
        }
        return Task.FromResult<QueryServersResponse?>(resp);
    }
}
