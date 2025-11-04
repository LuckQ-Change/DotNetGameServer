using System;
using System.Threading.Tasks;
using DirectoryServer.Components;
using DirectoryServer.Handlers;
using DotNetGameServer;
using Shared;
using Xunit;

namespace DirectoryServer.Tests;

public class SwitchAndVerifyHandlerTests
{
    [Fact]
    public async Task Switch_Then_Verify_Succeeds()
    {
        var reg = new InMemoryServerRegistryComponent();
        await reg.InitializeAsync();
        var metrics = new DirectoryMetricsComponent(message => { /* no-op */ });
        await metrics.InitializeAsync();
        try
        {
            // Seed an online server
            reg.UpsertHeartbeat(
                id: "logic-1",
                type: "logic",
                region: "cn-sh",
                host: "127.0.0.1",
                port: 5502,
                playerCount: 5,
                capacity: 100,
                status: "online",
                avgRttMs: 20);

            // Prepare context
            var ctx = new TestServerContext();
            ctx.AddComponent<IServerRegistryComponent>(reg);
            ctx.AddComponent(metrics);

            // Switch handler should issue signed token
            var switchHandler = new SwitchServerMessageHandler();
            var switchResp = await switchHandler.HandleAsync(new SwitchServerRequest
            {
                UserId = "user-42",
                ServerId = "logic-1"
            }, ctx);

            Assert.NotNull(switchResp);
            Assert.True(switchResp!.Success);
            Assert.Equal(5502, switchResp.Port);
            Assert.False(string.IsNullOrWhiteSpace(switchResp.Token));

            // Verify handler should accept the signed token, not consuming
            var verifyHandler = new VerifySwitchTokenMessageHandler();
            var verifyResp = await verifyHandler.HandleAsync(new VerifySwitchTokenRequest
            {
                UserId = "user-42",
                ServerId = "logic-1",
                Token = switchResp.Token,
                Consume = false
            }, ctx);

            Assert.NotNull(verifyResp);
            Assert.True(verifyResp!.Valid);
        }
        finally
        {
            await metrics.DestroyAsync();
            await reg.DestroyAsync();
        }
    }

    [Fact]
    public async Task Switch_Fails_When_Server_Offline()
    {
        var reg = new InMemoryServerRegistryComponent();
        await reg.InitializeAsync();
        var metrics = new DirectoryMetricsComponent(message => { });
        await metrics.InitializeAsync();
        try
        {
            // Offline server: mark heartbeat long ago to flip status
            reg.UpsertHeartbeat(
                id: "logic-off",
                type: "logic",
                region: "cn-sh",
                host: "127.0.0.1",
                port: 5503,
                playerCount: 0,
                capacity: 100,
                status: "offline",
                avgRttMs: 0);

            var ctx = new TestServerContext();
            ctx.AddComponent<IServerRegistryComponent>(reg);
            ctx.AddComponent(metrics);

            var switchHandler = new SwitchServerMessageHandler();
            var resp = await switchHandler.HandleAsync(new SwitchServerRequest
            {
                UserId = "user-x",
                ServerId = "logic-off"
            }, ctx);

            Assert.NotNull(resp);
            Assert.False(resp!.Success);
            Assert.Contains("不在线", resp.Message ?? string.Empty);
        }
        finally
        {
            await metrics.DestroyAsync();
            await reg.DestroyAsync();
        }
    }
}