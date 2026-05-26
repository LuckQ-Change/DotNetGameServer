using System.Collections.Concurrent;

namespace Core.Directory;

public interface IServerSelectionStrategy
{
    List<ServerRegistration> Select(List<ServerRegistration> candidates, int limit);
}

public class LoadBasedStrategy : IServerSelectionStrategy
{
    public List<ServerRegistration> Select(List<ServerRegistration> candidates, int limit)
    {
        return candidates
            .Where(s => s.Status == "online")
            .OrderBy(s => s.Load)
            .ThenByDescending(s => s.LastHeartbeatUnix)
            .Take(limit)
            .ToList();
    }
}

public class RandomStrategy : IServerSelectionStrategy
{
    private readonly Random _rng = new();

    public List<ServerRegistration> Select(List<ServerRegistration> candidates, int limit)
    {
        return candidates
            .Where(s => s.Status == "online")
            .OrderBy(_ => _rng.Next())
            .Take(limit)
            .ToList();
    }
}

public class RoundRobinStrategy : IServerSelectionStrategy
{
    private long _counter;

    public List<ServerRegistration> Select(List<ServerRegistration> candidates, int limit)
    {
        var online = candidates.Where(s => s.Status == "online").ToList();
        if (online.Count == 0) return online;
        var offset = (int)(Interlocked.Increment(ref _counter) % online.Count);
        return [.. online.Skip(offset).Take(limit).Concat(online.Take(Math.Max(0, limit - (online.Count - offset))))];
    }
}

/// <summary>
/// 服务器选择策略工厂。
/// 使用 <see cref="ConcurrentDictionary"/> 缓存策略实例为单例，
/// 避免每次请求重新创建导致的 RoundRobin 计数器被重置。
/// </summary>
public static class ServerSelectionStrategyFactory
{
    /// <summary>
    /// 策略实例缓存，按策略名重用，保证 RoundRobin 跨请求轮询。
    /// </summary>
    private static readonly ConcurrentDictionary<string, IServerSelectionStrategy> _strategyCache = new();

    public static IServerSelectionStrategy Create(string? strategy)
    {
        var strategyName = (strategy ?? "load").ToLowerInvariant();
        return _strategyCache.GetOrAdd(strategyName, name => name switch
        {
            "random" => new RandomStrategy(),
            "round_robin" => new RoundRobinStrategy(),
            _ => new LoadBasedStrategy(),
        });
    }
}
