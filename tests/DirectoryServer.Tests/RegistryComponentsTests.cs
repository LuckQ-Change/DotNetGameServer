using System;
using System.Linq;
using System.Threading.Tasks;
using DirectoryServer.Components;
using Xunit;

namespace DirectoryServer.Tests;

public class RegistryComponentsTests
{
    [Fact]
    public async Task InMemoryRegistry_Heartbeat_And_Query_Works()
    {
        var reg = new InMemoryServerRegistryComponent();
        await reg.InitializeAsync();
        try
        {
            reg.UpsertHeartbeat(
                id: "logic-1",
                type: "logic",
                region: "cn-sh",
                host: "127.0.0.1",
                port: 5502,
                playerCount: 10,
                capacity: 100,
                status: "online",
                avgRttMs: 30);

            var s = reg.GetServer("logic-1");
            Assert.NotNull(s);
            Assert.Equal("online", s!.Status);
            Assert.Equal(5502, s.Port);

            var list = reg.ListServers().ToList();
            Assert.Contains(list, x => x.Id == "logic-1");

            var token = reg.IssueSwitchToken("user-1", s!, TimeSpan.FromMinutes(1));
            Assert.False(string.IsNullOrWhiteSpace(token.Token));
            Assert.True(token.ExpireAtUtc > DateTime.UtcNow);
        }
        finally
        {
            await reg.DestroyAsync();
        }
    }

    [Fact]
    public async Task RedisRegistry_Initialize_And_BasicFlow_Works_IfRedisAvailable()
    {
        var conn = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";
        var reg = new RedisServerRegistryComponent(conn);
        try
        {
            await reg.InitializeAsync();
        }
        catch
        {
            // If redis is not available locally, skip the test gracefully
            return; // treated as passed but effectively skipped
        }

        try
        {
            reg.UpsertHeartbeat(
                id: "logic-redis-1",
                type: "logic",
                region: "cn-sh",
                host: "127.0.0.1",
                port: 5512,
                playerCount: 5,
                capacity: 50,
                status: "online",
                avgRttMs: 25);

            var s = reg.GetServer("logic-redis-1");
            Assert.NotNull(s);
            Assert.Equal("online", s!.Status);
            Assert.Equal(5512, s.Port);

            var list = reg.ListServers().ToList();
            Assert.Contains(list, x => x.Id == "logic-redis-1");

            var token = reg.IssueSwitchToken("user-2", s!, TimeSpan.FromMinutes(1));
            Assert.False(string.IsNullOrWhiteSpace(token.Token));
            Assert.True(token.ExpireAtUtc > DateTime.UtcNow);
        }
        finally
        {
            await reg.DestroyAsync();
        }
    }
}