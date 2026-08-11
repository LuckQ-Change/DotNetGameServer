using Core.Directory;

using Xunit;

namespace Tests;

public sealed class ServerSelectionStrategyTests
{
    [Fact]
    public void LoadStrategy_PrefersLowerLoadAndExcludesUnavailableServers()
    {
        var candidates = CreateCandidates();
        var result = new LoadBasedStrategy().Select(candidates, 10);

        Assert.Equal(new[] { "b", "a" }, result.Select(s => s.ServerId).ToArray());
    }

    [Fact]
    public void RoundRobin_DoesNotDuplicateWhenLimitExceedsServerCount()
    {
        var candidates = CreateCandidates().Where(s => s.Status == "online" && s.PlayerCount < s.Capacity).ToList();
        var strategy = new RoundRobinStrategy();

        var result = strategy.Select(candidates, 10);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result.Select(s => s.ServerId).Distinct().Count());
    }

    [Fact]
    public void ConsistentHash_ReturnsStableServerForSameRoutingKey()
    {
        var candidates = CreateCandidates();
        var strategy = new ConsistentHashStrategy();

        var first = strategy.Select(candidates, 1, "user-42").Single().ServerId;
        var second = strategy.Select(candidates, 1, "user-42").Single().ServerId;

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("weighted")]
    [InlineData("weighted_random")]
    [InlineData("hash")]
    [InlineData("consistent_hash")]
    [InlineData("least_connections")]
    public void Factory_ProvidesSupportedStrategies(string name)
    {
        Assert.NotNull(ServerSelectionStrategyFactory.Create(name));
    }

    private static List<ServerRegistration> CreateCandidates() =>
    [
        new() { ServerId = "a", Status = "online", PlayerCount = 50, Capacity = 100, LastHeartbeatUnix = 10 },
        new() { ServerId = "b", Status = "online", PlayerCount = 10, Capacity = 100, LastHeartbeatUnix = 10 },
        new() { ServerId = "full", Status = "online", PlayerCount = 100, Capacity = 100, LastHeartbeatUnix = 10 },
        new() { ServerId = "offline", Status = "offline", PlayerCount = 0, Capacity = 100, LastHeartbeatUnix = 10 }
    ];
}
