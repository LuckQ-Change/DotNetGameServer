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

public static class ServerSelectionStrategyFactory
{
    public static IServerSelectionStrategy Create(string? strategy)
    {
        return (strategy ?? "load").ToLowerInvariant() switch
        {
            "random" => new RandomStrategy(),
            "round_robin" => new RoundRobinStrategy(),
            _ => new LoadBasedStrategy(),
        };
    }
}
