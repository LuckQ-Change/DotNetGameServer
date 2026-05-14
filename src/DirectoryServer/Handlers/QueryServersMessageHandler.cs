using Core;
using DirectoryServer.Components;
using DotNetGameServer;
using Shared.Time;

namespace DirectoryServer.Handlers;

public class QueryServersMessageHandler : IMessageHandler<c2s_query_servers, s2c_query_servers>
{
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromSeconds(5);
    private static readonly int ThrottleMaxRequests = 10;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime windowStartUtc, int count)> Throttle = new();

    public Task<s2c_query_servers?> HandleAsync(c2s_query_servers request, IServerContext context)
    {
        var ip = context.RemoteAddress ?? "unknown";
        var nowUtc = TimeManager.UtcNow;
        var entry = Throttle.GetOrAdd(ip, _ => (nowUtc, 0));
        if (nowUtc - entry.windowStartUtc > ThrottleWindow)
        {
            Throttle[ip] = (nowUtc, 1);
        }
        else
        {
            if (entry.count + 1 > ThrottleMaxRequests)
            {
                return Task.FromResult<s2c_query_servers?>(new s2c_query_servers());
            }

            Throttle[ip] = (entry.windowStartUtc, entry.count + 1);
        }

        var registry = context.GetComponent<IServerRegistryComponent>();
        if (registry == null)
        {
            return Task.FromResult<s2c_query_servers?>(new s2c_query_servers());
        }

        var all = registry.ListServers();
        all = all.Where(server => string.Equals(server.Type, "logic", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(request.Region))
        {
            all = all.Where(server => string.Equals(server.Region, request.Region, StringComparison.OrdinalIgnoreCase));
        }

        var now = TimeManager.UtcNow;
        var strategy = (request.Strategy ?? string.Empty).ToLowerInvariant();
        IEnumerable<ServerRecord> ordered = strategy switch
        {
            "load" => all
                .OrderBy(server => server.PlayerCount * 1.0 / Math.Max(1, server.Capacity))
                .ThenByDescending(server => server.LastHeartbeatUtc),
            "latency" => all
                .OrderBy(server => server.AvgRttMs > 0
                    ? server.AvgRttMs
                    : (int)(now - server.LastHeartbeatUtc).TotalMilliseconds)
                .ThenBy(server => server.PlayerCount * 1.0 / Math.Max(1, server.Capacity)),
            "weighted" or "load_latency" => BuildWeightedOrder(all, now),
            _ => all.OrderByDescending(server => server.LastHeartbeatUtc)
        };

        if (request.Limit > 0)
        {
            ordered = ordered.Take(request.Limit);
        }

        var response = new s2c_query_servers();
        foreach (var server in ordered)
        {
            response.Servers.Add(new server_info
            {
                Id = server.Id,
                Type = server.Type,
                Region = server.Region,
                Host = server.Host,
                Port = server.Port,
                PlayerCount = server.PlayerCount,
                Capacity = server.Capacity,
                Status = server.Status,
                LastHeartbeat = new DateTimeOffset(server.LastHeartbeatUtc).ToUnixTimeMilliseconds(),
                AvgRttMs = server.AvgRttMs
            });
        }

        return Task.FromResult<s2c_query_servers?>(response);
    }

    private static IEnumerable<ServerRecord> BuildWeightedOrder(IEnumerable<ServerRecord> all, DateTime now)
    {
        var list = all.ToList();
        var minRtt = list.Where(server => server.AvgRttMs > 0).Select(server => (double)server.AvgRttMs).DefaultIfEmpty(0).Min();
        var maxRtt = list.Where(server => server.AvgRttMs > 0).Select(server => (double)server.AvgRttMs).DefaultIfEmpty(0).Max();
        if (maxRtt <= minRtt)
        {
            maxRtt = minRtt + 1;
        }

        const double loadWeight = 0.6;
        const double latencyWeight = 0.4;
        return list
            .Select(server =>
            {
                var load = server.PlayerCount * 1.0 / Math.Max(1, server.Capacity);
                var rtt = server.AvgRttMs > 0
                    ? server.AvgRttMs
                    : (int)(now - server.LastHeartbeatUtc).TotalMilliseconds;
                var rttNorm = (rtt - minRtt) / (maxRtt - minRtt);
                var score = loadWeight * load + latencyWeight * rttNorm;
                return (server, score);
            })
            .OrderBy(item => item.score)
            .ThenByDescending(item => item.server.LastHeartbeatUtc)
            .Select(item => item.server);
    }
}
