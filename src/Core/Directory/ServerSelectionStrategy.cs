using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Core.Directory;

public interface IServerSelectionStrategy
{
    List<ServerRegistration> Select(
        List<ServerRegistration> candidates,
        int limit,
        string? routingKey = null);
}

public sealed class LoadBasedStrategy : IServerSelectionStrategy
{
    public List<ServerRegistration> Select(List<ServerRegistration> candidates, int limit, string? routingKey = null) =>
        SelectionCandidates.Online(candidates)
            .OrderBy(s => s.Load)
            .ThenBy(s => s.PlayerCount)
            .ThenByDescending(s => s.LastHeartbeatUnix)
            .Take(SelectionCandidates.NormalizeLimit(limit, candidates.Count))
            .ToList();
}

public sealed class RandomStrategy : IServerSelectionStrategy
{
    public List<ServerRegistration> Select(List<ServerRegistration> candidates, int limit, string? routingKey = null) =>
        SelectionCandidates.Online(candidates)
            .OrderBy(_ => Random.Shared.Next())
            .Take(SelectionCandidates.NormalizeLimit(limit, candidates.Count))
            .ToList();
}

/// <summary>按剩余容量加权随机；剩余槽位更多的节点被选中的概率更高。</summary>
public sealed class WeightedRandomStrategy : IServerSelectionStrategy
{
    public List<ServerRegistration> Select(List<ServerRegistration> candidates, int limit, string? routingKey = null)
    {
        var pool = SelectionCandidates.Online(candidates).ToList();
        var take = SelectionCandidates.NormalizeLimit(limit, pool.Count);
        var result = new List<ServerRegistration>(take);
        while (result.Count < take && pool.Count > 0)
        {
            var weights = pool.Select(s => Math.Max(1L, (long)s.Capacity - s.PlayerCount)).ToArray();
            var total = weights.Sum();
            var roll = Random.Shared.NextInt64(total);
            var index = 0;
            while (index < weights.Length - 1 && roll >= weights[index])
            {
                roll -= weights[index];
                index++;
            }
            result.Add(pool[index]);
            pool.RemoveAt(index);
        }
        return result;
    }
}

public sealed class RoundRobinStrategy : IServerSelectionStrategy
{
    private long _counter = -1;

    public List<ServerRegistration> Select(List<ServerRegistration> candidates, int limit, string? routingKey = null)
    {
        var online = SelectionCandidates.Online(candidates).ToList();
        if (online.Count == 0) return [];
        var take = SelectionCandidates.NormalizeLimit(limit, online.Count);
        var offset = (int)(Interlocked.Increment(ref _counter) % online.Count);
        var result = new List<ServerRegistration>(take);
        for (var i = 0; i < take; i++)
            result.Add(online[(offset + i) % online.Count]);
        return result;
    }
}

/// <summary>Rendezvous Hash，一致性地把同一 routingKey 路由到相同候选节点。</summary>
public sealed class ConsistentHashStrategy : IServerSelectionStrategy
{
    public List<ServerRegistration> Select(List<ServerRegistration> candidates, int limit, string? routingKey = null)
    {
        if (string.IsNullOrWhiteSpace(routingKey))
            return new LoadBasedStrategy().Select(candidates, limit);

        return SelectionCandidates.Online(candidates)
            .OrderByDescending(server => Score(routingKey, server.ServerId))
            .Take(SelectionCandidates.NormalizeLimit(limit, candidates.Count))
            .ToList();
    }

    private static ulong Score(string routingKey, string serverId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{routingKey}|{serverId}"));
        return BitConverter.ToUInt64(bytes, 0);
    }
}

public static class ServerSelectionStrategyFactory
{
    private static readonly ConcurrentDictionary<string, IServerSelectionStrategy> StrategyCache = new();

    public static IServerSelectionStrategy Create(string? strategy)
    {
        var strategyName = (strategy ?? "load").ToLowerInvariant();
        return StrategyCache.GetOrAdd(strategyName, name => name switch
        {
            "random" => new RandomStrategy(),
            "weighted_random" or "weighted" => new WeightedRandomStrategy(),
            "round_robin" or "roundrobin" => new RoundRobinStrategy(),
            "consistent_hash" or "hash" => new ConsistentHashStrategy(),
            "least_connections" or "load" => new LoadBasedStrategy(),
            _ => new LoadBasedStrategy()
        });
    }
}

internal static class SelectionCandidates
{
    public static IEnumerable<ServerRegistration> Online(IEnumerable<ServerRegistration> candidates) =>
        candidates.Where(s =>
            string.Equals(s.Status, "online", StringComparison.OrdinalIgnoreCase) &&
            s.Capacity > 0 &&
            s.PlayerCount < s.Capacity);

    public static int NormalizeLimit(int limit, int count) => Math.Clamp(limit <= 0 ? 1 : limit, 0, count);
}
