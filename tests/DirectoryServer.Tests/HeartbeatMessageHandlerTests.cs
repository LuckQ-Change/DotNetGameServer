using System.Threading.Tasks;
using DirectoryServer.Components;
using DirectoryServer.Handlers;
using DotNetGameServer;
using Shared;
using Xunit;

namespace DirectoryServer.Tests;

public class HeartbeatMessageHandlerTests
{
    [Fact]
    public async Task Heartbeat_Rejected_When_Token_Invalid()
    {
        var reg = new InMemoryServerRegistryComponent();
        await reg.InitializeAsync();
        var metrics = new DirectoryMetricsComponent(_ => { });
        await metrics.InitializeAsync();
        try
        {
            var ctx = new TestServerContext();
            ctx.AddComponent<IServerRegistryComponent>(reg);
            ctx.AddComponent(metrics);
            ctx.AccessToken = "invalid-token";

            var handler = new HeartbeatMessageHandler();
            var resp = await handler.HandleAsync(new HeartbeatRequest
            {
                Id = "logic-1",
                Type = "logic",
                Region = "cn-sh",
                Host = "127.0.0.1",
                Port = 5502,
                PlayerCount = 0,
                Capacity = 100,
                Status = "online",
                AvgRttMs = 25
            }, ctx);

            Assert.NotNull(resp);
            Assert.False(resp!.Accepted);
        }
        finally
        {
            await metrics.DestroyAsync();
            await reg.DestroyAsync();
        }
    }

    [Fact]
    public async Task Heartbeat_Accepted_With_Valid_Token_And_Registry()
    {
        var reg = new InMemoryServerRegistryComponent();
        await reg.InitializeAsync();
        var metrics = new DirectoryMetricsComponent(_ => { });
        await metrics.InitializeAsync();
        try
        {
            var ctx = new TestServerContext();
            ctx.AddComponent<IServerRegistryComponent>(reg);
            ctx.AddComponent(metrics);

            // Generate heartbeat token using default secret fallback "dev-secret"
            var token = TokenSigner.ComposeHeartbeatToken("logic-1", "dev-secret", ttlSeconds: 60);
            ctx.AccessToken = token;

            var handler = new HeartbeatMessageHandler();
            var resp = await handler.HandleAsync(new HeartbeatRequest
            {
                Id = "logic-1",
                Type = "logic",
                Region = "cn-sh",
                Host = "127.0.0.1",
                Port = 5510,
                PlayerCount = 3,
                Capacity = 100,
                Status = "online",
                AvgRttMs = 18
            }, ctx);

            Assert.NotNull(resp);
            Assert.True(resp!.Accepted);
            var s = reg.GetServer("logic-1");
            Assert.NotNull(s);
            Assert.Equal("online", s!.Status);
            Assert.Equal(5510, s.Port);
        }
        finally
        {
            await metrics.DestroyAsync();
            await reg.DestroyAsync();
        }
    }
}